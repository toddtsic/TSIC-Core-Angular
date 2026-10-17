# =============================================================================
# 08-Setup-Phoenix-Sftp.ps1 -- SFTP drop for PHOENIX: sshd, the 'phoenix' account,
#                              its chroot, and admin key login
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\08-Setup-Phoenix-Sftp.ps1 -CheckOnly
#   .\scripts\TSICDev-Build\08-Setup-Phoenix-Sftp.ps1 [-PhoenixPublicKeyFile <phoenix-ovh.pub>]
#                                                     [-AdminPublicKeyFile <key.pub>]
#
# Needs 01 first (the SQL Server service account is granted read on backups\).
#
# PHOENIX pushes its hourly SQL backups here (task Push_Backups_To_OVH on PHOENIX,
# uploading as .partial then renaming). The account can do NOTHING else:
#
#   sshd             OpenSSH Server capability, service Automatic + running.
#                    Firewall rule OpenSSH-Server-In-TCP scoped to $TSICDev.SshAllowFrom.
#   sshd_config      Password and keyboard-interactive login OFF for everyone.
#                    Administrators: keys in administrators_authorized_keys.
#                    phoenix: keys in phoenix_authorized_keys, chrooted to
#                    C:\TSIC\Phoenix, sftp only, no forwarding / tty / X11.
#   phoenix          Local user, Users group only, random password nobody knows.
#   C:\TSIC\Phoenix  Chroot root. Inheritance off; SYSTEM + Administrators full;
#                    phoenix read on THIS folder only (sshd requires the chroot
#                    root not be writable by the user).
#     backups\       phoenix MODIFY (upload, rename, prune); SQL Server service
#                    READ (Restore-Databases.ps1 restores straight from here).
#     deploy\        phoenix READ only.
#   Keys             Each authorized_keys file: SYSTEM + Administrators only.
#                    -PhoenixPublicKeyFile: PHOENIX's public key, written with a
#                    from="<PHOENIX egress IP>" restriction. -AdminPublicKeyFile:
#                    an admin login key (SEDONA's tsic-ovh key today). Omitted =
#                    the existing file is kept, only its permissions are checked.
#
# sshd_config is replaced only if its settings differ, after a backup, and only
# once 'sshd -t' accepts the new file; then sshd restarts.
# =============================================================================

#Requires -RunAsAdministrator

param(
    [string]$PhoenixPublicKeyFile,
    [string]$PhoenixFrom = '204.17.37.202',     # PHOENIX's egress address
    [string]$AdminPublicKeyFile,
    [switch]$CheckOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

$SshDir      = 'C:\ProgramData\ssh'
$SshdConfig  = Join-Path $SshDir 'sshd_config'
$SshdExe     = Join-Path $env:windir 'System32\OpenSSH\sshd.exe'
$PhoenixKeys = Join-Path $SshDir 'phoenix_authorized_keys'
$AdminKeys   = Join-Path $SshDir 'administrators_authorized_keys'
$User        = $TSICDev.SftpUser
$Chroot      = $TSICDev.SftpChroot

# Effective settings only; comments are not compared.
$WantedConfig = @"
AuthorizedKeysFile	.ssh/authorized_keys
Subsystem	sftp	sftp-server.exe
PasswordAuthentication no
KbdInteractiveAuthentication no
Match Group administrators
       AuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys
Match User $User
       AuthorizedKeysFile __PROGRAMDATA__/ssh/phoenix_authorized_keys
       ChrootDirectory $Chroot
       ForceCommand internal-sftp
       AllowTcpForwarding no
       PermitTTY no
       X11Forwarding no
"@

function Get-EffectiveLines {
    param([string[]]$Lines)
    @($Lines | Where-Object { $_ -match '^\s*[^#\s]' } | ForEach-Object { ($_.Trim() -replace '\s+', ' ') })
}

# One public key line from a .pub file: "<type> <base64> [comment]".
function Read-PublicKey {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Public key file not found: $Path" }
    $line = @(Get-Content -LiteralPath $Path | Where-Object { $_.Trim() })
    if ($line.Count -ne 1 -or $line[0] -notmatch '^(ssh-ed25519|ssh-rsa|ecdsa-sha2-\S+) [A-Za-z0-9+/=]+') {
        throw "$Path must contain exactly one OpenSSH public key line (ssh-ed25519 / ssh-rsa / ecdsa)."
    }
    $line[0].Trim()
}

# 'a.b.c.d/nn' and 'a.b.c.d/255.255.255.x' are the same range; Windows reports the mask form.
function ConvertTo-MaskForm {
    param([string]$Address)
    if ($Address -notmatch '^(\d+\.\d+\.\d+\.\d+)/(\d{1,2})$') { return $Address }
    $bits = [int]$Matches[2]
    $mask = if ($bits -eq 0) { 0 } else { [uint32]([math]::Pow(2, 32) - [math]::Pow(2, 32 - $bits)) }
    $bytes = [BitConverter]::GetBytes($mask); [Array]::Reverse($bytes)
    "$($Matches[1])/$(([Net.IPAddress]$bytes).ToString())"
}

function Set-KeyFile {
    param([string]$Path, [string]$Content)
    $current = if (Test-Path -LiteralPath $Path) { ((Get-Content -LiteralPath $Path) -join "`n").Trim() } else { $null }
    if ($current -eq $Content.Trim()) { Write-Same "$Path content" }
    else {
        Invoke-TSICDevChange "write $Path" { [IO.File]::WriteAllText($Path, $Content.Trim() + "`r`n", [Text.Encoding]::ASCII) }
    }
}

try {
    Assert-TSICDevBox

    # --- sshd ------------------------------------------------------------------
    Write-Host ""
    Write-Host "OpenSSH Server" -ForegroundColor Cyan
    $cap = Get-WindowsCapability -Online -Name 'OpenSSH.Server*' | Select-Object -First 1
    if ($cap.State -eq 'Installed') { Write-Same "$($cap.Name) installed" }
    else { Invoke-TSICDevChange "install $($cap.Name)" { Add-WindowsCapability -Online -Name $cap.Name | Out-Null } }

    $svc = Get-Service sshd -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.StartType -ne 'Automatic') { Invoke-TSICDevChange 'sshd startup -> Automatic' { Set-Service sshd -StartupType Automatic } }
        else { Write-Same 'sshd startup Automatic' }
    }

    $rule = Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue
    if (-not $rule) {
        Invoke-TSICDevChange "firewall rule OpenSSH-Server-In-TCP (TCP 22 from $($TSICDev.SshAllowFrom))" {
            New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH SSH Server (sshd)' -Direction Inbound `
                -Protocol TCP -LocalPort 22 -Action Allow -RemoteAddress $TSICDev.SshAllowFrom | Out-Null
        }
    } else {
        $remote = @(($rule | Get-NetFirewallAddressFilter).RemoteAddress)
        if ($remote.Count -eq 1 -and (ConvertTo-MaskForm $remote[0]) -eq (ConvertTo-MaskForm $TSICDev.SshAllowFrom) -and $rule.Enabled -eq 'True' -and $rule.Action -eq 'Allow') {
            Write-Same "firewall: TCP 22 only from $($TSICDev.SshAllowFrom)"
        } else {
            Invoke-TSICDevChange "firewall: TCP 22 scope '$($remote -join ',')' -> $($TSICDev.SshAllowFrom), enabled" {
                Set-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -RemoteAddress $TSICDev.SshAllowFrom -Enabled True -Action Allow
            }
        }
    }

    # --- phoenix account -------------------------------------------------------
    Write-Host ""
    Write-Host "Account $User" -ForegroundColor Cyan
    $acct = Get-LocalUser -Name $User -ErrorAction SilentlyContinue
    if ($acct) { Write-Same "user $User exists" }
    else {
        Invoke-TSICDevChange "create local user $User (random password, never shown)" {
            $bytes = New-Object byte[] 32
            [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
            $pw = ConvertTo-SecureString ([Convert]::ToBase64String($bytes) + 'aA1!') -AsPlainText -Force
            New-LocalUser -Name $User -Password $pw -PasswordNeverExpires -UserMayNotChangePassword `
                -Description 'PHOENIX SFTP drop - key only, chrooted' | Out-Null
            Add-LocalGroupMember -Group 'Users' -Member $User -ErrorAction SilentlyContinue
        }
    }
    if ($acct) {
        $groups = @(Get-LocalGroup | Where-Object { @(Get-LocalGroupMember $_ -ErrorAction SilentlyContinue | Where-Object { $_.SID -eq $acct.SID }).Count } | ForEach-Object { $_.Name })
        if ($groups -contains 'Administrators') { throw "$User is in Administrators - it must not be." }
        Write-Same "$User groups: $($groups -join ', ')"
        if (-not $acct.Enabled) { Invoke-TSICDevChange "enable $User" { Enable-LocalUser -Name $User } } else { Write-Same "$User enabled" }
    }

    # --- chroot folders --------------------------------------------------------
    # Rights/inheritance spelled the way Windows stores them, so the comparison is exact.
    Write-Host ""
    Write-Host "Chroot $Chroot" -ForegroundColor Cyan
    foreach ($d in @($Chroot, $TSICDev.BackupDir, $TSICDev.DeployDropDir)) { Confirm-TSICDevFolder $d }
    if (-not ($CheckOnly -and -not (Test-Path $Chroot))) {
        $all = 'ContainerInherit, ObjectInherit'
        Set-TSICDevExactAcl -Path $Chroot -Protected $true -Owner 'BUILTIN\Administrators' -Rules @(
            @{ Id = 'NT AUTHORITY\SYSTEM';    Rights = 'FullControl';                 Inherit = $all },
            @{ Id = 'BUILTIN\Administrators'; Rights = 'FullControl';                 Inherit = $all },
            @{ Id = $User;                    Rights = 'ReadAndExecute, Synchronize'; Inherit = 'None' }
        )
        Set-TSICDevExactAcl -Path $TSICDev.BackupDir -Protected $false -Owner 'BUILTIN\Administrators' -Rules @(
            @{ Id = $User;                       Rights = 'Modify, Synchronize';         Inherit = $all },
            @{ Id = $TSICDev.SqlServiceAccount;  Rights = 'ReadAndExecute, Synchronize'; Inherit = $all }
        )
        Set-TSICDevExactAcl -Path $TSICDev.DeployDropDir -Protected $false -Owner 'BUILTIN\Administrators' -Rules @(
            @{ Id = $User; Rights = 'ReadAndExecute, Synchronize'; Inherit = $all }
        )
    }

    # --- keys ------------------------------------------------------------------
    Write-Host ""
    Write-Host "Authorized keys" -ForegroundColor Cyan
    if ($PhoenixPublicKeyFile) { Set-KeyFile $PhoenixKeys "from=`"$PhoenixFrom`" $(Read-PublicKey $PhoenixPublicKeyFile)" }
    elseif (-not (Test-Path $PhoenixKeys)) { throw "$PhoenixKeys does not exist - pass -PhoenixPublicKeyFile." }
    else { Write-Same "$PhoenixKeys kept (no -PhoenixPublicKeyFile)" }
    if ($AdminPublicKeyFile) { Set-KeyFile $AdminKeys (Read-PublicKey $AdminPublicKeyFile) }
    elseif (-not (Test-Path $AdminKeys)) { Write-Host "  NOTE     no $AdminKeys - nobody can log in as an administrator over SSH" -ForegroundColor Yellow }
    else { Write-Same "$AdminKeys kept (no -AdminPublicKeyFile)" }
    foreach ($k in @($PhoenixKeys, $AdminKeys)) {
        if (Test-Path $k) {
            # sshd refuses a key file anyone but SYSTEM / Administrators can write.
            $acl = Get-Acl -LiteralPath $k
            $rules = @($acl.Access | ForEach-Object { "$($_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value)|$([int]$_.FileSystemRights)" } | Sort-Object)
            $want  = @("$(Get-TSICDevSid 'NT AUTHORITY\SYSTEM')|$([int][Security.AccessControl.FileSystemRights]'FullControl')",
                       "$(Get-TSICDevSid 'BUILTIN\Administrators')|$([int][Security.AccessControl.FileSystemRights]'FullControl')") | Sort-Object
            if ($acl.AreAccessRulesProtected -and (($rules -join ';') -eq ($want -join ';'))) { Write-Same "$k permissions SYSTEM + Administrators only" }
            else {
                Invoke-TSICDevChange "$k permissions -> SYSTEM + Administrators only" {
                    & icacls $k /inheritance:r /grant:r 'SYSTEM:F' 'Administrators:F' | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "icacls failed on $k" }
                }
            }
        }
    }

    # --- sshd_config -------------------------------------------------------------
    Write-Host ""
    Write-Host "sshd_config" -ForegroundColor Cyan
    $haveLines = if (Test-Path $SshdConfig) { Get-EffectiveLines (Get-Content $SshdConfig) } else { @() }
    $wantLines = Get-EffectiveLines ($WantedConfig -split "`r?`n")
    if (($haveLines -join "`n") -eq ($wantLines -join "`n")) { Write-Same 'settings match' }
    else {
        Invoke-TSICDevChange 'replace sshd_config (backup kept) and restart sshd' {
            $tmp = Join-Path $SshDir 'sshd_config.tsicdev-new'
            [IO.File]::WriteAllText($tmp, "# Managed by scripts\TSICDev-Build\08-Setup-Phoenix-Sftp.ps1`r`n" + ($WantedConfig -replace "`r?`n", "`r`n") + "`r`n", [Text.Encoding]::ASCII)
            & $SshdExe -t -f $tmp
            if ($LASTEXITCODE -ne 0) { Remove-Item $tmp; throw "sshd -t rejected the new config; nothing replaced." }
            if (Test-Path $SshdConfig) { Copy-Item $SshdConfig "$SshdConfig.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')" }
            Move-Item $tmp $SshdConfig -Force
            Restart-Service sshd
        }
    }

    # --- proof --------------------------------------------------------------------
    if (-not $CheckOnly) {
        if ((Get-Service sshd).Status -ne 'Running') { Start-Service sshd; Write-Changed 'started sshd' }
        & $SshdExe -t
        if ($LASTEXITCODE -ne 0) { throw 'sshd -t reports the live sshd_config is invalid.' }
        Write-Ok 'sshd running; live sshd_config passes sshd -t'
    } elseif ((Get-Service sshd -ErrorAction SilentlyContinue).Status -ne 'Running') {
        Write-Host "  WOULD    start sshd" -ForegroundColor Magenta; $script:TSICDevDrift++
    } else { Write-Same 'sshd running' }

    Complete-TSICDevStep 'PHOENIX SFTP drop'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
