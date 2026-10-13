using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Injects an "AI chat mod" tutorial screen into the game's pause menu (ESC -> Tutorial -> AI chat mod).
    /// Clones an existing ImageTutorial screen from TutorialContainer, assigns test.png as the tutorial
    /// image, and configures a concise, paginated in-game guide.
    /// </summary>
    public static class TutorialInjector
    {
        public const string TUTORIAL_NAME = "AI chat mod";

        private static readonly Dictionary<string, Texture2D> _cachedTextures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] SUPPORTED_EXTENSIONS = new[] { ".jpg", ".jpeg", ".png" };

        /// <summary>
        /// Retrieves the cached tutorial texture for the given page number (1..5), or loads it from disk
        /// in BepInEx/config/AIChat/ (supports .jpg, .jpeg, .png).
        /// </summary>
        public static Texture2D GetTutorialTexture(int pageNumber)
        {
            string cacheKey = pageNumber.ToString();

            if (_cachedTextures.TryGetValue(cacheKey, out var existing) && existing != null && !existing.Equals(null))
            {
                return existing;
            }

            var loaded = LoadTutorialTexture(pageNumber);
            if (loaded != null)
            {
                UnityEngine.Object.DontDestroyOnLoad(loaded);
                _cachedTextures[cacheKey] = loaded;
            }

            return loaded;
        }

        /// <summary>
        /// Loads a tutorial image for the specified page number (e.g. 1.jpg, 1.png) from BepInEx/config/AIChat
        /// (or fallback plugin folders).
        /// </summary>
        private static Texture2D LoadTutorialTexture(int pageNumber)
        {
            // Collect candidate directories to search in priority order:
            // 1. Plugin.ConfigDir (BepInEx/config/AIChat)
            // 2. Paths.ConfigPath/AIChat & Paths.ConfigPath/AIchat
            // 3. Assembly directory
            // 4. Paths.PluginPath
            var candidateDirs = new List<string>();

            if (!string.IsNullOrEmpty(Plugin.ConfigDir))
            {
                candidateDirs.Add(Plugin.ConfigDir);
            }

            try
            {
                if (!string.IsNullOrEmpty(Paths.ConfigPath))
                {
                    candidateDirs.Add(Path.Combine(Paths.ConfigPath, "AIChat"));
                    candidateDirs.Add(Path.Combine(Paths.ConfigPath, "AIchat"));
                }
            }
            catch { }

            try
            {
                string asmDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    candidateDirs.Add(asmDir);
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(Paths.PluginPath))
                {
                    candidateDirs.Add(Paths.PluginPath);
                }
            }
            catch { }

            byte[] fileData = null;
            string loadedPath = null;

            // Check jpg first (smaller), then jpeg, then png in each candidate directory
            var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in candidateDirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                foreach (var ext in SUPPORTED_EXTENSIONS)
                {
                    string candidate = Path.Combine(dir, $"{pageNumber}{ext}");
                    if (!checkedPaths.Add(candidate)) continue;

                    if (File.Exists(candidate))
                    {
                        try
                        {
                            fileData = File.ReadAllBytes(candidate);
                            loadedPath = candidate;
                            Plugin.LogInfo($"[TutorialInjector] Loaded tutorial image for page {pageNumber} from {candidate} ({fileData.Length / 1024} KB)");
                            break;
                        }
                        catch (Exception ex)
                        {
                            Plugin.LogWarning($"[TutorialInjector] Error reading {candidate}: {ex.Message}");
                        }
                    }
                }

                if (fileData != null) break;
            }

            if (fileData != null && fileData.Length > 0)
            {
                try
                {
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.name = $"Tutorial_Page{pageNumber}";
                    if (ImageConversion.LoadImage(tex, fileData))
                    {
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Bilinear;
                        return tex;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogError($"[TutorialInjector] ImageConversion failed to decode image for page {pageNumber} ({loadedPath}): {ex}");
                }
            }

            Plugin.LogWarning($"[TutorialInjector] No tutorial image found for page {pageNumber} (searched BepInEx/config/AIChat for {pageNumber}.jpg / .png); page will display without image.");
            return null;
        }

        /// <summary>
        /// Generates the tutorial pages for the mod with rich text formatting and page-specific textures (1.png to 5.png).
        /// </summary>
        public static List<ImageTutorial.TutorialItem> CreateTutorialItems()
        {
            var items = new List<ImageTutorial.TutorialItem>();

            // Page 1: How to configure AI Chat -> 1.png
            items.Add(new ImageTutorial.TutorialItem
            {
                texture = GetTutorialTexture(1),
                info = "<size=22><color=#FFD700><b>1. How to configure AI Chat</b></color></size>\n\n" +
                       "• Press <b>F9</b> to open the window.\n" +
                       "• If you're going to use local AI, you must use and specify <b>exactly</b> the following model: <i>defyma85/gemma-4-E4B-it-ultra-uncensored-heretic-Q4_K_M_gguf</i>.\n" +
                       "• If you're going to use Gemini, just paste your API key into the <b>Gemini API key</b> field.\n" +
                       "• Remember to click the <b>Apply & Save</b> button.\n" +
                       "• All the detailed information can be found in <b>AIchat.html</b>; double-click it to open it in your browser."
            });

            // Page 2: How to Start a Conversation -> 2.png
            items.Add(new ImageTutorial.TutorialItem
            {
                texture = GetTutorialTexture(2),
                info = "<size=22><color=#FFD700><b>2. Start a conversation with the AI</b></color></size>\n\n" +
                       "<b>How do I talk to a character or NPC?</b>\n" +
                       "• Approach any girl in the game until you see the interaction <b>E</b>.\n" +
                       "• Press the <b>[E]</b> key to open the radial action menu.\n" +
                       "• Select the <b>\"AI chat\"</b> option (speech bubble icon).\n" +
                       "• The game’s dialog box will open automatically, the text field ready for you to type."
            });

            // Page 3: Dialogue Controls -> 3.png
            items.Add(new ImageTutorial.TutorialItem
            {
                texture = GetTutorialTexture(3),
                info = "<size=22><color=#FFD700><b>3. Dialogue Controls</b></color></size>\n" +
                       "<b>Typing and responding:</b>\n" +
                       "• <b>Type:</b> Write whatever you want to say to the character. The [E] key and the radial menu will not interfere while you type.\n" +
                       "• <b>Send [ENTER]:</b> Send your message to the AI. While waiting you will see \"(Waiting for AI response...)\".\n" +
                       "• <b>Continue [SPACE or ENTER]:</b> When the character finishes speaking, press Space or Enter to continue.\n" +
                       "• <b>Close dialogue:</b> Press <b>[SPACE]</b> on your turn to type (with empty text) or <b>[ESC]</b> at any time to exit."
            });

            // Page 4: Dynamic Expressions and Reactions -> 4.png
            items.Add(new ImageTutorial.TutorialItem
            {
                texture = GetTutorialTexture(4),
                info = "<size=22><color=#FFD700><b>4. Dynamic Expressions and Reactions</b></color></size>\n\n" +
                       "<b>Emotions and Facial Expressions:</b>\n" +
                       "• Characters react dynamically based on your words:\n" +
                       "  - <b>Compliments or pleasant conversation:</b> Smiles, blush, and friendly gestures.\n" +
                       "  - <b>Insults or annoying comments:</b> Expression of anger and displeasure.\n" +
                       "  - <b>Flirty or daring topics:</b> Intense blush, shyness, or arousal.\n" +
                       "• The character will remember the tone of the conversation and previous shared events."
            });

            // Page 5: Advanced Tools (F9) -> 5.png
            items.Add(new ImageTutorial.TutorialItem
            {
                texture = GetTutorialTexture(5),
                info = "<size=22><color=#FFD700><b>5. Advanced Tools (F9)</b></color></size>\n\n" +
                       "<b>Advanced Tools:</b>\n" +
                       "• Press <b>[F9]</b> to open/close the mod's advanced window:\n" +
                       "  - <b>Chat Tab:</b> Complete history of the dialogue and detected events.\n" +
                       "  - <b>Debug Tab:</b> Detailed information about the character in question.\n" +
                       "  - <b>Config Tab:</b> Configure your API Key, switch to local or cloud AI (Gemini) and enables or disables features.\n" +
                       "• You can move the F9 window by dragging its top bar."
            });

            return items;
        }

        /// <summary>
        /// Called in TutorialForMenu.Start Prefix. Ensures the tutorial screen exists in TutorialContainer
        /// so that TutorialForMenu.Start's native loop automatically instantiates its button in order.
        /// </summary>
        public static void OnTutorialMenuStartPrefix(TutorialForMenu menu)
        {
            if (menu == null || menu.TutorialContainer == null) return;
            if (menu.TutorialContainer.childCount == 0) return;

            EnsureTutorialScreen(menu);
            CleanDuplicateButtons(menu);
        }

        /// <summary>
        /// Called in TutorialForMenu.OnEnable Postfix. Ensures the screen exists and that no duplicate buttons exist.
        /// </summary>
        public static void OnTutorialMenuEnablePostfix(TutorialForMenu menu)
        {
            if (menu == null || menu.TutorialContainer == null) return;
            if (menu.TutorialContainer.childCount == 0) return;

            GameObject tutorialGO = EnsureTutorialScreen(menu);
            CleanDuplicateButtons(menu);
            if (tutorialGO != null)
            {
                EnsureTutorialButton(menu, tutorialGO);
            }
        }

        /// <summary>
        /// Ensures the "AI chat mod" ImageTutorial screen GameObject exists under TutorialContainer.
        /// </summary>
        public static GameObject EnsureTutorialScreen(TutorialForMenu menu)
        {
            try
            {
                Transform existing = menu.TutorialContainer.Find(TUTORIAL_NAME);
                if (existing != null)
                {
                    var tut = existing.GetComponent<ImageTutorial>();
                    if (tut != null)
                    {
                        tut.items = CreateTutorialItems();
                    }
                    SetupTutorialImage(existing.gameObject, GetTutorialTexture(1));
                    return existing.gameObject;
                }

                // Clone template (first tutorial screen in container)
                Transform template = menu.TutorialContainer.GetChild(0);
                if (template == null) return null;

                GameObject newScreen = UnityEngine.Object.Instantiate(template.gameObject, menu.TutorialContainer);
                newScreen.name = TUTORIAL_NAME;
                newScreen.SetActive(false);

                SetupTutorialScreen(newScreen);

                // Register with NotMove so player movement is frozen when open
                var notMove = UnityEngine.Object.FindAnyObjectByType<NotMove>(FindObjectsInactive.Include);
                if (notMove != null)
                {
                    notMove.AddMenu(newScreen);
                }

                Plugin.LogInfo($"[TutorialInjector] Successfully created '{TUTORIAL_NAME}' screen under TutorialContainer.");
                return newScreen;
            }
            catch (Exception ex)
            {
                Plugin.LogError($"[TutorialInjector] Error creating tutorial screen: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Configures the cloned ImageTutorial GameObject with custom items, test.png texture,
        /// upper-box containment, and button bindings.
        /// </summary>
        private static void SetupTutorialScreen(GameObject screenGO)
        {
            var imgTutorial = screenGO.GetComponent<ImageTutorial>();
            if (imgTutorial == null)
            {
                Plugin.LogWarning("[TutorialInjector] Cloned screen does not have ImageTutorial component!");
                return;
            }

            imgTutorial.items = CreateTutorialItems();
            imgTutorial.ExitMsg = null;

            if (imgTutorial.ImageToShow == null)
            {
                imgTutorial.ImageToShow = screenGO.GetComponentInChildren<RawImage>(true);
            }
            if (imgTutorial.TextToShow == null)
            {
                imgTutorial.TextToShow = screenGO.GetComponentInChildren<TMP_Text>(true);
            }

            // Confine ImageToShow strictly within the native upper-box bounds
            SetupTutorialImage(screenGO, GetTutorialTexture(1));

            // Ensure close button dismisses screen
            if (imgTutorial.closeButton != null)
            {
                var closeBtn = imgTutorial.closeButton.GetComponent<Button>();
                if (closeBtn != null)
                {
                    closeBtn.onClick.AddListener(() => screenGO.SetActive(false));
                }
            }

            // Ensure Next / Back arrow buttons trigger this ImageTutorial instance
            var allButtons = screenGO.GetComponentsInChildren<Button>(true);
            foreach (var b in allButtons)
            {
                if (imgTutorial.closeButton != null && b.gameObject == imgTutorial.closeButton) continue;

                string bName = b.name.ToLowerInvariant();
                bool isNext = bName.Contains("next") || bName.Contains("right") || bName.Contains("forward") || bName.Contains("sig") || bName.Contains("adelante");
                bool isBack = bName.Contains("back") || bName.Contains("prev") || bName.Contains("left") || bName.Contains("ant") || bName.Contains("atras");

                bool alreadyBound = false;
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                {
                    if (b.onClick.GetPersistentTarget(i) == (UnityEngine.Object)imgTutorial)
                    {
                        alreadyBound = true;
                        break;
                    }
                }

                if (!alreadyBound)
                {
                    if (isNext)
                    {
                        b.onClick.AddListener(() => imgTutorial.Next());
                    }
                    else if (isBack)
                    {
                        b.onClick.AddListener(() => imgTutorial.Back());
                    }
                }
            }
        }

        /// <summary>
        /// Adjusts ImageToShow's sizeDelta based on the currently displayed texture's aspect ratio,
        /// keeping it centered in the upper tutorial box without distortion.
        /// </summary>
        public static void AdjustImageSizeForCurrentTexture(ImageTutorial tut)
        {
            if (tut == null || tut.ImageToShow == null) return;
            var rawImage = tut.ImageToShow;
            var tex = rawImage.texture;
            var imgRT = rawImage.rectTransform;

            float boxWidth = 1134f;
            float boxHeight = 637.88f;

            float w = boxWidth;
            float h = boxHeight;

            if (tex != null && tex.height > 0)
            {
                float texAspect = (float)tex.width / tex.height;
                float boxAspect = boxWidth / boxHeight;

                if (texAspect < boxAspect)
                {
                    // Taller than 16:9 (e.g. 5.png: 448x659) -> fit height, center width
                    h = boxHeight;
                    w = h * texAspect;
                }
                else
                {
                    // Wider than 16:9 -> fit width, calculate height
                    w = boxWidth;
                    h = w / texAspect;
                }
            }

            imgRT.anchorMin = new Vector2(0.5f, 1f);
            imgRT.anchorMax = new Vector2(0.5f, 1f);
            imgRT.pivot = new Vector2(0.5f, 0.5f);
            imgRT.anchoredPosition = new Vector2(0f, -336f);
            imgRT.sizeDelta = new Vector2(w, h);
            rawImage.color = Color.white;
            rawImage.gameObject.SetActive(true);
        }

        /// <summary>
        /// Configures ImageToShow to sit cleanly within the native upper tutorial image bounds.
        /// Preserves the texture's aspect ratio without stretching or overflowing.
        /// </summary>
        public static void SetupTutorialImage(GameObject screenGO, Texture2D tex)
        {
            var imgTutorial = screenGO.GetComponent<ImageTutorial>();
            if (imgTutorial == null || imgTutorial.ImageToShow == null) return;

            var rawImage = imgTutorial.ImageToShow;

            // Remove any old container if present from previous experiments
            Transform oldContainer = screenGO.transform.Find("ImageContainer");
            if (oldContainer != null)
            {
                rawImage.transform.SetParent(screenGO.transform, false);
                UnityEngine.Object.DestroyImmediate(oldContainer.gameObject);
            }

            // Remove any AspectRatioFitter
            var oldFitter = rawImage.GetComponent<AspectRatioFitter>();
            if (oldFitter != null)
            {
                UnityEngine.Object.DestroyImmediate(oldFitter);
            }

            if (tex != null)
            {
                rawImage.texture = tex;
            }

            AdjustImageSizeForCurrentTexture(imgTutorial);
        }

        /// <summary>
        /// Removes any extra duplicate buttons for TUTORIAL_NAME from the menu button list.
        /// </summary>
        public static void CleanDuplicateButtons(TutorialForMenu menu)
        {
            if (menu == null || menu.PrefabButton == null || menu.PrefabButton.transform.parent == null) return;
            Transform buttonParent = menu.PrefabButton.transform.parent;

            List<GameObject> matching = new List<GameObject>();
            for (int i = 0; i < buttonParent.childCount; i++)
            {
                Transform child = buttonParent.GetChild(i);
                if (child.gameObject == menu.PrefabButton) continue;

                var textComp = child.GetComponentInChildren<TMP_Text>(true);
                if (textComp != null && textComp.text == TUTORIAL_NAME)
                {
                    matching.Add(child.gameObject);
                }
            }

            // If more than 1 button exists, destroy all duplicates except the first
            for (int i = 1; i < matching.Count; i++)
            {
                Plugin.LogInfo($"[TutorialInjector] Removing duplicate '{TUTORIAL_NAME}' button from menu.");
                UnityEngine.Object.Destroy(matching[i]);
            }
        }

        /// <summary>
        /// Ensures a single button with text "AI chat mod" exists in PrefabButton.transform.parent,
        /// positioned right before the "Close" button. Only instantiates if Start() has already run
        /// and the button is genuinely absent.
        /// </summary>
        public static void EnsureTutorialButton(TutorialForMenu menu, GameObject tutorialGO)
        {
            if (menu == null || menu.PrefabButton == null || menu.PrefabButton.transform.parent == null) return;

            Transform buttonParent = menu.PrefabButton.transform.parent;

            CleanDuplicateButtons(menu);

            // If Start() hasn't run yet (buttonParent only contains the prefab template), do nothing:
            // Native Start() will loop through TutorialContainer and create the button!
            if (buttonParent.childCount <= 1) return;

            // Check if button already exists in menu
            for (int i = 0; i < buttonParent.childCount; i++)
            {
                Transform child = buttonParent.GetChild(i);
                var textComp = child.GetComponentInChildren<TMP_Text>(true);
                if (textComp != null && textComp.text == TUTORIAL_NAME)
                {
                    return; // Already present!
                }
            }

            // Clone PrefabButton
            GameObject btnObj = UnityEngine.Object.Instantiate(menu.PrefabButton, buttonParent);
            btnObj.name = "Button_" + TUTORIAL_NAME;

            var tmp = btnObj.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = TUTORIAL_NAME;
            }

            var btnComp = btnObj.GetComponent<Button>();
            if (btnComp != null)
            {
                btnComp.onClick.RemoveAllListeners();
                btnComp.onClick.AddListener(() =>
                {
                    menu.OpenMenu(tutorialGO);
                });
            }

            btnObj.SetActive(true);

            // Reorder so it sits immediately before "Close"
            for (int i = 0; i < buttonParent.childCount; i++)
            {
                var child = buttonParent.GetChild(i);
                var t = child.GetComponentInChildren<TMP_Text>(true);
                if (t != null && t.text.Equals("Close", StringComparison.OrdinalIgnoreCase))
                {
                    btnObj.transform.SetSiblingIndex(child.GetSiblingIndex());
                    break;
                }
            }

            Plugin.LogInfo($"[TutorialInjector] Successfully added fallback '{TUTORIAL_NAME}' button to TutorialForMenu list.");
        }
    }
}
