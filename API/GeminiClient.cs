using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using DialogInterceptorMod.Core;
using DialogInterceptorMod.Models;

namespace DialogInterceptorMod.API
{
    public class GeminiClient : IApiClient
    {
        private DialogBehaviour _behaviour;

        private bool _isSummarizing = false;

        public GeminiClient(DialogBehaviour behaviour)
        {
            _behaviour = behaviour;
        }

        public IEnumerator SendMessage(string userMessage)
        {
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-lite-latest:generateContent?key={_behaviour.ApiKey}";
            string systemPromptStr = PromptBuilder.GenerateSystemPrompt(userMessage);

            StringBuilder contentsJson = new StringBuilder();
            contentsJson.Append("[");

            bool isFirst = true;
            foreach (var msg in _behaviour.ChatHistory)
            {
                if (msg.IsSystem) continue; // Skip system feedback messages
                if (!isFirst) contentsJson.Append(",");
                isFirst = false;
                string role = msg.IsUser ? "user" : "model";
                // Assistant turns are wrapped in the same JSON shape this provider is
                // asked for in the format instruction (see PromptBuilder).
                string escapedText = EscapeJson(msg.IsUser ? msg.Text : $"{{\"dialogue\": \"{msg.Text}\", \"command\": null}}");
                contentsJson.Append($"{{\"role\":\"{role}\",\"parts\":[{{\"text\":\"{escapedText}\"}}]}}");
            }
            contentsJson.Append("]");

            string escapedSystemPrompt = EscapeJson(systemPromptStr);
            string body = $"{{\"system_instruction\":{{\"parts\":[{{\"text\":\"{escapedSystemPrompt}\"}}]}},\"contents\":{contentsJson.ToString()},\"generationConfig\":{{\"maxOutputTokens\":512}}}}";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            UnityWebRequest request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                string error = request.error;
                string responseBody = request.downloadHandler?.text ?? "";
                Plugin.Log.LogError($"Gemini error: {error} | Body: {responseBody}");
                
                string errorMsg = ExtractErrorMessage(responseBody);
                if (!string.IsNullOrEmpty(errorMsg))
                    _behaviour.Window.SetStatus($"[ERROR] API Error: {errorMsg}", true);
                else
                    _behaviour.Window.SetStatus($"[ERROR] {error}", true);

                _behaviour.Window.AwaitingResponse = false;
                yield break;
            }

            string rawResponse = request.downloadHandler.text;
            string aiText = ExtractResponseText(rawResponse);

            if (string.IsNullOrEmpty(aiText))
            {
                _behaviour.Window.SetStatus("[ERROR] Could not parse JSON response.", true);
                _behaviour.Window.AwaitingResponse = false;
                yield break;
            }

            AIResponseProcessor.ProcessResponse(aiText, _behaviour, "Gemini");
            _behaviour.Window.AwaitingResponse = false;

            // Gemini uses summarization for memory
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

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-lite-latest:generateContent?key={_behaviour.ApiKey}";
            string currentSummary = _behaviour.ChatSummary;
            
            string sysPrompt = "Summarize the key events and context of this conversation briefly in English. Combine it with the previous summary if provided.";
            string prompt = (string.IsNullOrEmpty(currentSummary) ? "" : $"Previous Summary: {currentSummary}\n\n") + $"New Chat Messages to summarize:\n{textToSummarize.ToString()}";

            string bodyStr = $"{{\"system_instruction\":{{\"parts\":[{{\"text\":\"{EscapeJson(sysPrompt)}\"}}]}},\"contents\":[{{\"role\":\"user\",\"parts\":[{{\"text\":\"{EscapeJson(prompt)}\"}}]}}],\"generationConfig\":{{\"maxOutputTokens\":512}}}}";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyStr);
            UnityWebRequest request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Plugin.Log.LogWarning($"Could not generate the conversation summary: {request.error}");
                _isSummarizing = false;
                yield break;
            }

            string rawResponse = request.downloadHandler.text;
            string aiText = ExtractResponseText(rawResponse);

            if (!string.IsNullOrEmpty(aiText))
            {
                _behaviour.ChatSummary = aiText.Trim();
                if (_behaviour.Memory != null)
                {
                    _behaviour.Memory.Summary = _behaviour.ChatSummary;
                    _behaviour.Memory.Save();
                }
                // Remove the oldest non-system messages
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
                Plugin.Log.LogInfo($"Summary updated. Remaining messages: {_behaviour.ChatHistory.Count}");
            }
            _isSummarizing = false;
        }

        private string ExtractErrorMessage(string errorJson)
        {
            string msg = JsonHelper.ExtractJsonValue(errorJson, "message");
            if (string.IsNullOrEmpty(msg)) return null;
            if (msg.Length > 80) return msg.Substring(0, 77) + "...";
            return msg;
        }

        private string ExtractResponseText(string json)
        {
            int partsIdx = json.IndexOf("\"parts\"");
            if (partsIdx < 0) return null;
            int textKeyIdx = json.IndexOf("\"text\"", partsIdx);
            if (textKeyIdx < 0) return null;
            int colonIdx = json.IndexOf(':', textKeyIdx + 6);
            if (colonIdx < 0) return null;
            int startQuote = json.IndexOf('"', colonIdx + 1);
            if (startQuote < 0) return null;

            StringBuilder result = new StringBuilder();
            int i = startQuote + 1;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': break;
                        case 't': result.Append('\t'); break;
                        default: result.Append(next); break;
                    }
                    i += 2;
                }
                else if (c == '"') break;
                else { result.Append(c); i++; }
            }
            
            string clean = result.ToString().Trim();
            if (clean.StartsWith("```json")) clean = clean.Substring(7);
            if (clean.StartsWith("```")) clean = clean.Substring(3);
            if (clean.EndsWith("```")) clean = clean.Substring(0, clean.Length - 3);
            
            return clean.Trim();
        }

        public static string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
