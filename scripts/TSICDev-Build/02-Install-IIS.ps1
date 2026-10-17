# =============================================================================
# 02-Install-IIS.ps1 -- IIS, URL Rewrite, and the .NET Hosting Bundle
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\02-Install-IIS.ps1 [-CheckOnly]
#
# 1. IIS role features. ASP.NET 4.x (IIS-ASPNET45, IIS-NetFxExtensibility45) is
#    deliberately NOT installed: the API runs on .NET 10 through the ASP.NET Core
#    Module with "No Managed Runtime" pools, and the Angular site is static files.
#    BUT those features also unlock the handlers/modules config sections, which
#    the API's web.config needs -- so step 1b unlocks exactly those two.
# 2. URL Rewrite 2.1 -- the Angular site's SPA routing depends on it.
# 3. .NET Hosting Bundle $TSICDev.HostingBundleVersion -- the ASP.NET Core Module
#    that lets IIS run the API. Must go in AFTER IIS or it does not register
#    with IIS. URL and SHA-512 come from Microsoft's release metadata and the
#    download is checked against both the hash and a Microsoft signature.
# 4. iisreset if anything was installed; then proof that both modules are
#    registered with IIS and W3SVC is running.
# =============================================================================

#Requires -RunAsAdministrator

param([switch]$CheckOnly)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

$Features = @(
    'IIS-WebServerRole', 'IIS-WebServer', 'IIS-CommonHttpFeatures', 'IIS-HttpErrors',
    'IIS-HttpLogging', 'IIS-RequestFiltering', 'IIS-StaticContent', 'IIS-DefaultDocument',
    'IIS-WebServerManagementTools', 'IIS-ManagementConsole', 'IIS-WebSockets',
    'IIS-HttpCompressionStatic', 'IIS-HttpCompressionDynamic'
)
$UrlRewriteMsi = 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi'
$UrlRewriteKey = 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite'
$Appcmd        = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
$Downloads     = Join-Path $env:TEMP 'TSICDev-installers'

function Assert-MicrosoftSigned {
    param([string]$Path)
    $sig = Get-AuthenticodeSignature $Path
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw "$Path is not validly signed by Microsoft ($($sig.Status), $($sig.SignerCertificate.Subject))."
    }
}

function Test-GlobalModule {
    param([string]$Name)
    if (-not (Test-Path $Appcmd)) { return $false }
    [bool](& $Appcmd list config -section:system.webServer/globalModules | Select-String -SimpleMatch "name=`"$Name`"")
}

function Test-HostingBundle {
    if (-not (Test-GlobalModule 'AspNetCoreModuleV2')) { return $false }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    [bool](& dotnet --list-runtimes | Select-String -SimpleMatch "Microsoft.AspNetCore.App $($TSICDev.HostingBundleVersion) ")
}

try {
    Assert-TSICDevBox
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $installed = $false

    # --- 1. IIS features ------------------------------------------------------
    Write-Host ""
    Write-Host "IIS features" -ForegroundColor Cyan
    foreach ($f in $Features) {
        $state = (Get-WindowsOptionalFeature -Online -FeatureName $f).State
        if ($state -eq 'Enabled') { Write-Same $f; continue }
        Invoke-TSICDevChange "enable $f" {
            Enable-WindowsOptionalFeature -Online -FeatureName $f -All -NoRestart | Out-Null
            if ((Get-WindowsOptionalFeature -Online -FeatureName $f).State -ne 'Enabled') { throw "$f did not enable." }
        }
        $installed = $true
    }

    # --- 1b. Unlock handlers + modules --------------------------------------------
    # The API's web.config registers the aspNetCore handler and removes the IIS
    # CORS module. A plain IIS install locks both sections (overrideModeDefault=
    # Deny); the ASP.NET 4.x features would unlock them as a side effect, but we
    # do not install those. Locked => every request is 500.19 (0x80070021) and
    # never reaches the app. Hit on the first OVH deploy, 10-16.
    if (Test-Path $Appcmd) {
        Add-Type -Path (Join-Path $env:windir 'System32\inetsrv\Microsoft.Web.Administration.dll')
        foreach ($section in 'system.webServer/handlers', 'system.webServer/modules') {
            $sm = New-Object Microsoft.Web.Administration.ServerManager
            try { $mode = [string]$sm.GetApplicationHostConfiguration().GetSection($section).OverrideModeEffective } finally { $sm.Dispose() }
            if ($mode -eq 'Allow') { Write-Same "$section unlocked for site web.config" }
            else {
                Invoke-TSICDevChange "unlock $section (was $mode)" {
                    & $Appcmd unlock config "-section:$section" | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "appcmd unlock $section failed (exit $LASTEXITCODE)." }
                }
            }
        }
    } elseif ($CheckOnly) {
        Write-Host "  WOULD    unlock system.webServer/handlers and /modules (after IIS installs)" -ForegroundColor Magenta; $script:TSICDevDrift++
    }

    # --- 2. URL Rewrite --------------------------------------------------------
    Write-Host ""
    Write-Host "URL Rewrite 2.1" -ForegroundColor Cyan
    if (Test-Path $UrlRewriteKey) { Write-Same 'URL Rewrite installed' }
    else {
        Invoke-TSICDevChange 'install URL Rewrite 2.1' {
            New-Item -ItemType Directory -Force $Downloads | Out-Null
            $msi = Join-Path $Downloads 'rewrite_amd64_en-US.msi'
            Invoke-WebRequest $UrlRewriteMsi -OutFile $msi -UseBasicParsing
            Assert-MicrosoftSigned $msi
            $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart /l*v `"$msi.log`"" -Wait -PassThru
            if ($p.ExitCode -notin 0, 3010) { throw "URL Rewrite install failed (exit $($p.ExitCode)). Log: $msi.log" }
            if (-not (Test-Path $UrlRewriteKey)) { throw 'URL Rewrite install reported success but is not registered.' }
        }
        $installed = $true
    }

    # --- 3. Hosting Bundle -----------------------------------------------------
    Write-Host ""
    Write-Host ".NET Hosting Bundle $($TSICDev.HostingBundleVersion)" -ForegroundColor Cyan
    if (Test-HostingBundle) { Write-Same "Hosting Bundle $($TSICDev.HostingBundleVersion) installed and registered" }
    else {
        Invoke-TSICDevChange "install .NET Hosting Bundle $($TSICDev.HostingBundleVersion)" {
            $channel = ($TSICDev.HostingBundleVersion -split '\.')[0..1] -join '.'
            $meta = Invoke-RestMethod "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$channel/releases.json"
            $rel = @($meta.releases | Where-Object { $_.'release-version' -eq $TSICDev.HostingBundleVersion })
            if ($rel.Count -ne 1) { throw "Release $($TSICDev.HostingBundleVersion) not found in the $channel release metadata." }
            $file = @($rel[0].'aspnetcore-runtime'.files | Where-Object { $_.name -like '*hosting*win*.exe' })
            if ($file.Count -ne 1) { throw "No single hosting-bundle file in release $($TSICDev.HostingBundleVersion)." }
            New-Item -ItemType Directory -Force $Downloads | Out-Null
            $exe = Join-Path $Downloads (Split-Path $file[0].url -Leaf)
            Invoke-WebRequest $file[0].url -OutFile $exe -UseBasicParsing
            if ((Get-FileHash $exe -Algorithm SHA512).Hash -ne $file[0].hash.ToUpper()) { throw "Hosting Bundle SHA-512 does not match Microsoft's published hash." }
            Assert-MicrosoftSigned $exe
            $p = Start-Process $exe -ArgumentList "/install /quiet /norestart /log `"$exe.log`"" -Wait -PassThru
            if ($p.ExitCode -notin 0, 3010) { throw "Hosting Bundle install failed (exit $($p.ExitCode)). Log: $exe.log" }
        }
        $installed = $true
    }

    # --- 4. iisreset + proof -----------------------------------------------------
    if ($installed -and -not $CheckOnly) {
        Write-Host ""
        & iisreset /restart | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "iisreset failed (exit $LASTEXITCODE)." }
        Write-Changed 'iisreset'
    }
    if (-not $CheckOnly) {
        Write-Host ""
        Write-Host "Proof" -ForegroundColor Cyan
        if ((Get-Service W3SVC).Status -ne 'Running') { throw 'W3SVC is not running.' }
        Write-Ok 'W3SVC running'
        foreach ($m in 'RewriteModule', 'AspNetCoreModuleV2') {
            if (-not (Test-GlobalModule $m)) { throw "$m is not registered with IIS." }
            Write-Ok "$m registered with IIS"
        }
        if (-not (Test-HostingBundle)) { throw "ASP.NET Core runtime $($TSICDev.HostingBundleVersion) not present after install." }
        Write-Ok "Microsoft.AspNetCore.App $($TSICDev.HostingBundleVersion) present"
        if ((Get-WindowsOptionalFeature -Online -FeatureName 'IIS-WebServerRole').RestartNeeded) {
            Write-Host "  NOTE: Windows reports a restart is needed to finish the feature install." -ForegroundColor Yellow
        }
    }

    Complete-TSICDevStep 'IIS + URL Rewrite + Hosting Bundle'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
