using System;
using System.Collections;
using System.IO;
using UnityAtoms;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Shared, idempotent helpers for the editor setup scripts: load-or-create assets, folders, prefab edits,
    /// SerializedObject writes (plain fields and Atom References / Event References), persistent-listener removal,
    /// input action and layer lookups. Every helper is safe to call repeatedly.
    /// </summary>
    public static class SetupUtil
    {
        /// <summary>Default input actions asset of the project (Player map).</summary>
        public const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        // ---------------------------------------------------------------- assets & folders

        /// <summary>Creates every missing folder of an <c>Assets/...</c> path (forward slashes).</summary>
        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>Loads the asset of type <typeparamref name="T"/> at <paramref name="path"/> or creates it.</summary>
        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            return LoadOrCreate<T>(path, out _);
        }

        /// <summary>Loads the asset at <paramref name="path"/> or creates it; <paramref name="created"/> tells which.</summary>
        public static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            return (T)LoadOrCreate(typeof(T), path, out created);
        }

        /// <summary>
        /// Non-generic load-or-create (used for types that are only known at runtime, e.g. generated atoms).
        /// Returns null and logs an error when an asset of a different type already occupies the path.
        /// </summary>
        public static ScriptableObject LoadOrCreate(Type type, string path, out bool created)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
                throw new ArgumentException($"{type.Name} is not a ScriptableObject.", nameof(type));

            created = false;
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                if (type.IsInstanceOfType(existing)) return (ScriptableObject)existing;
                Debug.LogError($"{nameof(SetupUtil)}: '{path}' exists but is a {existing.GetType().Name}, expected {type.Name}. Left untouched.");
                return null;
            }

            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            ScriptableObject asset = ScriptableObject.CreateInstance(type);
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
        }

        /// <summary>Saves all dirty assets and refreshes the database.</summary>
        public static void SaveAndRefresh()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ---------------------------------------------------------------- prefabs & GameObjects

        /// <summary>
        /// Opens the prefab at <paramref name="path"/> in isolation, runs <paramref name="edit"/> and saves only if it
        /// returned true. Always unloads the contents. Returns whether the prefab was saved.
        /// </summary>
        public static bool UpdatePrefab(string path, Func<GameObject, bool> edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogError($"{nameof(SetupUtil)}: prefab '{path}' not found.");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!edit(root)) return false;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Returns the existing component of type <typeparamref name="T"/> or adds one.</summary>
        public static T AddOnce<T>(GameObject go) where T : Component
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            return go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();
        }

        /// <summary>Returns the direct child called <paramref name="name"/>, creating it (at local origin) if missing.</summary>
        public static GameObject Child(GameObject root, string name)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            Transform t = root.transform.Find(name);
            if (t != null) return t.gameObject;

            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            child.layer = root.layer;
            return child;
        }

        // ---------------------------------------------------------------- serialized writes

        /// <summary>
        /// Writes <paramref name="value"/> into the serialized field <paramref name="field"/> (a property path) of
        /// <paramref name="target"/> through SerializedObject. Supports object references, primitives, enums, strings,
        /// LayerMask, vectors, colors, arrays/lists (element-wise) and serializable structs (via boxedValue).
        /// </summary>
        public static void SetRef(Object target, string field, object value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var so = new SerializedObject(target);
            SerializedProperty prop = FindOrThrow(so, target, field);
            WriteProperty(prop, value);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        /// <summary>Sets an Atom Reference field to VALUE usage with the given literal.</summary>
        public static void SetValueRef(Object target, string field, object value)
        {
            SetReference(target, field, AtomReferenceUsage.VALUE, "_value", value);
        }

        /// <summary>Sets an Atom Reference field to CONSTANT usage pointing at <paramref name="constant"/>.</summary>
        public static void SetConstantRef(Object target, string field, AtomBaseVariable constant)
        {
            SetReference(target, field, AtomReferenceUsage.CONSTANT, "_constant", constant);
        }

        /// <summary>Sets an Atom Reference field to VARIABLE usage pointing at <paramref name="variable"/>.</summary>
        public static void SetVariableRef(Object target, string field, AtomBaseVariable variable)
        {
            SetReference(target, field, AtomReferenceUsage.VARIABLE, "_variable", variable);
        }

        /// <summary>Sets an Atom Reference field to VARIABLE_INSTANCER usage pointing at <paramref name="instancer"/>.</summary>
        public static void SetInstancerRef(Object target, string field, Component instancer)
        {
            SetReference(target, field, AtomReferenceUsage.VARIABLE_INSTANCER, "_variableInstancer", instancer);
        }

        /// <summary>Sets an Atom Event Reference field to EVENT usage pointing at <paramref name="atomEvent"/>.</summary>
        public static void SetEventRef(Object target, string field, AtomEventBase atomEvent)
        {
            SetReference(target, field, AtomEventReferenceUsage.EVENT, "_event", atomEvent);
        }

        /// <summary>Sets the replay buffer size of any <c>AtomEvent&lt;T&gt;</c> (generated types included).</summary>
        public static void SetReplayBufferSize(AtomEventBase atomEvent, int size)
        {
            if (atomEvent == null) return;
            var so = new SerializedObject(atomEvent);
            SerializedProperty p = so.FindProperty("_replayBufferSize");
            if (p == null) return; // e.g. a non-generic event type without a buffer
            if (p.intValue == size) return;
            p.intValue = size;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(atomEvent);
        }

        /// <summary>Reads the replay buffer size of an <c>AtomEvent&lt;T&gt;</c>; -1 if the event has none.</summary>
        public static int GetReplayBufferSize(AtomEventBase atomEvent)
        {
            if (atomEvent == null) return -1;
            SerializedProperty p = new SerializedObject(atomEvent).FindProperty("_replayBufferSize");
            return p == null ? -1 : p.intValue;
        }

        /// <summary>
        /// Removes every persistent listener of <paramref name="unityEvent"/> whose method name equals
        /// <paramref name="methodName"/>. The caller marks the owning object dirty. Returns the number removed.
        /// </summary>
        public static int RemovePersistentListeners(UnityEventBase unityEvent, string methodName)
        {
            if (unityEvent == null || string.IsNullOrEmpty(methodName)) return 0;
            int removed = 0;
            for (int i = unityEvent.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                if (unityEvent.GetPersistentMethodName(i) != methodName) continue;
                UnityEventTools.RemovePersistentListener(unityEvent, i);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Serialized variant: removes persistent calls named <paramref name="methodName"/> from the UnityEvent field
        /// <paramref name="eventField"/> of <paramref name="owner"/>. Returns the number removed.
        /// </summary>
        public static int RemovePersistentListeners(Object owner, string eventField, string methodName)
        {
            if (owner == null || string.IsNullOrEmpty(methodName)) return 0;
            var so = new SerializedObject(owner);
            SerializedProperty calls = so.FindProperty(eventField + ".m_PersistentCalls.m_Calls");
            if (calls == null || !calls.isArray) return 0;

            int removed = 0;
            for (int i = calls.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty m = calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_MethodName");
                if (m == null || m.stringValue != methodName) continue;
                calls.DeleteArrayElementAtIndex(i);
                removed++;
            }

            if (removed > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(owner);
            }
            return removed;
        }

        // ---------------------------------------------------------------- input & layers

        /// <summary>
        /// Returns the <see cref="UnityEngine.InputSystem.InputActionReference"/> sub-asset for <c>"Map/Action"</c>
        /// (e.g. <c>"Player/Click"</c>) in <see cref="InputActionsPath"/>, or null if it does not exist.
        /// </summary>
        public static InputActionReference LoadInputAction(string mapSlashAction)
        {
            return LoadInputAction(InputActionsPath, mapSlashAction);
        }

        /// <summary>As <see cref="LoadInputAction(string)"/> for an explicit .inputactions asset path.</summary>
        public static InputActionReference LoadInputAction(string assetPath, string mapSlashAction)
        {
            if (string.IsNullOrEmpty(mapSlashAction)) return null;
            int slash = mapSlashAction.IndexOf('/');
            if (slash <= 0) return null;
            string map = mapSlashAction.Substring(0, slash);
            string action = mapSlashAction.Substring(slash + 1);

            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (!(o is InputActionReference r) || r.action == null || r.action.actionMap == null) continue;
                if (r.action.actionMap.name == map && r.action.name == action) return r;
            }
            return null;
        }

        /// <summary>LayerMask of the named layers; unknown names are logged and ignored.</summary>
        public static LayerMask LayerMaskOf(params string[] layerNames)
        {
            int mask = 0;
            if (layerNames == null) return mask;
            foreach (string n in layerNames)
            {
                int layer = LayerMask.NameToLayer(n);
                if (layer < 0)
                {
                    Debug.LogWarning($"{nameof(SetupUtil)}: layer '{n}' does not exist.");
                    continue;
                }
                mask |= 1 << layer;
            }
            return mask;
        }

        // ---------------------------------------------------------------- internals (shared with tests)

        private static void SetReference(Object target, string field, int usage, string slot, object value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var so = new SerializedObject(target);
            SerializedProperty usageProp = FindOrThrow(so, target, field + "._usage");
            SerializedProperty slotProp = FindOrThrow(so, target, field + "." + slot);
            usageProp.intValue = usage;
            WriteProperty(slotProp, value);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        internal static SerializedProperty FindOrThrow(SerializedObject so, Object target, string path)
        {
            SerializedProperty prop = so.FindProperty(path);
            if (prop == null)
                throw new ArgumentException($"Serialized property '{path}' not found on {target.GetType().Name} '{target.name}'.");
            return prop;
        }

        /// <summary>Writes a boxed value into a SerializedProperty according to its property type.</summary>
        internal static void WriteProperty(SerializedProperty prop, object value)
        {
            if (prop.propertyType == SerializedPropertyType.ObjectReference)
            {
                if (value != null && !(value is Object))
                    throw new ArgumentException($"'{prop.propertyPath}' is an object reference; got a {value.GetType().Name}.");
                prop.objectReferenceValue = value as Object;
                return;
            }

            if (value == null)
            {
                if (prop.propertyType == SerializedPropertyType.String) prop.stringValue = null;
                else if (prop.propertyType == SerializedPropertyType.ManagedReference) prop.managedReferenceValue = null;
                else if (prop.isArray) prop.arraySize = 0;
                else throw new ArgumentException($"Cannot write null into '{prop.propertyPath}' ({prop.propertyType}).");
                return;
            }

            if (prop.isArray && prop.propertyType != SerializedPropertyType.String && value is IList list)
            {
                prop.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++) WriteProperty(prop.GetArrayElementAtIndex(i), list[i]);
                return;
            }

            switch (value)
            {
                case LayerMask mask: prop.intValue = mask.value; return;
                case Enum e: prop.intValue = Convert.ToInt32(e); return;
                case bool b: prop.boolValue = b; return;
                case int i:
                    if (prop.propertyType == SerializedPropertyType.Float) prop.floatValue = i;
                    else prop.intValue = i;
                    return;
                case long l: prop.longValue = l; return;
                case float f: prop.floatValue = f; return;
                case double d: prop.doubleValue = d; return;
                case string s: prop.stringValue = s; return;
                case Vector2 v2: prop.vector2Value = v2; return;
                case Vector3 v3: prop.vector3Value = v3; return;
                case Vector4 v4: prop.vector4Value = v4; return;
                case Quaternion q: prop.quaternionValue = q; return;
                case Color c: prop.colorValue = c; return;
                case Vector2Int vi2: prop.vector2IntValue = vi2; return;
                case Vector3Int vi3: prop.vector3IntValue = vi3; return;
                case Bounds bo: prop.boundsValue = bo; return;
                case Rect r: prop.rectValue = r; return;
                case AnimationCurve ac: prop.animationCurveValue = ac; return;
                case Gradient g: prop.gradientValue = g; return;
            }

            // Serializable structs / classes (config structs etc.).
            prop.boxedValue = value;
        }
    }
}
