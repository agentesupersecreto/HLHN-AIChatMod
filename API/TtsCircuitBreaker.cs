using System;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Stops sending requests to fish.audio once the same kind of fatal failure has repeated: a wrong
    /// key, an account out of credits or a bad voice id would otherwise cost one doomed request and
    /// one log entry per dialogue line, and would keep delaying the on-screen reply.
    ///
    /// Pure BCL: no Unity types, so the pause rule can be exercised outside the game.
    /// </summary>
    public static class TtsCircuitBreaker
    {
        /// <summary>Fatal failures tolerated in a row before TTS pauses.</summary>
        public const int MaxConsecutiveFailures = 3;

        /// <summary>Headline used for the pause entry in the visible TTS log.</summary>
        public const string PausedTitle = "TTS paused";

        /// <summary>What to do about a pause; shown next to the reason.</summary>
        public const string PausedAdvice =
            "Fix the cause of the failure above, then press [Retry now] - or re-apply the settings in CONFIG > [TTS].";

        /// <summary>Fatal failures counted so far; reset by a success or by resuming.</summary>
        public static int ConsecutiveFailures { get; private set; }

        /// <summary>True while requests are withheld.</summary>
        public static bool IsPaused => ConsecutiveFailures >= MaxConsecutiveFailures;

        /// <summary>Title and meaning of the failure that paused TTS.</summary>
        public static string Reason { get; private set; } = "";

        /// <summary>When TTS paused, for the log line; default while running.</summary>
        public static DateTime PausedAt { get; private set; }

        /// <summary>
        /// Fatal = the endpoint answered with a status no retry can fix (any 4xx other than 429).
        /// A failure with no HTTP status (misconfiguration, disk or decode error) never reaches the
        /// endpoint, so it is not counted here - it is reported in the log instead.
        /// </summary>
        public static bool IsFatal(TtsError error)
        {
            return error != null && error.Status >= 400 && error.Status <= 599 && !error.Retryable;
        }

        /// <summary>
        /// Counts a failure and pauses TTS when the limit is reached.
        /// </summary>
        /// <returns>True only for the failure that paused TTS, so the caller can log it once.</returns>
        public static bool RecordFailure(TtsError error)
        {
            if (IsPaused || !IsFatal(error)) return false;

            ConsecutiveFailures++;
            Reason = $"{error.Title} - {error.Meaning}";

            if (!IsPaused) return false;

            PausedAt = error.Time;
            return true;
        }

        /// <summary>A reply that went through proves the endpoint works again, so the count restarts.</summary>
        public static void RecordSuccess()
        {
            ConsecutiveFailures = 0;
            Reason = "";
            PausedAt = default;
        }

        /// <summary>Resumes after a pause: used by [Retry now] and whenever the settings are re-applied.</summary>
        public static void Resume()
        {
            ConsecutiveFailures = 0;
            Reason = "";
            PausedAt = default;
        }

        /// <summary>Meaning shown for the pause entry in the TTS log.</summary>
        public static string PausedMeaning =>
            $"{MaxConsecutiveFailures} requests in a row were rejected with {Reason}, so every dialogue line would keep failing the same way. TTS is not sending requests until this is fixed.";

        /// <summary>One-line notice for the transient status bar while TTS is paused.</summary>
        public static string StatusLine =>
            $"[TTS] Paused: {Reason} Fix it in CONFIG > [TTS], then press [Retry now].";
    }
}
