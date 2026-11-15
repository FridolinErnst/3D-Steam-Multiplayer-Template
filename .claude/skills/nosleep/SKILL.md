---
name: nosleep
description: Hands the current chat over to the unattended runner (Tools/claude-autocontinue.ps1), which resumes this session in its own window, never asks questions and waits out usage limits. Only when the user types /nosleep.
argument-hint: "[optional task; default: continue the current task]"
disable-model-invocation: true
---

The user is leaving. Hand this session to the unattended runner, then stop.

1. Write the hand-off prompt to `Logs/claude-autocontinue/handoff-${CLAUDE_SESSION_ID}.txt` (UTF-8):
   - If the user gave a task after `/nosleep`, use it: `$ARGUMENTS`
   - Otherwise write: `Continue the task from this conversation exactly where you left off. Do not ask anything; take the recommended action yourself.`
   - If the conversation has no unfinished task and no task was given, tell the user so in one line and skip the remaining steps.

2. Launch the runner with `-Detach`, resuming this session after a 60 s delay so this turn can finish first. `-Detach` starts it as a detached process (via WMI) in its own window. Never use `Start-Process` here: anything started from this shell is in this chat's job object and dies when the chat closes. Use the PowerShell tool, with the session id and file path filled in:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File "Tools\claude-autocontinue.ps1" -Detach -SessionId ${CLAUDE_SESSION_ID} -PromptFile "Logs\claude-autocontinue\handoff-${CLAUDE_SESSION_ID}.txt" -StartDelaySeconds 60
   ```

   If `${CLAUDE_SESSION_ID}` above was not replaced with an id, use the newest `.jsonl` file name (without extension) in `~/.claude/projects/<this project's folder>/`, which is this session.
   It must print `Started detached runner`. If it fails, show the error to the user and stop.

3. Reply with two short lines and end the turn, without other tool calls:
   - The runner takes over in about a minute in its own window. It keeps going even if you close VS Code, and waits out usage limits. The log is in `Logs/claude-autocontinue/`.
   - Don't type in this chat until the runner is done. Close the runner window to stop it. Afterwards, reopen this session to see what it did.
