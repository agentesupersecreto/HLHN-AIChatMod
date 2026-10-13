using System;
using System.Collections.Generic;
using System.IO;

namespace DialogInterceptorMod.Core
{
    /// <summary>
    /// Persistent conversation memory per character.
    /// Stores key facts, sexual/relationship milestones, conversation summaries, and conversation count.
    /// Saved/loaded from disk so memory persists across sessions in BepInEx/config/AIChat/memory/<Character>.txt.
    /// </summary>
    public class ConversationMemory
    {
        public string CharacterName { get; private set; } = "";
        public int TotalExchangeCount { get; set; }
        public string Summary { get; set; } = "";
        public List<string> KeyFacts { get; private set; } = new List<string>();
        public List<string> Tags { get; private set; } = new List<string>();

        private string _savePath = "";

        public void Bind(string characterName)
        {
            CharacterName = characterName;

            // BepInEx/config/AIChat/memory (see Plugin.GetDataDir), not the Unity save folder.
            string memoryDir = Plugin.GetDataDir(Plugin.MemoryDirName);
            _savePath = string.IsNullOrEmpty(memoryDir)
                ? ""
                : Path.Combine(memoryDir, $"{characterName}.txt");
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(_savePath)) return;
            try
            {
                var lines = new List<string>();
                lines.Add($"EXCHANGES={TotalExchangeCount}");
                if (!string.IsNullOrEmpty(Summary))
                {
                    lines.Add($"SUMMARY={Summary.Replace("\r", "").Replace("\n", "\\n")}");
                }
                foreach (var fact in KeyFacts)
                    lines.Add($"FACT={fact}");
                foreach (var tag in Tags)
                    lines.Add($"TAG={tag}");
                File.WriteAllLines(_savePath, lines);
            }
            catch (Exception ex)
            {
                Plugin.LogWarning($"Could not save memory for {CharacterName}: {ex.Message}");
            }
        }

        public void Load()
        {
            if (string.IsNullOrEmpty(_savePath) || !File.Exists(_savePath)) return;
            try
            {
                var lines = File.ReadAllLines(_savePath);

                // Reset first so Load() is idempotent
                TotalExchangeCount = 0;
                Summary = "";
                KeyFacts.Clear();
                Tags.Clear();

                foreach (var line in lines)
                {
                    if (line.StartsWith("EXCHANGES="))
                    {
                        if (int.TryParse(line.Substring(10), out int count))
                            TotalExchangeCount = count;
                    }
                    else if (line.StartsWith("SUMMARY="))
                    {
                        Summary = line.Substring(8).Replace("\\n", "\n");
                    }
                    else if (line.StartsWith("FACT="))
                    {
                        KeyFacts.Add(line.Substring(5));
                    }
                    else if (line.StartsWith("TAG="))
                    {
                        Tags.Add(line.Substring(4));
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarning($"Could not load memory for {CharacterName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Records or updates a categorized milestone (e.g. SEX_VAGINAL, SEX_TODAY, TRUST_TIER).
        /// If a milestone with the same [KEY] exists, it updates it with the latest data.
        /// </summary>
        public void RecordMilestone(string key, string text)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text)) return;
            string prefix = $"[{key}] ";
            int existingIndex = KeyFacts.FindIndex(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                KeyFacts[existingIndex] = prefix + text;
            }
            else
            {
                KeyFacts.Add(prefix + text);
                if (KeyFacts.Count > 25) KeyFacts.RemoveAt(0);
            }
        }

        /// <summary>
        /// Removes a categorized milestone (e.g. SEX_ACTIVE) if present.
        /// </summary>
        public void RemoveMilestone(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            string prefix = $"[{key}] ";
            int idx = KeyFacts.FindIndex(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) KeyFacts.RemoveAt(idx);
        }

        /// <summary>
        /// Records a factual event (e.g. "Kissed the player passionately") avoiding duplicates.
        /// </summary>
        public void RecordFact(string fact)
        {
            if (string.IsNullOrEmpty(fact)) return;
            string clean = fact.Trim();
            for (int i = 0; i < KeyFacts.Count; i++)
            {
                if (KeyFacts[i].Equals(clean, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            KeyFacts.Add(clean);
            if (KeyFacts.Count > 25) KeyFacts.RemoveAt(0);
        }

        /// <summary>
        /// Reads real-time in-game engine stats (Emotions & InteractionManager) to record
        /// persistent sexual and relationship milestones into this character's memory file.
        /// </summary>
        public void UpdateMilestones(Emotions emotions, NpcInteractionManager interactionManager)
        {
            if (emotions == null) return;

            // 1. Sexual Experience & Virginity Milestones
            if (emotions.isVaginalVirgin == 0 || emotions.AmountOfVaginalSex > 0)
            {
                string virginNote = emotions.isVaginalVirgin == 0 ? ", gave her vaginal virginity to him" : "";
                RecordMilestone("SEX_VAGINAL", $"Has had vaginal sex with the player ({emotions.AmountOfVaginalSex} times total{virginNote}).");
            }

            if (emotions.isAnalVirgin == 0 || emotions.AmountOfAnalSex > 0)
            {
                string virginNote = emotions.isAnalVirgin == 0 ? ", gave her anal virginity to him" : "";
                RecordMilestone("SEX_ANAL", $"Has had anal sex with the player ({emotions.AmountOfAnalSex} times total{virginNote}).");
            }

            if (emotions.isOralVirgin == 0 || emotions.AmountOfOralSex > 0)
            {
                RecordMilestone("SEX_ORAL", $"Has performed oral sex on the player ({emotions.AmountOfOralSex} times total).");
            }

            // 2. Sex status for today / active
            if (emotions.isBeingFucked)
            {
                RecordMilestone("SEX_ACTIVE", "Currently engaged in sex with the player.");
            }
            else
            {
                RemoveMilestone("SEX_ACTIVE");
            }

            if (emotions.wasFuckedToday)
            {
                RecordMilestone("SEX_TODAY", "Had sex with the player today.");
            }
            else
            {
                RemoveMilestone("SEX_TODAY");
            }

            // 3. Orgasm milestones
            if (emotions.hadOrgasmToday && emotions.OrgasmCount > 0)
            {
                RecordMilestone("ORGASM", $"Reached orgasm with the player today ({emotions.OrgasmCount} times).");
            }
            else
            {
                RemoveMilestone("ORGASM");
            }

            // 4. Pregnancy
            if (emotions.PregnancyDate > 0)
            {
                RecordMilestone("PREGNANCY", $"Is pregnant with the player's child (Day {emotions.PregnancyDate}).");
            }

            // 5. Relationship / Trust level
            if (emotions.Trust >= 100f)
            {
                RecordMilestone("RELATIONSHIP", "Deeply in love and fully devoted to the player (Trust 100+).");
            }
            else if (emotions.Trust >= 60f)
            {
                RecordMilestone("RELATIONSHIP", "Very close, affectionate and trusting relationship with the player (Trust 60+).");
            }
            else if (emotions.Trust >= 30f)
            {
                RecordMilestone("RELATIONSHIP", "Friendly, receptive and warming up to the player (Trust 30+).");
            }

            // 6. Nudity
            if (interactionManager != null && interactionManager.IsNaked)
            {
                RecordMilestone("NUDITY", "Has been completely naked around the player.");
            }
        }

        /// <summary>
        /// Auto-tag conversation topics from AI responses.
        /// </summary>
        public void AutoTag(string text, bool isAI)
        {
            string lower = text.ToLower();
            TryAddTag(lower, "flirting", "flirt", "cute", "attractive", "beautiful", "guapo", "linda", "hermosa");
            TryAddTag(lower, "intimate", "kiss", "touch", "hug", "close", "beso", "abraz", "caricia");
            TryAddTag(lower, "angry", "mad", "furious", "hate", "angry", "enojad", "odio", "molest");
            TryAddTag(lower, "sad", "sad", "cry", "upset", "depressed", "triste", "llor");
            TryAddTag(lower, "happy", "happy", "glad", "joy", "wonderful", "feliz", "alegr");
            TryAddTag(lower, "sexual", "sex", "fuck", "naked", "undress", "desnud", "coger", "horny", "chupar", "penetrat", "dildo", "masturbat");
        }

        private void TryAddTag(string text, string tag, params string[] keywords)
        {
            if (Tags.Contains(tag)) return;
            foreach (var kw in keywords)
            {
                if (text.Contains(kw))
                {
                    Tags.Add(tag);
                    return;
                }
            }
        }

        /// <summary>
        /// Generates a structured memory block for the AI prompt.
        /// </summary>
        public string GenerateMemoryBlock()
        {
            if (TotalExchangeCount == 0 && KeyFacts.Count == 0 && Tags.Count == 0 && string.IsNullOrEmpty(Summary))
                return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n[PERSISTENT LONG-TERM MEMORY]");
            sb.AppendLine($"- Conversations logged: {TotalExchangeCount}");

            if (!string.IsNullOrEmpty(Summary))
            {
                sb.AppendLine("\n[CONVERSATION_SUMMARY]");
                sb.AppendLine(Summary.Trim());
            }

            if (KeyFacts.Count > 0)
            {
                sb.AppendLine("\n[RELATIONSHIP_MILESTONES & PERSISTENT FACTS]");
                foreach (var fact in KeyFacts)
                {
                    string displayFact = fact;
                    if (displayFact.StartsWith("[") && displayFact.IndexOf("] ") > 0)
                    {
                        displayFact = displayFact.Substring(displayFact.IndexOf("] ") + 2);
                    }
                    sb.AppendLine($"  * {displayFact}");
                }
            }

            if (Tags.Count > 0)
            {
                sb.AppendLine($"\n[TOPICS_DISCUSSED] {string.Join(", ", Tags)}");
            }

            return sb.ToString();
        }

        // --- Centralized Summarization (Sliding Window + Summary) ---

        /// <summary>Number of history messages that triggers a summarization pass.</summary>
        public const int SummarizeThreshold = 10;

        /// <summary>Number of oldest non-system messages consumed per summarization pass.</summary>
        public const int MessagesToSummarize = 6;

        /// <summary>System instruction sent to the AI for the summarization sub-request.</summary>
        public const string SummarizationInstruction =
            "Summarize the key events and context of this conversation briefly in English (max 2-3 sentences). " +
            "Combine it with the previous summary if provided. Focus on relationship progress, emotional shifts, " +
            "and any important decisions or actions.";

        /// <summary>
        /// Builds the user-side prompt for a summarization request, combining the existing summary
        /// with the oldest messages that are about to be pruned.
        /// </summary>
        public static string BuildSummarizationPrompt(string currentSummary, System.Collections.Generic.List<Models.ChatMessage> history, int messagesToConsume)
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(currentSummary))
                sb.AppendLine($"Previous Summary: {currentSummary}\n");

            sb.AppendLine("New Chat Messages to summarize:");
            int counted = 0;
            for (int i = 0; i < history.Count && counted < messagesToConsume; i++)
            {
                var msg = history[i];
                if (msg.IsSystem) continue;
                sb.AppendLine((msg.IsUser ? "Player: " : "Model: ") + msg.Text);
                counted++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Applies the AI-generated summary and removes the consumed messages from the history.
        /// Called by any API client after its summarization request succeeds.
        /// </summary>
        public void ApplySummaryResult(string summaryText, System.Collections.Generic.List<Models.ChatMessage> history, int messagesToConsume)
        {
            if (string.IsNullOrEmpty(summaryText)) return;

            Summary = summaryText.Trim();
            Save();

            int removed = 0;
            while (removed < messagesToConsume && history.Count > 0)
            {
                if (!history[0].IsSystem)
                {
                    history.RemoveAt(0);
                    removed++;
                }
                else
                {
                    history.RemoveAt(0);
                }
            }

            Plugin.LogInfo($"Summary updated for {CharacterName}. Pruned {removed} messages, {history.Count} remaining.");
        }
    }
}
