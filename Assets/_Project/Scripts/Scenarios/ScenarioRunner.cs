using System.Collections;
using Game.Atoms;
using UnityEngine;

namespace Game.Scenarios
{
    /// <summary>
    /// Applies one <see cref="Scenario"/> shortly after the game starts, so every system has finished its own
    /// start-up (and any ResetValue) first. The Editor's scenario launcher adds one automatically; you can also place
    /// it in a scene for a development build.
    /// </summary>
    public sealed class ScenarioRunner : MonoBehaviour
    {
        [Required, SerializeField] private Scenario _scenario;
        [Tooltip("Frames to wait after Start before applying (1 = after every Start has run).")]
        [SerializeField, Min(0)] private int _delayFrames = 1;

        /// <summary>Sets the scenario before Start runs (used by the Editor launcher and tests).</summary>
        internal void Configure(Scenario scenario, int delayFrames)
        {
            _scenario = scenario;
            _delayFrames = Mathf.Max(0, delayFrames);
        }

        private IEnumerator Start()
        {
            if (_scenario == null)
            {
                Debug.LogError($"{nameof(ScenarioRunner)} on '{name}' is missing its '{nameof(_scenario)}'.", this);
                yield break;
            }

            for (int i = 0; i < _delayFrames; i++) yield return null;

            int applied = _scenario.Apply();
            Debug.Log($"Scenario '{_scenario.name}' applied ({applied} step(s)).", _scenario);
        }
    }
}
