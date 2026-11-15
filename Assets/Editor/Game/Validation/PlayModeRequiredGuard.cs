using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Stops entering Play Mode when a loaded scene has an empty <see cref="Game.Atoms.RequiredAttribute"/> field, and
    /// logs every problem at once (click a log line to select the component). Toggle with
    /// <c>Game/Validate/Block Play On Empty Required Fields</c> (on by default, stored per machine in EditorPrefs).
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeRequiredGuard
    {
        private const string MenuPath = "Game/Validate/Block Play On Empty Required Fields";
        private const string PrefKey = "Game.Validation.BlockPlayOnEmptyRequired";

        static PlayModeRequiredGuard()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Whether the guard is active on this machine.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            Debug.Log($"Block Play on empty required fields: {(Enabled ? "ON" : "OFF")}");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode || !Enabled) return;

            List<RequiredFieldValidator.Issue> issues = RequiredFieldValidator.ScanOpenScenes();
            if (issues.Count == 0) return;

            foreach (RequiredFieldValidator.Issue issue in issues)
                Debug.LogError("[Required] " + issue.Message, issue.Context);

            EditorApplication.isPlaying = false;
            string message = $"Play blocked: {issues.Count} empty required field(s). See the Console.";
            foreach (SceneView view in SceneView.sceneViews) view.ShowNotification(new GUIContent(message), 4f);
            Debug.LogError(message + $" (Turn this off via '{MenuPath}'.)");
        }
    }
}
