using Game.Scenarios;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Enters Play Mode with a <see cref="Scenario"/>: optionally switches the start scene, then adds a
    /// <see cref="ScenarioRunner"/> once Play Mode has begun. The last played scenario is remembered per machine
    /// (menu <c>Game/Scenarios/Play Last Scenario</c>).
    /// </summary>
    [InitializeOnLoad]
    public static class ScenarioLauncher
    {
        private const string PendingKey = "Game.Scenarios.Pending";          // SessionState: survives the domain reload
        private const string StartSceneSetKey = "Game.Scenarios.StartSceneSet";
        private const string LastKey = "Game.Scenarios.Last";                // EditorPrefs: survives restarts

        static ScenarioLauncher()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Starts Play Mode with <paramref name="scenario"/> (asks to save modified scenes first).</summary>
        public static void Play(Scenario scenario)
        {
            if (scenario == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(scenario));
            SessionState.SetString(PendingKey, guid);
            EditorPrefs.SetString(LastKey, guid);

            if (!string.IsNullOrEmpty(scenario.StartScenePath))
            {
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenario.StartScenePath);
                if (scene == null)
                {
                    Debug.LogError($"Scenario '{scenario.name}': start scene '{scenario.StartScenePath}' not found.", scenario);
                    SessionState.EraseString(PendingKey);
                    return;
                }
                EditorSceneManager.playModeStartScene = scene;
                SessionState.SetBool(StartSceneSetKey, true);
            }

            EditorApplication.isPlaying = true;
        }

        /// <summary>The scenario played last on this machine, or null.</summary>
        public static Scenario Last
        {
            get
            {
                string guid = EditorPrefs.GetString(LastKey, string.Empty);
                return string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<Scenario>(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        [MenuItem("Game/Scenarios/Play Last Scenario", priority = 1)]
        private static void PlayLast()
        {
            Scenario last = Last;
            if (last == null) Debug.LogWarning("No scenario played yet. Use 'Game/Scenarios/Scenario Picker'.");
            else Play(last);
        }

        [MenuItem("Game/Scenarios/Play Last Scenario", true)]
        private static bool PlayLastValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                string guid = SessionState.GetString(PendingKey, string.Empty);
                if (string.IsNullOrEmpty(guid)) return;
                SessionState.EraseString(PendingKey);

                var scenario = AssetDatabase.LoadAssetAtPath<Scenario>(AssetDatabase.GUIDToAssetPath(guid));
                if (scenario == null) return;

                var go = new GameObject($"[Scenario] {scenario.name}");
                go.AddComponent<ScenarioRunner>().Configure(scenario, 1);
            }
            else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(StartSceneSetKey, false))
            {
                EditorSceneManager.playModeStartScene = null; // back to normal Play behaviour
                SessionState.EraseBool(StartSceneSetKey);
            }
        }
    }
}
