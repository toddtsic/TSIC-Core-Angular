# =============================================================================
# Apply-Dev-Secrets.ps1 -- make the dev-api app pool's environment variables
#                          exactly match the secrets file
# =============================================================================
# Run in an ELEVATED PowerShell on TSIC-OVH-DEV:
#
#   .\scripts\TSICDev-Build\Apply-Dev-Secrets.ps1 [-CheckOnly]
#
# -CheckOnly: compare the pool with the file (names only, ok / WOULD), change nothing.
#
# The pool ends up with exactly: every entry in the secrets file, plus
# ASPNETCORE_ENVIRONMENT from TSICDev-Config.ps1. A variable on the pool that is
# in neither is REMOVED -- the file is the single source of truth.
#
# Secrets file ($TSICDev.SecretsFile, gitignored): one assignment,
#   $envVars = @{
#       "AWS_ACCESS_KEY_ID" = "..."
#       ...
#   }
# It is PARSED, never executed: only literal string values are accepted.
#
# Writes through Microsoft.Web.Administration (the API behind IIS Manager), not
# appcmd, so a value containing a quote or apostrophe goes in intact.
#
# No secret value is ever printed. Output is names, and ok / DIFF per name after
# reading the pool back.
#
# Exit code 0 = pool matches the file exactly and was recycled.
# =============================================================================

#Requires -RunAsAdministrator

param([switch]$CheckOnly)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\TSICDev-Ops\TSICDev-Config.ps1')

$EnvKey = 'ASPNETCORE_ENVIRONMENT'

# Returns an ordered name -> value map from the secrets file, without running it.
function Read-SecretsFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Secrets file not found: $Path" }

    $errors = $null
    $text = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
    $ast = [Management.Automation.Language.Parser]::ParseInput($text, [ref]$null, [ref]$errors)
    if (@($errors).Count -gt 0) { throw "Secrets file does not parse: $($errors[0].Message) (line $($errors[0].Extent.StartLineNumber))" }

    $statements = @($ast.EndBlock.Statements)
    if ($statements.Count -ne 1 -or
        $statements[0] -isnot [Management.Automation.Language.AssignmentStatementAst] -or
        $statements[0].Left.Extent.Text -ne '$envVars') {
        throw 'Secrets file must contain exactly one statement: $envVars = @{ ... }'
    }
    $ht = $statements[0].Right.Expression
    if ($ht -isnot [Management.Automation.Language.HashtableAst]) {
        throw 'Secrets file: $envVars must be assigned a @{ ... } hashtable literal.'
    }

    $map = [ordered]@{}
    foreach ($pair in $ht.KeyValuePairs) {
        $name = $pair.Item1.SafeGetValue()
        if ($name -isnot [string] -or $name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            throw "Secrets file: invalid variable name [$name]."
        }
        if ($map.Contains($name)) { throw "Secrets file: $name appears twice." }
        if ($name -eq $EnvKey) { throw "Secrets file must not set $EnvKey -- it comes from TSICDev-Config.ps1 (AspNetEnv)." }

        $expr = $null
        if ($pair.Item2 -is [Management.Automation.Language.PipelineAst] -and @($pair.Item2.PipelineElements).Count -eq 1) {
            $expr = $pair.Item2.PipelineElements[0].Expression
        }
        if ($expr -isnot [Management.Automation.Language.StringConstantExpressionAst]) {
            throw "Secrets file: $name must be a plain quoted string (no variables or expressions; use single quotes if the value contains a $)."
        }
        $value = $expr.Value
        if ([string]::IsNullOrWhiteSpace($value)) { throw "Secrets file: $name is empty." }
        if ($value -ne $value.Trim()) { throw "Secrets file: $name has leading or trailing whitespace." }
        if ($value -match 'FILL_ME|YOUR_|<paste') { throw "Secrets file: $name still holds a placeholder." }
        $map[$name] = $value
    }
    if ($map.Count -eq 0) { throw 'Secrets file: $envVars is empty.' }
    $map
}

# Name -> value of the pool's current environment variables.
function Get-PoolEnv {
    param([string]$PoolName)
    $sm = New-Object Microsoft.Web.Administration.ServerManager
    try {
        $pool = $sm.ApplicationPools[$PoolName]
        if (-not $pool) { throw "App pool '$PoolName' does not exist." }
        $map = @{}
        foreach ($el in $pool.GetCollection('environmentVariables')) { $map[[string]$el['name']] = [string]$el['value'] }
        $map
    } finally { $sm.Dispose() }
}

try {
    Assert-TSICDevBox
    Add-Type -Path (Join-Path $env:windir 'System32\inetsrv\Microsoft.Web.Administration.dll')

    $poolName = $TSICDev.ApiPool
    Write-Host ""
    Write-Host "Apply secrets to app pool '$poolName'" -ForegroundColor Cyan
    Write-Host "  from $($TSICDev.SecretsFile)"

    $desired = Read-SecretsFile $TSICDev.SecretsFile
    $desired[$EnvKey] = $TSICDev.AspNetEnv
    Write-Host "  $($desired.Count) variables wanted ($($desired.Count - 1) from the file + $EnvKey=$($TSICDev.AspNetEnv))."

    if ($CheckOnly) {
        $have = Get-PoolEnv $poolName
        foreach ($name in $desired.Keys) {
            if (-not $have.ContainsKey($name)) { Write-Host "  WOULD    add $name" -ForegroundColor Magenta; $script:TSICDevDrift++ }
            elseif ($have[$name] -cne $desired[$name]) { Write-Host "  WOULD    update $name" -ForegroundColor Magenta; $script:TSICDevDrift++ }
            else { Write-Same $name }
        }
        foreach ($name in $have.Keys) {
            if (-not $desired.Contains($name)) { Write-Host "  WOULD    remove $name" -ForegroundColor Magenta; $script:TSICDevDrift++ }
        }
        Complete-TSICDevStep "Secrets on '$poolName'"
    }

    # --- Write --------------------------------------------------------------
    $sm = New-Object Microsoft.Web.Administration.ServerManager
    try {
        $coll = $sm.ApplicationPools[$poolName].GetCollection('environmentVariables')
        foreach ($el in @($coll)) {
            if (-not $desired.Contains([string]$el['name'])) {
                Write-Host "  remove  $($el['name'])  (not in the file)" -ForegroundColor Yellow
                $coll.Remove($el)
            }
        }
        foreach ($name in $desired.Keys) {
            $existing = @($coll | Where-Object { [string]$_['name'] -eq $name })
            if ($existing.Count -gt 0) {
                if ([string]$existing[0]['value'] -cne $desired[$name]) {
                    $existing[0]['value'] = $desired[$name]
                    Write-Host "  update  $name"
                } else {
                    Write-Host "  same    $name" -ForegroundColor DarkGray
                }
            } else {
                $add = $coll.CreateElement('add')
                $add['name'] = $name
                $add['value'] = $desired[$name]
                [void]$coll.Add($add)
                Write-Host "  add     $name" -ForegroundColor Green
            }
        }
        $sm.CommitChanges()
    } finally { $sm.Dispose() }

    # --- Read back and compare (fresh ServerManager = what IIS actually has) --
    $after = Get-PoolEnv $poolName
    $bad = 0
    foreach ($name in $desired.Keys) {
        if (-not $after.ContainsKey($name)) { Write-Host "  MISSING $name" -ForegroundColor Red; $bad++ }
        elseif ($after[$name] -cne $desired[$name]) { Write-Host "  DIFF    $name" -ForegroundColor Red; $bad++ }
    }
    foreach ($name in $after.Keys) {
        if (-not $desired.Contains($name)) { Write-Host "  EXTRA   $name" -ForegroundColor Red; $bad++ }
    }
    if ($bad -gt 0) { throw "Pool does not match the file after writing ($bad problem(s))." }
    Write-Host "  Read back: all $($desired.Count) variables match, nothing extra." -ForegroundColor Green

    # --- Recycle so the running process picks them up -------------------------
    $sm = New-Object Microsoft.Web.Administration.ServerManager
    try {
        $p = $sm.ApplicationPools[$poolName]
        if ($p.State -eq 'Started') { [void]$p.Recycle(); Write-Host "  Recycled '$poolName'." }
        else { Write-Host "  '$poolName' is $($p.State); the variables apply when it starts." -ForegroundColor Yellow }
    } finally { $sm.Dispose() }

    Write-Host ""
    Write-Host "DONE. '$poolName' matches the secrets file ($($desired.Count) variables)." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
