using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor only: drops objects from the selection once they are destroyed, and clears play-mode selections before
/// leaving Play mode. Otherwise the Inspector keeps showing a dead object (the placement ghost, a range sphere, ...)
/// and Unity throws MissingReferenceException / SerializedObjectNotCreatableException from its own editors.
/// </summary>
[InitializeOnLoad]
public static class ClearStaleSelection
{
    private static readonly List<Object> Alive = new List<Object>();

    static ClearStaleSelection()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.hierarchyChanged += DropDestroyed; // fires right after objects are destroyed
        EditorApplication.update += DropDestroyed;           // backstop, cheap when nothing is selected
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
            Selection.activeObject = null;
    }

    private static void DropDestroyed()
    {
        int count = Selection.count;
        if (count == 0) return;

        Object[] selected = Selection.objects; // may already leave out destroyed objects, so compare with the count too
        Alive.Clear();
        foreach (Object o in selected)
        {
            if (o != null)
                Alive.Add(o);
        }

        if (Alive.Count != count)
            Selection.objects = Alive.ToArray();
    }
}
