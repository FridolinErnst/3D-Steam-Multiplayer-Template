using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Reports missing scripts and unresolved persistent UnityEvent calls (null target, or no method of that name on the
    /// target) in every prefab under <c>Assets/Prefabs</c> and the gameplay scenes. Read-only: scenes that were not
    /// open are opened additively and closed again without saving. Menu <c>Game/Validate/Missing References</c>.
    /// </summary>
    public static class MissingReferenceValidator
    {
        /// <summary>Prefab root folders that are scanned (missing folders are skipped).</summary>
        public static readonly string[] PrefabFolders = { "Assets/_Project/Prefabs", "Assets/Prefabs" };

        /// <summary>Scenes that are scanned: every enabled Build Settings scene (missing files are skipped).</summary>
        public static IEnumerable<string> Scenes
        {
            get
            {
                foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                    if (s.enabled && !string.IsNullOrEmpty(s.path)) yield return s.path;
            }
        }

        private const BindingFlags AnyMethod =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Array element types that can never contain a UnityEvent (skipped to keep big serialized arrays cheap).
        private static readonly HashSet<string> LeafElementTypes = new HashSet<string>
        {
            "int", "uint", "float", "double", "bool", "char", "string", "long", "ulong", "short", "ushort", "SInt8", "UInt8",
            "SInt16", "UInt16", "SInt32", "UInt32", "SInt64", "UInt64", "Vector2", "Vector3", "Vector4", "Vector2f", "Vector3f",
            "Vector4f", "Quaternionf", "ColorRGBA", "Color", "Matrix4x4f", "Rectf", "AABB", "Vector2Int", "Vector3Int",
            "int2_storage", "int3_storage", "BitField", "Keyframe", "GradientNEW",
        };

        [MenuItem("Game/Validate/Missing References")]
        private static void Menu() => Debug.Log(Run());

        /// <summary>Scans prefabs and scenes; logs every issue and returns a summary log.</summary>
        public static string Run()
        {
            var issues = new List<string>();
            ScanAll(issues);

            var log = new StringBuilder();
            log.Append("Missing reference validation: ").Append(issues.Count).Append(" issue(s)\n");
            foreach (string issue in issues)
            {
                log.Append("  ").Append(issue).Append('\n');
                Debug.LogWarning("[MissingReferenceValidator] " + issue);
            }
            return log.ToString();
        }

        /// <summary>Number of issues found by a full scan (no logging).</summary>
        public static int CountIssues()
        {
            var issues = new List<string>();
            ScanAll(issues);
            return issues.Count;
        }

        private static void ScanAll(List<string> issues)
        {
            string[] prefabFolders = ExistingFolders(PrefabFolders);
            if (prefabFolders.Length > 0) // FindAssets with no folders would search the whole project
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", prefabFolders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) ScanHierarchy(prefab, path, issues);
            }

            foreach (string scenePath in Scenes)
            {
                if (!File.Exists(scenePath)) continue;
                ScanScene(scenePath, issues);
            }
        }

        private static string[] ExistingFolders(string[] folders)
        {
            var existing = new List<string>();
            foreach (string f in folders)
                if (AssetDatabase.IsValidFolder(f)) existing.Add(f);
            return existing.ToArray();
        }

        private static void ScanScene(string path, List<string> issues)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    issues.Add($"{path}: not scanned (play mode)");
                    return;
                }
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }

            try
            {
                foreach (GameObject root in scene.GetRootGameObjects()) ScanHierarchy(root, path, issues);
            }
            finally
            {
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Scans <paramref name="root"/> and all descendants (inactive included). Appends one entry per missing script
        /// and per unresolved persistent call. Returns the number of issues added.
        /// </summary>
        internal static int ScanHierarchy(GameObject root, string context, List<string> issues)
        {
            int before = issues.Count;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missing > 0) issues.Add($"{context}: '{PathOf(go)}' has {missing} missing script(s)");

                foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue; // counted above as missing script
                    ScanUnityEvents(mb, $"{context}: '{PathOf(go)}' {mb.GetType().Name}", issues);
                }
            }
            return issues.Count - before;
        }

        /// <summary>Checks every persistent call of every UnityEvent serialized on <paramref name="target"/>.</summary>
        internal static void ScanUnityEvents(Object target, string context, List<string> issues)
        {
            var so = new SerializedObject(target);
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = ShouldEnter(it);
                if (!it.isArray || !it.propertyPath.EndsWith("m_PersistentCalls.m_Calls", StringComparison.Ordinal)) continue;

                for (int i = 0; i < it.arraySize; i++)
                {
                    SerializedProperty call = it.GetArrayElementAtIndex(i);
                    string method = call.FindPropertyRelative("m_MethodName")?.stringValue;
                    SerializedProperty targetProp = call.FindPropertyRelative("m_Target");
                    Object callTarget = targetProp?.objectReferenceValue;
                    string eventPath = it.propertyPath.Substring(0, it.propertyPath.Length - "m_PersistentCalls.m_Calls".Length).TrimEnd('.');

                    if (callTarget == null)
                    {
                        bool brokenLink = targetProp != null && targetProp.objectReferenceInstanceIDValue != 0;
                        if (!string.IsNullOrEmpty(method) || brokenLink)
                            issues.Add($"{context}.{eventPath}[{i}]: target is {(brokenLink ? "missing" : "unassigned")} (method '{method}')");
                        continue;
                    }

                    if (string.IsNullOrEmpty(method))
                    {
                        issues.Add($"{context}.{eventPath}[{i}]: no method selected on '{callTarget.name}'");
                        continue;
                    }

                    if (!HasMethod(callTarget.GetType(), method))
                        issues.Add($"{context}.{eventPath}[{i}]: method '{method}' not found on {callTarget.GetType().Name}");
                }
                enter = false; // the calls array itself was handled
            }
        }

        /// <summary>True if <paramref name="type"/> or a base type declares a method called <paramref name="name"/>.</summary>
        internal static bool HasMethod(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(AnyMethod))
                    if (m.Name == name) return true;
            }
            return false;
        }

        private static bool ShouldEnter(SerializedProperty p)
        {
            if (p.propertyType == SerializedPropertyType.String) return false;
            if (p.isArray)
            {
                string element = p.arrayElementType;
                if (string.IsNullOrEmpty(element)) return true;
                if (element.StartsWith("PPtr<", StringComparison.Ordinal)) return false;
                return !LeafElementTypes.Contains(element);
            }
            return p.propertyType == SerializedPropertyType.Generic || p.propertyType == SerializedPropertyType.ManagedReference;
        }

        private static string PathOf(GameObject go)
        {
            string path = go.name;
            for (Transform t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return path;
        }
    }
}
