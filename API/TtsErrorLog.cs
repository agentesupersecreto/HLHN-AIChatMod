using System;
using System.Collections.Generic;

namespace DialogInterceptorMod.API
{
    /// <summary>One failed TTS request, described the way the player needs to read it.</summary>
    public class TtsError
    {
        public DateTime Time;

        /// <summary>HTTP status code, or 0 when the request never got an answer (offline, timeout, wrong URL).</summary>
        public long Status;

        /// <summary>Short status name, used as the log headline (e.g. "401 Unauthorized").</summary>
        public string Title = "";

        /// <summary>What the status means (fish.audio status table).</summary>
        public string Meaning = "";

        /// <summary>What to do about it.</summary>
        public string Advice = "";

        /// <summary>True for 429 and 5xx: the only statuses worth retrying (fish.audio docs).</summary>
        public bool Retryable;

        /// <summary>Attempts made before giving up on this line.</summary>
        public int Attempts = 1;

        /// <summary>Identical consecutive failures collapsed into this entry (a missing key repeats on every line).</summary>
        public int RepeatCount = 1;

        /// <summary>Message sent by the endpoint, when it sent one (truncated); the "read the message" part of a 400.</summary>
        public string ServerMessage = "";
    }

    /// <summary>
    /// Turns fish.audio failures into something the player can act on and keeps the most recent ones
    /// so the TTS tab can show them. The status line is transient (a few seconds), this log is not.
    /// Pure BCL: no Unity types, so the mapping and the retry policy can be exercised outside the game.
    /// </summary>
    public static class TtsErrorLog
    {
        /// <summary>Attempts per reply: the first try plus two retries.</summary>
        public const int MaxAttempts = 3;

        /// <summary>Entries kept for the UI (newest first).</summary>
        public const int MaxEntries = 10;

        /// <summary>Base of the exponential backoff (0.75s, 1.5s, 3s, ...).</summary>
        private const double RETRY_BACKOFF_SECONDS = 0.75d;

        /// <summary>Cap on a retry delay, so an odd Retry-After cannot stall the dialogue for long.</summary>
        private const double MAX_RETRY_DELAY_SECONDS = 5d;

        /// <summary>Longest server message kept for display and for the log file.</summary>
        private const int MAX_SERVER_MESSAGE_CHARS = 200;

        private static readonly List<TtsError> _entries = new List<TtsError>();

        /// <summary>Most recent failure, or null while none has happened.</summary>
        public static TtsError Last { get; private set; }

        /// <summary>Bumped on every change so the UI can tell a new failure from the one it already drew.</summary>
        public static int Revision { get; private set; }

        /// <summary>Failures, newest first.</summary>
        public static IList<TtsError> Entries => _entries;

        public static int Count => _entries.Count;

        /// <summary>
        /// Only 429 and 5xx are retried: every other 4xx (bad parameters, bad key, no credits,
        /// forbidden, unknown model/voice) fails the same way until the request itself changes.
        /// </summary>
        public static bool IsRetryable(long status) => status == 429 || (status >= 500 && status <= 599);

        /// <summary>
        /// Exponential backoff for a retry, stretched when the server asks for a longer Retry-After
        /// and clamped to <see cref="MAX_RETRY_DELAY_SECONDS"/>.
        /// </summary>
        public static float RetryDelaySeconds(int attempt, float retryAfterSeconds)
        {
            if (attempt < 1) attempt = 1;

            double delay = RETRY_BACKOFF_SECONDS * Math.Pow(2d, attempt - 1);
            if (retryAfterSeconds > delay) delay = retryAfterSeconds;

            return (float)Math.Min(Math.Max(delay, RETRY_BACKOFF_SECONDS), MAX_RETRY_DELAY_SECONDS);
        }

        /// <summary>
        /// Maps a failed request to a described <see cref="TtsError"/>. <paramref name="status"/> 0
        /// means the request never reached fish.audio (offline, DNS, timeout, wrong API URL).
        /// </summary>
        public static TtsError Describe(long status, string body, int attempts)
        {
            var error = new TtsError
            {
                Time = DateTime.Now,
                Status = status,
                Attempts = Math.Max(1, attempts),
                Retryable = status > 0 && IsRetryable(status),
                ServerMessage = ExtractServerMessage(body)
            };

            switch (status)
            {
                case 0:
                    error.Title = "No response";
                    error.Meaning = "The request never got an answer from fish.audio (offline, DNS failure, timeout, or a wrong API URL).";
                    error.Advice = "Check your internet connection and the API URL in CONFIG > [TTS].";
                    break;
                case 400:
                    error.Title = "400 Bad request";
                    error.Meaning = "Invalid parameters, or a reference_id / voice that does not exist.";
                    error.Advice = "Fix the request and read the server message below; check the voice reference_id in CONFIG > [TTS].";
                    break;
                case 401:
                    error.Title = "401 Unauthorized";
                    error.Meaning = "Invalid or missing API key.";
                    error.Advice = "Get or check the key at https://fish.audio/app/api-keys and paste it in CONFIG > [TTS].";
                    break;
                case 402:
                    error.Title = "402 Insufficient credits";
                    error.Meaning = "Your fish.audio account is out of credits.";
                    error.Advice = "Top up at https://fish.audio/app/developers/billing/.";
                    break;
                case 403:
                    error.Title = "403 Forbidden";
                    error.Meaning = "Not permitted for this key or resource.";
                    error.Advice = "Check the key's scope and who owns the voice reference_id.";
                    break;
                case 404:
                    error.Title = "404 Not found";
                    error.Meaning = "Model or voice not found.";
                    error.Advice = "Verify the Model and the character's reference_id in CONFIG > [TTS].";
                    break;
                case 429:
                    error.Title = "429 Too many requests";
                    error.Meaning = "Rate or concurrency limit reached for your account.";
                    error.Advice = "Retried automatically with backoff; if it keeps failing, wait a moment or raise your concurrency limit.";
                    break;
                default:
                    if (status >= 500 && status <= 599)
                    {
                        error.Title = $"{status} fish.audio server error";
                        error.Meaning = "fish.audio failed on its side.";
                        error.Advice = "Retried automatically with backoff; if it persists, contact fish.audio support.";
                    }
                    else if (status > 0)
                    {
                        error.Title = $"{status} request rejected";
                        error.Meaning = "The endpoint refused the request.";
                        error.Advice = "Not retried: 4xx codes only change when the request does.";
                    }
                    else
                    {
                        error.Title = "Request failed";
                        error.Meaning = "The request could not be completed.";
                        error.Advice = "Check the API URL and the connection in CONFIG > [TTS].";
                    }
                    break;
            }

            return error;
        }

        /// <summary>
        /// Records a failure that has no HTTP status (empty audio, decode failure, disk error) so it
        /// shows up in the same visual log.
        /// </summary>
        public static TtsError RecordLocal(string title, string meaning, string advice)
        {
            var error = new TtsError
            {
                Time = DateTime.Now,
                Status = 0,
                Title = title,
                Meaning = meaning,
                Advice = advice,
                Retryable = false,
                Attempts = 1
            };

            Record(error);
            return error;
        }

        /// <summary>Adds a failure to the log (newest first, bounded, duplicates collapsed).</summary>
        public static void Record(TtsError error)
        {
            if (error == null) return;

            // A misconfiguration (missing key, no voice for the character) repeats on every single
            // reply, so consecutive identical failures collapse into one entry with a repeat count
            // instead of wiping the rest of the log.
            if (Last != null && Last.Status == error.Status &&
                Last.Title == error.Title && Last.Meaning == error.Meaning)
            {
                Last.RepeatCount++;
                Last.Time = error.Time;
                Last.Attempts = error.Attempts;
                Revision++;
                return;
            }

            error.RepeatCount = 1;
            _entries.Insert(0, error);
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(_entries.Count - 1);

            Last = error;
            Revision++;
        }

        public static void Clear()
        {
            _entries.Clear();
            Last = null;
            Revision++;
        }

        /// <summary>
        /// One-line summary for the transient status line; the meaning, the advice and the server
        /// message stay in the TTS tab, which is where the settings that caused the error live.
        /// </summary>
        public static string ToStatusLine(TtsError error)
        {
            if (error == null) return "[TTS] Unknown error.";
            return $"[TTS] {error.Title} - {error.Meaning} See CONFIG > [TTS] for details.";
        }

        /// <summary>Pulls the endpoint's own message out of an error body so a 400 can be acted on.</summary>
        private static string ExtractServerMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";

            string message = JsonHelper.ExtractJsonValue(body, "message");
            if (string.IsNullOrWhiteSpace(message)) message = JsonHelper.ExtractJsonValue(body, "detail");
            if (string.IsNullOrWhiteSpace(message)) message = JsonHelper.ExtractJsonValue(body, "error");
            if (string.IsNullOrWhiteSpace(message)) message = body;

            message = message.Replace("\r", " ").Replace("\n", " ").Trim();
            if (message.Length > MAX_SERVER_MESSAGE_CHARS)
                message = message.Substring(0, MAX_SERVER_MESSAGE_CHARS) + "...";

            return message;
        }
    }
}
