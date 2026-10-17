# ============================================================================
# Pull-DevFiles.ps1 - Run ON TSIC-PHOENIX: download the latest production
#                     package from TSIC-OVH-DEV and verify it
# ============================================================================
# On PHOENIX, elevated, from E:\TSIC-Incoming:
#
#   .\Pull-DevFiles.ps1          download + verify     (E:\Websites untouched)
#   .\Stage-PulledDevFiles.ps1   fill the -STAGING folders (live untouched)
#   cd E:\Websites\claude-api-STAGING ; .\Go.ps1      as always
#
# What it does: reads latest.txt from OVH (outbound sftp, key login, read-only
# account 'phoenix'), downloads that package and its SHA-256, refuses on a
# mismatch, unpacks into E:\TSIC-Incoming\<stamp>\package, checks both payloads
# (marker file, manifest, file count) and that the manifests agree with
# build-info.json. Then records the stamp in E:\TSIC-Incoming\last-pulled.txt --
# the ONE thing Stage-PulledDevFiles.ps1 reads to know what to stage.
#
# Safe to re-run: if the latest package is already here (say the deploy was
# declined at Deploy-FromDev's prompt), it is re-verified from disk instead of
# downloaded again. A pull that fails part-way removes its unfinished folder.
#
# Built by scripts\TSICDev-Ops\Deploy-Prod.ps1 on TSIC-OVH-DEV. Lives in the
# repo at scripts\TSICDev-Ops\ and on PHOENIX in E:\TSIC-Incoming -- never in
# IIS-Config-Prod. Fallback: RDP to SEDONA, 1-Build-And-Deploy-Prod.ps1.
#
# ASCII only: Windows PowerShell 5.1 reads BOM-less files as ANSI.
# ============================================================================

#Requires -RunAsAdministrator

param(
    # TEST ONLY - rehearsal on TSIC-OVH-DEV, never on PHOENIX: downloads become
    # copies from C:\TSIC\Phoenix\deploy, E:\TSIC-Incoming becomes
    # <RehearsalRoot>\TSIC-Incoming, and the prod _config.ps1 is loaded
    # tolerating the missing E: drive.
    [string]$RehearsalRoot
)

$ErrorActionPreference = 'Stop'

$OvhHost      = '15.204.47.57'
$OvhUser      = 'phoenix'
$SshKey       = 'C:\TSIC\ssh\phoenix-ovh'
$KnownHosts   = 'C:\TSIC\ssh\known_hosts'
$IncomingDir  = 'E:\TSIC-Incoming'
$KeepIncoming = 3

if ($RehearsalRoot) {
    if ($env:COMPUTERNAME -eq 'TSIC-PHOENIX') { throw "-RehearsalRoot is for TSIC-OVH-DEV only." }
    $IncomingDir = Join-Path $RehearsalRoot 'TSIC-Incoming'
    Write-Host "REHEARSAL under $RehearsalRoot - sftp replaced by local copies; nothing leaves this box." -ForegroundColor Magenta
}

$ok = $false
$createdDest = $null

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
    $batch = Join-Path $env:TEMP "pull-devfiles-sftp-$PID.txt"
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
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  PULL - download from TSIC-OVH-DEV" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    New-Item -ItemType Directory -Path $IncomingDir -Force | Out-Null
    $tmp = Join-Path $IncomingDir '_download'
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
    New-Item -ItemType Directory -Path $tmp | Out-Null
    $t = $tmp.Replace('\','/')

    Write-Host "Step 1: Reading latest.txt from OVH..." -ForegroundColor Yellow
    Invoke-OvhSftp @("get /deploy/latest.txt `"$t/latest.txt`"")
    $name = (Get-Content -LiteralPath (Join-Path $tmp 'latest.txt') -Raw).Trim()
    if ($name -notmatch '^tsic-prod-(v\d{6}\.\d{4}\.[0-9a-f]+)\.zip$') { throw "latest.txt names '$name' - not a production package name." }
    $stamp = $Matches[1]
    Write-Host "  Latest package: $name" -ForegroundColor Green

    $dest = Join-Path $IncomingDir $stamp
    $pkg  = Join-Path $dest 'package'
    if (Test-Path -LiteralPath $dest) {
        # Pulled before (e.g. the deploy was declined at the prompt). No download --
        # but nothing is trusted from disk either: the stored zip is re-hashed and
        # the payloads re-checked below, exactly as for a fresh pull.
        Write-Host ""
        Write-Host "Step 2: Already pulled - re-verifying the copy in $dest..." -ForegroundColor Yellow
        $zipLocal = Join-Path $dest $name
        if (-not (Test-Path -LiteralPath $zipLocal) -or -not (Test-Path -LiteralPath "$zipLocal.sha256") -or -not (Test-Path -LiteralPath $pkg)) {
            throw "$dest is incomplete. Delete that folder and run .\Pull-DevFiles.ps1 again."
        }
        $want = ((Get-Content -LiteralPath "$zipLocal.sha256" -Raw).Trim() -split '\s+')[0]
        $have = (Get-FileHash -LiteralPath $zipLocal -Algorithm SHA256).Hash
        if ($have -ne $want) { throw "stored package fails its SHA-256 ($have, expected $want). Delete $dest and pull again." }
        Write-Host "  SHA-256 verified: $have" -ForegroundColor Green
        Remove-Item -LiteralPath $tmp -Recurse -Force
    } else {
        Write-Host ""
        Write-Host "Step 2: Downloading package + checksum..." -ForegroundColor Yellow
        Invoke-OvhSftp @("get /deploy/$name `"$t/$name`"", "get /deploy/$name.sha256 `"$t/$name.sha256`"")
        $want = ((Get-Content -LiteralPath (Join-Path $tmp "$name.sha256") -Raw).Trim() -split '\s+')[0]
        $have = (Get-FileHash -LiteralPath (Join-Path $tmp $name) -Algorithm SHA256).Hash
        if ($have -ne $want) { throw "SHA-256 MISMATCH - expected $want, downloaded file is $have. Nothing was unpacked." }
        Write-Host "  SHA-256 verified: $have" -ForegroundColor Green

        Write-Host ""
        Write-Host "Step 3: Unpacking into $dest..." -ForegroundColor Yellow
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        New-Item -ItemType Directory -Path $dest | Out-Null
        $createdDest = $dest     # removed again if anything below fails
        [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $tmp $name), $pkg)
        Move-Item -LiteralPath (Join-Path $tmp $name) -Destination $dest
        Move-Item -LiteralPath (Join-Path $tmp "$name.sha256") -Destination $dest
        Remove-Item -LiteralPath $tmp -Recurse -Force
    }

    $biFile = Join-Path $pkg 'build-info.json'
    if (-not (Test-Path -LiteralPath $biFile)) { throw "no build-info.json - not a TSIC production package." }
    $bi = Get-Content -LiteralPath $biFile -Raw | ConvertFrom-Json
    if ($bi.comparison) { throw "this is a COMPARISON build - never deployable." }
    if (-not $bi.api -and -not $bi.angular) { throw "build-info says neither API nor Angular was built." }
    if ($bi.buildStamp -ne $stamp) { throw "build-info stamp $($bi.buildStamp) does not match the package name ($stamp)." }

    # The package's own helpers: the commit being deployed (what SEDONA used).
    foreach ($h in @((Join-Path $pkg 'IIS-Config-Prod\_config.ps1'), (Join-Path $pkg 'IIS-Config-Prod\_deploy-common.ps1'))) {
        if (-not (Test-Path -LiteralPath $h)) { throw "package is missing $h" }
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

    # Only now, fully verified, does it become "the pulled package".
    [IO.File]::WriteAllText((Join-Path $IncomingDir 'last-pulled.txt'), "$stamp`n", (New-Object System.Text.ASCIIEncoding))

    $old = @(Get-ChildItem -LiteralPath $IncomingDir -Directory |
             Where-Object { $_.Name -match '^v\d{6}\.\d{4}\.[0-9a-f]+$' -and $_.Name -ne $stamp } |
             Sort-Object LastWriteTime -Descending | Select-Object -Skip ($KeepIncoming - 1))
    foreach ($o in $old) { Remove-Item -LiteralPath $o.FullName -Recurse -Force; Write-Host "  Pruned old package: $($o.Name)" -ForegroundColor DarkGray }

    $scope = if ($bi.api -and $bi.angular) { 'API + Angular' } elseif ($bi.api) { 'API only' } else { 'Angular only' }
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "PULLED AND VERIFIED - E:\Websites was not touched." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Build:  $($bi.buildStamp)  git $($bi.gitHash)  ($scope)  built on $($bi.builtOn)" -ForegroundColor Green
    Write-Host ""
    Write-Host "  NEXT (fills the staging folders; live is untouched):" -ForegroundColor Yellow
    Write-Host "    .\Stage-PulledDevFiles.ps1" -ForegroundColor White
    Write-Host ""
    $ok = $true
    exit 0
} catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    # A package folder this run created but did not finish verifying is removed,
    # so it can never be mistaken later for a verified pull.
    if ($createdDest -and (Test-Path -LiteralPath $createdDest)) {
        Remove-Item -LiteralPath $createdDest -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Removed the incomplete $createdDest." -ForegroundColor Yellow
    }
    Write-Host "  E:\Websites was NOT touched." -ForegroundColor Yellow
    Write-Host ""
    exit 1
} finally {
    if (-not $ok) { Write-Host "  Ended without completing: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Red }
}
