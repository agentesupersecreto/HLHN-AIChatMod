using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DialogInterceptorMod.Core
{
    /// <summary>
    /// Captures a snapshot of the current NPC's stats from Emotions, TalkInteractions,
    /// NpcNavigatorAndRutines, and NpcInteractionManager to feed into the AI prompt.
    /// All character info, clothing states, and behavior states are included.
    /// </summary>
    public class CharacterContextSnapshot
    {
        public string Name = "";
        public float Trust;
        public int Submission;
        public int Perversion;
        public int Desire;
        public int Level;
        public int DaysWithoutSex;
        public int IsVaginalVirgin;
        public int IsAnalVirgin;
        public int IsOralVirgin;
        public int AmountOfVaginalSex;
        public int AmountOfAnalSex;
        public int AmountOfOralSex;
        public int PregnancyDate;
        public float HornyLevel;
        public float ValueToKiss;
        public float ValueToFollow;
        public float ValueToSex;
        public float ValueToTouch;
        public float ValueToUndress;
        public float ValueToGrab;
        public float ValueToStareAt;
        public float ValueToCare;
        public bool IsBeingFucked;
        public bool WasFuckedToday;
        public bool HadOrgasmToday;
        public int OrgasmCount;
        public bool Idle;
        public bool Saluting;

        // Talk interaction data
        public float TalkPointsToday;
        public float TalkMaxPointsToday;
        public float TalkMaxPoints;
        public bool IsRandomCharacter;

        // Navigation, routine, and scene state
        public bool IsFollowing;
        public string CurrentTaskName = "";
        public string CurrentScene = "";

        // Time and day awareness
        public int GameHour;
        public int GameMinute;
        public int GameDay;
        public bool IsNight;
        public string DayPeriod = ""; // morning, afternoon, evening, night

        // Clothing states (Slot name -> is equipped/visible)
        public Dictionary<string, bool> AvailableClothes = new Dictionary<string, bool>();
        public bool IsNaked;

        public static CharacterContextSnapshot Capture(
            Emotions emotions, 
            TalkInteractions talkInteractions, 
            NpcNavigatorAndRutines navigator, 
            NpcInteractionManager interactionManager, 
            string charName)
        {
            var snapshot = new CharacterContextSnapshot();
            snapshot.Name = charName;
            try
            {
                snapshot.CurrentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
            catch { }

            // Time awareness: read from the in-game TimeManager
            try
            {
                var timeManager = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
                if (timeManager != null)
                {
                    snapshot.GameHour = Mathf.FloorToInt(timeManager.hour);
                    snapshot.GameMinute = Mathf.FloorToInt(timeManager.minute);
                    snapshot.GameDay = timeManager.CurrentDay;
                    snapshot.IsNight = timeManager.isLateNight;

                    int h = snapshot.GameHour;
                    if (h >= 5 && h < 12)
                        snapshot.DayPeriod = "morning";
                    else if (h >= 12 && h < 17)
                        snapshot.DayPeriod = "afternoon";
                    else if (h >= 17 && h < 21)
                        snapshot.DayPeriod = "evening";
                    else
                        snapshot.DayPeriod = "night";
                }
            }
            catch { }

            if (emotions != null)
            {
                snapshot.Trust = emotions.Trust;
                snapshot.Submission = emotions.Submission;
                snapshot.Perversion = emotions.Perversion;
                snapshot.Desire = emotions.Desire;
                snapshot.Level = emotions.Level;
                snapshot.DaysWithoutSex = emotions.DaysWithoutSex;
                snapshot.IsVaginalVirgin = emotions.isVaginalVirgin;
                snapshot.IsAnalVirgin = emotions.isAnalVirgin;
                snapshot.IsOralVirgin = emotions.isOralVirgin;
                snapshot.AmountOfVaginalSex = emotions.AmountOfVaginalSex;
                snapshot.AmountOfAnalSex = emotions.AmountOfAnalSex;
                snapshot.AmountOfOralSex = emotions.AmountOfOralSex;
                snapshot.PregnancyDate = emotions.PregnancyDate;
                snapshot.HornyLevel = emotions.HornyLevel;
                snapshot.ValueToKiss = emotions.ValueToKiss;
                snapshot.ValueToFollow = emotions.ValueToFollow;
                snapshot.ValueToSex = emotions.ValueToSex;
                snapshot.ValueToTouch = emotions.ValueToTouch;
                snapshot.ValueToUndress = emotions.ValueToUndress;
                snapshot.ValueToGrab = emotions.ValueToGrab;
                snapshot.ValueToStareAt = emotions.ValueToStareAt;
                snapshot.ValueToCare = emotions.ValueToCare;
                snapshot.IsBeingFucked = emotions.isBeingFucked;
                snapshot.WasFuckedToday = emotions.wasFuckedToday;
                snapshot.HadOrgasmToday = emotions.hadOrgasmToday;
                snapshot.OrgasmCount = emotions.OrgasmCount;
                snapshot.Idle = emotions.Idle;
                snapshot.Saluting = emotions.saluting;
            }

            if (talkInteractions != null)
            {
                snapshot.TalkPointsToday = talkInteractions.PointsToday;
                snapshot.TalkMaxPointsToday = talkInteractions.MaxPointsToday;
                snapshot.TalkMaxPoints = talkInteractions.MaxPoints;
                // Katherine, Susan, Stacy, and Laura are always main characters, never random
                snapshot.IsRandomCharacter = talkInteractions.IsARandomCharacter && !IsMainCharacterName(charName);
            }
            else
            {
                snapshot.IsRandomCharacter = !IsMainCharacterName(charName);
            }

            if (navigator != null)
            {
                snapshot.IsFollowing = navigator.Follow;
                if (navigator.CurrentTask != null && navigator.CurrentTask.chore != null)
                {
                    snapshot.CurrentTaskName = navigator.CurrentTask.Desc ?? navigator.CurrentTask.chore.AnimationName;
                }
            }

            if (interactionManager != null)
            {
                snapshot.IsNaked = interactionManager.IsNaked;
                if (interactionManager.Dress != null)
                    snapshot.AvailableClothes["dress"] = interactionManager.Dress.activeSelf;
                if (interactionManager.Shirt != null)
                    snapshot.AvailableClothes["shirt"] = interactionManager.Shirt.activeSelf;
                if (interactionManager.Pants != null)
                    snapshot.AvailableClothes["pants"] = interactionManager.Pants.activeSelf;
                if (interactionManager.Skirt != null)
                    snapshot.AvailableClothes["skirt"] = interactionManager.Skirt.activeSelf;
                if (interactionManager.Bra != null)
                    snapshot.AvailableClothes["bra"] = interactionManager.Bra.activeSelf;
                if (interactionManager.Panty != null)
                    snapshot.AvailableClothes["panty"] = interactionManager.Panty.activeSelf;
                if (interactionManager.SocksStocking != null)
                    snapshot.AvailableClothes["socks"] = interactionManager.SocksStocking.activeSelf;
                if (interactionManager.Shoes != null)
                    snapshot.AvailableClothes["shoes"] = interactionManager.Shoes.activeSelf;
                if (interactionManager.Jacket != null)
                    snapshot.AvailableClothes["jacket"] = interactionManager.Jacket.activeSelf;
                if (interactionManager.Belt != null)
                    snapshot.AvailableClothes["belt"] = interactionManager.Belt.activeSelf;
                if (interactionManager.Glasses != null)
                    snapshot.AvailableClothes["glasses"] = interactionManager.Glasses.activeSelf;
                if (interactionManager.Gloves != null)
                    snapshot.AvailableClothes["gloves"] = interactionManager.Gloves.activeSelf;
                if (interactionManager.Hat != null)
                    snapshot.AvailableClothes["hat"] = interactionManager.Hat.activeSelf;
                if (interactionManager.Collar != null)
                    snapshot.AvailableClothes["collar"] = interactionManager.Collar.activeSelf;
            }

            return snapshot;
        }

        /// <summary>
        /// Checks whether the character name corresponds to one of the four main characters:
        /// Katherine, Susan, Stacy, or Laura.
        /// </summary>
        public static bool IsMainCharacterName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.Trim();
            return n.IndexOf("Katherine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("Susan", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("Stacy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("Laura", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Generates a compact prompt block with all character stats, clothing status, and action states.
        /// </summary>
        public string ToPromptBlock()
        {
            if (string.IsNullOrEmpty(Name))
                return "";

            var sb = new StringBuilder();
            sb.AppendLine($"[CHARACTER] {Name}");
            if (!string.IsNullOrEmpty(CurrentScene))
                sb.AppendLine($"[LOCATION] Current Scene / Map: {CurrentScene}");

            // Time context
            if (GameHour > 0 || GameMinute > 0 || GameDay > 0)
            {
                string timeStr = $"{GameHour:D2}:{GameMinute:D2}";
                string periodStr = !string.IsNullOrEmpty(DayPeriod) ? $" ({DayPeriod})" : "";
                string nightStr = IsNight ? " [IT IS NIGHTTIME]" : "";
                sb.AppendLine($"[TIME] Day {GameDay}, {timeStr}{periodStr}{nightStr}");
            }

            // Who she is: the game has four main characters living in this house (Katherine, Susan,
            // Stacy, and Laura) and the rest are secondary women of the neighbourhood.
            bool isMain = IsMainCharacterName(Name) || !IsRandomCharacter;
            sb.AppendLine(isMain
                ? "[ROLE] One of the four women who live in this house with the man (the player): Katherine, Susan, Stacy and Laura."
                : "[ROLE] A secondary character of the neighbourhood, NOT one of the four women who live in this house (Katherine, Susan, Stacy, Laura). Do not claim their home, their routine or their relationship with the player as your own.");
            sb.AppendLine($"[STATS] Trust:{Trust:F0} Level:{Level} Submission:{Submission} Perversion:{Perversion} Desire:{Desire}");
            sb.AppendLine($"[AROUSAL] HornyLevel:{HornyLevel:F0} IsBeingFucked:{IsBeingFucked} WasFuckedToday:{WasFuckedToday} HadOrgasmToday:{HadOrgasmToday} Orgasms:{OrgasmCount}");

            // Virginity / Experience
            string vagVirginity = IsVaginalVirgin == 1 ? "Virgin" : $"Experienced({AmountOfVaginalSex}x)";
            string analVirginity = IsAnalVirgin == 1 ? "Virgin" : $"Experienced({AmountOfAnalSex}x)";
            string oralVirginity = IsOralVirgin == 1 ? "Virgin" : $"Experienced({AmountOfOralSex}x)";
            sb.AppendLine($"[EXPERIENCE] Vaginal:{vagVirginity} Anal:{analVirginity} Oral:{oralVirginity} DaysWithoutSex:{DaysWithoutSex}");

            if (PregnancyDate > 0)
                sb.AppendLine($"[PREGNANCY] Day:{PregnancyDate}");

            // Thresholds — the AI needs to know what trust level unlocks actions
            sb.AppendLine($"[ACTION THRESHOLDS & REQUIREMENTS]");
            sb.AppendLine($"- Follow player: Trust >= {ValueToFollow:F0} (Current Trust: {Trust:F0} -> {(Trust >= ValueToFollow ? "MEETS THRESHOLD" : "BELOW THRESHOLD")})");
            sb.AppendLine($"- Undress/Clothes: Trust >= {ValueToUndress:F0} (Current Trust: {Trust:F0} -> {(Trust >= ValueToUndress ? "MEETS THRESHOLD" : "BELOW THRESHOLD")})");
            sb.AppendLine($"- Kiss: Trust >= {ValueToKiss:F0} (Current Trust: {Trust:F0} -> {(Trust >= ValueToKiss ? "MEETS THRESHOLD" : "BELOW THRESHOLD")})");
            sb.AppendLine($"- Touch/Sex: Touch:{ValueToTouch:F0} Sex:{ValueToSex:F0} Grab:{ValueToGrab:F0}");

            // Movement and clothing status
            sb.AppendLine($"[CURRENT BEHAVIOR & CLOTHES]");
            sb.AppendLine($"- Currently Following: {(IsFollowing ? "YES (following player right now)" : "NO")}");
            if (!string.IsNullOrEmpty(CurrentTaskName))
                sb.AppendLine($"- Current Activity: {CurrentTaskName}");

            if (AvailableClothes.Count > 0)
            {
                sb.AppendLine("- Clothes on your body:");
                foreach (var kvp in AvailableClothes)
                {
                    sb.AppendLine($"  * {kvp.Key}: {(kvp.Value ? "WORN (ON)" : "REMOVED (OFF)")}");
                }
            }
            sb.AppendLine($"- Is Naked: {(IsNaked ? "YES" : "NO")}");

            return sb.ToString();
        }
    }
}
