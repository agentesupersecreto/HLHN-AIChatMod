using System;
using System.Collections;
using UnityEngine;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    public enum ReactionType
    {
        Neutral,
        Positive,
        Negative,
        Sad,
        Shy
    }

    /// <summary>
    /// Manages NPC facial expressions and body animations in response to AI conversation turns.
    /// Replicates the native game behavior:
    /// - Good / Positive reaction: Character makes happy/aroused face and plays GoodDialogue animation.
    /// - Bad / Negative reaction: Character makes annoyed/angry face and plays BadAnimation (GrumpyFirst).
    /// - Sad / Shy / Neutral reactions: Matches blend shapes (Sad, Neutral, Smile) and directs eye contact.
    /// </summary>
    public static class CharacterReactionController
    {
        // Keywords for fallback sentiment analysis when no explicit style tag is provided
        private static readonly string[] PositiveWords = {
            "love", "like", "happy", "great", "wonderful", "amazing", "beautiful", "sweet",
            "thank", "enjoy", "smile", "laugh", "fun", "nice", "cute", "yes", "sure", "gladly",
            "delighted", "appreciate", "pleasure", "kiss", "darling", "honey", "babe", "sexy",
            "amo", "quiero", "gusta", "feliz", "maravilloso", "encanta", "gracias", "sonrisa",
            "lindo", "hermoso", "bello", "sí", "si", "claro", "seguro", "bueno", "placer", "beso"
        };

        private static readonly string[] NegativeWords = {
            "hate", "angry", "mad", "upset", "disgusting", "horrible", "terrible", "creep",
            "pervert", "leave", "stop", "no", "don't", "refuse", "gross", "annoying", "stupid",
            "ugly", "rude", "never", "pathetic", "get away", "shut up", "bitch", "loser",
            "odio", "enojada", "molesta", "asco", "asqueroso", "horrible", "pervertido", "vete",
            "para", "no", "basta", "deja", "estúpido", "grosero", "jamás", "nunca", "aléjate"
        };

        private static readonly string[] SadWords = {
            "sad", "cry", "crying", "hurt", "depressed", "sorry", "afraid", "scared", "lonely",
            "triste", "llorar", "lloro", "dolor", "perdón", "miedo", "sola"
        };

        /// <summary>
        /// Analyzes the style tag and dialogue text to determine the reaction category.
        /// </summary>
        public static ReactionType DetermineReaction(string style, string dialogue)
        {
            if (!string.IsNullOrEmpty(style))
            {
                string s = style.ToLower().Trim();

                if (s.Contains("happy") || s.Contains("aroused") || s.Contains("flirty") ||
                    s.Contains("pleased") || s.Contains("cheerful") || s.Contains("excited") ||
                    s.Contains("grateful") || s.Contains("amused") || s.Contains("horny"))
                {
                    return ReactionType.Positive;
                }

                if (s.Contains("angry") || s.Contains("annoyed") || s.Contains("mad") ||
                    s.Contains("disgusted") || s.Contains("furious") || s.Contains("upset") ||
                    s.Contains("irritated") || s.Contains("grumpy") || s.Contains("offended"))
                {
                    return ReactionType.Negative;
                }

                if (s.Contains("sad") || s.Contains("crying") || s.Contains("depressed") ||
                    s.Contains("scared") || s.Contains("hurt"))
                {
                    return ReactionType.Sad;
                }

                if (s.Contains("shy") || s.Contains("embarrassed") || s.Contains("blushing"))
                {
                    return ReactionType.Shy;
                }

                if (s.Contains("neutral") || s.Contains("calm") || s.Contains("thoughtful"))
                {
                    return ReactionType.Neutral;
                }
            }

            // Fallback: analyze dialogue text keywords
            if (!string.IsNullOrEmpty(dialogue))
            {
                string lower = dialogue.ToLower();
                int posCount = 0;
                int negCount = 0;
                int sadCount = 0;

                foreach (var w in PositiveWords)
                {
                    if (lower.Contains(w)) posCount++;
                }
                foreach (var w in NegativeWords)
                {
                    if (lower.Contains(w)) negCount++;
                }
                foreach (var w in SadWords)
                {
                    if (lower.Contains(w)) sadCount++;
                }

                if (sadCount > 0 && sadCount >= negCount && sadCount > posCount)
                    return ReactionType.Sad;

                if (posCount > negCount && posCount > 0)
                    return ReactionType.Positive;

                if (negCount > posCount && negCount > 0)
                    return ReactionType.Negative;
            }

            return ReactionType.Neutral;
        }

        /// <summary>
        /// Triggers facial expression and body animation on the active character.
        /// </summary>
        public static void TriggerReaction(DialogBehaviour behaviour, string style, string dialogue)
        {
            if (behaviour == null) return;

            var emotions = behaviour.CurrentEmotions;
            var nav = behaviour.CurrentNavigator;
            var talkInteractions = behaviour.CurrentTalkInteractions;

            if (emotions == null) return;

            ReactionType reaction = DetermineReaction(style, dialogue);

            int blendShapeIndex = -1;
            string animName = null;

            switch (reaction)
            {
                case ReactionType.Positive:
                    // Use Aroused blendshape (or Smile as fallback)
                    blendShapeIndex = emotions.Aroused > 0 ? emotions.Aroused : emotions.Smile;
                    // Native GoodAnimation (GoodDialogue)
                    animName = talkInteractions != null && !string.IsNullOrEmpty(talkInteractions.GoodAnimation)
                        ? talkInteractions.GoodAnimation
                        : "GoodDialogue";
                    break;

                case ReactionType.Negative:
                    // Native Annoyed blendshape
                    blendShapeIndex = emotions.Annoyed;
                    // Native BadAnimation (GrumpyFirst)
                    animName = talkInteractions != null && !string.IsNullOrEmpty(talkInteractions.BadAnimation)
                        ? talkInteractions.BadAnimation
                        : "GrumpyFirst";
                    break;

                case ReactionType.Sad:
                    blendShapeIndex = emotions.Sad > 0 ? emotions.Sad : emotions.Annoyed;
                    animName = talkInteractions != null && !string.IsNullOrEmpty(talkInteractions.BadAnimation)
                        ? talkInteractions.BadAnimation
                        : "GrumpyFirst";
                    break;

                case ReactionType.Shy:
                    blendShapeIndex = emotions.Smile > 0 ? emotions.Smile : emotions.Aroused;
                    animName = talkInteractions != null && !string.IsNullOrEmpty(talkInteractions.GoodAnimation)
                        ? talkInteractions.GoodAnimation
                        : "GoodDialogue";
                    break;

                case ReactionType.Neutral:
                default:
                    blendShapeIndex = emotions.Neutral;
                    animName = null;
                    break;
            }

            // 1. Trigger Facial Expression & Gaze
            if (blendShapeIndex >= 0)
            {
                behaviour.StartCoroutine(ApplyFacialExpressionRoutine(emotions, blendShapeIndex));
            }

            // 2. Trigger Body Animation
            if (!string.IsNullOrEmpty(animName) && nav != null)
            {
                try
                {
                    nav.PlayAnimation(animName);
                    Plugin.LogInfo($"Character reacted [{reaction}]: Anim='{animName}', BlendIndex={blendShapeIndex}");
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"Could not play reaction animation '{animName}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Coroutine that smoothly blends the facial expression and maintains eye contact with the player.
        /// </summary>
        private static IEnumerator ApplyFacialExpressionRoutine(Emotions emotions, int blendIndex)
        {
            if (emotions == null) yield break;

            var lookAt = emotions.GetComponent<RealisticEyeMovements.LookTargetController>();
            var eyes = emotions.GetComponent<RealisticEyeMovements.EyeAndHeadAnimator>();

            float startTime = Time.time;

            // Direct gaze to player's face
            if (lookAt != null)
            {
                try
                {
                    lookAt.LookAtPlayer(0.1f, 0.075f);
                }
                catch { }
            }

            // Reset any active facial blend
            if (emotions.MakingFace)
            {
                emotions.MakingFace = false;
            }

            // Trigger native MakeFace
            emotions.MakeFace(blendIndex, 100f);

            // Hold gaze during reaction
            while (Time.time < startTime + 3f)
            {
                if (emotions == null) yield break;

                if (eyes != null && !eyes.IsLookingAtFace() && lookAt != null)
                {
                    lookAt.LookAtPlayer(0.1f, 0.075f);
                }

                yield return new WaitForEndOfFrame();
            }
        }
    }
}
