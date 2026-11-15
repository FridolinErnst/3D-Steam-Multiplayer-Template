<#
.SYNOPSIS
    Runs Claude Code unattended on this project and waits out usage limits.

.DESCRIPTION
    Starts (or resumes) a headless Claude Code session, tells it never to ask
    questions and to take the recommended option itself, and keeps sending
    "continue" until Claude reports ALL_TASKS_COMPLETE or MaxRuns is reached.
    When a usage limit is hit, it reads the reset time from the message, keeps
    the PC awake, sleeps until then and carries on in the same session.

    Permissions use auto mode by default (safe actions run, risky ones are
    denied and Claude has to pick another way). -Bypass skips all checks.

    Logs go to Logs/claude-autocontinue/ (gitignored).

.EXAMPLE
    .\Tools\claude-autocontinue.ps1
    Starts a fresh session that resumes the full-game implementation.

.EXAMPLE
    .\Tools\claude-autocontinue.ps1 -SessionId 1234abcd-...
    Continues a specific earlier session instead.

.EXAMPLE
    .\Tools\claude-autocontinue.ps1 -Prompt "Fix the 14 failing support tests" -MaxRuns 5

.EXAMPLE
    .\Tools\claude-autocontinue.ps1 -Detach -SessionId 1234abcd-... -StartDelaySeconds 60
    Starts the loop as a detached process in its own window and returns at once. Use this when launching from inside Claude Code: a process
    started directly from Claude's shell lives in Claude's job object and is
    killed as soon as that chat closes.

.NOTES
    Typing /nosleep in a chat (.claude/skills/nosleep) launches this with -Detach,
    resuming that chat's session.
#>
param(
    [string]$Prompt = "Continue the task from this conversation exactly where you left off. Do not ask anything; take the recommended action yourself.",
    [string]$PromptFile,      # read the prompt from this file instead (overrides -Prompt)
    [string]$SessionId,       # resume this session (id or name)
    [switch]$Continue,        # resume the most recent session in the project folder
    [int]$MaxRuns = 40,       # turns that actually ran (limit waits don't count)
    [int]$FallbackWaitMinutes = 30,
    [int]$StartDelaySeconds = 0,  # let the chat that launched this finish its turn first
    [switch]$Bypass,          # --dangerously-skip-permissions instead of auto mode
    [string]$Claude,          # path to claude.exe; found automatically if omitted
    [switch]$Detach           # run via Task Scheduler so it outlives the process that started it
)

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$projectDir = Split-Path $PSScriptRoot -Parent
$doneMarker = 'ALL_TASKS_COMPLETE'
$logDir = Join-Path $projectDir 'Logs\claude-autocontinue'
New-Item -ItemType Directory -Force $logDir | Out-Null

if ($Detach) {
    # Hand the prompt over as a file so no quoting survives a trip through the command line.
    if (-not $PromptFile -and $PSBoundParameters.ContainsKey('Prompt')) {
        $PromptFile = Join-Path $logDir ("prompt-{0:yyyyMMdd-HHmmss}.txt" -f (Get-Date))
        Set-Content -Path $PromptFile -Value $Prompt -Encoding UTF8
    }
    $forward = @('powershell.exe', '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($PromptFile) { $forward += @('-PromptFile', "`"$((Resolve-Path $PromptFile).Path)`"") }
    if ($SessionId) { $forward += @('-SessionId', $SessionId) }
    if ($Continue) { $forward += '-Continue' }
    if ($Bypass) { $forward += '-Bypass' }
    if ($Claude) { $forward += @('-Claude', "`"$Claude`"") }
    $forward += @('-MaxRuns', $MaxRuns, '-FallbackWaitMinutes', $FallbackWaitMinutes, '-StartDelaySeconds', $StartDelaySeconds)

    # WMI creates the process itself, so it is not a child of this shell and not in its job object.
    $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
        CommandLine      = $forward -join ' '
        CurrentDirectory = $projectDir
    }
    if ($created.ReturnValue -ne 0) { throw "Win32_Process.Create failed with code $($created.ReturnValue)." }
    Write-Host "Started detached runner (pid $($created.ProcessId)). Logs: $logDir"
    return
}

$logFile = Join-Path $logDir ("run-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))

# No double quotes in here: Windows PowerShell mangles them when passing arguments to native programs.
$autonomy = @"
You are running unattended in a headless loop. Nobody is watching and nobody can answer questions.
- Never ask the user anything and never wait for confirmation. Whenever you would ask, offer options or propose a plan for approval, pick the recommended or most conventional option yourself, state the decision in one line and keep working.
- If a tool call is denied by the permission system, do not stop: find a safer way to reach the same goal, or skip that step, note it, and continue with the rest.
- Do not git push. Do not delete or rename existing assets.
- Work through as much of the task as you can in each turn.
- When the whole task is finished and verified, end your final message with a line containing only $doneMarker. If you stop for any other reason, list what is left.
"@

Add-Type -Namespace Win32 -Name Power -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);
'@

function Write-Log([string]$message) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $message
    Write-Host $line
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

function Find-Claude {
    if ($Claude) { return $Claude }
    $cmd = Get-Command claude -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $extensions = Get-ChildItem "$env:USERPROFILE\.vscode\extensions" -Directory -Filter 'anthropic.claude-code-*' -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Name -replace '^anthropic\.claude-code-([\d.]+).*$', '$1') } -Descending
    foreach ($dir in $extensions) {
        $exe = Join-Path $dir.FullName 'resources\native-binary\claude.exe'
        if (Test-Path $exe) { return $exe }
    }
    throw 'claude.exe not found. Pass -Claude <path>.'
}

# Reset time from a limit message: "...limit reached|1730000000", "...resets 3pm (Europe/Vienna)"
# or, for weekly limits, "...resets Nov 2, 3pm".
function Get-ResetTime([string]$text) {
    if ($text -match '\|(\d{10})\b') {
        return [DateTimeOffset]::FromUnixTimeSeconds([long]$Matches[1]).LocalDateTime
    }
    if ($text -match '(?i)resets?\s+(?:on\s+)?(?:(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+(\d{1,2}),?\s+)?(?:at\s+)?(\d{1,2})(?::(\d{2}))?\s*(am|pm)?') {
        $hour = [int]$Matches[3]
        $minute = if ($Matches[4]) { [int]$Matches[4] } else { 0 }
        if ($Matches[5]) {
            $hour = $hour % 12
            if ($Matches[5] -ieq 'pm') { $hour += 12 }
        }
        $day = (Get-Date).Date
        if ($Matches[1]) {
            $month = 'janfebmaraprmayjunjulaugsepoctnovdec'.IndexOf($Matches[1].ToLower()) / 3 + 1
            $day = Get-Date -Year $day.Year -Month $month -Day ([int]$Matches[2]) -Hour 0 -Minute 0 -Second 0 -Millisecond 0
            if ($day -lt (Get-Date).Date) { $day = $day.AddYears(1) }
        }
        $time = $day.AddHours($hour).AddMinutes($minute)
        if ($time -le (Get-Date)) { $time = $time.AddDays(1) }
        return $time
    }
    return $null
}

function Wait-Until([datetime]$time) {
    Write-Log ("Waiting until {0:yyyy-MM-dd HH:mm}" -f $time)
    while ((Get-Date) -lt $time) {
        $left = $time - (Get-Date)
        Write-Host ("  {0}h {1:00}m left" -f [int][Math]::Floor($left.TotalHours), $left.Minutes)
        Start-Sleep -Seconds ([Math]::Max(1, [Math]::Min(600, [int][Math]::Ceiling($left.TotalSeconds))))
    }
}

function Invoke-Claude([string]$exe, [string]$text) {
    $cliArgs = @('-p', ($text -replace '"', "'"),
                 '--output-format', 'json',
                 '--append-system-prompt', $autonomy,
                 '--disallowedTools', 'AskUserQuestion')
    if ($Bypass) { $cliArgs += '--dangerously-skip-permissions' }
    else { $cliArgs += @('--permission-mode', 'auto') }
    if ($script:SessionId) { $cliArgs += @('--resume', $script:SessionId) }
    elseif ($Continue) { $cliArgs += '--continue' }

    Push-Location $projectDir
    try {
        $raw = (& $exe @cliArgs 2>&1 | ForEach-Object { "$_" }) -join "`n"
        $exitCode = $LASTEXITCODE
    }
    finally { Pop-Location }

    $json = $null
    $jsonLine = $raw -split "`n" | Where-Object { $_.TrimStart().StartsWith('{') } | Select-Object -Last 1
    if ($jsonLine) { try { $json = $jsonLine | ConvertFrom-Json } catch { } }

    [pscustomobject]@{
        Raw    = $raw
        Json   = $json
        Failed = ($exitCode -ne 0) -or (-not $json) -or $json.is_error
    }
}

$exe = Find-Claude
Write-Log "Claude: $exe"
Write-Log "Log:    $logFile"
[Win32.Power]::SetThreadExecutionState([uint32]2147483649) | Out-Null  # ES_CONTINUOUS | ES_SYSTEM_REQUIRED: no sleep while waiting

try {
    if ($StartDelaySeconds -gt 0) { Wait-Until (Get-Date).AddSeconds($StartDelaySeconds) }
    $text = if ($PromptFile) { Get-Content -Raw -Encoding UTF8 $PromptFile } else { $Prompt }
    $runs = 0
    $errors = 0
    $done = $false
    while ($runs -lt $MaxRuns) {
        $runs++
        Write-Log ("Run $runs/$MaxRuns" + $(if ($SessionId) { " (session $SessionId)" } else { '' }))
        $result = Invoke-Claude $exe $text
        if ($result.Json -and $result.Json.session_id) { $SessionId = $result.Json.session_id }

        if (-not $result.Failed) {
            $errors = 0
            $answer = [string]$result.Json.result
            Add-Content -Path $logFile -Value $answer -Encoding UTF8
            Write-Host ($answer.Substring([Math]::Max(0, $answer.Length - 1500)))
            if ($answer -match $doneMarker) {
                Write-Log 'Claude reports the task is complete.'
                $done = $true
                break
            }
            $text = 'Continue with the task from where you stopped. Do not ask anything; take the recommended action yourself.'
            continue
        }

        Add-Content -Path $logFile -Value $result.Raw -Encoding UTF8
        if ($result.Raw -match '(?i)usage limit|limit reached|hit your (usage )?limit|rate.?limit|out of (extra )?usage|resets?\s+(on\s+|at\s+)?[a-z]{0,9}\.?\s*\d') {
            $runs--  # the turn didn't run; resend the same prompt after the reset
            $reset = Get-ResetTime $result.Raw
            if ($reset) { $reset = $reset.AddMinutes(2) }
            else { $reset = (Get-Date).AddMinutes($FallbackWaitMinutes) }
            Write-Log 'Usage limit reached.'
            Wait-Until $reset
            continue
        }

        $errors++
        Write-Log "Run failed ($errors in a row): $($result.Raw.Substring(0, [Math]::Min(500, $result.Raw.Length)))"
        if ($errors -ge 5) {
            Write-Log 'Giving up after 5 consecutive failures.'
            $done = $true
            break
        }
        Wait-Until (Get-Date).AddMinutes(5 * $errors)
    }
    if (-not $done) { Write-Log "Stopped after $MaxRuns runs." }
    if ($SessionId) { Write-Log "Open the session with: claude --resume $SessionId" }
}
finally {
    [Win32.Power]::SetThreadExecutionState([uint32]2147483648) | Out-Null  # ES_CONTINUOUS: allow sleep again
}
