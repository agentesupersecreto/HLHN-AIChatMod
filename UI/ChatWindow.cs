using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DialogInterceptorMod.Core;
using DialogInterceptorMod.Models;

namespace DialogInterceptorMod.UI
{
    /// <summary>
    /// IMGUI-based chat window for HLHN AI Chat.
    /// Toggled with F9 (or configured hotkey). Three tabs: Chat, Settings, Debug.
    /// AI responses are shown in the chat window AND via the game's native Speach system.
    /// Config is persisted to BepInEx/config/HLHN_AIChatMod.ini
    /// </summary>
    public class ChatWindow
    {
        private DialogBehaviour _behaviour;
        private bool _visible = false;
        private string _inputText = "";
        private Vector2 _scrollPosition;
        private Vector2 _debugScrollPosition;
        private Rect _windowRect;

        // Resize handle
        private bool _isResizing = false;
        private Vector2 _resizeStart;
        private Vector2 _resizeSizeStart;
        private const float RESIZE_HANDLE_SIZE = 18f;
        private const float MIN_WIDTH = 340f;
        private const float MIN_HEIGHT = 400f;

        // Size used the first time the window is placed. After that the size is whatever the user
        // drags the resize grip to: it must never be forced back to these values.
        private const float DEFAULT_WIDTH  = 460f;
        private const float DEFAULT_HEIGHT = 640f;
        private const float SCREEN_MARGIN_X = 20f;
        private const float SCREEN_MARGIN_Y = 40f;
        private string _statusText = "";
        private bool _statusIsError = false;
        private float _statusTime;

        private bool _hasPositioned = false;
        private int _lastScreenWidth = 0;
        private int _lastScreenHeight = 0;
        private bool _focusInputPending = false;

        public bool IsVisible => _visible;
        public bool AwaitingResponse { get; set; }

        /// <summary>Flag to prevent Speach.Say patch from capturing AI-generated responses.</summary>
        public bool IsAIResponse { get; set; }

        // Active tab: 0 = Chat, 1 = Settings, 2 = Debug
        private int _activeTab = 0;

        // Settings panel fields
        private string _editOllamaUrl;
        private string _editOllamaModel;
        private string _editApiKey;
        // Enum-typed (not a 0/1/2 index) so reordering ProviderType cannot silently break the UI.
        private ProviderType _editProvider;
        private string _editGemmaUrl;
        private string _editGemmaModel;
        private bool _editNativeDialogueInput;
        private string _editNativeSpeakerName = "You";

        // Hotkey editing
        private bool _listeningForKey = false;
        private string _editHotkeyDisplay = "";

        public ChatWindow(DialogBehaviour behaviour)
        {
            _behaviour = behaviour;
            _windowRect = new Rect(0, 0, DEFAULT_WIDTH, DEFAULT_HEIGHT);
        }

        public void Initialize()
        {
            // BepInEx/config/AIChat is created on demand, and the .ini is written on first run
            Plugin.EnsureConfigDir();
            if (!File.Exists(GetIniPath()))
                SaveConfig();

            _editOllamaUrl   = _behaviour.OllamaUrl;
            _editOllamaModel = _behaviour.OllamaModel;
            _editGemmaUrl    = _behaviour.GemmaUrl;
            _editGemmaModel  = _behaviour.GemmaModel;
            _editApiKey      = _behaviour.ApiKey;
            _editProvider    = _behaviour.Provider;
            _editHotkeyDisplay = _behaviour.ChatHotkey.ToString();
            _editNativeDialogueInput = _behaviour.NativeDialogueInputEnabled;
            _editNativeSpeakerName   = _behaviour.NativeSpeakerName;

            LoadConfig();
        }

        public void SetInputText(string text) => _inputText = text ?? "";

        // ═══════════════════════════════════════════════════════════
        //  Positioning
        // ═══════════════════════════════════════════════════════════

        public void ResetToDefaultPosition()
        {
            float width  = Mathf.Min(DEFAULT_WIDTH,  Screen.width  > 500 ? DEFAULT_WIDTH  : Screen.width  - SCREEN_MARGIN_X);
            float height = Mathf.Min(DEFAULT_HEIGHT, Screen.height > 700 ? DEFAULT_HEIGHT : Screen.height - SCREEN_MARGIN_Y);
            float x = Mathf.Max(10f, Screen.width - width - SCREEN_MARGIN_X);
            float y = 20f;
            _windowRect = new Rect(x, y, width, height);
            _lastScreenWidth  = Screen.width;
            _lastScreenHeight = Screen.height;
            _hasPositioned = true;
        }

        public void EnsureWindowPosition()
        {
            if (Screen.width <= 100 || Screen.height <= 100) return;

            if (!_hasPositioned ||
                Screen.width  != _lastScreenWidth  ||
                Screen.height != _lastScreenHeight ||
                _windowRect.x < 0 || _windowRect.x > Screen.width  - 60 ||
                _windowRect.y < 0 || _windowRect.y > Screen.height - 60)
            {
                ResetToDefaultPosition();
                return;
            }

            // Keep the size the user dragged the resize grip to: only clamp it to the minimums and
            // to the screen. Force-assigning a fixed size here ran twice per frame and silently
            // undid every resize, which made the grip look dead.
            _windowRect.width  = Mathf.Clamp(_windowRect.width,  MIN_WIDTH,  MaxWindowWidth());
            _windowRect.height = Mathf.Clamp(_windowRect.height, MIN_HEIGHT, MaxWindowHeight());

            float maxX = Mathf.Max(0f, Screen.width  - _windowRect.width);
            float maxY = Mathf.Max(0f, Screen.height - _windowRect.height);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, maxX);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, maxY);
        }

        // ═══════════════════════════════════════════════════════════
        //  Visibility & update
        // ═══════════════════════════════════════════════════════════

        public void ToggleVisibility() => SetVisible(!_visible);

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;

            if (_visible)
            {
                EnsureWindowPosition();
                _focusInputPending = true;
                Cursor.visible   = true;
                Cursor.lockState = CursorLockMode.None;
                _behaviour.OnChatVisibilityChanged(true);
                Plugin.LogInfo("Chat window opened — Cursor unlocked");
            }
            else
            {
                _behaviour.OnChatVisibilityChanged(false);
                Plugin.LogInfo("Chat window closed");
            }
        }

        public void HandleUpdate()
        {
            // Hotkey capture mode
            if (_listeningForKey)
            {
                foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
                {
                    if (kc == KeyCode.None || kc == KeyCode.Mouse0 || kc == KeyCode.Mouse1) continue;
                    if (Input.GetKeyDown(kc))
                    {
                        if (kc == KeyCode.Escape)
                        {
                            // Cancel
                            _listeningForKey = false;
                            _editHotkeyDisplay = _behaviour.ChatHotkey.ToString();
                        }
                        else
                        {
                            _behaviour.ChatHotkey = kc;
                            _editHotkeyDisplay = kc.ToString();
                            _listeningForKey = false;
                            Plugin.LogInfo($"Hotkey changed to: {kc}");
                        }
                        return;
                    }
                }
                return; // don't toggle while listening
            }

            if (Input.GetKeyDown(_behaviour.ChatHotkey))
                ToggleVisibility();

            if (_visible)
            {
                Cursor.visible   = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  Draw
        // ═══════════════════════════════════════════════════════════

        public void Draw()
        {
            if (!_visible) return;

            if (ChatStyles.NeedsReinitialize())
            {
                ChatStyles.Initialized = false;
                ChatStyles.Initialize();
            }

            HandleResize();
            EnsureWindowPosition();

            _windowRect = GUILayout.Window(
                98765,
                _windowRect,
                DrawWindowContents,
                "",
                ChatStyles.WindowStyle,
                GUILayout.Width(_windowRect.width),
                GUILayout.Height(_windowRect.height)
            );

            EnsureWindowPosition();

            // Draw resize grip over the window (drawn outside the window callback so it's always on top)
            DrawResizeHandle();
        }

        /// <summary>Largest width the window may have on the current screen.</summary>
        private static float MaxWindowWidth()
        {
            return Mathf.Max(MIN_WIDTH, Screen.width - SCREEN_MARGIN_X);
        }

        /// <summary>Largest height the window may have on the current screen.</summary>
        private static float MaxWindowHeight()
        {
            return Mathf.Max(MIN_HEIGHT, Screen.height - SCREEN_MARGIN_Y);
        }

        private void HandleResize()
        {
            Event e = Event.current;
            Rect handleRect = new Rect(
                _windowRect.xMax - RESIZE_HANDLE_SIZE,
                _windowRect.yMax - RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE);

            if (e.type == EventType.MouseDown && e.button == 0 && handleRect.Contains(e.mousePosition))
            {
                _isResizing    = true;
                _resizeStart   = e.mousePosition;
                _resizeSizeStart = new Vector2(_windowRect.width, _windowRect.height);
                e.Use();
            }

            if (_isResizing)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
                {
                    Vector2 delta = e.mousePosition - _resizeStart;
                    // Same bounds the per-frame clamp uses, so the drag never fights with it
                    float newW = Mathf.Clamp(_resizeSizeStart.x + delta.x, MIN_WIDTH,  MaxWindowWidth());
                    float newH = Mathf.Clamp(_resizeSizeStart.y + delta.y, MIN_HEIGHT, MaxWindowHeight());
                    _windowRect.width  = newW;
                    _windowRect.height = newH;
                    if (e.type == EventType.MouseDrag) e.Use();
                }
                if (e.type == EventType.MouseUp)
                {
                    _isResizing = false;
                    e.Use();
                }
            }
        }

        private void DrawResizeHandle()
        {
            Rect grip = new Rect(
                _windowRect.xMax - RESIZE_HANDLE_SIZE,
                _windowRect.yMax - RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE);

            bool hovered = Event.current != null && grip.Contains(Event.current.mousePosition);

            Color old = GUI.color;
            if (_isResizing)   GUI.color = new Color(0.0f, 0.9f, 1f, 1f);        // cyan while dragging
            else if (hovered)  GUI.color = new Color(0.78f, 0.85f, 0.95f, 1f);   // brighten on hover
            else               GUI.color = new Color(0.5f, 0.55f, 0.65f, 1f);

            GUI.DrawTexture(grip, ChatStyles.ResizeGripTexture, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        private void DrawWindowContents(int windowID)
        {
            // ── Title bar ──────────────────────────────────────
            GUILayout.BeginHorizontal();
            string charLabel = string.IsNullOrEmpty(_behaviour.CurrentCharName)
                ? "No NPC nearby"
                : $"Chatting: {_behaviour.CurrentCharName}";
            GUILayout.Label($"HLHN AI Chat v0.1 -- {charLabel}", ChatStyles.TitleStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("[R]", ChatStyles.ButtonStyle, GUILayout.Width(32)))
                ResetToDefaultPosition();
            if (GUILayout.Button("[X]", ChatStyles.ButtonStyle, GUILayout.Width(32)))
                SetVisible(false);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // ── Tab bar ────────────────────────────────────────
            GUILayout.BeginHorizontal();
            DrawTabButton("[CHAT]",  0);
            DrawTabButton("[CONFIG]",   1);
            DrawTabButton("[DEBUG]",   2);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ── Tab content ────────────────────────────────────
            switch (_activeTab)
            {
                case 0: DrawChat();     break;
                case 1: DrawSettings(); break;
                case 2: DrawDebug();    break;
            }

            // ── Footer ─────────────────────────────────────────
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.Label("made by agenteSuperSecreto", ChatStyles.FooterStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 30));
        }

        private void DrawTabButton(string label, int tabIndex)
        {
            bool active = _activeTab == tabIndex;
            if (GUILayout.Button(label, active ? ChatStyles.ActiveButtonStyle : ChatStyles.ButtonStyle))
                _activeTab = tabIndex;
        }

        // ═══════════════════════════════════════════════════════════
        //  TAB: Chat
        // ═══════════════════════════════════════════════════════════

        private void DrawChat()
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, ChatStyles.ChatAreaStyle, GUILayout.ExpandHeight(true));

            foreach (var msg in _behaviour.ChatHistory)
            {
                if (msg.IsSystem)
                {
                    GUILayout.Label(msg.Text, ChatStyles.ChatSystemStyle);
                }
                else if (msg.IsUser)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(msg.Text, ChatStyles.ChatUserStyle, GUILayout.MaxWidth(340));
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"[{_behaviour.CurrentCharName}] {msg.Text}", ChatStyles.ChatAIStyle, GUILayout.MaxWidth(340));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();

            // Status
            if (!string.IsNullOrEmpty(_statusText) && Time.time - _statusTime < 10f)
            {
                Color old = GUI.color;
                GUI.color = _statusIsError ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.6f);
                GUILayout.Label(_statusText, ChatStyles.StatusStyle);
                GUI.color = old;
            }

            // Input
            GUILayout.Space(4);

            if (_behaviour.NativeDialogueInputEnabled)
            {
                var nativeHandler = Game.NativeDialogueInputHandler.Instance;
                bool isNativeActive = nativeHandler != null && nativeHandler.IsInputActive;

                GUILayout.BeginHorizontal();
                string statusBtn = isNativeActive 
                    ? "[Diálogo nativo activo (escribiendo...)]" 
                    : $"[Abrir diálogo nativo ({_behaviour.NativeSpeakerName})]";
                if (GUILayout.Button(statusBtn, isNativeActive ? ChatStyles.ActiveButtonStyle : ChatStyles.ButtonStyle))
                {
                    if (isNativeActive) nativeHandler.CancelInput(closeBox: true);
                    else nativeHandler.StartInput();
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            GUILayout.BeginHorizontal();

            bool enterPressed = Event.current.type == EventType.KeyDown &&
                               (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                               GUI.GetNameOfFocusedControl() == "ChatInput";

            GUI.SetNextControlName("ChatInput");
            string prevInput = _inputText;
            _inputText = GUILayout.TextField(_inputText, ChatStyles.InputStyle, GUILayout.ExpandWidth(true));

            // Sync with NativeDialogueInputHandler if user is typing in IMGUI
            if (_behaviour.NativeDialogueInputEnabled && _inputText != prevInput && Game.NativeDialogueInputHandler.Instance != null)
            {
                Game.NativeDialogueInputHandler.Instance.CurrentInput = _inputText;
                Game.NativeDialogueInputHandler.Instance.UpdateDialogueText();
            }

            if (_focusInputPending)
            {
                GUI.FocusControl("ChatInput");
                _focusInputPending = false;
            }

            bool canSend = !AwaitingResponse &&
                           !string.IsNullOrEmpty(_inputText.Trim()) &&
                           !string.IsNullOrEmpty(_behaviour.CurrentCharName);

            if ((GUILayout.Button("Send", canSend ? ChatStyles.ActiveButtonStyle : ChatStyles.ButtonStyle, GUILayout.Width(60)) || enterPressed) && canSend)
            {
                if (Game.NativeDialogueInputHandler.Instance != null && Game.NativeDialogueInputHandler.Instance.IsInputActive)
                {
                    Game.NativeDialogueInputHandler.Instance.SubmitInput();
                }
                else
                {
                    SendMessage();
                }
            }

            GUILayout.EndHorizontal();

            if (AwaitingResponse)
                GUILayout.Label("[...] Waiting for AI response...", ChatStyles.StatusStyle);

            // Quick actions
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear", ChatStyles.ButtonStyle, GUILayout.Width(60)))
            {
                _behaviour.ChatHistory.Clear();
                _behaviour.ChatSummary = "";
                if (_behaviour.Memory != null)
                {
                    _behaviour.Memory.Summary = "";
                    _behaviour.Memory.Save();
                }
            }
            GUILayout.FlexibleSpace();
            if (_behaviour.CurrentEmotions != null)
                GUILayout.Label($"Trust: {_behaviour.CurrentEmotions.Trust:F0}", ChatStyles.LabelStyle);
            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════
        //  TAB: Settings
        // ═══════════════════════════════════════════════════════════

        private void DrawSettings()
        {
            GUILayout.Label("[SET] Settings", ChatStyles.SectionStyle);
            GUILayout.Space(4);

            // Provider
            GUILayout.Label("AI Provider:", ChatStyles.LabelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ollama", _editProvider == ProviderType.Ollama ? ChatStyles.ActiveButtonStyle : ChatStyles.ButtonStyle)) _editProvider = ProviderType.Ollama;
            if (GUILayout.Button("Gemini", _editProvider == ProviderType.Gemini ? ChatStyles.ActiveButtonStyle : ChatStyles.ButtonStyle)) _editProvider = ProviderType.Gemini;
            if (GUILayout.Button("Gemma",  _editProvider == ProviderType.Gemma  ? ChatStyles.ActiveButtonStyle : ChatStyles.ButtonStyle)) _editProvider = ProviderType.Gemma;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (_editProvider == ProviderType.Ollama)
            {
                GUILayout.Label("Ollama URL:",   ChatStyles.LabelStyle);
                _editOllamaUrl   = GUILayout.TextField(_editOllamaUrl,   ChatStyles.InputStyle);
                GUILayout.Label("Ollama Model:", ChatStyles.LabelStyle);
                _editOllamaModel = GUILayout.TextField(_editOllamaModel, ChatStyles.InputStyle);
            }
            else if (_editProvider == ProviderType.Gemini)
            {
                GUILayout.Label("Gemini API Key:", ChatStyles.LabelStyle);
                _editApiKey = GUILayout.TextField(_editApiKey, ChatStyles.InputStyle);
            }
            else if (_editProvider == ProviderType.Gemma)
            {
                GUILayout.Label("Gemma URL:",   ChatStyles.LabelStyle);
                _editGemmaUrl   = GUILayout.TextField(_editGemmaUrl,   ChatStyles.InputStyle);
                GUILayout.Label("Gemma Model:", ChatStyles.LabelStyle);
                _editGemmaModel = GUILayout.TextField(_editGemmaModel, ChatStyles.InputStyle);
            }

            GUILayout.Space(6);

            // Hotkey (editable)
            GUILayout.Label("Chat Hotkey:", ChatStyles.LabelStyle);
            GUILayout.BeginHorizontal();
            if (_listeningForKey)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.2f);
                GUILayout.Label("Press any key... (Esc to cancel)", ChatStyles.StatusStyle, GUILayout.ExpandWidth(true));
                GUI.color = prev;
            }
            else
            {
                GUILayout.Label(_editHotkeyDisplay, ChatStyles.LabelStyle, GUILayout.Width(80));
                if (GUILayout.Button("[Change]", ChatStyles.ButtonStyle))
                {
                    _listeningForKey = true;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label($"Max History: {_behaviour.MaxHistoryMessages}", ChatStyles.LabelStyle);

            // Trust modification toggle
            GUILayout.BeginHorizontal();
            GUILayout.Label("Allow Trust Mod:", ChatStyles.LabelStyle);
            if (GUILayout.Button(_behaviour.AllowTrustModification ? "[ON]" : "[OFF]",
                _behaviour.AllowTrustModification ? ChatStyles.ActiveButtonStyle : ChatStyles.ButtonStyle,
                GUILayout.Width(50)))
            {
                _behaviour.AllowTrustModification = !_behaviour.AllowTrustModification;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // ── Command toggles ──────────────────────────────────
            GUILayout.Label("AI Commands:", ChatStyles.SectionStyle);
            DrawCmdToggle("Follow/Unfollow",  ref _behaviour.CmdFollowEnabled);
            DrawCmdToggle("Go to a place",    ref _behaviour.CmdGoToEnabled);
            DrawCmdToggle("Clothes put/remove",ref _behaviour.CmdClothesEnabled);
            DrawCmdToggle("Kiss",              ref _behaviour.CmdKissEnabled);
            DrawCmdToggle("Trust mod",         ref _behaviour.CmdTrustModEnabled);
            DrawCmdToggle("Emotion mod",       ref _behaviour.CmdEmotionModEnabled);
            DrawCmdToggle("Make face",         ref _behaviour.CmdMakeFaceEnabled);
            DrawCmdToggle("Force say",         ref _behaviour.CmdSayEnabled);

            GUILayout.Space(6);
            GUILayout.Label("Native Dialogue Box Input:", ChatStyles.SectionStyle);
            DrawCmdToggle("Native Dialogue", ref _editNativeDialogueInput);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Speaker Name:", ChatStyles.DebugLabelStyle, GUILayout.Width(140));
            _editNativeSpeakerName = GUILayout.TextField(_editNativeSpeakerName, ChatStyles.InputStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            if (GUILayout.Button("Apply & Save", ChatStyles.ActiveButtonStyle))
            {
                _behaviour.Provider    = _editProvider;
                _behaviour.OllamaUrl   = _editOllamaUrl;
                _behaviour.OllamaModel = _editOllamaModel;
                _behaviour.GemmaUrl    = _editGemmaUrl;
                _behaviour.GemmaModel  = _editGemmaModel;
                _behaviour.ApiKey      = _editApiKey;
                _behaviour.NativeDialogueInputEnabled = _editNativeDialogueInput;
                _behaviour.NativeSpeakerName = string.IsNullOrWhiteSpace(_editNativeSpeakerName) ? "You" : _editNativeSpeakerName.Trim();
                if (Game.NativeDialogueInputHandler.Instance != null)
                {
                    Game.NativeDialogueInputHandler.Instance.Enabled = _behaviour.NativeDialogueInputEnabled;
                    Game.NativeDialogueInputHandler.Instance.SpeakerName = _behaviour.NativeSpeakerName;
                }
                _behaviour.RebuildApiClient();
                SaveConfig();
                SetStatus("Settings saved to BepInEx/config!", false);
                _activeTab = 0;
            }
        }

        private void DrawCmdToggle(string label, ref bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, ChatStyles.DebugLabelStyle, GUILayout.Width(140));
            if (GUILayout.Button(value ? "[ON]" : "[OFF]",
                value ? ChatStyles.ActiveButtonStyle : ChatStyles.ButtonStyle,
                GUILayout.Width(48)))
            {
                value = !value;
            }
            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════
        //  TAB: Debug
        // ═══════════════════════════════════════════════════════════

        private void DrawDebug()
        {
            GUILayout.Label("[DBG] Live NPC Analyzer", ChatStyles.SectionStyle);
            GUILayout.Space(4);

            _debugScrollPosition = GUILayout.BeginScrollView(_debugScrollPosition, ChatStyles.ChatAreaStyle, GUILayout.ExpandHeight(true));

            var db = _behaviour;

            // ── System ─────────────────────────────────────────
            GUILayout.Label("-- System --", ChatStyles.DebugHeaderStyle);
            DrawDebugRow("Active NPC",    string.IsNullOrEmpty(db.CurrentCharName) ? "(none)" : db.CurrentCharName);
            DrawDebugRow("Provider",      db.Provider.ToString());
            DrawDebugRow("Hotkey",        db.ChatHotkey.ToString());
            DrawDebugRow("History count", db.ChatHistory.Count.ToString());
            DrawDebugRow("Awaiting AI",   AwaitingResponse.ToString());
            DrawDebugRow("FPS",           $"{(1f / Time.deltaTime):F1}");

            GUILayout.Space(6);

            if (db.CurrentEmotions != null)
            {
                var e = db.CurrentEmotions;

                // ── Core stats ─────────────────────────────────
                GUILayout.Label("-- Emotions & Stats --", ChatStyles.DebugHeaderStyle);
                DrawDebugRow("Trust",        $"{e.Trust:F1}");
                DrawDebugRow("Level",        e.Level.ToString());
                DrawDebugRow("Submission",   e.Submission.ToString());
                DrawDebugRow("Perversion",   e.Perversion.ToString());
                DrawDebugRow("Desire",       e.Desire.ToString());
                DrawDebugRow("HornyLevel",   $"{e.HornyLevel:F1}");
                DrawDebugRow("IsBeingFucked",e.isBeingFucked.ToString());
                DrawDebugRow("WasFuckedToday",e.wasFuckedToday.ToString());
                DrawDebugRow("HadOrgasm",    e.hadOrgasmToday.ToString());
                DrawDebugRow("OrgasmCount",  e.OrgasmCount.ToString());
                DrawDebugRow("PregnancyDay", e.PregnancyDate.ToString());
                DrawDebugRow("DaysNoSex",    e.DaysWithoutSex.ToString());
                DrawDebugRow("Idle",         e.Idle.ToString());

                GUILayout.Space(4);

                // ── Thresholds ─────────────────────────────────
                GUILayout.Label("-- Action Thresholds --", ChatStyles.DebugHeaderStyle);
                DrawThresholdRow("Kiss",    e.ValueToKiss,    e.Trust);
                DrawThresholdRow("Follow",  e.ValueToFollow,  e.Trust);
                DrawThresholdRow("Undress", e.ValueToUndress, e.Trust);
                DrawThresholdRow("Touch",   e.ValueToTouch,   e.Trust);
                DrawThresholdRow("Sex",     e.ValueToSex,     e.Trust);

                GUILayout.Space(4);

                // ── Virginity / Experience ──────────────────────
                GUILayout.Label("-- Experience --", ChatStyles.DebugHeaderStyle);
                DrawDebugRow("Vaginal Virgin",  e.isVaginalVirgin  == 1 ? "Yes" : $"No ({e.AmountOfVaginalSex}x)");
                DrawDebugRow("Anal Virgin",     e.isAnalVirgin     == 1 ? "Yes" : $"No ({e.AmountOfAnalSex}x)");
                DrawDebugRow("Oral Virgin",     e.isOralVirgin     == 1 ? "Yes" : $"No ({e.AmountOfOralSex}x)");
            }
            else
            {
                GUILayout.Label("(No Emotions component — no NPC nearby)", ChatStyles.StatusStyle);
            }

            GUILayout.Space(4);

            if (db.CurrentNavigator != null)
            {
                var nav = db.CurrentNavigator;
                GUILayout.Label("-- Navigator --", ChatStyles.DebugHeaderStyle);
                DrawDebugRow("Following",    nav.Follow.ToString());
                DrawDebugRow("Working",      nav.Working.ToString());
                DrawDebugRow("PauseWork",    nav.PauseWork.ToString());
                if (nav.CurrentTask != null)
                    DrawDebugRow("Task", nav.CurrentTask.Desc ?? "(no desc)");

                var spots = Game.LocationCatalog.Collect(nav);
                DrawDebugRow("Places here",  spots.Count.ToString());
                DrawDebugRow("GoTo order",   Game.NpcGoToHandler.HasActiveOrder ? Game.NpcGoToHandler.ActivePlaceName : "(none)");
            }

            GUILayout.Space(4);

            if (db.CurrentInteractionManager != null)
            {
                var mgr = db.CurrentInteractionManager;
                GUILayout.Label("-- Clothing --", ChatStyles.DebugHeaderStyle);
                DrawDebugRow("IsNaked", mgr.IsNaked.ToString());
                DrawClothRow("Dress",   mgr.Dress);
                DrawClothRow("Shirt",   mgr.Shirt);
                DrawClothRow("Pants",   mgr.Pants);
                DrawClothRow("Skirt",   mgr.Skirt);
                DrawClothRow("Bra",     mgr.Bra);
                DrawClothRow("Panty",   mgr.Panty);
                DrawClothRow("Socks",   mgr.SocksStocking);
                DrawClothRow("Shoes",   mgr.Shoes);
                DrawClothRow("Jacket",  mgr.Jacket);
                DrawClothRow("Belt",    mgr.Belt);
                DrawClothRow("Glasses", mgr.Glasses);
                DrawClothRow("Gloves",  mgr.Gloves);
                DrawClothRow("Hat",     mgr.Hat);
                DrawClothRow("Collar",  mgr.Collar);
            }

            GUILayout.Space(4);

            if (db.CurrentTalkInteractions != null)
            {
                var ti = db.CurrentTalkInteractions;
                GUILayout.Label("-- Talk Interactions --", ChatStyles.DebugHeaderStyle);
                DrawDebugRow("PointsToday",    $"{ti.PointsToday:F1}");
                DrawDebugRow("MaxPointsToday", $"{ti.MaxPointsToday:F1}");
                DrawDebugRow("MaxPoints",      $"{ti.MaxPoints:F1}");
                DrawDebugRow("IsRandom",       ti.IsARandomCharacter.ToString());
            }

            GUILayout.Space(4);

            // Memory
            GUILayout.Label("-- Memory --", ChatStyles.DebugHeaderStyle);
            DrawDebugRow("Char bound",   db.Memory.CharacterName ?? "(none)");
            DrawDebugRow("Exchanges",    db.Memory.TotalExchangeCount.ToString());
            DrawDebugRow("Facts count",  db.Memory.KeyFacts.Count.ToString());
            DrawDebugRow("Summary len",  (db.ChatSummary?.Length ?? 0).ToString() + " chars");
            if (!string.IsNullOrEmpty(db.ChatSummary))
            {
                string preview = db.ChatSummary.Length > 80 ? db.ChatSummary.Substring(0, 77) + "..." : db.ChatSummary;
                DrawDebugRow("Summary", preview);
            }

            GUILayout.EndScrollView();

            // Quick test buttons
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force Reinit Styles", ChatStyles.ButtonStyle))
            {
                ChatStyles.Initialized = false;
                ChatStyles.Initialize();
                SetStatus("Styles reinitialized.", false);
            }
            if (GUILayout.Button("Clear Memory", ChatStyles.ButtonStyle))
            {
                db.ResetMemory();
                SetStatus("Memory cleared.", false);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Dump locations", ChatStyles.ButtonStyle))
            {
                string path = db.CurrentNavigator != null
                    ? Game.LocationCatalog.Dump(db.CurrentNavigator)
                    : null;
                SetStatus(path != null
                    ? "Places written to BepInEx/config/AIChat/locations_dump.txt"
                    : "Could not write locations_dump.txt (no NPC tracked).", path == null);
            }
            if (GUILayout.Button("List places", ChatStyles.ButtonStyle))
                SetStatus(Game.NpcGoToHandler.ListPlaces(), false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Dump animations", ChatStyles.ButtonStyle))
            {
                string path = Game.AnimationCatalog.Dump();
                SetStatus(path != null
                    ? "Animations written to BepInEx/config/AIChat/animations_dump.txt"
                    : "Could not write animations_dump.txt.", path == null);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawDebugRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", ChatStyles.DebugLabelStyle, GUILayout.Width(130));
            GUILayout.Label(value,       ChatStyles.DebugValueStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawThresholdRow(string action, float threshold, float trust)
        {
            bool meets = trust >= threshold;
            Color old = GUI.color;
            GUI.color = meets ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.45f, 0.4f);
            DrawDebugRow(action, $"Need {threshold:F0} | Have {trust:F0} [{(meets ? "OK" : "NO")}]");
            GUI.color = old;
        }

        private void DrawClothRow(string name, GameObject go)
        {
            if (go == null) return;
            bool on = go.activeSelf;
            Color old = GUI.color;
            GUI.color = on ? new Color(0.4f, 1f, 0.5f) : new Color(0.6f, 0.6f, 0.6f);
            DrawDebugRow(name, on ? "ON (worn)" : "OFF (removed)");
            GUI.color = old;
        }

        // ═══════════════════════════════════════════════════════════
        //  Send / Scroll / Status
        // ═══════════════════════════════════════════════════════════

        public void SendUserMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _inputText = "";

            _behaviour.ChatHistory.Add(new ChatMessage(true, message));
            _behaviour.Memory.TotalExchangeCount++;
            _behaviour.Memory.AutoTag(message, false);
            ScrollToBottom();

            AwaitingResponse = true;
            SetStatus("Sending to AI...", false);

            _behaviour.StartCoroutine(_behaviour.APIClient.SendMessage(message));
        }

        private void SendMessage()
        {
            SendUserMessage(_inputText.Trim());
        }

        public void ScrollToBottom() => _scrollPosition = new Vector2(0, float.MaxValue);

        public void SetStatus(string text, bool isError)
        {
            _statusText    = text;
            _statusIsError = isError;
            _statusTime    = Time.time;
            if (isError)
            {
                Game.NativeDialogueInputHandler.Instance?.OnAIResponseReady();
            }
        }

        public void ShowEmotionFeedback(string text, bool isPositive) => SetStatus(text, !isPositive);

        // ═══════════════════════════════════════════════════════════
        //  Config: BepInEx/config/AIChat/HLHN_AIChatMod.ini
        // ═══════════════════════════════════════════════════════════

        private string GetIniPath()
        {
            Plugin.EnsureConfigDir();
            return Plugin.GetConfigFilePath(Plugin.SettingsFileName);
        }

        public void SaveConfig()
        {
            try
            {
                var lines = new List<string>
                {
                    "[AI]",
                    $"Provider={_behaviour.Provider}",
                    $"OllamaUrl={_behaviour.OllamaUrl}",
                    $"OllamaModel={_behaviour.OllamaModel}",
                    $"GemmaUrl={_behaviour.GemmaUrl}",
                    $"GemmaModel={_behaviour.GemmaModel}",
                    $"ApiKey={_behaviour.ApiKey}",
                    "",
                    "[Mod]",
                    $"Hotkey={_behaviour.ChatHotkey}",
                    $"MaxHistory={_behaviour.MaxHistoryMessages}",
                    $"AllowTrustMod={_behaviour.AllowTrustModification}",
                    $"NativeDialogueInput={_behaviour.NativeDialogueInputEnabled}",
                    $"NativeSpeakerName={_behaviour.NativeSpeakerName}",
                    "",
                    "[Commands]",
                    $"Follow={_behaviour.CmdFollowEnabled}",
                    $"GoTo={_behaviour.CmdGoToEnabled}",
                    $"Clothes={_behaviour.CmdClothesEnabled}",
                    $"Kiss={_behaviour.CmdKissEnabled}",
                    $"TrustMod={_behaviour.CmdTrustModEnabled}",
                    $"EmotionMod={_behaviour.CmdEmotionModEnabled}",
                    $"MakeFace={_behaviour.CmdMakeFaceEnabled}",
                    $"Say={_behaviour.CmdSayEnabled}",
                };
                File.WriteAllLines(GetIniPath(), lines);
                Plugin.LogInfo($"Config saved to: {GetIniPath()}");
            }
            catch (Exception ex)
            {
                Plugin.LogWarning($"Could not save config: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            string path = GetIniPath();
            if (!File.Exists(path)) return;

            try
            {
                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("[") || trimmed.StartsWith(";") || trimmed.Length == 0) continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;
                    string key = trimmed.Substring(0, eq).Trim();
                    string val = trimmed.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "Provider":
                            if (Enum.TryParse<ProviderType>(val, out var prov))
                                _behaviour.Provider = prov;
                            break;
                        case "OllamaUrl":    _behaviour.OllamaUrl   = val; break;
                        case "OllamaModel":  _behaviour.OllamaModel = val; break;
                        case "GemmaUrl":
                            // Legacy default pointed at LM Studio's OpenAI-style endpoint, which
                            // GemmaClient (Ollama /api/generate protocol) cannot talk to.
                            if (val == "http://localhost:1234/v1")
                            {
                                _behaviour.GemmaUrl = "http://localhost:11434/api/generate";
                                Plugin.LogInfo("GemmaUrl: migrated legacy LM Studio default to http://localhost:11434/api/generate (Ollama /api/generate).");
                            }
                            else
                            {
                                _behaviour.GemmaUrl = val;
                            }
                            break;
                        case "GemmaModel":   _behaviour.GemmaModel  = val; break;
                        case "ApiKey":       _behaviour.ApiKey      = val; break;
                        case "Hotkey":
                            if (Enum.TryParse<KeyCode>(val, out var kc))
                            {
                                _behaviour.ChatHotkey = kc;
                                _editHotkeyDisplay    = kc.ToString();
                            }
                            break;
                        case "MaxHistory":
                            if (int.TryParse(val, out int maxH))
                                _behaviour.MaxHistoryMessages = maxH;
                            break;
                        case "AllowTrustMod":
                            if (bool.TryParse(val, out bool atm))
                                _behaviour.AllowTrustModification = atm;
                            break;
                        case "NativeDialogueInput":
                            if (bool.TryParse(val, out bool ndi))
                            {
                                _behaviour.NativeDialogueInputEnabled = ndi;
                                _editNativeDialogueInput = ndi;
                                if (Game.NativeDialogueInputHandler.Instance != null)
                                    Game.NativeDialogueInputHandler.Instance.Enabled = ndi;
                            }
                            break;
                        case "NativeSpeakerName":
                            if (!string.IsNullOrEmpty(val))
                            {
                                _behaviour.NativeSpeakerName = val;
                                _editNativeSpeakerName = val;
                                if (Game.NativeDialogueInputHandler.Instance != null)
                                    Game.NativeDialogueInputHandler.Instance.SpeakerName = val;
                            }
                            break;
                        case "Follow":
                            if (bool.TryParse(val, out bool cf)) _behaviour.CmdFollowEnabled = cf; break;
                        case "GoTo":
                            if (bool.TryParse(val, out bool cg)) _behaviour.CmdGoToEnabled = cg; break;
                        case "Clothes":
                            if (bool.TryParse(val, out bool cc)) _behaviour.CmdClothesEnabled = cc; break;
                        case "Kiss":
                            if (bool.TryParse(val, out bool ck)) _behaviour.CmdKissEnabled = ck; break;
                        case "TrustMod":
                            if (bool.TryParse(val, out bool ct)) _behaviour.CmdTrustModEnabled = ct; break;
                        case "EmotionMod":
                            if (bool.TryParse(val, out bool ce)) _behaviour.CmdEmotionModEnabled = ce; break;
                        case "MakeFace":
                            if (bool.TryParse(val, out bool cmf)) _behaviour.CmdMakeFaceEnabled = cmf; break;
                        case "Say":
                            if (bool.TryParse(val, out bool cs)) _behaviour.CmdSayEnabled = cs; break;
                    }
                }

                // Sync edit fields after loading
                _editOllamaUrl   = _behaviour.OllamaUrl;
                _editOllamaModel = _behaviour.OllamaModel;
                _editGemmaUrl    = _behaviour.GemmaUrl;
                _editGemmaModel  = _behaviour.GemmaModel;
                _editApiKey      = _behaviour.ApiKey;
                _editProvider    = _behaviour.Provider;
                _editNativeDialogueInput = _behaviour.NativeDialogueInputEnabled;
                _editNativeSpeakerName   = _behaviour.NativeSpeakerName;

                Plugin.LogInfo($"Config loaded from: {path}");
            }
            catch (Exception ex)
            {
                Plugin.LogWarning($"Could not load config: {ex.Message}");
            }
        }

        public void SaveChatHistory()
        {
            try
            {
                if (string.IsNullOrEmpty(_behaviour.CurrentCharName)) return;

                // BepInEx/config/AIChat/history (see Plugin.GetDataDir), not the Unity save folder.
                string dir = Plugin.GetDataDir(Plugin.HistoryDirName);
                if (string.IsNullOrEmpty(dir)) return;

                string path = Path.Combine(dir, $"{_behaviour.CurrentCharName}_history.txt");
                var lines = new List<string>();
                foreach (var msg in _behaviour.ChatHistory)
                    lines.Add(msg.ToLine());
                File.WriteAllLines(path, lines);

                _behaviour.Memory.Save();
            }
            catch (Exception ex)
            {
                Plugin.LogDebug($"Could not save chat history: {ex.Message}");
            }
        }
    }
}
