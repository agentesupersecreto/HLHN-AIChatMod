using System;
using UnityEngine;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Executes commands embedded in AI responses.
    /// Commands allow the AI to trigger native in-game actions:
    /// - Follow / stop following (NpcNavigatorAndRutines.MakeNpcFollowMe)
    /// - Clothes put/remove for all slots (NpcInteractionManager)
    /// - Kiss (Emotions.NpcKiss)
    /// - Emotions, Trust, facial expressions
    /// </summary>
    public static class CommandExecutor
    {
        /// <summary>
        /// Parses and executes a single command string.
        /// Returns a human-readable feedback string.
        /// </summary>
        public static string ExecuteCommand(string command, Action<string, bool> setStatus, Action<string, bool> showEmotion)
        {
            try
            {
                string cmd = command.Trim().ToLowerInvariant();
                Plugin.LogInfo($"Executing command: {cmd}");
                var db = DialogBehaviour.Instance;

                // 1. Follow / Unfollow
                if (cmd == "follow:start" || cmd == "follow:on" || cmd == "follow:true" || cmd == "follow")
                {
                    if (!db.CmdFollowEnabled) return "[CMD disabled: follow]";
                    return ExecuteFollow(true);
                }
                if (cmd == "follow:stop" || cmd == "follow:off" || cmd == "follow:false" || cmd == "unfollow")
                {
                    if (!db.CmdFollowEnabled) return "[CMD disabled: follow]";
                    return ExecuteFollow(false);
                }

                // 2. Remove / Put on clothes
                if (cmd.StartsWith("remove_cloth:") || cmd.StartsWith("remove:") || cmd.StartsWith("take_off:"))
                {
                    if (!db.CmdClothesEnabled) return "[CMD disabled: clothes]";
                    string slot = cmd.Substring(cmd.IndexOf(':') + 1);
                    return ExecuteClothAction(slot, remove: true);
                }
                if (cmd.StartsWith("put_cloth:") || cmd.StartsWith("put:") || cmd.StartsWith("wear:"))
                {
                    if (!db.CmdClothesEnabled) return "[CMD disabled: clothes]";
                    string slot = cmd.Substring(cmd.IndexOf(':') + 1);
                    return ExecuteClothAction(slot, remove: false);
                }

                // 3. Kiss ("beso" is a Spanish alias, kept for models answering in another language)
                if (cmd == "kiss" || cmd == "beso")
                {
                    if (!db.CmdKissEnabled) return "[CMD disabled: kiss]";
                    return ExecuteKiss();
                }

                // 4. Modify Trust
                if (cmd.StartsWith("modify_trust:"))
                {
                    if (!db.CmdTrustModEnabled) return "[CMD disabled: modify_trust]";
                    return ExecuteModifyTrust(cmd.Substring(13));
                }

                // 5. Modify Emotion / Stats
                if (cmd.StartsWith("modify_emotion:"))
                {
                    if (!db.CmdEmotionModEnabled) return "[CMD disabled: modify_emotion]";
                    return ExecuteModifyEmotion(cmd.Substring(15));
                }

                // 6. Facial Expressions
                if (cmd.StartsWith("make_face:"))
                {
                    if (!db.CmdMakeFaceEnabled) return "[CMD disabled: make_face]";
                    return ExecuteMakeFace(cmd.Substring(10));
                }

                // 7. Force in-game speech
                if (cmd.StartsWith("say:"))
                {
                    if (!db.CmdSayEnabled) return "[CMD disabled: say]";
                    return ExecuteSay(cmd.Substring(4));
                }

                Plugin.LogDebug($"Unknown command: {command}");
                return $"Unknown command: {command}";
            }
            catch (Exception ex)
            {
                Plugin.LogError($"Command execution error: {ex.Message}");
                return $"Command error: {ex.Message}";
            }
        }

        private static string ExecuteFollow(bool start)
        {
            var navigator = DialogBehaviour.Instance.CurrentNavigator;
            if (navigator == null) return "No NPC navigator available.";

            string charName = DialogBehaviour.Instance.CurrentCharName;

            if (start)
            {
                if (!navigator.Follow)
                {
                    navigator.MakeNpcFollowMe();
                    return $"{charName} is now following you.";
                }
                return $"{charName} is already following you.";
            }
            else
            {
                if (navigator.Follow)
                {
                    navigator.MakeNpcFollowMe();
                    return $"{charName} stopped following you.";
                }
                return $"{charName} is not following you.";
            }
        }

        private static string ExecuteKiss()
        {
            var mgr = DialogBehaviour.Instance.CurrentInteractionManager;
            var emotions = DialogBehaviour.Instance.CurrentEmotions;
            string charName = DialogBehaviour.Instance.CurrentCharName;

            if (mgr != null)
            {
                mgr.KissHer();
                return $"{charName} kissed you!";
            }
            if (emotions != null)
            {
                emotions.NpcKiss();
                return $"{charName} kissed you!";
            }
            return "No NPC available for kiss.";
        }

        private static string ExecuteClothAction(string slot, bool remove)
        {
            var mgr = DialogBehaviour.Instance.CurrentInteractionManager;
            if (mgr == null) return "No NPC interaction manager available.";

            slot = ToCanonicalSlot(slot.Trim().ToLowerInvariant());
            bool canAnimate = mgr.navigator != null && (!mgr.navigator.Working || mgr.navigator.PauseWork) && !mgr.removing;

            switch (slot)
            {
                case "dress":
                    if (mgr.Dress != null && mgr.Dress.activeSelf == remove)
                    {
                        if (canAnimate) mgr.PutRemoveDress(); else mgr.PutRemoveDressNOA();
                        return $"Dress {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Dress == null ? "Character has no dress." : $"Dress is already {(remove ? "removed" : "on")}.";

                case "shirt":
                case "top":
                    if (mgr.Shirt != null && mgr.Shirt.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveShirt(); else mgr.PutRemoveShirtNOA();
                        return $"Shirt {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Shirt == null ? "Character has no shirt." : $"Shirt is already {(remove ? "removed" : "on")}.";

                case "pants":
                case "jeans":
                    if (mgr.Pants != null && mgr.Pants.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemovePants(); else mgr.PutRemovePantsNOA();
                        return $"Pants {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Pants == null ? "Character has no pants." : $"Pants are already {(remove ? "removed" : "on")}.";

                case "skirt":
                    if (mgr.Skirt != null && mgr.Skirt.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveSkirt(); else mgr.PutRemoveSkirtNOA();
                        return $"Skirt {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Skirt == null ? "Character has no skirt." : $"Skirt is already {(remove ? "removed" : "on")}.";

                case "bra":
                    if (mgr.Bra != null && mgr.Bra.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveBra(); else mgr.PutRemoveBraNOA();
                        return $"Bra {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Bra == null ? "Character has no bra." : $"Bra is already {(remove ? "removed" : "on")}.";

                case "panty":
                case "panties":
                case "underwear":
                    if (mgr.Panty != null && mgr.Panty.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemovePanty(); else mgr.PutRemovePantyNOA();
                        return $"Panty {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Panty == null ? "Character has no panties." : $"Panty is already {(remove ? "removed" : "on")}.";

                case "socks":
                case "stockings":
                    if (mgr.SocksStocking != null && mgr.SocksStocking.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveSocks(); else mgr.PutRemoveSocksNOA();
                        return $"Socks {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.SocksStocking == null ? "Character has no socks." : $"Socks are already {(remove ? "removed" : "on")}.";

                case "shoes":
                    if (mgr.Shoes != null && mgr.Shoes.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveShoes(); else mgr.PutRemoveShoesNOA();
                        return $"Shoes {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Shoes == null ? "Character has no shoes." : $"Shoes are already {(remove ? "removed" : "on")}.";

                case "jacket":
                    if (mgr.Jacket != null && mgr.Jacket.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveJacket(); else mgr.RemoveJacketNOA();
                        return $"Jacket {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Jacket == null ? "Character has no jacket." : $"Jacket is already {(remove ? "removed" : "on")}.";

                case "belt":
                    if (mgr.Belt != null && mgr.Belt.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveBelt(); else mgr.RemoveBeltNOA();
                        return $"Belt {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Belt == null ? "Character has no belt." : $"Belt is already {(remove ? "removed" : "on")}.";

                case "glasses":
                    if (mgr.Glasses != null && mgr.Glasses.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveGlasses(); else mgr.RemoveGlassesNOA();
                        return $"Glasses {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Glasses == null ? "Character has no glasses." : $"Glasses are already {(remove ? "removed" : "on")}.";

                case "gloves":
                    if (mgr.Gloves != null && mgr.Gloves.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveGloves(); else mgr.RemoveGlovesNOA();
                        return $"Gloves {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Gloves == null ? "Character has no gloves." : $"Gloves are already {(remove ? "removed" : "on")}.";

                case "hat":
                    if (mgr.Hat != null && mgr.Hat.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveHat(); else mgr.RemoveHatNOA();
                        return $"Hat {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Hat == null ? "Character has no hat." : $"Hat is already {(remove ? "removed" : "on")}.";

                case "collar":
                    if (mgr.Collar != null && mgr.Collar.activeSelf == remove)
                    {
                        if (canAnimate) mgr.RemoveCollar(); else mgr.RemoveCollarNOA();
                        return $"Collar {(remove ? "removed" : "put on")}.";
                    }
                    return mgr.Collar == null ? "Character has no collar." : $"Collar is already {(remove ? "removed" : "on")}.";

                case "all":
                    int count = 0;
                    if (mgr.Dress != null && mgr.Dress.activeSelf == remove) { mgr.PutRemoveDressNOA(); count++; }
                    if (mgr.Shirt != null && mgr.Shirt.activeSelf == remove) { mgr.PutRemoveShirtNOA(); count++; }
                    if (mgr.Pants != null && mgr.Pants.activeSelf == remove) { mgr.PutRemovePantsNOA(); count++; }
                    if (mgr.Skirt != null && mgr.Skirt.activeSelf == remove) { mgr.PutRemoveSkirtNOA(); count++; }
                    if (mgr.Bra != null && mgr.Bra.activeSelf == remove) { mgr.PutRemoveBraNOA(); count++; }
                    if (mgr.Panty != null && mgr.Panty.activeSelf == remove) { mgr.PutRemovePantyNOA(); count++; }
                    if (mgr.SocksStocking != null && mgr.SocksStocking.activeSelf == remove) { mgr.PutRemoveSocksNOA(); count++; }
                    if (mgr.Shoes != null && mgr.Shoes.activeSelf == remove) { mgr.PutRemoveShoesNOA(); count++; }
                    if (mgr.Jacket != null && mgr.Jacket.activeSelf == remove) { mgr.RemoveJacketNOA(); count++; }
                    if (mgr.Belt != null && mgr.Belt.activeSelf == remove) { mgr.RemoveBeltNOA(); count++; }
                    if (mgr.Glasses != null && mgr.Glasses.activeSelf == remove) { mgr.RemoveGlassesNOA(); count++; }
                    if (mgr.Gloves != null && mgr.Gloves.activeSelf == remove) { mgr.RemoveGlovesNOA(); count++; }
                    if (mgr.Hat != null && mgr.Hat.activeSelf == remove) { mgr.RemoveHatNOA(); count++; }
                    if (mgr.Collar != null && mgr.Collar.activeSelf == remove) { mgr.RemoveCollarNOA(); count++; }
                    return $"{(remove ? "Removed" : "Put on")} {count} clothing items.";

                default:
                    return $"Unknown cloth slot: {slot}";
            }
        }

        /// <summary>
        /// Maps cloth-slot spelling variants to the canonical English slot names used by the
        /// command protocol. The Spanish synonyms are kept as data (not code) so that models
        /// answering in another language, or prompts written before the switch to English
        /// canonical names, still resolve to the right slot.
        /// </summary>
        private static string ToCanonicalSlot(string slot)
        {
            switch (slot)
            {
                case "vestido":
                    return "dress";
                case "camisa":
                case "blusa":
                    return "shirt";
                case "pantalon":
                case "pantalones":
                    return "pants";
                case "falda":
                    return "skirt";
                case "sosten":
                case "sujetador":
                case "brasier":
                    return "bra";
                case "calzon":
                case "calzones":
                case "bragas":
                    return "panty";
                case "medias":
                case "calcetines":
                    return "socks";
                case "zapatos":
                    return "shoes";
                case "chaqueta":
                case "abrigo":
                    return "jacket";
                case "cinturon":
                    return "belt";
                case "lentes":
                case "gafas":
                    return "glasses";
                case "guantes":
                    return "gloves";
                case "sombrero":
                    return "hat";
                case "todo":
                    return "all";
                default:
                    return slot;
            }
        }

        private static string ExecuteModifyTrust(string valueStr)
        {
            if (!DialogBehaviour.Instance.AllowTrustModification)
                return "Trust modification disabled.";

            var emotions = DialogBehaviour.Instance.CurrentEmotions;
            if (emotions == null) return "No NPC to modify.";

            if (float.TryParse(valueStr, out float value))
            {
                emotions.AddTrust(value);
                return $"Trust modified by {value:+0;-0;0} (now {emotions.Trust:F0})";
            }
            return $"Invalid trust value: {valueStr}";
        }

        private static string ExecuteModifyEmotion(string args)
        {
            var emotions = DialogBehaviour.Instance.CurrentEmotions;
            if (emotions == null) return "No NPC to modify.";

            string[] parts = args.Split(':');
            if (parts.Length < 2) return $"Invalid emotion format: {args}";

            string emotionName = parts[0].Trim();
            if (!float.TryParse(parts[1].Trim(), out float value))
                return $"Invalid emotion value: {parts[1]}";

            switch (emotionName)
            {
                case "submission":
                    emotions.Submission = Mathf.Clamp(emotions.Submission + (int)value, 0, 100);
                    return $"Submission: {emotions.Submission}";
                case "perversion":
                    emotions.Perversion = Mathf.Clamp(emotions.Perversion + (int)value, 0, 100);
                    return $"Perversion: {emotions.Perversion}";
                case "desire":
                    emotions.Desire = Mathf.Clamp(emotions.Desire + (int)value, 0, 100);
                    return $"Desire: {emotions.Desire}";
                case "hornylevel":
                    emotions.HornyLevel = Mathf.Clamp(emotions.HornyLevel + value, 0f, 100f);
                    return $"HornyLevel: {emotions.HornyLevel:F0}";
                default:
                    return $"Unknown emotion: {emotionName}";
            }
        }

        private static string ExecuteMakeFace(string faceType)
        {
            var emotions = DialogBehaviour.Instance.CurrentEmotions;
            if (emotions == null) return "No NPC.";

            int blendIndex;
            switch (faceType.Trim())
            {
                case "happy":
                case "aroused":
                    blendIndex = emotions.Aroused;
                    break;
                case "angry":
                case "annoyed":
                    blendIndex = emotions.Annoyed;
                    break;
                case "sad":
                    blendIndex = emotions.Sad;
                    break;
                case "neutral":
                default:
                    blendIndex = emotions.Neutral;
                    break;
            }

            emotions.MakeFace(blendIndex, 80f);
            return $"Face: {faceType}";
        }

        private static string ExecuteSay(string text)
        {
            DialogBehaviour.Instance.ShowAIResponseInGame(text);
            return $"NPC said: {text}";
        }
    }
}
