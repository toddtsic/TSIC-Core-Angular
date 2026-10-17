# ============================================================================
# Stage-PulledDevFiles.ps1 - Run ON TSIC-PHOENIX: put the package that
#                            Pull-DevFiles.ps1 pulled into the -STAGING folders
# ============================================================================
# On PHOENIX, elevated, from E:\TSIC-Incoming:
#
#   .\Pull-DevFiles.ps1          download + verify     (E:\Websites untouched)
#   .\Stage-PulledDevFiles.ps1   fill the -STAGING folders (live untouched)
#   cd E:\Websites\claude-api-STAGING ; .\Go.ps1      as always
#
# No parameters: it stages the build named in E:\TSIC-Incoming\last-pulled.txt,
# which Pull-DevFiles.ps1 writes only after that build verified.
#
# What it does = what 1-Build-And-Deploy-Prod.ps1 (SEDONA) Steps 3-4 do to this
# box, using the shared helpers FROM THE PACKAGE (the commit being deployed):
#   1. E:\Websites\claude-api-STAGING / claude-app-STAGING: Assert-TsicSafeStaging,
#      robocopy with the ONE shared exclusion list, Test-TsicPayload on what landed
#   2. the four deployment scripts into E:\Websites\IIS-Config-Prod (length-checked)
#   3. the same Go.ps1 into BOTH staging folders, carrying this build's scope
# Also refreshes Pull-DevFiles.ps1 and this script in E:\TSIC-Incoming from the
# package. Live folders are never touched; Go.ps1 does that, as always.
#
# Fallback: RDP to SEDONA, 1-Build-And-Deploy-Prod.ps1 -- it mirrors over the
# same staging folders and rewrites the same Go.ps1.
#
# ASCII only: Windows PowerShell 5.1 reads BOM-less files as ANSI.
# ============================================================================

#Requires -RunAsAdministrator

param(
    # TEST ONLY - rehearsal on TSIC-OVH-DEV, never on PHOENIX: E:\TSIC-Incoming,
    # the -STAGING folders and IIS-Config-Prod are created under this folder,
    # and the prod _config.ps1 is loaded tolerating the missing E: drive.
    # Go.ps1's text is unchanged (it names E:\).
    [string]$RehearsalRoot
)

$ErrorActionPreference = 'Stop'

$IncomingDir = 'E:\TSIC-Incoming'
$DeployRoot  = 'E:\Websites\IIS-Config-Prod'
$RecycleCmd  = 'E:\Websites\IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1'

if ($RehearsalRoot) {
    if ($env:COMPUTERNAME -eq 'TSIC-PHOENIX') { throw "-RehearsalRoot is for TSIC-OVH-DEV only." }
    $IncomingDir = Join-Path $RehearsalRoot 'TSIC-Incoming'
    $DeployRoot  = Join-Path $RehearsalRoot 'Websites\IIS-Config-Prod'
    Write-Host "REHEARSAL under $RehearsalRoot - nothing leaves this box." -ForegroundColor Magenta
}

$ok = $false

try {
    $lp = Join-Path $IncomingDir 'last-pulled.txt'
    if (-not (Test-Path -LiteralPath $lp)) { throw "nothing has been pulled yet - run .\Pull-DevFiles.ps1 first." }
    $stamp = (Get-Content -LiteralPath $lp -Raw).Trim()
    if ($stamp -notmatch '^v\d{6}\.\d{4}\.[0-9a-f]+$') { throw "last-pulled.txt holds '$stamp' - not a build stamp." }
    $pkg = Join-Path (Join-Path $IncomingDir $stamp) 'package'
    if (-not (Test-Path -LiteralPath $pkg)) { throw "$pkg is gone - run .\Pull-DevFiles.ps1 again." }

    $biFile = Join-Path $pkg 'build-info.json'
    if (-not (Test-Path -LiteralPath $biFile)) { throw "no build-info.json - not a TSIC production package." }
    $bi = Get-Content -LiteralPath $biFile -Raw | ConvertFrom-Json
    if ($bi.comparison) { throw "this is a COMPARISON build - never deployable." }
    if (-not $bi.api -and -not $bi.angular) { throw "build-info says neither API nor Angular was built." }
    if ($bi.buildStamp -ne $stamp) { throw "build-info stamp $($bi.buildStamp) does not match last-pulled.txt ($stamp)." }

    foreach ($h in @((Join-Path $pkg 'IIS-Config-Prod\_config.ps1'), (Join-Path $pkg 'IIS-Config-Prod\_deploy-common.ps1'))) {
        if (-not (Test-Path -LiteralPath $h)) { throw "package is missing $h" }
        if ($RehearsalRoot) { $ErrorActionPreference = 'Continue'; . $h 2>$null; $ErrorActionPreference = 'Stop' } else { . $h }
    }

    $ApiSite        = $Config.ApiSiteName          # claude-api
    $AngularSite    = $Config.AngularSiteName      # claude-app
    $ApiStaging     = $Config.ApiStagingPath       # E:\Websites\claude-api-STAGING
    $AngularStaging = $Config.AngularStagingPath   # E:\Websites\claude-app-STAGING
    if ($RehearsalRoot) {
        $ApiStaging     = Join-Path $RehearsalRoot "Websites\$ApiSite-STAGING"
        $AngularStaging = Join-Path $RehearsalRoot "Websites\$AngularSite-STAGING"
    }

    $scopeText = 'API + Angular'
    if     (-not $bi.api)     { $scopeText = 'Angular only' }
    elseif (-not $bi.angular) { $scopeText = 'API only' }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  STAGE $stamp" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  git $($bi.gitHash)  ($scopeText)  built on $($bi.builtOn) at $($bi.builtUtc)" -ForegroundColor White
    Write-Host ""

    # --- SEDONA Step 3: stage into the -STAGING folders ------------------------
    Write-Host "Step 1: Staging into the -STAGING folders..." -ForegroundColor Yellow
    foreach ($s in @(
        if ($bi.api)     { @{ Site = $ApiSite;     Src = (Join-Path $pkg $ApiSite);     Staging = $ApiStaging } }
        if ($bi.angular) { @{ Site = $AngularSite; Src = (Join-Path $pkg $AngularSite); Staging = $AngularStaging } }
    )) {
        Assert-TsicSafeStaging -Site $s.Site -Path $s.Staging | Out-Null

        $bad = Test-TsicPayload -Path $s.Src -Site $s.Site
        if ($bad) { throw "$($s.Site) pulled payload is not deployable - $bad" }

        $ex = Get-TsicExclusions -Site $s.Site
        if (!(Test-Path $s.Staging)) { New-Item -ItemType Directory -Path $s.Staging -Force | Out-Null }

        $exit = Invoke-TsicRobocopy -Source $s.Src -Dest $s.Staging -ExcludeDirs $ex.Dirs -ExcludeFiles $ex.Files -Quiet
        if ($exit -ge 8) { throw "staging $($s.Site) failed (robocopy exit $exit)." }

        $bad = Test-TsicPayload -Path $s.Staging -Site $s.Site
        if ($bad) { throw "$($s.Site) staging payload is not deployable - $bad" }

        Write-Host "  $($s.Site) staged and verified ($stamp)." -ForegroundColor Green
    }
    Write-Host ""

    # --- SEDONA Step 4: deployment scripts + Go.ps1 ------------------------------
    Write-Host "Step 2: Updating deployment scripts..." -ForegroundColor Yellow
    $scriptPushes = @(
        @{ From = 'IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1'; To = (Join-Path $DeployRoot 'Deployment') }
        @{ From = 'IIS-Config-Prod\Deployment\Rollback-Deploy.ps1';      To = (Join-Path $DeployRoot 'Deployment') }
        @{ From = 'IIS-Config-Prod\_config.ps1';                         To = $DeployRoot }
        @{ From = 'IIS-Config-Prod\_deploy-common.ps1';                  To = $DeployRoot }
    )
    # The two pull scripts themselves: refreshed in E:\TSIC-Incoming (never IIS-Config-Prod).
    foreach ($self in 'Pull-DevFiles.ps1', 'Stage-PulledDevFiles.ps1') {
        if (Test-Path -LiteralPath (Join-Path $pkg $self)) { $scriptPushes += @{ From = $self; To = $IncomingDir } }
    }
    foreach ($push in $scriptPushes) {
        $src  = Join-Path $pkg $push.From
        $name = Split-Path $src -Leaf
        if (-not (Test-Path -LiteralPath $src)) { throw "$name not found in the package." }
        if (-not (Test-Path $push.To)) { New-Item -ItemType Directory -Path $push.To -Force | Out-Null }
        Copy-Item -LiteralPath $src "$($push.To)\" -Force
        $landed = Join-Path $push.To $name
        if (!(Test-Path $landed) -or (Get-Item $landed).Length -ne (Get-Item $src).Length) {
            throw "$name did not land intact in $($push.To)."
        }
        Write-Host "  Updated: $name" -ForegroundColor Green
    }

    # Go.ps1: same body SEDONA writes, same scope rule, into BOTH staging folders.
    $goArgs = ''
    if     (-not $bi.api)     { $goArgs = ' -SkipApi' }
    elseif (-not $bi.angular) { $goArgs = ' -SkipAngular' }
    $goBody = "& '$RecycleCmd'$goArgs"
    foreach ($stagingDir in @($ApiStaging, $AngularStaging)) {
        if (Test-Path $stagingDir) {
            Set-Content (Join-Path $stagingDir 'Go.ps1') $goBody -Encoding UTF8
        }
    }
    Write-Host "  Go.ps1 (in both staging folders) runs: $goBody" -ForegroundColor Green
    Write-Host ""

    $goFolder = $ApiStaging
    if (-not $bi.api) { $goFolder = $AngularStaging }

    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "STAGED - nothing is live yet." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Build: $stamp  git $($bi.gitHash)  built on $($bi.builtOn)" -ForegroundColor Green
    Write-Host ""
    Write-Host "  NEXT, as always - run it ONCE:" -ForegroundColor Yellow
    Write-Host "    cd $goFolder" -ForegroundColor White
    Write-Host "    .\Go.ps1" -ForegroundColor White
    Write-Host ""
    Write-Host "  That ONE run deploys everything this build staged ($scopeText)." -ForegroundColor Yellow
    Write-Host "  If it fails, the rollback is already on the box:" -ForegroundColor DarkGray
    Write-Host "    E:\Websites\IIS-Config-Prod\Deployment\Rollback-Deploy.ps1 -List" -ForegroundColor DarkGray
    Write-Host ""
    $ok = $true
    exit 0
} catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Live folders were NOT touched." -ForegroundColor Yellow
    Write-Host ""
    exit 1
} finally {
    if (-not $ok) { Write-Host "  Ended without completing: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Red }
}
