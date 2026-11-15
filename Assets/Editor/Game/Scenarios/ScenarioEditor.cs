using Game.Scenarios;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Setup
{
    /// <summary>Inspector for <see cref="Scenario"/> assets: the normal fields plus a "Play This Scenario" button.</summary>
    [CustomEditor(typeof(Scenario))]
    public sealed class ScenarioEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Play This Scenario", GUILayout.Height(28)))
                    ScenarioLauncher.Play((Scenario)target);
            }
        }
    }
}
