using UnityAtoms.BaseAtoms;

namespace Game.Atoms
{
    /// <summary>Checks conditions wired in the Inspector as Bool References, e.g. "the tray is pulled out" and "it is clean".</summary>
    public static class Requirements
    {
        /// <summary>True when every reference reads true; a missing or empty list asks for nothing.</summary>
        public static bool AllMet(BoolReference[] requires)
        {
            if (requires == null) return true;
            foreach (BoolReference required in requires)
                if (required != null && !required.Value) return false;
            return true;
        }
    }
}
