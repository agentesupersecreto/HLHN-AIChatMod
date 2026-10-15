using System;
using System.Collections.Generic;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Gives every random NPC her own voice.
    ///
    /// A random NPC is any character that is not one of the four main ones (Katherine, Susan, Stacy,
    /// Laura). Instead of sharing a single voice, each of them is handed one from
    /// <see cref="VoicePool"/> the first time she speaks, and keeps it for the rest of the session -
    /// so a scene of up to six NPCs sounds like six people and meeting the same girl again sounds
    /// like her.
    ///
    /// Voices are handed out least-used-first (oldest assignment wins a tie), so the first NPCs met
    /// always get different voices while the pool lasts, and only after all eight are in use does the
    /// pool start over.
    ///
    /// Pure BCL: no Unity types, so the assignment rule can be exercised outside the game.
    /// </summary>
    public sealed class NpcVoiceAssigner
    {
        /// <summary>
        /// Voices shipped for the random NPCs. Eight is comfortably more than the six NPCs a scene can
        /// hold, so the NPCs you actually meet side by side do not share a voice.
        /// </summary>
        public static readonly string[] VoicePool =
        {
            "f80f728df4c243bca79cd8dd0b57cac4",
            "83c19893c4974594839bd2d101b1fd66",
            "ee2fa25a56364be9a80d0028eecc6ea4",
            "f7f74a4bc4324c25b96b9b6741a72ab3",
            "935a4dbcc15541f480e33a41107ca498",
            "512931bc4ae747edb22787ec9d273879",
            "15f55a723f094e188bd075eb0e9eff85",
            "bf189034a74f4ce6a4c7bd5f28398c52",
        };

        /// <summary>NPC name -> pool voice, in the order the NPCs were first heard.</summary>
        private readonly Dictionary<string, string> _assigned =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How many NPCs currently speak with each pool voice.</summary>
        private readonly int[] _useCount = new int[VoicePool.Length];

        /// <summary>Sequence number of the last NPC assigned to each pool voice (0 = never used).</summary>
        private readonly long[] _lastUsed = new long[VoicePool.Length];

        private long _sequence;

        /// <summary>Voices handed out so far, for the CONFIG list. Empty when nobody has spoken yet.</summary>
        public IReadOnlyDictionary<string, string> Assignments => _assigned;

        /// <summary>
        /// Voice for a random NPC: the one she already has, or the least-used pool voice.
        /// An empty name has no voice of its own and yields an empty string (the caller then uses the
        /// Default NPC Voice).
        /// </summary>
        public string Select(string npcName)
        {
            string name = (npcName ?? "").Trim();
            if (name.Length == 0) return "";

            if (_assigned.TryGetValue(name, out string existing))
                return existing;

            int best = 0;
            for (int i = 1; i < VoicePool.Length; i++)
                if (IsBetterChoice(i, best)) best = i;

            string voice = VoicePool[best];
            _assigned[name] = voice;
            _useCount[best]++;
            _lastUsed[best] = ++_sequence;
            return voice;
        }

        /// <summary>Voice already given to a NPC, without assigning a new one.</summary>
        public bool TryGet(string npcName, out string voice)
        {
            // Only written on a hit: Dictionary.TryGetValue would hand back null otherwise.
            voice = "";
            string name = (npcName ?? "").Trim();
            if (name.Length == 0) return false;

            if (_assigned.TryGetValue(name, out string assigned))
            {
                voice = assigned;
                return true;
            }

            return false;
        }

        /// <summary>Forgets every assignment, so the next NPCs start from the top of the pool again.</summary>
        public void Reset()
        {
            _assigned.Clear();
            Array.Clear(_useCount, 0, _useCount.Length);
            Array.Clear(_lastUsed, 0, _lastUsed.Length);
            _sequence = 0;
        }

        /// <summary>Used by the fewest NPCs wins; on a tie the one idle longest wins.</summary>
        private bool IsBetterChoice(int candidate, int current)
        {
            if (_useCount[candidate] != _useCount[current])
                return _useCount[candidate] < _useCount[current];

            return _lastUsed[candidate] < _lastUsed[current];
        }
    }
}
