using System;
using System.Collections.Generic;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Decides whose voice reads the game's own dialogue lines, and filters out the "lines" that are
    /// really the interface talking to the player.
    ///
    /// The game shows text in two ways, and both are covered by the mod's patches:
    ///   * <c>Speach.Say()</c> - the NPC bubbles, used by Talk About / Compliment / Critic, the jail
    ///     Police lines, gifts, kisses and the random NPC chatter.
    ///   * <c>ShowDialogue.ShowSimpleWithName()</c> and the sex-talk windows - same idea, but the
    ///     speaker is a plain string ("Police", "whispers", a character name).
    ///
    /// The rules that keep the feature pleasant live here: only a speaker that is a character (never
    /// the player or a narrator) gets a voice, only a character's own name is used to look the voice
    /// up, and a line repeated within a moment is not paid for twice (see
    /// <see cref="GameDialogueRepeatGuard"/>). Pure BCL, so all of them can be exercised outside the
    /// game.
    /// </summary>
    public static class GameDialogueSpeaker
    {
        /// <summary>Speakers that are the player, a narrator or a label, never a character.</summary>
        private static readonly string[] NonCharacterSpeakers =
        {
            "you", "me", "player", "myself", "whispers", "whisper", "narrator", "system", "police chief",
        };

        /// <summary>Characters whose names can appear as a speaker, so they keep their own voice.</summary>
        private static readonly string[] KnownCharacters = { "Katherine", "Susan", "Stacy", "Laura", "Police" };

        /// <summary>
        /// Whether a speaker is a character that should be heard. A missing speaker means the bubble
        /// belongs to the NPC the player is talking to, which is a character.
        /// </summary>
        public static bool IsCharacterSpeaker(string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker)) return true;

            string name = speaker.Trim();

            foreach (string nonCharacter in NonCharacterSpeakers)
                if (name.Equals(nonCharacter, StringComparison.OrdinalIgnoreCase))
                    return false;

            // A speaker label is short ("Police", "Katherine"); a sentence in that field is an
            // on-screen message ("I'm a little tired, maybe we could continue chatting tomorrow"),
            // not a name, and must not be voiced in anyone's voice.
            return name.Length <= 32;
        }

        /// <summary>
        /// The character name to look a voice up by. <c>Speach</c> object names can carry a suffix
        /// ("Katherine(Clone)"), and the sex-talk windows pass the full object name ("Katherine 1"),
        /// which the game itself shortens to the first word; this does the same.
        /// </summary>
        public static string NormalizeSpeaker(string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker)) return "";

            string name = speaker.Trim();
            int space = name.IndexOf(' ');
            if (space > 0) name = name.Substring(0, space);

            int parenthesis = name.IndexOf('(');
            if (parenthesis > 0) name = name.Substring(0, parenthesis);

            return name.Trim();
        }

        /// <summary>
        /// The speaker of a native line, when the game handed it to us as a plain string. A known
        /// character keeps her configured voice ("Katherine 1" is still Katherine); the jail Police
        /// is not one of the four main characters, so she is voiced like any random NPC - a voice of
        /// her own, or the shared Default NPC Voice when the pool is off.
        /// </summary>
        public static string VoiceNameFor(string speaker)
        {
            string name = NormalizeSpeaker(speaker);
            if (name.Length == 0) return "";

            foreach (string known in KnownCharacters)
                if (name.IndexOf(known, StringComparison.OrdinalIgnoreCase) >= 0)
                    return known;

            return name;
        }
    }

    /// <summary>
    /// Drops a line that was already voiced a moment ago.
    ///
    /// Several of the game's flows can reach two of the patched entry points with the same sentence
    /// (its own retry loops, and the dialogue box echoing a bubble), and in sex mode the same phrase
    /// fires again on every animation cycle. Each of those would be a second paid request for a line
    /// the player is already hearing, so identical text inside the window is spoken once.
    ///
    /// Only identical text is filtered, and the window is short: a character who says the same thing
    /// again after that really did say it again.
    /// </summary>
    public sealed class GameDialogueRepeatGuard
    {
        private readonly float _windowSeconds;
        private string _lastLine = "";
        private float _lastTime;

        public GameDialogueRepeatGuard(float windowSeconds = 1.5f)
        {
            _windowSeconds = windowSeconds;
        }

        /// <summary>
        /// True when <paramref name="line"/> is a repeat of the previous one. Otherwise the line
        /// becomes the new reference and is meant to be voiced.
        /// </summary>
        public bool ShouldSkip(string line, float now)
        {
            if (string.IsNullOrEmpty(line)) return false;

            if (string.Equals(line, _lastLine, StringComparison.Ordinal))
            {
                // A clock that went backwards (level reload) counts as "long ago", not as a repeat.
                float elapsed = now - _lastTime;
                if (elapsed >= 0f && elapsed < _windowSeconds) return true;
            }

            _lastLine = line;
            _lastTime = now;
            return false;
        }

        /// <summary>Forgets the previous line (used when the feature is toggled back on).</summary>
        public void Reset()
        {
            _lastLine = "";
            _lastTime = 0f;
        }
    }
}
