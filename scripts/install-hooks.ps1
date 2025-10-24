Param()

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptDir
Try {
    $repoRoot = (Get-Item ..).FullName
    Set-Location $repoRoot
    if (-Not (Test-Path '.githooks')) {
        Write-Host "No .githooks directory found in repo root: $repoRoot" -ForegroundColor Yellow
        Exit 1
    }
    Write-Host "Setting git core.hooksPath to .githooks"
    git config core.hooksPath .githooks
    Write-Host "Done. Hooks will be read from .githooks. To undo run: git config --unset core.hooksPath"
} Finally {
    Pop-Location
}
