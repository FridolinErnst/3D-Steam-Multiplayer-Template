# 3D Steam Multiplayer Template

A Unity 6 starting point for 3D multiplayer games on Steam, set up for building the game with
[Claude Code](https://claude.com/claude-code).

- **Architecture:** Scriptable Object architecture with [Unity Atoms](https://github.com/unity-atoms/unity-atoms).
  The rules every agent follows are in [AGENTS.md](AGENTS.md).
- **Built in:** `[Required]` field validation, Scenarios (start Play mode at a testable state) and architecture tests.
- **Agent skills** (in `.claude/skills/`): planning interviews (`/grill-with-docs`), Steam publishing, Unity builds,
  animation, input, Blender → Unity export, and [graphify](https://github.com/Graphify-Labs/graphify) code search.
- **Not decided yet:** the netcode and Steam libraries. You choose them in your first planning session (see below).

## Requirements

- Unity **6000.3.9f1** (install it from Unity Hub)
- [Git LFS](https://git-lfs.com)
- [Claude Code](https://claude.com/claude-code)
- Optional: [Blender](https://www.blender.org), for making models with Claude

## Setup

1. On GitHub, click **Use this template** → create your own repository, then clone it.
2. From the project folder, run the setup script once:
   ```sh
   sh Tools/install-claude-plugins.sh
   ```
   It pulls the LFS files, installs `uv` and `graphify`, adds the Unity Claude plugin and downloads the Blender add-on.
3. **Restart your terminal and editor.** The hooks in `.claude/settings.json` need `graphify` on your PATH.
4. Open the project in Unity Hub. The first import takes a few minutes.
5. Optional, for Blender: install `Tools/blender_mcp_addon.py` in Blender (Edit → Preferences → Add-ons →
   Install from Disk), then press **Connect to Claude** in the 3D view's N-panel → BlenderMCP tab.

## Start your game

```sh
claude
```

```
/grill-with-docs <your game idea, in a few sentences>
```

Claude interviews you in rounds of numbered questions, each with a recommended answer ("yes" accepts it).
It covers the game, the netcode stack (e.g. Netcode for GameObjects, FishNet or Mirror), the Steam wrapper
(Steamworks.NET or Facepunch.Steamworks), hosting, how Atoms and networked state fit together, and testing
without Steam. Decisions are written to `docs/adr/` and terms to `GLOSSARY.md` as you go. Nothing gets built
until you confirm.

After that, ask Claude to build it feature by feature. Each change follows AGENTS.md: Claude does the Unity Editor work itself (assets, wiring, Play mode checks)
through the Unity CLI, runs only the tests for the code it changed, and ends with a summary of what it did.

## Folder layout

| Folder | What goes there |
|--------|-----------------|
| `Assets/_Project/` | All game content: Atoms, scripts, prefabs, scenes, data |
| `BlenderSource/` | `.blend` source files, kept outside `Assets/` |
| `Tools/Blender/` | Headless Blender rebuild scripts (`build_<asset>.py`) |
| `Docs/` | Design docs and `Decisions.md` (why each rule exists) |
| `.claude/` | Agent skills, hooks and settings. Licenses for third-party skills are in `.claude/skill-licenses/` |

## Good to know

- `graphify-out/` is not committed. Rebuild it with `graphify update .`, which costs nothing (it uses no API).
- The `nosleep` skill and `Tools/claude-autocontinue.ps1` only work on Windows.
- For Steam testing before you have your own App ID, Valve's public test app **480 (Spacewar)** works.
