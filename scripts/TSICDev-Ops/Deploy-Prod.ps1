# =============================================================================
# Deploy-Prod.ps1 -- build + publish the PRODUCTION package on TSIC-OVH-DEV
#                    (the deploy itself happens on PHOENIX: Pull-DevFiles,
#                    Stage-PulledDevFiles, then Go.ps1)
# =============================================================================
# Run in a PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Ops\Deploy-Prod.ps1                  # master, normal build
#   .\scripts\TSICDev-Ops\Deploy-Prod.ps1 -SkipApi         # Angular only
#   .\scripts\TSICDev-Ops\Deploy-Prod.ps1 -SkipAngular     # API only
#
#   # Comparison build: an older commit, checked out elsewhere, with a given stamp
#   .\scripts\TSICDev-Ops\Deploy-Prod.ps1 -SourceRoot <worktree> -BuildStamp <stamp>
#
# WHAT IT DOES: the build half of scripts\1-Build-And-Deploy-Prod.ps1 (the
# SEDONA script), Steps 1 and 2, copied line for line -- same dotnet restore /
# build / publish, same Angular production build, same buildVersion stamping,
# version.json and web.config templates. Then it does what SEDONA's Step 3 does
# to a staging folder, but into a LOCAL folder: deploy-manifest.json, robocopy
# with the shared exclusion list, payload check.
#
# The output is what SEDONA leaves in E:\Websites\claude-api-STAGING and
# claude-app-STAGING, minus Go.ps1 (written on PHOENIX at deploy time):
#
#   <OutDir>\claude-api\   <OutDir>\claude-app\
#
# Then, for a normal (non-comparison) build:
#
#   Step 4  package   <OutDir>\IIS-Config-Prod\  the four PHOENIX deployment
#                     scripts FROM THIS COMMIT (what SEDONA's Step 4 copies:
#                     _config.ps1, _deploy-common.ps1, Deployment\Recycle-After-
#                     Deploy.ps1, Deployment\Rollback-Deploy.ps1), plus
#                     build-info.json, zipped to
#                     C:\TSIC\Deploy\packages\tsic-prod-<stamp>.zip + .sha256
#   Step 5  publish   copy zip + .sha256 into C:\TSIC\Phoenix\deploy (PHOENIX's
#                     read-only sftp folder), re-check the hash there, and ONLY
#                     THEN point latest.txt at it (written to a temp name and
#                     renamed, so PHOENIX never reads a half-written pointer).
#                     Keeps the 5 newest packages. -NoPublish stops after Step 4.
#
# WHAT IT DOES NOT DO: touch PHOENIX. Publishing only places a file PHOENIX may
# pull; nothing on PHOENIX changes until someone runs the pull there. No SEDONA,
# no live site.
#
# Everything that shapes the output is read from -SourceRoot, not from the
# folder this script lives in: web.config templates, _deploy-common.ps1 (manifest
# and version.json format), the prod _config.ps1. An old commit therefore builds
# with its own files, exactly as SEDONA built it on the day.
#
# Source rules:
#   default -SourceRoot (this repo): must be on master, not behind origin, with no
#     uncommitted change in the app source -- the same rule as Deploy-Dev.ps1.
#   other -SourceRoot (a comparison worktree): no uncommitted change in the app
#     source; any commit. The run is labelled COMPARISON BUILD.
#
# ASCII only (see TSICDev-Config.ps1).
# =============================================================================

param(
    [string]$SourceRoot,
    [string]$BuildStamp,
    [string]$OutDir,
    [switch]$SkipApi,
    [switch]$SkipAngular,
    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TSICDev-Config.ps1')
Assert-TSICDevBox

$isComparison = [bool]$SourceRoot
if (-not $SourceRoot) { $SourceRoot = $TSICDev.RepoRoot }
$SourceRoot = (Resolve-Path $SourceRoot).Path

# Build inputs, all from the source being built.
$SolutionDir     = Join-Path $SourceRoot 'TSIC-Core-Angular'
$ProjectPath     = Join-Path $SolutionDir 'src\backend\TSIC.API\TSIC.API.csproj'
$AngularPath     = Join-Path $SolutionDir 'src\frontend\tsic-app'
$PublishRoot     = Join-Path $SourceRoot 'publish'
$ApiPublish      = Join-Path $PublishRoot 'api'
$AngPublish      = Join-Path $PublishRoot 'angular'
$WebConfigApiSrc = Join-Path $SourceRoot 'scripts\web.config.api'
$WebConfigAngSrc = Join-Path $SourceRoot 'scripts\IIS-Config-Prod\web.config.angular'

# The prod _config.ps1 also builds PHOENIX's E:\ paths, and Join-Path refuses a
# drive this box does not have (SEDONA has an E:). The build needs none of those
# paths -- only the two site names and the environment -- so load it with errors
# non-fatal, then insist on exactly the three values used.
$ErrorActionPreference = 'Continue'
. (Join-Path $SourceRoot 'scripts\IIS-Config-Prod\_config.ps1') -Environment Prod 2>$null
$ErrorActionPreference = 'Stop'
. (Join-Path $SourceRoot 'scripts\_deploy-common.ps1')

$ApiSite     = $Config.ApiSiteName        # claude-api
$AngularSite = $Config.AngularSiteName    # claude-app
$AspNetEnv   = $Config.AspNetEnv          # Production
if ($ApiSite -ne 'claude-api' -or $AngularSite -ne 'claude-app' -or $AspNetEnv -ne 'Production') {
    throw "prod _config.ps1 gave unexpected values: api='$ApiSite' app='$AngularSite' env='$AspNetEnv'"
}

$StartedAt = Get-Date
$buildOk   = $false

# git writes progress and notes to stderr on success; with EAP=Stop that is fatal.
function Invoke-SourceGit {
    $ErrorActionPreference = 'Continue'
    $out = & git -C $SourceRoot @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $(@($out) -join ' ')" }
    @($out | ForEach-Object { "$_" })
}

# --- Step 0: source --------------------------------------------------------------
# Content changes only: the help-index generator rewrites public/help/*.json with
# LF endings on every build, which git reports as modified with no real change.
$dirty = @(Invoke-SourceGit diff --ignore-cr-at-eol --name-only HEAD -- 'TSIC-Core-Angular/src') +
         @(Invoke-SourceGit ls-files --others --exclude-standard -- 'TSIC-Core-Angular/src' | ForEach-Object { "$_ (new, untracked)" })
$dirty = @($dirty | Where-Object { $_ -and $_ -notmatch '^warning: ' })
if ($dirty.Count -gt 0) {
    throw "uncommitted changes in the app source would ship in this build:`n    $($dirty -join "`n    ")"
}
if (-not $isComparison) {
    $branch = @(Invoke-SourceGit rev-parse --abbrev-ref HEAD)[0]
    if ($branch -ne $TSICDev.DeployBranch) { throw "checkout is on '$branch'; prod builds only from '$($TSICDev.DeployBranch)'." }
    Invoke-SourceGit fetch --quiet origin $TSICDev.DeployBranch | Out-Null
    $behind = [int]@(Invoke-SourceGit rev-list --count "HEAD..origin/$($TSICDev.DeployBranch)")[0]
    if ($behind -gt 0) { throw "local $($TSICDev.DeployBranch) is $behind commit(s) behind origin: git pull" }
}
$GitHash = @(Invoke-SourceGit rev-parse --short HEAD)[0]

# ONE stamp for this build (same rule as SEDONA): it goes into the Angular footer
# AND deploy-manifest.json, and they must agree.
if (-not $BuildStamp) { $BuildStamp = "v$(Get-Date -Format 'yyMMdd.HHmm').$GitHash" }
if (-not $OutDir) { $OutDir = Join-Path 'C:\TSIC\Deploy\builds' $BuildStamp }

Write-Host "========================================" -ForegroundColor Cyan
if ($isComparison) { Write-Host "TSIC PRODUCTION BUILD - COMPARISON BUILD (not for deploy)" -ForegroundColor Magenta }
else               { Write-Host "TSIC PRODUCTION BUILD" -ForegroundColor Cyan }
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Source:  $SourceRoot @ $GitHash" -ForegroundColor Yellow
Write-Host "  Stamp:   $BuildStamp" -ForegroundColor Yellow
Write-Host "  Output:  $OutDir" -ForegroundColor Yellow
if ($SkipApi)     { Write-Host "  Skipping API" -ForegroundColor DarkGray }
if ($SkipAngular) { Write-Host "  Skipping Angular" -ForegroundColor DarkGray }
Write-Host ""

if (Test-Path -LiteralPath $OutDir) { throw "$OutDir already exists - a build never overwrites another build's output." }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$transcriptStarted = $false
try {
    Start-Transcript -Path (Join-Path $OutDir 'build.log') | Out-Null
    $transcriptStarted = $true
} catch {
    Write-Host "Transcript could not be started; continuing without file logging." -ForegroundColor Yellow
}

try {

# --- Step 1: Build .NET API (SEDONA Step 1, unchanged) ------------------------------
if (!$SkipApi) {
    Write-Host "Step 1: Building .NET API..." -ForegroundColor Yellow

    if (!(Test-Path $ApiPublish)) { New-Item -ItemType Directory -Path $ApiPublish -Force | Out-Null }
    Get-ChildItem -Path $ApiPublish -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

    Push-Location $SolutionDir
    try {
        dotnet restore
        if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed!" }

        dotnet build --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Build failed!" }

        dotnet publish $ProjectPath --configuration Release --output $ApiPublish -r win-x64 --self-contained false
        if ($LASTEXITCODE -ne 0) { throw "Publish failed!" }
    } finally {
        Pop-Location
    }

    if (Test-Path $WebConfigApiSrc) {
        $wcDest = Join-Path $ApiPublish 'web.config'
        Copy-Item $WebConfigApiSrc $wcDest -Force
        $raw = Get-Content $wcDest -Raw
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success -and $m.Value.Length -ne $raw.Length) {
            Set-Content -Path $wcDest -Value $m.Value -Encoding UTF8
        }
        [void]([xml](Get-Content $wcDest -Raw))
    }

    Write-Host "  API build complete." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Step 1: Skipped (API)" -ForegroundColor DarkGray
    Write-Host ""
}

# --- Step 2: Build Angular (SEDONA Step 2, unchanged) -------------------------------
if (!$SkipAngular) {
    Write-Host "Step 2: Building Angular (production)..." -ForegroundColor Yellow

    Push-Location $AngularPath
    try {
        if (!(Test-Path "node_modules")) {
            Write-Host "  Installing npm dependencies..." -ForegroundColor Cyan
            npm install
            if ($LASTEXITCODE -ne 0) { throw "npm install failed!" }
        }

        Write-Host "  Build version: $BuildStamp" -ForegroundColor White

        $envDir = Join-Path $AngularPath "src\environments"
        Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
            $content = Get-Content $_.FullName -Raw
            $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: '$BuildStamp'"
            Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
        }

        $env:NO_COLOR = '1'
        npm run build -- --configuration production
        if ($LASTEXITCODE -ne 0) { throw "Angular build failed!" }
    } finally {
        $envDir = Join-Path $AngularPath "src\environments"
        Get-ChildItem $envDir -Filter "environment*.ts" | ForEach-Object {
            $content = Get-Content $_.FullName -Raw
            $content = $content -replace "buildVersion:\s*'[^']*'", "buildVersion: 'dev'"
            Set-Content -Path $_.FullName -Value $content -NoNewline -Encoding UTF8
        }
        Write-Host "  Environment files reset (buildVersion only)." -ForegroundColor DarkGray
        Pop-Location
    }

    if (!(Test-Path $AngPublish)) { New-Item -ItemType Directory -Path $AngPublish -Force | Out-Null }
    Get-ChildItem -Path $AngPublish -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

    $distPath = Join-Path $AngularPath "dist\tsic-app"
    if (Test-Path (Join-Path $distPath "browser")) { $distPath = Join-Path $distPath "browser" }
    if (!(Test-Path $distPath)) { throw "Angular build output not found at: $distPath" }
    Copy-Item "$distPath\*" $AngPublish -Recurse -Force

    Write-TsicVersionFile -Path $AngPublish -BuildStamp $BuildStamp | Out-Null
    Write-Host "  Wrote version.json ($BuildStamp)" -ForegroundColor DarkGray

    if (Test-Path $WebConfigAngSrc) {
        $wcDest = Join-Path $AngPublish 'web.config'
        Copy-Item $WebConfigAngSrc $wcDest -Force
        $raw = Get-Content $wcDest -Raw
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success -and $m.Value.Length -ne $raw.Length) {
            Set-Content -Path $wcDest -Value $m.Value -Encoding UTF8
        }
        [void]([xml](Get-Content $wcDest -Raw))
    }

    Write-Host "  Angular build complete." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Step 2: Skipped (Angular)" -ForegroundColor DarkGray
    Write-Host ""
}

# --- Step 3: Stage LOCALLY (what SEDONA Step 3 does to PHOENIX's staging) ------------
# Manifest stamped into the publish folder BEFORE the copy, so it rides along and
# pins what should land; robocopy with the ONE shared exclusion list; payload check
# re-reads what actually landed.
Write-Host "Step 3: Staging the payload locally..." -ForegroundColor Yellow

foreach ($s in @(
    if (!$SkipApi)     { @{ Site = $ApiSite;     Publish = $ApiPublish } }
    if (!$SkipAngular) { @{ Site = $AngularSite; Publish = $AngPublish } }
)) {
    $dest = Join-Path $OutDir $s.Site
    $ex = Get-TsicExclusions -Site $s.Site

    $manifest = New-TsicManifest -Path $s.Publish -Site $s.Site -Environment $AspNetEnv `
                    -GitHash $GitHash -BuildStamp $BuildStamp
    Write-Host "  $($s.Site): $($manifest.fileCount) files -> $dest" -ForegroundColor White

    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    $exit = Invoke-TsicRobocopy -Source $s.Publish -Dest $dest -ExcludeDirs $ex.Dirs -ExcludeFiles $ex.Files -Quiet
    if ($exit -ge 8) { throw "staging $($s.Site) failed (robocopy exit $exit)." }

    $bad = Test-TsicPayload -Path $dest -Site $s.Site
    if ($bad) { throw "$($s.Site) payload is not deployable - $bad" }

    Write-Host "  $($s.Site) staged and verified ($BuildStamp)." -ForegroundColor Green
}

# The PHOENIX deployment scripts, from the commit being built. SEDONA's Step 4
# copies these four next to the payload on every deploy; they ride in the package
# for the same reason -- Recycle and Rollback dot-source _config / _deploy-common,
# and the recovery path must already be on the box before anything can go wrong.
$deployScripts = @(
    @{ From = 'scripts\IIS-Config-Prod\_config.ps1';                         To = 'IIS-Config-Prod\_config.ps1' }
    @{ From = 'scripts\_deploy-common.ps1';                                  To = 'IIS-Config-Prod\_deploy-common.ps1' }
    @{ From = 'scripts\IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1'; To = 'IIS-Config-Prod\Deployment\Recycle-After-Deploy.ps1' }
    @{ From = 'scripts\IIS-Config-Prod\Deployment\Rollback-Deploy.ps1';      To = 'IIS-Config-Prod\Deployment\Rollback-Deploy.ps1' }
)
# The two PHOENIX pull scripts ride along too (when the commit has them), at the
# package ROOT, so Stage-PulledDevFiles.ps1 refreshes both in E:\TSIC-Incoming.
# They are not IIS-Config-Prod files: that folder gets exactly the four SEDONA
# puts there.
$PullScripts = @('Pull-DevFiles.ps1', 'Stage-PulledDevFiles.ps1')
foreach ($ps in $PullScripts) {
    $rel = "scripts\TSICDev-Ops\$ps"
    if (Test-Path -LiteralPath (Join-Path $SourceRoot $rel)) { $deployScripts += @{ From = $rel; To = $ps } }
}
foreach ($d in $deployScripts) {
    $src = Join-Path $SourceRoot $d.From
    if (-not (Test-Path -LiteralPath $src)) { throw "deployment script missing from the source: $($d.From)" }
    $dst = Join-Path $OutDir $d.To
    New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst
}
Write-Host "  Deployment scripts: $($deployScripts.Count) copied from $GitHash" -ForegroundColor Green

# What this build contains -- the pull on PHOENIX writes Go.ps1's scope from it.
$buildInfo = [ordered]@{
    format     = 1
    buildStamp = $BuildStamp
    gitHash    = $GitHash
    api        = (-not $SkipApi)
    angular    = (-not $SkipAngular)
    comparison = $isComparison
    builtUtc   = (Get-Date).ToUniversalTime().ToString('o')
    builtOn    = $env:COMPUTERNAME
}
[IO.File]::WriteAllText((Join-Path $OutDir 'build-info.json'), ($buildInfo | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))

# The source tree must be exactly as it was: the stamping above is undone in the
# finally block, and nothing else may have changed (e.g. npm rewriting the lock file).
$after = @(Invoke-SourceGit diff --ignore-cr-at-eol --name-only HEAD -- 'TSIC-Core-Angular/src')
$after = @($after | Where-Object { $_ -and $_ -notmatch '^warning: ' })
if ($after.Count -gt 0) {
    Write-Host ""
    Write-Host "  WARNING: the build changed tracked source files:" -ForegroundColor Yellow
    $after | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
}

$PackagePath = $null
$Published   = $false

# --- Step 4: Package --------------------------------------------------------------
# One zip: the payload folders, the deployment scripts and build-info.json. Built
# entry by entry (not Compress-Archive, which skips hidden files under PS 5.1),
# from an explicit list, so build.log -- held open by the transcript -- stays out.
if ($isComparison) {
    Write-Host ""
    Write-Host "Steps 4-5: skipped - a COMPARISON build is never packaged or published." -ForegroundColor Magenta
} else {
    Write-Host ""
    Write-Host "Step 4: Packaging..." -ForegroundColor Yellow
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

    $PackageDir  = 'C:\TSIC\Deploy\packages'
    $PackageName = "tsic-prod-$BuildStamp.zip"
    $PackagePath = Join-Path $PackageDir $PackageName
    New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null
    if (Test-Path -LiteralPath $PackagePath) { throw "$PackagePath already exists - a package is never overwritten." }

    $roots = @('IIS-Config-Prod') + @(if (!$SkipApi) { $ApiSite }) + @(if (!$SkipAngular) { $AngularSite })
    $files = @(Get-Item -LiteralPath (Join-Path $OutDir 'build-info.json'))
    foreach ($ps in $PullScripts) {
        if (Test-Path -LiteralPath (Join-Path $OutDir $ps)) { $files += Get-Item -LiteralPath (Join-Path $OutDir $ps) }
    }
    foreach ($r in $roots) { $files += @(Get-ChildItem -LiteralPath (Join-Path $OutDir $r) -Recurse -File -Force) }

    $zip = [IO.Compression.ZipFile]::Open($PackagePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in $files) {
            $entry = $f.FullName.Substring($OutDir.TrimEnd('\').Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zip.Dispose() }

    # Read it back: every file must be in it.
    $check = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try { $entries = @($check.Entries | Where-Object { $_.Name }).Count } finally { $check.Dispose() }
    if ($entries -ne $files.Count) { throw "package holds $entries files, expected $($files.Count)." }

    $PackageHash = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText("$PackagePath.sha256", "$PackageHash  $PackageName`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("  {0}  ({1} files, {2:N1} MB)" -f $PackageName, $files.Count, ((Get-Item $PackagePath).Length / 1MB)) -ForegroundColor Green
    Write-Host "  SHA-256 $PackageHash" -ForegroundColor DarkGray

    # --- Step 5: Publish to PHOENIX's pull folder ------------------------------------
    if ($NoPublish) {
        Write-Host ""
        Write-Host "Step 5: skipped (-NoPublish). PHOENIX's pull folder is unchanged." -ForegroundColor DarkGray
    } else {
        Write-Host ""
        Write-Host "Step 5: Publishing to $($TSICDev.DeployDropDir)..." -ForegroundColor Yellow
        $drop = $TSICDev.DeployDropDir
        Copy-Item -LiteralPath $PackagePath -Destination $drop
        Copy-Item -LiteralPath "$PackagePath.sha256" -Destination $drop
        $landed = (Get-FileHash -LiteralPath (Join-Path $drop $PackageName) -Algorithm SHA256).Hash
        if ($landed -ne $PackageHash) { throw "the copy in $drop does not match the package (hash $landed). latest.txt NOT changed." }

        # Loose copies of the two pull scripts (the same files the package carries),
        # so a PHOENIX without them can bootstrap with one sftp get each into
        # E:\TSIC-Incoming. Temp name, then rename: never half-written.
        foreach ($ps in $PullScripts) {
            $loose = Join-Path $OutDir $ps
            if (Test-Path -LiteralPath $loose) {
                Copy-Item -LiteralPath $loose -Destination (Join-Path $drop "$ps.tmp") -Force
                Move-Item -LiteralPath (Join-Path $drop "$ps.tmp") -Destination (Join-Path $drop $ps) -Force
                Write-Host "  $ps (loose copy, for bootstrapping PHOENIX)" -ForegroundColor Green
            }
        }
        $staleLoose = Join-Path $drop 'Pull-Deploy.ps1'      # superseded by the two above
        if (Test-Path -LiteralPath $staleLoose) { Remove-Item -LiteralPath $staleLoose -Force }

        # The pointer moves LAST, and atomically: write a temp file, then rename.
        $tmp = Join-Path $drop 'latest.txt.tmp'
        [IO.File]::WriteAllText($tmp, "$PackageName`n", (New-Object System.Text.ASCIIEncoding))
        Move-Item -LiteralPath $tmp -Destination (Join-Path $drop 'latest.txt') -Force
        Write-Host "  latest.txt -> $PackageName" -ForegroundColor Green

        # Keep the 5 newest packages; never the one latest.txt names.
        $old = @(Get-ChildItem -LiteralPath $drop -Filter 'tsic-prod-*.zip' -File |
                 Where-Object { $_.Name -ne $PackageName } |
                 Sort-Object LastWriteTime -Descending | Select-Object -Skip 4)
        foreach ($o in $old) {
            Remove-Item -LiteralPath $o.FullName -Force
            Remove-Item -LiteralPath "$($o.FullName).sha256" -Force -ErrorAction SilentlyContinue
            Write-Host "  Pruned: $($o.Name)" -ForegroundColor DarkGray
        }
        $Published = $true
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if ($Published)       { Write-Host "BUILT AND PUBLISHED - PHOENIX can pull it; nothing on PHOENIX has changed." -ForegroundColor Green }
elseif ($PackagePath) { Write-Host "BUILT AND PACKAGED - not published." -ForegroundColor Green }
else                  { Write-Host "BUILT - nothing was sent anywhere." -ForegroundColor Green }
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build:    $BuildStamp" -ForegroundColor Green
Write-Host "  Output:   $OutDir" -ForegroundColor Green
if ($PackagePath) { Write-Host "  Package:  $PackagePath" -ForegroundColor Green }
Write-Host "  Finished: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  (took $([int]((Get-Date) - $StartedAt).TotalSeconds)s)" -ForegroundColor Green
Write-Host ""

$buildOk = $true
exit 0

} finally {
    if (-not $buildOk) {
        Write-Host ""
        Write-Host "  Build ENDED without completing: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Red
        Write-Host ""
    }
    if ($transcriptStarted) { try { Stop-Transcript | Out-Null } catch {} }
}
