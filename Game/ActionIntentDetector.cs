using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    public enum ActionType
    {
        None,
        FollowStart,
        FollowStop,
        RemoveCloth,
        PutCloth,
        Kiss,
        GoToPlace
    }

    /// <summary>
    /// Heuristic intent detector for player chat messages.
    ///
    /// The AI model itself understands ANY language (English, Spanish, Portuguese, Russian,
    /// German, etc.), and it always receives the player's raw message. These keyword lists
    /// (English + Spanish synonyms) exist only to add explicit trust/clothing threshold
    /// guidance when a request is recognized. When nothing matches, no guidance is emitted
    /// and the AI still decides on its own.
    /// </summary>
    public static class ActionIntentDetector
    {
        public static string DetectIntentGuidance(string userMessage, CharacterContextSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(userMessage) || snapshot == null)
                return "";

            string normalized = NormalizeText(userMessage);

            // 1. Follow / Stop following
            if (IsFollowStop(normalized))
            {
                var sb = new StringBuilder();
                sb.AppendLine("\n[DETECTED PLAYER REQUEST: STOP FOLLOWING]");
                sb.AppendLine($"- Currently following: {(snapshot.IsFollowing ? "YES" : "NO")}");
                sb.AppendLine("- The player is asking you to stop following them / stay where you are.");
                sb.AppendLine("- DECISION: If you agree to stay, include: [CMD: follow:stop]. If you insist on staying with them, do NOT include the command.");
                return sb.ToString();
            }

            if (IsFollowStart(normalized))
            {
                var sb = new StringBuilder();
                sb.AppendLine("\n[DETECTED PLAYER REQUEST: FOLLOW HIM]");
                sb.AppendLine($"- Currently following: {(snapshot.IsFollowing ? "YES" : "NO")}");
                sb.AppendLine($"- Required Trust threshold: {snapshot.ValueToFollow:F0} (Your Trust: {snapshot.Trust:F0} -> {(snapshot.Trust >= snapshot.ValueToFollow ? "MEETS THRESHOLD" : "BELOW THRESHOLD")})");
                sb.AppendLine("- DECISION: Based on your trust, current activity, and mood, decide whether to follow him or refuse.");
                sb.AppendLine("  * IF YOU ACCEPT: Include command [CMD: follow:start] in your response.");
                sb.AppendLine("  * IF YOU REFUSE: Do NOT include the command. Explain why in your dialogue in character.");
                return sb.ToString();
            }

            // 2. Go to a place in the house ("go to the kitchen", "vete a la cocina", "lie in bed")
            if (PlaceMatcher.IsGoToRequest(userMessage) || PlaceMatcher.IsPlacesQuestion(userMessage))
                return BuildGoToGuidance(userMessage, snapshot);

            // 3. Kiss
            if (IsKiss(normalized))
            {
                var sb = new StringBuilder();
                sb.AppendLine("\n[DETECTED PLAYER REQUEST: KISS]");
                sb.AppendLine($"- Required Trust threshold: {snapshot.ValueToKiss:F0} (Your Trust: {snapshot.Trust:F0} -> {(snapshot.Trust >= snapshot.ValueToKiss ? "MEETS THRESHOLD" : "BELOW THRESHOLD")})");
                sb.AppendLine("- DECISION: Decide whether to kiss him based on your feelings, trust, and intimacy level.");
                sb.AppendLine("  * IF YOU ACCEPT: Include command [CMD: kiss] in your response.");
                sb.AppendLine("  * IF YOU REFUSE: Do NOT include the command. React appropriately (shy, dismissive, or angry) in dialogue.");
                return sb.ToString();
            }

            // 4. Remove Clothes
            string removeSlot = DetectClothSlot(normalized, isRemoval: true);
            if (!string.IsNullOrEmpty(removeSlot))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"\n[DETECTED PLAYER REQUEST: REMOVE CLOTHING ({removeSlot.ToUpper()})]");

                bool hasCloth = snapshot.AvailableClothes.ContainsKey(removeSlot);
                bool isWorn = hasCloth && snapshot.AvailableClothes[removeSlot];

                if (!hasCloth && removeSlot != "all")
                {
                    sb.AppendLine($"- IMPORTANT: You do NOT have a '{removeSlot}' in your current outfit! Point out in your dialogue what you are actually wearing instead.");
                    sb.AppendLine("- Do NOT output any clothing command.");
                }
                else if (!isWorn && removeSlot != "all")
                {
                    sb.AppendLine($"- Note: Your '{removeSlot}' is ALREADY removed (off). Point this out in your response.");
                    sb.AppendLine("- Do NOT output remove command since it's already off.");
                }
                else
                {
                    sb.AppendLine($"- Required Trust threshold: {snapshot.ValueToUndress:F0} (Your Trust: {snapshot.Trust:F0} -> {(snapshot.Trust >= snapshot.ValueToUndress ? "MEETS THRESHOLD" : "BELOW THRESHOLD")})");
                    sb.AppendLine($"- Submission: {snapshot.Submission}, Perversion: {snapshot.Perversion}, Desire: {snapshot.Desire}");
                    sb.AppendLine($"- DECISION: Decide whether to take off your {removeSlot} based on your trust, modesty, and arousal.");
                    sb.AppendLine($"  * IF YOU ACCEPT: Include command [CMD: remove_cloth:{removeSlot}] in your response.");
                    sb.AppendLine($"  * IF YOU REFUSE: Do NOT include the command. Express your refusal in character.");
                }
                return sb.ToString();
            }

            // 5. Put Clothes Back On
            string putSlot = DetectClothSlot(normalized, isRemoval: false);
            if (!string.IsNullOrEmpty(putSlot))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"\n[DETECTED PLAYER REQUEST: PUT ON CLOTHING ({putSlot.ToUpper()})]");

                bool hasCloth = snapshot.AvailableClothes.ContainsKey(putSlot);
                bool isWorn = hasCloth && snapshot.AvailableClothes[putSlot];

                if (!hasCloth && putSlot != "all")
                {
                    sb.AppendLine($"- Note: You do not have '{putSlot}' in your wardrobe.");
                }
                else if (isWorn && putSlot != "all")
                {
                    sb.AppendLine($"- Note: You are ALREADY wearing your '{putSlot}'.");
                }
                else
                {
                    sb.AppendLine($"- DECISION: If you agree to put on your {putSlot}, include command [CMD: put_cloth:{putSlot}].");
                }
                return sb.ToString();
            }

            return "";
        }

        /// <summary>
        /// Guidance for "take me / go to a place" requests. The place names come from the game's own
        /// routine chores (plus locations.txt), because the model cannot guess how the house names
        /// its spots. Movement is gated by the same trust value as following.
        /// </summary>
        private static string BuildGoToGuidance(string userMessage, CharacterContextSnapshot snapshot)
        {
            var sb = new StringBuilder();
            bool wantsList = PlaceMatcher.IsPlacesQuestion(userMessage);
            string phrase = PlaceMatcher.ExtractPlacePhrase(userMessage);
            bool meetsThreshold = snapshot.Trust >= snapshot.ValueToFollow;

            sb.AppendLine(wantsList
                ? "\n[DETECTED PLAYER REQUEST: WHERE CAN YOU GO]"
                : "\n[DETECTED PLAYER REQUEST: GO SOMEWHERE IN THE HOUSE]");

            if (!string.IsNullOrEmpty(phrase))
                sb.AppendLine($"- Place the player seems to be asking for: \"{phrase}\"");

            string places = GetKnownPlaces();
            if (!string.IsNullOrEmpty(places))
                sb.AppendLine($"- Places available in this house right now: {places}");
            else
                sb.AppendLine("- No place list available (no NPC tracked right now).");

            sb.AppendLine($"- Required Trust threshold for going somewhere on his word: {snapshot.ValueToFollow:F0} (Your Trust: {snapshot.Trust:F0} -> {(meetsThreshold ? "MEETS THRESHOLD" : "BELOW THRESHOLD")})");
            sb.AppendLine($"- Submission: {snapshot.Submission}, Desire: {snapshot.Desire}");

            if (wantsList)
            {
                sb.AppendLine("- The player wants to know where you can go: answer with the places above, in character.");
                sb.AppendLine("- You may also include [CMD: places] so the game chat prints the list.");
                return sb.ToString();
            }

            sb.AppendLine("- DECISION: Only if the player really asked you to move to one of the places above AND you are willing to go:");
            sb.AppendLine("  * IF YOU ACCEPT: include [CMD: goto:PLACE], where PLACE is copied from the list above character for character, nothing else after it (the house accepts both its rooms and its chores: [CMD: goto:Do laundry], [CMD: goto:Sweeping Kitchen]). Optionally how long you stay, in in-game hours: [CMD: goto:Do laundry:2].");
            sb.AppendLine("  * IF YOU REFUSE, or it was not a real request to move: do NOT include the command, just answer in character (annoyed, busy, shy...).");
            return sb.ToString();
        }

        /// <summary>Known places for the current NPC, defensively (the detector runs on every turn).</summary>
        private static string GetKnownPlaces()
        {
            try
            {
                // The list is the AI's menu: the houses expose around twenty places, so the cap is
                // only a guard against a pathological locations.txt.
                string list = MovementHelper.GetAvailableGoToTargets();
                if (string.IsNullOrEmpty(list)) return "";
                return list.Length > 700 ? list.Substring(0, 700) : list;
            }
            catch (Exception ex)
            {
                Plugin.LogDebug($"Could not list places for the prompt: {ex.Message}");
                return "";
            }
        }

        private static bool IsFollowStart(string text)
        {
            // Spanish keywords (data, not code: kept so non-English players are detected)
            if (text.Contains("sigueme") || text.Contains("ven conmigo") || text.Contains("acompaname") || 
                text.Contains("camina conmigo") || text.Contains("vamos conmigo") || text.Contains("ven aca") || text.Contains("ven aqui"))
                return true;

            // English keywords
            if (text.Contains("follow me") || text.Contains("come with me") || text.Contains("walk with me") || 
                text.Contains("come along") || text.Contains("follow") || text.Contains("tag along"))
                return true;

            return false;
        }

        private static bool IsFollowStop(string text)
        {
            // Spanish keywords (data, not code)
            if (text.Contains("deja de seguirme") || text.Contains("no me sigas") || text.Contains("quedate aqui") || 
                text.Contains("espera aqui") || text.Contains("detente") || text.Contains("para de seguirme"))
                return true;

            // English keywords
            if (text.Contains("stop following") || text.Contains("dont follow") || text.Contains("don't follow") || 
                text.Contains("stay here") || text.Contains("wait here") || text.Contains("unfollow"))
                return true;

            return false;
        }

        private static bool IsKiss(string text)
        {
            // Spanish keywords (data, not code)
            if (text.Contains("besame") || text.Contains("dame un beso") || text.Contains("un beso") || 
                text.Contains("quiero besarte") || text.Contains("te puedo besar"))
                return true;

            // English keywords
            if (text.Contains("kiss me") || text.Contains("give me a kiss") || text.Contains("can i kiss you") || 
                text.Contains("kiss you"))
                return true;

            return false;
        }

        private static string DetectClothSlot(string text, bool isRemoval)
        {
            bool hasRemovalVerb = isRemoval && (
                text.Contains("quitate") || text.Contains("sacate") || text.Contains("desvistete") || 
                text.Contains("bajate") || text.Contains("remove") || text.Contains("take off") || 
                text.Contains("strip") || text.Contains("undress") || text.Contains("pull down") || 
                text.Contains("drop") || text.Contains("sin ropa") || text.Contains("desnuda") || text.Contains("naked")
            );

            bool hasPutVerb = !isRemoval && (
                text.Contains("ponte") || text.Contains("vistete") || text.Contains("colocate") || 
                text.Contains("subete") || text.Contains("put on") || text.Contains("wear") || 
                text.Contains("pull up") || text.Contains("dress up")
            );

            if (!hasRemovalVerb && !hasPutVerb)
                return null;

            // Check slots
            if (text.Contains("vestido") || ContainsWord(text, "dress") || ContainsWord(text, "dresses")) // "undress" is not "dress"
                return "dress";
            if (text.Contains("panti") || text.Contains("panties") || text.Contains("panty") || text.Contains("calzon") || text.Contains("calzones") || text.Contains("bragas") || text.Contains("tanga") || text.Contains("underwear"))
                return "panty";
            if (ContainsWord(text, "bra") || ContainsWord(text, "bras") || text.Contains("sosten") || text.Contains("sujetador") || text.Contains("brasier"))
                return "bra";
            if (text.Contains("camisa") || text.Contains("shirt") || text.Contains("blusa") || text.Contains("remera") || ContainsWord(text, "top") || ContainsWord(text, "tops"))
                return "shirt";
            if (text.Contains("pantalon") || text.Contains("pantalones") || text.Contains("pants") || text.Contains("jeans"))
                return "pants";
            if (text.Contains("falda") || text.Contains("skirt") || text.Contains("pollera"))
                return "skirt";
            if (text.Contains("calcetines") || text.Contains("medias") || text.Contains("calcetas") || text.Contains("socks") || text.Contains("stockings"))
                return "socks";
            if (text.Contains("zapatos") || text.Contains("shoes") || text.Contains("tacones") || text.Contains("calzado"))
                return "shoes";
            if (text.Contains("chaqueta") || text.Contains("jacket") || text.Contains("abrigo") || text.Contains("campera"))
                return "jacket";
            if (text.Contains("cinturon") || text.Contains("belt"))
                return "belt";
            if (text.Contains("lentes") || text.Contains("gafas") || text.Contains("glasses"))
                return "glasses";
            if (text.Contains("guantes") || text.Contains("gloves"))
                return "gloves";
            if (text.Contains("sombrero") || text.Contains("gorro") || ContainsWord(text, "hat") || ContainsWord(text, "hats"))
                return "hat";
            if (text.Contains("collar"))
                return "collar";
            // Whole-word checks: "all" must not match "call"/"small", and "undress"/"strip"
            // mean everything rather than the dress slot. "todos los"/"todas las" are matched
            // as phrases so "todo" doesn't fire on "hola a todos" (everyone).
            if (ContainsWord(text, "all") || ContainsWord(text, "everything") ||
                ContainsWord(text, "todo") || ContainsWord(text, "toda") ||
                ContainsWord(text, "todos los") || ContainsWord(text, "todas las") ||
                ContainsWord(text, "completamente") || ContainsWord(text, "desnuda") ||
                ContainsWord(text, "naked") || ContainsWord(text, "undress") || ContainsWord(text, "strip"))
                return "all";

            return null;
        }

        /// <summary>
        /// Whole-word containment check. NormalizeText() lowercases the message and turns
        /// punctuation into spaces, so padding both sides makes a short token match only as
        /// its own word: "all" no longer matches "call"/"small", "hat" not "that",
        /// "dress" not "undress". Phrases ("todos los") work the same way.
        /// </summary>
        private static bool ContainsWord(string text, string word)
        {
            return (" " + text + " ").Contains(" " + word + " ");
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string lower = text.ToLowerInvariant();

            // Replace accented characters
            lower = lower.Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n");
            lower = lower.Replace("à", "a").Replace("è", "e").Replace("ì", "i").Replace("ò", "o").Replace("ù", "u");

            // Remove common punctuation
            lower = Regex.Replace(lower, @"[¿?¡!.,;:""'()\[\]{}]", " ");
            return lower.Trim();
        }
    }
}
