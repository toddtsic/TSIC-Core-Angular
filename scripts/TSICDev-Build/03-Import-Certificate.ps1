# =============================================================================
# 03-Import-Certificate.ps1 -- the *.teamsportsinfo.com wildcard into the machine store
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\03-Import-Certificate.ps1 -PfxPath <file.pfx>
#   .\scripts\TSICDev-Build\03-Import-Certificate.ps1 -CheckOnly
#
# Imports the .pfx into LocalMachine\My with the private key NON-EXPORTABLE (the
# .pfx itself, kept off-box in IDrive, stays the only copy that can be moved).
# The password is asked for at the prompt and never echoed, logged or stored.
#
# Certificates are now reissued about every 200 days (CA/Browser Forum limit
# since 2026-03-15), so this is a RENEWAL step too: import the new .pfx here,
# then re-run 06-Create-Sites.ps1 so IIS binds the newest certificate.
#
# -CheckOnly: no import; reports the newest usable certificate and its expiry.
# After importing, delete the local .pfx copy -- a .pfx plus its password is
# everything needed to impersonate the sites.
# =============================================================================

#Requires -RunAsAdministrator

param(
    [string]$PfxPath,
    [switch]$CheckOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

# Newest certificate in LocalMachine\My for our subject, with its private key,
# currently valid. 06-Create-Sites.ps1 binds the same selection.
function Get-SiteCertificate {
    @(Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq $TSICDev.CertSubject -and $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending) | Select-Object -First 1
}

try {
    Assert-TSICDevBox
    Write-Host ""
    Write-Host "Certificate $($TSICDev.CertSubject)" -ForegroundColor Cyan

    if (-not $CheckOnly) {
        if (-not $PfxPath) { throw 'Pass -PfxPath <file.pfx> (or -CheckOnly).' }
        if (-not (Test-Path -LiteralPath $PfxPath)) { throw "Not found: $PfxPath" }
        $pw = Read-Host 'PFX password' -AsSecureString
        $imported = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $pw
        $pw.Dispose()
        foreach ($c in @($imported)) { Write-Changed "imported $($c.Subject)  $($c.Thumbprint)  expires $($c.NotAfter.ToString('yyyy-MM-dd'))" }
    }

    $cert = Get-SiteCertificate
    if (-not $cert) {
        if ($CheckOnly) {
            Write-Host "  WOULD    import a $($TSICDev.CertSubject) certificate (none usable in LocalMachine\My)" -ForegroundColor Magenta
            $script:TSICDevDrift++
            Complete-TSICDevStep 'Certificate'
        }
        throw "No valid $($TSICDev.CertSubject) certificate with a private key in LocalMachine\My."
    }

    $chain = New-Object Security.Cryptography.X509Certificates.X509Chain
    $chain.ChainPolicy.RevocationMode = 'Online'
    if (-not $chain.Build($cert)) {
        throw "Certificate chain does not validate: $((@($chain.ChainStatus | ForEach-Object { $_.Status }) -join ', '))"
    }
    $days = [int]($cert.NotAfter - (Get-Date)).TotalDays
    Write-Ok "$($cert.Subject)  $($cert.Thumbprint)"
    Write-Ok "private key present, chain valid (revocation checked online)"
    if ($days -lt 30) { Write-Host "  WARNING  expires $($cert.NotAfter.ToString('yyyy-MM-dd')) - $days days left. Renew now." -ForegroundColor Red }
    else { Write-Ok "expires $($cert.NotAfter.ToString('yyyy-MM-dd')) ($days days)" }

    Complete-TSICDevStep 'Certificate'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
