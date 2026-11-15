# AGENTS.md — Unity C# with Unity Atoms

This file tells coding agents (Claude Code, Copilot, Cursor, etc.) how to write and modify code in this Unity project. The project follows the **Scriptable Object Architecture** as implemented by [Unity Atoms](https://github.com/unity-atoms/unity-atoms) (inspired by Ryan Hipple's Unite 2017 talk). Read this whole file before making changes.

---

## 1. Project facts

- **Engine:** Unity 2022.2+ (Unity Atoms minimum requirement)
- **Language:** C# (Unity's supported language version; no features beyond what the editor compiles)
- **Architecture library:** Unity Atoms v4.x
  - Required: `com.unity-atoms.unity-atoms-core`, `com.unity-atoms.unity-atoms-base-atoms`
  - Optional (check `Packages/manifest.json` before using): `-fsm`, `-mobile`, `-mono-hooks`, `-tags`, `-scene-mgmt`, `-ui`, `-input-system`
- **Namespaces:** `UnityAtoms` (core), `UnityAtoms.BaseAtoms` (int/float/bool/string/Vector/GameObject/Collider/etc. atoms)
- **Docs:** https://unity-atoms.github.io/unity-atoms

> Before using an API from an optional package, confirm it's listed in `Packages/manifest.json`. Never add packages without asking.

---

## 2. Core philosophy (non-negotiable)

Unity Atoms exists to keep game code **modular, editable, and debuggable**. Every change you make must serve these goals:

1. **No direct coupling between systems.** A `MonoBehaviour` must not hold a reference to another unrelated system's `MonoBehaviour` to read its state or call its methods. Systems communicate through shared Atom assets (Variables, Events, Collections).
2. **Data lives in assets, not in singletons.** Shared state is a `ScriptableObject` Variable, not a static field, singleton `Manager.Instance`, or `FindObjectOfType` lookup.
3. **Designers can rewire behaviour in the Inspector.** Prefer serialized Atom references over hard-coded logic so connections can be changed without code.
4. **Small, single-purpose components.** One component does one thing. Compose behaviour on a GameObject from several small components plus Atom assets.
5. **Everything is testable in isolation.** Because dependencies are injected via serialized assets, a component can be tested by dropping it in an empty scene with test Atom assets.

### Forbidden patterns (unless explicitly requested)

| Don't                                                     | Do instead                                                |
| --------------------------------------------------------- | --------------------------------------------------------- |
| `GameManager.Instance.score += 1`                         | Raise an `IntEvent` or update an `IntVariable`            |
| `FindObjectOfType<Player>()` / `GameObject.Find(...)`     | Serialize a `GameObjectVariable` or `GameObjectValueList` |
| `public static event Action OnPlayerDied`                 | `VoidEvent` asset (`PlayerDied`)                          |
| `public float speed = 5f;` for tunable shared values      | `[SerializeField] FloatReference _speed;`                 |
| Polling another component's field in `Update()`           | Subscribe to the Variable's `Changed` event               |
| Large "manager" MonoBehaviours with many responsibilities | Multiple small components + Atom assets                   |

---

## 3. The Atoms and when to use each

| Atom                   | Base type                         | Use for                                                                  | Example asset                                               |
| ---------------------- | --------------------------------- | ------------------------------------------------------------------------ | ----------------------------------------------------------- |
| **Constant**           | `AtomBaseVariable<T>` (read-only) | Immutable shared values                                                  | `MaxHealth` (`IntConstant`)                                 |
| **Variable**           | `AtomVariable<T, ...>`            | Mutable shared state; raises `Changed` / `ChangedWithHistory`            | `PlayerHealth` (`IntVariable`)                              |
| **Reference**          | `AtomReference<T, ...>`           | Serialized field that can be a literal, Constant, Variable, or Instancer | `IntReference _damage`                                      |
| **Event**              | `AtomEvent<T>`                    | Fire-and-forget messages between systems                                 | `EnemyKilled` (`GameObjectEvent`), `GameOver` (`VoidEvent`) |
| **Event Reference**    | `AtomEventReference<...>`         | Serialized field pointing at an Event or a Variable's Changed event      | `IntEventReference _onScoreChanged`                         |
| **Pair**               | `IPair<T>`                        | Old/new value in `ChangedWithHistory`                                    | `IntPair`                                                   |
| **Action**             | `AtomAction` / `AtomAction<T>`    | Reusable side-effect as an asset, invoked by listeners                   | `PlayHitSound`                                              |
| **Function**           | `AtomFunction<R, T>`              | Reusable transform; used as Variable **pre-change transformers**         | `ClampInt` for health                                       |
| **Condition**          | `AtomCondition<T>`                | Reusable boolean predicate asset                                         | `IsHealthLow`                                               |
| **Value List**         | `AtomValueList<T, ...>`           | Runtime set of values; raises Added/Removed/Cleared                      | `ActiveEnemies` (`GameObjectValueList`)                     |
| **Collection**         | `AtomCollection`                  | Keyed heterogeneous runtime data                                         | `PlayerStats`                                               |
| **Listener**           | `AtomEventReferenceListener<...>` | Component that hooks an Event to UnityEvents / Actions without code      | `IntEventReferenceListener`                                 |
| **Variable Instancer** | `AtomVariableInstancer<...>`      | Per-instance copy of a Variable (e.g. each enemy's own health)           | `IntVariableInstancer` on enemy prefab                      |
| **Event Instancer**    | `AtomEventInstancer<...>`         | Per-instance Event                                                       | `IntEventInstancer`                                         |

### Decision rules

- **Shared by many objects?** → Variable asset. **Per prefab instance?** → Variable Instancer referenced via `IntReference` (Usage: Variable Instancer).
- **"Something happened"** → Event. **"Something is"** → Variable.
- **Value never changes at runtime** → Constant (or Reference with Use Constant).
- **Need to know about many objects of a kind** → Value List that each object adds itself to in `OnEnable` and removes in `OnDisable`.
- **Clamping / validation of a Variable** → pre-change transformer Function, not ad-hoc clamping in every writer.

---

## 4. Coding rules

### 4.1 Serialized fields

- Always use `[SerializeField] private` fields, never public fields, for Atom references.
- Prefer **References** (`IntReference`, `FloatReference`, `BoolReference`, …) over concrete `IntVariable` fields whenever a component only needs to read/write a value. This lets the designer pick constant, variable, or instancer.
- Use concrete **Event** types (`VoidEvent`, `IntEvent`) for things a component _raises_, and **Event References** (`IntEventReference`, `VoidBaseEventReference`) for things a component _listens to_.
- Name fields with an underscore prefix and a role-describing name: `_health`, `_onDamaged`, `_enemyDied`.
- Mark every field the component cannot work without with `[Required]` (`Game.Atoms`): `[Required, SerializeField] private IntReference _health;`. Empty required fields block Play Mode and fail `RequiredFieldTests`. Fields without `[Required]` are optional and must be null-checked where used.

```csharp
using UnityAtoms.BaseAtoms;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>Applies incoming damage to a health value. Knows nothing about UI, audio, or score.</summary>
    public sealed class Damageable : MonoBehaviour
    {
        [SerializeField] private IntReference _health;      // Variable or Variable Instancer
        [SerializeField] private VoidEvent _died;            // Raised once health reaches 0

        public void TakeDamage(int amount)
        {
            if (amount <= 0 || _health.Value <= 0) return;

            _health.Value -= amount;   // Pre-change transformer (ClampInt) handles bounds

            if (_health.Value <= 0 && _died != null)
                _died.Raise();
        }
    }
}
```

### 4.2 Subscribing and unsubscribing

- Register in `OnEnable`, unregister in `OnDisable`. **Every `Register` must have a matching `Unregister`.** Leaked listeners on ScriptableObjects survive scene loads and cause bugs and null references.
- Prefer method groups over lambdas so unregistration works.
- Null-check optional Event assets before registering.

```csharp
using UnityAtoms.BaseAtoms;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class HealthBar : MonoBehaviour
    {
        [SerializeField] private IntVariable _health;
        [SerializeField] private IntConstant _maxHealth;
        [SerializeField] private Image _fill;

        private void OnEnable()
        {
            _health.Changed.Register(OnHealthChanged);
            OnHealthChanged(_health.Value); // Sync initial state
        }

        private void OnDisable()
        {
            _health.Changed.Unregister(OnHealthChanged);
        }

        private void OnHealthChanged(int value)
        {
            _fill.fillAmount = Mathf.Clamp01((float)value / _maxHealth.Value);
        }
    }
}
```

- When the inspector-only path is enough (e.g. "play a sound when `GameOver` fires"), **don't write a script** — use an `*EventReferenceListener` component with a UnityEvent or an Action asset. Mention this to the user in your summary.

### 4.3 Writing values

- Write through `.Value` (or `SetValue`) so `Changed` events fire. Never bypass the Variable to cache and mutate a copy.
- Don't write to the same Variable from many unrelated systems. If several systems need to change a value, have them raise an Event and let **one** owner component apply the change.
- Use `ResetValue()` (or Variable's initial value) at game/level start instead of manual zeroing scattered across scripts.

### 4.4 Custom Atom types

- For custom data types (e.g. `DamageInfo`, `QuestState`), use the **Unity Atoms Generator** (`Window > Unity Atoms > Generator`) rather than hand-writing the boilerplate. If you cannot run the editor, write the struct and tell the user which atoms to generate (Variable, Event, Pair, Reference, EventReference, Listener, etc.).
- Custom value types must be serializable (`[System.Serializable]`), preferably `struct`, and implement `IEquatable<T>` so Variables can detect changes correctly.
- Place generated atoms in `Assets/_Project/Scripts/Atoms/Generated/<TypeName>/` and don't hand-edit generated files; regenerate instead.

```csharp
using System;
using UnityEngine;

namespace Game.Combat
{
    [Serializable]
    public struct DamageInfo : IEquatable<DamageInfo>
    {
        public int Amount;
        public GameObject Source;

        public bool Equals(DamageInfo other) => Amount == other.Amount && Source == other.Source;
        public override bool Equals(object obj) => obj is DamageInfo other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Amount, Source);
    }
}
```

### 4.5 Custom Actions, Functions, Conditions

- Subclass the generic bases and add `[CreateAssetMenu]` under a consistent menu path.
- Keep them stateless; any configuration is serialized on the asset.

```csharp
using UnityAtoms;
using UnityAtoms.BaseAtoms;
using UnityEngine;

namespace Game.Atoms.Actions
{
    [CreateAssetMenu(menuName = "Unity Atoms/Actions/Game/Add To Score")]
    public sealed class AddToScore : AtomAction<int>
    {
        [SerializeField] private IntVariable _score;

        public override void Do(int amount) => _score.Value += amount;
    }
}
```

### 4.6 Runtime sets (Value Lists)

Objects register themselves; consumers read the list. Nobody searches the scene.

```csharp
using UnityAtoms.BaseAtoms;
using UnityEngine;

namespace Game.Enemies
{
    public sealed class RegisterInList : MonoBehaviour
    {
        [SerializeField] private GameObjectValueList _list;

        private void OnEnable()  => _list.Add(gameObject);
        private void OnDisable() => _list.Remove(gameObject);
    }
}
```

### 4.7 General C# / Unity conventions

- One public type per file; file name matches type name.
- Namespaces mirror folders: `Game.<Feature>`.
- `sealed` classes by default; `private` by default.
- No allocations in `Update`/`FixedUpdate` hot paths (no LINQ, no closures, no string concat).
- Cache component lookups in `Awake`; never call `GetComponent` per frame.
- Use `TryGetComponent` over `GetComponent` + null check.
- Don't use `Update()` to check for state changes — that's what Atom events are for.
- XML `<summary>` on every public type stating its single responsibility.

---

## 5. Folder & asset layout

```
Assets/_Project/
├── Atoms/                      # ScriptableObject ASSETS (.asset)
│   ├── Constants/
│   ├── Variables/
│   ├── Events/
│   ├── Lists/
│   ├── Collections/
│   ├── Actions/
│   ├── Functions/
│   └── Conditions/
├── Scripts/
│   ├── Atoms/                  # Custom atom SCRIPTS (Actions, Functions, Conditions)
│   │   └── Generated/          # Generator output — do not hand-edit
│   └── <Feature>/              # Gameplay MonoBehaviours, grouped by feature
├── Data/
│   └── Scenarios/              # Scenario assets (start states for testing)
├── Prefabs/
├── Scenes/
└── Tests/
    ├── EditMode/
    └── PlayMode/
```

### Asset naming

- Variables: noun describing the state — `PlayerHealth`, `Score`, `IsPaused`
- Events: past tense or `On` + noun — `PlayerDied`, `LevelCompleted`, `OnScoreChanged`
- Constants: `Max`/`Default` prefix where meaningful — `MaxHealth`, `DefaultSpeed`
- Lists: plural — `ActiveEnemies`, `Pickups`
- Actions: verb phrase — `PlayHitSound`, `AddToScore`
- Conditions: `Is`/`Has` — `IsHealthLow`, `HasKey`

You **cannot create `.asset` files reliably by writing YAML by hand**. When a change requires new Atom assets, write the code, then list in your summary exactly which assets the user must create (type, name, folder) and where to wire them in the Inspector.

---

## 6. Assembly definitions

If the project uses `.asmdef` files, any assembly using Atoms must reference the Atoms runtime assemblies (check the package folders for exact names, typically `UnityAtomsCore.Runtime` and `UnityAtomsBaseAtoms.Runtime`, plus any optional package runtime assemblies you use). Don't add editor assemblies to runtime asmdefs.

This project has three:
- `Game.Core` (`Assets/_Project/Core/`): pure C#, references nothing.
- `Game.Runtime` (`Assets/_Project/Scripts/`): all gameplay code and generated atoms. It references `Game.Core`, all Atoms runtime assemblies, Input System, AI Navigation, URP, ProBuilder, uGUI and TextMeshPro. If a new package's types are needed, add that package's runtime assembly to this asmdef. Never put an `Editor/` folder under `Assets/_Project/Scripts/`.
- `Game.Core.Tests` (`Assets/_Project/CoreTests/`). Editor code and binder tests stay in `Assets/Editor/` (Assembly-CSharp-Editor). `Game.Runtime` exposes `internal` test hooks to it.

---

## 7. Testing

- Edit Mode tests can create atoms with `ScriptableObject.CreateInstance<IntVariable>()` and destroy them in `[TearDown]` with `Object.DestroyImmediate`.
- Test components in isolation by injecting test atoms. Expose an `internal` or `#if UNITY_INCLUDE_TESTS` setter, or use `SerializedObject` to assign private serialized fields — don't make fields public for tests.
- Test that listeners unregister: disable the component, raise the event, assert nothing happened.
- **Running tests:** run only the fixtures for what you changed, by name filter, always with a timeout (e.g. `--timeout 600`). Never run the whole suite unless the user asks. If a run takes much longer than expected, stop and report it instead of waiting.

```csharp
using NUnit.Framework;
using UnityAtoms.BaseAtoms;
using UnityEngine;

public class ScoreTests
{
    private IntVariable _score;

    [SetUp]    public void SetUp()    => _score = ScriptableObject.CreateInstance<IntVariable>();
    [TearDown] public void TearDown() => Object.DestroyImmediate(_score);

    [Test]
    public void Changed_Fires_When_Value_Set()
    {
        int received = -1;
        void Handler(int v) => received = v;

        _score.Changed.Register(Handler);
        _score.Value = 10;
        _score.Changed.Unregister(Handler);

        Assert.AreEqual(10, received);
    }
}
```

---

## 8. Common pitfalls to avoid

- **Variables persist in the Editor.** Changing a Variable asset in Play Mode changes the asset; values may carry over between play sessions. Reset on start (`ResetValue()` or the Variable's reset settings) when state must be fresh.
- **Shared Variable on a prefab.** If every enemy's health points to the same `IntVariable`, damaging one damages all. Use a **Variable Instancer** for per-instance state.
- **Raising events in `OnDisable`/`OnDestroy` during scene unload** can hit listeners already destroyed. Guard or avoid.
- **Event loops.** A listener that writes to the Variable it listens to causes recursion. Restructure so one owner writes.
- **Lambdas in `Register`** can't be unregistered. Use method groups.
- **Serialized references are null by default.** Mark required ones `[Required]` (section 4.1) so the validator catches them before Play. For objects spawned at runtime, also validate in `Awake` and log a clear error naming the missing field and GameObject.

```csharp
private void Awake()
{
    if (_died == null)
        Debug.LogError($"{nameof(Damageable)} on '{name}' is missing the '{nameof(_died)}' event.", this);
}
```

---

## 9. Agent workflow

1. **Read before writing.** Check existing Atom assets and scripts under `Assets/_Project/Atoms` and `Scripts/` — reuse existing Variables/Events instead of creating duplicates.
2. **Model first.** Before coding a feature, decide which state is a Variable, which signals are Events, and which components own writes. State this plan briefly in your response.
3. **Prefer no-code wiring.** If an existing Listener + Action can do it, say so instead of writing a script.
4. **Keep changes small.** One feature, a few small components. Don't refactor unrelated systems.
5. **Don't touch** `.meta` files, `ProjectSettings/`, `Packages/manifest.json`, scenes, or prefabs unless explicitly asked. Never delete or rename existing Atom assets — other scenes and prefabs reference them by GUID.
6. **Add a scenario.** For every feature that needs game progress to reach, create a `Scenario` asset (via a setup script) in `Assets/_Project/Data/Scenarios/` that starts right where the feature can be tested, and name it in your summary.
7. **Record rule changes.** Any change to AGENTS.md, `Docs/Design/Architecture.md` or the architecture/validation tests gets an entry in `Docs/Decisions.md` (what, why, instead of).
8. **Summarize setup steps.** End every change with a short checklist: new assets to create, Inspector fields to wire, and any Generator types to generate.
9. **If unsure about an Atoms API**, check the installed package source under `Packages/` or `Library/PackageCache/com.unity-atoms.*` rather than guessing; APIs differ between major versions.
