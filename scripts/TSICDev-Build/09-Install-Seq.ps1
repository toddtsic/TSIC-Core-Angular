# =============================================================================
# 09-Install-Seq.ps1 -- Seq log server + its HTTPS front door (seq.teamsportsinfo.com)
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\09-Install-Seq.ps1 -CheckOnly
#   .\scripts\TSICDev-Build\09-Install-Seq.ps1 [-TemplatesDir C:\Users\admin\seq-templates]
#
# Needs 02 (IIS + URL Rewrite) and 03 (certificate). Re-run 06 is NOT needed:
# the 0.0.0.0:443 certificate binding already covers this hostname.
#
# ONE Seq instance for prod and Staging (ruled 10-16):
#   prod (PHOENIX)  -> https://seq.teamsportsinfo.com -> IIS site 'Seq' -> ARR -> localhost:5341
#   Staging (here)  -> http://localhost:5341 (appsettings.json default, no change)
#
# 1. ARR 3.0 (Application Request Routing) + server-wide proxy on. The 'Seq'
#    site's web.config is one rewrite rule: everything -> http://localhost:5341/.
#    That is the whole of SEDONA's setup, copied.
# 2. Seq $TSICDev.SeqVersion (Datalust-signed MSI), as a Windows service listening
#    on localhost only -- 5341 is never exposed; outside traffic comes via 443.
#    Admin user 'admin'; the password is asked for at the prompt on FIRST
#    install only, stored by Seq as a salted hash, never echoed or logged.
#    Ingestion stays anonymous (apps log without a key, as on SEDONA).
# 3. Retention (SEDONA had none): Error/Fatal kept $TSICDev.SeqKeepErrorsDays days,
#    everything else $TSICDev.SeqKeepAllDays. seqcli (via a temporary API key you
#    make in the UI; the script deletes it) creates the signal and the delete-all
#    rule; the signal-scoped rule is one manual click-through the script prints,
#    because this seqcli build cannot create signal-scoped rules.
# 4. -TemplatesDir: optional. Imports signals / dashboards / queries exported
#    from SEDONA (seqcli template export), merging with same-named defaults.
#
# Not carried over from SEDONA, on purpose: event history (fresh start, ruled
# 10-16), the 'SES alert email' app + its alert (closed issue), API keys (none used).
# =============================================================================

#Requires -RunAsAdministrator

param(
    [string]$TemplatesDir,
    [switch]$CheckOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

$SeqExe     = 'C:\Program Files\Seq\Seq.exe'
$SeqCli     = 'C:\Program Files\Seq\Client\seqcli.exe'
$SeqUrl     = $TSICDev.SeqListen.TrimEnd('/')
$ArrKey     = 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\Application Request Routing'
$ArrMsi     = 'https://download.microsoft.com/download/E/9/8/E9849D6A-020E-47E4-9FD0-A023E99B54EB/requestRouter_amd64.msi'
$SeqMsiUrl  = "https://datalust.co/download/begin?version=$($TSICDev.SeqVersion)"
$Downloads  = Join-Path $env:TEMP 'TSICDev-installers'
$TempKeyTitle   = 'TSICDev-Build-temp'
$RetentionTitle = 'Retention: everything except errors'

$WebConfig = @"
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
    <system.webServer>
        <rewrite>
            <rules>
                <rule name="ReverseProxyInboundRule1" stopProcessing="true">
                    <match url="(.*)" />
                    <action type="Rewrite" url="$($TSICDev.SeqListen){R:1}" />
                </rule>
            </rules>
        </rewrite>
    </system.webServer>
</configuration>
"@

function Assert-Signed {
    param([string]$Path, [string]$Publisher)
    $sig = Get-AuthenticodeSignature $Path
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch [regex]::Escape($Publisher)) {
        throw "$Path is not validly signed by $Publisher ($($sig.Status), $($sig.SignerCertificate.Subject))."
    }
}

function Install-Msi {
    param([string]$Url, [string]$File, [string]$Publisher)
    New-Item -ItemType Directory -Force $Downloads | Out-Null
    $msi = Join-Path $Downloads $File
    Invoke-WebRequest $Url -OutFile $msi -UseBasicParsing
    Assert-Signed $msi $Publisher
    $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart /l*v `"$msi.log`"" -Wait -PassThru
    if ($p.ExitCode -notin 0, 3010) { throw "$File install failed (exit $($p.ExitCode)). Log: $msi.log" }
}

# Native call with the exit code as the verdict (stderr is not an error).
function Invoke-Native {
    param([string]$Exe, [string[]]$ArgList, [string]$StdIn)
    $ErrorActionPreference = 'Continue'
    $out = if ($PSBoundParameters.ContainsKey('StdIn')) { $StdIn | & $Exe @ArgList 2>&1 } else { & $Exe @ArgList 2>&1 }
    $code = $LASTEXITCODE
    $text = @($out | ForEach-Object { "$_" })
    if ($code -ne 0) { throw "$(Split-Path $Exe -Leaf) $($ArgList[0..1] -join ' ') failed (exit $code): $($text -join ' ')" }
    $text
}

function Test-SeqUp {
    try { (Invoke-WebRequest "$SeqUrl/api" -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { $false }
}

try {
    Assert-TSICDevBox
    Import-Module WebAdministration
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    # --- 1. ARR + proxy --------------------------------------------------------
    Write-Host ""
    Write-Host "Application Request Routing" -ForegroundColor Cyan
    if (Test-Path $ArrKey) { Write-Same 'ARR installed' }
    else {
        Invoke-TSICDevChange 'install ARR 3.0' {
            Install-Msi $ArrMsi 'requestRouter_amd64.msi' 'O=Microsoft Corporation'
            if (-not (Test-Path $ArrKey)) { throw 'ARR install reported success but is not registered.' }
        }
    }
    if (Test-Path $ArrKey) {
        $proxy = (Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name enabled).Value
        if ($proxy -eq $true) { Write-Same 'proxy enabled server-wide' }
        else { Invoke-TSICDevChange 'enable ARR proxy server-wide' { Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name enabled -Value $true } }
    } elseif ($CheckOnly) { Write-Host "  WOULD    enable ARR proxy server-wide" -ForegroundColor Magenta; $script:TSICDevDrift++ }

    # --- 2. Seq -------------------------------------------------------------------
    Write-Host ""
    Write-Host "Seq $($TSICDev.SeqVersion)" -ForegroundColor Cyan
    $installed = (Test-Path $SeqExe) -and ((Get-Item $SeqExe).VersionInfo.ProductVersion -like "$($TSICDev.SeqVersion)*")
    if ($installed) { Write-Same "Seq $($TSICDev.SeqVersion) installed" }
    elseif (Test-Path $SeqExe) { throw "Seq $((Get-Item $SeqExe).VersionInfo.ProductVersion) is installed, config says $($TSICDev.SeqVersion). Upgrade by hand, then set SeqVersion." }
    else { Invoke-TSICDevChange "install Seq $($TSICDev.SeqVersion)" { Install-Msi $SeqMsiUrl "Seq-$($TSICDev.SeqVersion).msi" 'Datalust' } }

    $svc = Get-Service -Name seq -ErrorAction SilentlyContinue
    if ($svc) { Write-Same 'Seq service registered' }
    else {
        Invoke-TSICDevChange "register Seq service (storage $($TSICDev.SeqStorage), listen $($TSICDev.SeqListen))" {
            Invoke-Native $SeqExe @('service', 'install', "--storage=$($TSICDev.SeqStorage)", "--listen=$($TSICDev.SeqListen)") | Out-Null
        }
    }

    # Admin password BEFORE the first start: Seq reads firstRun.* once, when it
    # first starts. Its own step (not part of registering the service), so a run
    # interrupted between the two still sets it next time.
    $seqJson = Join-Path $TSICDev.SeqStorage 'Seq.json'
    $hashSet = (Test-Path $seqJson) -and [bool]((Get-Content $seqJson -Raw | ConvertFrom-Json).firstRun.adminPasswordHash)
    if ($hashSet) { Write-Same 'admin password configured' }
    else {
        Invoke-TSICDevChange "set Seq admin user 'admin' and password (asked for now)" {
            $pw1 = Read-Host 'New Seq admin password' -AsSecureString
            $pw2 = Read-Host 'Again' -AsSecureString
            $b1 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($pw1)
            $b2 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($pw2)
            try {
                $p1 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b1)
                $p2 = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b2)
                if ($p1 -cne $p2) { throw 'Passwords do not match.' }
                if ($p1.Length -lt 8) { throw 'Use at least 8 characters.' }
                $hash = (Invoke-Native $SeqExe @('config', 'hash') -StdIn $p1 | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim()
            } finally {
                [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b1); [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b2)
                $p1 = $null; $p2 = $null
            }
            Invoke-Native $SeqExe @('config', 'set', '-k', 'firstRun.adminUsername', '-v', 'admin', "--storage=$($TSICDev.SeqStorage)") | Out-Null
            Invoke-Native $SeqExe @('config', 'set', '-k', 'firstRun.adminPasswordHash', '--value-stdin', "--storage=$($TSICDev.SeqStorage)") -StdIn $hash | Out-Null
        }
    }

    # Settings that match SEDONA's Seq.json.
    if (Test-Path (Join-Path $TSICDev.SeqStorage 'Seq.json')) {
        $cfg = Get-Content (Join-Path $TSICDev.SeqStorage 'Seq.json') -Raw | ConvertFrom-Json
        $want = "https://$($TSICDev.SeqHost)"
        if ($cfg.api.canonicalUri -eq $want) { Write-Same "canonicalUri $want" }
        else {
            Invoke-TSICDevChange "canonicalUri -> $want" {
                Invoke-Native $SeqExe @('config', 'set', '-k', 'api.canonicalUri', '-v', $want, "--storage=$($TSICDev.SeqStorage)") | Out-Null
                if ((Get-Service seq).Status -eq 'Running') { Restart-Service seq }
            }
        }
        $listen = @($cfg.api.listenUris)
        if ($listen.Count -eq 1 -and $listen[0] -eq $TSICDev.SeqListen) { Write-Same "listens on $($TSICDev.SeqListen) only" }
        else { throw "Seq.json listenUris is '$($listen -join ', ')', expected only $($TSICDev.SeqListen). Fix by hand: 5341 must stay localhost." }
    }

    $svc = Get-Service -Name seq -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.StartType -ne 'Automatic') { Invoke-TSICDevChange 'Seq service startup -> Automatic' { Set-Service seq -StartupType Automatic } }
        else { Write-Same 'Seq service startup Automatic' }
        if ($svc.Status -ne 'Running') { Invoke-TSICDevChange 'start Seq service' { Start-Service seq } }
        else { Write-Same 'Seq service running' }
    }
    if (-not $CheckOnly) {
        $deadline = (Get-Date).AddSeconds(60)
        while (-not (Test-SeqUp)) { if ((Get-Date) -gt $deadline) { throw "Seq did not answer on $SeqUrl/api within 60 s." }; Start-Sleep 2 }
        Write-Ok "Seq answers on $SeqUrl"
    }

    # --- 3. IIS site 'Seq' -----------------------------------------------------
    Write-Host ""
    Write-Host "IIS site $($TSICDev.SeqSite)" -ForegroundColor Cyan
    Confirm-TSICDevFolder $TSICDev.SeqSitePath
    $wc = Join-Path $TSICDev.SeqSitePath 'web.config'
    $have = if (Test-Path $wc) { (Get-Content $wc -Raw).Trim() } else { $null }
    if ($have -eq $WebConfig.Trim()) { Write-Same 'web.config (reverse proxy rule)' }
    else { Invoke-TSICDevChange "write $wc (proxy all -> $($TSICDev.SeqListen))" { [IO.File]::WriteAllText($wc, $WebConfig.Trim() + "`r`n", [Text.Encoding]::ASCII) } }
    if (Test-Path $TSICDev.SeqSitePath) {
        Grant-TSICDevAccess -Path $TSICDev.SeqSitePath -Identity 'BUILTIN\IIS_IUSRS' -Rights 'ReadAndExecute, Synchronize'
    }

    $binding = "https/*:443:$($TSICDev.SeqHost)"
    $site = Get-Website | Where-Object { $_.Name -eq $TSICDev.SeqSite }
    if (-not $site) {
        Invoke-TSICDevChange "create site $($TSICDev.SeqSite) ($binding, pool DefaultAppPool)" {
            New-Website -Name $TSICDev.SeqSite -PhysicalPath $TSICDev.SeqSitePath -ApplicationPool 'DefaultAppPool' -Ssl -Port 443 -HostHeader $TSICDev.SeqHost | Out-Null
        }
    } else {
        Write-Same "site $($TSICDev.SeqSite) exists"
        $b = @(Get-WebBinding -Name $TSICDev.SeqSite | ForEach-Object { "$($_.protocol)/$($_.bindingInformation)" })
        if ($b -contains $binding) { Write-Same "binding $binding" }
        else { Invoke-TSICDevChange "add binding $binding" { New-WebBinding -Name $TSICDev.SeqSite -Protocol https -Port 443 -HostHeader $TSICDev.SeqHost } }
    }
    if (-not $CheckOnly) {
        if ((Get-Website | Where-Object { $_.Name -eq $TSICDev.SeqSite }).State -ne 'Started') { Start-Website -Name $TSICDev.SeqSite; Write-Changed "started site $($TSICDev.SeqSite)" }
        # Through IIS + ARR, the way prod will reach it (SNI to this box, no DNS needed).
        $req = [Net.HttpWebRequest]::Create("https://127.0.0.1/api")
        $req.Host = $TSICDev.SeqHost
        $req.ServerCertificateValidationCallback = { $true }
        try { $resp = $req.GetResponse(); $code = [int]$resp.StatusCode; $resp.Close() } catch { throw "https://$($TSICDev.SeqHost)/api via IIS on this box failed: $($_.Exception.Message)" }
        if ($code -ne 200) { throw "https://$($TSICDev.SeqHost)/api via IIS answered $code." }
        Write-Ok "https://$($TSICDev.SeqHost)/api answers through IIS + ARR"
    }

    # --- 4. Retention + templates (need a running Seq and an admin login) -------
    Write-Host ""
    Write-Host "Retention" -ForegroundColor Cyan
    if (-not (Test-SeqUp)) {
        if ($CheckOnly) { Write-Host "  WOULD    create retention policies (Seq not running yet)" -ForegroundColor Magenta; $script:TSICDevDrift++ }
        else { throw 'Seq is not answering; cannot set retention.' }
    } elseif ($CheckOnly) {
        # Reading retention needs a login and a temporary key - a change. Not in a check.
        Write-Host "  (retention and templates not checked under -CheckOnly: needs an admin login)" -ForegroundColor DarkGray
    } else {
        # A temporary key YOU make in the Seq UI (seqcli's username/password login
        # was refused on this build - 401 - while API keys work), removed in finally
        # by its title.
        Write-Host "  In Seq (http://localhost:5341): Settings > API Keys > Add API Key" -ForegroundColor Yellow
        Write-Host "    Title: $TempKeyTitle   Permissions: add Read, Write, Project, Organization, System" -ForegroundColor Yellow
        Write-Host "    Save, copy the token it shows. The script deletes the key when it is done." -ForegroundColor Yellow
        $sk = Read-Host 'Paste the temporary API key token' -AsSecureString
        $bk = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sk)
        try { $key = ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($bk)).Trim() }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bk) }
        if (-not $key) { throw 'No API key entered.' }
        $conn = @('-s', $SeqUrl, '-a', $key)
        try {
            $policies = @(Invoke-Native $SeqCli (@('retention', 'list', '--json') + $conn) | Where-Object { $_.Trim() })
            if ($policies.Count -gt 0) {
                Write-Same "$($policies.Count) retention policy(ies) exist - left as they are:"
                $policies | ForEach-Object { Write-Host "             $_" -ForegroundColor DarkGray }
            } else {
                # This seqcli build (2025.2.02451) can only create delete-ALL-events
                # policies ("currently the only supported option"), so the shorter,
                # signal-scoped rule is a one-time step in the Seq UI. The script
                # makes the signal it needs and says exactly what to click.
                $sig = @(Invoke-Native $SeqCli (@('signal', 'list', '-t', $RetentionTitle, '--json') + $conn) | Where-Object { $_.Trim() })
                if ($sig.Count -gt 0) { Write-Same "signal '$RetentionTitle'" }
                else {
                    Invoke-TSICDevChange "signal '$RetentionTitle' (@Level not in Error, Fatal)" {
                        Invoke-Native $SeqCli (@('signal', 'create', '-t', $RetentionTitle, '-f', "@Level not in ['Error', 'Fatal']", '--no-group', '--protected') + $conn) | Out-Null
                    }
                }
                Invoke-TSICDevChange "retention: delete ALL events after $($TSICDev.SeqKeepErrorsDays)d" {
                    Invoke-Native $SeqCli (@('retention', 'create', "--after=$($TSICDev.SeqKeepErrorsDays)d", '--delete-all-events') + $conn) | Out-Null
                    $n = @(Invoke-Native $SeqCli (@('retention', 'list', '--json') + $conn) | Where-Object { $_.Trim() }).Count
                    if ($n -ne 1) { throw "Expected 1 retention policy after creating it, found $n." }
                }
                Write-Host ""
                Write-Host "  ONE MANUAL STEP (seqcli cannot make signal-scoped rules):" -ForegroundColor Yellow
                Write-Host "    Seq > Data > Storage > Add Retention Policy:" -ForegroundColor Yellow
                Write-Host "      delete events matching signal '$RetentionTitle' after $($TSICDev.SeqKeepAllDays) days" -ForegroundColor Yellow
            }

            if ($TemplatesDir) {
                Write-Host ""
                Write-Host "Templates from $TemplatesDir" -ForegroundColor Cyan
                if (-not (Test-Path $TemplatesDir)) { throw "Templates folder not found: $TemplatesDir" }
                $state = Join-Path $TSICDev.SeqStorage 'tsicdev-templates.state'
                Invoke-TSICDevChange "import $(@(Get-ChildItem $TemplatesDir -Filter *.template).Count) templates (merge with same-named)" {
                    Invoke-Native $SeqCli (@('template', 'import', '-i', $TemplatesDir, "--state=$state", '--merge') + $conn) | ForEach-Object { Write-Host "    $_" }
                }
            }
        } finally {
            try { Invoke-Native $SeqCli (@('apikey', 'remove', '-t', $TempKeyTitle) + $conn) | Out-Null; Write-Ok "temporary API key '$TempKeyTitle' removed" }
            catch { Write-Host "  WARNING  could not remove temporary API key '$TempKeyTitle' - delete it in Seq: Settings > API Keys." -ForegroundColor Red }
        }
    }

    Complete-TSICDevStep 'Seq'
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
