using System;
using HarmonyLib;
using UnityEngine;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Patching
{
    /// <summary>
    /// Harmony patches for intercepting the game's dialogue system.
    /// 
    /// Key intercepts:
    /// 1. Speach.Say() — captures ALL NPC dialogue to feed context to AI
    /// 2. TalkInteractions.Talk/Cumpliment/Critic — detects player-initiated conversations
    /// 10-12. The game's own dialogue lines - the bubbles, the dialogue box and the Sex-Talk windows -
    ///         are read out loud when the "Default Game Dialogues" option is on (see
    ///         <see cref="Game.NativeDialogueTts"/>).
    /// 
    /// The voicing patches sit next to the capture patches and never change what the game shows: a
    /// line that the game itself drops (the bubble is busy, the box is still showing the previous
    /// line, Sex-Talk is off) is not voiced either, because the player never read it.
    /// </summary>
    public static class HarmonyPatches
    {
        // ═══════════════════════════════════════════════════════════
        //  PATCH 1: Intercept Speach.Say() to capture NPC dialogue
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(Speach), "Say")]
        public static class Speach_Say_Patch
        {
            /// <summary>
            /// Postfix: Logs what each NPC says so the AI has conversation context.
            /// Does NOT block the original dialogue — it runs alongside it.
            /// </summary>
            static void Postfix(Speach __instance, string text)
            {
                try
                {
                    if (DialogBehaviour.Instance == null) return;
                    if (string.IsNullOrEmpty(text)) return;

                    string npcName = __instance.transform.name;

                    // Don't capture AI-generated responses (avoid echo loop)
                    if (DialogBehaviour.Instance.Window != null && DialogBehaviour.Instance.Window.IsAIResponse)
                        return;

                    Plugin.LogDebug($"[Speach.Say] {npcName}: {text}");

                    // If this is the NPC we're currently chatting with, add to context
                    if (npcName == DialogBehaviour.Instance.CurrentCharName)
                    {
                        // Add as system message to provide context about NPC's game dialogue.
                        // Routed through the spam filter so repeated chatter can't flood the window.
                        DialogBehaviour.Instance.AddCapturedEvent($"[{npcName} says in-game]: {text}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"Speach_Say_Patch error: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 2: Intercept TalkInteractions.Talk()
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(TalkInteractions), "Talk")]
        public static class TalkInteractions_Talk_Patch
        {
            static void Postfix(TalkInteractions __instance, string topic)
            {
                try
                {
                    if (DialogBehaviour.Instance == null) return;

                    string npcName = __instance.transform.name;
                    Plugin.LogDebug($"[Talk] Player talked to {npcName} about: {topic}");

                    if (npcName == DialogBehaviour.Instance.CurrentCharName)
                    {
                        DialogBehaviour.Instance.AddCapturedEvent($"[EVT] Player discussed '{topic}' with {npcName}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"TalkInteractions_Talk_Patch error: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 3: Intercept TalkInteractions.Cumpliment()
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(TalkInteractions), "Cumpliment")]
        public static class TalkInteractions_Cumpliment_Patch
        {
            static void Postfix(TalkInteractions __instance, string topic)
            {
                try
                {
                    if (DialogBehaviour.Instance == null) return;

                    string npcName = __instance.transform.name;
                    Plugin.LogDebug($"[Cumpliment] Player complimented {npcName}: {topic}");

                    if (npcName == DialogBehaviour.Instance.CurrentCharName)
                    {
                        DialogBehaviour.Instance.AddCapturedEvent($"[EVT] Player complimented {npcName}: '{topic}'");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"TalkInteractions_Cumpliment_Patch error: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 4: Intercept TalkInteractions.Critic()
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(TalkInteractions), "Critic")]
        public static class TalkInteractions_Critic_Patch
        {
            static void Postfix(TalkInteractions __instance, string topic)
            {
                try
                {
                    if (DialogBehaviour.Instance == null) return;

                    string npcName = __instance.transform.name;
                    Plugin.LogDebug($"[Critic] Player criticized {npcName}: {topic}");

                    if (npcName == DialogBehaviour.Instance.CurrentCharName)
                    {
                        DialogBehaviour.Instance.AddCapturedEvent($"[EVT] Player criticized {npcName}: '{topic}'");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"TalkInteractions_Critic_Patch error: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 5: Intercept Emotions.NpcKiss()
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(Emotions), "NpcKiss")]
        public static class Emotions_NpcKiss_Patch
        {
            static void Postfix(Emotions __instance)
            {
                try
                {
                    if (DialogBehaviour.Instance == null) return;

                    string npcName = __instance.transform.name;
                    if (npcName == DialogBehaviour.Instance.CurrentCharName)
                    {
                        DialogBehaviour.Instance.AddCapturedEvent($"[EVT] Player kissed {npcName}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"Emotions_NpcKiss_Patch error: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 6: Intercept InteractionMenu to inject "AI chat" button
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(InteractionMenu), "Start")]
        public static class InteractionMenu_Start_Patch
        {
            static void Postfix(InteractionMenu __instance)
            {
                Game.RadialMenuInjector.EnsureAIChatButton(__instance);
            }
        }

        [HarmonyPatch(typeof(InteractionMenu), "Active")]
        public static class InteractionMenu_Active_Patch
        {
            static void Postfix(InteractionMenu __instance)
            {
                Game.RadialMenuInjector.EnsureAIChatButton(__instance);
            }
        }

        [HarmonyPatch(typeof(InteractionMenu), "ActiveOnlyTalkOptions")]
        public static class InteractionMenu_ActiveOnlyTalkOptions_Patch
        {
            static void Postfix(InteractionMenu __instance)
            {
                Game.RadialMenuInjector.EnsureAIChatButton(__instance);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 7: Suppress 'E' key while typing / conversing
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(InteractionMenu), "Update")]
        public static class InteractionMenu_Update_Patch
        {
            static bool Prefix(InteractionMenu __instance)
            {
                bool isDialogueActive = false;

                if (Game.NativeDialogueInputHandler.Instance != null &&
                    (Game.NativeDialogueInputHandler.Instance.IsInputActive ||
                     Game.NativeDialogueInputHandler.Instance.IsWaitingForAI ||
                     Game.NativeDialogueInputHandler.Instance.IsNpcSpeaking))
                {
                    isDialogueActive = true;
                }

                if (DialogBehaviour.Instance?.Window != null && DialogBehaviour.Instance.Window.IsVisible)
                {
                    isDialogueActive = true;
                }

                if (isDialogueActive)
                {
                    // Keep the radial menu closed while typing or conversing
                    if (__instance.transform.childCount > 0 && __instance.transform.GetChild(0).gameObject.activeSelf)
                    {
                        __instance.transform.GetChild(0).gameObject.SetActive(false);
                        __instance.MenuIsActive = false;
                    }

                    // Hide the floating "E to interact" prompt while dialogue is active
                    var shower = Traverse.Create(__instance).Field<InteractionsShower>("shower").Value;
                    if (shower != null)
                    {
                        shower.NpcInteract = false;
                    }

                    // Skip Update so KeyCode.E does NOT trigger radial menu while typing
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(DoorInteractor), "Update")]
        public static class DoorInteractor_Update_Patch
        {
            static bool Prefix()
            {
                if (Game.NativeDialogueInputHandler.Instance != null &&
                    (Game.NativeDialogueInputHandler.Instance.IsInputActive ||
                     Game.NativeDialogueInputHandler.Instance.IsWaitingForAI ||
                     Game.NativeDialogueInputHandler.Instance.IsNpcSpeaking))
                {
                    return false;
                }

                if (DialogBehaviour.Instance?.Window != null && DialogBehaviour.Instance.Window.IsVisible)
                {
                    return false;
                }

                return true;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 8: Inject "AI chat mod" tutorial in TutorialForMenu
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(TutorialForMenu), "Start")]
        public static class TutorialForMenu_Start_Patch
        {
            static void Prefix(TutorialForMenu __instance)
            {
                try
                {
                    Game.TutorialInjector.OnTutorialMenuStartPrefix(__instance);
                }
                catch (Exception ex)
                {
                    Plugin.LogError($"[TutorialForMenu.Start Prefix] {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(TutorialForMenu), "OnEnable")]
        public static class TutorialForMenu_OnEnable_Patch
        {
            static void Postfix(TutorialForMenu __instance)
            {
                try
                {
                    Game.TutorialInjector.OnTutorialMenuEnablePostfix(__instance);
                }
                catch (Exception ex)
                {
                    Plugin.LogError($"[TutorialForMenu.OnEnable Postfix] {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 9: Dynamic image aspect ratio on tutorial page flip
        // ═══════════════════════════════════════════════════════════

        [HarmonyPatch(typeof(ImageTutorial), "Show")]
        public static class ImageTutorial_Show_Patch
        {
            static void Postfix(ImageTutorial __instance)
            {
                try
                {
                    if (__instance != null && __instance.gameObject.name == Game.TutorialInjector.TUTORIAL_NAME)
                    {
                        Game.TutorialInjector.AdjustImageSizeForCurrentTexture(__instance);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"[ImageTutorial.Show Postfix] {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 10: Read the NPC bubbles out loud (Talk About,
        //            Compliment, Critic, gifts, kisses, random chatter)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Voices the lines the game puts in an NPC's speech bubble. The speaker is the component the
        /// line belongs to, which is how a main character keeps her voice and a random NPC hers.
        ///
        /// This runs alongside <see cref="Speach_Say_Patch"/>, which feeds the same line to the AI
        /// context; the two are independent, so the context never depends on the TTS option.
        /// </summary>
        [HarmonyPatch(typeof(Speach), "Say")]
        public static class Speach_Say_NativeTts_Patch
        {
            static void Postfix(Speach __instance, string text)
            {
                try
                {
                    // Say() drops the line while the NPC is still speaking, and a line the player
                    // never saw must not be heard either.
                    if (!Game.NativeDialogueTts.LineWasShown(__instance, text)) return;

                    Game.NativeDialogueTts.Speak(
                        Game.NativeDialogueTts.SpeakerFrom(__instance), text, "Speach.Say",
                        allowCurrentNpcFallback: true);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"[NativeTts] Speach.Say: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 11: Read the dialogue box out loud (events, gifts,
        //            kisses, the jail Police, quest lines)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Voices <c>ShowDialogue.ShowSimpleWithName(text, name)</c>, the box every scripted event uses.
        ///
        /// The name is the speaker: a character name keeps her voice ("Katherine" for
        /// KatherineSalutesAtNight), "Police" gets a random NPC's, and "whispers" / "Me" / an empty
        /// name are not characters and stay silent - an empty name is the game narrating the player's
        /// own situation ("It seems you're finally free, investigate how to get out"), which nobody
        /// should hear an NPC say. <c>ShowSimpleText()</c> is left alone for the same reason: it
        /// carries no speaker at all and is used for the interface ("Laura is here", "Your lust has
        /// run out").
        /// </summary>
        [HarmonyPatch(typeof(ShowDialogue), "ShowSimpleWithName")]
        public static class ShowDialogue_ShowSimpleWithName_Patch
        {
            static void Prefix(ShowDialogue __instance, out bool __state)
            {
                // The box ignores a new line while "showing" is set (its own guard), so whether the
                // line was accepted has to be read before the original runs.
                __state = __instance.showing;
            }

            static void Postfix(string NewText, string NewName, bool __state)
            {
                try
                {
                    if (__state) return;
                    Game.NativeDialogueTts.Speak(NewName, NewText, "ShowDialogue.ShowSimpleWithName");
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"[NativeTts] ShowSimpleWithName: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Voices the lines of <c>ShowDialogue.ShowArrayText(texts, name)</c>: same box, same speaker
        /// rule, one line after another. The mod's own repeat guard keeps a line repeated inside the
        /// array from being requested twice.
        /// </summary>
        [HarmonyPatch(typeof(ShowDialogue), "ShowArrayText")]
        public static class ShowDialogue_ShowArrayText_Patch
        {
            static void Prefix(ShowDialogue __instance, out bool __state)
            {
                __state = __instance.showing;
            }

            static void Postfix(string[] NewText, string NewName, bool __state)
            {
                try
                {
                    if (__state || NewText == null) return;

                    foreach (string line in NewText)
                        Game.NativeDialogueTts.Speak(NewName, line, "ShowDialogue.ShowArrayText");
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"[NativeTts] ShowArrayText: {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  PATCH 12: Read the "Sex-Talk" windows out loud
        //            (SexDialogue, CumDialogue, ThreesomeSexDialogue)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Voices the Sex-Talk window used for the comments a character makes while she is in sex
        /// mode. The speaker arrives as the NPC's object name ("Katherine 1"), which the window
        /// itself shortens to the first word - the voice lookup does the same.
        /// </summary>
        [HarmonyPatch(typeof(SexDialogue), "ShowText")]
        public static class SexDialogue_ShowText_Patch
        {
            static void Prefix(SexDialogue __instance, out bool __state)
            {
                __state = __instance.showing;
            }

            static void Postfix(string Npc, string Phrase, bool __state)
            {
                try
                {
                    if (!Game.NativeDialogueTts.SexTalkWillShow(Npc, Phrase, "SexDialogue.ShowText")) return;
                    if (__state) return;

                    // sexTalk: the line is sent with the [moaning] tag in front.
                    Game.NativeDialogueTts.Speak(Npc, Phrase, "SexDialogue.ShowText", sexTalk: true);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"[NativeTts] SexDialogue.ShowText: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Voices <c>SexDialogue.ForceText()</c>: the enter/exit lines that interrupt whatever the
        /// window is showing, so it has no "busy" guard of its own.
        /// </summary>
        [HarmonyPatch(typeof(SexDialogue), "ForceText")]
        public static class SexDialogue_ForceText_Patch
        {
            static void Postfix(string Npc, string Phrase)
            {
                try
                {
                    if (!Game.NativeDialogueTts.SexTalkWillShow(Npc, Phrase, "SexDialogue.ForceText")) return;

                    Game.NativeDialogueTts.Speak(Npc, Phrase, "SexDialogue.ForceText", sexTalk: true);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"[NativeTts] SexDialogue.ForceText: {ex.Message}");
                }
            }
        }

        /// <summary>Voices the cum-shot window ("So deep...") of the same Sex-Talk group.</summary>
        [HarmonyPatch(typeof(CumDialogue), "ShowText")]
        public static class CumDialogue_ShowText_Patch
        {
            static void Prefix(CumDialogue __instance, out bool __state)
            {
                __state = __instance.showing;
            }

            static void Postfix(string Npc, string Phrase, bool __state)
            {
                try
                {
                    if (!Game.NativeDialogueTts.SexTalkWillShow(Npc, Phrase, "CumDialogue.ShowText")) return;
                    if (__state) return;

                    Game.NativeDialogueTts.Speak(Npc, Phrase, "CumDialogue.ShowText", sexTalk: true);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"[NativeTts] CumDialogue.ShowText: {ex.Message}");
                }
            }
        }

        /// <summary>Voices the window the game shows for a third character during a threesome.</summary>
        [HarmonyPatch(typeof(ThreesomeSexDialogue), "ShowText")]
        public static class ThreesomeSexDialogue_ShowText_Patch
        {
            static void Prefix(ThreesomeSexDialogue __instance, out bool __state)
            {
                // This window keeps its "showing" flag private, so it is read through Harmony's
                // Traverse like the mod does elsewhere.
                __state = Traverse.Create(__instance).Field<bool>("showing").Value;
            }

            static void Postfix(string Npc, string Phrase, bool __state)
            {
                try
                {
                    if (!Game.NativeDialogueTts.SexTalkWillShow(Npc, Phrase, "ThreesomeSexDialogue.ShowText")) return;
                    if (__state) return;

                    Game.NativeDialogueTts.Speak(Npc, Phrase, "ThreesomeSexDialogue.ShowText", sexTalk: true);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug($"[NativeTts] ThreesomeSexDialogue.ShowText: {ex.Message}");
                }
            }
        }
    }
}
