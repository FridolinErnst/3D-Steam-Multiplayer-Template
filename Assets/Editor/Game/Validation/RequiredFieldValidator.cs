using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Atoms;
using UnityAtoms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Finds every <see cref="RequiredAttribute"/> field that is empty on components in prefabs and scenes.
    /// "Empty" means: a null UnityEngine.Object, an Atom Reference whose usage is Constant/Variable/Instancer with
    /// nothing assigned (Value usage is always filled), an Atom Event Reference whose selected slot is unassigned, or a
    /// null/empty array or list. Menu <c>Game/Validate/Required Fields</c>; used by <see cref="PlayModeRequiredGuard"/>.
    /// </summary>
    public static class RequiredFieldValidator
    {
        /// <summary>One empty required field and the object to select for it.</summary>
        public readonly struct Issue
        {
            /// <summary>Human-readable description including where the field is.</summary>
            public readonly string Message;
            /// <summary>The component that owns the field (null for prefab scans after unloading).</summary>
            public readonly Object Context;
            /// <summary>Name of the empty field (null for issues that are not about a field).</summary>
            public readonly string FieldName;

            public Issue(string message, Object context, string fieldName = null)
            {
                Message = message;
                Context = context;
                FieldName = fieldName;
            }

            public override string ToString() => Message;
        }

        private const BindingFlags InstanceFields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Required fields per component type (reflection is cached; scans run on every Play press).
        private static readonly Dictionary<Type, FieldInfo[]> RequiredFieldCache = new Dictionary<Type, FieldInfo[]>();

        [MenuItem("Game/Validate/Required Fields")]
        private static void Menu() => Debug.Log(Run());

        /// <summary>Scans prefabs and every enabled Build Settings scene; logs every issue and returns a summary.</summary>
        public static string Run()
        {
            List<Issue> issues = ScanProject();
            var log = new StringBuilder();
            log.Append("Required field validation: ").Append(issues.Count).Append(" empty required field(s)\n");
            foreach (Issue issue in issues)
            {
                log.Append("  ").Append(issue.Message).Append('\n');
                Debug.LogError("[RequiredFieldValidator] " + issue.Message, issue.Context);
            }
            return log.ToString();
        }

        /// <summary>Scans prefabs (<see cref="MissingReferenceValidator.PrefabFolders"/>) and enabled Build Settings scenes.</summary>
        public static List<Issue> ScanProject()
        {
            var issues = new List<Issue>();

            var folders = new List<string>();
            foreach (string f in MissingReferenceValidator.PrefabFolders)
                if (AssetDatabase.IsValidFolder(f)) folders.Add(f);
            if (folders.Count > 0) // FindAssets with no folders would search the whole project
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders.ToArray()))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null) ScanHierarchy(prefab, path, issues);
                }
            }

            foreach (string scenePath in MissingReferenceValidator.Scenes)
            {
                if (File.Exists(scenePath)) ScanScenePath(scenePath, issues);
            }
            return issues;
        }

        /// <summary>Scans only the scenes that are currently loaded (fast; used before entering Play Mode).</summary>
        public static List<Issue> ScanOpenScenes()
        {
            var issues = new List<Issue>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects()) ScanHierarchy(root, scene.path, issues);
            }
            return issues;
        }

        private static void ScanScenePath(string path, List<Issue> issues)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    issues.Add(new Issue($"{path}: not scanned (play mode)", null));
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

        /// <summary>Scans <paramref name="root"/> and all descendants (inactive included). Returns the number of issues added.</summary>
        public static int ScanHierarchy(GameObject root, string context, List<Issue> issues)
        {
            int before = issues.Count;
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue; // missing script: reported by MissingReferenceValidator
                CheckComponent(mb, $"{context}: '{PathOf(mb.gameObject)}'", issues);
            }
            return issues.Count - before;
        }

        /// <summary>Adds one issue per empty <see cref="RequiredAttribute"/> field of <paramref name="target"/>.</summary>
        public static void CheckComponent(Object target, string context, List<Issue> issues)
        {
            foreach (FieldInfo field in RequiredFieldsOf(target.GetType()))
            {
                if (IsEmpty(field.GetValue(target)))
                    issues.Add(new Issue($"{context} {target.GetType().Name}.{field.Name} is required but empty", target, field.Name));
            }
        }

        /// <summary>True when a required field value counts as not filled in (see class summary).</summary>
        public static bool IsEmpty(object value)
        {
            switch (value)
            {
                case null:
                    return true;
                case Object unityObject:
                    return unityObject == null; // Unity's null check (destroyed / missing objects)
                case AtomBaseReference reference:
                    return IsUnassignedReference(reference);
                case AtomBaseEventReference eventReference:
                    return IsUnassignedEventReference(eventReference);
                case string _:
                    return false; // strings are value-like; use a non-empty check in the component if needed
                case ICollection collection:
                    return collection.Count == 0;
                default:
                    return false;
            }
        }

        private static bool IsUnassignedReference(AtomBaseReference reference)
        {
            // AtomReference<...>.IsUnassigned handles Constant / Variable / Variable Instancer usages.
            PropertyInfo unassigned = reference.GetType().GetProperty("IsUnassigned", BindingFlags.Instance | BindingFlags.Public);
            return unassigned != null && (bool)unassigned.GetValue(reference);
        }

        private static bool IsUnassignedEventReference(AtomBaseEventReference reference)
        {
            int usage = (int)(FindField(reference.GetType(), "_usage")?.GetValue(reference) ?? 0);
            string slot;
            switch (usage)
            {
                case AtomEventReferenceUsage.EVENT: slot = "_event"; break;
                case AtomEventReferenceUsage.EVENT_INSTANCER: slot = "_eventInstancer"; break;
                case AtomEventReferenceUsage.VARIABLE: slot = "_variable"; break;
                case AtomEventReferenceUsage.VARIABLE_INSTANCER: slot = "_variableInstancer"; break;
                default: return true;
            }
            object assigned = FindField(reference.GetType(), slot)?.GetValue(reference);
            return assigned == null || (assigned is Object o && o == null);
        }

        private static FieldInfo[] RequiredFieldsOf(Type type)
        {
            if (RequiredFieldCache.TryGetValue(type, out FieldInfo[] cached)) return cached;

            var fields = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(InstanceFields))
                    if (f.IsDefined(typeof(RequiredAttribute), true)) fields.Add(f);
            }
            FieldInfo[] result = fields.ToArray();
            RequiredFieldCache[type] = result;
            return result;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, InstanceFields);
                if (f != null) return f;
            }
            return null;
        }

        private static string PathOf(GameObject go)
        {
            string path = go.name;
            for (Transform t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return path;
        }
    }
}
