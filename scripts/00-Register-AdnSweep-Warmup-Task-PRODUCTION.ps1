<#
.SYNOPSIS
    *** PRODUCTION SETUP — RUN ONCE PER PRODUCTION HOST ***

    Registers (or removes) a Windows Scheduled Task on the PRODUCTION machine that warms
    claude-api at 04:30 daily, so the in-process AdnSweepBackgroundService timer is alive
    when it needs to fire at 05:00.

.DESCRIPTION
    *** PRODUCTION HOST SETUP ***
    Run this script once on every machine that hosts claude-api in PRODUCTION
    (currently TSIC-PHOENIX; rerun on any future replacement / DR / second prod host).

    -----------------------------------------------------------------------------
    Background:
      claude-api hosts a .NET 8 BackgroundService (AdnSweepBackgroundService) that runs
      the ADN reconciliation sweep daily at 05:00 local. The timer lives in the IIS worker
      process — if the worker is dead at 05:00, the sweep silently misses that day.

      claude-api currently has light-to-zero overnight traffic, so IIS's default 20-minute
      idle timeout kills the worker overnight. This Task fires at 04:30 and hits an
      anonymous URL on claude-api. IIS spins up the worker on the request, the .NET app
      initializes, the BackgroundService computes nextRun = today 05:00 (delay = 30 min),
      and at 05:00 the sweep runs as designed.

      The URL pinged returns 404 in Production (Swagger is wired only in Development), but
      that's sufficient — what matters is that a request reaches the worker and forces
      app initialization. Status code is irrelevant.

    Why not change IIS pool settings instead:
      Solving "scheduling reliability" by setting AlwaysRunning + IdleTimeout=0 + Preload
      keeps the worker immortal as a side effect, but it's the wrong layer. A warmup task
      leaves IIS defaults intact (idle recycle, scheduled recycle, OnDemand start) and
      keeps the host process behavior IIS was designed for.

      Idempotent: safely re-runnable. Existing task with the same name is removed first.
    -----------------------------------------------------------------------------

.PARAMETER ApiHealthUrl
    URL to GET at 04:30. Default points at claude-api in PRODUCTION. Override only when
    setting up the warmup on a different production host (DR / replacement / second prod).

.PARAMETER TaskName
    Scheduled Task name. Default 'TSIC-ClaudeApi-Warmup'.

.PARAMETER At
    Local time of day to fire. Default '4:30AM'. Must give the BackgroundService enough
    runway to start AND fall inside its 60-minute grace window before 05:00 — keep this
    between 04:01 and 04:59.

.PARAMETER Remove
    Unregister the Task and exit. Use to roll back this script's effects.

.PARAMETER DryRun
    Print what would be done without actually registering / removing.

.EXAMPLE
    # Standard install on TSIC-PHOENIX
    .\00-Register-AdnSweep-Warmup-Task-PRODUCTION.ps1

.EXAMPLE
    # Roll back
    .\00-Register-AdnSweep-Warmup-Task-PRODUCTION.ps1 -Remove

.EXAMPLE
    # Stand up on a different production host
    .\00-Register-AdnSweep-Warmup-Task-PRODUCTION.ps1 -ApiHealthUrl 'https://new-prod-host/swagger/v1/swagger.json'

.NOTES
    *** PRODUCTION ***
    Run as Administrator. Task runs as NT AUTHORITY\SYSTEM (no stored credentials).
    The script ONLY registers the Task — it does NOT trigger it. Validation is the
    arrival of the [claude-api] AdnSweep digest email after the next 05:00 sweep.
#>
[CmdletBinding()]
param(
    [string]$ApiHealthUrl = 'https://claude-api.teamsportsinfo.com/swagger/v1/swagger.json',
    [string]$TaskName     = 'TSIC-ClaudeApi-Warmup',
    [string]$At           = '4:30AM',
    [switch]$Remove,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Write-Info($msg)  { Write-Host "[INFO]  $msg" -ForegroundColor Cyan }
function Write-Done($msg)  { Write-Host "[OK]    $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "[WARN]  $msg" -ForegroundColor Yellow }
function Write-Prod($msg)  { Write-Host "[PROD]  $msg" -ForegroundColor Magenta }

Write-Host ""
Write-Prod "=========================================================="
Write-Prod " PRODUCTION SETUP — AdnSweep Warmup Scheduled Task"
Write-Prod " Host:   $env:COMPUTERNAME"
Write-Prod " Action: $(if ($Remove) {'REMOVE'} elseif ($DryRun) {'DRY RUN'} else {'REGISTER'})"
Write-Prod "=========================================================="
Write-Host ""

# ── Admin check ─────────────────────────────────────────────────────────────
$isAdmin = ([Security.Principal.WindowsPrincipal]`
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Must run as Administrator. Right-click PowerShell -> Run as administrator."
}

# ── Remove path ─────────────────────────────────────────────────────────────
if ($Remove) {
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $existing) {
        Write-Info "Task '$TaskName' not present — nothing to remove."
        return
    }
    if ($DryRun) {
        Write-Info "DryRun: would Unregister-ScheduledTask -TaskName '$TaskName'"
        return
    }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Done "Removed scheduled task '$TaskName'."
    return
}

# ── Register path ───────────────────────────────────────────────────────────
Write-Info "TaskName: $TaskName"
Write-Info "Trigger:  Daily at $At"
Write-Info "Pings:    $ApiHealthUrl"

# Build the inner command. $ApiHealthUrl is expanded NOW (at registration time) and baked
# into the Task's stored argument string — the inner powershell.exe at task-run time sees
# only a literal URL inside single quotes (no further variable interpolation needed).
# `catch { exit 0 }` swallows any HTTP/cert/DNS failure: warmup is best-effort, and the
# real validation is whether the 05:00 sweep email arrives, not the Task's exit code.
$inner = "try { Invoke-WebRequest -Uri '$ApiHealthUrl' -UseBasicParsing -TimeoutSec 30 | Out-Null } catch { exit 0 }"

$action  = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -Command `"$inner`""

$trigger = New-ScheduledTaskTrigger -Daily -At $At

$principal = New-ScheduledTaskPrincipal `
    -UserId 'NT AUTHORITY\SYSTEM' `
    -LogonType ServiceAccount `
    -RunLevel Highest

$settings  = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -DontStopOnIdleEnd `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 5)

$description = "PRODUCTION: warms claude-api at $At so the in-process " +
               "AdnSweepBackgroundService timer is armed for the 05:00 sweep. " +
               "Registered by scripts\00-Register-AdnSweep-Warmup-Task-PRODUCTION.ps1."

if ($DryRun) {
    Write-Info "DryRun: would unregister any existing '$TaskName' then register fresh."
    Write-Info "Action: powershell.exe -NoProfile -WindowStyle Hidden -Command `"$inner`""
    return
}

# Idempotent: drop existing if present, then register.
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Warn2 "Existing task '$TaskName' found — removing before re-creating."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName    $TaskName `
    -Description $description `
    -Action      $action `
    -Trigger     $trigger `
    -Principal   $principal `
    -Settings    $settings | Out-Null

Write-Done "Registered scheduled task '$TaskName'."

# Read back what was just registered (no execution — just confirmation)
$info = Get-ScheduledTaskInfo -TaskName $TaskName

Write-Host ""
Write-Host "Registration confirmed (task NOT triggered):" -ForegroundColor Cyan
Write-Host ("  TaskName:    {0}" -f $TaskName)
Write-Host ("  Host:        {0}" -f $env:COMPUTERNAME)
Write-Host ("  NextRunTime: {0}" -f $info.NextRunTime)
Write-Host ""
Write-Host "Validation = receipt of the '[claude-api] AdnSweep --- ...' email after the next 05:00 sweep." -ForegroundColor Cyan
Write-Host ""
Write-Host "If the email does not arrive, post-mortem checklist:" -ForegroundColor Cyan
Write-Host "  1. Seq query: @MessageTemplate like '%Sweep finished%' or '%AdnSweepBackgroundService%'  (window 04:25 - 05:10)"
Write-Host "  2. DB:        SELECT TOP 5 * FROM echeck.SweepLog ORDER BY startedAt DESC"
Write-Host "  3. Task log:  Get-ScheduledTaskInfo -TaskName '$TaskName'"
Write-Host "  4. Rollback:  .\00-Register-AdnSweep-Warmup-Task-PRODUCTION.ps1 -Remove"
