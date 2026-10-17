# =============================================================================
# 01-Install-SqlServer.ps1 -- SQL Server 2019 Developer, instance SS2016
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\01-Install-SqlServer.ps1 [-CheckOnly]
#
# Unattended install of the database engine only, as named instance SS2016 (the
# name every script and connection string already uses), Windows auth, TCP on,
# engine + Agent + Browser automatic. Sysadmins: the account running this and
# BUILTIN\Administrators. Also creates the data folder restores write into.
#
# Running this ACCEPTS the SQL Server license terms (/IACCEPTSQLSERVERLICENSETERMS).
#
# Media (~1.5 GB) is downloaded from Microsoft into C:\SQLMedia once and reused.
# If the instance already exists, nothing is downloaded or installed.
#
# Ported from install-sql2019-dev.ps1 (repo root, untracked).
# =============================================================================

#Requires -RunAsAdministrator

param([switch]$CheckOnly)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

$Media     = 'C:\SQLMedia'
$Bootstrap = Join-Path $Media 'SQL2019-SSEI-Dev.exe'
$Extract   = Join-Path $Media 'Setup'
$Agent     = "SQLAgent`$$($TSICDev.SqlInstanceName)"

try {
    Assert-TSICDevBox
    Write-Host ""
    Write-Host "SQL Server instance $($TSICDev.SqlInstanceName)" -ForegroundColor Cyan

    if (Get-Service -Name $TSICDev.SqlServiceName -ErrorAction SilentlyContinue) {
        Write-Same "instance $($TSICDev.SqlInstanceName) is installed"
    } else {
        Invoke-TSICDevChange "install SQL Server 2019 Developer as $($TSICDev.SqlInstanceName)" {
            New-Item -ItemType Directory -Force $Media | Out-Null
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            if (-not (Test-Path $Bootstrap)) {
                Write-Host "    downloading bootstrapper..."
                Invoke-WebRequest 'https://go.microsoft.com/fwlink/?linkid=866662' -OutFile $Bootstrap -UseBasicParsing
            }
            $sig = Get-AuthenticodeSignature $Bootstrap
            if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
                throw "Bootstrapper signature is not a valid Microsoft signature ($($sig.Status))."
            }
            $cab = Get-ChildItem $Media -Filter 'SQLServer2019-DEV-x64-*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $cab) {
                Write-Host "    downloading full media (~1.5 GB)..."
                $p = Start-Process $Bootstrap -ArgumentList "/ACTION=Download /MEDIAPATH=$Media /MEDIATYPE=CAB /QUIET" -Wait -PassThru
                if ($p.ExitCode -ne 0) { throw "Media download failed (exit $($p.ExitCode))." }
                $cab = Get-ChildItem $Media -Filter 'SQLServer2019-DEV-x64-*.exe' | Select-Object -First 1
                if (-not $cab) { throw "Media download reported success but no SQLServer2019-DEV-x64-*.exe is in $Media." }
            }
            if (-not (Test-Path (Join-Path $Extract 'setup.exe'))) {
                Write-Host "    extracting media..."
                $p = Start-Process $cab.FullName -ArgumentList "/q /x:$Extract" -Wait -PassThru
                if ($p.ExitCode -ne 0) { throw "Media extract failed (exit $($p.ExitCode))." }
            }
            $sysadmin = "$env:USERDOMAIN\$env:USERNAME"
            Write-Host "    installing (sysadmins: $sysadmin, BUILTIN\Administrators)..."
            $setupArgs = @(
                '/Q', '/ACTION=Install', '/IACCEPTSQLSERVERLICENSETERMS',
                '/FEATURES=SQLENGINE',
                "/INSTANCENAME=$($TSICDev.SqlInstanceName)",
                "/SQLSYSADMINACCOUNTS=`"$sysadmin`" `"BUILTIN\Administrators`"",
                '/SQLSVCSTARTUPTYPE=Automatic', '/AGTSVCSTARTUPTYPE=Automatic',
                '/BROWSERSVCSTARTUPTYPE=Automatic', '/TCPENABLED=1',
                '/UPDATEENABLED=True', '/UPDATESOURCE=MU'
            )
            $p = Start-Process (Join-Path $Extract 'setup.exe') -ArgumentList $setupArgs -Wait -PassThru
            if ($p.ExitCode -eq 3010) { Write-Host "    setup succeeded; Windows wants a REBOOT." -ForegroundColor Yellow }
            elseif ($p.ExitCode -ne 0) {
                throw "SQL setup failed (exit $($p.ExitCode)). Log: C:\Program Files\Microsoft SQL Server\150\Setup Bootstrap\Log\Summary.txt"
            }
        }
    }

    # Services: present, automatic, running.
    foreach ($svc in @($TSICDev.SqlServiceName, $Agent, 'SQLBrowser')) {
        $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
        if (-not $s) {
            if ($CheckOnly) { Write-Host "  WOULD    (service $svc appears with the install)" -ForegroundColor Magenta; continue }
            throw "Service $svc is missing after install."
        }
        if ($s.StartType -ne 'Automatic') {
            Invoke-TSICDevChange "service $svc startup -> Automatic" { Set-Service -Name $svc -StartupType Automatic }
        } else { Write-Same "service $svc startup Automatic" }
        if ($s.Status -ne 'Running') {
            Invoke-TSICDevChange "start service $svc" { Start-Service -Name $svc }
        } else { Write-Same "service $svc running" }
    }

    Confirm-TSICDevFolder $TSICDev.DataDir

    # Proof: connect and ask the engine what it is.
    if (Get-Service -Name $TSICDev.SqlServiceName -ErrorAction SilentlyContinue) {
        $v = & sqlcmd -S $TSICDev.SqlInstance -E -b -W -h -1 -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(20)) + ' ' + CAST(SERVERPROPERTY('Edition') AS varchar(60));"
        if ($LASTEXITCODE -ne 0) { throw "Cannot connect to $($TSICDev.SqlInstance): $(@($v) -join ' ')" }
        Write-Ok "connected: $((@($v) -join ' ').Trim())"
    }

    Complete-TSICDevStep 'SQL Server'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
