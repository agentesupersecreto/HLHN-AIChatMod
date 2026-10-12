using DialogInterceptorMod.Core;

namespace DialogInterceptorMod.Game
{
    /// <summary>
    /// Helper for NPC navigation in HLHN.
    /// NPCs do not have a generic GoTo API: they walk to "chores" (spots with a position and an
    /// animation) driven by NpcNavigatorAndRutines. <see cref="LocationCatalog"/> exposes those
    /// spots, and <see cref="NpcGoToHandler"/> sends an NPC to one of them.
    /// </summary>
    public static class MovementHelper
    {
        /// <summary>
        /// Returns a comma-separated list of the places the current NPC can be sent to, based on the
        /// routine chores of the NPC and on the user-defined places in locations.txt.
        /// </summary>
        public static string GetAvailableGoToTargets()
        {
            var nav = DialogBehaviour.Instance != null ? DialogBehaviour.Instance.CurrentNavigator : null;
            if (nav == null) return "";

            return LocationCatalog.Describe(LocationCatalog.Collect(nav));
        }
    }
}
