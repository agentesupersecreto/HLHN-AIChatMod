using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Diagnostic list of the animations of every character in the scene, written on demand from the
    /// [DBG] tab. Two sources, both read from the running game:
    ///
    ///   * "routine animations" — the pose each entry of the character's routine plays
    ///     (NpcNavigatorAndRutines.Chores.chore.AnimationName). These are the poses the goto command
    ///     can reach, because LocationCatalog and this dump walk exactly the same arrays;
    ///   * "animator clips" — every clip of the character's Animator controller, read through
    ///     Animator.runtimeAnimatorController.animationClips, which is also how the game itself looks
    ///     a clip up by name.
    ///
    /// Character names are the ones the mod already uses (the object name), and "main"/"secondary"
    /// follows the game's own TalkInteractions.IsARandomCharacter flag.
    /// </summary>
    public static class AnimationCatalog
    {
        /// <summary>File name of the dump, inside BepInEx/config/AIChat.</summary>
        public const string DumpFileName = "animations_dump.txt";

        /// <summary>
        /// Writes animations_dump.txt with every character of the scene and returns its full path,
        /// or null when the characters could not be enumerated or the file could not be written.
        /// </summary>
        public static string Dump()
        {
            NpcNavigatorAndRutines[] navs;
            try
            {
                // Inactive characters count too: one of the three may be inside a room/disabled object.
                navs = UnityEngine.Object.FindObjectsByType<NpcNavigatorAndRutines>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            }
            catch (Exception ex)
            {
                Plugin.LogWarning($"AnimationCatalog: could not enumerate the characters: {ex.Message}");
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
            sb.AppendLine($"Characters ({navs.Length}):");

            foreach (var nav in navs)
            {
                if (nav == null) continue;

                string name = nav.transform.name;
                sb.AppendLine();
                sb.AppendLine($"  {name} ({(IsSecondary(nav) ? "secondary character" : "main character")})");

                var routine = RoutineAnimations(nav);
                sb.AppendLine($"      routine animations ({routine.Count}): " +
                              (routine.Count == 0 ? "(none)" : string.Join(", ", routine.ToArray())));

                var clips = ClipNames(nav.Animator);
                sb.AppendLine($"      animator clips ({clips.Count}): " +
                              (clips.Count == 0 ? "(none)" : string.Join(", ", clips.ToArray())));
            }

            string path = Plugin.GetConfigFilePath(DumpFileName);
            try
            {
                Plugin.EnsureConfigDir();
                File.WriteAllText(path, sb.ToString());
                Plugin.LogInfo($"Wrote the animation list of {navs.Length} character(s) to {path}");
            }
            catch (Exception ex)
            {
                Plugin.LogWarning($"Could not write {DumpFileName}: {ex.Message}");
                return null;
            }

            return path;
        }

        /// <summary>The game flags the women who do not live in the house as random characters, except for the four main characters.</summary>
        private static bool IsSecondary(NpcNavigatorAndRutines nav)
        {
            if (nav == null) return true;
            string name = nav.transform.name;
            if (Core.CharacterContextSnapshot.IsMainCharacterName(name)) return false;
            var talk = nav.GetComponent<TalkInteractions>();
            return talk != null && talk.IsARandomCharacter;
        }

        /// <summary>
        /// Every pose the character's routine can play, without duplicates and in a stable order.
        /// Walks the same routine arrays as <see cref="LocationCatalog.Collect"/>.
        /// </summary>
        private static List<string> RoutineAnimations(NpcNavigatorAndRutines nav)
        {
            var names = new List<string>();
            foreach (var entry in RoutineChores(nav))
            {
                var chore = entry != null ? entry.chore : null;
                string anim = chore != null ? chore.AnimationName : null;
                if (!string.IsNullOrEmpty(anim) && !names.Contains(anim))
                    names.Add(anim);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Every routine entry of a character, in the order the mod reads them.</summary>
        private static IEnumerable<NpcNavigatorAndRutines.Chores> RoutineChores(NpcNavigatorAndRutines nav)
        {
            // The weekly arrays, then today's list, then the single chores, exactly like LocationCatalog.
            var arrays = new NpcNavigatorAndRutines.Chores[][]
            {
                nav.AllChoresRecopilated,
                nav.Monday, nav.Tuesday, nav.Wednesday, nav.Thursday,
                nav.Friday, nav.Saturday, nav.Sunday,
                nav.SpecialChores
            };

            foreach (var array in arrays)
            {
                if (array == null) continue;
                foreach (var entry in array)
                    if (entry != null) yield return entry;
            }

            if (nav.CurrentDayChores != null)
            {
                foreach (var entry in nav.CurrentDayChores)
                    if (entry != null) yield return entry;
            }

            if (nav.NormalEndDay != null) yield return nav.NormalEndDay;
            if (nav.CurrentTask != null) yield return nav.CurrentTask;
            if (nav.lateNightChore != null) yield return nav.lateNightChore;
        }

        /// <summary>
        /// Clip names of the character's Animator controller, as the game reads them when it plays a
        /// pose by name. Empty when there is no animator, which is not an error.
        /// </summary>
        private static List<string> ClipNames(Animator animator)
        {
            var names = new List<string>();
            if (animator == null) return names;

            try
            {
                var controller = animator.runtimeAnimatorController;
                if (controller == null) return names;

                foreach (var clip in controller.animationClips)
                {
                    if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                    if (!names.Contains(clip.name)) names.Add(clip.name);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug($"AnimationCatalog: could not read the animator clips: {ex.Message}");
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
