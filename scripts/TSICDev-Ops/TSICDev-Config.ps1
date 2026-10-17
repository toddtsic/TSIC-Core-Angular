# =============================================================================
# TSICDev-Config.ps1 -- shared settings for scripts\TSICDev-Build and scripts\TSICDev-Ops
# =============================================================================
# The one place that names this box's paths, SQL instance, sites and accounts.
# Every TSICDev script dot-sources it:
#
#   . (Join-Path $PSScriptRoot 'TSICDev-Config.ps1')                 # from TSICDev-Ops
#   . (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')  # from TSICDev-Build
#
# ASCII ONLY, in this file and in every TSICDev script. Windows PowerShell 5.1
# reads a BOM-less file as ANSI, and one UTF-8 character (an em dash) can turn
# into a quote that ends a string early and silently swallows the code after it.
# That is how IIS-Config-Dev\Setup\04 skipped creating dev-app.
# =============================================================================

$TSICDev = @{
    BoxName     = 'TSIC-OVH-DEV'

    # --- SQL Server ------------------------------------------------------------
    SqlInstance     = '.\SS2016'              # SQL Server 2019 Developer; instance name kept from SEDONA
    SqlInstanceName = 'SS2016'
    SqlServiceName  = 'MSSQL$SS2016'          # its service; NT Service\<this> is the account
    DataDir         = 'C:\DBFiles'            # .mdf / .ldf for TSICV5 and TSICLogs

    # --- PHOENIX -> this box over SFTP (account 'phoenix', key only, chrooted) --
    SftpUser       = 'phoenix'
    SftpChroot     = 'C:\TSIC\Phoenix'
    BackupDir      = 'C:\TSIC\Phoenix\backups'   # PHOENIX pushes hourly .bak files here
    DeployDropDir  = 'C:\TSIC\Phoenix\deploy'    # read-only to phoenix
    SshAllowFrom   = '204.17.37.200/29'          # firewall scope for inbound 22 (the colo block)

    # --- IIS -------------------------------------------------------------------
    # Site name = pool name = folder name under WebsitesDir; the shared deploy
    # guard (_deploy-common.ps1 Assert-TsicSafeTarget) insists on exactly that.
    ApiPool        = 'dev-api'
    AppPool        = 'dev-app'
    WebsitesDir    = 'C:\Websites'
    WebBackupsDir  = 'C:\Websites\Backups'    # pre-deploy snapshots, 3 kept per site
    StaticsDir     = 'C:\Websites\TSIC-STATICS'
    ApiHost        = 'devapi.teamsportsinfo.com'
    AppHost        = 'dev.teamsportsinfo.com'
    CertSubject    = 'CN=*.teamsportsinfo.com'
    # .NET Hosting Bundle (ASP.NET Core Module for IIS). Keep in step with the
    # runtime the API targets.
    HostingBundleVersion = '10.0.13'

    # --- Seq (one instance: prod + Staging, see 09-Install-Seq.ps1) --------------
    # Same build as SEDONA had. Prod reaches it as https://seq.teamsportsinfo.com
    # (IIS + ARR reverse proxy); Staging on this box as http://localhost:5341.
    SeqVersion     = '2025.2.15571'
    SeqHost        = 'seq.teamsportsinfo.com'
    SeqListen      = 'http://localhost:5341/'
    SeqStorage     = 'C:\ProgramData\Seq'
    SeqSite        = 'Seq'
    SeqSitePath    = 'C:\Websites\Seq'
    SeqKeepAllDays    = 30     # every event
    SeqKeepErrorsDays = 90     # Error / Fatal kept longer

    # ASPNETCORE_ENVIRONMENT for dev-api. Staging = behaves like prod except email
    # is suppressed and card processing goes to the ADN sandbox (IsSandbox()).
    AspNetEnv   = 'Staging'
    # Untracked (gitignored by **/app-pool-secrets*). Defines one hashtable,
    # $envVars = @{ NAME = 'value'; ... }, of literal strings only.
    SecretsFile = (Join-Path $PSScriptRoot '..\TSICDev-Build\app-pool-secrets.ps1')

    # --- Source -----------------------------------------------------------------
    # Checkout this box deploys from, and the only branch it deploys.
    RepoRoot       = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    DeployBranch   = 'master'
}
$TSICDev.ApiPoolUser = "IIS APPPOOL\$($TSICDev.ApiPool)"
$TSICDev.SqlServiceAccount = "NT Service\$($TSICDev.SqlServiceName)"
$TSICDev.ApiPath     = Join-Path $TSICDev.WebsitesDir $TSICDev.ApiPool
$TSICDev.AppPath     = Join-Path $TSICDev.WebsitesDir $TSICDev.AppPool
$TSICDev.PublishRoot = Join-Path $TSICDev.RepoRoot 'publish'          # where 1a/1b build into
$TSICDev.ApiSource   = Join-Path $TSICDev.RepoRoot 'TSIC-Core-Angular\src\backend\TSIC.API'

function Assert-TSICDevBox {
    if ($env:COMPUTERNAME -ne $TSICDev.BoxName) {
        throw "This script only runs on $($TSICDev.BoxName). This machine is $env:COMPUTERNAME."
    }
}

# --- Build-script helpers ------------------------------------------------------
# Every TSICDev-Build script takes -CheckOnly: report what WOULD change, change
# nothing. Run against a built box, every line should say "same"; any WOULD line
# is drift between the box and the scripts.
#
# Output: one line per item --  same / CHANGED / WOULD / ok.
function Write-Same    { param([string]$Text) Write-Host "  same     $Text" -ForegroundColor DarkGray }
function Write-Changed { param([string]$Text) Write-Host "  CHANGED  $Text" -ForegroundColor Yellow }
function Write-Ok      { param([string]$Text) Write-Host "  ok       $Text" -ForegroundColor Green }

$script:TSICDevDrift = 0

# Runs $__do and reports CHANGED, or under -CheckOnly reports WOULD and counts
# drift. Parameter names are deliberately odd: the scriptblock resolves the
# caller's variables by dynamic scope, and must not see these instead.
function Invoke-TSICDevChange {
    param([string]$__desc, [scriptblock]$__do)
    if (Get-Variable -Name CheckOnly -ValueOnly -ErrorAction SilentlyContinue) {
        Write-Host "  WOULD    $__desc" -ForegroundColor Magenta
        $script:TSICDevDrift++
    } else {
        & $__do
        Write-Changed $__desc
    }
}

function Get-TSICDevSid {
    param([string]$Identity)
    ([Security.Principal.NTAccount]$Identity).Translate([Security.Principal.SecurityIdentifier]).Value
}

# Explicit (not inherited) allow rules on a path, as SID|Rights|InheritanceFlags strings.
function Get-TSICDevExplicitRules {
    param([string]$Path)
    foreach ($r in (Get-Acl -LiteralPath $Path).Access) {
        if ($r.IsInherited -or $r.AccessControlType -ne 'Allow') { continue }
        $sid = try { $r.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { $r.IdentityReference.Value }
        "$sid|$([int]$r.FileSystemRights)|$([int]$r.InheritanceFlags)"
    }
}

function ConvertTo-TSICDevRule {
    param([string]$Identity, [Security.AccessControl.FileSystemRights]$Rights, [Security.AccessControl.InheritanceFlags]$Inherit)
    New-Object Security.AccessControl.FileSystemAccessRule($Identity, $Rights, $Inherit, 'None', 'Allow')
}

# Ensures one explicit allow rule (identity + rights + inheritance) exists on Path.
# Other rules are left alone.
function Grant-TSICDevAccess {
    param(
        [string]$Path,
        [string]$Identity,
        [Security.AccessControl.FileSystemRights]$Rights,
        [Security.AccessControl.InheritanceFlags]$Inherit = 'ContainerInherit, ObjectInherit'
    )
    $want = ConvertTo-TSICDevRule $Identity $Rights $Inherit
    $key  = "$(Get-TSICDevSid $Identity)|$([int]$want.FileSystemRights)|$([int]$want.InheritanceFlags)"
    if (@(Get-TSICDevExplicitRules $Path) -contains $key) { Write-Same "$Identity $Rights on $Path"; return }
    Invoke-TSICDevChange "grant $Identity $Rights on $Path" {
        $acl = Get-Acl -LiteralPath $Path
        $acl.AddAccessRule($want)
        Set-Acl -LiteralPath $Path -AclObject $acl
    }
}

# Makes Path's ACL EXACTLY: inheritance on/off, owner, and these explicit rules
# (array of @{ Id; Rights; Inherit }), nothing else.
function Set-TSICDevExactAcl {
    param([string]$Path, [bool]$Protected, [string]$Owner, [object[]]$Rules)
    $acl  = Get-Acl -LiteralPath $Path
    $have = @(Get-TSICDevExplicitRules $Path | Sort-Object)
    $wantRules = @($Rules | ForEach-Object { ConvertTo-TSICDevRule $_.Id $_.Rights $_.Inherit })
    $want = @($Rules | ForEach-Object {
        $r = ConvertTo-TSICDevRule $_.Id $_.Rights $_.Inherit
        "$(Get-TSICDevSid $_.Id)|$([int]$r.FileSystemRights)|$([int]$r.InheritanceFlags)"
    } | Sort-Object)
    $ownerSid = try { ([Security.Principal.NTAccount]$acl.Owner).Translate([Security.Principal.SecurityIdentifier]).Value } catch { $acl.Owner }
    $same = ($acl.AreAccessRulesProtected -eq $Protected) -and
            ($ownerSid -eq (Get-TSICDevSid $Owner)) -and
            (($have -join ';') -eq ($want -join ';'))
    if ($same) { Write-Same "ACL of $Path"; return }
    Invoke-TSICDevChange "ACL of $Path (protected=$Protected, owner=$Owner, $($Rules.Count) rules)" {
        $new = New-Object Security.AccessControl.DirectorySecurity
        $new.SetAccessRuleProtection($Protected, $false)
        $new.SetOwner([Security.Principal.NTAccount]$Owner)
        foreach ($r in $wantRules) { $new.AddAccessRule($r) }
        Set-Acl -LiteralPath $Path -AclObject $new
    }
}

function Confirm-TSICDevFolder {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path -PathType Container) { Write-Same "folder $Path"; return }
    Invoke-TSICDevChange "create folder $Path" { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

# Standard ending for a build script.
function Complete-TSICDevStep {
    param([string]$Name)
    Write-Host ""
    if (Get-Variable -Name CheckOnly -ValueOnly -ErrorAction SilentlyContinue) {
        if ($script:TSICDevDrift -gt 0) {
            Write-Host "CHECK: $Name - $($script:TSICDevDrift) item(s) differ from the script (WOULD lines above). Nothing changed." -ForegroundColor Magenta
            exit 2
        }
        Write-Host "CHECK: $Name - box matches the script. Nothing changed." -ForegroundColor Green
        exit 0
    }
    Write-Host "DONE: $Name" -ForegroundColor Green
    exit 0
}
