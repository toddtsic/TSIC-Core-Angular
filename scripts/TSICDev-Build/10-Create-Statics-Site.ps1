# =============================================================================
# 10-Create-Statics-Site.ps1 -- a statics website: static files only, prod's own rules
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\10-Create-Statics-Site.ps1 [-CheckOnly]
#
# Needs 02 (IIS), 03 (certificate) and 05 (folders) first, and the statics
# content in place (iDrive restore of PHOENIX's statics folder).
#
# Defaults build Staging's statics (names in TSICDev-Config.ps1):
#
#   dev-statics   https *:443 devstatics.teamsportsinfo.com -> C:\Websites\TSIC-STATICS-DEV
#
# -Root / -SiteName / -HostName build another statics site with the same script
# (the DR mirror of prod's statics, later). Site name = pool name.
#
# The RULES are not written by this script. They are prod's own web.config
# files, which arrive with the content:
#
#   <root>\web.config              hides RegFileUploads (med forms: the API serves
#                                  those itself, after checking the user)
#   <root>\BannerFiles\web.config  Cache-Control no-cache (a re-uploaded banner
#                                  keeps its filename and must be revalidated)
#
# The site is not created without both, so it never serves, even briefly,
# without prod's rules. The proof below then checks the rules actually work.
#
# Pool: No Managed Code -- IIS's static file handler only. Read access: IIS_IUSRS
# read + execute on the root, as 05 grants for dev-app. Uploads written later by
# the dev-api pool inherit it.
#
# Proof, against 127.0.0.1 with the site's host name -- NEVER through DNS. Until
# the host name has its own A record it falls through the * wildcard to PHOENIX,
# and a check by name would be testing PROD's statics and pass.
#   - the *.teamsportsinfo.com certificate is presented
#   - HEAD of a real file in BannerFiles  -> 200, Cache-Control no-cache
#   - HEAD of a probe file written into RegFileUploads -> 404 (probe removed after)
# =============================================================================

#Requires -RunAsAdministrator

param(
    [switch]$CheckOnly,
    [string]$Root,
    [string]$SiteName,
    [string]$HostName
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

if (-not $Root)     { $Root     = $TSICDev.StaticsDir }
if (-not $SiteName) { $SiteName = $TSICDev.StaticsSite }
if (-not $HostName) { $HostName = $TSICDev.StaticsHost }

$Binding      = "https/*:443:$HostName"
$RootConfig   = Join-Path $Root 'web.config'
$BannerDir    = Join-Path $Root 'BannerFiles'
$BannerConfig = Join-Path $BannerDir 'web.config'
$HiddenDir    = Join-Path $Root 'RegFileUploads'

# Opens TLS to 127.0.0.1:443 with SNI = $HostName. Returns the certificate
# thumbprint, and for -Path also the HTTP status code and response headers of a
# HEAD request (Host: $HostName). No DNS involved.
function Invoke-LocalHead {
    param([string]$HostName, [string]$Path)
    $tcp = New-Object Net.Sockets.TcpClient('127.0.0.1', 443)
    try {
        $ssl = New-Object Net.Security.SslStream($tcp.GetStream(), $false, ({ $true }))
        try {
            $ssl.AuthenticateAsClient($HostName)
            $result = @{
                Thumbprint = (New-Object Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)).Thumbprint
                Status     = 0
                Headers    = @{}
            }
            if ($Path) {
                $req = "HEAD $Path HTTP/1.1`r`nHost: $HostName`r`nConnection: close`r`n`r`n"
                $bytes = [Text.Encoding]::ASCII.GetBytes($req)
                $ssl.Write($bytes, 0, $bytes.Length)
                $ssl.Flush()
                $reader = New-Object IO.StreamReader($ssl, [Text.Encoding]::ASCII)
                $status = $reader.ReadLine()
                if ($status -notmatch '^HTTP/1\.[01] (\d{3})') { throw "Unexpected response from 127.0.0.1 for $HostName$Path : '$status'" }
                $result.Status = [int]$Matches[1]
                while ($true) {
                    $line = $reader.ReadLine()
                    if ($null -eq $line -or $line -eq '') { break }
                    $i = $line.IndexOf(':')
                    if ($i -gt 0) { $result.Headers[$line.Substring(0, $i).Trim()] = $line.Substring($i + 1).Trim() }
                }
            }
            $result
        } finally { $ssl.Dispose() }
    } finally { $tcp.Dispose() }
}

try {
    Assert-TSICDevBox
    Import-Module WebAdministration

    Write-Host ""
    Write-Host "Statics site $SiteName  ($HostName -> $Root)" -ForegroundColor Cyan

    # --- Content and prod's rules must be there first ---------------------------------
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw "$Root does not exist - run 05-Create-Folders.ps1 and restore the statics content first." }
    foreach ($wc in @($RootConfig, $BannerConfig)) {
        if (-not (Test-Path -LiteralPath $wc -PathType Leaf)) {
            throw "$wc is missing. It is prod's own rule file and comes with the statics content (iDrive restore). Not creating a statics site without it."
        }
        [void]([xml](Get-Content -LiteralPath $wc -Raw))
        Write-Same "prod rule file $wc (valid XML)"
    }
    $sample = Get-ChildItem -LiteralPath $BannerDir -File |
        Where-Object { $_.Name -ne 'web.config' -and $_.Name -match '^[A-Za-z0-9._-]+$' } |
        Select-Object -First 1
    if (-not $sample) { throw "$BannerDir has no files - restore the statics content first." }
    Write-Same "content present (sample BannerFiles\$($sample.Name))"

    $cert = @(Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { $_.Subject -eq $TSICDev.CertSubject -and $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending) | Select-Object -First 1
    if (-not $cert) { throw "No valid $($TSICDev.CertSubject) certificate in LocalMachine\My - run 03-Import-Certificate.ps1 first." }

    # --- Read access --------------------------------------------------------------------
    Grant-TSICDevAccess -Path $Root -Identity 'BUILTIN\IIS_IUSRS' -Rights 'ReadAndExecute, Synchronize'

    # --- Pool -----------------------------------------------------------------------------
    if (-not (Test-Path "IIS:\AppPools\$SiteName")) {
        Invoke-TSICDevChange "create pool $SiteName (No Managed Code)" {
            New-WebAppPool -Name $SiteName | Out-Null
            Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ''
        }
    } else {
        Write-Same "pool $SiteName exists"
        if ((Get-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion).Value -ne '') {
            Invoke-TSICDevChange "pool $SiteName -> No Managed Code" { Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value '' }
        } else { Write-Same "pool $SiteName No Managed Code" }
    }

    # --- Site -----------------------------------------------------------------------------
    $site = Get-Website | Where-Object { $_.Name -eq $SiteName }
    if (-not $site) {
        Invoke-TSICDevChange "create site $SiteName ($Binding)" {
            New-Website -Name $SiteName -PhysicalPath $Root -ApplicationPool $SiteName -Ssl -Port 443 -HostHeader $HostName | Out-Null
        }
    } else {
        Write-Same "site $SiteName exists"
        if ($site.physicalPath -ne $Root) {
            Invoke-TSICDevChange "$SiteName path '$($site.physicalPath)' -> '$Root'" { Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $Root }
        } else { Write-Same "$SiteName path $Root" }
        if ($site.applicationPool -ne $SiteName) {
            Invoke-TSICDevChange "$SiteName pool '$($site.applicationPool)' -> '$SiteName'" { Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $SiteName }
        } else { Write-Same "$SiteName pool $SiteName" }
        $have = @(Get-WebBinding -Name $SiteName | ForEach-Object { "$($_.protocol)/$($_.bindingInformation)" })
        if ($have -contains $Binding) { Write-Same "binding $Binding" }
        else { Invoke-TSICDevChange "add binding $Binding" { New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName } }
        foreach ($extra in @($have | Where-Object { $_ -ne $Binding })) {
            Write-Host "  NOTE     extra binding $extra on $SiteName (not in this script; left alone)" -ForegroundColor Yellow
        }
    }

    # --- Start + proof ----------------------------------------------------------------------
    if ($CheckOnly) {
        $w = Get-Website | Where-Object { $_.Name -eq $SiteName }
        if ($w -and $w.State -ne 'Started') { Write-Host "  WOULD    start $SiteName (is $($w.State))" -ForegroundColor Magenta; $script:TSICDevDrift++ }
        Complete-TSICDevStep "Statics site $SiteName"
    }

    if ((Get-Website | Where-Object { $_.Name -eq $SiteName }).State -ne 'Started') { Start-Website -Name $SiteName; Write-Changed "started $SiteName" }

    Write-Host ""
    Write-Host "Proof (127.0.0.1, Host: $HostName -- no DNS)" -ForegroundColor Cyan

    $r = Invoke-LocalHead -HostName $HostName
    if ($r.Thumbprint -ne $cert.Thumbprint) { throw "https://$HostName on this box presents $($r.Thumbprint), expected $($cert.Thumbprint)." }
    Write-Ok "certificate $($cert.Thumbprint)"

    $r = Invoke-LocalHead -HostName $HostName -Path "/BannerFiles/$($sample.Name)"
    if ($r.Status -ne 200) { throw "HEAD /BannerFiles/$($sample.Name) returned $($r.Status), expected 200." }
    $cc = if ($r.Headers.ContainsKey('Cache-Control')) { $r.Headers['Cache-Control'] } else { '' }
    if ($cc -notmatch 'no-cache') { throw "BannerFiles rule not in effect: Cache-Control is '$cc', expected no-cache (prod's BannerFiles\web.config)." }
    Write-Ok "BannerFiles/$($sample.Name) 200, Cache-Control: $cc"

    if (-not (Test-Path -LiteralPath $HiddenDir)) { New-Item -ItemType Directory -Path $HiddenDir | Out-Null }
    $probeName = "probe-$([guid]::NewGuid().ToString('N')).txt"
    $probe = Join-Path $HiddenDir $probeName
    Set-Content -LiteralPath $probe -Value 'statics site probe' -Encoding ASCII
    try {
        $r = Invoke-LocalHead -HostName $HostName -Path "/RegFileUploads/$probeName"
    } finally { Remove-Item -LiteralPath $probe -Force }
    if ($r.Status -ne 404) { throw "RegFileUploads is NOT hidden: a file there returned $($r.Status), expected 404 (prod's root web.config)." }
    Write-Ok "RegFileUploads hidden (probe file -> 404, removed)"

    Complete-TSICDevStep "Statics site $SiteName"
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
