# =============================================================================
# Verify-Prod-Readiness.ps1
#
# Read-only verification that TSIC-PHOENIX is correctly configured for the new
# system. Modifies NOTHING. Designed to be safe to copy/paste into an elevated
# PowerShell prompt -- no param() block, no [CmdletBinding()], no exit calls
# (any of which can misbehave or close the host window when pasted).
#
# To override defaults: edit the four $Verify* lines below before pasting.
#
# Coverage:
#   DB     SQL login + user, db_datareader/writer, GRANT EXECUTE (db-wide)
#   IIS    App pool, ApplicationPoolIdentity, LoadUserProfile, 64-bit
#   FS     Site path, logs/ writable, FileStorage paths writable
# =============================================================================

# --- Edit these if reality differs ------------------------------------------
$VerifyServerInstance   = 'localhost'
$VerifyDatabase         = 'TSICV5'
$VerifyAppPoolName      = 'claude-api'
$VerifySitePhysicalPath = 'E:\Websites\claude-api'
# ----------------------------------------------------------------------------

$ErrorActionPreference = 'Continue'  # don't kill the host on stray non-terminating errors
$VerifyPoolPrincipal   = "IIS APPPOOL\$VerifyAppPoolName"
$VerifyResults         = New-Object System.Collections.ArrayList

function Add-VerifyResult {
    param([string]$Area, [string]$Check, [string]$Status, [string]$Detail = '')
    [void]$VerifyResults.Add([pscustomobject]@{ Area = $Area; Check = $Check; Status = $Status; Detail = $Detail })
    $color = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'WARN' { 'Yellow' } default { 'Gray' } }
    Write-Host ('  [{0}] {1,-6} {2}' -f $Status, $Area, $Check) -ForegroundColor $color
    if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
}

function Test-PoolWritable {
    param([string]$Path, [string]$Principal)
    if (-not (Test-Path $Path)) { return @{ Exists = $false; Writable = $false; Reason = 'path does not exist' } }
    try {
        $acl = Get-Acl $Path -ErrorAction Stop
        $entries = $acl.Access | Where-Object {
            $_.IdentityReference.Value -ieq $Principal -and
            ($_.FileSystemRights -match 'Write|Modify|FullControl')
        }
        if ($entries) { return @{ Exists = $true; Writable = $true;  Reason = ($entries | ForEach-Object { $_.FileSystemRights }) -join ', ' } }
        else          { return @{ Exists = $true; Writable = $false; Reason = "no Write/Modify ACE for $Principal" } }
    }
    catch {
        return @{ Exists = $true; Writable = $false; Reason = "ACL read failed: $($_.Exception.Message)" }
    }
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Prod Readiness Verification' -ForegroundColor Cyan
Write-Host "  Server : $VerifyServerInstance / $VerifyDatabase"
Write-Host "  Pool   : $VerifyPoolPrincipal"
Write-Host "  Site   : $VerifySitePhysicalPath"
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

# --- DB checks --------------------------------------------------------------
Write-Host 'Database' -ForegroundColor White
try {
    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        Add-VerifyResult 'DB' 'SqlServer module available' 'WARN' 'Install-Module SqlServer required for DB checks; skipping DB section.'
    }
    else {
        Import-Module SqlServer -DisableNameChecking -ErrorAction Stop | Out-Null

        $loginQuery = "SELECT 1 AS found FROM sys.server_principals WHERE name = N'$VerifyPoolPrincipal';"
        $loginRow = Invoke-Sqlcmd -ServerInstance $VerifyServerInstance -Database master -Query $loginQuery -TrustServerCertificate -ErrorAction Stop
        if ($loginRow) { Add-VerifyResult 'DB' 'Server login exists' 'PASS' $VerifyPoolPrincipal }
        else           { Add-VerifyResult 'DB' 'Server login exists' 'FAIL' "Login [$VerifyPoolPrincipal] not found in sys.server_principals" }

        $userQuery = "SELECT 1 AS found FROM sys.database_principals WHERE name = N'$VerifyPoolPrincipal';"
        $userRow = Invoke-Sqlcmd -ServerInstance $VerifyServerInstance -Database $VerifyDatabase -Query $userQuery -TrustServerCertificate -ErrorAction Stop
        if ($userRow) { Add-VerifyResult 'DB' "Database user exists in $VerifyDatabase" 'PASS' $VerifyPoolPrincipal }
        else          { Add-VerifyResult 'DB' "Database user exists in $VerifyDatabase" 'FAIL' "User [$VerifyPoolPrincipal] not found" }

        $rolesQuery = "SELECT r.name AS role_name FROM sys.database_role_members rm JOIN sys.database_principals u ON u.principal_id = rm.member_principal_id JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id WHERE u.name = N'$VerifyPoolPrincipal';"
        $roles = Invoke-Sqlcmd -ServerInstance $VerifyServerInstance -Database $VerifyDatabase -Query $rolesQuery -TrustServerCertificate -ErrorAction Stop
        $roleNames = @($roles | ForEach-Object { $_.role_name })

        foreach ($needed in @('db_datareader', 'db_datawriter')) {
            if ($roleNames -contains $needed) { Add-VerifyResult 'DB' "Role: $needed" 'PASS' '' }
            else                              { Add-VerifyResult 'DB' "Role: $needed" 'FAIL' "Pool is not a member of $needed" }
        }

        $execQuery = "SELECT dp.permission_name, dp.state_desc FROM sys.database_permissions dp JOIN sys.database_principals pr ON pr.principal_id = dp.grantee_principal_id WHERE pr.name = N'$VerifyPoolPrincipal' AND dp.permission_name = 'EXECUTE' AND dp.class = 0 AND dp.state IN ('G','W');"
        $execRow = Invoke-Sqlcmd -ServerInstance $VerifyServerInstance -Database $VerifyDatabase -Query $execQuery -TrustServerCertificate -ErrorAction Stop
        if ($execRow) {
            Add-VerifyResult 'DB' 'GRANT EXECUTE (db-wide)' 'PASS' "$($execRow.state_desc) EXECUTE"
        }
        else {
            Add-VerifyResult 'DB' 'GRANT EXECUTE (db-wide)' 'FAIL' 'Sproc calls (e.g. reporting.CustomerJobRevenueRollups) will return permission denied.'
        }
    }
}
catch {
    Add-VerifyResult 'DB' 'SQL connection' 'FAIL' $_.Exception.Message
}

Write-Host ''
Write-Host 'IIS' -ForegroundColor White

# --- IIS checks -------------------------------------------------------------
try {
    Import-Module WebAdministration -DisableNameChecking -ErrorAction Stop | Out-Null

    $pool = Get-Item "IIS:\AppPools\$VerifyAppPoolName" -ErrorAction SilentlyContinue
    if (-not $pool) {
        Add-VerifyResult 'IIS' "App pool exists: $VerifyAppPoolName" 'FAIL' 'Pool does not exist. Edit $VerifyAppPoolName at top of script if name differs.'
    }
    else {
        Add-VerifyResult 'IIS' "App pool exists: $VerifyAppPoolName" 'PASS' "State: $($pool.state)"

        $identityType    = $pool.processModel.identityType
        $loadUserProfile = $pool.processModel.loadUserProfile
        $enable32Bit     = $pool.enable32BitAppOnWin64

        if ($identityType -eq 'ApplicationPoolIdentity') {
            Add-VerifyResult 'IIS' 'Identity = ApplicationPoolIdentity' 'PASS' "Pool runs as $VerifyPoolPrincipal"
        }
        else {
            Add-VerifyResult 'IIS' 'Identity = ApplicationPoolIdentity' 'FAIL' "IdentityType is '$identityType'; DB grants targeted at $VerifyPoolPrincipal won't apply."
        }

        if ($loadUserProfile) {
            Add-VerifyResult 'IIS' 'LoadUserProfile = true' 'PASS' 'DataProtection keys persist across recycles'
        }
        else {
            Add-VerifyResult 'IIS' 'LoadUserProfile = true' 'FAIL' 'Without it, ASP.NET Core uses ephemeral DataProtection keys. Cookies/antiforgery break on every IIS recycle.'
        }

        if (-not $enable32Bit) {
            Add-VerifyResult 'IIS' 'enable32BitAppOnWin64 = false' 'PASS' '.NET 8 is 64-bit'
        }
        else {
            Add-VerifyResult 'IIS' 'enable32BitAppOnWin64 = false' 'FAIL' '.NET 8 will not start in 32-bit mode'
        }
    }
}
catch {
    Add-VerifyResult 'IIS' 'WebAdministration module' 'FAIL' $_.Exception.Message
}

Write-Host ''
Write-Host 'Filesystem' -ForegroundColor White

# --- FS checks --------------------------------------------------------------
if (-not (Test-Path $VerifySitePhysicalPath)) {
    Add-VerifyResult 'FS' "Site path: $VerifySitePhysicalPath" 'FAIL' 'Path does not exist'
}
else {
    Add-VerifyResult 'FS' "Site path: $VerifySitePhysicalPath" 'PASS' ''

    $logsPath = Join-Path $VerifySitePhysicalPath 'logs'
    $logsCheck = Test-PoolWritable -Path $logsPath -Principal $VerifyPoolPrincipal
    if (-not $logsCheck.Exists) {
        Add-VerifyResult 'FS' 'logs/ folder writable by pool' 'WARN' "logs/ does not exist; pool needs Modify on parent to create it on first start."
    }
    elseif ($logsCheck.Writable) {
        Add-VerifyResult 'FS' 'logs/ folder writable by pool' 'PASS' $logsCheck.Reason
    }
    else {
        Add-VerifyResult 'FS' 'logs/ folder writable by pool' 'FAIL' $logsCheck.Reason
    }

    $appsettingsPath = Join-Path $VerifySitePhysicalPath 'appsettings.json'
    if (Test-Path $appsettingsPath) {
        try {
            $cfg = Get-Content $appsettingsPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($cfg.FileStorage) {
                $pathProps = $cfg.FileStorage.PSObject.Properties | Where-Object { $_.Name -match 'Path$' -and $_.Value }
                if (-not $pathProps) {
                    Add-VerifyResult 'FS' 'FileStorage paths' 'WARN' 'FileStorage section has no *Path keys configured'
                }
                foreach ($p in $pathProps) {
                    $check = Test-PoolWritable -Path $p.Value -Principal $VerifyPoolPrincipal
                    if (-not $check.Exists) {
                        Add-VerifyResult 'FS' "FileStorage.$($p.Name)" 'FAIL' "$($p.Value) does not exist"
                    }
                    elseif ($check.Writable) {
                        Add-VerifyResult 'FS' "FileStorage.$($p.Name)" 'PASS' "$($p.Value) ($($check.Reason))"
                    }
                    else {
                        Add-VerifyResult 'FS' "FileStorage.$($p.Name)" 'FAIL' "$($p.Value): $($check.Reason)"
                    }
                }
            }
            else {
                Add-VerifyResult 'FS' 'FileStorage section in appsettings.json' 'WARN' 'No FileStorage section found; uploads/medforms/banners may not be configured'
            }
        }
        catch {
            Add-VerifyResult 'FS' 'Parse appsettings.json' 'WARN' $_.Exception.Message
        }
    }
    else {
        Add-VerifyResult 'FS' 'appsettings.json present' 'WARN' "Not found at $appsettingsPath; cannot verify FileStorage paths"
    }
}

# --- Summary ----------------------------------------------------------------
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan

$passCount = ($VerifyResults | Where-Object Status -eq 'PASS').Count
$failCount = ($VerifyResults | Where-Object Status -eq 'FAIL').Count
$warnCount = ($VerifyResults | Where-Object Status -eq 'WARN').Count

Write-Host (' Summary: {0} PASS  {1} FAIL  {2} WARN' -f $passCount, $failCount, $warnCount) -ForegroundColor White
Write-Host '============================================================' -ForegroundColor Cyan

if ($failCount -gt 0) {
    Write-Host ''
    Write-Host 'Failures:' -ForegroundColor Red
    $VerifyResults | Where-Object Status -eq 'FAIL' | ForEach-Object {
        Write-Host ("  - [{0}] {1}: {2}" -f $_.Area, $_.Check, $_.Detail) -ForegroundColor Red
    }
}

Write-Host ''
