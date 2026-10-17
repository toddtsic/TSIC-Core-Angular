# =============================================================================
# 06-Create-Sites.ps1 -- dev-api and dev-app IIS sites, bindings and certificate
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\06-Create-Sites.ps1 [-CheckOnly]
#
# Needs 03 (certificate), 04 (pools) and 05 (folders) first.
#
#   dev-api   https *:443 devapi.teamsportsinfo.com   -> C:\Websites\dev-api, pool dev-api
#   dev-app   https *:443 dev.teamsportsinfo.com      -> C:\Websites\dev-app, pool dev-app
#             http  *:80  dev.teamsportsinfo.com      (web.config redirects to https)
#
# Certificate: ONE binding on 0.0.0.0:443 to the newest valid *.teamsportsinfo.com
# certificate in LocalMachine\My. One wildcard covers every hostname on the box,
# so per-hostname (SNI) bindings would add nothing. After a certificate renewal
# (03), re-run this: it moves the binding to the new certificate.
#
# Also stops "Default Web Site" and stops it starting with IIS: with it gone, a
# request for any other hostname gets no answer instead of the IIS welcome page.
#
# Proof: an HTTPS handshake to 127.0.0.1 for each hostname must present that
# certificate. No DNS involved.
# =============================================================================

#Requires -RunAsAdministrator

param([switch]$CheckOnly)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

$Sites = @(
    [pscustomobject]@{ Name = $TSICDev.ApiPool; Path = $TSICDev.ApiPath; Pool = $TSICDev.ApiPool; Host = $TSICDev.ApiHost
                       Bindings = @("https/*:443:$($TSICDev.ApiHost)") },
    [pscustomobject]@{ Name = $TSICDev.AppPool; Path = $TSICDev.AppPath; Pool = $TSICDev.AppPool; Host = $TSICDev.AppHost
                       Bindings = @("https/*:443:$($TSICDev.AppHost)", "http/*:80:$($TSICDev.AppHost)") }
)
$SslBinding = 'IIS:\SslBindings\0.0.0.0!443'

function Get-SiteBindings {
    param([string]$Name)
    @(Get-WebBinding -Name $Name | ForEach-Object { "$($_.protocol)/$($_.bindingInformation)" })
}

# The certificate presented for $HostName on 127.0.0.1:443 (SNI set to that name).
function Get-PresentedThumbprint {
    param([string]$HostName)
    $tcp = New-Object Net.Sockets.TcpClient('127.0.0.1', 443)
    try {
        $ssl = New-Object Net.Security.SslStream($tcp.GetStream(), $false, ({ $true }))
        try {
            $ssl.AuthenticateAsClient($HostName)
            (New-Object Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)).Thumbprint
        } finally { $ssl.Dispose() }
    } finally { $tcp.Dispose() }
}

try {
    Assert-TSICDevBox
    Import-Module WebAdministration

    $cert = @(Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq $TSICDev.CertSubject -and $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending) | Select-Object -First 1
    if (-not $cert) { throw "No valid $($TSICDev.CertSubject) certificate in LocalMachine\My - run 03-Import-Certificate.ps1 first." }

    foreach ($s in $Sites) {
        Write-Host ""
        Write-Host "Site $($s.Name)" -ForegroundColor Cyan
        if (-not (Test-Path -LiteralPath $s.Path)) { throw "$($s.Path) does not exist - run 05-Create-Folders.ps1 first." }
        if (-not (Test-Path "IIS:\AppPools\$($s.Pool)")) { throw "Pool $($s.Pool) does not exist - run 04-Create-AppPools.ps1 first." }

        $site = Get-Website | Where-Object { $_.Name -eq $s.Name }
        if (-not $site) {
            $first = $s.Bindings[0] -split '[/:]'      # https, *, 443, host
            Invoke-TSICDevChange "create site $($s.Name) ($($s.Bindings[0]))" {
                New-Website -Name $s.Name -PhysicalPath $s.Path -ApplicationPool $s.Pool -Ssl -Port 443 -HostHeader $first[3] | Out-Null
            }
            if ($CheckOnly) { continue }
            $site = Get-Website | Where-Object { $_.Name -eq $s.Name }
        } else { Write-Same "site $($s.Name) exists" }

        if ($site.physicalPath -ne $s.Path) {
            Invoke-TSICDevChange "$($s.Name) path '$($site.physicalPath)' -> '$($s.Path)'" { Set-ItemProperty "IIS:\Sites\$($s.Name)" -Name physicalPath -Value $s.Path }
        } else { Write-Same "$($s.Name) path $($s.Path)" }
        if ($site.applicationPool -ne $s.Pool) {
            Invoke-TSICDevChange "$($s.Name) pool '$($site.applicationPool)' -> '$($s.Pool)'" { Set-ItemProperty "IIS:\Sites\$($s.Name)" -Name applicationPool -Value $s.Pool }
        } else { Write-Same "$($s.Name) pool $($s.Pool)" }

        $have = Get-SiteBindings $s.Name
        foreach ($b in $s.Bindings) {
            if ($have -contains $b) { Write-Same "binding $b"; continue }
            $p = $b -split '[/:]'
            Invoke-TSICDevChange "add binding $b" { New-WebBinding -Name $s.Name -Protocol $p[0] -Port $p[2] -HostHeader $p[3] }
        }
        foreach ($extra in @($have | Where-Object { $s.Bindings -notcontains $_ })) {
            Write-Host "  NOTE     extra binding $extra on $($s.Name) (not in this script; left alone)" -ForegroundColor Yellow
        }
    }

    # --- Certificate binding ------------------------------------------------------
    Write-Host ""
    Write-Host "Certificate binding 0.0.0.0:443" -ForegroundColor Cyan
    $existing = Get-Item $SslBinding -ErrorAction SilentlyContinue
    if ($existing -and $existing.Thumbprint -eq $cert.Thumbprint) {
        Write-Same "bound to $($cert.Thumbprint) (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"
    } else {
        $from = if ($existing) { $existing.Thumbprint } else { 'nothing' }
        Invoke-TSICDevChange "bind 0.0.0.0:443 to $($cert.Thumbprint) (was $from)" {
            if ($existing) { Remove-Item $SslBinding }
            Get-Item "Cert:\LocalMachine\My\$($cert.Thumbprint)" | New-Item $SslBinding | Out-Null
        }
    }

    # --- Default Web Site ------------------------------------------------------------
    Write-Host ""
    Write-Host "Default Web Site" -ForegroundColor Cyan
    $dws = Get-Website | Where-Object { $_.Name -eq 'Default Web Site' }
    if (-not $dws) { Write-Same 'not present' }
    else {
        if ($dws.State -ne 'Stopped') { Invoke-TSICDevChange 'stop Default Web Site' { Stop-Website -Name 'Default Web Site' } }
        else { Write-Same 'stopped' }
        if ($dws.serverAutoStart -ne $false) {
            Invoke-TSICDevChange 'Default Web Site: do not start with IIS' { Set-ItemProperty 'IIS:\Sites\Default Web Site' -Name serverAutoStart -Value $false }
        } else { Write-Same 'does not start with IIS' }
    }

    # --- Start + proof ------------------------------------------------------------------
    if (-not $CheckOnly) {
        Write-Host ""
        Write-Host "Proof" -ForegroundColor Cyan
        foreach ($s in $Sites) {
            if ((Get-Website | Where-Object { $_.Name -eq $s.Name }).State -ne 'Started') { Start-Website -Name $s.Name; Write-Changed "started $($s.Name)" }
            $tp = Get-PresentedThumbprint $s.Host
            if ($tp -ne $cert.Thumbprint) { throw "https://$($s.Host) on this box presents $tp, expected $($cert.Thumbprint)." }
            Write-Ok "$($s.Name) started; https://$($s.Host) presents $($cert.Thumbprint)"
        }
    } else {
        foreach ($s in $Sites) {
            $w = Get-Website | Where-Object { $_.Name -eq $s.Name }
            if ($w -and $w.State -ne 'Started') { Write-Host "  WOULD    start $($s.Name) (is $($w.State))" -ForegroundColor Magenta; $script:TSICDevDrift++ }
        }
    }

    Complete-TSICDevStep 'IIS sites'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
