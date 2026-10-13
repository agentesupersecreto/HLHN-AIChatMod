using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using DialogInterceptorMod.Core;
using DialogInterceptorMod.Models;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Local Gemma provider (e.g. gemma-the-writer-n-restless-quill-10b-uncensored)
    /// served via an Ollama-style /api/generate endpoint.
    ///
    /// Gemma requires its STRICT chat template using control tokens:
    ///   <start_of_turn>user\n{...}<end_of_turn>
    ///   <start_of_turn>model\n{...}<end_of_turn>
    /// The final <start_of_turn>model turn is left open so the model continues it.
    /// System instructions are prepended before the turns (Gemma has no dedicated
    /// system role in the base template).
    /// </summary>
    public class GemmaClient : IApiClient
    {
        // Control tokens — written as literal strings; the Gemma tokenizer
        // recognizes them as special tokens.
        private const string START_OF_TURN = "<start_of_turn>";
        private const string END_OF_TURN = "<end_of_turn>";

        private const int SLIDING_WINDOW_SIZE = 5; // Keep last 5 exchanges (10 messages)
        private const int MAX_OUTPUT_TOKENS = 150;

        private readonly DialogBehaviour _behaviour;
        private bool _isSummarizing = false;

        public GemmaClient(DialogBehaviour behaviour)
        {
            _behaviour = behaviour;
        }

        public IEnumerator SendMessage(string userMessage)
        {
            string url = NormalizeGemmaUrl(_behaviour.GemmaUrl);
            string model = _behaviour.GemmaModel;

            string systemPrompt = PromptBuilder.GenerateSystemPrompt(userMessage);
            string fullPrompt = BuildGemmaPrompt(systemPrompt, userMessage);

            string escapedPrompt = JsonHelper.EscapeJson(fullPrompt);
            string body = $"{{\"model\":\"{JsonHelper.EscapeJson(model)}\",\"prompt\":\"{escapedPrompt}\",\"stream\":false,\"options\":{{\"num_predict\":{MAX_OUTPUT_TOKENS}}}}}";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            UnityWebRequest request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 180; // 10B model can be slow on CPU/constrained GPUs

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                string error = request.error;
                string responseBody = request.downloadHandler?.text ?? "";
                Plugin.Log.LogError($"Gemma error: {error} | Body: {responseBody}");

                if (error != null && error.Contains("Cannot connect"))
                    _behaviour.Window.SetStatus("[ERROR] Cannot connect to Gemma server. Is Ollama running? (ollama serve)", true);
                else
                    _behaviour.Window.SetStatus($"[ERROR] Gemma: {error}", true);

                _behaviour.Window.AwaitingResponse = false;
                yield break;
            }

            string rawResponse = request.downloadHandler.text;
            Plugin.Log.LogInfo($"Gemma raw response: {(rawResponse.Length > 200 ? rawResponse.Substring(0, 200) + "..." : rawResponse)}");

            // Ollama-style response: extract the "response" field.
            string aiText = JsonHelper.ExtractJsonValue(rawResponse, "response");

            if (string.IsNullOrEmpty(aiText))
            {
                _behaviour.Window.SetStatus("[ERROR] Empty response from Gemma.", true);
                _behaviour.Window.AwaitingResponse = false;
                yield break;
            }

            // Stop at the model's own <end_of_turn> / next <start_of_turn> if it kept going.
            aiText = TruncateAtTurnBoundary(aiText);

            AIResponseProcessor.ProcessResponse(aiText, _behaviour, "Gemma");
            _behaviour.Window.AwaitingResponse = false;

            if (_behaviour.ChatHistory.Count > 10 && !_isSummarizing)
            {
                _behaviour.StartCoroutine(GenerateSummaryAndClean());
            }
        }

        private IEnumerator GenerateSummaryAndClean()
        {
            if (_isSummarizing) yield break;
            _isSummarizing = true;

            int messagesToSummarize = 6;
            if (_behaviour.ChatHistory.Count <= messagesToSummarize)
            {
                _isSummarizing = false;
                yield break;
            }

            StringBuilder textToSummarize = new StringBuilder();
            int counted = 0;
            for (int i = 0; i < _behaviour.ChatHistory.Count && counted < messagesToSummarize; i++)
            {
                var msg = _behaviour.ChatHistory[i];
                if (msg.IsSystem) continue;
                textToSummarize.AppendLine((msg.IsUser ? "Player: " : "Model: ") + msg.Text);
                counted++;
            }

            string url = NormalizeGemmaUrl(_behaviour.GemmaUrl);
            string currentSummary = _behaviour.ChatSummary;
            string sysPrompt = "Summarize the key events and context of this conversation briefly in English (max 2-3 sentences). Combine it with the previous summary if provided.";
            string promptText = (string.IsNullOrEmpty(currentSummary) ? "" : $"Previous Summary: {currentSummary}\n\n") + $"New Chat Messages to summarize:\n{textToSummarize}";

            string gemmaPrompt = $"{START_OF_TURN}user\n{sysPrompt}\n\n{promptText}\n{END_OF_TURN}\n{START_OF_TURN}model\n";
            string escapedPrompt = JsonHelper.EscapeJson(gemmaPrompt);
            string body = $"{{\"model\":\"{JsonHelper.EscapeJson(_behaviour.GemmaModel)}\",\"prompt\":\"{escapedPrompt}\",\"stream\":false,\"options\":{{\"num_predict\":120}}}}";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            UnityWebRequest request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 60;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Plugin.Log.LogWarning($"Gemma summary error: {request.error}");
                _isSummarizing = false;
                yield break;
            }

            string rawResponse = request.downloadHandler.text;
            string aiText = JsonHelper.ExtractJsonValue(rawResponse, "response");
            aiText = TruncateAtTurnBoundary(aiText);

            if (!string.IsNullOrEmpty(aiText))
            {
                _behaviour.ChatSummary = aiText.Trim();
                if (_behaviour.Memory != null)
                {
                    _behaviour.Memory.Summary = _behaviour.ChatSummary;
                    _behaviour.Memory.Save();
                }
                int removed = 0;
                while (removed < messagesToSummarize && _behaviour.ChatHistory.Count > 0)
                {
                    if (!_behaviour.ChatHistory[0].IsSystem)
                    {
                        _behaviour.ChatHistory.RemoveAt(0);
                        removed++;
                    }
                    else
                    {
                        _behaviour.ChatHistory.RemoveAt(0);
                    }
                }
                Plugin.Log.LogInfo($"Gemma summary updated. Remaining messages: {_behaviour.ChatHistory.Count}");
            }
            _isSummarizing = false;
        }

        /// <summary>
        /// This client speaks Ollama's /api/generate protocol (raw `prompt` + `options`, reply in
        /// the `response` field), so the configured endpoint must be an Ollama one. A bare host
        /// (http://localhost:11434) or an /api prefix is completed automatically; an OpenAI-style
        /// /v1 endpoint cannot work with this protocol and is reported as such.
        /// </summary>
        private static string NormalizeGemmaUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "http://localhost:11434/api/generate";

            url = url.Trim().TrimEnd('/');

            if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) || url.Contains("/v1/"))
            {
                Plugin.LogWarning($"GemmaUrl '{url}' looks like an OpenAI-style endpoint, but GemmaClient uses Ollama's /api/generate protocol. Use e.g. http://localhost:11434/api/generate.");
            }

            if (url.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                return url + "/generate";

            // No path at all (e.g. http://192.168.1.10:11434) — assume the Ollama default path.
            int schemeIdx = url.IndexOf("://", StringComparison.Ordinal);
            string hostAndPath = schemeIdx >= 0 ? url.Substring(schemeIdx + 3) : url;
            if (!hostAndPath.Contains("/"))
                return url + "/api/generate";

            return url;
        }

        /// <summary>
        /// Builds the strict Gemma chat template:
        ///   {system prompt}
        ///   <start_of_turn>user\n{msg}<end_of_turn>
        ///   <start_of_turn>model\n{msg}<end_of_turn>
        ///   ...
        ///   <start_of_turn>user\n{current}<end_of_turn>
        ///   <start_of_turn>model\n   <- left open for generation
        /// </summary>
        private string BuildGemmaPrompt(string systemPrompt, string currentMessage)
        {
            StringBuilder sb = new StringBuilder();

            // System instructions go first (Gemma has no system role in the base template).
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.Append(systemPrompt.Trim());
                sb.Append("\n\n");
            }

            // Sliding window: include only the last N exchanges (non-system messages).
            int windowMessages = SLIDING_WINDOW_SIZE * 2;
            int startIdx = 0;
            int nonSystemCount = 0;
            for (int i = _behaviour.ChatHistory.Count - 1; i >= 0; i--)
            {
                if (!_behaviour.ChatHistory[i].IsSystem)
                {
                    nonSystemCount++;
                    if (nonSystemCount >= windowMessages)
                    {
                        startIdx = i;
                        break;
                    }
                }
            }

            for (int i = startIdx; i < _behaviour.ChatHistory.Count; i++)
            {
                var msg = _behaviour.ChatHistory[i];
                if (msg.IsSystem) continue;
                string role = msg.IsUser ? "user" : "model";
                sb.Append(START_OF_TURN).Append(role).Append('\n')
                  .Append(msg.Text).Append('\n')
                  .Append(END_OF_TURN).Append('\n');
            }

            // Current user turn, then open the model turn.
            sb.Append(START_OF_TURN).Append("user\n")
              .Append(currentMessage).Append('\n')
              .Append(END_OF_TURN).Append('\n');
            sb.Append(START_OF_TURN).Append("model\n");

            return sb.ToString();
        }

        /// <summary>
        /// If the model continues past its own turn (emitting a new control token),
        /// cut the reply at that boundary so we only keep the model's actual line.
        /// </summary>
        private string TruncateAtTurnBoundary(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int cut = text.Length;
            int nextStart = text.IndexOf(START_OF_TURN, StringComparison.Ordinal);
            if (nextStart >= 0) cut = Math.Min(cut, nextStart);
            int nextEnd = text.IndexOf(END_OF_TURN, StringComparison.Ordinal);
            if (nextEnd >= 0) cut = Math.Min(cut, nextEnd);
            return text.Substring(0, cut).Trim();
        }
    }
}
