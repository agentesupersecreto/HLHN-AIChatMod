using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Dynamically injects an "AI chat" button into the game's native radial interaction menu
    /// (InteractionMenu) when the player approaches an NPC and presses 'E'.
    /// Clones the "Talk About" button so it inherits the exact speech-bubble icon, styling,
    /// shadow effects, and radial positioning from RadialUIController.
    /// </summary>
    public static class RadialMenuInjector
    {
        private const string BUTTON_NAME = "AIChatButton";
        private const string BUTTON_LABEL = "AI chat";

        /// <summary>
        /// Ensures the "AI chat" button exists inside the InteractionMenu's radial container
        /// and updates its visibility according to InRangeForTalk.
        /// </summary>
        public static void EnsureAIChatButton(InteractionMenu menu)
        {
            if (menu == null) return;

            try
            {
                if (menu.transform.childCount == 0) return;
                Transform radialRoot = menu.transform.GetChild(0);
                if (radialRoot == null) return;

                var traverse = Traverse.Create(menu);
                bool inRangeForTalk = traverse.Field<bool>("InRangeForTalk").Value;

                // Check if button already exists in radial container
                Transform existing = radialRoot.Find(BUTTON_NAME);
                if (existing != null)
                {
                    existing.gameObject.SetActive(inRangeForTalk);
                    ConfigureAIButtonAppearance(existing.gameObject);
                    return;
                }

                // Locate the "Talk About" button to use as template
                GameObject sourceButton = FindTalkAboutButton(menu);
                if (sourceButton == null)
                {
                    Plugin.LogDebug("RadialMenuInjector: Could not find Talk About button template.");
                    return;
                }

                // Instantiate clone as a child of the radial container
                GameObject aiBtnObj = UnityEngine.Object.Instantiate(sourceButton, radialRoot, false);
                aiBtnObj.name = BUTTON_NAME;

                // Configure appearance: hide speech bubble icon and display "AI" text
                ConfigureAIButtonAppearance(aiBtnObj);

                // Configure ButtonDescription (used by ButtonChecker to show description in center)
                var desc = aiBtnObj.GetComponent<ButtonDescription>();
                if (desc == null) desc = aiBtnObj.AddComponent<ButtonDescription>();
                desc.Description = BUTTON_LABEL;

                // Configure button onClick listener
                var btnComp = aiBtnObj.GetComponent<Button>();
                if (btnComp != null)
                {
                    btnComp.onClick = new Button.ButtonClickedEvent();
                    btnComp.onClick.AddListener(() =>
                    {
                        OnAIChatClicked(menu);
                    });
                }

                // Append our button to InteractionMenu.TalkButtons array so native routines track it
                var talkButtons = traverse.Field<GameObject[]>("TalkButtons").Value;
                if (talkButtons != null)
                {
                    var list = new List<GameObject>(talkButtons);
                    if (!list.Contains(aiBtnObj))
                    {
                        list.Add(aiBtnObj);
                        traverse.Field("TalkButtons").SetValue(list.ToArray());
                    }
                }

                aiBtnObj.SetActive(inRangeForTalk);
                Plugin.LogInfo($"Injected '{BUTTON_LABEL}' button into InteractionMenu radial container with 'AI' label.");
            }
            catch (Exception ex)
            {
                Plugin.LogWarning($"RadialMenuInjector.EnsureAIChatButton error: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the "Talk about" button in InteractionMenu to serve as the template for our AI chat button.
        /// </summary>
        private static GameObject FindTalkAboutButton(InteractionMenu menu)
        {
            var traverse = Traverse.Create(menu);
            var talkButtons = traverse.Field<GameObject[]>("TalkButtons").Value;

            if (talkButtons != null && talkButtons.Length > 0)
            {
                // 1. Search by ButtonDescription containing "Talk"
                foreach (var btn in talkButtons)
                {
                    if (btn == null) continue;
                    var desc = btn.GetComponent<ButtonDescription>();
                    if (desc != null && !string.IsNullOrEmpty(desc.Description))
                    {
                        if (desc.Description.IndexOf("Talk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            desc.Description.IndexOf("Hablar", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return btn;
                        }
                    }
                }

                // 2. Search by GameObject name
                foreach (var btn in talkButtons)
                {
                    if (btn == null) continue;
                    if (btn.name.IndexOf("Talk", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return btn;
                    }
                }

                // 3. Fallback: first element in TalkButtons
                if (talkButtons[0] != null)
                {
                    return talkButtons[0];
                }
            }

            // 4. Search under radialRoot
            if (menu.transform.childCount > 0)
            {
                Transform radialRoot = menu.transform.GetChild(0);
                foreach (Transform child in radialRoot)
                {
                    var desc = child.GetComponent<ButtonDescription>();
                    if (desc != null && !string.IsNullOrEmpty(desc.Description))
                    {
                        if (desc.Description.IndexOf("Talk", StringComparison.OrdinalIgnoreCase) >= 0)
                            return child.gameObject;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Handles click on the "AI chat" button:
        /// 1. Captures target NPC and pauses them.
        /// 2. Binds target NPC in DialogBehaviour.
        /// 3. Closes radial menu so only the native dialogue box appears.
        /// 4. Opens native dialogue input without opening IMGUI window.
        /// </summary>
        public static void OnAIChatClicked(InteractionMenu menu)
        {
            if (menu == null) return;

            try
            {
                // 1. Capture current NPC
                NpcNavigatorAndRutines nav = menu.Navigator;
                if (nav == null && menu.manager != null)
                {
                    nav = menu.manager.GetComponent<NpcNavigatorAndRutines>();
                }

                // 2. Bind in DialogBehaviour
                var db = DialogBehaviour.Instance;
                if (db != null && nav != null)
                {
                    db.SetTargetNpc(nav);
                }

                // 3. Close radial menu
                menu.CloseMenu(true);

                // Ensure NPC stays paused while chatting
                if (nav != null)
                {
                    nav.Pause(true);
                }

                // 4. Ensure IMGUI window stays closed (manual F9 toggle only)
                if (db?.Window != null && db.Window.IsVisible)
                {
                    db.Window.SetVisible(false);
                }

                // 5. Open native dialogue box for player typing
                NativeDialogueInputHandler.Instance?.StartInput();
            }
            catch (Exception ex)
            {
                Plugin.LogError($"RadialMenuInjector.OnAIChatClicked error: {ex.Message}");
            }
        }

        /// <summary>
        /// Replaces the speech bubble icon on the button with centered "AI" text.
        /// </summary>
        private static void ConfigureAIButtonAppearance(GameObject btnObj)
        {
            if (btnObj == null) return;

            // 1. Hide the "Talk About" icon (disable any child Image / RawImage)
            var rootImage = btnObj.GetComponent<Image>();
            foreach (Transform child in btnObj.transform)
            {
                if (child.name == "AILabel") continue;

                var childImg = child.GetComponent<Image>();
                if (childImg != null) childImg.enabled = false;

                var childRaw = child.GetComponent<RawImage>();
                if (childRaw != null) childRaw.enabled = false;
            }

            // 2. Add or update the centered "AI" text label
            Transform labelTransform = btnObj.transform.Find("AILabel");
            TMP_Text label = null;
            if (labelTransform != null)
            {
                label = labelTransform.GetComponent<TMP_Text>();
            }
            else
            {
                GameObject labelObj = new GameObject("AILabel", typeof(RectTransform));
                labelObj.transform.SetParent(btnObj.transform, false);

                RectTransform rt = labelObj.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;

                label = labelObj.AddComponent<TextMeshProUGUI>();
            }

            if (label != null)
            {
                label.text = "AI";
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = 26;
                label.fontStyle = FontStyles.Bold;
                label.color = new Color(0.12f, 0.12f, 0.12f, 1f);
                label.raycastTarget = false;
                label.gameObject.SetActive(true);
            }
        }
    }
}
