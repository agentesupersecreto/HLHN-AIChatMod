using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DialogInterceptorMod.Models;
using DialogInterceptorMod.UI;
using DialogInterceptorMod.API;

namespace DialogInterceptorMod.Core
{
    /// <summary>
    /// Active AI provider selection.
    /// </summary>
    public enum ProviderType { Gemini, Ollama, Gemma }

    /// <summary>
    /// Core behaviour that manages the AI chat system for HLHN.
    /// Tracks the current NPC via SpeachListener, extracts character info
    /// from Emotions/TalkInteractions, and routes AI responses through Speach.Say().
    /// </summary>
    public class DialogBehaviour : MonoBehaviour
    {
        public static DialogBehaviour Instance { get; private set; }

        public ChatWindow Window { get; private set; }
        public IApiClient APIClient { get; private set; }

        public List<ChatMessage> ChatHistory { get; private set; } = new List<ChatMessage>();
        public string ChatSummary = "";
        public string CustomPrompt = "";
        private DateTime _customPromptWriteTime = DateTime.MinValue;

        // --- Config fields ---
        public string ApiKey = "";
        public ProviderType Provider = ProviderType.Ollama;
        public string OllamaUrl = "http://localhost:11434/api/chat";
        public string OllamaModel = "gemma3:4b";
        public string GemmaUrl = "http://localhost:11434/api/generate";
        public string GemmaModel = "gemma3:4b";
        public KeyCode ChatHotkey { get; set; } = KeyCode.F9;

        // --- Mod settings ---
        public int MaxHistoryMessages { get; set; } = 30;
        public bool AllowTrustModification { get; set; } = true;

        // --- Command toggles (fields, not properties, so the Settings tab can pass them by ref) ---
        public bool CmdFollowEnabled      = true;
        public bool CmdGoToEnabled        = true;
        public bool CmdClothesEnabled     = true;
        public bool CmdKissEnabled        = true;
        public bool CmdTrustModEnabled    = true;
        public bool CmdEmotionModEnabled  = true;
        public bool CmdMakeFaceEnabled    = true;
        public bool CmdSayEnabled         = true;

        // --- Native Dialogue Box Input ---
        public bool NativeDialogueInputEnabled = true;
        public string NativeSpeakerName        = "You";

        // --- Spam Filter ---
        public SpamFilter SpamFilter { get; private set; } = new SpamFilter();

        // --- Conversation Memory ---
        public ConversationMemory Memory { get; private set; } = new ConversationMemory();

        // --- Current NPC tracking ---
        public string CurrentCharName { get; set; } = "";
        public Speach CurrentSpeach { get; set; }
        public Emotions CurrentEmotions { get; set; }
        public TalkInteractions CurrentTalkInteractions { get; set; }
        public NpcNavigatorAndRutines CurrentNavigator { get; set; }
        public NpcInteractionManager CurrentInteractionManager { get; set; }

        private string _lastCharName = "";
        private float _noNpcTimer = 0f;
        private const float NO_NPC_GRACE_SECONDS = 2f;

        // Speach.Say() drops the line while the NPC is still speaking, so the AI reply is
        // retried until the bubble is free (or these limits are reached).
        private const float AI_SPEECH_RETRY_INTERVAL = 0.3f;
        private const float AI_SPEECH_MAX_WAIT_SECONDS = 15f;
        private Coroutine _aiSpeechRoutine;

        // SpeachListener is cached because it lives in the scene: on a scene change it is
        // destroyed (the Unity fake-null makes the cache invalidate itself) and then found again.
        private SpeachListener _speachListener;
        private float _nextListenerSearchTime;
        private float _nextNotMoveSearchTime;

        /// <summary>Back-off applied to repeated failed scene lookups (both are called from Update()).</summary>
        private const float SCENE_LOOKUP_RETRY_INTERVAL = 1f;

        public static void Initialize()
        {
            var go = new GameObject("HLHNAIChat");
            go.hideFlags |= (HideFlags)61; // HideAndDontSave
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<DialogBehaviour>();
            Plugin.LogInfo("Persistent GameObject 'HLHNAIChat' created.");
        }

        private GameObject _chatMenuDummy;
        private NotMove _notMove;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;

            _chatMenuDummy = new GameObject("AIChat_MenuDummy");
            _chatMenuDummy.transform.SetParent(this.transform);
            _chatMenuDummy.SetActive(false);

            Window = new ChatWindow(this);
            Game.NativeDialogueInputHandler.Init();

            // Creates BepInEx/config/AIChat/{system_prompt,custom_prompt,locations}.txt on first run
            PromptBuilder.EnsurePromptFiles();
            Game.LocationCatalog.EnsureLocationsFile();
        }

        private void Start()
        {
            Window.Initialize();
            RebuildApiClient();
        }

        /// <summary>
        /// Returns the contents of custom_prompt.txt, re-reading it whenever the file changes so
        /// edits apply without restarting the game. Lines starting with # or // are comments.
        /// </summary>
        public string GetCustomPrompt()
        {
            string path = Plugin.GetConfigFilePath(Plugin.CustomPromptFileName);
            try
            {
                if (!System.IO.File.Exists(path))
                {
                    _customPromptWriteTime = DateTime.MinValue;
                    CustomPrompt = "";
                    return CustomPrompt;
                }

                DateTime writeTime = System.IO.File.GetLastWriteTimeUtc(path);
                if (writeTime == _customPromptWriteTime) return CustomPrompt;

                _customPromptWriteTime = writeTime;
                CustomPrompt = StripCommentLines(System.IO.File.ReadAllText(path));
                Plugin.LogInfo($"{Plugin.CustomPromptFileName} loaded ({CustomPrompt.Length} chars).");
            }
            catch (Exception ex)
            {
                Plugin.LogWarning($"Could not read {Plugin.CustomPromptFileName}: {ex.Message}");
            }

            return CustomPrompt;
        }

        /// <summary>
        /// Drops comment lines (# or //) and blank lines from a user-editable prompt file.
        /// </summary>
        private static string StripCommentLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new System.Text.StringBuilder();
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#") || line.StartsWith("//")) continue;
                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Recreates the API client based on the active provider.
        /// </summary>
        /// <summary>Resets the conversation memory for the current character (used by debug panel).</summary>
        public void ResetMemory()
        {
            Memory = new ConversationMemory();
            ChatSummary = "";
            if (!string.IsNullOrEmpty(CurrentCharName))
            {
                Memory.Bind(CurrentCharName);
                Memory.Save();
            }
        }

        public void RebuildApiClient()
        {
            switch (Provider)
            {
                case ProviderType.Ollama:
                    APIClient = new OllamaClient(this);
                    Plugin.LogInfo($"Using Ollama ({OllamaModel}) at {OllamaUrl}");
                    break;
                case ProviderType.Gemma:
                    APIClient = new GemmaClient(this);
                    Plugin.LogInfo($"Using Gemma ({GemmaModel}) at {GemmaUrl}");
                    break;
                default:
                    APIClient = new GeminiClient(this);
                    Plugin.LogInfo("Using Gemini API.");
                    break;
            }
        }

        public void RefreshNotMove()
        {
            if (_notMove != null) return;

            // Same reasoning as GetSpeachListener(): NotMove is absent in some scenes and
            // Update() would otherwise run a full-scene search every single frame.
            if (Time.time < _nextNotMoveSearchTime) return;

            _notMove = FindFirstObjectByType<NotMove>();
            if (_notMove == null)
            {
                _nextNotMoveSearchTime = Time.time + SCENE_LOOKUP_RETRY_INTERVAL;
                return;
            }

            if (_chatMenuDummy != null)
            {
                _notMove.AddMenu(_chatMenuDummy);
                Plugin.LogInfo("Registered AIChat dummy menu with NotMove");
            }
        }

        public void OnChatVisibilityChanged(bool visible)
        {
            RefreshNotMove();

            if (_chatMenuDummy != null)
            {
                _chatMenuDummy.SetActive(visible);
            }

            if (visible)
            {
                if (_notMove != null)
                {
                    _notMove.notmove = true;
                }
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            else
            {
                // When closing chat window: check if any other game menu is active
                bool otherMenuActive = false;
                if (_notMove != null && _notMove.ActiveMenus != null)
                {
                    for (int i = 0; i < _notMove.ActiveMenus.Length; i++)
                    {
                        var m = _notMove.ActiveMenus[i];
                        if (m != null && m != _chatMenuDummy && m.activeSelf)
                        {
                            otherMenuActive = true;
                            break;
                        }
                    }
                }

                if (!otherMenuActive && (Game.NativeDialogueInputHandler.Instance == null || !Game.NativeDialogueInputHandler.Instance.IsInputActive))
                {
                    if (_notMove != null)
                    {
                        _notMove.notmove = false;
                    }
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Confined;
                }
            }
        }

        /// <summary>
        /// Explicitly binds the targeted NPC (e.g. from the radial menu) as the active conversational partner.
        /// </summary>
        public void SetTargetNpc(NpcNavigatorAndRutines nav)
        {
            if (nav == null) return;
            var speach = nav.GetComponent<Speach>();
            if (speach == null) return;

            string npcName = speach.transform.name;
            _noNpcTimer = 0f;

            if (npcName != _lastCharName)
            {
                if (!string.IsNullOrEmpty(_lastCharName))
                {
                    Memory.Summary = ChatSummary;
                    Memory.Save();
                    ChatHistory.Clear();
                    ChatSummary = "";
                    Plugin.LogInfo($"Character changed via radial menu: {_lastCharName} → {npcName}. History cleared.");
                }

                _lastCharName = npcName;
                Memory = new ConversationMemory();
                Memory.Bind(npcName);
                Memory.Load();
                ChatSummary = Memory.Summary;
            }

            CurrentCharName = npcName;
            CurrentSpeach = speach;
            CurrentEmotions = speach.GetComponent<Emotions>();
            CurrentTalkInteractions = speach.GetComponent<TalkInteractions>();
            CurrentNavigator = nav;
            CurrentInteractionManager = speach.GetComponent<NpcInteractionManager>();

            if (CurrentEmotions != null)
            {
                Memory.UpdateMilestones(CurrentEmotions, CurrentInteractionManager);
            }
        }

        public void SetChatMenuDummyActive(bool active)
        {
            RefreshNotMove();
            if (_chatMenuDummy != null)
            {
                _chatMenuDummy.SetActive(active);
            }
        }

        private void Update()
        {
            RefreshNotMove();
            Window.HandleUpdate();
            Game.NativeDialogueInputHandler.Instance?.HandleUpdate();

            // Refresh current NPC binding from SpeachListener
            RefreshCurrentNpc();

            // Flush spam filter batches
            if (SpamFilter.Enabled)
            {
                string batched = SpamFilter.FlushBatch();
                if (batched != null)
                {
                    ChatHistory.Add(ChatMessage.SystemMessage(batched));
                    Window.ScrollToBottom();
                }
            }

            // Trim history
            if (ChatHistory.Count > MaxHistoryMessages)
            {
                int toRemove = ChatHistory.Count - MaxHistoryMessages;
                ChatHistory.RemoveRange(0, toRemove);
            }
        }

        /// <summary>
        /// Adds a captured game event (NPC in-game speech, player interactions, command
        /// feedback) to the chat history through the spam filter, so in-game chatter obeys
        /// the same cooldown/dedup rules as the mod's own system messages.
        /// </summary>
        public void AddCapturedEvent(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (!SpamFilter.ShouldShow(message)) return;

            ChatHistory.Add(ChatMessage.SystemMessage(message));
            if (Window != null)
                Window.ScrollToBottom();
        }

        /// <summary>
        /// Finds the NPC the player is looking at via SpeachListener and caches references.
        /// </summary>
        /// <summary>
        /// Returns the scene's SpeachListener, searching only when the cached one is gone.
        /// Repeated failed lookups are throttled so a scene without a listener doesn't pay
        /// for a full-scene FindFirstObjectByType() every single frame.
        /// </summary>
        private SpeachListener GetSpeachListener()
        {
            if (_speachListener != null) return _speachListener;
            if (Time.time < _nextListenerSearchTime) return null;

            _speachListener = FindFirstObjectByType<SpeachListener>();
            // Found: allow an immediate re-search if it gets destroyed (scene change).
            // Not found: back off, so the timer only throttles consecutive misses.
            _nextListenerSearchTime = _speachListener != null ? 0f : Time.time + SCENE_LOOKUP_RETRY_INTERVAL;
            return _speachListener;
        }

        private void RefreshCurrentNpc()
        {
            try
            {
                var listener = GetSpeachListener();
                if (listener == null || listener.NpcHit == null)
                {
                    // Start grace period before clearing the active NPC
                    _noNpcTimer += Time.deltaTime;
                    if (_noNpcTimer >= NO_NPC_GRACE_SECONDS && !string.IsNullOrEmpty(CurrentCharName))
                    {
                        Plugin.LogDebug($"No NPC in range for {NO_NPC_GRACE_SECONDS}s — clearing active NPC: {CurrentCharName}");
                        Memory.Summary = ChatSummary;
                        Memory.Save();
                        CurrentCharName = "";
                        CurrentSpeach = null;
                        CurrentEmotions = null;
                        CurrentTalkInteractions = null;
                        CurrentNavigator = null;
                        CurrentInteractionManager = null;
                        _lastCharName = "";
                        ChatSummary = "";
                    }
                    return;
                }

                // NPC found — reset grace period timer
                _noNpcTimer = 0f;

                var speach = listener.NpcHit.GetComponentInParent<Speach>();
                if (speach == null) return;

                string npcName = speach.transform.name;

                // Detect NPC change
                if (npcName != _lastCharName)
                {
                    if (!string.IsNullOrEmpty(_lastCharName))
                    {
                        // Save memory for previous character
                        Memory.Summary = ChatSummary;
                        Memory.Save();

                        // Clear chat history on character swap
                        ChatHistory.Clear();
                        ChatSummary = "";
                        Plugin.LogInfo($"Character changed: {_lastCharName} → {npcName}. History cleared.");
                    }

                    _lastCharName = npcName;

                    // Load memory for new character
                    Memory = new ConversationMemory();
                    Memory.Bind(npcName);
                    Memory.Load();
                    ChatSummary = Memory.Summary; // Restore persistent summary
                }

                CurrentCharName = npcName;
                CurrentSpeach = speach;
                CurrentEmotions = speach.GetComponent<Emotions>();
                CurrentTalkInteractions = speach.GetComponent<TalkInteractions>();
                CurrentNavigator = speach.GetComponent<NpcNavigatorAndRutines>();
                CurrentInteractionManager = speach.GetComponent<NpcInteractionManager>();

                if (CurrentEmotions != null)
                {
                    Memory.UpdateMilestones(CurrentEmotions, CurrentInteractionManager);
                }
            }
            catch (Exception ex)
            {
                // Silently fail - NPC tracking is best-effort
                Plugin.LogDebug($"RefreshCurrentNpc error: {ex.Message}");
            }
        }

        /// <summary>
        /// Makes the current NPC say the AI's response via the game's native dialogue system.
        /// Speach.Say() silently drops the text while the NPC is already speaking, so that
        /// case is retried instead of losing the response.
        /// </summary>
        public void ShowAIResponseInGame(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (NativeDialogueInputEnabled && Game.NativeDialogueInputHandler.Instance != null)
            {
                Game.NativeDialogueInputHandler.Instance.ShowNpcSpeech(CurrentCharName, text);
                return;
            }

            Game.NativeDialogueInputHandler.Instance?.OnAIResponseReady();

            if (CurrentSpeach != null)
            {
                if (_aiSpeechRoutine != null)
                {
                    // A newer response supersedes the one still waiting for the bubble.
                    StopCoroutine(_aiSpeechRoutine);
                    _aiSpeechRoutine = null;
                    Plugin.LogDebug("Pending AI speech cancelled — superseded by a newer response.");
                }

                _aiSpeechRoutine = StartCoroutine(SayWhenIdle(CurrentSpeach, text));
                return;
            }

            // Fallback: no NPC bound — try ShowDialogue directly
            var showDialogue = FindFirstObjectByType<ShowDialogue>();
            if (showDialogue != null)
            {
                showDialogue.ShowSimpleWithName(text, CurrentCharName);
            }
        }

        /// <summary>
        /// Calls Speach.Say() until the component accepts the line. Say() only starts its
        /// routine while it is not speaking and reports nothing when it drops the text, so
        /// acceptance is detected through Speach.Dialogue: SayRutine() assigns it synchronously,
        /// before its first yield, which means the assignment is already visible right after Say().
        /// </summary>
        private IEnumerator SayWhenIdle(Speach speach, string text)
        {
            float deadline = Time.time + AI_SPEECH_MAX_WAIT_SECONDS;
            int attempts = 0;

            while (speach != null && Time.time < deadline)
            {
                // Guard the echo-loop check inside the Speach.Say Harmony patch.
                if (Window != null) Window.IsAIResponse = true;
                speach.Say(text);
                if (Window != null) Window.IsAIResponse = false;

                attempts++;

                if (speach == null || speach.Dialogue == text)
                    break;

                // NPC is mid-line: wait for the bubble to free up and try again.
                yield return new WaitForSeconds(AI_SPEECH_RETRY_INTERVAL);
            }

            _aiSpeechRoutine = null;

            if (speach != null && speach.Dialogue == text)
            {
                if (attempts > 1)
                    Plugin.LogDebug($"AI speech shown after {attempts} attempts (NPC was busy).");
                yield break;
            }

            Plugin.LogWarning($"NPC was busy — AI response not shown in the speech bubble after {attempts} attempt(s): {text}");
            if (Window != null)
                Window.SetStatus("[WARN] NPC was busy — reply kept in the chat window only.", true);
        }

        /// <summary>
        /// Captures a snapshot of the current character's data for the AI prompt.
        /// </summary>
        public CharacterContextSnapshot CaptureCharacterContext()
        {
            if (CurrentEmotions != null && Memory != null)
            {
                Memory.UpdateMilestones(CurrentEmotions, CurrentInteractionManager);
            }
            return CharacterContextSnapshot.Capture(CurrentEmotions, CurrentTalkInteractions, CurrentNavigator, CurrentInteractionManager, CurrentCharName);
        }

        private void LateUpdate()
        {
            if ((Window != null && Window.IsVisible) || (Game.NativeDialogueInputHandler.Instance != null && Game.NativeDialogueInputHandler.Instance.IsInputActive))
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void OnGUI()
        {
            if ((Window != null && Window.IsVisible) || (Game.NativeDialogueInputHandler.Instance != null && Game.NativeDialogueInputHandler.Instance.IsInputActive))
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            Window.Draw();
        }
    }
}
