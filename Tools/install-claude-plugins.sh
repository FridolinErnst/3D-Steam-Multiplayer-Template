#!/usr/bin/env sh
# Installs the Claude Code plugins this project uses.
# Run from the project root (needs the `claude` CLI on PATH: npm install -g @anthropic-ai/claude-code).
#
# Inside a Claude Code chat the same thing is:
#   /plugin marketplace add https://github.com/Unity-Technologies/unity-agent-plugin.git
#   /plugin install unity@unity-agent-plugin
#
# Not strictly needed: .claude/settings.json already lists the marketplace and the plugin,
# so Claude Code offers to install it when the project folder is trusted.
set -e

claude plugin marketplace add https://github.com/Unity-Technologies/unity-agent-plugin.git
claude plugin install unity@unity-agent-plugin --scope project

# Blender connection (MCP server "blender", configured in .mcp.json; https://github.com/ahujasid/blender-mcp).
# 1. The server runs through uvx, so uv must be installed:
command -v uvx >/dev/null || powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://astral.sh/uv/install.ps1 | iex"
# 2. Download the Blender add-on next to this script:
curl -fsSL -o "$(dirname "$0")/blender_mcp_addon.py" https://raw.githubusercontent.com/ahujasid/blender-mcp/main/addon.py
# 3. By hand, in Blender: Edit > Preferences > Add-ons > Install from Disk > Tools/blender_mcp_addon.py, enable "Blender MCP",
#    then in the 3D view sidebar (N) > BlenderMCP tab press "Connect to Claude". Restart Claude Code afterwards.
#    Without .mcp.json, the same server can be added per user with:
#    claude mcp add blender -- cmd /c uvx blender-mcp
