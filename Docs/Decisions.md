# Decisions

One entry per change to the rules (AGENTS.md, Architecture.md, setup scripts, tests that enforce rules).
Newest first. Say **what** changed and **why** in plain words, so anyone can see later why a rule exists.

Template:

```
## YYYY-MM-DD — Short title
**What:** …
**Why:** …
**Instead of:** (the option that was rejected, if any)
```

---

## 2026-11-15 — Agent skills and code graph for the Steam multiplayer template
**What:** Project skills in `.claude/skills/`: grilling (`/grill-me`, `/grill-with-docs`, `domain-modeling`),
`steam-publish`, `unity-build-pipeline`, `unity-animation`, `unity-input-system`, our own `blender-to-unity`,
and graphify (code knowledge graph, hooks in `.claude/settings.json`). Sources and licenses are in `.claude/skill-licenses/`.
Blender `.blend` sources live in `BlenderSource/`, outside `Assets/`.
**Why:** Plan with `/grill-with-docs` before building. Agents get correct Steam upload, build, animation and input
know-how without searching, and query the graph instead of grepping the whole project.
**Instead of:** Large third-party skill packs (e.g. 94 Blender skills): they cost context in every session and
depend on sibling skills. Duplicating skills from the official Unity plugin (`unity@unity-agent-plugin`)
was also rejected. **Open:** there is no skill yet for Steamworks SDK use in game code (lobbies, P2P, achievements).
Write it once the netcode stack is decided.

## 2026-11-15 — Template extras on top of the Fluffle architecture
This project is the Fluffle architecture (see `Docs/Design/Architecture.md` D1–D9) plus the following.

### Gameplay code has its own assembly (`Game.Runtime`)
**What:** `Assets/_Project/Scripts/Game.Runtime.asmdef` holds all gameplay code; `Assembly-CSharp` stays empty.
Generated atoms live inside it too. Editor code and binder tests stay in `Assembly-CSharp-Editor`.
**Why:** Editing one script no longer recompiles everything, and PlayMode tests can reference gameplay code
(in Fluffle they couldn't see `Assembly-CSharp`).
**Instead of:** Keeping gameplay in `Assembly-CSharp` like Fluffle (simpler, but slower compiles).

### `[Required]` fields fail loudly
**What:** `Game.Atoms.RequiredAttribute` marks fields a component cannot work without. Empty required fields
block Play Mode (`PlayModeRequiredGuard`), are listed by `Game/Validate/Required Fields`, run in
`Game/Setup/Run All`, and fail `RequiredFieldTests` (project scan).
**Why:** A missing reference should be found before playing, all at once, with a message that names the field,
instead of a `NullReferenceException` or a UI that silently shows nothing. Optional fields stay optional.
**Instead of:** Throwing exceptions in `Awake` (stops at the first problem, only after pressing Play), or a
custom Inspector drawer (it would replace Unity Atoms' own Reference drawers).

### Scenarios start the game at a testable state
**What:** `Scenario` assets (Assets > Create > Game > Scenario, kept in `Assets/_Project/Data/Scenarios/`)
set Variables, spawn prefabs and raise events one frame after Play starts. Play them from
`Game/Scenarios/Scenario Picker`, the asset's "Play This Scenario" button, or `Game/Scenarios/Play Last Scenario`.
**Why:** Testing a new feature shouldn't mean playing for minutes to reach it. Because state lives in Atom
Variables, a starting state is mostly a list of Variable values.

### `.editorconfig` instead of a written style guide
**What:** Code style (naming, braces, `var`) lives in `.editorconfig`; the IDE shows hints. All rules are
suggestions, not errors.
**Why:** Machine-checked style beats a document nobody re-reads. AGENTS.md 4.7 remains the written summary.

### Test runs are small and bounded
**What:** AGENTS.md section 7 now says: run only the tests for what you changed, by filter, with a timeout.
**Why:** Long, unbounded test runs made chats hang.
