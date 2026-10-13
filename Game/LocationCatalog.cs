using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// One place the current NPC can be sent to.
    ///
    /// The game has no named-room system: NPC movement is driven by "chores" (Chore ScriptableObjects
    /// carrying a position, a rotation and an animation) that the routine system walks to. This class
    /// is the mod's view of one of those spots.
    /// </summary>
    public class LocationSpot
    {
        /// <summary>Human label shown in feedback and offered to the AI.</summary>
        public string Name = "";

        /// <summary>Text used for matching (asset name + description + hint).</summary>
        public string Keywords = "";

        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>Animation the game plays on arrival ("" for plain locations).</summary>
        public string Animation = "";

        /// <summary>"routine" (an NPC chore) or "spot" (a placed ChoreInPlace).</summary>
        public string Source = "";

        /// <summary>Name of the Chore asset behind the spot.</summary>
        public string AssetName = "";

        /// <summary>NPC whose routine contains this chore ("" for spots usable by anyone).</summary>
        public string OwnerName = "";

        /// <summary>Original game entry, copied when the visit is injected into the routine.</summary>
        public NpcNavigatorAndRutines.Chores Entry;

        public GameObject[] UseObject;
        public string ClothesToUse = "";
        public bool HasRootMotion;
        public string Hint = "";

        public override string ToString() => $"{Name} ({Source}, {Position.x:F1},{Position.y:F1},{Position.z:F1})";
    }

    /// <summary>
    /// Builds the list of places the current NPC can be sent to and resolves a player/AI-provided
    /// name against it.
    ///
    /// Sources, in priority order:
    ///   1. The NPC's own routine chores (Monday..Sunday, specials, end-of-day): the spots she
    ///      already knows how to use, with their animation, objects and outfit.
    ///   2. ChoreInPlace components placed in the scene (spots shared by every NPC).
    ///
    /// Every candidate is validated against the NavMesh of the current scene, so chores that belong
    /// to another map (routines are stored per scene) are discarded.
    /// </summary>
    public static class LocationCatalog
    {
        /// <summary>Spots farther than this from the NPC are treated as belonging to another map.</summary>
        private const float MAX_SPOT_DISTANCE = 80f;

        /// <summary>NavMesh search radius used to validate/snap a spot position.</summary>
        private const float NAVMESH_SAMPLE_RADIUS = 3f;

        /// <summary>
        /// Maximum spots described in a prompt/list response. The house exposes about twenty
        /// distinct places (rooms and the chores the routines perform), and the AI can only send an
        /// NPC somewhere it can see in that list, so it is sized to hold all of them.
        /// </summary>
        public const int MAX_LISTED_SPOTS = 24;

        /// <summary>
        /// Collection is not free (a scene-wide FindObjectsByType plus one NavMesh query per spot), and
        /// it is called from OnGUI (debug panel) and from every AI turn, so the result is cached for a
        /// moment. Callers must treat the returned list as read-only.
        /// </summary>
        private const float COLLECT_CACHE_SECONDS = 3f;

        private static List<LocationSpot> _collectCache;
        private static NpcNavigatorAndRutines _collectCacheNav;
        private static string _collectCacheScene = "";
        private static float _collectCacheTime = float.NegativeInfinity;

        // ─────────────────────────────────────────────────────────────
        //  Collection

        /// <summary>
        /// Every place reachable from the current scene for this NPC, best first (routine chores,
        /// placed spots). The result is cached per NPC and scene for a few seconds.
        /// </summary>
        public static List<LocationSpot> Collect(NpcNavigatorAndRutines nav)
        {
            string scene = SceneManager.GetActiveScene().name;
            if (_collectCache != null &&
                ReferenceEquals(_collectCacheNav, nav) &&
                _collectCacheScene == scene &&
                Time.unscaledTime - _collectCacheTime < COLLECT_CACHE_SECONDS)
            {
                return _collectCache;
            }

            var spots = new List<LocationSpot>();

            if (nav != null)
                AddRoutineChores(spots, nav);

            AddPlacedSpots(spots, nav);

            // Deterministic order: routine chores first, then placed spots, then by name
            spots.Sort((a, b) =>
            {
                int byPriority = Priority(a).CompareTo(Priority(b));
                if (byPriority != 0) return byPriority;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            _collectCache = spots;
            _collectCacheNav = nav;
            _collectCacheScene = scene;
            _collectCacheTime = Time.unscaledTime;
            return spots;
        }

        private static int Priority(LocationSpot spot)
        {
            // Lower is better: routine (0) < placed spot (1).
            return spot.Source == "routine" ? 0 : 1;
        }

        /// <summary>Adds a candidate if it is unique, on the NavMesh and close enough to be usable.</summary>
        private static void Add(List<LocationSpot> spots, LocationSpot spot, NpcNavigatorAndRutines nav)
        {
            if (spot == null || string.IsNullOrEmpty(spot.Name)) return;
            if (spot.Position == Vector3.zero) return;

            // Reject spots that are not on this scene's NavMesh: routines are stored per scene, so a
            // position that does not belong to the current NavMesh is a chore of another map.
            if (!NavMesh.SamplePosition(spot.Position, out NavMeshHit hit, NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas))
            {
                Plugin.LogDebug($"LocationCatalog: skipping '{spot.Name}' — not on the NavMesh of '{SceneManager.GetActiveScene().name}'.");
                return;
            }
            if (spot.Entry == null) spot.Position = hit.position;

            if (nav != null && Vector3.Distance(nav.transform.position, spot.Position) > MAX_SPOT_DISTANCE)
            {
                Plugin.LogDebug($"LocationCatalog: skipping '{spot.Name}' — farther than {MAX_SPOT_DISTANCE:F0}m.");
                return;
            }

            string key = PlaceKey(spot);
            foreach (var existing in spots)
            {
                if (PlaceKey(existing) == key) return;
            }

            spots.Add(spot);
        }

        /// <summary>
        /// Identity of a place: its Chore asset plus the rounded position.
        /// </summary>
        private static string PlaceKey(LocationSpot spot)
        {
            string id = string.IsNullOrEmpty(spot.AssetName) ? spot.Name : spot.AssetName;
            return PlaceMatcher.Normalize(id) + "|" +
                   $"{Mathf.Round(spot.Position.x * 2f) / 2f:F1},{Mathf.Round(spot.Position.y * 2f) / 2f:F1},{Mathf.Round(spot.Position.z * 2f) / 2f:F1}";
        }

        /// <summary>
        /// Chores of this NPC's routine: the spots she already performs during the week, which means
        /// the position, animation, objects and outfit are all valid for her.
        /// </summary>
        private static void AddRoutineChores(List<LocationSpot> spots, NpcNavigatorAndRutines nav)
        {
            string owner = nav.transform.name;

            var lists = new List<NpcNavigatorAndRutines.Chores[]>
            {
                nav.AllChoresRecopilated,
                nav.Monday, nav.Tuesday, nav.Wednesday, nav.Thursday,
                nav.Friday, nav.Saturday, nav.Sunday,
                nav.SpecialChores
            };

            foreach (var list in lists)
            {
                if (list == null) continue;
                foreach (var entry in list) AddChoreEntry(spots, nav, entry, "routine", owner);
            }

            AddChoreEntry(spots, nav, nav.NormalEndDay, "routine", owner);

            // Today's list and the current/late-night chore are not always part of the week arrays.
            if (nav.CurrentDayChores != null)
            {
                foreach (var entry in nav.CurrentDayChores) AddChoreEntry(spots, nav, entry, "routine", owner);
            }
            AddChoreEntry(spots, nav, nav.CurrentTask, "routine", owner);
            AddChoreEntry(spots, nav, nav.lateNightChore, "routine", owner);
        }

        private static void AddChoreEntry(List<LocationSpot> spots, NpcNavigatorAndRutines nav,
                                          NpcNavigatorAndRutines.Chores entry, string source, string owner)
        {
            if (entry == null || entry.chore == null) return;

            var spot = new LocationSpot
            {
                Name = PlaceMatcher.PrettyLabel(entry.Desc, entry.chore.name, owner),
                AssetName = entry.chore.name,
                Keywords = $"{entry.chore.name} {entry.Desc} {entry.Hint} {entry.chore.AnimationName}",
                Position = entry.chore.Pos,
                Rotation = entry.chore.Rot,
                Animation = entry.chore.AnimationName,
                Source = source,
                OwnerName = owner,
                Entry = entry,
                UseObject = entry.UseObject,
                ClothesToUse = entry.clothesToUse ?? "",
                HasRootMotion = entry.hasRootMotion,
                Hint = entry.Hint ?? ""
            };

            Add(spots, spot, nav);
        }

        /// <summary>
        /// ChoreInPlace components: spots placed in the scene (each one owns a trigger and a SexZone).
        /// They are shared by every NPC, so they act as a fallback when the routine has no match.
        /// </summary>
        private static void AddPlacedSpots(List<LocationSpot> spots, NpcNavigatorAndRutines nav)
        {
            ChoreInPlace[] placed;
            try
            {
                placed = UnityEngine.Object.FindObjectsByType<ChoreInPlace>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug($"LocationCatalog: could not enumerate ChoreInPlace ({ex.Message}).");
                return;
            }

            foreach (var component in placed)
            {
                if (component == null || component.Chore == null || component.Chore.chore == null) continue;

                var chore = component.Chore.chore;
                var spot = new LocationSpot
                {
                    Name = PlaceMatcher.PrettyLabel(string.IsNullOrEmpty(component.Chore.Desc) ? chore.name : component.Chore.Desc),
                    AssetName = chore.name,
                    Keywords = $"{chore.name} {component.gameObject.name} {component.Chore.Desc} {chore.AnimationName}",
                    Position = chore.Pos,
                    Rotation = chore.Rot,
                    Animation = chore.AnimationName,
                    Source = "spot",
                    Entry = component.Chore,
                    UseObject = component.Chore.UseObject,
                    ClothesToUse = component.Chore.clothesToUse ?? "",
                    HasRootMotion = component.Chore.hasRootMotion,
                    Hint = component.Chore.Hint ?? ""
                };

                Add(spots, spot, nav);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Resolution

        /// <summary>
        /// Finds the spot that best matches a place name or a player sentence. A request that is
        /// exactly one of the labels (which is what the AI is told to send) wins outright; otherwise
        /// the best score wins and ties are broken by distance to the NPC.
        /// </summary>
        public static LocationSpot Resolve(string query, List<LocationSpot> spots, NpcNavigatorAndRutines nav)
        {
            if (spots == null || spots.Count == 0 || string.IsNullOrEmpty(query)) return null;

            string queryKey = PlaceMatcher.Normalize(query);

            LocationSpot best = null;
            int bestScore = 0;
            float bestDistance = float.MaxValue;

            foreach (var spot in spots)
            {
                // The AI copies a label from the list it was given, so an exact name is not a guess:
                // it must not lose a tie to a nearby look-alike (two bathrooms, several sweeping spots).
                if (queryKey.Length > 0 && queryKey == PlaceMatcher.Normalize(spot.Name)) return spot;

                int score = PlaceMatcher.ScoreRequest(query, spot.Keywords.Length > 0 ? spot.Keywords : spot.Name);
                if (score <= 0) continue;

                float distance = nav != null ? Vector3.Distance(nav.transform.position, spot.Position) : 0f;

                if (score > bestScore || (score == bestScore && distance < bestDistance))
                {
                    best = spot;
                    bestScore = score;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>Comma-separated short list of known places, for chat feedback and the prompt.</summary>
        public static string Describe(List<LocationSpot> spots, int max = MAX_LISTED_SPOTS)
        {
            if (spots == null || spots.Count == 0) return "";

            var sb = new StringBuilder();
            int count = 0;
            foreach (var spot in spots)
            {
                if (count >= max) { sb.Append(", ..."); break; }
                if (count > 0) sb.Append(", ");
                sb.Append(spot.Name);
                count++;
            }
            return sb.ToString();
        }

        /// <summary>Formats a position (invariant, 2 decimals).</summary>
        public static string FormatVector(Vector3 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2}", v.x, v.y, v.z);
        }
    }
}
