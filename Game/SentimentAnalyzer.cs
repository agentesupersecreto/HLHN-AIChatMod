using System;
using UnityEngine;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Analyzes AI responses for sentiment and modifies Trust/Emotions accordingly.
    /// The AI can influence the game's progression system through its dialogue tone.
    /// </summary>
    public static class SentimentAnalyzer
    {
        // Keywords that indicate positive/negative sentiment
        private static readonly string[] PositiveKeywords = {
            "love", "like", "happy", "great", "wonderful", "amazing", "beautiful",
            "sweet", "kind", "thank", "enjoy", "smile", "laugh", "fun", "nice",
            "cute", "handsome", "agree", "yes", "sure", "okay", "gladly",
            "pleasure", "delighted", "appreciate", "absolutely", "of course"
        };

        private static readonly string[] NegativeKeywords = {
            "hate", "angry", "mad", "upset", "disgusting", "horrible", "terrible",
            "creep", "pervert", "leave", "stop", "no", "don't", "refuse",
            "gross", "annoying", "stupid", "ugly", "rude", "pathetic",
            "get away", "go away", "shut up", "never"
        };

        private static readonly string[] VeryPositiveKeywords = {
            "love you", "i love", "kiss me", "i want you", "take me",
            "you're amazing", "so sweet", "adore", "passionate"
        };

        private static readonly string[] VeryNegativeKeywords = {
            "get out", "i hate you", "disgusting", "call the police",
            "leave now", "never talk to me", "i'm calling"
        };

        /// <summary>
        /// Analyzes text sentiment and applies Trust changes to the current NPC.
        /// Returns a feedback string describing what happened.
        /// </summary>
        public static string ApplySentiment(string text)
        {
            if (!DialogBehaviour.Instance.AllowTrustModification) return null;

            var emotions = DialogBehaviour.Instance.CurrentEmotions;
            if (emotions == null) return null;

            string lower = text.ToLower();
            float trustChange = 0f;
            string mood = "";

            // Check for very strong sentiments first
            foreach (var kw in VeryPositiveKeywords)
            {
                if (lower.Contains(kw))
                {
                    trustChange += 3f;
                    mood = "very positive";
                    break;
                }
            }

            foreach (var kw in VeryNegativeKeywords)
            {
                if (lower.Contains(kw))
                {
                    trustChange -= 5f;
                    mood = "very negative";
                    break;
                }
            }

            // Regular sentiment analysis
            if (string.IsNullOrEmpty(mood))
            {
                int positiveCount = 0;
                int negativeCount = 0;

                foreach (var kw in PositiveKeywords)
                {
                    if (lower.Contains(kw)) positiveCount++;
                }
                foreach (var kw in NegativeKeywords)
                {
                    if (lower.Contains(kw)) negativeCount++;
                }

                if (positiveCount > negativeCount && positiveCount >= 2)
                {
                    trustChange = 1f;
                    mood = "positive";
                }
                else if (negativeCount > positiveCount && negativeCount >= 2)
                {
                    trustChange = -2f;
                    mood = "negative";
                }
                else
                {
                    return null; // Neutral — no change
                }
            }

            // Apply the trust change
            emotions.AddTrust(trustChange);

            string direction = trustChange > 0 ? "📈" : "📉";
            return $"{direction} {DialogBehaviour.Instance.CurrentCharName} feels {mood} (Trust {trustChange:+0;-0;0})";
        }

        /// <summary>
        /// Applies sentiment based on an explicit style tag from the AI response.
        /// </summary>
        public static string ApplySentimentFromStyle(string style)
        {
            if (!DialogBehaviour.Instance.AllowTrustModification) return null;

            var emotions = DialogBehaviour.Instance.CurrentEmotions;
            if (emotions == null) return null;

            float trustChange = 0f;
            string lower = style.ToLower().Trim();

            switch (lower)
            {
                case "happy":
                case "aroused":
                case "flirty":
                    trustChange = 2f;
                    break;
                case "angry":
                case "annoyed":
                case "disgusted":
                    trustChange = -3f;
                    break;
                case "sad":
                case "scared":
                    trustChange = -1f;
                    break;
                case "neutral":
                case "normal":
                    trustChange = 0.5f;
                    break;
                case "shy":
                case "embarrassed":
                    trustChange = 1f;
                    break;
                default:
                    return null;
            }

            if (trustChange != 0f)
            {
                emotions.AddTrust(trustChange);
                string direction = trustChange > 0 ? "📈" : "📉";
                return $"{direction} {DialogBehaviour.Instance.CurrentCharName} reacts ({lower}) Trust {trustChange:+0;-0;0}";
            }

            return null;
        }
    }
}
