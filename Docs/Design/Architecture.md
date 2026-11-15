# Architecture Specification (single source of truth)

Status: TEMPLATE BASELINE (enhanced). This is the generic part of the architecture used in Fluffle, with everything
game-specific removed, plus D10–D11 and the `Game.Runtime` assembly (see `Docs/Decisions.md`). When the game grows, add game decisions as new rows (D10, D11, …) and new sections;
don't rewrite the baseline rows without writing down why.

Where this document and `GameDesign.md` disagree, this document wins. Where this document and `AGENTS.md`
disagree, this document wins only where it says so explicitly.

---

## 0. Top-level decisions (read first)

| # | Decision |
|---|---|
| D1 | **Assemblies.** One pure runtime asmdef `Game.Core` at `Assets/_Project/Core/` (pure C#: no Atoms, no gameplay code, no Physics/NavMesh/Time/Random/Object.Find). One test asmdef `Game.Core.Tests` at `Assets/_Project/CoreTests/`. All other runtime code goes into **`Game.Runtime`** (`Assets/_Project/Scripts/Game.Runtime.asmdef`) under `Assets/_Project/Scripts/<Feature>/`; `Assembly-CSharp` stays empty. Editor code and binder tests go into Assembly-CSharp-Editor (`Assets/Editor/`). (Changed from Fluffle, see `Docs/Decisions.md`.) |
| D2 | **Three-layer pattern** for every mechanic: Core (pure, `Game.Core`) → Binder (sealed MonoBehaviour, Assembly-CSharp, wires Atoms) → optional Presenter (visuals only). Cores hold the authoritative state; binders mirror values into Atom References (one-way). |
| D3 | **No singletons, no static events.** "Managers" become small single-purpose components on a systems prefab (e.g. `Assets/_Project/Prefabs/Systems/GameSystems.prefab`), communicating through Atom assets and a few runtime-state ScriptableObjects ("service assets"). |
| D4 | **Game time is event-driven (recommended for simulation games).** One `GameClock` owns time and raises `GameTicked` (FloatEvent, payload = elapsed game time, already scaled and zero while paused). Simulation binders advance on `GameTicked`; only presentation uses `Update`. Tests raise the event or call `internal void Tick(float dt)`. Real-time action games may use `Update` in binders instead; record that choice as a decision row. |
| D5 | **Randomness is injected.** Cores take an `IRandomSource` (`Game.Core`). Runtime binders pass `UnityRandomSource` (`Game.Contracts`), tests pass `SeededRandom`. |
| D6 | **Single writer per Variable.** Every shared Variable has exactly one owning component that writes it. Others raise an Event that the owner handles. Keep the owner table in section 3. |
| D7 | **Atom events never replay.** Every Event asset under `Assets/_Project/Atoms/` has `ReplayBufferSize = 0`, and every listener registers with `replayEventsBuffer: false`. Enforced by `ArchitectureTests`. |
| D8 | **Custom atom types are generated**, never hand-written: list the struct in `AtomGenerationSetup.TypeNames` and run `Game/Setup/00 Generate Custom Atoms`. |
| D9 | **Assets and wiring are created by setup scripts** (`Assets/Editor/Game/`, section 5), not by hand, so they can be re-run and reviewed. |
| D10 | **Required references fail loudly.** Fields a component cannot work without carry `[Required]` (`Game.Atoms`). `PlayModeRequiredGuard` blocks Play while a loaded scene has one empty; `RequiredFieldValidator` (menu `Game/Validate/Required Fields`, also in Run All) and `RequiredFieldTests` scan prefabs and Build Settings scenes. `Awake` logging stays for runtime-spawned objects. |
| D11 | **Scenarios.** Each feature that needs progress to reach ships a `Scenario` asset (`Assets/_Project/Data/Scenarios/`, created by its setup script) that starts the game at a testable state: Variable values, spawns, then events, applied one frame after Play starts by `ScenarioRunner`. |

---

## 1. Layers and dependency DAG

```
L0  Game.Core (asmdef)         Game.Core.*            pure contracts, config structs, cores
L1  Game.Runtime               Game.Contracts         Unity-side contracts, generated-atom structs
                               Game.Data              ScriptableObject data + runtime-state "service assets"
                               Game.Atoms             small Atom helpers (e.g. Requirements)
                               UnityAtoms.BaseAtoms   generated atoms
L2  Game.Runtime feature binders (siblings, never reference each other's MonoBehaviour types):
    Game.<Feature>             e.g. Game.Combat, Game.Movement, Game.Scoring, Game.Scenarios
L3  Game.UI.*                  views (read Atoms + L1 service assets + L0/L1 interfaces only)
L4  Editor setup               Assets/Editor/Game/*   (may reference everything)
L5  Tests                      Game.Core.Tests → L0;  Assembly-CSharp-Editor tests → L0..L4
```

Rules (enforced by `Assets/Editor/Tests/Architecture/ArchitectureTests.cs`):
- An L2 module never has a field/parameter/return type that is a MonoBehaviour declared in another L2 module. Cross-entity calls go through L0/L1 interfaces via `TryGetComponent<IInterface>()`.
- Entity-local composition is allowed: components on the same GameObject talk through L0/L1 interfaces obtained with `TryGetComponent`, validated in `Awake`.
- UI never references a gameplay MonoBehaviour type.
- No `static event`, no static `Instance` in `Game.*` or `Game.Core`.
- `Game.Core` sources contain none of: `UnityEngine.Random`, `Random.`, `Time.`, `Physics`, `NavMesh`, `FindObjectOfType`, `GameObject.Find`, `Debug.Log`, `MonoBehaviour`, `ScriptableObject`, `DateTime.Now`.
- `Game.Core.*` namespaces are declared only inside the `Game.Core` assembly.

### 1.1 Namespaces
- Mirror folders: `Assets/_Project/Scripts/Combat/` → `Game.Combat`; `Assets/_Project/Core/Combat/` → `Game.Core.Combat`.
- Never use a segment that shadows a UnityEngine/System type (`Time`, `Input`, `Random`, `Debug`, `Object`, `Physics`, `Camera`, `Audio`, `Event`, `Action`, … full list in `ArchitectureTests.ForbiddenSegments`).
- Never name a type like a namespace segment (no class `Combat` while `Game.Combat` exists).

---

## 2. Game.Core (L0)

- Contracts live in `Assets/_Project/Core/Contracts/` (namespace `Game.Core`): interfaces, enums, config structs, ids.
- Cores live in `Assets/_Project/Core/<Area>/`: plain C# classes with the rules and state of a mechanic. They take config structs and `IRandomSource` in the constructor and expose methods like `Tick(float dt)`.
- `AssemblyInfo.cs` makes `internal` members visible to `Game.Core.Tests` and `Assembly-CSharp-Editor`.

---

## 3. Atom assets and single writers

All assets under `Assets/_Project/Atoms/<Kind>/<Feature>/`. Keep this table up to date when adding shared state.

| Asset | Type | Single writer | Readers |
|---|---|---|---|
| *(none yet)* | | | |

---

## 4. Test strategy

### 4.1 Assemblies and folders
- Pure cores: `Assets/_Project/CoreTests/<Area>/<Type>Tests.cs` (`Game.Core.Tests`, namespace `Game.Core.<Area>.Tests`).
- Binders/content/architecture/validation: `Assets/Editor/Tests/<Feature>/` (Assembly-CSharp-Editor; namespace `Game.<Feature>.Tests`). Shared helpers: `Assets/Editor/Tests/Support/` (`TestScene`, `AtomFactory`).
- Scene integration: EditMode `[UnityTest]` with `yield return new EnterPlayMode();` … `ExitPlayMode`. Keep these few; they are slow.

### 4.2 Binder fixture pattern (EditMode)
- `Awake/OnEnable/Start/OnDisable` do NOT run for `AddComponent` in EditMode → call `internal void Init()` / `Bind()` / `Unbind()` hooks (or `TestScene.CallPrivate(c, "OnEnable")`) and always unbind in TearDown.
- `AtomVariable.Value` returns `InitialValue` while `!Application.isPlaying` → wire `*Reference` fields in **VALUE** usage, assert on the core (`internal Core`) or via `Changed` handlers, never read a shared Variable's `.Value`.
- Always use `AtomFactory` (`ScriptableObject.CreateInstance`) atoms, never project assets; destroy in TearDown.
- Physics: `TestScene.New(physics: true)` gives a preview scene with its own `PhysicsScene`.

```csharp
public sealed class DamageableTests
{
    private TestScene _scene; private AtomFactory _atoms; private Damageable _dmg;
    [SetUp] public void SetUp() {
        _scene = TestScene.New(); _atoms = new AtomFactory();
        _dmg = _scene.Add<Damageable>("Target");
        TestScene.SetValueRef(_dmg, "_health", 10);
        TestScene.Set(_dmg, "_died", _atoms.New<VoidEvent>());
    }
    [TearDown] public void TearDown() { _scene.Dispose(); _atoms.DestroyAll(); }
    [Test] public void TakeDamage_To_Zero_Raises_Died() { /* ... */ }
}
```

### 4.3 Architecture test
`Assets/Editor/Tests/Architecture/ArchitectureTests.cs` checks every rule of section 1 and D7, and tests its own rule helpers on fixture types so a broken rule cannot pass silently.

---

## 5. Editor setup scripts (`Assets/Editor/Game/`, namespace `Game.Editor.Setup`)

Every script: static class, `[MenuItem("Game/Setup/<NN> <Name>")]` + `[GameSetupStep(NN)]` + `public static string Run()` returning a log, no dialogs, idempotent (load-or-create, add-if-missing, never duplicate). Assets via `AssetDatabase`, prefabs via `PrefabUtility.LoadPrefabContents` → edit → `SaveAsPrefabAsset` → `UnloadPrefabContents`, scenes via `EditorSceneManager.OpenScene` → edit → `SaveScene`. `GameSetup.RunAll()` (menu `Game/Setup/Run All`) discovers steps by reflection, runs them in order, then `MissingReferenceValidator.Run()`.

Shared helpers in `SetupUtil.cs`: `LoadOrCreate<T>(path)`, `EnsureFolder(path)`, `UpdatePrefab(path, Func<GameObject,bool>)`, `AddOnce<T>(GameObject)`, `Child(root, name)`, `SetRef`, `SetValueRef`, `SetConstantRef`, `SetVariableRef`, `SetInstancerRef`, `SetEventRef`, `SetReplayBufferSize`, `RemovePersistentListeners`, `LoadInputAction("Player/Attack")`, `LayerMaskOf(...)`.

| Order | Script | Menu | Creates / changes |
|---|---|---|---|
| 00 | AtomGenerationSetup | Game/Setup/00 Generate Custom Atoms | generator assets + generated code for `TypeNames` |

**MissingReferenceValidator** (`Assets/Editor/Game/Validation/`, menu `Game/Validate/Missing References`): missing scripts and unresolved persistent UnityEvent calls in `Assets/_Project/Prefabs`, `Assets/Prefabs` and every enabled Build Settings scene.

**RequiredFieldValidator** (same folder, menu `Game/Validate/Required Fields`): empty `[Required]` fields in the same prefabs and scenes. **PlayModeRequiredGuard** runs it on the open scenes when Play is pressed (toggle `Game/Validate/Block Play On Empty Required Fields`).

**Scenarios** (`Assets/Editor/Game/Scenarios/`): `ScenarioLauncher.Play(scenario)`, menu `Game/Scenarios/Scenario Picker` and `Game/Scenarios/Play Last Scenario`, "Play This Scenario" button on each asset. A setup step that creates a scenario uses `SetupUtil.LoadOrCreate<Scenario>("Assets/_Project/Data/Scenarios/<Name>.asset")` and fills it with `SetupUtil.SetRef`.

---

## 6. Conventions for implementation agents

1. **Read before writing.** Reuse existing Atom assets and cores; check the package source under `Library/PackageCache/` when unsure of an API.
2. **Never hand-write `.asset`, `.prefab`, `.unity`, `.meta` YAML, ProjectSettings or `.inputactions` JSON.** Assets come from setup scripts (section 5) run in the live Editor. Never delete or rename existing assets, scripts or serialized fields; use `[FormerlySerializedAs]` if a rename is unavoidable.
3. **Naming.** One public type per file, file name = type name. `public sealed class` by default. `[SerializeField] private` fields with `_camelCase` role names. Events past tense, variables nouns, lists plural.
4. **Docs.** XML `<summary>` on every public type stating its single responsibility; on interfaces, every member.
5. **Atoms usage.** References for values a component reads/writes; concrete `*Event` types for events it raises; `*EventReference` for events it listens to. Register in `OnEnable` with `replayEventsBuffer: false`, unregister in `OnDisable`, method groups only.
6. **Time & randomness.** Per D4/D5. No allocations in `Update`/tick paths.
7. **Validation.** Mark required references `[Required]` (D10). For runtime-spawned objects also log a clear error in `Awake` naming the missing field and GameObject.
8. **Tests.** Every feature ships tests per section 4 and, where it needs progress to reach, a scenario (D11). Test names `Method_Condition_Result`. Run only the touched fixtures, with a timeout.
9. **Editor scripts.** Per section 5.
10. **Summaries.** Finish each work item with: files created/modified, tests added, and the assets/wiring the setup scripts provide.
