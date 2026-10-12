using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DialogInterceptorMod.Core
{
    /// <summary>
    /// Persistent conversation memory per character.
    /// Stores key facts, relationship milestones, and conversation count.
    /// Saved/loaded from disk so memory persists across sessions.
    /// </summary>
    public class ConversationMemory
    {
        public string CharacterName { get; private set; } = "";
        public int TotalExchangeCount { get; set; }
        public List<string> KeyFacts { get; private set; } = new List<string>();
        public List<string> Tags { get; private set; } = new List<string>();

        private string _savePath = "";

        public void Bind(string characterName)
        {
            CharacterName = characterName;
            string memoryDir = Path.Combine(Application.persistentDataPath, "AIchat", "Memory");
            if (!Directory.Exists(memoryDir))
                Directory.CreateDirectory(memoryDir);
            _savePath = Path.Combine(memoryDir, $"{characterName}.txt");
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(_savePath)) return;
            try
            {
                var lines = new List<string>();
                lines.Add($"EXCHANGES={TotalExchangeCount}");
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

                // Reset first so Load() is idempotent (it must not duplicate facts/tags
                // when called more than once on the same instance).
                TotalExchangeCount = 0;
                KeyFacts.Clear();
                Tags.Clear();

                foreach (var line in lines)
                {
                    if (line.StartsWith("EXCHANGES="))
                    {
                        if (int.TryParse(line.Substring(10), out int count))
                            TotalExchangeCount = count;
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
        /// Auto-tag conversation topics from AI responses.
        /// </summary>
        public void AutoTag(string text, bool isAI)
        {
            string lower = text.ToLower();
            TryAddTag(lower, "flirting", "flirt", "cute", "attractive", "beautiful");
            TryAddTag(lower, "intimate", "kiss", "touch", "hug", "close");
            TryAddTag(lower, "angry", "mad", "furious", "hate", "angry");
            TryAddTag(lower, "sad", "sad", "cry", "upset", "depressed");
            TryAddTag(lower, "happy", "happy", "glad", "joy", "wonderful");
            TryAddTag(lower, "sexual", "sex", "fuck", "naked", "undress");
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
        /// Generates a memory block for the AI prompt.
        /// </summary>
        public string GenerateMemoryBlock()
        {
            if (TotalExchangeCount == 0 && KeyFacts.Count == 0 && Tags.Count == 0)
                return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"\n[MEMORY] Conversations:{TotalExchangeCount}");
            if (Tags.Count > 0)
                sb.AppendLine($"[TOPICS_DISCUSSED] {string.Join(", ", Tags)}");
            if (KeyFacts.Count > 0)
            {
                sb.AppendLine("[KEY_FACTS]");
                foreach (var fact in KeyFacts)
                    sb.AppendLine($"  - {fact}");
            }
            return sb.ToString();
        }
    }
}
