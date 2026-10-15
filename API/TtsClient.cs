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

        // Only one line plays at a time: a newer reply replaces the current one.
        private AudioClip _currentClip;
        private Coroutine _routine;
        private UnityWebRequest _pendingRequest;

        public TtsClient(DialogBehaviour behaviour)
        {
            _behaviour = behaviour;

            _audioSource = behaviour.gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f; // 2D: the voice is heard at full volume wherever the player stands.
            PlatformSettings();
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

        /// <summary>Stops playback, aborts an in-flight request and drops the pending coroutine.</summary>
        public void CancelPending()
        {
            if (_routine != null)
            {
                _behaviour.StopCoroutine(_routine);
                _routine = null;
            }

            if (_pendingRequest != null)
            {
                // Stopping the coroutine skips its own Dispose(), so the in-flight request is
                // aborted and released here — otherwise the handler would leak on every cut-off line.
                try { _pendingRequest.Abort(); } catch (Exception) { /* already finished */ }
                try { _pendingRequest.Dispose(); } catch (Exception) { }
                _pendingRequest = null;
            }

            if (_audioSource != null && _audioSource.isPlaying)
                _audioSource.Stop();
        }

        /// <summary>Releases the AudioSource clip and aborts any work in progress.</summary>
        public void Dispose()
        {
            CancelPending();

            if (_currentClip != null)
            {
                UnityEngine.Object.Destroy(_currentClip);
                _currentClip = null;
            }
        }

        private void PlatformSettings()
        {
            // Spatial audio is pointless for a single 2D voice and costs a bit of CPU.
            _audioSource.rolloffMode = AudioRolloffMode.Linear;
            _audioSource.priority = 0;
        }

        private IEnumerator SpeakRoutine(string text, string voice, string characterName, Action onReady)
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
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            // 429 and 5xx are retried (see TtsErrorLog.IsRetryable); anything else is reported at
            // once, because retrying a 4xx can only waste the player's time.
            for (int attempt = 1; attempt <= TtsErrorLog.MaxAttempts; attempt++)
            {
                var request = BuildRequest(bodyBytes);
                _pendingRequest = request;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // responseCode is 0 when the request never reached the server at all.
                    long status = request.responseCode;
                    string err = request.error;
                    string errBody = request.downloadHandler?.text ?? "";
                    float retryAfter = ReadRetryAfterSeconds(request);

                    _pendingRequest = null;
                    request.Dispose();

                    TtsError failure = TtsErrorLog.Describe(status, errBody, attempt);
                    Plugin.LogError($"TTS error: {failure.Title} | {err} | attempt {attempt}/{TtsErrorLog.MaxAttempts} | Body: {errBody}");

                    if (failure.Retryable && attempt < TtsErrorLog.MaxAttempts)
                    {
                        float delay = TtsErrorLog.RetryDelaySeconds(attempt, retryAfter);
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
                    onReady?.Invoke();
                    _routine = null;
                    yield break;
                }

                // The endpoint answered: whatever failed before is no longer a repeat failure.
                TtsCircuitBreaker.RecordSuccess();

                if (attempt > 1)
                    Plugin.LogInfo($"TTS: the request succeeded on attempt {attempt}.");

                byte[] audioBytes = request.downloadHandler.data;
                _pendingRequest = null;
                request.Dispose();

                if (audioBytes == null || audioBytes.Length == 0)
                {
                    ReportFailure("Empty audio response",
                        "fish.audio accepted the request but returned no audio data.",
                        "Retry the line; if it repeats, check the Model in CONFIG > [TTS].");
                    onReady?.Invoke();
                    _routine = null;
                    yield break;
                }

                // Unity cannot decode MP3 from an in-memory buffer, so the bytes are written to a
                // temporary file and loaded through the documented file-URL path.
                string tempPath = Path.Combine(Application.temporaryCachePath, "hlhn_tts_" + Sanitize(characterName) + "." + AUDIO_FORMAT);
                try
                {
                    File.WriteAllBytes(tempPath, audioBytes);
                }
                catch (Exception ex)
                {
                    ReportFailure("Could not write the audio file",
                        $"The temporary MP3 could not be written to disk ({ex.Message}).",
                        "Check free space and permissions on the game's temporary cache folder.");
                    onReady?.Invoke();
                    _routine = null;
                    yield break;
                }

                string fileUrl = "file:///" + tempPath.Replace('\\', '/');

                var clipRequest = UnityWebRequestMultimedia.GetAudioClip(fileUrl, AUDIO_TYPE);
                _pendingRequest = clipRequest;
                yield return clipRequest.SendWebRequest();

                if (clipRequest.result != UnityWebRequest.Result.Success)
                {
                    string decodeError = clipRequest.error;
                    _pendingRequest = null;
                    clipRequest.Dispose();
                    ReportFailure("Could not decode the audio",
                        $"The MP3 returned by fish.audio could not be decoded ({decodeError}).",
                        "Check that the API URL points at fish.audio > /v1/tts, then retry the line.");
                    onReady?.Invoke();
                    _routine = null;
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(clipRequest);
                _pendingRequest = null;
                clipRequest.Dispose();

                if (clip == null)
                {
                    ReportFailure("Decoded audio is empty",
                        "fish.audio returned audio that Unity could not turn into a playable clip.",
                        "Retry the line; if it repeats, check the Model in CONFIG > [TTS].");
                    onReady?.Invoke();
                    _routine = null;
                    yield break;
                }

                PlayClip(clip, onReady);
                _routine = null;
                yield break;
            }
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
        private void ReportFailure(string title, string meaning, string advice)
        {
            TtsError failure = TtsErrorLog.RecordLocal(title, meaning, advice);
            Plugin.LogError($"TTS error: {failure.Title} | {failure.Meaning}");
            _behaviour.Window?.SetStatus(TtsErrorLog.ToStatusLine(failure), true);
        }

        private void PlayClip(AudioClip clip, Action onReady)
        {
            if (_currentClip != null)
            {
                // Clips created by UnityWebRequestMultimedia are not garbage collected: the previous
                // one has to be destroyed explicitly or it leaks every time the NPC speaks.
                UnityEngine.Object.Destroy(_currentClip);
                _currentClip = null;
            }

            _currentClip = clip;
            _audioSource.volume = Mathf.Clamp01(_behaviour.TtsVolume);
            _audioSource.clip = clip;

            // Reveal the on-screen reply at the same instant the voice starts.
            onReady?.Invoke();
            _audioSource.Play();
            Plugin.LogDebug($"TTS: playing {clip.length:F1}s of audio for the reply.");
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
