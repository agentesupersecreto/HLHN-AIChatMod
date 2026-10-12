using System;
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

        // --- Config fields ---
        public string ApiKey = "";
        public ProviderType Provider = ProviderType.Ollama;
        public string OllamaUrl = "http://localhost:11434/api/chat";
        public string OllamaModel = "gemma3:4b";
        public string GemmaUrl = "http://localhost:1234/v1";
        public string GemmaModel = "gemma3:4b";
        public KeyCode ChatHotkey { get; set; } = KeyCode.F9;

        // --- Mod settings ---
        public int MaxHistoryMessages { get; set; } = 30;
        public bool AllowTrustModification { get; set; } = true;

        // --- Command toggles (fields, not properties, so the Settings tab can pass them by ref) ---
        public bool CmdFollowEnabled      = true;
        public bool CmdClothesEnabled     = true;
        public bool CmdKissEnabled        = true;
        public bool CmdTrustModEnabled    = true;
        public bool CmdEmotionModEnabled  = true;
        public bool CmdMakeFaceEnabled    = true;
        public bool CmdSayEnabled         = true;

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

        public static void Initialize()
        {
            var go = new GameObject("HLHNAIChat");
            go.hideFlags |= (HideFlags)61; // HideAndDontSave
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<DialogBehaviour>();
            Plugin.LogInfo("GameObject persistente 'HLHNAIChat' creado.");
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

            string customPromptPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Plugin.ConfigPath ?? Application.dataPath),
                "AIchat/custom_prompt.txt");
            if (System.IO.File.Exists(customPromptPath))
            {
                CustomPrompt = System.IO.File.ReadAllText(customPromptPath);
                Plugin.LogInfo("Custom prompt loaded.");
            }
        }

        private void Start()
        {
            Window.Initialize();
            RebuildApiClient();
        }

        /// <summary>
        /// Recreates the API client based on the active provider.
        /// </summary>
        /// <summary>Resets the conversation memory for the current character (used by debug panel).</summary>
        public void ResetMemory()
        {
            Memory = new ConversationMemory();
            if (!string.IsNullOrEmpty(CurrentCharName))
                Memory.Bind(CurrentCharName);
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
            if (_notMove == null)
            {
                _notMove = FindFirstObjectByType<NotMove>();
                if (_notMove != null && _chatMenuDummy != null)
                {
                    _notMove.AddMenu(_chatMenuDummy);
                    Plugin.LogInfo("Registered AIChat dummy menu with NotMove");
                }
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

                if (!otherMenuActive)
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

        private void Update()
        {
            RefreshNotMove();
            Window.HandleUpdate();

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
        /// Finds the NPC the player is looking at via SpeachListener and caches references.
        /// </summary>
        private void RefreshCurrentNpc()
        {
            try
            {
                var listener = FindFirstObjectByType<SpeachListener>();
                if (listener == null || listener.NpcHit == null)
                {
                    // Start grace period before clearing the active NPC
                    _noNpcTimer += Time.deltaTime;
                    if (_noNpcTimer >= NO_NPC_GRACE_SECONDS && !string.IsNullOrEmpty(CurrentCharName))
                    {
                        Plugin.LogDebug($"No NPC in range for {NO_NPC_GRACE_SECONDS}s — clearing active NPC: {CurrentCharName}");
                        Memory.Save();
                        CurrentCharName = "";
                        CurrentSpeach = null;
                        CurrentEmotions = null;
                        CurrentTalkInteractions = null;
                        CurrentNavigator = null;
                        CurrentInteractionManager = null;
                        _lastCharName = "";
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
                }

                CurrentCharName = npcName;
                CurrentSpeach = speach;
                CurrentEmotions = speach.GetComponent<Emotions>();
                CurrentTalkInteractions = speach.GetComponent<TalkInteractions>();
                CurrentNavigator = speach.GetComponent<NpcNavigatorAndRutines>();
                CurrentInteractionManager = speach.GetComponent<NpcInteractionManager>();
            }
            catch (Exception ex)
            {
                // Silently fail - NPC tracking is best-effort
                Plugin.LogDebug($"RefreshCurrentNpc error: {ex.Message}");
            }
        }

        /// <summary>
        /// Makes the current NPC say the AI's response via the game's native dialogue system.
        /// </summary>
        public void ShowAIResponseInGame(string text)
        {
            if (CurrentSpeach != null)
            {
                CurrentSpeach.Say(text);
            }
            else
            {
                // Fallback: try ShowDialogue directly
                var showDialogue = FindFirstObjectByType<ShowDialogue>();
                if (showDialogue != null)
                {
                    showDialogue.ShowSimpleWithName(text, CurrentCharName);
                }
            }
        }

        /// <summary>
        /// Captures a snapshot of the current character's data for the AI prompt.
        /// </summary>
        public CharacterContextSnapshot CaptureCharacterContext()
        {
            return CharacterContextSnapshot.Capture(CurrentEmotions, CurrentTalkInteractions, CurrentNavigator, CurrentInteractionManager, CurrentCharName);
        }

        private void LateUpdate()
        {
            if (Window != null && Window.IsVisible)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void OnGUI()
        {
            if (Window != null && Window.IsVisible)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            Window.Draw();
        }
    }
}
