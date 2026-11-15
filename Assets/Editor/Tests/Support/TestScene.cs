using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Game.Editor.Setup;
using UnityAtoms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Support.Tests
{
    /// <summary>
    /// Isolated EditMode test scene (an editor preview scene with its own physics scene) plus SerializedObject helpers
    /// for wiring private serialized fields, Atom References and Event References without making fields public.
    /// Dispose closes the scene (destroying everything in it).
    /// </summary>
    public sealed class TestScene : IDisposable
    {
        private readonly List<Object> _extra = new List<Object>();
        private readonly bool _physics;
        private Scene _scene;
        private bool _disposed;

        private TestScene(Scene scene, bool physics)
        {
            _scene = scene;
            _physics = physics;
        }

        /// <summary>The underlying preview scene.</summary>
        public Scene Scene => _scene;

        /// <summary>The scene's own physics scene; pass it to validators and raycasts.</summary>
        public PhysicsScene Physics => _scene.GetPhysicsScene();

        /// <summary>
        /// Creates a new preview scene. With <paramref name="physics"/> = true, transforms are synced to physics after
        /// every <see cref="Add{T}"/>/<see cref="Go"/> so raycasts against <see cref="Physics"/> see new colliders.
        /// (Fallback if the G0 canary fails: switch this to an additive EmptyScene + default physics scene.)
        /// </summary>
        public static TestScene New(bool physics = false)
        {
            return new TestScene(EditorSceneManager.NewPreviewScene(), physics);
        }

        /// <summary>Creates a GameObject in this scene (optionally positioned) and adds/returns component <typeparamref name="T"/>.</summary>
        public T Add<T>(string name, Vector3? pos = null) where T : Component
        {
            GameObject go = Go(name, pos);
            T component = go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();
            SyncIfPhysics();
            return component;
        }

        /// <summary>Creates an empty GameObject in this scene.</summary>
        public GameObject Go(string name)
        {
            return Go(name, null);
        }

        /// <summary>Creates an empty GameObject in this scene at <paramref name="pos"/> (world origin if null).</summary>
        public GameObject Go(string name, Vector3? pos)
        {
            ThrowIfDisposed();
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _scene);
            if (pos.HasValue) go.transform.position = pos.Value;
            SyncIfPhysics();
            return go;
        }

        /// <summary>Moves an existing GameObject (e.g. an instantiated prefab) into this scene.</summary>
        public GameObject Adopt(GameObject go)
        {
            ThrowIfDisposed();
            if (go.transform.parent != null) go.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(go, _scene);
            SyncIfPhysics();
            return go;
        }

        /// <summary>Registers an extra object (e.g. a ScriptableObject) to destroy on <see cref="Dispose"/>.</summary>
        public T Track<T>(T obj) where T : Object
        {
            if (obj != null) _extra.Add(obj);
            return obj;
        }

        /// <summary>Pushes transform changes to physics (call after moving objects in a physics test).</summary>
        public void SyncPhysics()
        {
            UnityEngine.Physics.SyncTransforms();
        }

        /// <summary>Closes the preview scene and destroys tracked objects and fixtures. Safe to call twice.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = _extra.Count - 1; i >= 0; i--)
                if (_extra[i] != null) Object.DestroyImmediate(_extra[i]);
            _extra.Clear();

            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
        }

        private void SyncIfPhysics()
        {
            if (_physics) SyncPhysics();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TestScene));
        }

        // ---------------------------------------------------------------- serialized helpers

        /// <summary>
        /// Writes a private serialized field via SerializedObject: object refs, ints, floats, bools, strings, enums,
        /// LayerMask, vectors, colors, arrays/lists and serializable structs (config structs). <paramref name="field"/>
        /// is a property path (nested paths like <c>"_config.Capacity"</c> work).
        /// </summary>
        public static void Set(Object target, string field, object value)
        {
            SetupUtil.SetRef(target, field, value);
        }

        /// <summary>Reads a private serialized field's boxed value (object refs, primitives, structs).</summary>
        public static object Get(Object target, string field)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = SetupUtil.FindOrThrow(so, target, field);
            return p.propertyType == SerializedPropertyType.ObjectReference ? p.objectReferenceValue : p.boxedValue;
        }

        /// <summary>Atom Reference in VALUE usage (<c>_usage = 0</c>, <c>_value</c>) — the EditMode-safe way to feed binders.</summary>
        public static void SetValueRef(Object target, string field, float value) => SetupUtil.SetValueRef(target, field, value);

        /// <summary>Atom Reference in VALUE usage.</summary>
        public static void SetValueRef(Object target, string field, int value) => SetupUtil.SetValueRef(target, field, value);

        /// <summary>Atom Reference in VALUE usage.</summary>
        public static void SetValueRef(Object target, string field, bool value) => SetupUtil.SetValueRef(target, field, value);

        /// <summary>Atom Reference in VALUE usage.</summary>
        public static void SetValueRef(Object target, string field, string value) => SetupUtil.SetValueRef(target, field, value);

        /// <summary>Atom Reference in VALUE usage.</summary>
        public static void SetValueRef(Object target, string field, Vector3 value) => SetupUtil.SetValueRef(target, field, value);

        /// <summary>
        /// Atom Reference in VARIABLE usage (<c>_usage = 2</c>, <c>_variable</c>). Remember: in EditMode
        /// <c>Variable.Value</c> returns InitialValue — assert via Changed handlers, not by reading the Variable.
        /// </summary>
        public static void SetVariableRef(Object target, string field, AtomBaseVariable variable) => SetupUtil.SetVariableRef(target, field, variable);

        /// <summary>Atom Reference in CONSTANT usage (<c>_usage = 1</c>, <c>_constant</c>).</summary>
        public static void SetConstantRef(Object target, string field, AtomBaseVariable constant) => SetupUtil.SetConstantRef(target, field, constant);

        /// <summary>Atom Reference in VARIABLE_INSTANCER usage (<c>_usage = 3</c>, <c>_variableInstancer</c>).</summary>
        public static void SetInstancerRef(Object target, string field, Component instancer) => SetupUtil.SetInstancerRef(target, field, instancer);

        /// <summary>Atom Event Reference in EVENT usage (<c>_usage = 0</c>, <c>_event</c>).</summary>
        public static void SetEventRef(Object target, string field, AtomEventBase atomEvent) => SetupUtil.SetEventRef(target, field, atomEvent);

        /// <summary>
        /// Invokes a (private) instance or static method by name, e.g. <c>CallPrivate(c, "OnEnable")</c>. Overloads are
        /// matched by argument count and assignability. Exceptions thrown by the method are rethrown unwrapped.
        /// </summary>
        public static object CallPrivate(object o, string method, params object[] args)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            args = args ?? new object[0];
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(flags))
                {
                    if (m.Name != method || !ArgumentsMatch(m.GetParameters(), args)) continue;
                    try
                    {
                        return m.Invoke(m.IsStatic ? null : o, args);
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException != null)
                    {
                        ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                        throw;
                    }
                }
            }
            throw new MissingMethodException(o.GetType().Name, method);
        }

        private static bool ArgumentsMatch(ParameterInfo[] parameters, object[] args)
        {
            if (parameters.Length != args.Length) return false;
            for (int i = 0; i < parameters.Length; i++)
            {
                Type p = parameters[i].ParameterType;
                if (p.IsByRef) p = p.GetElementType();
                if (args[i] == null)
                {
                    if (p.IsValueType && Nullable.GetUnderlyingType(p) == null) return false;
                }
                else if (!p.IsInstanceOfType(args[i])) return false;
            }
            return true;
        }
    }
}
