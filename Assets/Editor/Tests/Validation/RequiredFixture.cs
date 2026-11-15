using Game.Atoms;
using UnityAtoms.BaseAtoms;
using UnityEngine;

namespace Game.Validation.Tests
{
    /// <summary>
    /// Fixture with one field per kind the validator understands. A ScriptableObject because MonoBehaviours declared in
    /// an Editor assembly cannot be added to GameObjects; the validator checks any UnityEngine.Object the same way.
    /// </summary>
    internal sealed class RequiredFixture : ScriptableObject
    {
#pragma warning disable 0649
        [Required, SerializeField] private GameObject _target;
        [Required, SerializeField] private IntReference _amount = new IntReference();
        [Required, SerializeField] private VoidBaseEventReference _onHit = new VoidBaseEventReference();
        [Required, SerializeField] private Transform[] _points = new Transform[0];
        [SerializeField] private GameObject _optional;
#pragma warning restore 0649

        internal Transform[] Points { set => _points = value; }
    }
}
