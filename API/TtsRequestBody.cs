using System;
using System.Globalization;
using System.Text;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Builds the JSON body of a fish.audio <c>/v1/tts</c> request.
    ///
    /// Only the parameters that differ from the endpoint defaults are written, so a request stays as
    /// small as the documented example. Pure BCL (no Unity types), which means the exact bytes that
    /// go out can be asserted outside the game.
    /// </summary>
    public static class TtsRequestBody
    {
        /// <summary>Endpoint default (best quality). Any other value is sent explicitly.</summary>
        public const string DefaultLatency = "normal";

        /// <summary>Endpoint default speaking speed (prosody.speed).</summary>
        public const float DefaultSpeed = 1f;

        /// <summary>Slowest speaking speed offered in CONFIG.</summary>
        public const float MinSpeed = 0.5f;

        /// <summary>Fastest speaking speed offered in CONFIG.</summary>
        public const float MaxSpeed = 1.5f;

        /// <summary>Accepted values of the <c>latency</c> field, fastest first.</summary>
        public static readonly string[] Latencies = { "low", "balanced", "normal" };

        /// <summary>
        /// Maps a value read from the ini or typed in CONFIG to a latency the endpoint accepts.
        /// Anything unknown falls back to <see cref="DefaultLatency"/>, so a typo cannot cause a 400.
        /// </summary>
        public static string NormalizeLatency(string latency)
        {
            if (string.IsNullOrWhiteSpace(latency)) return DefaultLatency;

            string value = latency.Trim().ToLowerInvariant();
            foreach (string option in Latencies)
                if (option == value) return value;

            return DefaultLatency;
        }

        /// <summary>Keeps the speaking speed inside the range the UI offers.</summary>
        public static float ClampSpeed(float speed)
        {
            if (float.IsNaN(speed)) return DefaultSpeed;
            return Math.Min(Math.Max(speed, MinSpeed), MaxSpeed);
        }

        /// <summary>
        /// Serializes one speech request.
        /// </summary>
        /// <param name="text">Line to speak (already stripped of anything the voice should not read).</param>
        /// <param name="voice">fish.audio <c>reference_id</c> of the voice.</param>
        /// <param name="format">Audio container; must match the AudioType used to decode it.</param>
        /// <param name="latency">low / balanced / normal; lower trades quality for time-to-first-audio.</param>
        /// <param name="speed">prosody.speed multiplier.</param>
        /// <param name="normalizeLoudness">prosody.normalize_loudness: evens out the loudness of lines.</param>
        /// <param name="normalize">Server-side text normalization (numbers) for English/Chinese.</param>
        public static string Build(
            string text,
            string voice,
            string format = "mp3",
            string latency = DefaultLatency,
            float speed = DefaultSpeed,
            bool normalizeLoudness = true,
            bool normalize = true)
        {
            var sb = new StringBuilder(192);

            sb.Append('{');
            sb.Append("\"text\":\"").Append(JsonHelper.EscapeJson(text ?? "")).Append("\",");
            sb.Append("\"reference_id\":\"").Append(JsonHelper.EscapeJson(voice ?? "")).Append("\",");
            sb.Append("\"format\":\"").Append(JsonHelper.EscapeJson(string.IsNullOrWhiteSpace(format) ? "mp3" : format)).Append('"');

            float clampedSpeed = ClampSpeed(speed);
            if (Math.Abs(clampedSpeed - DefaultSpeed) > 0.0001f || !normalizeLoudness)
            {
                sb.Append(",\"prosody\":{\"speed\":");
                // InvariantCulture matters: on a comma-decimal locale "1,1" is not valid JSON.
                sb.Append(clampedSpeed.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(",\"normalize_loudness\":").Append(normalizeLoudness ? "true" : "false");
                sb.Append('}');
            }

            if (!normalize)
                sb.Append(",\"normalize\":false");

            string normalizedLatency = NormalizeLatency(latency);
            if (normalizedLatency != DefaultLatency)
                sb.Append(",\"latency\":\"").Append(normalizedLatency).Append('"');

            sb.Append('}');
            return sb.ToString();
        }
    }
}
