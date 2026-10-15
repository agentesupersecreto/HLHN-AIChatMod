using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Text-to-speech for the NPC replies, backed by an external endpoint (fish.audio).
    /// The reply is POSTed as JSON and the endpoint answers with raw audio bytes, which are
    /// decoded and played through an <see cref="AudioSource"/>. The component is attached to the
    /// persistent mod GameObject so playback keeps working across scene loads.
    /// </summary>
    public class TtsClient
    {
        /// <summary>Audio container requested from the endpoint; must match the AudioType used to decode.</summary>
        private const string AUDIO_FORMAT = "mp3";
        private const AudioType AUDIO_TYPE = AudioType.MPEG;

        private readonly DialogBehaviour _behaviour;
        private readonly AudioSource _audioSource;

        /// <summary>
        /// Audio of the game's own lines that has already been paid for: a line said twice is replayed
        /// from disk instead of being requested again. See <see cref="TtsAudioCache"/>.
        /// </summary>
        private readonly TtsAudioCache _cache;

        /// <summary>The on-disk cache, exposed so the TTS tab can show its size and clear it.</summary>
        public TtsAudioCache Cache => _cache;

        /// <summary>
        /// The game's own dialogue runs on its own channel: a bubble can appear while the AI reply is
        /// still being generated, and cutting that request off would lose the reply.
        /// </summary>
        private readonly AudioSource _nativeAudioSource;

        // Only one line plays at a time per channel: a newer line replaces the current one.
        private AudioClip _currentClip;
        private Coroutine _routine;
        private UnityWebRequest _pendingRequest;

        private AudioClip _nativeClip;
        private Coroutine _nativeRoutine;
        private UnityWebRequest _nativeRequest;

        // Whether the last playback of each channel actually reached the AudioSource. Only audio Unity
        // has decoded once is cached: an endpoint answering with something that is not audio must not
        // be stored, or the bad file would be replayed on every later line.
        private bool _playbackOk;
        private bool _nativePlaybackOk;

        public TtsClient(DialogBehaviour behaviour)
        {
            _behaviour = behaviour;
            _cache = new TtsAudioCache(Plugin.GetDataDir(Plugin.TtsCacheDirName));

            _audioSource = behaviour.gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f; // 2D: the voice is heard at full volume wherever the player stands.
            PlatformSettings(_audioSource);

            _nativeAudioSource = behaviour.gameObject.AddComponent<AudioSource>();
            _nativeAudioSource.playOnAwake = false;
            _nativeAudioSource.loop = false;
            _nativeAudioSource.spatialBlend = 0f;
            PlatformSettings(_nativeAudioSource);
        }

        /// <summary>
        /// Requests speech for <paramref name="text"/> with the voice configured for
        /// <paramref name="characterName"/> and plays it. Any line already playing is cut off,
        /// so the audio stays in sync with the latest on-screen dialogue.
        ///
        /// <paramref name="onReady"/> runs the instant the audio is ready — just before playback
        /// starts — so the caller can reveal the on-screen reply at the very same moment as the
        /// voice. It is also invoked (immediately, on the calling frame) whenever speech cannot be
        /// produced, so the reply is never withheld when TTS is off, misconfigured, or fails.
        ///
        /// A 429 or 5xx answer is retried with exponential backoff before the reply is released
        /// (see <see cref="TtsErrorLog"/>); every failure is reported both in the BepInEx log and in
        /// the visible TTS error log.
        ///
        /// While <see cref="TtsCircuitBreaker"/> is paused no request is sent at all - the reply is
        /// still released, so the line shows up (without voice) instead of hanging.
        /// </summary>
        public void Speak(string text, string characterName, Action onReady)
        {
            if (!_behaviour.TtsEnabled) { onReady?.Invoke(); return; }
            if (string.IsNullOrWhiteSpace(text)) { onReady?.Invoke(); return; }

            // Repeated fatal failures (wrong key, no credits, bad voice id) would fail for every
            // line: stop calling the endpoint and tell the player instead of retrying forever.
            if (TtsCircuitBreaker.IsPaused)
            {
                _behaviour.Window?.SetStatus(TtsCircuitBreaker.StatusLine, true);
                onReady?.Invoke();
                return;
            }

            if (string.IsNullOrWhiteSpace(_behaviour.TtsApiUrl))
            {
                ReportFailure("No API URL configured",
                    "TTS is enabled but the API URL field is empty.",
                    "Set it to https://api.fish.audio/v1/tts in CONFIG > [TTS].");
                onReady?.Invoke();
                return;
            }

            if (string.IsNullOrWhiteSpace(_behaviour.TtsApiKey))
            {
                ReportFailure("No API key configured",
                    "TTS is enabled but no fish.audio API key is set.",
                    "Get a key at https://fish.audio/app/api-keys and paste it in CONFIG > [TTS].");
                onReady?.Invoke();
                return;
            }

            string voice = _behaviour.GetTtsVoice(characterName);
            if (string.IsNullOrWhiteSpace(voice))
            {
                ReportFailure("No voice for " + (string.IsNullOrEmpty(characterName) ? "this NPC" : characterName),
                    "The character has no voice reference_id and the Default NPC Voice is empty too.",
                    "Fill in the character's voice - or the Default NPC Voice - in CONFIG > [TTS].");
                onReady?.Invoke();
                return;
            }

            CancelPending();
            _routine = _behaviour.StartCoroutine(SpeakRoutine(text.Trim(), voice, characterName, onReady));
        }

        /// <summary>
        /// Speaks one of the game's own dialogue lines (see
        /// <see cref="Game.NativeDialogueTts"/>), on the native channel so it never cancels an AI
        /// reply that is still being generated.
        ///
        /// Unlike <see cref="Speak"/>, a native line that cannot be voiced is dropped quietly: the
        /// game speaks constantly, and a missing voice or an empty field would otherwise fill the TTS
        /// error log with the same entry every few seconds.
        /// </summary>
        public void SpeakNative(string text, string characterName)
        {
            if (!_behaviour.TtsEnabled) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            // A paused circuit breaker is handled once the line turns out not to be cached (see
            // SpeakRoutine): a line already on disk is free and still plays while TTS is paused.

            if (string.IsNullOrWhiteSpace(_behaviour.TtsApiUrl) || string.IsNullOrWhiteSpace(_behaviour.TtsApiKey))
                return;

            string voice = _behaviour.GetTtsVoice(characterName);
            if (string.IsNullOrWhiteSpace(voice))
            {
                Plugin.LogDebug($"[NativeTts] No voice for '{characterName}': line not voiced.");
                return;
            }

            CancelNative();
            _nativeRoutine = _behaviour.StartCoroutine(
                SpeakRoutine(text.Trim(), voice, characterName, null, native: true));
        }

        /// <summary>Stops playback, aborts an in-flight request and drops the pending coroutine.</summary>
        public void CancelPending() => CancelChannel(native: false);

        /// <summary>Cuts off the native game-dialogue channel only.</summary>
        public void CancelNative() => CancelChannel(native: true);

        private void CancelChannel(bool native)
        {
            Coroutine routine = native ? _nativeRoutine : _routine;
            UnityWebRequest request = native ? _nativeRequest : _pendingRequest;
            AudioSource source = native ? _nativeAudioSource : _audioSource;

            if (routine != null)
            {
                _behaviour.StopCoroutine(routine);
                if (native) _nativeRoutine = null; else _routine = null;
            }

            if (request != null)
            {
                // Stopping the coroutine skips its own Dispose(), so the in-flight request is
                // aborted and released here — otherwise the handler would leak on every cut-off line.
                try { request.Abort(); } catch (Exception) { /* already finished */ }
                try { request.Dispose(); } catch (Exception) { }
                if (native) _nativeRequest = null; else _pendingRequest = null;
            }

            if (source != null && source.isPlaying)
                source.Stop();
        }

        /// <summary>Releases the AudioSource clips and aborts any work in progress.</summary>
        public void Dispose()
        {
            CancelPending();
            CancelNative();

            if (_currentClip != null)
            {
                UnityEngine.Object.Destroy(_currentClip);
                _currentClip = null;
            }

            if (_nativeClip != null)
            {
                UnityEngine.Object.Destroy(_nativeClip);
                _nativeClip = null;
            }
        }

        private static void PlatformSettings(AudioSource source)
        {
            // Spatial audio is pointless for a single 2D voice and costs a bit of CPU.
            source.rolloffMode = AudioRolloffMode.Linear;
            source.priority = 0;
        }

        /// <summary>
        /// Requests the audio for one line and plays it.
        ///
        /// <paramref name="native"/> selects the channel: the AI reply (<c>false</c>) or one of the
        /// game's own dialogue lines (<c>true</c>). Each channel has its own request, clip and
        /// AudioSource, so the two can be in flight at the same time without cancelling each other -
        /// the game speaks while the AI reply is still being generated. Only the AI channel reports
        /// failures in the TTS tab: the game's dialogue is constant, and a misconfigured voice would
        /// otherwise bury the log.
        /// </summary>
        private IEnumerator SpeakRoutine(string text, string voice, string characterName, Action onReady, bool native = false)
        {
            // The game's own lines repeat, so they are cached on disk: a line already paid for is
            // replayed from there in the same frame, without a request or a second charge. An AI
            // reply is a new sentence nearly every time, so it always goes to the endpoint.
            byte[] audioBytes = null;
            string cacheKey = "";

            if (native && _behaviour.TtsNativeCache && _cache.Enabled)
            {
                // The key mirrors what is actually sent: the tuning values are normalized exactly as
                // the request body normalizes them, so two spellings of the same setting share one
                // entry instead of storing the same audio twice.
                cacheKey = TtsAudioCache.Key(
                    text,
                    voice,
                    _behaviour.TtsApiUrl?.Trim() ?? "",
                    _behaviour.TtsModel?.Trim() ?? "",
                    TtsRequestBody.NormalizeLatency(_behaviour.TtsLatency),
                    TtsRequestBody.ClampSpeed(_behaviour.TtsSpeed),
                    _behaviour.TtsNormalizeLoudness,
                    _behaviour.TtsNormalize);

                if (_cache.TryRead(cacheKey, out audioBytes))
                    Plugin.LogDebug($"[NativeTts] cached line ({audioBytes.Length / 1024} KB) - no request sent.");
                else
                {
                    audioBytes = null;
                    Plugin.LogDebug("[NativeTts] line not cached yet - asking the endpoint.");
                }
            }

            // A paused circuit breaker stops new requests, not the lines already on disk.
            if (native && audioBytes == null && TtsCircuitBreaker.IsPaused)
            {
                Plugin.LogDebug("[NativeTts] TTS is paused - the line stays silent.");
                EndChannel(native);
                yield break;
            }

            // Bodies are only built when there is something to ask for.
            byte[] bodyBytes = null;
            if (audioBytes == null)
            {
                // fish.audio: {text, reference_id, format} plus the documented tuning parameters that
                // differ from the endpoint defaults (latency, prosody.speed, normalization).
                string body = TtsRequestBody.Build(
                    text,
                    voice,
                    AUDIO_FORMAT,
                    _behaviour.TtsLatency,
                    _behaviour.TtsSpeed,
                    _behaviour.TtsNormalizeLoudness,
                    _behaviour.TtsNormalize);
                bodyBytes = Encoding.UTF8.GetBytes(body);
            }

            // 429 and 5xx are retried (see TtsErrorLog.IsRetryable); anything else is reported at
            // once, because retrying a 4xx can only waste the player's time. A cached line skips the
            // loop entirely.
            for (int attempt = 1; audioBytes == null && attempt <= TtsErrorLog.MaxAttempts; attempt++)
            {
                var request = BuildRequest(bodyBytes);
                if (native) _nativeRequest = request; else _pendingRequest = request;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // responseCode is 0 when the request never reached the server at all.
                    long status = request.responseCode;
                    string err = request.error;
                    string errBody = request.downloadHandler?.text ?? "";
                    float retryAfter = ReadRetryAfterSeconds(request);

                    if (native) _nativeRequest = null; else _pendingRequest = null;
                    request.Dispose();

                    TtsError failure = TtsErrorLog.Describe(status, errBody, attempt);
                    Plugin.LogError($"TTS error: {failure.Title} | {err} | attempt {attempt}/{TtsErrorLog.MaxAttempts} | Body: {errBody}");

                    if (failure.Retryable && attempt < TtsErrorLog.MaxAttempts)
                    {
                        float delay = TtsErrorLog.RetryDelaySeconds(attempt, retryAfter);
                        if (!native)
                            _behaviour.Window?.SetStatus(
                                $"[TTS] {failure.Title} - retrying in {delay:0.0}s (attempt {attempt + 1}/{TtsErrorLog.MaxAttempts})...", true);
                        yield return new WaitForSecondsRealtime(delay);
                        continue;
                    }

                    // Counted before it is stored: a fatal failure repeated to the limit pauses TTS,
                    // which is recorded as its own entry so the reason stays visible in the tab.
                    bool pausedNow = TtsCircuitBreaker.RecordFailure(failure);

                    // Out of attempts (or not retryable): keep the failure in the TTS tab log, tell
                    // the player, and release the reply. A broken voice must never swallow a line.
                    // A native line is only logged: the game speaks constantly and it must stay quiet.
                    if (!native)
                    {
                        TtsErrorLog.Record(failure);
                        if (pausedNow)
                        {
                            Plugin.LogWarning(
                                $"TTS paused after {TtsCircuitBreaker.ConsecutiveFailures} consecutive failures: {TtsCircuitBreaker.Reason}");
                            TtsErrorLog.RecordLocal(
                                TtsCircuitBreaker.PausedTitle,
                                TtsCircuitBreaker.PausedMeaning,
                                TtsCircuitBreaker.PausedAdvice);
                        }

                        _behaviour.Window?.SetStatus(TtsErrorLog.ToStatusLine(failure), true);
                    }

                    onReady?.Invoke();
                    EndChannel(native);
                    yield break;
                }

                // The endpoint answered: whatever failed before is no longer a repeat failure.
                TtsCircuitBreaker.RecordSuccess();

                if (attempt > 1)
                    Plugin.LogInfo($"TTS: the request succeeded on attempt {attempt}.");

                byte[] downloaded = request.downloadHandler.data;
                if (native) _nativeRequest = null; else _pendingRequest = null;
                request.Dispose();

                if (downloaded == null || downloaded.Length == 0)
                {
                    ReportFailure("Empty audio response",
                        "fish.audio accepted the request but returned no audio data.",
                        "Retry the line; if it repeats, check the Model in CONFIG > [TTS].",
                        native);
                    onReady?.Invoke();
                    EndChannel(native);
                    yield break;
                }

                // The audio is in hand: playback happens below, shared with the cached path.
                audioBytes = downloaded;
                break;
            }

            // Unreachable while MaxAttempts is at least 1 - a failed attempt ends the routine - but the
            // loop must never fall through to playback without audio.
            if (audioBytes == null)
            {
                EndChannel(native);
                yield break;
            }

            yield return PlayAudioRoutine(audioBytes, characterName, onReady, native);

            // Keep the line for the next time it is said (the game's own lines only: an AI reply is
            // nearly always a new sentence, and would evict the lines that are worth keeping). Only
            // audio that played is stored.
            bool played = native ? _nativePlaybackOk : _playbackOk;
            if (played && cacheKey.Length > 0 && _cache.Store(cacheKey, audioBytes))
                Plugin.LogDebug($"[NativeTts] line cached ({audioBytes.Length / 1024} KB).");

            EndChannel(native);
        }

        /// <summary>
        /// Plays one line's audio: the bytes go to a temporary file, which is then decoded and played.
        /// Unity cannot decode MP3 from an in-memory buffer, so the documented file-URL path is used.
        ///
        /// Both sources of audio run through here - the endpoint's answer and a cached line - and the
        /// cached one is written to the temporary file rather than decoded in place, because an
        /// eviction may delete the stored file at any moment.
        /// </summary>
        private IEnumerator PlayAudioRoutine(byte[] audioBytes, string characterName, Action onReady, bool native)
        {
            if (native) _nativePlaybackOk = false; else _playbackOk = false;

            string tempPath = Path.Combine(Application.temporaryCachePath,
                (native ? "hlhn_tts_native_" : "hlhn_tts_") + Sanitize(characterName) + "." + AUDIO_FORMAT);
            try
            {
                File.WriteAllBytes(tempPath, audioBytes);
            }
            catch (Exception ex)
            {
                ReportFailure("Could not write the audio file",
                    $"The temporary MP3 could not be written to disk ({ex.Message}).",
                    "Check free space and permissions on the game's temporary cache folder.",
                    native);
                onReady?.Invoke();
                yield break;
            }

            string fileUrl = "file:///" + tempPath.Replace('\\', '/');

            var clipRequest = UnityWebRequestMultimedia.GetAudioClip(fileUrl, AUDIO_TYPE);
            if (native) _nativeRequest = clipRequest; else _pendingRequest = clipRequest;
            yield return clipRequest.SendWebRequest();

            if (clipRequest.result != UnityWebRequest.Result.Success)
            {
                string decodeError = clipRequest.error;
                if (native) _nativeRequest = null; else _pendingRequest = null;
                clipRequest.Dispose();
                ReportFailure("Could not decode the audio",
                    $"The MP3 returned by fish.audio could not be decoded ({decodeError}).",
                    "Check that the API URL points at fish.audio > /v1/tts, then retry the line.",
                    native);
                onReady?.Invoke();
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(clipRequest);
            if (native) _nativeRequest = null; else _pendingRequest = null;
            clipRequest.Dispose();

            if (clip == null)
            {
                ReportFailure("Decoded audio is empty",
                    "fish.audio returned audio that Unity could not turn into a playable clip.",
                    "Retry the line; if it repeats, check the Model in CONFIG > [TTS].",
                    native);
                onReady?.Invoke();
                yield break;
            }

            PlayClip(clip, onReady, native);
            if (native) _nativePlaybackOk = true; else _playbackOk = true;
        }

        /// <summary>Marks the channel's coroutine as finished (it is stopped from outside only).</summary>
        private void EndChannel(bool native)
        {
            if (native) _nativeRoutine = null;
            else _routine = null;
        }

        /// <summary>Builds the POST that asks fish.audio for the audio bytes of one reply.</summary>
        private UnityWebRequest BuildRequest(byte[] bodyBytes)
        {
            var request = new UnityWebRequest(_behaviour.TtsApiUrl, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + _behaviour.TtsApiKey.Trim());
            if (!string.IsNullOrWhiteSpace(_behaviour.TtsModel))
                request.SetRequestHeader("model", _behaviour.TtsModel.Trim());
            request.timeout = 60;
            return request;
        }

        /// <summary>
        /// Seconds from a Retry-After header (sent with 429/503), or 0 when it is absent or uses the
        /// HTTP-date form - in that case the exponential backoff decides the wait.
        /// </summary>
        private static float ReadRetryAfterSeconds(UnityWebRequest request)
        {
            string raw = request.GetResponseHeader("Retry-After");
            if (string.IsNullOrWhiteSpace(raw)) return 0f;

            return float.TryParse(raw.Trim(), out float seconds) ? Mathf.Max(0f, seconds) : 0f;
        }

        /// <summary>
        /// Records a failure that has no HTTP status (misconfiguration, empty answer, disk error,
        /// undecodable audio) in the same visible log, and mirrors it on the status line.
        /// </summary>
        private void ReportFailure(string title, string meaning, string advice, bool native = false)
        {
            if (native)
            {
                // The visible log is for the AI replies; a native line only leaves a debug trace.
                Plugin.LogDebug($"[NativeTts] {title}: {meaning}");
                return;
            }

            TtsError failure = TtsErrorLog.RecordLocal(title, meaning, advice);
            Plugin.LogError($"TTS error: {failure.Title} | {failure.Meaning}");
            _behaviour.Window?.SetStatus(TtsErrorLog.ToStatusLine(failure), true);
        }

        private void PlayClip(AudioClip clip, Action onReady, bool native = false)
        {
            AudioSource source = native ? _nativeAudioSource : _audioSource;
            AudioClip previous = native ? _nativeClip : _currentClip;

            if (previous != null)
            {
                // Clips created by UnityWebRequestMultimedia are not garbage collected: the previous
                // one has to be destroyed explicitly or it leaks every time the NPC speaks.
                UnityEngine.Object.Destroy(previous);
            }

            if (native) _nativeClip = clip; else _currentClip = clip;

            source.volume = Mathf.Clamp01(_behaviour.TtsVolume);
            source.clip = clip;

            // Reveal the on-screen reply at the same instant the voice starts.
            onReady?.Invoke();
            source.Play();
            Plugin.LogDebug(native
                ? $"[NativeTts] playing {clip.length:F1}s of game dialogue."
                : $"TTS: playing {clip.length:F1}s of audio for the reply.");
        }

        /// <summary>Keeps the temp file name safe for any character name (main NPC or random).</summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "npc";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
