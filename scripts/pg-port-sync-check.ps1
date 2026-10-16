<#
.SYNOPSIS
  Brings master's changes into feature/postgres-port safely. See docs/postgres-port/SYNC-FROM-MASTER.md.

.DESCRIPTION
  Default (preview, changes nothing): fetch origin, show what master added, dry-run the merge
  for conflicts, and scan master's incoming C# for SQL Server-only constructs and schema changes.

  -Merge   Also merge origin/master into the branch (local commit, never pushed), then build
           and run the test suite.
  -Verify  Also refresh Postgres from SQL Server (pg-migrate), run the A/B equivalence check,
           and run the A/B HTTP sweep against the baseline. Takes ~10 minutes.

.EXAMPLE
  .\scripts\pg-port-sync-check.ps1                 # preview
  .\scripts\pg-port-sync-check.ps1 -Merge -Verify  # full sync
#>
param(
    [switch]$Merge,
    [switch]$Verify,
    # With -Verify: record this sweep as the new baseline (after reviewing an expected change).
    [switch]$WriteBaseline,
    # With -Verify: skip the pg-migrate refresh (Postgres data already current, no schema change).
    [switch]$SkipMigrate
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sln = Join-Path $repo 'TSIC-Core-Angular\TSIC-Core-Angular.sln'
$api = Join-Path $repo 'TSIC-Core-Angular\src\backend\TSIC.API'
$problems = New-Object System.Collections.Generic.List[string]

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host "   $text" -ForegroundColor Red; $problems.Add($text) }

Push-Location $repo
try {
    $branch = (git branch --show-current)
    if ($branch -ne 'feature/postgres-port') { throw "On '$branch'. Switch to feature/postgres-port first." }

    Step 'Fetch origin'
    git fetch origin --quiet
    if ($LASTEXITCODE -ne 0) { throw 'git fetch failed' }
    $base = (git merge-base HEAD origin/master)
    $ahead = [int](git rev-list --count "HEAD..origin/master")
    Write-Host "   origin/master is $ahead commit(s) ahead of the branch."
    if ($ahead -gt 0) { git log --oneline "HEAD..origin/master" | ForEach-Object { Write-Host "     $_" } }

    if ($ahead -gt 0) {
        Step 'Dry-run merge (nothing is touched)'
        $null = git merge-tree --write-tree HEAD origin/master
        if ($LASTEXITCODE -ne 0) {
            Fail 'Merge has conflicts:'
            git merge-tree --write-tree --name-only HEAD origin/master | Select-Object -Skip 1 | ForEach-Object { Write-Host "     $_" }
        } else { Write-Host '   Clean - no conflicts.' }

        $both = Compare-Object (git diff --name-only $base origin/master) (git diff --name-only $base HEAD) -IncludeEqual -ExcludeDifferent -PassThru
        if ($both) { Write-Host '   Changed on both sides (check these after merging):'; $both | ForEach-Object { Write-Host "     $_" } }

        Step 'Scan master''s incoming C# for SQL Server-only constructs'
        # Each of these needed a Postgres change on this branch; new occurrences need the same.
        $patterns = @(
            'EF\.Functions\.(DateDiff|DateFromParts|DateTimeFromParts|Collate|Contains|FreeText|IsDate|DataLength)',
            'SqlConnection|SqlCommand|SqlBulkCopy|SqlParameter|SqlDbType|SqlException|Microsoft\.Data\.SqlClient',
            'ExecuteSqlRaw|ExecuteSqlInterpolated|ExecuteSql\(|SqlQueryRaw|SqlQuery<|FromSqlRaw|FromSqlInterpolated|FromSql\(',
            'CommandType\.StoredProcedure|sp_getapplock|NEXT VALUE FOR|GETDATE\(|GETUTCDATE|ISNULL\(|NOLOCK|DATEADD|DATEDIFF',
            'SQL_Latin1_General|Like\([^)]*"[^"]*\[',
            'HasColumnType\("(money|datetime|nvarchar|varchar|bit|uniqueidentifier)'
        )
        $added = git diff $base origin/master -- '*.cs' | Where-Object { $_ -match '^\+' -and $_ -notmatch '^\+\+\+' }
        $hits = $added | Select-String -Pattern ($patterns -join '|')
        if ($hits) {
            Fail "$(@($hits).Count) added line(s) need a Postgres look:"
            git diff $base origin/master -G ($patterns -join '|') --name-only -- '*.cs' | ForEach-Object { Write-Host "     $_" }
            $hits | Select-Object -First 25 | ForEach-Object { Write-Host "       $($_.Line.Trim())" }
        } else { Write-Host '   None.' }

        Step 'Schema changes from master'
        $schema = git diff --name-only $base origin/master | Where-Object { $_ -match '\.sql$|/Entities/|SqlDbContext\.cs$|LogsDbContext\.cs$' }
        if ($schema) {
            Write-Host '   Review: new tables go in tools/pg-migrate/tables-tsicv5.txt, then re-run pg-migrate.' -ForegroundColor Yellow
            $schema | ForEach-Object { Write-Host "     $_" }
            $newEntities = git diff --name-only --diff-filter=A $base origin/master | Where-Object { $_ -match '/Entities/.*\.cs$' }
            if ($newEntities) { Fail "New entities (likely new tables): $($newEntities -join ', ')" }
        } else { Write-Host '   None.' }
    }

    if ($Merge -and $ahead -gt 0) {
        if ($problems | Where-Object { $_ -like 'Merge has conflicts*' }) { throw 'Resolve the conflicts by hand: git merge origin/master' }
        Step 'Merge origin/master into feature/postgres-port (local commit)'
        git merge --no-ff --no-edit -m "merge: sync feature/postgres-port with origin/master ($ahead commits)" origin/master
        if ($LASTEXITCODE -ne 0) { throw 'git merge failed' }
    }

    if ($Merge -or $Verify) {
        Step 'Build and test'
        dotnet build $sln --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
        dotnet test (Join-Path $repo 'TSIC-Core-Angular\src\backend\TSIC.Tests\TSIC.Tests.csproj') --nologo -v q --no-build
        if ($LASTEXITCODE -ne 0) { Fail 'Tests failed' }
    }

    if ($Verify) {
        if (-not $SkipMigrate) {
            Step 'Refresh Postgres from SQL Server (pg-migrate)'
            dotnet run --project (Join-Path $repo 'tools\pg-migrate') -c Release
            if ($LASTEXITCODE -ne 0) { Fail 'pg-migrate reported problems (tsicv5)' }
            dotnet run --project (Join-Path $repo 'tools\pg-migrate') -c Release -- --source 'Server=.\SS2016;Database=TSICLogs;Trusted_Connection=True;TrustServerCertificate=True' --target 'Host=localhost;Port=5432;Database=tsiclogs;Username=postgres' --tables all
            if ($LASTEXITCODE -ne 0) { Fail 'pg-migrate reported problems (tsiclogs)' }
        }

        Step 'A/B equivalence (rewritten repository methods, both databases)'
        dotnet run --project (Join-Path $repo 'tools\pg-ab\equivalence') -c Release
        if ($LASTEXITCODE -ne 0) { Fail 'Equivalence check found differences or errors' }

        Step 'A/B HTTP sweep (API on Postgres :7301 vs SQL Server :7302)'
        $dll = Join-Path $api 'bin\Debug\net10.0\TSIC.API.dll'
        $logDir = Join-Path $env:TEMP 'pg-ab-sweep'
        New-Item -ItemType Directory -Force $logDir | Out-Null
        $procs = @()
        # API output goes to files: a redirected-but-unread pipe fills up and stalls the API.
        # Start-Process takes no per-process environment in PS 5.1, so set it, start, restore.
        $saved = @{ ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT; DatabaseProvider = $env:DatabaseProvider; ASPNETCORE_URLS = $env:ASPNETCORE_URLS }
        try {
            foreach ($p in @(@{ Provider = 'Postgres'; Port = 7301 }, @{ Provider = 'SqlServer'; Port = 7302 })) {
                $env:ASPNETCORE_ENVIRONMENT = 'Development'
                $env:DatabaseProvider = $p.Provider
                $env:ASPNETCORE_URLS = "https://localhost:$($p.Port)"
                $procs += Start-Process dotnet -ArgumentList "`"$dll`"" -WorkingDirectory $api -WindowStyle Hidden -PassThru `
                    -RedirectStandardOutput (Join-Path $logDir "api-$($p.Provider).log") -RedirectStandardError (Join-Path $logDir "api-$($p.Provider).err.log")
            }
        } finally {
            foreach ($k in $saved.Keys) { Set-Item "env:$k" $saved[$k] -ErrorAction SilentlyContinue; if ($null -eq $saved[$k]) { Remove-Item "env:$k" -ErrorAction SilentlyContinue } }
        }
        Write-Host "   API logs: $logDir"
        try {
            foreach ($port in 7301, 7302) {
                $up = $false
                for ($i = 0; $i -lt 60 -and -not $up; $i++) {
                    Start-Sleep -Seconds 2
                    $code = & curl.exe -sk -o NUL -w '%{http_code}' "https://localhost:$port/swagger/v1/swagger.json"
                    $up = ($code -eq '200')
                }
                if (-not $up) { throw "API on :$port did not start" }
            }
            $sweepArgs = @((Join-Path $repo 'tools\pg-ab\sweep\sweep.mjs'), '--baseline', (Join-Path $repo 'tools\pg-ab\sweep\baseline.json'))
            if ($WriteBaseline) { $sweepArgs += '--write-baseline' }
            node @sweepArgs
            if ($LASTEXITCODE -ne 0 -and -not $WriteBaseline) { Fail 'HTTP sweep changed from baseline' }
        } finally {
            $procs | ForEach-Object { if (-not $_.HasExited) { Stop-Process -Id $_.Id -Force } }
        }
    }

    Step 'Result'
    if ($problems.Count -eq 0) { Write-Host '   All checks passed.' -ForegroundColor Green }
    else { Write-Host "   $($problems.Count) item(s) need attention:" -ForegroundColor Yellow; $problems | ForEach-Object { Write-Host "     - $_" } }
    if (-not $Merge -and $ahead -gt 0) { Write-Host "`n   Preview only. To merge: .\scripts\pg-port-sync-check.ps1 -Merge -Verify" }
}
finally {
    Pop-Location
}
if ($problems.Count -gt 0) { exit 1 }
