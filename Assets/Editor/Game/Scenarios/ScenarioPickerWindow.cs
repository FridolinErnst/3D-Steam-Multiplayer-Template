using System.Collections.Generic;
using Game.Scenarios;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Setup
{
    /// <summary>Lists every <see cref="Scenario"/> asset with a Play button (menu <c>Game/Scenarios/Scenario Picker</c>).</summary>
    public sealed class ScenarioPickerWindow : EditorWindow
    {
        private readonly List<Scenario> _scenarios = new List<Scenario>();
        private Vector2 _scroll;

        [MenuItem("Game/Scenarios/Scenario Picker", priority = 0)]
        private static void Open() => GetWindow<ScenarioPickerWindow>("Scenarios");

        private void OnEnable() => Refresh();

        private void OnProjectChange() => Refresh();

        private void Refresh()
        {
            _scenarios.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(Scenario)))
            {
                var s = AssetDatabase.LoadAssetAtPath<Scenario>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) _scenarios.Add(s);
            }
            _scenarios.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            Repaint();
        }

        private void OnGUI()
        {
            if (_scenarios.Count == 0)
            {
                EditorGUILayout.HelpBox("No scenarios yet. Create one with Assets > Create > Game > Scenario " +
                                        "in Assets/_Project/Data/Scenarios/.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                foreach (Scenario s in _scenarios)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(s.name, EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(s.Description)) EditorGUILayout.LabelField(s.Description, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.EndVertical();
                    if (GUILayout.Button("Select", GUILayout.Width(60))) Selection.activeObject = s;
                    if (GUILayout.Button("Play", GUILayout.Width(60))) ScenarioLauncher.Play(s);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
