# ============================================================================
# Deploy-FromDev.ps1 - Run ON TSIC-PHOENIX: the whole production deploy in one
#                      command -- pull, stage, confirm, Go
# ============================================================================
# On PHOENIX, elevated, from E:\TSIC-Incoming:
#
#   .\Deploy-FromDev.ps1
#
#   1. .\Pull-DevFiles.ps1          download + verify the latest package
#   2. .\Stage-PulledDevFiles.ps1   fill the -STAGING folders
#   3. shows what is about to go live and asks   Deploy ... now? [y/N]
#   4. on "y" only: Go.ps1 from the staging folder -- the SAME Go.ps1 you run by
#      hand, unchanged
#
# Any failure in 1 or 2 stops everything: nothing reaches live. Anything but "y"
# at the prompt stops cleanly with the build staged; run Go.ps1 by hand later
# (cd E:\Websites\claude-api-STAGING ; .\Go.ps1), or this script again.
#
# Steps 1 and 2 stay available on their own, e.g. to pull and stage early and
# Go at a quiet time.
#
# Fallback: RDP to SEDONA, 1-Build-And-Deploy-Prod.ps1, then Go.ps1 as before.
#
# ASCII only: Windows PowerShell 5.1 reads BOM-less files as ANSI.
# ============================================================================

#Requires -RunAsAdministrator

param(
    # TEST ONLY - rehearsal on TSIC-OVH-DEV, never on PHOENIX: passed on to the
    # pull and stage scripts, and Go.ps1 is NOT run -- only shown.
    [string]$RehearsalRoot
)

$ErrorActionPreference = 'Stop'

$IncomingDir = 'E:\TSIC-Incoming'
if ($RehearsalRoot) {
    if ($env:COMPUTERNAME -eq 'TSIC-PHOENIX') { throw "-RehearsalRoot is for TSIC-OVH-DEV only." }
    $IncomingDir = Join-Path $RehearsalRoot 'TSIC-Incoming'
}

$here = $PSScriptRoot
$pull  = Join-Path $here 'Pull-DevFiles.ps1'
$stage = Join-Path $here 'Stage-PulledDevFiles.ps1'
foreach ($s in $pull, $stage) { if (-not (Test-Path -LiteralPath $s)) { Write-Host "MISSING: $s" -ForegroundColor Red; exit 1 } }

$childArgs = @{}
if ($RehearsalRoot) { $childArgs.RehearsalRoot = $RehearsalRoot }

function Stop-Here {
    param([string]$Message, [string]$Color = 'Red')
    Write-Host ""
    Write-Host "STOPPED: $Message" -ForegroundColor $Color
    Write-Host "  Production was NOT changed." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# --- 1. Pull ----------------------------------------------------------------
& $pull @childArgs
if ($LASTEXITCODE -ne 0) { Stop-Here "the pull did not complete (see above)." }

# --- 2. Stage ---------------------------------------------------------------
& $stage @childArgs
if ($LASTEXITCODE -ne 0) { Stop-Here "staging did not complete (see above)." }

# --- 3. What is about to go live -----------------------------------------------
$stamp = (Get-Content -LiteralPath (Join-Path $IncomingDir 'last-pulled.txt') -Raw).Trim()
$bi = Get-Content -LiteralPath (Join-Path $IncomingDir "$stamp\package\build-info.json") -Raw | ConvertFrom-Json
$scope = if ($bi.api -and $bi.angular) { 'API + Angular' } elseif ($bi.api) { 'API only' } else { 'Angular only' }
$stagingDir = if ($bi.api) { 'E:\Websites\claude-api-STAGING' } else { 'E:\Websites\claude-app-STAGING' }
if ($RehearsalRoot) { $stagingDir = $stagingDir.Replace('E:\Websites', "$RehearsalRoot\Websites") }
$subject = if ($bi.PSObject.Properties['gitSubject'] -and $bi.gitSubject) { $bi.gitSubject } else { '(not recorded)' }

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  READY TO GO LIVE" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Build:     $($bi.buildStamp)" -ForegroundColor White
Write-Host "  Commit:    $($bi.gitHash)  $subject" -ForegroundColor White
Write-Host "  Includes:  $scope" -ForegroundColor White
Write-Host "  Built on:  $($bi.builtOn) at $($bi.builtUtc)" -ForegroundColor White
Write-Host ""

$answer = Read-Host "  Deploy $($bi.buildStamp) to production now? [y/N]"
if ($answer -notmatch '^\s*[yY]\s*$') {
    Write-Host ""
    Write-Host "Not deployed. The build is staged; production is unchanged." -ForegroundColor Yellow
    Write-Host "  Later:  cd $stagingDir ; .\Go.ps1    (or run this script again)" -ForegroundColor White
    Write-Host ""
    exit 0
}

# --- 4. Go.ps1, exactly as by hand ------------------------------------------
$go = "$stagingDir\Go.ps1"
if (-not (Test-Path -LiteralPath $go)) { Stop-Here "$go is missing - staging should have written it." }
if ($RehearsalRoot) {
    Write-Host ""
    Write-Host "REHEARSAL: would now run  $go  -- which contains:  $(Get-Content -LiteralPath $go -Raw)" -ForegroundColor Magenta
    exit 0
}

Write-Host ""
Push-Location $stagingDir
try {
    & $go
    $goExit = $LASTEXITCODE
} finally {
    Pop-Location
}
exit $goExit
