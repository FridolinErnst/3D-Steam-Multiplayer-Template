using System;
using System.Collections.Generic;
using System.Reflection;
using UnityAtoms;
using UnityAtoms.BaseAtoms;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Support.Tests
{
    /// <summary>
    /// Creates in-memory Atom ScriptableObjects for tests (never project assets): events get
    /// <c>ReplayBufferSize = 0</c>, lists get Added/Removed events, variables get an initial value. Everything created
    /// is tracked and destroyed by <see cref="DestroyAll"/> (call it in TearDown).
    /// </summary>
    public sealed class AtomFactory : IDisposable
    {
        private readonly List<Object> _created = new List<Object>();

        /// <summary>Number of objects currently tracked.</summary>
        public int Count => _created.Count;

        /// <summary>Creates a tracked instance of <typeparamref name="T"/>; Atom events get replay buffer 0.</summary>
        public T New<T>() where T : ScriptableObject
        {
            var instance = ScriptableObject.CreateInstance<T>();
            instance.name = typeof(T).Name;
            if (instance is AtomEventBase evt) DisableReplay(evt);
            _created.Add(instance);
            return instance;
        }

        /// <summary>Creates a tracked instance of a type known only at runtime (e.g. a generated atom type).</summary>
        public ScriptableObject New(Type type)
        {
            ScriptableObject instance = ScriptableObject.CreateInstance(type);
            instance.name = type.Name;
            if (instance is AtomEventBase evt) DisableReplay(evt);
            _created.Add(instance);
            return instance;
        }

        /// <summary>A GameObjectValueList with tracked Added/Removed events (replay 0).</summary>
        public GameObjectValueList NewList()
        {
            GameObjectValueList list = New<GameObjectValueList>();
            list.Added = New<GameObjectEvent>();
            list.Removed = New<GameObjectEvent>();
            return list;
        }

        /// <summary>A FloatVariable whose InitialValue (what EditMode reads return) is <paramref name="initial"/>.</summary>
        public FloatVariable Float(float initial = 0f)
        {
            FloatVariable v = New<FloatVariable>();
            v.InitialValue = initial;
            v.Value = initial;
            return v;
        }

        /// <summary>An IntVariable with the given initial value.</summary>
        public IntVariable Int(int initial = 0)
        {
            IntVariable v = New<IntVariable>();
            v.InitialValue = initial;
            v.Value = initial;
            return v;
        }

        /// <summary>A BoolVariable with the given initial value.</summary>
        public BoolVariable Bool(bool initial = false)
        {
            BoolVariable v = New<BoolVariable>();
            v.InitialValue = initial;
            v.Value = initial;
            return v;
        }

        /// <summary>A StringVariable with the given initial value.</summary>
        public StringVariable String(string initial = "")
        {
            StringVariable v = New<StringVariable>();
            v.InitialValue = initial;
            v.Value = initial;
            return v;
        }

        /// <summary>Registers an externally created object for destruction.</summary>
        public T Track<T>(T obj) where T : Object
        {
            if (obj != null) _created.Add(obj);
            return obj;
        }

        /// <summary>Destroys everything this factory created or tracked.</summary>
        public void DestroyAll()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        /// <summary>Same as <see cref="DestroyAll"/>.</summary>
        public void Dispose() => DestroyAll();

        /// <summary>Sets ReplayBufferSize = 0 on any AtomEvent&lt;T&gt; (works for generated event types too).</summary>
        public static void DisableReplay(AtomEventBase evt)
        {
            if (evt == null) return;
            PropertyInfo p = evt.GetType().GetProperty("ReplayBufferSize", BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) p.SetValue(evt, 0);
        }

        /// <summary>Reads ReplayBufferSize of any AtomEvent&lt;T&gt;; -1 if the event type has none.</summary>
        public static int ReplayOf(AtomEventBase evt)
        {
            if (evt == null) return -1;
            PropertyInfo p = evt.GetType().GetProperty("ReplayBufferSize", BindingFlags.Public | BindingFlags.Instance);
            return p == null ? -1 : (int)p.GetValue(evt);
        }
    }
}
