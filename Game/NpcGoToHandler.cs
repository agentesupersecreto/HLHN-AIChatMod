using System;
using System.Collections;
using UnityEngine;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// The "go to a place" order: sends the current NPC to a spot of the house (kitchen, sofa, bed,
    /// bathroom, outside...) and lets her do there whatever the game does at that spot.
    ///
    /// How it works — instead of driving the NavMeshAgent by hand, the mod hands the game a temporary
    /// chore and lets the routine system do the walking:
    ///   1. stop following the player (same toggle the game uses),
    ///   2. end the chore she is in the middle of (NpcNavigatorAndRutines.EndChore),
    ///   3. insert a temporary entry at the head of <c>CurrentDayChores</c> pointing at the target spot,
    ///   4. <c>CheckForChores()</c> picks it up and <c>StartWorking()</c> walks her there, plays the
    ///      spot's animation, uses its objects and cleans everything up when the window ends.
    ///
    /// The order is gated by the same trust rule as following
    /// (<see cref="CommandExecutor.CheckFollowThreshold"/>): ValueToFollow &lt;= Trust.
    /// </summary>
    public static class NpcGoToHandler
    {
        /// <summary>How long she stays at the spot, in in-game hours, when the command has no duration.</summary>
        public const float DEFAULT_STAY_HOURS = 1f;

        private const float MIN_STAY_HOURS = 0.03f;
        private const float MAX_STAY_HOURS = 8f;

        /// <summary>Distance at which she counts as arrived (the game teleports her to the exact pose).</summary>
        private const float ARRIVE_DISTANCE = 1.6f;

        private const float START_TIMEOUT_SECONDS = 6f;
        private const float ARRIVE_TIMEOUT_SECONDS = 180f;
        private const float RESUME_TIMEOUT_SECONDS = 8f;

        /// <summary>Hard cap on how long the coroutine watches a visit before giving up on it.</summary>
        private const float MAX_VISIT_REAL_SECONDS = 300f;

        private static NpcNavigatorAndRutines _activeNav;
        private static NpcNavigatorAndRutines.Chores _activeEntry;
        private static Coroutine _activeRoutine;

        /// <summary>True while an order is being carried out (used by the debug panel).</summary>
        public static bool HasActiveOrder => _activeNav != null;

        /// <summary>Name of the place of the order in progress, or "".</summary>
        public static string ActivePlaceName { get; private set; } = "";

        /// <summary>
        /// Sends the current NPC to the place named by the AI (or the player). Returns the immediate
        /// feedback line; the actual walking happens in a coroutine on the mod's GameObject.
        /// </summary>
        public static string GoTo(string placeArg, float? stayHours)
        {
            var db = DialogBehaviour.Instance;
            if (db == null) return "The AI chat mod is not ready yet.";

            var nav = db.CurrentNavigator;
            string name = string.IsNullOrEmpty(db.CurrentCharName) ? "She" : db.CurrentCharName;
            if (nav == null) return "There is no NPC nearby to send anywhere.";

            // Same gate as following: the game itself refuses to take her anywhere below ValueToFollow.
            string refusal = CommandExecutor.CheckFollowThreshold("go there");
            if (refusal != null) return refusal;

            if (nav.IsInSexPose) return $"{name} is busy right now.";
            if (nav.EndHerDay) return $"{name} is about to leave for the day.";
            if (nav.PauseWork) return $"{name} is busy with something else right now.";

            var spots = LocationCatalog.Collect(nav);
            if (spots.Count == 0)
                return "I don't know any place to go around here (no routine spots found in this scene).";

            var spot = LocationCatalog.Resolve(placeArg, spots, nav);
            if (spot == null)
                return $"There is no '{placeArg}' here. Places I know: {LocationCatalog.Describe(spots)}";

            if (nav.timeManager == null) return $"{name} can't go anywhere right now.";

            float hours = Mathf.Clamp(stayHours ?? DEFAULT_STAY_HOURS, MIN_STAY_HOURS, MAX_STAY_HOURS);

            // Dropping the previous order first keeps the routine list consistent.
            CancelActive();

            _activeNav = nav;
            _activeRoutine = db.StartCoroutine(Run(nav, spot, hours));
            return $"{name} is on her way to the {spot.Name}.";
        }

        /// <summary>Comma-separated list of places the current NPC can be sent to.</summary>
        public static string ListPlaces()
        {
            var db = DialogBehaviour.Instance;
            var nav = db?.CurrentNavigator;
            if (nav == null) return "There is no NPC nearby.";

            var spots = LocationCatalog.Collect(nav);
            if (spots.Count == 0) return $"{nav.transform.name} has nowhere to go in this scene.";

            return $"{nav.transform.name} can go to: {LocationCatalog.Describe(spots)}";
        }

        /// <summary>
        /// Aborts the order in progress: ends a running visit and drops a pending one, leaving the
        /// NPC's routine as the game would have left it.
        /// </summary>
        public static void CancelActive()
        {
            var db = DialogBehaviour.Instance;
            if (_activeRoutine != null && db != null) db.StopCoroutine(_activeRoutine);
            _activeRoutine = null;

            if (_activeNav != null && _activeEntry != null)
            {
                if (_activeNav.timeManager != null)
                    _activeEntry.EndTime = _activeNav.timeManager.ActualTime; // ends a visit already running
                if (!_activeNav.Working)
                    RemoveEntry(_activeNav, _activeEntry);
            }

            _activeNav = null;
            _activeEntry = null;
            ActivePlaceName = "";
        }

        // ─────────────────────────────────────────────────────────────
        //  Coroutine

        private static IEnumerator Run(NpcNavigatorAndRutines nav, LocationSpot spot, float hours)
        {
            var db = DialogBehaviour.Instance;
            string name = nav.transform.name;
            ActivePlaceName = spot.Name;
            Plugin.LogInfo($"GoTo: sending {name} to '{spot.Name}' ({spot.Source}) at {LocationCatalog.FormatVector(spot.Position)} for {hours:F2} in-game hours.");

            // 1. She must not be following the player: follow mode drives the agent every frame.
            if (nav.Follow)
            {
                nav.MakeNpcFollowMe();
                float followDeadline = Time.time + RESUME_TIMEOUT_SECONDS;
                while (nav != null && nav.Follow && Time.time < followDeadline) yield return null;
            }

            // 2. Interrupt the chore in progress. EndChore(true) is the game's own "stop now":
            //    it unpauses her and marks the chore finished, and StartWorking() cleans up.
            if (nav != null && nav.Working)
            {
                nav.EndChore(true);
                float workDeadline = Time.time + RESUME_TIMEOUT_SECONDS;
                while (nav != null && nav.Working && Time.time < workDeadline) yield return null;
            }

            if (nav == null) { Finish(); yield break; }

            // 3. CheckForChores() only starts a chore while she is idle and not paused.
            float startDeadline = Time.time + START_TIMEOUT_SECONDS;
            while (nav.PauseWork || nav.PlayingAnimation)
            {
                if (Time.time > startDeadline)
                {
                    Report(db, $"{name} couldn't go to the {spot.Name}: she is busy right now.", true);
                    Finish();
                    yield break;
                }
                yield return null;
            }

            // 4. The visit has to fit before her end-of-day chore, otherwise the game sends her home
            //    instead of running it.
            var time = nav.timeManager;
            if (time == null)
            {
                Report(db, $"{name} can't go anywhere right now.", true);
                Finish();
                yield break;
            }

            float now = time.ActualTime;
            float end = now + hours;
            if (nav.CurrentEndChore != null)
                end = Mathf.Min(end, nav.CurrentEndChore.StartTime - 0.05f);

            if (end <= now + MIN_STAY_HOURS)
            {
                Report(db, $"{name} can't go to the {spot.Name} now: it's too late in her day.", true);
                Finish();
                yield break;
            }

            // 5. Hand the game a temporary chore and let CheckForChores() pick it up first.
            var entry = BuildEntry(spot, now, end);
            nav.CurrentDayChores.Insert(0, entry);
            _activeEntry = entry;

            float pickupDeadline = Time.time + START_TIMEOUT_SECONDS;
            while (nav != null && nav.CurrentTask != entry && Time.time < pickupDeadline) yield return null;

            if (nav == null || nav.CurrentTask != entry)
            {
                RemoveEntry(nav, entry);
                _activeEntry = null;
                Report(db, $"{name} couldn't leave right now.", true);
                Finish();
                yield break;
            }

            // 6. Watch the visit: announce the arrival, then wait for the game to finish it.
            float visitDeadline = Time.time + MAX_VISIT_REAL_SECONDS;
            bool announced = false;
            bool timedOut = false;

            while (nav != null && nav.CurrentTask == entry)
            {
                if (!announced && Vector3.Distance(nav.transform.position, spot.Position) <= ARRIVE_DISTANCE)
                {
                    announced = true;
                    Report(db, $"{name} arrived at the {spot.Name}.", false);
                }

                if (Time.time > visitDeadline) { timedOut = true; break; }
                yield return null;
            }

            // 7. StartWorking() removes the entry itself; only a timeout needs an explicit cleanup.
            if (timedOut && nav != null)
            {
                entry.EndTime = nav.timeManager != null ? nav.timeManager.ActualTime : entry.EndTime;
                Plugin.LogWarning($"GoTo: visit to '{spot.Name}' exceeded {MAX_VISIT_REAL_SECONDS:F0}s — ended from the mod.");
            }

            Finish();
        }

        /// <summary>Clears the order state once the coroutine is done (the game owns the chore entry).</summary>
        private static void Finish()
        {
            _activeNav = null;
            _activeEntry = null;
            _activeRoutine = null;
            ActivePlaceName = "";
        }

        /// <summary>
        /// Builds the temporary routine entry for a spot. Routine spots keep their own animation,
        /// objects and outfit; user-defined (or animation-less) spots get a synthetic chore.
        /// </summary>
        private static NpcNavigatorAndRutines.Chores BuildEntry(LocationSpot spot, float start, float end)
        {
            Chore chore = spot.Entry != null && spot.Entry.chore != null ? spot.Entry.chore : CreateChore(spot);

            return new NpcNavigatorAndRutines.Chores
            {
                chore = chore,
                StartTime = Mathf.Max(0f, start - 0.01f),
                EndTime = end,
                UseObject = spot.UseObject,
                clothesToUse = spot.ClothesToUse ?? "",
                CanBeInterrupted = true,
                CanBeTalkedWhileBusy = true,
                hasRootMotion = spot.HasRootMotion,
                Desc = spot.Name,
                Hint = "" // the routine's hint belongs to the routine, not to a player-ordered visit
            };
        }

        private static Chore CreateChore(LocationSpot spot)
        {
            var chore = ScriptableObject.CreateInstance<Chore>();
            chore.name = "AI_" + spot.Name;
            chore.AnimationName = spot.Animation ?? "";
            chore.Pos = spot.Position;
            chore.Rot = spot.Rotation;
            return chore;
        }

        private static void RemoveEntry(NpcNavigatorAndRutines nav, NpcNavigatorAndRutines.Chores entry)
        {
            if (nav == null || entry == null || nav.CurrentDayChores == null) return;
            if (!nav.CurrentDayChores.Remove(entry))
                RemoveEntryByReference(nav, entry);
        }

        /// <summary>
        /// Fallback removal for a copy of the entry (List.Remove uses Equals, which Chores does not
        /// override, so identity removal is what actually matters here).
        /// </summary>
        private static void RemoveEntryByReference(NpcNavigatorAndRutines nav, NpcNavigatorAndRutines.Chores entry)
        {
            for (int i = 0; i < nav.CurrentDayChores.Count; i++)
            {
                if (ReferenceEquals(nav.CurrentDayChores[i], entry))
                {
                    nav.CurrentDayChores.RemoveAt(i);
                    return;
                }
            }
        }

        private static void Report(DialogBehaviour db, string message, bool error)
        {
            Plugin.LogInfo($"GoTo: {message}");
            if (db == null) return;
            db.AddCapturedEvent(message);
            if (db.Window != null) db.Window.SetStatus(message, error);
        }
    }
}
