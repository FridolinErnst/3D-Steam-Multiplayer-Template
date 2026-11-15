using System.Collections.Generic;
using System.Linq;
using Game.Atoms;
using Game.Editor.Setup;
using Game.Support.Tests;
using NUnit.Framework;
using UnityAtoms.BaseAtoms;
using UnityEngine;

namespace Game.Validation.Tests
{
    /// <summary>Checks the <see cref="RequiredAttribute"/> rules on a fixture, and that the project itself has no empty required fields.</summary>
    public sealed class RequiredFieldTests
    {
        private TestScene _scene;
        private AtomFactory _atoms;
        private RequiredFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _scene = TestScene.New();
            _atoms = new AtomFactory();
            _fixture = _atoms.New<RequiredFixture>();
        }

        [TearDown]
        public void TearDown()
        {
            _scene.Dispose();
            _atoms.DestroyAll();
        }

        [Test]
        public void Empty_Object_Field_Is_Reported()
        {
            CollectionAssert.Contains(EmptyFields(), "_target");
        }

        [Test]
        public void Optional_Field_Is_Never_Reported()
        {
            CollectionAssert.DoesNotContain(EmptyFields(), "_optional");
        }

        [Test]
        public void Atom_Reference_In_Value_Usage_Counts_As_Filled()
        {
            TestScene.SetValueRef(_fixture, "_amount", 5);

            CollectionAssert.DoesNotContain(EmptyFields(), "_amount");
        }

        [Test]
        public void Atom_Reference_In_Variable_Usage_Without_Variable_Is_Reported()
        {
            TestScene.SetVariableRef(_fixture, "_amount", null);

            CollectionAssert.Contains(EmptyFields(), "_amount");
        }

        [Test]
        public void Event_Reference_Without_Event_Is_Reported()
        {
            CollectionAssert.Contains(EmptyFields(), "_onHit");
        }

        [Test]
        public void Empty_Required_Array_Is_Reported()
        {
            CollectionAssert.Contains(EmptyFields(), "_points");
        }

        [Test]
        public void Fully_Wired_Component_Has_No_Issues()
        {
            TestScene.Set(_fixture, "_target", _scene.Go("Target"));
            TestScene.SetVariableRef(_fixture, "_amount", _atoms.Int(3));
            TestScene.SetEventRef(_fixture, "_onHit", _atoms.New<VoidEvent>());
            _fixture.Points = new[] { _scene.Go("Point").transform };

            CollectionAssert.IsEmpty(EmptyFields());
        }

        [Test]
        public void Project_Prefabs_And_Build_Scenes_Have_No_Empty_Required_Fields()
        {
            List<RequiredFieldValidator.Issue> issues = RequiredFieldValidator.ScanProject();

            Assert.IsEmpty(issues, "Empty required fields:\n  " + string.Join("\n  ", issues));
        }

        private List<string> EmptyFields()
        {
            var issues = new List<RequiredFieldValidator.Issue>();
            RequiredFieldValidator.CheckComponent(_fixture, "test", issues);
            return issues.Select(i => i.FieldName).ToList();
        }
    }
}
