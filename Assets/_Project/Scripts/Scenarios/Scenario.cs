using System;
using UnityAtoms.BaseAtoms;
using UnityEngine;

namespace Game.Scenarios
{
    /// <summary>
    /// A named starting state for testing a feature without playing up to it: Variable values to set, prefabs to
    /// spawn and events to raise right after the game has started. Played from the Editor with
    /// <c>Game/Scenarios/Scenario Picker</c> or the "Play This Scenario" button on the asset; a
    /// <see cref="ScenarioRunner"/> applies it. Keep scenario assets in <c>Assets/_Project/Data/Scenarios/</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Scenario", fileName = "NewScenario")]
    public sealed class Scenario : ScriptableObject
    {
        /// <summary>Sets an IntVariable to a value.</summary>
        [Serializable] public struct IntSetting { public IntVariable Variable; public int Value; }
        /// <summary>Sets a FloatVariable to a value.</summary>
        [Serializable] public struct FloatSetting { public FloatVariable Variable; public float Value; }
        /// <summary>Sets a BoolVariable to a value.</summary>
        [Serializable] public struct BoolSetting { public BoolVariable Variable; public bool Value; }
        /// <summary>Sets a StringVariable to a value.</summary>
        [Serializable] public struct StringSetting { public StringVariable Variable; public string Value; }
        /// <summary>Spawns a prefab at a world position.</summary>
        [Serializable] public struct Spawn { public GameObject Prefab; public Vector3 Position; }

        [SerializeField, TextArea] private string _description;
        [Tooltip("Scene to start in, e.g. Assets/Scenes/SampleScene.unity. Empty = the scene that is open.")]
        [SerializeField] private string _startScenePath;
        [SerializeField] private IntSetting[] _ints = Array.Empty<IntSetting>();
        [SerializeField] private FloatSetting[] _floats = Array.Empty<FloatSetting>();
        [SerializeField] private BoolSetting[] _bools = Array.Empty<BoolSetting>();
        [SerializeField] private StringSetting[] _strings = Array.Empty<StringSetting>();
        [SerializeField] private Spawn[] _spawns = Array.Empty<Spawn>();
        [Tooltip("Raised last, after all values are set and prefabs spawned (e.g. 'UnlockEverything').")]
        [SerializeField] private VoidEvent[] _raise = Array.Empty<VoidEvent>();

        /// <summary>What this scenario is for (shown in the picker).</summary>
        public string Description => _description;

        /// <summary>Scene to start Play Mode in; empty means the currently open scene.</summary>
        public string StartScenePath => _startScenePath;

        /// <summary>Sets every value, spawns every prefab, then raises every event. Returns the number of steps applied.</summary>
        public int Apply()
        {
            int applied = 0;
            foreach (IntSetting s in _ints) if (s.Variable != null) { s.Variable.Value = s.Value; applied++; }
            foreach (FloatSetting s in _floats) if (s.Variable != null) { s.Variable.Value = s.Value; applied++; }
            foreach (BoolSetting s in _bools) if (s.Variable != null) { s.Variable.Value = s.Value; applied++; }
            foreach (StringSetting s in _strings) if (s.Variable != null) { s.Variable.Value = s.Value; applied++; }
            foreach (Spawn s in _spawns) if (s.Prefab != null) { Instantiate(s.Prefab, s.Position, Quaternion.identity); applied++; }
            foreach (VoidEvent e in _raise) if (e != null) { e.Raise(); applied++; }
            return applied;
        }

        /// <summary>Test hook: replaces the content of the scenario.</summary>
        internal void SetContent(IntSetting[] ints = null, FloatSetting[] floats = null, BoolSetting[] bools = null,
            StringSetting[] strings = null, Spawn[] spawns = null, VoidEvent[] raise = null)
        {
            _ints = ints ?? Array.Empty<IntSetting>();
            _floats = floats ?? Array.Empty<FloatSetting>();
            _bools = bools ?? Array.Empty<BoolSetting>();
            _strings = strings ?? Array.Empty<StringSetting>();
            _spawns = spawns ?? Array.Empty<Spawn>();
            _raise = raise ?? Array.Empty<VoidEvent>();
        }
    }
}
