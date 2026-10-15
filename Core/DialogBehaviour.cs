using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        public TtsClient Tts { get; private set; }

        public List<ChatMessage> ChatHistory { get; private set; } = new List<ChatMessage>();
        public string ChatSummary = "";
        public string CustomPrompt = "";
        private DateTime _customPromptWriteTime = DateTime.MinValue;

        // --- Config fields ---
        public string ApiKey = "";
        public ProviderType Provider = ProviderType.Ollama;
        public string OllamaUrl = "http://localhost:11434/api/chat";
        public string OllamaModel = "defyma85/gemma-4-E4B-it-ultra-uncensored-heretic-Q4_K_M_gguf";
        public string GemmaUrl = "http://localhost:11434/api/generate";
        public string GemmaModel = "defyma85/gemma-4-E4B-it-ultra-uncensored-heretic-Q4_K_M_gguf";
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

        // --- Text-to-Speech (fish.audio) ---
        // The NPC reply is sent to fish.audio and the returned MP3 is played back in-game.
        // Each character can use her own voice (fish.audio reference_id).
        public bool TtsEnabled = false;
        public string TtsApiUrl = "https://api.fish.audio/v1/tts";
        public string TtsApiKey = "";
        public string TtsModel = "s2.1-pro-free";
        /// <summary>
        /// Voice shipped for the random/secondary NPCs: every character that is NOT one of the
        /// <see cref="MainCharacters"/> below. It is what the "Default NPC Voice" field holds out of
        /// the box, what an empty field falls back to (see <see cref="TtsVoiceDefault"/>), and what
        /// those NPCs use while <see cref="TtsDistinctNpcVoices"/> is off.
        /// </summary>
        public const string DefaultNpcVoice = "22bff164a7104976ba3ade3b7ec1877c";

        /// <summary>
        /// Shared voice for the random/secondary NPCs, used only while
        /// <see cref="TtsDistinctNpcVoices"/> is off. Changes made in the TTS tab are saved in the ini;
        /// a value found there overrides <see cref="DefaultNpcVoice"/>.
        /// </summary>
        public string TtsVoiceDefault = DefaultNpcVoice;

        /// <summary>
        /// When on (the default), every random NPC gets her own voice out of
        /// <see cref="API.NpcVoiceAssigner.VoicePool"/> instead of sharing <see cref="TtsVoiceDefault"/>:
        /// a scene holds at most six NPCs, and the pool has eight voices, so the ones met side by side
        /// do not sound alike. The voice sticks to the NPC's name for the session.
        /// </summary>
        public bool TtsDistinctNpcVoices = true;

        /// <summary>
        /// When on, the mod also reads out loud the dialogue the game itself writes: the Talk About /
        /// Compliment / Critic replies, the interactions and events (gifts, kisses, the jail Police),
        /// and the "Sex-Talk" lines shown while a character is in sex mode. They use the same voices as
        /// the AI replies (a main character her own slot, any other character her random voice).
        ///
        /// Off by default: every one of those lines is a separate paid request, and the game speaks
        /// them constantly - a party, a scene with several NPCs, or sex mode alone would spend far more
        /// than the AI conversation does. See <see cref="Game.NativeDialogueTts"/>.
        /// </summary>
        public bool TtsGameDialogues = false;

        /// <summary>
        /// When on (the default), the audio of the game's own dialogue lines is kept on disk in
        /// <c>BepInEx/config/AIChat/tts_cache</c>: the game repeats those lines constantly (Talk About
        /// comments, greetings, kiss replies, Sex-Talk), and a line that has already been paid for is
        /// replayed from the file - instantly, and without a second request.
        ///
        /// Only the game's lines are stored. AI replies are new sentences nearly every time, so they
        /// would fill the folder with one-use files and evict the lines worth keeping. The cache keeps
        /// itself under <see cref="API.TtsAudioCache.DefaultMaxBytes"/> (200 MB) by deleting the least
        /// recently used entries, and the TTS tab can clear it. See <see cref="API.TtsAudioCache"/>.
        /// </summary>
        public bool TtsNativeCache = true;

        /// <summary>
        /// Picks (and remembers) the voice of each random NPC; see <see cref="API.NpcVoiceAssigner"/>.
        /// </summary>
        public API.NpcVoiceAssigner NpcVoices { get; } = new API.NpcVoiceAssigner();

        public float TtsVolume = 1f;

        /// <summary>
        /// Latency/quality trade-off sent to fish.audio: low, balanced or normal (see
        /// <see cref="API.TtsRequestBody"/>). Anything lower answers sooner - and makes the on-screen
        /// reply, which waits for the voice, show up sooner too.
        /// </summary>
        public string TtsLatency = API.TtsRequestBody.DefaultLatency;

        /// <summary>Speaking speed of the voice (fish.audio prosody.speed).</summary>
        public float TtsSpeed = API.TtsRequestBody.DefaultSpeed;

        /// <summary>Evens out the loudness between lines (fish.audio prosody.normalize_loudness).</summary>
        public bool TtsNormalizeLoudness = true;

        /// <summary>Server-side text normalization, which makes fish.audio read numbers properly.</summary>
        public bool TtsNormalize = true;

        /// <summary>
        /// Main characters of the house, offered as editable voice slots in the TTS tab.
        /// Every other NPC (the random/secondary ones) is a random NPC: she gets a voice of her own
        /// out of <see cref="API.NpcVoiceAssigner.VoicePool"/> while <see cref="TtsDistinctNpcVoices"/>
        /// is on, and <see cref="TtsVoiceDefault"/> otherwise. Only the shorter, non-committal form of
        /// the name is matched here - the check itself lives in
        /// <see cref="CharacterContextSnapshot.IsMainCharacterName"/>, so voices and the rest of the mod
        /// agree on who is a main character.
        /// </summary>
        public static readonly string[] MainCharacters = { "Katherine", "Susan", "Stacy", "Laura" };

        /// <summary>
        /// Character name -> fish.audio voice reference_id. Shipped with a default voice for each of
        /// the four main characters; a value in the ini overrides the default, and clears it back to
        /// the default when the field is left empty.
        /// </summary>
        public Dictionary<string, string> TtsVoices { get; private set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Katherine", "24c9366ab9704b8bbde8e53c4826610c" },
                { "Susan",     "2a9605eeafe84974b5b20628d42c0060" },
                { "Stacy",     "ca3007f96ae7499ab87d27ea3599956a" },
                { "Laura",     "ffaf1ae1d3a1404e836ae205d42c94e9" },
            };

        /// <summary>
        /// Voice reference used for a character:
        /// a dedicated voice from the ini if there is one, otherwise one of the pool voices for a
        /// random NPC, and finally <see cref="TtsVoiceDefault"/> (the shared random-NPC voice, and the
        /// fallback for an unnamed speaker).
        /// </summary>
        public string GetTtsVoice(string characterName)
        {
            if (!string.IsNullOrWhiteSpace(characterName))
            {
                // Exact name first: the main characters and any NPC given a voice in the ini.
                if (TtsVoices.TryGetValue(characterName, out string voice) && !string.IsNullOrWhiteSpace(voice))
                    return voice.Trim();

                // A NPC's object name can carry a suffix ("Katherine(Clone)"), and she is still
                // Katherine: match her entry before she can be mistaken for a random NPC.
                foreach (string mainCharacter in MainCharacters)
                {
                    if (characterName.IndexOf(mainCharacter, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        TtsVoices.TryGetValue(mainCharacter, out string mainVoice) &&
                        !string.IsNullOrWhiteSpace(mainVoice))
                        return mainVoice.Trim();
                }

                // One voice per random NPC, so the up-to-six NPCs of a scene do not all sound alike.
                if (TtsDistinctNpcVoices &&
                    !CharacterContextSnapshot.IsMainCharacterName(characterName))
                    return NpcVoices.Select(characterName);
            }

            return TtsVoiceDefault;
        }

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

            // Creates BepInEx/config/AIChat/{system_prompt,custom_prompt}.txt on first run
            PromptBuilder.EnsurePromptFiles();

            // Text-to-speech host, lives on this persistent GameObject.
            Tts = new TtsClient(this);
        }

        private void Start()
        {
            Window.Initialize();
            RebuildApiClient();
        }

        private void OnEnable()
        {
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDestroy()
        {
            Tts?.Dispose();
        }

        private void OnDisable()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            // Save memory when the mod behaviour is disabled (game exit, etc.)
            if (!string.IsNullOrEmpty(CurrentCharName) && Memory != null)
            {
                Memory.Summary = ChatSummary;
                Memory.Save();
                Plugin.LogInfo($"Memory saved on disable for {CurrentCharName}.");
            }
        }

        /// <summary>
        /// Called by Unity when any scene is unloaded (House → Beach, etc.).
        /// Saves conversation memory before NPCs are destroyed so nothing is lost.
        /// </summary>
        private void OnSceneUnloaded(Scene scene)
        {
            if (!string.IsNullOrEmpty(CurrentCharName) && Memory != null)
            {
                Memory.Summary = ChatSummary;
                Memory.Save();
                Plugin.LogInfo($"Scene '{scene.name}' unloading — memory saved for {CurrentCharName}.");
            }

            // Invalidate cached scene-bound objects so they're re-found in the new scene
            _speachListener = null;
            _notMove = null;
            CurrentSpeach = null;
            CurrentEmotions = null;
            CurrentTalkInteractions = null;
            CurrentNavigator = null;
            CurrentInteractionManager = null;
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

            // Fallback: no NPC bound — try ShowDialogue directly.
            // The flag tells the native-dialogue patch that this box is the AI reply, not one of the
            // game's own lines: without it the reply would be sent to the endpoint a second time and
            // read in the wrong voice.
            var showDialogue = FindFirstObjectByType<ShowDialogue>();
            if (showDialogue != null)
            {
                if (Window != null) Window.IsAIResponse = true;
                try
                {
                    showDialogue.ShowSimpleWithName(text, CurrentCharName);
                }
                finally
                {
                    if (Window != null) Window.IsAIResponse = false;
                }
            }
        }

        /// <summary>
        /// Speaks the NPC reply out loud through the configured TTS endpoint.
        /// <paramref name="onAudioReady"/> is invoked as soon as the audio is ready (so the caller can
        /// show the reply at the same moment as the voice), or immediately when TTS is unavailable.
        /// </summary>
        public void SpeakWithTts(string text, Action onAudioReady)
        {
            if (Tts != null)
                Tts.Speak(text, CurrentCharName, onAudioReady);
            else
                onAudioReady?.Invoke();
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
