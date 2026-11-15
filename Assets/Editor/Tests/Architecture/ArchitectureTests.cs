using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Game.Editor.Setup;
using Game.Support.Tests;
using NUnit.Framework;
using UnityAtoms;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Architecture.Tests
{
    /// <summary>
    /// Enforces the architecture rules of Docs/Design/Architecture.md: module isolation of L2 binders, UI isolation, no
    /// singletons or static events, a pure Game.Core (source token scan + assembly references), forbidden namespace
    /// segments and type names, Game.Core only inside Game.Core, and replay buffer 0 on every Atom event asset.
    /// The rule helpers are tested on fixture types so a broken rule cannot pass silently.
    /// </summary>
    public sealed class ArchitectureTests
    {
        // ---------------------------------------------------------------- rule data

        /// <summary>Namespace segments that shadow UnityEngine/System types (section 1.1).</summary>
        internal static readonly HashSet<string> ForbiddenSegments = new HashSet<string>(StringComparer.Ordinal)
        {
            "Time", "Input", "Random", "Debug", "Object", "Physics", "Application", "Camera", "Light", "Screen", "Cursor",
            "Gizmos", "Mathf", "Resources", "Color", "Math", "Action", "Event", "Animation", "Animator", "Collider",
            "Transform", "Rigidbody", "Texture", "Sprite", "Material", "Mesh", "Shader", "Audio",
        };

        /// <summary>
        /// Type names that may never be used because they equal a namespace segment. Every second segment of a Game.*
        /// namespace that exists in the project is added automatically (see <see cref="ForbiddenTypeNamesInProject"/>).
        /// </summary>
        internal static readonly HashSet<string> ForbiddenTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "UI", "Data", "Contracts", "Core", "Atoms",
        };

        /// <summary>Second namespace segments that are L1/L0 (never an L2 module).</summary>
        private static readonly HashSet<string> LayerOneSegments = new HashSet<string>(StringComparer.Ordinal)
        {
            "Contracts", "Data", "Core",
        };

        /// <summary>Game.* namespaces whose types the module rule ignores (shared glue such as Game.Atoms helpers).</summary>
        private static readonly HashSet<string> LegacySegments = new HashSet<string>(StringComparer.Ordinal)
        {
            "Atoms",
        };

        /// <summary>Exact namespaces the module rule ignores (add legacy code here when you must, with a reason).</summary>
        private static readonly HashSet<string> LegacyExactNamespaces = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        /// <summary>Namespace prefixes whose MonoBehaviours UI views may reference (UnityAtoms: listeners/instancers).</summary>
        private static readonly string[] UiAllowedPrefixes = { "Game.UI", "Game.Contracts", "UnityEngine", "UnityAtoms" };

        /// <summary>Forbidden source tokens in Assets/_Project/Core (applied after stripping comments and strings).</summary>
        internal static readonly (string Name, Regex Pattern)[] CoreForbiddenTokens =
        {
            ("UnityEngine.Random", new Regex(@"\bUnityEngine\s*\.\s*Random\b")),
            ("Random.", new Regex(@"(?<![\w.])Random\s*\.")),
            ("Time.", new Regex(@"(?<![\w.])Time\s*\.")),
            ("Physics", new Regex(@"\bPhysics")),
            ("NavMesh", new Regex(@"\bNavMesh")),
            ("FindObjectOfType", new Regex(@"\bFindObjectsOfType|\bFindObjectOfType|\bFindFirstObjectByType|\bFindAnyObjectByType")),
            ("GameObject.Find", new Regex(@"\bGameObject\s*\.\s*Find")),
            ("Debug.Log", new Regex(@"\bDebug\s*\.\s*Log")),
            ("MonoBehaviour", new Regex(@"\bMonoBehaviour\b")),
            ("ScriptableObject", new Regex(@"\bScriptableObject\b")),
            ("DateTime.Now", new Regex(@"\bDateTime\s*\.\s*(Now|UtcNow)\b")),
        };

        /// <summary>Assembly names Game.Core must never reference.</summary>
        private static readonly string[] CoreForbiddenReferencePrefixes =
        {
            "Assembly-CSharp", "UnityAtoms", "Unity.AI.Navigation", "UnityEngine.PhysicsModule", "UnityEngine.AIModule",
            "UnityEditor", "Unity.InputSystem",
        };

        private const string CoreSourceRoot = "Assets/_Project/Core";
        private const string AtomsRoot = "Assets/_Project/Atoms/";

        private const BindingFlags Declared =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // ---------------------------------------------------------------- assemblies

        private static Assembly GameAssembly => typeof(Game.Contracts.UnityRandomSource).Assembly; // Game.Runtime
        private static Assembly CoreAssembly => typeof(Game.Core.SeededRandom).Assembly;        // Game.Core
        private static Assembly EditorAssembly => typeof(ArchitectureTests).Assembly;           // Assembly-CSharp-Editor

        private static IEnumerable<Assembly> NamespaceCheckedAssemblies()
        {
            var seen = new HashSet<Assembly> { GameAssembly, CoreAssembly, EditorAssembly };
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Game.Core.Tests") seen.Add(a);
            return seen;
        }

        // ================================================================ 1. L2 module isolation

        [Test]
        public void L2_Modules_Do_Not_Reference_MonoBehaviours_Of_Other_L2_Modules()
        {
            var violations = new List<string>();
            foreach (Type t in TypesOf(GameAssembly))
            {
                if (!IsL2Source(t.Namespace)) continue;
                CollectCrossModuleViolations(t, GameAssembly, violations);
            }
            AssertNone(violations, "L2 cross-module MonoBehaviour references (use L0/L1 interfaces via TryGetComponent)");
        }

        // ================================================================ 2. UI isolation

        [Test]
        public void UI_Types_Reference_Only_UI_Contracts_And_Engine_MonoBehaviours()
        {
            var violations = new List<string>();
            foreach (Type t in TypesOf(GameAssembly))
            {
                if (!IsUiNamespace(t.Namespace)) continue;
                CollectUiViolations(t, violations);
            }
            AssertNone(violations, "UI references to gameplay MonoBehaviours");
        }

        // ================================================================ 3. singletons / static events

        [Test]
        public void No_Static_Events_Or_Instance_Singletons_In_Game_Or_Core()
        {
            var violations = new List<string>();
            foreach (Type t in TypesOf(GameAssembly))
                if (IsGameNamespace(t.Namespace)) CollectSingletonViolations(t, violations);
            foreach (Type t in TypesOf(CoreAssembly))
                CollectSingletonViolations(t, violations);
            AssertNone(violations, "static events / static Instance members");
        }

        // ================================================================ 4. pure core

        [Test]
        public void Core_Sources_Contain_No_Forbidden_Tokens()
        {
            Assert.IsTrue(Directory.Exists(CoreSourceRoot), $"'{CoreSourceRoot}' not found.");
            var violations = new List<string>();
            foreach (string file in Directory.GetFiles(CoreSourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                string code = StripCommentsAndStrings(File.ReadAllText(file));
                foreach (string token in FindForbiddenTokens(code))
                    violations.Add($"{file.Replace('\\', '/')}: '{token}'");
            }
            AssertNone(violations, "forbidden tokens in Game.Core");
        }

        [Test]
        public void Core_Has_No_Editor_Folder()
        {
            string[] editorDirs = Directory.GetDirectories(CoreSourceRoot, "Editor", SearchOption.AllDirectories);
            Assert.IsEmpty(editorDirs, "Assets/_Project/Core must not contain an Editor folder.");
        }

        [Test]
        public void Core_Assembly_References_No_Atoms_Game_Physics_Navigation_Or_Editor()
        {
            var violations = new List<string>();
            foreach (AssemblyName reference in CoreAssembly.GetReferencedAssemblies())
            {
                foreach (string prefix in CoreForbiddenReferencePrefixes)
                    if (reference.Name.StartsWith(prefix, StringComparison.Ordinal)) violations.Add(reference.Name);
            }
            AssertNone(violations, "Game.Core assembly references");
        }

        [Test]
        public void Core_Types_Are_Not_Unity_Objects()
        {
            var violations = new List<string>();
            foreach (Type t in TypesOf(CoreAssembly))
                if (typeof(Object).IsAssignableFrom(t)) violations.Add(t.FullName);
            AssertNone(violations, "UnityEngine.Object subclasses in Game.Core");
        }

        // ================================================================ 5. namespaces and type names

        [Test]
        public void No_Game_Namespace_Uses_A_Forbidden_Segment()
        {
            var violations = new List<string>();
            foreach (Assembly a in NamespaceCheckedAssemblies())
            {
                foreach (Type t in TypesOf(a))
                {
                    string bad = ForbiddenSegmentOf(t.Namespace);
                    if (bad != null) violations.Add($"{t.FullName} ({a.GetName().Name}): segment '{bad}'");
                }
            }
            AssertNone(violations, "forbidden namespace segments");
        }

        [Test]
        public void No_Game_Type_Is_Named_Like_A_Namespace_Segment()
        {
            var violations = new List<string>();
            HashSet<string> forbidden = ForbiddenTypeNamesInProject();
            foreach (Assembly a in NamespaceCheckedAssemblies())
            {
                foreach (Type t in TypesOf(a))
                {
                    if (t.IsNested || !IsGameNamespace(t.Namespace)) continue;
                    if (forbidden.Contains(SimpleName(t))) violations.Add($"{t.FullName} ({a.GetName().Name})");
                }
            }
            AssertNone(violations, "types named like Game.* namespace segments");
        }

        [Test]
        public void Game_Core_Namespace_Is_Declared_Only_In_Game_Core()
        {
            var violations = new List<string>();
            foreach (Assembly a in new[] { GameAssembly, EditorAssembly })
            {
                foreach (Type t in TypesOf(a))
                {
                    string ns = t.Namespace;
                    if (ns == "Game.Core" || (ns != null && ns.StartsWith("Game.Core.", StringComparison.Ordinal)))
                        violations.Add($"{t.FullName} ({a.GetName().Name})");
                }
            }
            AssertNone(violations, "Game.Core.* declared outside Game.Core");
        }

        [Test]
        public void Gameplay_Code_Lives_In_Game_Runtime_Not_Assembly_CSharp()
        {
            var violations = new List<string>();
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "Assembly-CSharp") continue;
                foreach (Type t in TypesOf(a))
                    if (!t.Name.StartsWith("UnitySourceGenerated", StringComparison.Ordinal)) violations.Add(t.FullName);
            }
            AssertNone(violations, "types in Assembly-CSharp (move them under Assets/_Project/Scripts/, the Game.Runtime asmdef)");
        }

        // ================================================================ 6. atom assets

        [Test]
        public void Atom_Event_Assets_Have_Zero_Replay_Buffer()
        {
            var violations = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { AtomsRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) || !path.EndsWith(".asset", StringComparison.Ordinal)) continue;

                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(o is AtomEventBase evt)) continue;
                    int replay = AtomFactory.ReplayOf(evt);
                    if (replay > 0) violations.Add($"{path} ({evt.name}): ReplayBufferSize {replay}");
                }
            }
            AssertNone(violations, "Atom events with replay buffers (set Replay Buffer Size to 0)");
        }

        // ================================================================ rule self-tests

        [Test]
        public void ModuleOf_Uses_Second_Segment()
        {
            Assert.AreEqual("Combat", ModuleOf("Game.Combat"));
            Assert.AreEqual("Combat", ModuleOf("Game.Combat.Sub"));
            Assert.IsNull(ModuleOf(null));
            Assert.IsNull(ModuleOf("Game"));
            Assert.IsNull(ModuleOf("UnityAtoms.BaseAtoms"));
        }

        [Test]
        public void L1_And_Shared_Namespaces_Are_Exempt()
        {
            Assert.IsTrue(IsExempt(null));
            Assert.IsTrue(IsExempt("Game.Contracts"));
            Assert.IsTrue(IsExempt("Game.Data"));
            Assert.IsTrue(IsExempt("Game.Atoms"));
            Assert.IsTrue(IsExempt("UnityAtoms.BaseAtoms"));
            Assert.IsFalse(IsExempt("Game.Combat"));
            Assert.IsFalse(IsExempt("Game.UI.Hud"));
        }

        [Test]
        public void CrossModule_Rule_Detects_Field_Property_Array_And_Generic_References()
        {
            var violations = new List<string>();
            CollectCrossModuleViolations(typeof(Game.ArchFixtureA.Offender), EditorAssembly, violations);

            Assert.AreEqual(5, violations.Count, string.Join("\n", violations));
        }

        [Test]
        public void CrossModule_Rule_Allows_Same_Module_Interfaces_And_L1()
        {
            var violations = new List<string>();
            CollectCrossModuleViolations(typeof(Game.ArchFixtureA.WellBehaved), EditorAssembly, violations);

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void Ui_Rule_Flags_Gameplay_MonoBehaviour_But_Not_Engine_Ones()
        {
            var violations = new List<string>();
            CollectUiViolations(typeof(Game.ArchFixtureA.UiLikeOffender), violations);

            Assert.AreEqual(1, violations.Count, string.Join("\n", violations));
            StringAssert.Contains("ProbeB", violations[0]);
        }

        [Test]
        public void Singleton_Rule_Flags_Static_Event_And_Instance()
        {
            var violations = new List<string>();
            CollectSingletonViolations(typeof(Game.ArchFixtureA.SingletonLike), violations);

            Assert.AreEqual(2, violations.Count, string.Join("\n", violations));
        }

        [Test]
        public void Token_Scan_Ignores_Comments_And_Strings_But_Finds_Code()
        {
            const string code = "// Time.deltaTime in a comment\n/* Physics.Raycast */\nvar s = \"Debug.Log\";\n" +
                                "var r = new SeededRandom(1); var d = DateTime.UtcNow; float t = Time.time;";
            List<string> found = FindForbiddenTokens(StripCommentsAndStrings(code)).ToList();

            CollectionAssert.AreEquivalent(new[] { "Time.", "DateTime.Now" }, found);
        }

        [Test]
        public void Token_Scan_Finds_Random_And_MonoBehaviour()
        {
            List<string> found = FindForbiddenTokens("class A : MonoBehaviour { float v = UnityEngine.Random.value; }").ToList();

            CollectionAssert.Contains(found, "UnityEngine.Random");
            CollectionAssert.Contains(found, "MonoBehaviour");
            CollectionAssert.DoesNotContain(found, "Random."); // qualified access is reported once, as UnityEngine.Random
        }

        [Test]
        public void ForbiddenSegmentOf_Checks_Only_Game_Namespaces()
        {
            Assert.AreEqual("Time", ForbiddenSegmentOf("Game.Core.Time"));
            Assert.AreEqual("Random", ForbiddenSegmentOf("Game.Random.Things"));
            Assert.IsNull(ForbiddenSegmentOf("Game.Core.Clock"));
            Assert.IsNull(ForbiddenSegmentOf("Game.Navigation"));
            Assert.IsNull(ForbiddenSegmentOf("UnityEngine.Physics"));
            Assert.IsNull(ForbiddenSegmentOf(null));
        }

        // ================================================================ rule implementation

        /// <summary>Module key: the second namespace segment (Game.Combat.X → "Combat"); null outside Game.*.</summary>
        internal static string ModuleOf(string ns)
        {
            if (!IsGameNamespace(ns) || ns == "Game") return null;
            return ns.Split('.')[1];
        }

        /// <summary><see cref="ForbiddenTypeNames"/> plus every second segment of a Game.* namespace in the project.</summary>
        internal static HashSet<string> ForbiddenTypeNamesInProject()
        {
            var names = new HashSet<string>(ForbiddenTypeNames, StringComparer.Ordinal);
            foreach (Assembly a in NamespaceCheckedAssemblies())
            {
                foreach (Type t in TypesOf(a))
                {
                    string module = ModuleOf(t.Namespace);
                    if (module != null && !module.StartsWith("ArchFixture", StringComparison.Ordinal)) names.Add(module);
                }
            }
            return names;
        }

        /// <summary>True for types the module rule ignores: non-Game, L1/L0 and legacy namespaces.</summary>
        internal static bool IsExempt(string ns)
        {
            if (!IsGameNamespace(ns) || ns == "Game") return true;
            if (LegacyExactNamespaces.Contains(ns)) return true;
            string second = ns.Split('.')[1];
            return LayerOneSegments.Contains(second) || LegacySegments.Contains(second);
        }

        private static bool IsL2Source(string ns) => !IsExempt(ns) && !IsUiNamespace(ns);

        private static bool IsUiNamespace(string ns) =>
            ns == "Game.UI" || (ns != null && ns.StartsWith("Game.UI.", StringComparison.Ordinal));

        private static bool IsGameNamespace(string ns) =>
            ns == "Game" || (ns != null && ns.StartsWith("Game.", StringComparison.Ordinal));

        /// <summary>
        /// Adds a violation for every serialized/public member of <paramref name="source"/> whose type (arrays and
        /// generic arguments unwrapped) is a MonoBehaviour of <paramref name="scanned"/> in a different, non-exempt module.
        /// </summary>
        internal static void CollectCrossModuleViolations(Type source, Assembly scanned, List<string> violations)
        {
            string sourceModule = ModuleOf(source.Namespace);
            foreach ((string member, Type type) in ReferencedTypes(source))
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.Assembly != scanned) continue;
                if (IsExempt(type.Namespace)) continue;
                if (ModuleOf(type.Namespace) == sourceModule) continue;
                violations.Add($"{source.FullName}.{member} -> {type.FullName}");
            }
        }

        /// <summary>Adds a violation for every referenced MonoBehaviour outside Game.UI, Game.Contracts and the engine/Atoms.</summary>
        internal static void CollectUiViolations(Type source, List<string> violations)
        {
            foreach ((string member, Type type) in ReferencedTypes(source))
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                string ns = type.Namespace ?? string.Empty;
                bool allowed = false;
                foreach (string prefix in UiAllowedPrefixes)
                {
                    if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed) violations.Add($"{source.FullName}.{member} -> {type.FullName}");
            }
        }

        /// <summary>Adds a violation for every static event and every static field/property called Instance.</summary>
        internal static void CollectSingletonViolations(Type t, List<string> violations)
        {
            if (IsCompilerGenerated(t)) return;
            const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (EventInfo e in t.GetEvents(statics)) violations.Add($"{t.FullName}: static event '{e.Name}'");
            if (t.GetField("Instance", statics) != null) violations.Add($"{t.FullName}: static field 'Instance'");
            if (t.GetProperty("Instance", statics) != null) violations.Add($"{t.FullName}: static property 'Instance'");
        }

        /// <summary>Every (member, leaf type) pair of serialized fields, public fields/properties and public method signatures.</summary>
        private static IEnumerable<(string Member, Type Type)> ReferencedTypes(Type t)
        {
            if (IsCompilerGenerated(t)) yield break;

            foreach (FieldInfo f in t.GetFields(Declared))
            {
                if (IsCompilerGenerated(f)) continue;
                bool serialized = !f.IsStatic && !f.IsNotSerialized &&
                                  (f.IsPublic || f.IsDefined(typeof(SerializeField), true) || f.IsDefined(typeof(SerializeReference), true));
                if (!serialized && !f.IsPublic) continue;
                foreach (Type leaf in Leaves(f.FieldType)) yield return (f.Name, leaf);
            }

            foreach (PropertyInfo p in t.GetProperties(Declared))
            {
                MethodInfo getter = p.GetGetMethod(false);
                if (getter == null) continue;
                foreach (Type leaf in Leaves(p.PropertyType)) yield return (p.Name, leaf);
            }

            foreach (MethodInfo m in t.GetMethods(Declared))
            {
                if (!m.IsPublic || m.IsSpecialName) continue;
                foreach (Type leaf in Leaves(m.ReturnType)) yield return (m.Name + "()", leaf);
                foreach (ParameterInfo parameter in m.GetParameters())
                    foreach (Type leaf in Leaves(parameter.ParameterType)) yield return (m.Name + "(" + parameter.Name + ")", leaf);
            }
        }

        /// <summary>Unwraps by-ref, arrays and generic arguments into the leaf types.</summary>
        private static IEnumerable<Type> Leaves(Type type)
        {
            if (type == null) yield break;
            if (type.IsByRef || type.IsArray || type.IsPointer)
            {
                foreach (Type inner in Leaves(type.GetElementType())) yield return inner;
                yield break;
            }
            if (type.IsGenericType)
            {
                foreach (Type arg in type.GetGenericArguments())
                    foreach (Type inner in Leaves(arg)) yield return inner;
                yield break;
            }
            yield return type;
        }

        /// <summary>First forbidden segment (after "Game") of a Game.* namespace, or null.</summary>
        internal static string ForbiddenSegmentOf(string ns)
        {
            if (!IsGameNamespace(ns)) return null;
            string[] segments = ns.Split('.');
            for (int i = 1; i < segments.Length; i++)
                if (ForbiddenSegments.Contains(segments[i])) return segments[i];
            return null;
        }

        /// <summary>Names of the forbidden tokens found in (already stripped) code.</summary>
        internal static IEnumerable<string> FindForbiddenTokens(string code)
        {
            foreach ((string name, Regex pattern) in CoreForbiddenTokens)
                if (pattern.IsMatch(code)) yield return name;
        }

        /// <summary>Replaces comments, string literals and char literals with spaces (keeps line structure).</summary>
        internal static string StripCommentsAndStrings(string code)
        {
            var sb = new StringBuilder(code.Length);
            int i = 0;
            while (i < code.Length)
            {
                char c = code[i];
                char next = i + 1 < code.Length ? code[i + 1] : '\0';

                if (c == '/' && next == '/')
                {
                    while (i < code.Length && code[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    i += 2;
                    while (i < code.Length && !(code[i] == '*' && i + 1 < code.Length && code[i + 1] == '/'))
                    {
                        if (code[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i += 2;
                    continue;
                }
                if (c == '@' && next == '"')
                {
                    i += 2;
                    while (i < code.Length)
                    {
                        if (code[i] == '"' && i + 1 < code.Length && code[i + 1] == '"') { i += 2; continue; }
                        if (code[i] == '"') { i++; break; }
                        i++;
                    }
                    sb.Append("\"\"");
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < code.Length && code[i] != quote && code[i] != '\n')
                    {
                        if (code[i] == '\\') i++;
                        i++;
                    }
                    i++;
                    sb.Append(quote).Append(quote);
                    continue;
                }

                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- reflection helpers

        private static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            foreach (Type t in types)
                if (!IsCompilerGenerated(t)) yield return t;
        }

        private static bool IsCompilerGenerated(MemberInfo m) =>
            m.IsDefined(typeof(CompilerGeneratedAttribute), false) || m.Name.IndexOf('<') >= 0;

        private static string SimpleName(Type t)
        {
            int tick = t.Name.IndexOf('`');
            return tick < 0 ? t.Name : t.Name.Substring(0, tick);
        }

        private static void AssertNone(List<string> violations, string what)
        {
            if (violations.Count == 0) return;
            Assert.Fail($"{violations.Count} {what}:\n  " + string.Join("\n  ", violations.Distinct()));
        }
    }
}

// ---------------------------------------------------------------- fixtures for the rule self-tests
// Test-only types in two fake "modules". They are never added to GameObjects; only reflected over.

namespace Game.ArchFixtureB
{
    /// <summary>Fixture: a MonoBehaviour of module "ArchFixtureB".</summary>
    internal sealed class ProbeB : MonoBehaviour { }
}

namespace Game.ArchFixtureA
{
    /// <summary>Fixture: a MonoBehaviour of module "ArchFixtureA".</summary>
    internal sealed class ProbeA : MonoBehaviour { }

    /// <summary>Fixture interface (references through interfaces are allowed).</summary>
    internal interface IProbe { }

    /// <summary>Fixture with exactly five cross-module references (field, array, list, property, method parameter).</summary>
    internal sealed class Offender : MonoBehaviour
    {
#pragma warning disable 0649, 0414
        [SerializeField] private Game.ArchFixtureB.ProbeB _single;
        [SerializeField] private Game.ArchFixtureB.ProbeB[] _array;
        [SerializeField] private List<Game.ArchFixtureB.ProbeB> _list;
        private Game.ArchFixtureB.ProbeB _privateNotSerialized; // not serialized, not public: allowed
#pragma warning restore 0649, 0414

        public Game.ArchFixtureB.ProbeB Exposed => _single;

        public void Accept(Game.ArchFixtureB.ProbeB probe) { _privateNotSerialized = probe; }
    }

    /// <summary>Fixture with only allowed references: same module, interface and engine types.</summary>
    internal sealed class WellBehaved : MonoBehaviour
    {
#pragma warning disable 0649
        [SerializeField] private ProbeA _sameModule;
        [SerializeField] private MonoBehaviour _anyBehaviour;
        [SerializeField] private Transform _engine;
#pragma warning restore 0649

        public IProbe Probe => null;

        public ProbeA Same => _sameModule;
    }

    /// <summary>Fixture evaluated with the UI rule: one gameplay MonoBehaviour and one engine MonoBehaviour.</summary>
    internal sealed class UiLikeOffender : MonoBehaviour
    {
#pragma warning disable 0649
        [SerializeField] private Game.ArchFixtureB.ProbeB _gameplay;
        [SerializeField] private UnityEngine.UIElements.UIDocument _document;
#pragma warning restore 0649
    }

    /// <summary>Fixture with a static event and a static Instance.</summary>
    internal sealed class SingletonLike
    {
#pragma warning disable 0067
        public static event Action Happened;
#pragma warning restore 0067

        public static SingletonLike Instance { get; private set; }
    }
}
