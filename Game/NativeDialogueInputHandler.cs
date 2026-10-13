using System;
using TMPro;
using UnityEngine;
using HarmonyLib;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Integrates player text input and conversation turns directly into the game's native ShowDialogue box.
    /// Handles the turn-based conversational loop:
    /// 1. Player types in native dialogue box (speaker "You") with blinking cursor.
    /// 2. Pressing Enter submits the query to the AI and displays a waiting status.
    /// 3. When the AI responds, the NPC speaks in the native dialogue box.
    /// 4. Pressing SPACE or ENTER while NPC is speaking advances past NPC dialogue and reopens the player input box.
    /// 5. Pressing SPACE while the player's input box is empty closes the dialogue box and restores free movement.
    /// </summary>
    public class NativeDialogueInputHandler
    {
        public static NativeDialogueInputHandler Instance { get; private set; }

        public bool IsInputActive { get; private set; }
        public bool IsWaitingForAI { get; private set; }
        public bool IsNpcSpeaking { get; private set; }
        public string CurrentInput { get; set; } = "";
        public string SpeakerName { get; set; } = "You";
        public bool Enabled { get; set; } = true;

        private string _currentNpcSpeakerName = "";
        private string _currentNpcSpeechText = "";

        private ShowDialogue _showDialogue;
        private ModularMotion.UIMotion _motion;
        private float _nextSearchTime = 0f;
        private const float SEARCH_INTERVAL = 2f;
        private float _lastBlinkTime = 0f;
        private bool _blinkVisible = true;

        public static void Init()
        {
            if (Instance == null)
            {
                Instance = new NativeDialogueInputHandler();
            }
        }

        public ShowDialogue GetShowDialogue()
        {
            if (_showDialogue != null) return _showDialogue;
            if (Time.time < _nextSearchTime) return null;

            _showDialogue = UnityEngine.Object.FindFirstObjectByType<ShowDialogue>();
            if (_showDialogue != null && _showDialogue.image != null)
            {
                _motion = _showDialogue.image.GetComponent<ModularMotion.UIMotion>();
            }

            _nextSearchTime = _showDialogue != null ? 0f : Time.time + SEARCH_INTERVAL;
            return _showDialogue;
        }

        /// <summary>
        /// Activates the native dialogue box for player input.
        /// </summary>
        public void StartInput()
        {
            if (!Enabled) return;

            var dialogue = GetShowDialogue();
            if (dialogue == null)
            {
                Plugin.LogDebug("NativeDialogueInputHandler: ShowDialogue not found in scene.");
                return;
            }

            IsInputActive = true;
            IsWaitingForAI = false;
            IsNpcSpeaking = false;
            CurrentInput = "";

            dialogue.showing = true; // Prevents ShowDialogue.Update from overriding our text

            if (dialogue.NpcName != null)
                dialogue.NpcName.text = SpeakerName;

            UpdateDialogueText();

            // Open the native box with animation if it was closed
            if (_motion != null)
            {
                if (_motion.Frame == 1)
                {
                    _motion.Play();
                }
            }
            else if (dialogue.image != null && !dialogue.image.activeSelf)
            {
                dialogue.image.SetActive(true);
            }

            // Lock character movement while typing
            LockPlayerMovement(true);
        }

        /// <summary>
        /// Cancels player input, closes the native dialogue box, and unlocks player movement.
        /// </summary>
        public void CancelInput(bool closeBox = true)
        {
            IsInputActive = false;
            IsWaitingForAI = false;
            IsNpcSpeaking = false;
            CurrentInput = "";
            _currentNpcSpeechText = "";
            _currentNpcSpeakerName = "";

            if (_showDialogue != null)
            {
                if (closeBox)
                {
                    if (_motion != null)
                    {
                        if (_motion.Frame == 2)
                        {
                            _motion.PlayPrevious();
                        }
                    }
                    else if (_showDialogue.image != null)
                    {
                        _showDialogue.image.SetActive(false);
                    }

                    if (_showDialogue.NpcName != null) _showDialogue.NpcName.text = "";
                    if (_showDialogue.text != null) _showDialogue.text.text = "";
                    _showDialogue.showing = false;
                }
            }

            // Unlock player movement so they can move freely
            LockPlayerMovement(false);
        }

        /// <summary>
        /// Locks or unlocks player movement and resumes the target NPC when unlocked.
        /// </summary>
        public void LockPlayerMovement(bool locked)
        {
            var db = DialogBehaviour.Instance;
            if (db != null)
            {
                db.SetChatMenuDummyActive(locked);
            }

            var notMove = UnityEngine.Object.FindFirstObjectByType<NotMove>();
            if (notMove != null)
            {
                notMove.notmove = locked;
            }

            var playerCtrl = UnityEngine.Object.FindFirstObjectByType<SimplePlayerController>();
            if (playerCtrl != null)
            {
                playerCtrl.StopMoving = locked;
                playerCtrl.StopRotate = locked;
            }

            if (locked)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Confined;
            }
            else
            {
                // Only hide cursor if IMGUI window is not visible
                if (db?.Window == null || !db.Window.IsVisible)
                {
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Confined;
                }

                // Resume NPC movement
                if (db?.CurrentNavigator != null)
                {
                    db.CurrentNavigator.Pause(false);
                }
            }
        }

        /// <summary>
        /// Submits the current input buffer to the AI and displays waiting feedback.
        /// </summary>
        public void SubmitInput()
        {
            if (!IsInputActive) return;

            string text = CurrentInput.Trim();
            if (string.IsNullOrEmpty(text))
            {
                CancelInput(closeBox: true);
                return;
            }

            var db = DialogBehaviour.Instance;
            if (db == null)
            {
                CancelInput(closeBox: true);
                return;
            }

            if (string.IsNullOrEmpty(db.CurrentCharName))
            {
                db.Window.SetStatus("[WARN] No NPC nearby to speak to.", true);
                return;
            }

            // Keep dialogue box showing what the player said until NPC replies
            IsInputActive = false;
            IsWaitingForAI = true;
            string sentText = text;
            CurrentInput = "";

            if (_showDialogue != null)
            {
                if (_showDialogue.NpcName != null)
                    _showDialogue.NpcName.text = SpeakerName;

                if (_showDialogue.text != null)
                    _showDialogue.text.text = $"{sentText}\n<size=80%><color=#888888>[...] Waiting for response...</color></size>";

                _showDialogue.showing = true;
            }

            // Send to AI
            db.Window.SendUserMessage(sentText);
        }

        /// <summary>
        /// Displays the NPC's AI response in the native dialogue box.
        /// </summary>
        public void ShowNpcSpeech(string npcName, string text)
        {
            IsWaitingForAI = false;
            IsNpcSpeaking = true;
            _currentNpcSpeakerName = npcName;
            _currentNpcSpeechText = text;

            var dialogue = GetShowDialogue();
            if (dialogue != null)
            {
                dialogue.showing = true;
                if (dialogue.NpcName != null) dialogue.NpcName.text = npcName;
                if (dialogue.text != null) dialogue.text.text = text;

                if (_motion != null)
                {
                    if (_motion.Frame == 1) _motion.Play();
                }
                else if (dialogue.image != null && !dialogue.image.activeSelf)
                {
                    dialogue.image.SetActive(true);
                }
            }

            // Keep player locked while reading dialogue
            LockPlayerMovement(true);

            // Also trigger NPC facial expressions / animation via native Speach if available
            var db = DialogBehaviour.Instance;
            if (db?.CurrentSpeach != null)
            {
                try
                {
                    if (db.Window != null) db.Window.IsAIResponse = true;
                    db.CurrentSpeach.Say(text);
                    if (db.Window != null) db.Window.IsAIResponse = false;
                }
                catch { }
            }
        }

        /// <summary>
        /// Immediately dismisses the NPC's speech and clears the dialogue box.
        /// </summary>
        public void DismissNpcSpeech()
        {
            IsNpcSpeaking = false;
            _currentNpcSpeechText = "";
            _currentNpcSpeakerName = "";

            var db = DialogBehaviour.Instance;
            if (db?.CurrentSpeach != null)
            {
                try
                {
                    db.CurrentSpeach.StopAllCoroutines();
                    db.CurrentSpeach.Dialogue = "";
                    Traverse.Create(db.CurrentSpeach).Field("speaking").SetValue(false);
                }
                catch { }
            }

            if (_showDialogue != null)
            {
                _showDialogue.showing = false;
                if (_showDialogue.text != null) _showDialogue.text.text = "";
            }
        }

        /// <summary>
        /// Legacy hook called when AI response arrives.
        /// </summary>
        public void OnAIResponseReady()
        {
            IsWaitingForAI = false;
            if (_showDialogue != null)
            {
                _showDialogue.showing = false;
            }
        }

        /// <summary>
        /// Handles keyboard events, IME/Unicode input, caret blinking, and conversation turn loop.
        /// </summary>
        public void HandleUpdate()
        {
            if (!Enabled) return;

            var dialogue = GetShowDialogue();
            if (dialogue == null) return;

            // ═══════════════════════════════════════════════════════════
            //  STATE 1: NPC is currently speaking AI response
            // ═══════════════════════════════════════════════════════════
            if (IsNpcSpeaking)
            {
                dialogue.showing = true;
                if (dialogue.NpcName != null && !string.IsNullOrEmpty(_currentNpcSpeakerName))
                    dialogue.NpcName.text = _currentNpcSpeakerName;
                if (dialogue.text != null && !string.IsNullOrEmpty(_currentNpcSpeechText))
                    dialogue.text.text = _currentNpcSpeechText;

                // Pressing SPACE or ENTER advances past the NPC dialogue and reopens player input box
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    DismissNpcSpeech();
                    StartInput(); // Reopens dialogue box for player to type
                    return;
                }

                // Escape closes and ends conversation
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    DismissNpcSpeech();
                    CancelInput(closeBox: true);
                    return;
                }

                return;
            }

            // ═══════════════════════════════════════════════════════════
            //  STATE 2: Waiting for AI response
            // ═══════════════════════════════════════════════════════════
            if (IsWaitingForAI)
            {
                dialogue.showing = true;
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelInput(closeBox: true);
                    return;
                }
                return;
            }

            // ═══════════════════════════════════════════════════════════
            //  STATE 3: Player is typing in dialogue box
            // ═══════════════════════════════════════════════════════════
            if (IsInputActive)
            {
                dialogue.showing = true;
                if (dialogue.NpcName != null)
                    dialogue.NpcName.text = SpeakerName;

                // Escape cancels and closes dialogue
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelInput(closeBox: true);
                    return;
                }

                // Pressing SPACE when input is empty closes dialogue box and frees player movement
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    if (string.IsNullOrEmpty(CurrentInput))
                    {
                        CancelInput(closeBox: true);
                        return;
                    }
                    // Non-empty input: SPACE will be added as a character below
                }

                // Enter submits input
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    SubmitInput();
                    return;
                }

                // Handle Backspace
                if (Input.GetKeyDown(KeyCode.Backspace))
                {
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    {
                        CurrentInput = "";
                    }
                    else if (CurrentInput.Length > 0)
                    {
                        CurrentInput = CurrentInput.Substring(0, CurrentInput.Length - 1);
                    }
                    UpdateDialogueText();
                }

                // Handle Paste (Ctrl + V)
                if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
                {
                    string clipboard = GUIUtility.systemCopyBuffer;
                    if (!string.IsNullOrEmpty(clipboard))
                    {
                        clipboard = clipboard.Replace("\r", "").Replace("\n", " ");
                        CurrentInput += clipboard;
                        UpdateDialogueText();
                    }
                }

                // Handle printable characters typed via Input.inputString
                string inputChars = Input.inputString;
                if (!string.IsNullOrEmpty(inputChars))
                {
                    bool changed = false;
                    foreach (char c in inputChars)
                    {
                        if (c != '\b' && c != '\r' && c != '\n' && !char.IsControl(c))
                        {
                            CurrentInput += c;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        UpdateDialogueText();
                    }
                }

                // Periodic cursor blink
                if (Time.unscaledTime - _lastBlinkTime > 0.5f)
                {
                    _lastBlinkTime = Time.unscaledTime;
                    _blinkVisible = !_blinkVisible;
                    UpdateDialogueText();
                }
            }
        }

        public void UpdateDialogueText()
        {
            if (_showDialogue == null || _showDialogue.text == null) return;

            string cursor = _blinkVisible ? "<color=#AAAAAA>_</color>" : " ";
            if (string.IsNullOrEmpty(CurrentInput))
            {
                _showDialogue.text.text = $"<color=#777777>Type your message...{cursor}</color>";
            }
            else
            {
                _showDialogue.text.text = CurrentInput + cursor;
            }

            DialogBehaviour.Instance?.Window?.SetInputText(CurrentInput);
        }
    }
}
