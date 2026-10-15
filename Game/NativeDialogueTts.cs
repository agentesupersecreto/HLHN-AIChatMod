using System;
using UnityEngine;
using DialogInterceptorMod.API;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Speaks the game's own dialogue lines - the ones that are not written by the AI: Talk About /
    /// Compliment / Critic replies, the jail Police, gifts, kisses, and the "Sex-Talk" lines shown
    /// while a character is in sex mode.
    ///
    /// The game has no single place where these lines are spoken, so every entry point is patched and
    /// funnels here (see <c>Patching/HarmonyPatches.cs</c>):
    ///   * <c>Speach.Say()</c> - the bubbles, where the speaker is the component's own NPC.
    ///   * <c>ShowDialogue.ShowSimpleWithName()</c> - the box, where the speaker arrives as a string
    ///     ("Police", "Katherine", "whispers").
    ///   * <c>SexDialogue</c> / <c>CumDialogue</c> / <c>ThreesomeSexDialogue.ShowText()</c> - the
    ///     Sex-Talk windows, whose speaker is also a string.
    ///
    /// Each of them has a speaker and a line, so the voice comes from the same configuration as the AI
    /// replies: a main character uses her own slot, any other character uses her own random-NPC voice,
    /// and the jail Police - who is not one of the four main characters - is voiced like the random NPC
    /// she is (<see cref="GameDialogueSpeaker.VoiceNameFor"/>).
    ///
    /// Three guards keep the feature from misbehaving:
    ///   * the player, a narrator or an on-screen message is never spoken, and the game's
    ///     speaker-less narration is skipped altogether (see <see cref="GameDialogueSpeaker"/>);
    ///   * nothing is voiced while the mod's own AI reply is being shown, so an AI line can never be
    ///     read twice, in the wrong voice, or echoed back into the chat context;
    ///   * a sentence shown again within a moment is not requested a second time (see
    ///     <see cref="GameDialogueSpeaker.GameDialogueRepeatGuard"/>).
    ///
    /// Everything here is best-effort: a failure is logged at debug level and can never stop the
    /// game's dialogue from being displayed.
    /// </summary>
    public static class NativeDialogueTts
    {
        /// <summary>How many native lines in a row are reported when something looks broken.</summary>
        private const int REPORT_EVERY = 20;

        /// <summary>Ignored while the scene search for the shower is still fresh (see <see cref="SexTalkWillShow"/>).</summary>
        private const float SHOWER_LOOKUP_RETRY_INTERVAL = 1f;

        private static int _spoken;

        /// <summary>The last line voiced, so a sentence the game shows twice is not paid for twice.</summary>
        private static readonly GameDialogueRepeatGuard RepeatGuard = new GameDialogueRepeatGuard();

        private static InteractionsShower _shower;
        private static float _nextShowerSearchTime;

        /// <summary>
        /// Speaks a native line if the feature is on and the line is worth it.
        /// </summary>
        /// <param name="speaker">
        /// Who is talking. Null or empty means "the NPC the game attached the line to" - the component
        /// that called <c>Say()</c> or the <c>Speach</c> the dialogue box is showing.
        /// </param>
        /// <param name="text">The line, exactly as the game displays it.</param>
        /// <param name="source">Tag for the log, e.g. "Speach.Say".</param>
        /// <param name="allowCurrentNpcFallback">
        /// Whether a missing speaker may be read as the NPC the player is talking to. True for
        /// <c>Speach.Say()</c>, where the line is bound to the component that speaks it. False for the
        /// dialogue box and the Sex-Talk windows, which pass the speaker as a string: there an empty
        /// name is the game narrating the player's own situation ("Laura is here", "Your lust has run
        /// out"), and reading that out loud in an NPC's voice would be wrong.
        /// </param>
        /// <param name="sexTalk">
        /// Whether the line comes from one of the Sex-Talk windows. Those lines are always sent with
        /// <see cref="GameDialogueText.SexTalkTag"/> in front, so the voice moans them instead of
        /// reading them flat - every line of those windows, on every character, during sex.
        /// </param>
        public static void Speak(string speaker, string text, string source,
                                 bool allowCurrentNpcFallback = false, bool sexTalk = false)
        {
            try
            {
                DialogBehaviour behaviour = DialogBehaviour.Instance;
                if (behaviour == null) return;

                // Off by default: the game has a lot of lines, and every one of them is a paid request.
                if (!behaviour.TtsGameDialogues) return;

                // The AI reply has its own voice path; never speak it again here.
                if (behaviour.Window != null && behaviour.Window.IsAIResponse) return;

                if (!GameDialogueSpeaker.IsCharacterSpeaker(speaker))
                {
                    Plugin.LogDebug($"[NativeTts] {source}: skipped - '{speaker}' is not a character.");
                    return;
                }

                string voiceName;
                if (string.IsNullOrWhiteSpace(speaker))
                {
                    if (!allowCurrentNpcFallback)
                    {
                        Plugin.LogDebug($"[NativeTts] {source}: skipped - the game named no character.");
                        return;
                    }

                    voiceName = behaviour.CurrentCharName;
                }
                else
                {
                    voiceName = GameDialogueSpeaker.VoiceNameFor(speaker);
                }

                if (string.IsNullOrEmpty(voiceName))
                {
                    Plugin.LogDebug($"[NativeTts] {source}: skipped - no character to attribute the line to.");
                    return;
                }

                string line = GameDialogueText.Sanitize(text);
                if (!GameDialogueText.HasSpeakableText(line))
                {
                    Plugin.LogDebug($"[NativeTts] {source}: skipped - nothing speakable in '{text}'.");
                    return;
                }

                // The same sentence can reach two of the patched entry points (and, in sex mode,
                // repeat every cycle): it is voiced once, not once per call.
                if (RepeatGuard.ShouldSkip(line, Time.realtimeSinceStartup))
                {
                    Plugin.LogDebug($"[NativeTts] {source}: skipped - '{line}' was just said.");
                    return;
                }

                // The tag goes on after the sanitizer, which would otherwise remove it along with the
                // game's on-screen hints. The repeat guard above keeps working on the bare sentence,
                // so it is the line that is judged, not the way it is delivered.
                string spoken = sexTalk ? GameDialogueText.WithSexTalkTag(line) : line;

                Plugin.LogDebug($"[NativeTts] {source}: {voiceName} says '{spoken}'.");

                // The native line plays on its own channel: it must not cancel an AI reply that is
                // still being generated, and the AI reply must not be able to cancel it either.
                behaviour.Tts?.SpeakNative(spoken, voiceName);

                _spoken++;
                if (_spoken % REPORT_EVERY == 0)
                    Plugin.LogInfo($"[NativeTts] {_spoken} game dialogue lines voiced this session.");
            }
            catch (Exception ex)
            {
                // A patch must never be able to break the game's own dialogue.
                Plugin.LogDebug($"[NativeTts] {source} error: {ex.Message}");
            }
        }

        /// <summary>
        /// Forgets the session counter and the last line, so the first line after the feature is
        /// switched on is never mistaken for a repeat.
        /// </summary>
        public static void ResetCounter()
        {
            _spoken = 0;
            RepeatGuard.Reset();
        }

        /// <summary>
        /// Whether <c>Speach.Say()</c> accepted the line. The game drops a bubble while the NPC is
        /// still speaking (<c>if (!this.speaking)</c>), and it reports nothing when it does - but
        /// <c>SayRutine</c> assigns <c>Dialogue</c> synchronously, before its first yield, so a line
        /// that was accepted is visible here. Only the bubble the player actually saw is voiced: the
        /// AI reply retries Say() until it is accepted, and those dropped attempts must stay silent.
        /// </summary>
        public static bool LineWasShown(Speach speach, string text)
        {
            if (speach == null || string.IsNullOrEmpty(text)) return false;

            try
            {
                return speach.Dialogue == text;
            }
            catch (Exception)
            {
                // A destroyed component cannot have shown anything.
                return false;
            }
        }

        /// <summary>
        /// The NPC a dialogue box belongs to: the game reuses a single <c>ShowDialogue</c>, so a line
        /// shown through it is attributed to the NPC the player is currently talking to.
        /// </summary>
        public static string SpeakerFrom(Speach speach)
        {
            if (speach != null)
            {
                try
                {
                    string name = speach.transform != null ? speach.transform.name : null;
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
                catch (Exception) { /* destroyed object: fall back to the tracked NPC */ }
            }

            return DialogBehaviour.Instance != null ? DialogBehaviour.Instance.CurrentCharName : "";
        }

        /// <summary>
        /// Whether a sex-talk window would actually show the line. The windows are shared between
        /// characters, and showing a line is exactly what the game does: a line that never appears
        /// must not be voiced either, or the player would hear a comment nobody made.
        ///
        /// Each of the three windows returns early while the player has Sex-Talk switched off in the
        /// game's own UI, which is what the shower is read for. The lookup is a scene-wide search and
        /// these lines fire every few seconds, so the reference is cached and only refreshed once a
        /// scene change has invalidated it.
        /// </summary>
        public static bool SexTalkWillShow(string npcName, string phrase, string source)
        {
            if (string.IsNullOrEmpty(npcName) || string.IsNullOrEmpty(phrase)) return false;

            try
            {
                if (_shower == null && Time.realtimeSinceStartup >= _nextShowerSearchTime)
                {
                    _nextShowerSearchTime = Time.realtimeSinceStartup + SHOWER_LOOKUP_RETRY_INTERVAL;
                    _shower = UnityEngine.Object.FindAnyObjectByType<InteractionsShower>();
                }

                if (_shower != null && !_shower.sexTalk)
                {
                    Plugin.LogDebug($"[NativeTts] {source}: skipped - the player turned sex talk off.");
                    return false;
                }
            }
            catch (Exception) { /* no shower in the scene: the game's own ShowText would not run either */ }

            return true;
        }
    }
}
