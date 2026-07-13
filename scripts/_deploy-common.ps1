# ============================================================================
# _deploy-common.ps1 - Shared primitives for every TSIC deploy and rollback
# ============================================================================
# Dot-source from any deploy or rollback script, local or prod:
#   . "$PSScriptRoot\_deploy-common.ps1"
#
# This file exists because the local and prod deploy scripts were independent
# copies of similar logic, and they drifted in four separate directions: local
# had a transcript and prod didn't; prod set $ErrorActionPreference and local
# didn't; local aborted when a pool wouldn't start and prod only warned; local
# checked its backup's exit code and prod ignored it. Nobody decided any of
# that. It is what duplicated logic does.
#
# Anything both sides must agree on lives HERE, so it cannot drift again.
# ============================================================================

# ---------------------------------------------------------------------------
# The sites we are allowed to touch, and how to recognise one on disk.
#
# E:\Websites also holds TSICUnify-2024, TSICUnify-Api, TSIC-CR-2025 and
# TSIC-STATICS - all live. robocopy /MIR at a wrong destination does not
# corrupt a folder, it EMPTIES it. So no script here accepts a free-form path:
# a site name maps to exactly one literal directory, and that directory must
# prove it is ours before anything writes to it.
# ---------------------------------------------------------------------------
$TsicSites = @{
    'dev-api'    = @{ Role = 'api';     Fingerprint = 'TSIC.API.dll' }
    'dev-app'    = @{ Role = 'angular'; Fingerprint = 'index.html'   }
    'claude-api' = @{ Role = 'api';     Fingerprint = 'TSIC.API.dll' }
    'claude-app' = @{ Role = 'angular'; Fingerprint = 'index.html'   }
}

# ---------------------------------------------------------------------------
# ONE exclusion list, applied to EVERY copy: publish->staging, staging->live,
# live->backup, backup->live.
#
# /MIR deletes whatever the destination has and the source lacks. So a copy
# that forgets one of these deletes it from the destination:
#
#   App_Data  - the pool's AdnMonthEnd cache. An admin-run copy that writes it
#               back lands it admin-owned (CREATOR OWNER resolves to
#               Administrators); the pool then cannot rewrite it and the
#               month-end import 500s. That is bug 993e15be. Excluded
#               everywhere, so it simply persists, pool-owned, untouched.
#   FirebaseAuth_*.json
#             - the two Google service-account files. They live only in the live
#               folders and are in no backup. The deploy excludes them, so a
#               redeploy does NOT recreate them: mirror them away and Firebase
#               push stays dead until someone hand-copies them back.
#   logs      - runtime log output.
#   keys      - empty on every box today and nothing persists a key ring there
#               (no PersistKeysToFileSystem anywhere in the app). Kept as a
#               no-op guard in case that changes. Do not rely on it.
#   Go.ps1    - the deploy wrapper; belongs in staging, never in a live folder.
#
# deploy-manifest.json is deliberately NOT excluded: it rides into live so the
# box can answer "what is live?", and into each backup so the rollback can say
# which build it is about to restore.
# ---------------------------------------------------------------------------
$TsicExclusions = @{
    api     = @{
        Dirs  = @('logs', 'App_Data', 'keys')
        Files = @('FirebaseAuth_*.json', 'Go.ps1')
    }
    angular = @{
        Dirs  = @()                      # pure build output; no runtime state
        Files = @('Go.ps1')
    }
}

$TsicManifestName = 'deploy-manifest.json'

# ---------------------------------------------------------------------------
# Site lookup
# ---------------------------------------------------------------------------
function Get-TsicSiteInfo {
    param([Parameter(Mandatory)] [string] $Site)

    if (-not $TsicSites.ContainsKey($Site)) {
        throw "Unknown site '$Site'. Known sites: $($TsicSites.Keys -join ', ')"
    }
    return $TsicSites[$Site]
}

function Get-TsicExclusions {
    param([Parameter(Mandatory)] [string] $Site)

    $role = (Get-TsicSiteInfo -Site $Site).Role
    return $TsicExclusions[$role]
}

# ---------------------------------------------------------------------------
# Path helpers
# ---------------------------------------------------------------------------
function Get-TsicFullPath {
    param([Parameter(Mandatory)] [string] $Path)
    return ([System.IO.Path]::GetFullPath($Path)).TrimEnd('\')
}

function Test-TsicReparsePoint {
    param([Parameter(Mandatory)] [string] $Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if (-not $item) { return $false }
    return [bool]($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
}

# ---------------------------------------------------------------------------
# THE guard. Nothing writes to a live folder without passing this first.
#
# Five independent checks; any one failing aborts. The load-bearing one is #2:
# a site name resolves to exactly one literal path, so there is no input that
# can steer a /MIR at TSICUnify-2024. The rest are defence in depth.
# ---------------------------------------------------------------------------
function Assert-TsicSafeTarget {
    param(
        [Parameter(Mandatory)] [string] $Site,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $BasePath,
        [switch] $SkipIisCheck
    )

    $info = Get-TsicSiteInfo -Site $Site          # 1. site is a known name, not a path

    # 2. the path IS <BasePath>\<Site>. Not "under BasePath" - E:\Websites is
    #    where legacy lives, so "under" proves nothing.
    $expected = Get-TsicFullPath (Join-Path $BasePath $Site)
    $actual   = Get-TsicFullPath $Path
    if ($actual -ine $expected) {
        throw "REFUSING to touch '$actual' - site '$Site' must be exactly '$expected'."
    }

    if (-not (Test-Path -LiteralPath $actual -PathType Container)) {
        throw "REFUSING to touch '$actual' - it does not exist as a directory."
    }

    # 3. not a junction. If claude-api were ever a reparse point aimed at a
    #    legacy folder, checks 1 and 2 would both pass and /MIR would destroy it.
    if (Test-TsicReparsePoint -Path $actual) {
        throw "REFUSING to touch '$actual' - it is a reparse point (junction/symlink)."
    }

    # 4. it looks like our app. Legacy folders carry neither fingerprint, which
    #    makes mirroring over a running legacy site structurally impossible.
    #    An EMPTY folder is exempt: no live site is empty, and a first deploy
    #    into a freshly-created site folder is legitimate.
    $isEmpty = -not @(Get-ChildItem -LiteralPath $actual -Force -ErrorAction SilentlyContinue).Count
    if (-not $isEmpty) {
        $fingerprint = Join-Path $actual $info.Fingerprint
        if (-not (Test-Path -LiteralPath $fingerprint)) {
            throw "REFUSING to touch '$actual' - it is not empty and has no '$($info.Fingerprint)'; this does not look like $Site."
        }
    }

    # 5. IIS agrees this folder is what the site serves. Binds pool to folder:
    #    you cannot stop pool A and mirror into folder B.
    #
    # NOT named $site: PowerShell variable names are case-insensitive, so $site
    # IS the [string]$Site parameter. Assigning the site object to it coerces the
    # object to a string, .physicalPath comes back empty, and this check then
    # refuses every legitimate deploy.
    if (-not $SkipIisCheck) {
        $iisSite = Get-Website -Name $Site -ErrorAction SilentlyContinue
        if (-not $iisSite) {
            throw "REFUSING to touch '$actual' - IIS has no site named '$Site'."
        }
        if (-not $iisSite.physicalPath) {
            throw "REFUSING to touch '$actual' - IIS returned no physicalPath for site '$Site'."
        }
        $iisPath = Get-TsicFullPath $iisSite.physicalPath
        if ($iisPath -ine $expected) {
            throw "REFUSING to touch '$actual' - IIS says site '$Site' serves '$iisPath'."
        }
    }

    return $true
}

# ---------------------------------------------------------------------------
# Staging lives next to live, and the build script mirrors into it over SMB from
# another box - so Assert-TsicSafeTarget's IIS check cannot run there. This is
# the weaker guard that still holds: the destination leaf must literally be
# "<site>-STAGING". A staging folder is not an IIS site and carries no
# fingerprint, so leaf identity is all there is to check - but it is enough to
# make a /MIR at \\PHOENIX\Websites\TSICUnify-2024 impossible.
# ---------------------------------------------------------------------------
function Assert-TsicSafeStaging {
    param(
        [Parameter(Mandatory)] [string] $Site,
        [Parameter(Mandatory)] [string] $Path
    )

    Get-TsicSiteInfo -Site $Site | Out-Null       # site must be a known name

    $expectedLeaf = "$Site-STAGING"
    $leaf = Split-Path (Get-TsicFullPath $Path) -Leaf
    if ($leaf -ine $expectedLeaf) {
        throw "REFUSING to mirror into '$Path' - staging for '$Site' must end in '$expectedLeaf', not '$leaf'."
    }
    return $true
}

# ---------------------------------------------------------------------------
# The Backups root also holds legacy's TSICUnify-2024-* and TSICUnify-Api-*
# backups. An anchored pattern is the only thing standing between us and
# deleting THEIR recovery path.
# ---------------------------------------------------------------------------
function Get-TsicBackupPattern {
    param([Parameter(Mandatory)] [string] $Site)
    return "^$([regex]::Escape($Site))-\d{8}-\d{6}$"
}

function Assert-TsicSafeBackup {
    param(
        [Parameter(Mandatory)] [string] $Site,
        [Parameter(Mandatory)] [string] $BackupPath,
        [Parameter(Mandatory)] [string] $BackupsRoot
    )

    $info   = Get-TsicSiteInfo -Site $Site
    $actual = Get-TsicFullPath $BackupPath
    $root   = Get-TsicFullPath $BackupsRoot

    $parent = Get-TsicFullPath (Split-Path $actual -Parent)
    if ($parent -ine $root) {
        throw "REFUSING to restore from '$actual' - it is not directly under '$root'."
    }

    $leaf = Split-Path $actual -Leaf
    if ($leaf -notmatch (Get-TsicBackupPattern -Site $Site)) {
        throw "REFUSING to restore from '$leaf' - not a $Site backup (expected $Site-yyyyMMdd-HHmmss)."
    }

    $fingerprint = Join-Path $actual $info.Fingerprint
    if (-not (Test-Path -LiteralPath $fingerprint)) {
        throw "REFUSING to restore from '$leaf' - no '$($info.Fingerprint)'; the backup is not a $Site build."
    }

    return $true
}

# ---------------------------------------------------------------------------
# robocopy
# ---------------------------------------------------------------------------
function Invoke-TsicRobocopy {
    <#
        /MIR with the caller's exclusions. Returns the exit code.
        robocopy: 0=no change, 1=copied, 2=extras deleted, 3=both, >=8=failure.

        /R:2 /W:5 matters. robocopy's DEFAULT is /R:1000000 /W:30 - a million
        retries thirty seconds apart, i.e. it hangs effectively forever on a
        locked file instead of failing. Fail fast; the caller aborts.
    #>
    param(
        [Parameter(Mandatory)] [string]   $Source,
        [Parameter(Mandatory)] [string]   $Dest,
        [string[]] $ExcludeDirs  = @(),
        [string[]] $ExcludeFiles = @(),
        [switch]   $ListOnly,
        [switch]   $Quiet
    )

    $roboArgs = @($Source, $Dest, '/MIR', '/R:2', '/W:5')
    if ($ExcludeDirs.Count)  { $roboArgs += '/XD'; $roboArgs += $ExcludeDirs }
    if ($ExcludeFiles.Count) { $roboArgs += '/XF'; $roboArgs += $ExcludeFiles }
    if ($ListOnly) { $roboArgs += '/L' }
    $roboArgs += @('/NJH', '/NJS', '/NDL')
    if ($Quiet) { $roboArgs += @('/NC', '/NS', '/NP') }

    robocopy @roboArgs | Out-Null
    return $LASTEXITCODE
}

function Get-TsicRoboPreview {
    <#
        Dry run (/L writes nothing). Returns what the real pass WOULD do, so the
        operator sees "4 files copied, 0 deleted" before anything is mirrored.
    #>
    param(
        [Parameter(Mandatory)] [string]   $Source,
        [Parameter(Mandatory)] [string]   $Dest,
        [string[]] $ExcludeDirs  = @(),
        [string[]] $ExcludeFiles = @()
    )

    $roboArgs = @($Source, $Dest, '/MIR', '/L', '/R:0', '/W:0')
    if ($ExcludeDirs.Count)  { $roboArgs += '/XD'; $roboArgs += $ExcludeDirs }
    if ($ExcludeFiles.Count) { $roboArgs += '/XF'; $roboArgs += $ExcludeFiles }
    $roboArgs += @('/NJH', '/NJS', '/NDL', '/NP')

    $out = robocopy @roboArgs 2>&1
    $copy   = @($out | Where-Object { $_ -match '^\s*(New File|Newer|Older|Changed)\s' }).Count
    $delete = @($out | Where-Object { $_ -match '^\s*\*EXTRA (File|Dir)' }).Count
    return @{ Copy = $copy; Delete = $delete }
}

# ---------------------------------------------------------------------------
# Payload - is this staging folder actually a deployable build?
#
# "Get-ChildItem returned something" is not "this is a build". A half-copied
# staging folder passes that test, and /MIR then deletes from live every file
# the partial copy happens to lack.
# ---------------------------------------------------------------------------
function Get-TsicFileCount {
    <#
        Counts ONLY the files a copy of this payload would actually carry - i.e.
        under the site's own exclusion list, matching robocopy's semantics (a
        bare /XD name excludes a directory of that name at any depth).

        This has to agree with the exclusions or the check eats itself: a dirty
        publish carrying App_Data would be counted here, excluded by the mirror,
        and the staging folder would then "fail" a file-count check for being
        exactly right.

        deploy-manifest.json is excluded because it is written after the count.
    #>
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Site
    )

    $ex   = Get-TsicExclusions -Site $Site
    $root = Get-TsicFullPath $Path
    $skipPatterns = @($TsicManifestName) + @($ex.Files)

    $count = 0
    foreach ($f in Get-ChildItem -LiteralPath $root -Recurse -File -Force -ErrorAction SilentlyContinue) {
        $rel  = $f.FullName.Substring($root.Length).TrimStart('\')
        $segs = $rel.Split('\')

        $skip = $false
        if ($segs.Length -gt 1) {
            foreach ($d in $ex.Dirs) {
                if ($segs[0..($segs.Length - 2)] -contains $d) { $skip = $true; break }
            }
        }
        if (-not $skip) {
            foreach ($pat in $skipPatterns) {
                if ($f.Name -like $pat) { $skip = $true; break }
            }
        }
        if (-not $skip) { $count++ }
    }
    return $count
}

function New-TsicManifest {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Site,
        [Parameter(Mandatory)] [string] $Environment,
        [string] $GitHash    = 'unknown',
        [string] $BuildStamp = 'unknown',
        [string] $BuiltOn    = $env:COMPUTERNAME
    )

    $manifest = [ordered]@{
        site        = $Site
        environment = $Environment
        gitHash     = $GitHash
        buildStamp  = $BuildStamp
        builtUtc    = (Get-Date).ToUniversalTime().ToString('o')
        builtOn     = $BuiltOn
        fileCount   = (Get-TsicFileCount -Path $Path -Site $Site)
    }
    $dest = Join-Path $Path $TsicManifestName
    $manifest | ConvertTo-Json | Set-Content -Path $dest -Encoding UTF8
    return $manifest
}

function Get-TsicManifest {
    param([Parameter(Mandatory)] [string] $Path)
    $file = Join-Path $Path $TsicManifestName
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    try   { return (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json) }
    catch { return $null }
}

function Test-TsicPayload {
    <#
        Returns $null when the folder is a deployable build for $Site, otherwise
        a human-readable reason. Caller aborts on any reason.
    #>
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Site
    )

    $info = Get-TsicSiteInfo -Site $Site

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return "'$Path' does not exist" }
    if (-not @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue).Count) { return "'$Path' is empty" }

    if (-not (Test-Path -LiteralPath (Join-Path $Path $info.Fingerprint))) {
        return "no '$($info.Fingerprint)' - not a $Site build"
    }

    $manifest = Get-TsicManifest -Path $Path
    if (-not $manifest) { return "no readable $TsicManifestName - build it with the current deploy script" }
    if ($manifest.site -ne $Site) { return "manifest says site='$($manifest.site)', expected '$Site'" }

    $actual = Get-TsicFileCount -Path $Path -Site $Site
    if ($actual -ne $manifest.fileCount) {
        return "file count mismatch - manifest says $($manifest.fileCount), found $actual (incomplete copy?)"
    }

    return $null
}

# ---------------------------------------------------------------------------
# Backup
# ---------------------------------------------------------------------------
function Test-TsicBackupSpace {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $BackupsPath
    )

    if (-not (Test-Path -LiteralPath $Source)) { return $true }

    $driveLetter = (Split-Path $BackupsPath -Qualifier).TrimEnd(':')
    $freeGB = [math]::Round((Get-PSDrive $driveLetter).Free / 1GB, 1)

    $bytes  = (Get-ChildItem -LiteralPath $Source -Recurse -Force -ErrorAction SilentlyContinue |
               Measure-Object -Property Length -Sum).Sum
    $sizeGB = [math]::Round(($bytes / 1GB), 2)
    $needGB = $sizeGB + 1

    Write-Host ("  Disk: {0} GB free on {1}:  |  {2} is {3} GB  |  need ~{4} GB" -f `
        $freeGB, $driveLetter, (Split-Path $Source -Leaf), $sizeGB, $needGB) -ForegroundColor White

    if ($freeGB -lt $needGB) {
        Write-Host "  ERROR: not enough free space on ${driveLetter}: to take a backup." -ForegroundColor Red
        return $false
    }
    return $true
}

function New-TsicBackup {
    <#
        Snapshots the live folder, then prunes to $Keep most recent for THIS site.
        Returns the backup path, '' when there was nothing to back up (first
        deploy), or $null on FAILURE - on which the caller must abort.
    #>
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $BackupsPath,
        [Parameter(Mandatory)] [string] $Site,
        [Parameter(Mandatory)] [string] $Timestamp,
        [int] $Keep = 3
    )

    if (-not (Test-Path -LiteralPath $Source) -or
        -not @(Get-ChildItem -LiteralPath $Source -Force -ErrorAction SilentlyContinue).Count) {
        Write-Host "  Nothing to back up for $Site (first deploy)." -ForegroundColor DarkGray
        return ''
    }

    if (-not (Test-Path -LiteralPath $BackupsPath)) {
        New-Item -ItemType Directory -Path $BackupsPath -Force | Out-Null
    }

    $ex     = Get-TsicExclusions -Site $Site
    $backup = Join-Path $BackupsPath "$Site-$Timestamp"
    New-Item -ItemType Directory -Path $backup -Force | Out-Null

    $exit = Invoke-TsicRobocopy -Source $Source -Dest $backup `
                -ExcludeDirs $ex.Dirs -ExcludeFiles $ex.Files -Quiet
    if ($exit -ge 8) {
        Write-Host "  ERROR: backup of $Site FAILED (robocopy exit $exit)." -ForegroundColor Red
        # Delete the partial directory BEFORE the prune. Its timestamp makes it
        # the newest, so it would count as one of the $Keep and silently evict a
        # GOOD backup - a failed backup would destroy a working one.
        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
        return $null
    }
    Write-Host "  Backed up: $backup" -ForegroundColor Green

    $pattern = Get-TsicBackupPattern -Site $Site
    $old = Get-ChildItem -LiteralPath $BackupsPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $pattern } |
        Sort-Object Name -Descending | Select-Object -Skip $Keep
    foreach ($dir in $old) {
        Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Pruned old backup: $($dir.Name)" -ForegroundColor DarkGray
    }

    return $backup
}

# ---------------------------------------------------------------------------
# App pools
# ---------------------------------------------------------------------------
function Stop-TsicPool {
    <#
        Stops a pool and waits for it to actually report 'Stopped'. Leaving
        'Started' is not the same as being stopped: a live worker still holds
        file handles on TSIC.API.dll, and mirroring into a locked folder yields
        a half-swapped site. Returns $true ONLY when confirmed Stopped.
    #>
    param(
        [Parameter(Mandatory)] [string] $Pool,
        [int] $TimeoutSeconds = 30
    )

    $state = (Get-WebAppPoolState -Name $Pool -ErrorAction SilentlyContinue).Value
    if (-not $state) {
        Write-Host "  ERROR: app pool not found: $Pool" -ForegroundColor Red
        return $false
    }
    if ($state -eq 'Started') { Stop-WebAppPool -Name $Pool -ErrorAction SilentlyContinue }

    for ($i = 0; $i -lt $TimeoutSeconds; $i++) {
        $state = (Get-WebAppPoolState -Name $Pool -ErrorAction SilentlyContinue).Value
        if ($state -eq 'Stopped') {
            Write-Host "  Stopped: $Pool" -ForegroundColor Green
            return $true
        }
        Start-Sleep -Seconds 1
    }

    Write-Host "  ERROR: $Pool did not stop after ${TimeoutSeconds}s (state: $state)." -ForegroundColor Red
    return $false
}

function Start-TsicPool {
    param([Parameter(Mandatory)] [string] $Pool)

    try {
        Start-WebAppPool -Name $Pool -ErrorAction Stop
        Write-Host "  Started: $Pool" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "  FAILED to start ${Pool}: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Try manually: Start-WebAppPool $Pool" -ForegroundColor Yellow
        return $false
    }
}

# ---------------------------------------------------------------------------
# Is the site actually up?
#
# A cold start is not a failure - the first request JITs. Retry, don't judge on
# one 60s GET. But do NOT print success on a failed warmup, which is what both
# scripts did.
#
# A 200 from the API is a strong oracle: it proves the NEW worker process booted
# (Program.cs refuses to start without ASPNETCORE_ENVIRONMENT) and reached the
# database. We do not additionally query Seq for the [STARTUP-CONFIG] boot audit
# - Seq's /api/events is 401 for anonymous callers (only /ingest is open), so
# that check would need a read API key provisioned on the box. Not worth a new
# secret on PHOENIX for a marginal gain over this.
# ---------------------------------------------------------------------------
function Test-TsicEndpoint {
    param(
        [Parameter(Mandatory)] [string] $Url,
        [int] $Retries      = 6,
        [int] $DelaySeconds = 10,
        [int] $TimeoutSec   = 30
    )

    for ($i = 1; $i -le $Retries; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSec -ErrorAction Stop
            if ([int]$r.StatusCode -ge 200 -and [int]$r.StatusCode -lt 400) {
                Write-Host "  OK ($([int]$r.StatusCode)): $Url" -ForegroundColor Green
                return $true
            }
            Write-Host "  Attempt ${i}/${Retries}: HTTP $([int]$r.StatusCode) from $Url" -ForegroundColor Yellow
        } catch {
            Write-Host "  Attempt ${i}/${Retries}: $($_.Exception.Message)" -ForegroundColor Yellow
        }
        if ($i -lt $Retries) { Start-Sleep -Seconds $DelaySeconds }
    }

    Write-Host "  FAILED after $Retries attempts: $Url" -ForegroundColor Red
    return $false
}

# ---------------------------------------------------------------------------
# Seq
#
# Resolved exactly the way ASP.NET resolves it: appsettings.json, then the
# environment overlay wins. So the deploy logs to the SAME Seq the app logs to,
# with no new config and no secrets - CLEF ingestion is anonymous on both boxes
# (verified: POST /ingest/clef -> 201 Created, no API key).
# ---------------------------------------------------------------------------
function Get-TsicSeqUrl {
    param(
        [Parameter(Mandatory)] [string] $AppRoot,
        [Parameter(Mandatory)] [string] $AspNetEnv
    )

    $url = $null
    foreach ($file in @('appsettings.json', "appsettings.$AspNetEnv.json")) {
        $path = Join-Path $AppRoot $file
        if (Test-Path -LiteralPath $path) {
            try {
                $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                if ($json.Seq -and $json.Seq.ServerUrl) { $url = $json.Seq.ServerUrl }
            } catch { }
        }
    }
    return $url
}

function Send-TsicDeployEvent {
    <#
        Best-effort, 5s timeout. Seq being unreachable must NEVER fail a deploy -
        it is an observer, not a dependency.
    #>
    param(
        [string] $SeqUrl,
        [Parameter(Mandatory)] [string] $Site,
        [Parameter(Mandatory)] [ValidateSet('Started','Completed','Failed','RolledBack')] [string] $Outcome,
        [string] $Environment = '',
        [string] $FailedStep  = '',
        [string] $BuildStamp  = '',
        [string] $GitHash     = '',
        [string] $BackupName  = '',
        [string] $LogPath     = '',
        [int]    $DurationSec = 0
    )

    if (-not $SeqUrl) { return }

    $level = 'Information'
    if ($Outcome -eq 'Failed') { $level = 'Error' }
    elseif ($Outcome -eq 'RolledBack') { $level = 'Warning' }

    $evt = [ordered]@{
        '@t'          = (Get-Date).ToUniversalTime().ToString('o')
        '@l'          = $level
        '@mt'         = '[DEPLOY] {Site} {Outcome} ({Environment}) step={FailedStep} build={BuildStamp} git={GitHash}'
        'Site'        = $Site
        'Outcome'     = $Outcome
        'Environment' = $Environment
        'FailedStep'  = $FailedStep
        'BuildStamp'  = $BuildStamp
        'GitHash'     = $GitHash
        'BackupName'  = $BackupName
        'LogPath'     = $LogPath
        'DurationSec' = $DurationSec
        'deploy_event'= $true
    }

    try {
        $body = ($evt | ConvertTo-Json -Compress)
        Invoke-RestMethod -Uri "$($SeqUrl.TrimEnd('/'))/ingest/clef" -Method Post -Body $body `
            -ContentType 'application/vnd.serilog.clef' -TimeoutSec 5 -ErrorAction Stop | Out-Null
    } catch {
        Write-Host "  (Seq unavailable, deploy event not logged: $($_.Exception.Message))" -ForegroundColor DarkGray
    }
}
