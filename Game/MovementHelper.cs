using UnityEngine;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Helper for NPC navigation in HLHN.
    /// Uses the game's NpcNavigatorAndRutines system.
    /// </summary>
    public static class MovementHelper
    {
        /// <summary>
        /// Returns a comma-separated list of available rooms/locations the NPC can go to.
        /// In HLHN this is based on the NPC's routine/chore system.
        /// </summary>
        public static string GetAvailableGoToTargets()
        {
            // HLHN doesn't have a generic GoTo system like the other game.
            // NPCs follow routines defined by NpcNavigatorAndRutines.
            // For now, return empty — can be expanded later if needed.
            return "";
        }
    }
}
