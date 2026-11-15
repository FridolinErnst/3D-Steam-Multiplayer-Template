using Game.Support.Tests;
using NUnit.Framework;
using UnityAtoms.BaseAtoms;

namespace Game.Scenarios.Tests
{
    /// <summary>Checks that a <see cref="Scenario"/> writes its values, raises its events and skips empty rows.</summary>
    public sealed class ScenarioTests
    {
        private AtomFactory _atoms;
        private Scenario _scenario;

        [SetUp]
        public void SetUp()
        {
            _atoms = new AtomFactory();
            _scenario = _atoms.New<Scenario>();
        }

        [TearDown]
        public void TearDown() => _atoms.DestroyAll();

        [Test]
        public void Apply_Sets_Every_Variable()
        {
            // In Edit Mode Variable.Value reads InitialValue, so observe the Changed events instead.
            IntVariable gold = _atoms.Int(0);
            BoolVariable unlocked = _atoms.Bool(false);
            int goldSeen = -1;
            bool unlockedSeen = false;
            void OnGold(int v) => goldSeen = v;
            void OnUnlocked(bool v) => unlockedSeen = v;
            gold.Changed.Register(OnGold);
            unlocked.Changed.Register(OnUnlocked);

            _scenario.SetContent(
                ints: new[] { new Scenario.IntSetting { Variable = gold, Value = 500 } },
                bools: new[] { new Scenario.BoolSetting { Variable = unlocked, Value = true } });
            int applied = _scenario.Apply();

            gold.Changed.Unregister(OnGold);
            unlocked.Changed.Unregister(OnUnlocked);
            Assert.AreEqual(2, applied);
            Assert.AreEqual(500, goldSeen);
            Assert.IsTrue(unlockedSeen);
        }

        [Test]
        public void Apply_Raises_Events_After_Values()
        {
            FloatVariable speed = _atoms.Float(1f);
            VoidEvent started = _atoms.New<VoidEvent>();
            float speedSeen = 0f;
            float speedWhenRaised = -1f;
            void OnSpeed(float v) => speedSeen = v;
            void OnStarted() => speedWhenRaised = speedSeen;
            speed.Changed.Register(OnSpeed);
            started.Register(OnStarted);

            _scenario.SetContent(
                floats: new[] { new Scenario.FloatSetting { Variable = speed, Value = 3f } },
                raise: new[] { started });
            int applied = _scenario.Apply();

            speed.Changed.Unregister(OnSpeed);
            started.Unregister(OnStarted);
            Assert.AreEqual(2, applied);
            Assert.AreEqual(3f, speedWhenRaised, "the event must be raised after the values are set");
        }

        [Test]
        public void Apply_Skips_Empty_Rows()
        {
            _scenario.SetContent(
                ints: new[] { new Scenario.IntSetting { Variable = null, Value = 1 } },
                spawns: new[] { new Scenario.Spawn { Prefab = null } },
                raise: new VoidEvent[] { null });

            Assert.AreEqual(0, _scenario.Apply());
        }
    }
}
