# ============================================================================
# _backup-common.ps1 — Backup/restore primitives shared by deploy + rollback
# ============================================================================
# Dot-source from any deploy or rollback script:
#   . "$PSScriptRoot\_backup-common.ps1"
#
# The exclusion lists below are the reason this file exists. A backup and the
# restore that reads it MUST use the identical lists, because robocopy /MIR
# deletes anything in the destination that is absent from the source:
#
#   App_Data  — the app pool's AdnMonthEnd cache. Deploy strips it from staging
#               so the POOL recreates it pool-owned; a backup taken by an admin
#               and mirrored back would restore it admin-owned, which 500s the
#               ADN month-end import. Excluded from backup AND restore.
#   FirebaseAuth_*.json
#             — Google service-account creds. The prod deploy /XF-excludes them
#               from the staging->live sync, so a redeploy will NOT recreate
#               them. Mirror them away on restore and Firebase push stays dead
#               until someone hand-copies the files back.
#   logs      — runtime log output; no reason to snapshot or clobber it.
#   keys      — currently EMPTY on every box and nothing persists a key ring
#               there (no PersistKeysToFileSystem anywhere). Kept as a no-op
#               guard in case that ever changes; do not rely on it.
# ============================================================================

# API: live folder carries pool-owned runtime state and creds that must survive.
$TsicApiXD = @('logs', 'App_Data', 'keys')
$TsicApiXF = @('FirebaseAuth_*.json', 'Go.ps1')

# Angular: pure build output, no runtime-generated state.
$TsicAngularXD = @()
$TsicAngularXF = @('Go.ps1')

function Invoke-TsicRobocopy {
    <#
        Wraps robocopy /MIR with the caller's exclusions. Returns the exit code.
        robocopy: 0=no change, 1=copied, 2=extras deleted, 3=both, >=8=failure.
    #>
    param(
        [Parameter(Mandatory)] [string]   $Source,
        [Parameter(Mandatory)] [string]   $Dest,
        [string[]] $ExcludeDirs  = @(),
        [string[]] $ExcludeFiles = @(),
        [switch]   $Quiet
    )

    $roboArgs = @($Source, $Dest, '/MIR')
    if ($ExcludeDirs.Count)  { $roboArgs += '/XD'; $roboArgs += $ExcludeDirs }
    if ($ExcludeFiles.Count) { $roboArgs += '/XF'; $roboArgs += $ExcludeFiles }
    $roboArgs += @('/NJH', '/NJS', '/NDL')
    if ($Quiet) { $roboArgs += @('/NC', '/NS', '/NP') }

    robocopy @roboArgs | Out-Null
    return $LASTEXITCODE
}

function Test-TsicBackupSpace {
    <#
        Aborts a deploy before it stops anything if the backup cannot possibly fit.
        Requires the source's size plus 1 GB of headroom.
    #>
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $BackupsPath
    )

    if (!(Test-Path $Source)) { return $true }   # nothing to back up

    $driveLetter = (Split-Path $BackupsPath -Qualifier).TrimEnd(':')
    $freeGB = [math]::Round((Get-PSDrive $driveLetter).Free / 1GB, 1)

    $sizeBytes = (Get-ChildItem $Source -Recurse -Force -ErrorAction SilentlyContinue |
                  Measure-Object -Property Length -Sum).Sum
    $sizeGB = [math]::Round(($sizeBytes / 1GB), 2)

    $neededGB = $sizeGB + 1
    Write-Host ("  Disk: {0} GB free on {1}:  |  {2} is {3} GB  |  need ~{4} GB" -f `
        $freeGB, $driveLetter, (Split-Path $Source -Leaf), $sizeGB, $neededGB) -ForegroundColor White

    if ($freeGB -lt $neededGB) {
        Write-Host "  ERROR: insufficient free space on ${driveLetter}: for a backup." -ForegroundColor Red
        return $false
    }
    return $true
}

function New-TsicBackup {
    <#
        Snapshots $Source into $BackupsPath\<Prefix>-<timestamp>, then prunes to
        the $Keep most recent backups carrying that EXACT prefix.

        The prefix filter matters: E:\Websites\Backups also holds legacy
        TSICUnify-2024-* / TSICUnify-Api-* backups, and C:\Websites\Backups holds
        a stale claude-api-* directory. A loose filter would prune someone else's.

        Returns the backup path on success, $null on failure (caller must abort).
    #>
    param(
        [Parameter(Mandatory)] [string]   $Source,
        [Parameter(Mandatory)] [string]   $BackupsPath,
        [Parameter(Mandatory)] [string]   $Prefix,
        [Parameter(Mandatory)] [string]   $Timestamp,
        [string[]] $ExcludeDirs  = @(),
        [string[]] $ExcludeFiles = @(),
        [int]      $Keep = 3
    )

    if (!(Test-Path $Source) -or !(Get-ChildItem $Source -Force -ErrorAction SilentlyContinue)) {
        Write-Host "  No existing $Prefix to back up (first deploy)." -ForegroundColor DarkGray
        return ''   # not a failure — distinct from $null
    }

    if (!(Test-Path $BackupsPath)) { New-Item -ItemType Directory -Path $BackupsPath -Force | Out-Null }

    $backupDir = Join-Path $BackupsPath "$Prefix-$Timestamp"
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

    $exit = Invoke-TsicRobocopy -Source $Source -Dest $backupDir `
                -ExcludeDirs $ExcludeDirs -ExcludeFiles $ExcludeFiles -Quiet
    if ($exit -ge 8) {
        Write-Host "  ERROR: backup of $Prefix failed (robocopy exit $exit)." -ForegroundColor Red
        return $null
    }
    Write-Host "  Backed up: $backupDir" -ForegroundColor Green

    $old = Get-ChildItem $BackupsPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^$([regex]::Escape($Prefix))-\d{8}-\d{6}$" } |
        Sort-Object Name -Descending | Select-Object -Skip $Keep
    foreach ($dir in $old) {
        Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Pruned old backup: $($dir.Name)" -ForegroundColor DarkGray
    }

    return $backupDir
}

function Stop-TsicPool {
    <#
        Stops an app pool and waits for it to actually reach 'Stopped' — leaving
        'Started' is not the same as being stopped, and syncing into a live folder
        whose worker still holds file locks is how you get a half-copied site.
        Returns $true only if the pool is confirmed Stopped.
    #>
    param(
        [Parameter(Mandatory)] [string] $Pool,
        [int] $TimeoutSeconds = 30
    )

    $state = (Get-WebAppPoolState -Name $Pool -ErrorAction SilentlyContinue).Value
    if (!$state) {
        Write-Host "  App pool not found: $Pool" -ForegroundColor Red
        return $false
    }
    if ($state -eq 'Started') {
        Stop-WebAppPool -Name $Pool
    } else {
        Write-Host "  Already stopped: $Pool ($state)" -ForegroundColor DarkGray
    }

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
    } catch {
        Write-Host "  Failed to start ${Pool}: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Try manually: Start-WebAppPool $Pool" -ForegroundColor Yellow
    }
}
