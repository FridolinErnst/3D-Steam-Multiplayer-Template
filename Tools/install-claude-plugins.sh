#!/usr/bin/env sh
# One-time setup after cloning: checks Git LFS, installs uv, graphify and the Unity Claude plugin,
# and downloads the Blender MCP add-on.
# Run from the project root on macOS, Linux or Windows (Git Bash):  sh Tools/install-claude-plugins.sh
# Needs the `claude` CLI on PATH (https://claude.com/claude-code).
set -e

say() { printf '\n== %s\n' "$1"; }
is_windows() { case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) return 0 ;; *) return 1 ;; esac; }

# 1. Git LFS: models, textures and audio are stored in LFS (.gitattributes).
say "Git LFS"
if git lfs version >/dev/null 2>&1; then
    git lfs install --local >/dev/null
    git lfs pull
    echo "ok"
else
    echo "MISSING: install Git LFS (https://git-lfs.com), then run: git lfs install && git lfs pull"
    echo "Without it, models and textures are tiny pointer files and Unity can't import them."
fi

# 2. uv: runs graphify and the Blender MCP server.
say "uv"
if ! command -v uv >/dev/null 2>&1; then
    if is_windows; then
        powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://astral.sh/uv/install.ps1 | iex"
    else
        curl -LsSf https://astral.sh/uv/install.sh | sh
    fi
    export PATH="$HOME/.local/bin:$PATH"
fi
uv --version

# 3. graphify: code knowledge graph. The hooks in .claude/settings.json call `graphify`,
#    so it must be on PATH or every file read in Claude Code shows a hook error.
#    The PyPI package is "graphifyy" (two y's); the command is "graphify".
say "graphify"
uv tool install graphifyy || uv tool upgrade graphifyy
uv tool update-shell || true
export PATH="$HOME/.local/bin:$PATH"
graphify --version
graphify update .

# 4. Unity plugin for Claude Code (skills for the Unity CLI, multiplayer services, physics, UI, ...).
#    .claude/settings.json already lists it, so Claude Code also offers it when you trust the folder.
say "Unity Claude plugin"
if command -v claude >/dev/null 2>&1; then
    claude plugin marketplace add https://github.com/Unity-Technologies/unity-agent-plugin.git || true
    claude plugin install unity@unity-agent-plugin --scope project
else
    echo "MISSING: the claude CLI. Install Claude Code, then rerun this script."
fi

# 5. Blender connection (MCP server "blender" in .mcp.json; https://github.com/ahujasid/blender-mcp).
say "Blender MCP add-on"
curl -fsSL -o "$(dirname "$0")/blender_mcp_addon.py" https://raw.githubusercontent.com/ahujasid/blender-mcp/main/addon.py
echo "Downloaded Tools/blender_mcp_addon.py. In Blender: Edit > Preferences > Add-ons > Install from Disk >"
echo "Tools/blender_mcp_addon.py, enable \"Blender MCP\", then N-panel > BlenderMCP > Connect to Claude."

say "Done"
echo "Restart your terminal and editor so the new PATH applies, then run: claude"
echo "Then start planning your game with: /grill-with-docs <your game idea>"
