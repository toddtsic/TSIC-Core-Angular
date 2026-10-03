# Club rep reopen stress test - one command.
#   1. Launches the stress target (refuses if an API is already up; reuses a running frontend):
#        Backend : Release build, ASPNETCORE_ENVIRONMENT=Development (from the 'https' launch profile)
#                  -> dev123 bypass on, ADN SANDBOX, local .\SS2016 TSICV5.
#        Frontend: ng serve, development config -> apiUrl https://localhost:7215/api.
#                  NEVER '-c production' here: that config points the browser at the LIVE prod API.
#   2. Runs the stampede (clubrep-reopen-stampede.mjs): enables team registration on the 2027 job,
#      releases every rep at once, prints the timings, counts and DB checks, saves to runs\.
# Extra arguments pass through to the stampede, e.g.  .\Run-StressTest.ps1 --dry

$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()

$root     = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$apiDir   = Join-Path $root 'TSIC-Core-Angular\src\backend\TSIC.API'
$webDir   = Join-Path $root 'TSIC-Core-Angular\src\frontend\tsic-app'
$apiProbe = 'https://localhost:7215/'
$webProbe = 'https://localhost:4200/'

# Up = the server answered at all (any HTTP status). curl reports 000 when nothing is listening.
function Test-Up([string]$url) {
    $code = & curl.exe -sk -o NUL -w '%{http_code}' --max-time 5 $url
    return $code -ne '000'
}

function Wait-Up([string]$url, [string]$name, [int]$timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Up $url) { Write-Host "$name up"; return }
        Start-Sleep -Seconds 3
    }
    throw "$name did not come up within $timeoutSec s ($url)"
}

# Always start the API fresh: a running one may be a Debug session or an older build that
# predates the code under test (take 2 on 2026-10-03 silently reused the 8:04 API).
if (Test-Up $apiProbe) {
    $listener = Get-NetTCPConnection -LocalPort 7215 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    $proc = if ($listener) { Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue }
    throw "An API is already running on 7215 ($($proc.ProcessName) pid $($proc.Id), started $($proc.StartTime)). Close it, then rerun - the test must start its own Release build."
}
Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$apiDir'; dotnet run -c Release --launch-profile https"
if (Test-Up $webProbe) {
    Write-Host 'Frontend already running - using it.'
} else {
    Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$webDir'; npm start"
}

Wait-Up $apiProbe 'API (Release)' 300
Wait-Up $webProbe 'Frontend' 300
$startup = $clock.Elapsed
Write-Host ("Startup: {0:mm\:ss}" -f $startup)

Push-Location $PSScriptRoot
try {
    & node clubrep-reopen-stampede.mjs @args
    if ($LASTEXITCODE -ne 0) { throw "Stampede exited with code $LASTEXITCODE" }
} finally {
    Pop-Location
    Write-Host ("Startup {0:mm\:ss}  Stampede + report {1:mm\:ss}  Total {2:mm\:ss}" -f $startup, ($clock.Elapsed - $startup), $clock.Elapsed)
}
