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
    }
}
