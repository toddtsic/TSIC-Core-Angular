# ============================================================================
# Pull-Deploy.ps1 - Run ON TSIC-PHOENIX: fetch a production package from
#                   TSIC-OVH-DEV and stage it, exactly as the SEDONA push did
# ============================================================================
# Lives in the repo at scripts\TSICDev-Ops\Pull-Deploy.ps1 and on PHOENIX at
# E:\TSIC-Incoming\Pull-Deploy.ps1 -- deliberately NOT in IIS-Config-Prod, so
# that folder receives exactly the four files the SEDONA push puts there. Each
# package carries the current copy; -Stage refreshes E:\TSIC-Incoming\ from it.
#
# Two separate, explicit steps (elevated PowerShell on TSIC-PHOENIX, from
# E:\TSIC-Incoming):
#
#   1. FETCH   .\Pull-Deploy.ps1
#      Downloads the package OVH's latest.txt names (outbound sftp, key login,
#      account 'phoenix', read-only) into E:\TSIC-Incoming\<stamp>\, checks its
#      SHA-256, unpacks it, and checks both payloads. E:\Websites is NOT touched.
#
#   2. STAGE   .\Pull-Deploy.ps1 -Stage <stamp>
#      Does what 1-Build-And-Deploy-Prod.ps1 (SEDONA) Steps 3-4 do to this box:
#        - mirrors the payload into E:\Websites\claude-api-STAGING and
#          claude-app-STAGING (Assert-TsicSafeStaging, the ONE shared exclusion
#          list, robocopy, Test-TsicPayload on what landed)
#        - copies the four deployment scripts into E:\Websites\IIS-Config-Prod
#          (length-checked, as SEDONA does)
#        - writes the same Go.ps1 into BOTH staging folders, carrying this
#          build's scope
#      Live folders are untouched. Then, as always:
#        cd E:\Websites\claude-api-STAGING ; .\Go.ps1
#
# Fallback: RDP to SEDONA and run 1-Build-And-Deploy-Prod.ps1. It mirrors over
# the same staging folders and rewrites the same Go.ps1; nothing here is
# needed for that path to work.
#
# The shared helpers (_deploy-common.ps1, _config.ps1) are loaded FROM THE
# PACKAGE, i.e. from the commit being deployed -- exactly what SEDONA used,
# since it ran them from the same checkout it built.
#
# ASCII only: Windows PowerShell 5.1 reads BOM-less files as ANSI.
# ============================================================================

#Requires -RunAsAdministrator

param(
    [string]$Stage,
    # TEST ONLY - rehearsal on TSIC-OVH-DEV. Never pass this on PHOENIX. It changes
    # exactly three things: "downloads" are file copies from C:\TSIC\Phoenix\deploy
    # instead of sftp; TSIC-Incoming, the -STAGING folders and IIS-Config-Prod are
    # created under this folder instead of E:\; and the prod _config.ps1 is loaded
    # tolerating the missing E: drive. Go.ps1's text is unchanged (it names E:\).
    [string]$RehearsalRoot
)

$ErrorActionPreference = 'Stop'

$OvhHost     = '15.204.47.57'
$OvhUser     = 'phoenix'
$SshKey      = 'C:\TSIC\ssh\phoenix-ovh'
$KnownHosts  = 'C:\TSIC\ssh\known_hosts'
$IncomingDir = 'E:\TSIC-Incoming'
$KeepIncoming = 3

$DeployRoot  = 'E:\Websites\IIS-Config-Prod'
$RecycleCmd  = 'E:\Websites\IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1'

if ($RehearsalRoot) {
    if ($env:COMPUTERNAME -eq 'TSIC-PHOENIX') { throw "-RehearsalRoot is for TSIC-OVH-DEV only." }
    $IncomingDir = Join-Path $RehearsalRoot 'TSIC-Incoming'
    $DeployRoot  = Join-Path $RehearsalRoot 'Websites\IIS-Config-Prod'
    Write-Host "REHEARSAL under $RehearsalRoot - sftp replaced by local copies; nothing leaves this box." -ForegroundColor Magenta
}

$StartedAt = Get-Date
$ok = $false

function Read-BuildInfo {
    param([string]$PkgDir)
    $f = Join-Path $PkgDir 'build-info.json'
    if (-not (Test-Path -LiteralPath $f)) { throw "no build-info.json in $PkgDir - not a TSIC production package." }
    $bi = Get-Content -LiteralPath $f -Raw | ConvertFrom-Json
    if ($bi.comparison) { throw "this is a COMPARISON build - never deployable." }
    if (-not $bi.api -and -not $bi.angular) { throw "build-info says neither API nor Angular was built." }
    return $bi
}

# Loads _config.ps1 + _deploy-common.ps1 from the package into the caller's scope.
function Get-PackageHelpers {
    param([string]$PkgDir)
    $cfg = Join-Path $PkgDir 'IIS-Config-Prod\_config.ps1'
    $com = Join-Path $PkgDir 'IIS-Config-Prod\_deploy-common.ps1'
    foreach ($f in $cfg, $com) { if (-not (Test-Path -LiteralPath $f)) { throw "package is missing $f" } }
    return @($cfg, $com)
}

# Runs sftp with a batch file. Native stderr must not become a terminating error
# under Windows PowerShell 5.1, so EAP is relaxed for the call only.
function Invoke-OvhSftp {
    param([string[]]$Commands)
    if ($RehearsalRoot) {
        foreach ($c in $Commands) {
            if ($c -notmatch '^get /deploy/(\S+) "(.+)"$') { throw "rehearsal cannot simulate sftp command: $c" }
            Copy-Item -LiteralPath (Join-Path 'C:\TSIC\Phoenix\deploy' $Matches[1]) -Destination $Matches[2].Replace('/','\')
        }
        return
    }
    $batch = Join-Path $env:TEMP "pull-deploy-sftp-$PID.txt"
    Set-Content -LiteralPath $batch -Value $Commands -Encoding ASCII
    try {
        $ErrorActionPreference = 'Continue'
        & sftp -i $SshKey -o "UserKnownHostsFile=$KnownHosts" -o StrictHostKeyChecking=yes -o BatchMode=yes -b $batch "$OvhUser@$OvhHost"
        $code = $LASTEXITCODE
    } finally {
        Remove-Item -LiteralPath $batch -Force -ErrorAction SilentlyContinue
    }
    if ($code -ne 0) { throw "sftp failed (exit $code)." }
}

try {

if (-not $Stage) {
    # ======================================================================
    # FETCH
    # ======================================================================
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  PULL - fetch from TSIC-OVH-DEV" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    New-Item -ItemType Directory -Path $IncomingDir -Force | Out-Null
    $tmp = Join-Path $IncomingDir '_download'
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
    New-Item -ItemType Directory -Path $tmp | Out-Null

    Write-Host "Step 1: Reading latest.txt from OVH..." -ForegroundColor Yellow
    Invoke-OvhSftp @("get /deploy/latest.txt `"$($tmp.Replace('\','/'))/latest.txt`"")
    $name = (Get-Content -LiteralPath (Join-Path $tmp 'latest.txt') -Raw).Trim()
    if ($name -notmatch '^tsic-prod-(v\d{6}\.\d{4}\.[0-9a-f]+)\.zip$') { throw "latest.txt names '$name' - not a production package name." }
    $stamp = $Matches[1]
    Write-Host "  Latest package: $name" -ForegroundColor Green

    $dest = Join-Path $IncomingDir $stamp
    if (Test-Path -LiteralPath $dest) { throw "$dest already exists - this package was already fetched. Stage it with: .\Pull-Deploy.ps1 -Stage $stamp" }

    Write-Host ""
    Write-Host "Step 2: Downloading package + checksum..." -ForegroundColor Yellow
    $t = $tmp.Replace('\','/')
    Invoke-OvhSftp @("get /deploy/$name `"$t/$name`"", "get /deploy/$name.sha256 `"$t/$name.sha256`"")
    $want = ((Get-Content -LiteralPath (Join-Path $tmp "$name.sha256") -Raw).Trim() -split '\s+')[0]
    $have = (Get-FileHash -LiteralPath (Join-Path $tmp $name) -Algorithm SHA256).Hash
    if ($have -ne $want) { throw "SHA-256 MISMATCH - expected $want, downloaded file is $have. Nothing was unpacked." }
    Write-Host "  SHA-256 verified: $have" -ForegroundColor Green

    Write-Host ""
    Write-Host "Step 3: Unpacking into $dest..." -ForegroundColor Yellow
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pkg = Join-Path $dest 'package'
    New-Item -ItemType Directory -Path $dest | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $tmp $name), $pkg)
    Move-Item -LiteralPath (Join-Path $tmp $name) -Destination $dest
    Move-Item -LiteralPath (Join-Path $tmp "$name.sha256") -Destination $dest
    Remove-Item -LiteralPath $tmp -Recurse -Force

    $bi = Read-BuildInfo -PkgDir $pkg
    foreach ($h in (Get-PackageHelpers -PkgDir $pkg)) {
    if ($RehearsalRoot) { $ErrorActionPreference = 'Continue'; . $h 2>$null; $ErrorActionPreference = 'Stop' } else { . $h }
}

    Write-Host ""
    Write-Host "Step 4: Checking the payloads..." -ForegroundColor Yellow
    foreach ($s in @(
        if ($bi.api)     { $Config.ApiSiteName }
        if ($bi.angular) { $Config.AngularSiteName }
    )) {
        $p = Join-Path $pkg $s
        $bad = Test-TsicPayload -Path $p -Site $s
        if ($bad) { throw "$s payload is not deployable - $bad" }
        $m = Get-TsicManifest -Path $p
        if ($m.environment -ne 'Production') { throw "$s manifest says environment '$($m.environment)', expected Production." }
        if ($m.buildStamp -ne $bi.buildStamp -or $m.gitHash -ne $bi.gitHash) { throw "$s manifest ($($m.buildStamp) / $($m.gitHash)) does not match build-info ($($bi.buildStamp) / $($bi.gitHash))." }
        Write-Host "  $s : $($m.fileCount) files, $($m.buildStamp), git $($m.gitHash) - OK" -ForegroundColor Green
    }

    # Keep the newest few fetched packages.
    $old = @(Get-ChildItem -LiteralPath $IncomingDir -Directory |
             Where-Object { $_.Name -match '^v\d{6}\.\d{4}\.[0-9a-f]+$' -and $_.Name -ne $stamp } |
             Sort-Object LastWriteTime -Descending | Select-Object -Skip ($KeepIncoming - 1))
    foreach ($o in $old) { Remove-Item -LiteralPath $o.FullName -Recurse -Force; Write-Host "  Pruned old package: $($o.Name)" -ForegroundColor DarkGray }

    $scope = if ($bi.api -and $bi.angular) { 'API + Angular' } elseif ($bi.api) { 'API only' } else { 'Angular only' }
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "FETCHED AND VERIFIED - E:\Websites was not touched." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Build:  $($bi.buildStamp)  git $($bi.gitHash)  ($scope)  built on $($bi.builtOn)" -ForegroundColor Green
    Write-Host ""
    Write-Host "  NEXT (fills the staging folders; live is untouched):" -ForegroundColor Yellow
    Write-Host "    .\Pull-Deploy.ps1 -Stage $stamp" -ForegroundColor White
    Write-Host ""
    $ok = $true
    exit 0
}

# ==========================================================================
# STAGE
# ==========================================================================
if ($Stage -notmatch '^v\d{6}\.\d{4}\.[0-9a-f]+$') { throw "-Stage takes a build stamp like v261017.1450.b5a07642d, not '$Stage'." }
$pkg = Join-Path (Join-Path $IncomingDir $Stage) 'package'
if (-not (Test-Path -LiteralPath $pkg)) { throw "$pkg does not exist - fetch it first: .\Pull-Deploy.ps1" }

$bi = Read-BuildInfo -PkgDir $pkg
foreach ($h in (Get-PackageHelpers -PkgDir $pkg)) {
    if ($RehearsalRoot) { $ErrorActionPreference = 'Continue'; . $h 2>$null; $ErrorActionPreference = 'Stop' } else { . $h }
}
if ($bi.buildStamp -ne $Stage) { throw "build-info stamp $($bi.buildStamp) does not match -Stage $Stage." }

$ApiSite        = $Config.ApiSiteName          # claude-api
$AngularSite    = $Config.AngularSiteName      # claude-app
$ApiStaging     = $Config.ApiStagingPath       # E:\Websites\claude-api-STAGING
$AngularStaging = $Config.AngularStagingPath   # E:\Websites\claude-app-STAGING
if ($RehearsalRoot) {
    $ApiStaging     = Join-Path $RehearsalRoot "Websites\$ApiSite-STAGING"
    $AngularStaging = Join-Path $RehearsalRoot "Websites\$AngularSite-STAGING"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  STAGE $Stage  (git $($bi.gitHash))" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- SEDONA Step 3: stage into the -STAGING folders --------------------------
Write-Host "Step 1: Staging into E:\Websites\*-STAGING..." -ForegroundColor Yellow
foreach ($s in @(
    if ($bi.api)     { @{ Site = $ApiSite;     Src = (Join-Path $pkg $ApiSite);     Staging = $ApiStaging } }
    if ($bi.angular) { @{ Site = $AngularSite; Src = (Join-Path $pkg $AngularSite); Staging = $AngularStaging } }
)) {
    Assert-TsicSafeStaging -Site $s.Site -Path $s.Staging | Out-Null

    $bad = Test-TsicPayload -Path $s.Src -Site $s.Site
    if ($bad) { throw "$($s.Site) package payload is not deployable - $bad" }

    $ex = Get-TsicExclusions -Site $s.Site
    if (!(Test-Path $s.Staging)) { New-Item -ItemType Directory -Path $s.Staging -Force | Out-Null }

    $exit = Invoke-TsicRobocopy -Source $s.Src -Dest $s.Staging -ExcludeDirs $ex.Dirs -ExcludeFiles $ex.Files -Quiet
    if ($exit -ge 8) { throw "staging $($s.Site) failed (robocopy exit $exit). Live folders are untouched." }

    $bad = Test-TsicPayload -Path $s.Staging -Site $s.Site
    if ($bad) { throw "$($s.Site) staging payload is not deployable - $bad. Live folders are untouched." }

    Write-Host "  $($s.Site) staged and verified ($Stage)." -ForegroundColor Green
}
Write-Host ""

# --- SEDONA Step 4: deployment scripts + Go.ps1 -------------------------------
Write-Host "Step 2: Updating deployment scripts..." -ForegroundColor Yellow
$scriptPushes = @(
    @{ From = 'IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1'; To = (Join-Path $DeployRoot 'Deployment') }
    @{ From = 'IIS-Config-Prod\Deployment\Rollback-Deploy.ps1';      To = (Join-Path $DeployRoot 'Deployment') }
    @{ From = 'IIS-Config-Prod\_config.ps1';                         To = $DeployRoot }
    @{ From = 'IIS-Config-Prod\_deploy-common.ps1';                  To = $DeployRoot }
)
# This script itself: refreshed in E:\TSIC-Incoming (never IIS-Config-Prod).
if (Test-Path -LiteralPath (Join-Path $pkg 'Pull-Deploy.ps1')) { $scriptPushes += @{ From = 'Pull-Deploy.ps1'; To = $IncomingDir } }
foreach ($push in $scriptPushes) {
    $src  = Join-Path $pkg $push.From
    $name = Split-Path $src -Leaf
    if (-not (Test-Path -LiteralPath $src)) { throw "$name not found in the package." }
    if (-not (Test-Path $push.To)) { New-Item -ItemType Directory -Path $push.To -Force | Out-Null }
    Copy-Item -LiteralPath $src "$($push.To)\" -Force
    $landed = Join-Path $push.To $name
    if (!(Test-Path $landed) -or (Get-Item $landed).Length -ne (Get-Item $src).Length) {
        throw "$name did not land intact in $($push.To). Recycle-After-Deploy.ps1 will not run without it."
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

$scopeText = 'API + Angular'
if     (-not $bi.api)     { $scopeText = 'Angular only' }
elseif (-not $bi.angular) { $scopeText = 'API only' }
$goFolder = $ApiStaging
if (-not $bi.api) { $goFolder = $AngularStaging }

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STAGED - nothing is live yet." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build: $Stage  git $($bi.gitHash)  built on $($bi.builtOn)" -ForegroundColor Green
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
    Write-Host "  Live folders were NOT modified." -ForegroundColor Yellow
    Write-Host ""
    exit 1
} finally {
    if (-not $ok) { Write-Host "  Ended without completing: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Red }
}
