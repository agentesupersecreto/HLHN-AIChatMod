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
        /// <summary>
        /// fish.audio voice tone tags. They are kept in the text sent to the TTS endpoint but removed
        /// from the on-screen dialogue. Only tags from the official list are matched, so any other
        /// bracketed text the model writes is left untouched.
        /// </summary>
        private static readonly Regex ToneTagRegex = new Regex(
            @"\[\s*(angry|sad|embarrassed|emphasis|whispering|soft|breathy|excited|laughing|chuckling|moaning|clear throat|sobbing|crying loudly|sighing|panting|groaning|crowd laughing|background laughter|audience laughing|pause|long pause)\s*\]",
            RegexOptions.IgnoreCase);

        public static void ProcessResponse(string rawResponse, DialogBehaviour behaviour, string clientName)
        {
            try
            {
                rawResponse = JsonHelper.StripMarkdownCodeBlocks(rawResponse);
                string dialogue = rawResponse;
                var foundCommands = new List<string>();

                // Extract [CMD:] or [COMMAND:]
                var matches = Regex.Matches(dialogue, @"\[(?:CMD|COMMAND):\s*([^\]]+)\]", RegexOptions.IgnoreCase);
                foreach (Match m in matches)
                {
                    string commandContent = m.Groups[1].Value.Trim();
                    dialogue = dialogue.Replace(m.Value, "");

                    string[] subCmds = commandContent.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var sub in subCmds)
                    {
                        string c = sub.Trim();
                        if (!string.IsNullOrEmpty(c) && c.ToLower() != "null")
                            foundCommands.Add(c);
                    }
                }
                dialogue = dialogue.Trim();

                string detectedStyle = null;

                // Handle JSON responses (some models emit JSON even in plain-text mode).
                // English keys are the documented protocol; the Spanish keys are legacy
                // (older prompts and user-edited system_prompt.txt templates) and still work.
                if (dialogue.StartsWith("{") && (dialogue.Contains("\"dialogue\"") || dialogue.Contains("\"dialogo\"")))
                {
                    string innerDialogue = JsonHelper.ExtractJsonValue(dialogue, "dialogue");
                    if (string.IsNullOrEmpty(innerDialogue))
                        innerDialogue = JsonHelper.ExtractJsonValue(dialogue, "dialogo");

                    if (!string.IsNullOrEmpty(innerDialogue))
                    {
                        string innerCommand = JsonHelper.ExtractJsonValue(dialogue, "command");
                        if (string.IsNullOrEmpty(innerCommand))
                            innerCommand = JsonHelper.ExtractJsonValue(dialogue, "comando");

                        if (!string.IsNullOrEmpty(innerCommand))
                        {
                            string[] subCmds = innerCommand.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var sub in subCmds)
                            {
                                string c = sub.Trim();
                                if (!string.IsNullOrEmpty(c) && c.ToLower() != "null" && !foundCommands.Contains(c))
                                    foundCommands.Add(c);
                            }
                        }

                        string innerStyle = JsonHelper.ExtractJsonValue(dialogue, "style");
                        if (string.IsNullOrEmpty(innerStyle))
                            innerStyle = JsonHelper.ExtractJsonValue(dialogue, "estilo");
                        if (!string.IsNullOrEmpty(innerStyle))
                            detectedStyle = innerStyle.Trim();

                        dialogue = innerDialogue;
                    }
                }

                // Extract [STYLE: name]
                var styleMatches = Regex.Matches(dialogue, @"\[STYLE:\s*([^\]]+)\]", RegexOptions.IgnoreCase);
                foreach (Match m in styleMatches)
                {
                    detectedStyle = m.Groups[1].Value.Trim();
                    dialogue = dialogue.Replace(m.Value, "");
                }

                dialogue = dialogue.Trim();

                // fish.audio tone tags ([excited], [whispering], ...) act on the voice, so they stay in
                // ttsText while displayDialogue — everything shown in the game — has them removed.
                string ttsText = ToneTagRegex.Replace(dialogue,
                    m => "[" + m.Groups[1].Value.ToLowerInvariant() + "]");
                string displayDialogue = Regex.Replace(ToneTagRegex.Replace(dialogue, ""), @"[ \t]{2,}", " ").Trim();

                if (string.IsNullOrEmpty(displayDialogue) && foundCommands.Count == 0)
                {
                    behaviour.Window.SetStatus($"[ERROR] Empty or invalid response from {clientName}.", true);
                    return;
                }

                if (!string.IsNullOrEmpty(displayDialogue))
                {
                    // Add to chat history (clean text, without tone tags)
                    behaviour.ChatHistory.Add(new ChatMessage(false, displayDialogue));
                    behaviour.Memory.TotalExchangeCount++;
                    behaviour.Memory.AutoTag(displayDialogue, true);
                    behaviour.Window.ScrollToBottom();
                    behaviour.Window.SetStatus($"[OK] Response received ({clientName}).", false);

                    // *** KEY: Speak the reply AND show it in-game. The voice gets the tagged text
                    // (ttsText) so it can act, while the on-screen text and the facial reaction use the
                    // clean displayDialogue. When TTS is enabled the voice is generated first and this
                    // callback runs the instant the audio is ready, so text, reaction and voice all
                    // start together; with TTS off it fires immediately. ***
                    behaviour.SpeakWithTts(ttsText, () =>
                    {
                        ShowInGameDialogue(behaviour, displayDialogue);
                        CharacterReactionController.TriggerReaction(behaviour, detectedStyle, displayDialogue);
                    });

                    // Sentiment analysis → Trust modification
                    if (!string.IsNullOrEmpty(detectedStyle))
                    {
                        string sentimentFeedback = SentimentAnalyzer.ApplySentimentFromStyle(detectedStyle);
                        if (!string.IsNullOrEmpty(sentimentFeedback))
                            behaviour.AddCapturedEvent(sentimentFeedback);
                    }
                    else
                    {
                        string sentimentFeedback = SentimentAnalyzer.ApplySentiment(displayDialogue);
                        if (!string.IsNullOrEmpty(sentimentFeedback))
                            behaviour.AddCapturedEvent(sentimentFeedback);
                    }
                }
                else
                {
                    behaviour.Window.SetStatus($"[OK] Silent command execution ({clientName}).", false);
                }

                // Execute embedded commands
                foreach (string cmd in foundCommands)
                {
                    string feedback = CommandExecutor.ExecuteCommand(
                        cmd, 
                        behaviour.Window.SetStatus, 
                        behaviour.Window.ShowEmotionFeedback
                    );
                    behaviour.AddCapturedEvent($"[CMD] {feedback}");
                }

                if (foundCommands.Count > 0)
                    behaviour.Window.ScrollToBottom();

                // Save history and persistent character memory
                behaviour.Window.SaveChatHistory();
                behaviour.Memory?.Save();
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
        private static void ShowInGameDialogue(DialogBehaviour behaviour, string text)
        {
            try
            {
                // Route through the NPC's Speach component. The IsAIResponse echo guard is
                // toggled around the actual Say() call inside DialogBehaviour.SayWhenIdle(),
                // so it also covers retries and the [CMD: say] path.
                behaviour.ShowAIResponseInGame(text);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug($"ShowInGameDialogue error: {ex.Message}");
            }
        }

    }
}
