using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using DialogInterceptorMod.Core;
using DialogInterceptorMod.Game;
using DialogInterceptorMod.Models;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Processes AI responses: extracts dialogue text, commands, and style tags.
    /// Shows the AI response in the game's native ShowDialogue system via Speach.Say(),
    /// executes embedded commands, and applies sentiment-based Trust changes.
    /// </summary>
    public static class AIResponseProcessor
    {
        public static void ProcessResponse(string respuesta, DialogBehaviour behaviour, string clientName)
        {
            try
            {
                respuesta = JsonHelper.StripMarkdownCodeBlocks(respuesta);
                string dialogo = respuesta;
                var comandosEncontrados = new List<string>();

                // Extract [CMD:] or [COMMAND:]
                var matches = Regex.Matches(dialogo, @"\[(?:CMD|COMMAND):\s*([^\]]+)\]", RegexOptions.IgnoreCase);
                foreach (Match m in matches)
                {
                    string contenido = m.Groups[1].Value.Trim();
                    dialogo = dialogo.Replace(m.Value, "");

                    string[] subCmds = contenido.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var sub in subCmds)
                    {
                        string c = sub.Trim();
                        if (!string.IsNullOrEmpty(c) && c.ToLower() != "null")
                            comandosEncontrados.Add(c);
                    }
                }
                dialogo = dialogo.Trim();

                // Handle JSON responses (some models emit JSON even in plain-text mode)
                if (dialogo.StartsWith("{") && dialogo.Contains("\"dialogo\""))
                {
                    string innerDialogo = JsonHelper.ExtractJsonValue(dialogo, "dialogo");
                    if (!string.IsNullOrEmpty(innerDialogo))
                    {
                        string innerComando = JsonHelper.ExtractJsonValue(dialogo, "comando");
                        if (!string.IsNullOrEmpty(innerComando))
                        {
                            string[] subCmds = innerComando.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var sub in subCmds)
                            {
                                string c = sub.Trim();
                                if (!string.IsNullOrEmpty(c) && c.ToLower() != "null" && !comandosEncontrados.Contains(c))
                                    comandosEncontrados.Add(c);
                            }
                        }
                        dialogo = innerDialogo;
                    }
                }

                // Extract [STYLE: estilo]
                string estiloDetectado = null;
                var styleMatches = Regex.Matches(dialogo, @"\[STYLE:\s*([^\]]+)\]", RegexOptions.IgnoreCase);
                foreach (Match m in styleMatches)
                {
                    estiloDetectado = m.Groups[1].Value.Trim();
                    dialogo = dialogo.Replace(m.Value, "");
                }

                dialogo = dialogo.Trim();

                if (string.IsNullOrEmpty(dialogo) && comandosEncontrados.Count == 0)
                {
                    behaviour.Window.SetStatus($"[ERROR] Empty or invalid response from {clientName}.", true);
                    return;
                }

                if (!string.IsNullOrEmpty(dialogo))
                {
                    // Add to chat history
                    behaviour.ChatHistory.Add(new ChatMessage(false, dialogo));
                    behaviour.Memory.TotalExchangeCount++;
                    behaviour.Memory.AutoTag(dialogo, true);
                    behaviour.Window.ScrollToBottom();
                    behaviour.Window.SetStatus($"[OK] Response received ({clientName}).", false);

                    // *** KEY: Show the AI response in the game's native dialogue system ***
                    ShowInGameDialogue(behaviour, dialogo);

                    // Sentiment analysis → Trust modification
                    if (!string.IsNullOrEmpty(estiloDetectado))
                    {
                        string sentimentFeedback = SentimentAnalyzer.ApplySentimentFromStyle(estiloDetectado);
                        if (!string.IsNullOrEmpty(sentimentFeedback))
                            behaviour.AddCapturedEvent(sentimentFeedback);
                    }
                    else
                    {
                        string sentimentFeedback = SentimentAnalyzer.ApplySentiment(dialogo);
                        if (!string.IsNullOrEmpty(sentimentFeedback))
                            behaviour.AddCapturedEvent(sentimentFeedback);
                    }
                }
                else
                {
                    behaviour.Window.SetStatus($"[OK] Silent command execution ({clientName}).", false);
                }

                // Execute embedded commands
                foreach (string cmd in comandosEncontrados)
                {
                    string feedback = CommandExecutor.ExecuteCommand(
                        cmd, 
                        behaviour.Window.SetStatus, 
                        behaviour.Window.ShowEmotionFeedback
                    );
                    behaviour.AddCapturedEvent($"[CMD] {feedback}");
                }

                if (comandosEncontrados.Count > 0)
                    behaviour.Window.ScrollToBottom();

                // Save history
                behaviour.Window.SaveChatHistory();
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Error processing {clientName} response: {ex.Message}");
                behaviour.Window.SetStatus($"[ERROR] Processing {clientName} response.", true);
            }
        }

        /// <summary>
        /// Shows the AI response through the game's native dialogue system.
        /// Uses Speach.Say() on the current NPC, which makes ShowDialogue display it
        /// with the NPC's name above their head.
        /// </summary>
        private static void ShowInGameDialogue(DialogBehaviour behaviour, string texto)
        {
            try
            {
                // Route through the NPC's Speach component. The IsAIResponse echo guard is
                // toggled around the actual Say() call inside DialogBehaviour.SayWhenIdle(),
                // so it also covers retries and the [CMD: say] path.
                behaviour.ShowAIResponseInGame(texto);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug($"ShowInGameDialogue error: {ex.Message}");
            }
        }

    }
}
