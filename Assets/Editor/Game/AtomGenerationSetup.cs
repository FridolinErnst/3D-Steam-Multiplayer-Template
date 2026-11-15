using System;
using System.Text;
using UnityAtoms.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Step 00: creates the Unity Atoms generator assets for the custom atom value types listed in <see cref="TypeNames"/>
    /// (structs in <c>Game.Contracts</c>, Game.Runtime) and generates all 22 atom kinds for each into
    /// <c>Assets/_Project/Scripts/Atoms/Generated/&lt;T&gt;/Generated/</c>. Idempotent: generation is skipped for a type
    /// whose generated event type already compiles (use the "Regenerate" menu to force).
    /// </summary>
    [GameSetupStep(0)]
    public static class AtomGenerationSetup
    {
        /// <summary>Root folder of the generator assets.</summary>
        public const string Root = "Assets/_Project/Scripts/Atoms/Generated";

        /// <summary>All 22 atom kinds (bit i = AtomTypes.ALL_ATOM_TYPES[i]).</summary>
        public const int AllAtomKinds = 4194303;

        /// <summary>The custom value types to generate, by simple name (namespace Game.Contracts, Game.Runtime).</summary>
        public static readonly string[] TypeNames = { }; // add e.g. "DamageInfo" after creating Game.Contracts.DamageInfo

        /// <summary>True when the last <see cref="Run"/> wrote new code (a recompile is required before step 02).</summary>
        public static bool LastRunGenerated { get; private set; }

        [MenuItem("Game/Setup/00 Generate Custom Atoms")]
        private static void Menu() => Debug.Log(Run());

        [MenuItem("Game/Setup/Regenerate Custom Atoms (force)")]
        private static void ForceMenu() => Debug.Log(Run(force: true));

        /// <summary>Creates/updates the generator assets and generates missing atom code; returns a log.</summary>
        public static string Run() => Run(force: false);

        /// <summary>As <see cref="Run()"/>; <paramref name="force"/> regenerates even if the types already compile.</summary>
        public static string Run(bool force)
        {
            LastRunGenerated = false;
            var log = new StringBuilder();

            foreach (string typeName in TypeNames)
            {
                string valueTypeName = ValueTypeQualifiedName(typeName);
                if (Type.GetType(valueTypeName) == null)
                {
                    log.Append("ERROR ").Append(valueTypeName).Append(" not found; is Game.Contracts (Game.Runtime) compiled?\n");
                    continue;
                }

                string path = GeneratorAssetPath(typeName);
                AtomGenerator generator = SetupUtil.LoadOrCreate<AtomGenerator>(path, out bool created);
                if (generator == null)
                {
                    log.Append("ERROR could not load or create ").Append(path).Append('\n');
                    continue;
                }

                bool changed = created;
                if (generator.FullQualifiedName != valueTypeName)
                {
                    generator.FullQualifiedName = valueTypeName;
                    changed = true;
                }
                if (generator.GenerationOptions != AllAtomKinds)
                {
                    generator.GenerationOptions = AllAtomKinds;
                    changed = true;
                }

                bool alreadyGenerated = Type.GetType(GeneratedEventQualifiedName(typeName)) != null;
                if (force || !alreadyGenerated || changed)
                {
                    generator.Generate();
                    LastRunGenerated = true;
                    log.Append("generated ").Append(typeName).Append(" atoms (").Append(path).Append(")\n");
                }
                else
                {
                    log.Append("up to date ").Append(typeName).Append('\n');
                }

                EditorUtility.SetDirty(generator);
            }

            AssetDatabase.SaveAssets();
            if (LastRunGenerated) AssetDatabase.Refresh();
            return log.ToString();
        }

        /// <summary><c>Assets/_Project/Scripts/Atoms/Generated/&lt;T&gt;/&lt;T&gt;AtomGenerator.asset</c>.</summary>
        public static string GeneratorAssetPath(string typeName) => $"{Root}/{typeName}/{typeName}AtomGenerator.asset";

        /// <summary>Assembly-qualified name of the custom value type.</summary>
        public static string ValueTypeQualifiedName(string typeName) => $"Game.Contracts.{typeName}, Game.Runtime";

        /// <summary>Assembly-qualified name of the generated event type (e.g. DamageInfoEvent).</summary>
        public static string GeneratedEventQualifiedName(string typeName) => $"UnityAtoms.BaseAtoms.{typeName}Event, Game.Runtime";
    }
}
