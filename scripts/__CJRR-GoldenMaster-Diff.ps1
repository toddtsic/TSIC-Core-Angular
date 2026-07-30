# =====================================================================
# CJRR Golden-Master Diff — legacy sproc endpoint vs EF-ported endpoints
# =====================================================================
# Compares the OLD monolithic endpoint (GET /api/customer-job-revenue,
# backed by [reporting].[CustomerJobRevenueRollups] / _NotTSICADN) against
# the NEW EF-ported endpoints (GET .../rollup + .../details/{method})
# under the same JWT, same API, same DB.
#
# Gate: the frontend cutover ships only on zero-diff, or on penny-level
# diffs explicitly accepted (sprocs sum in float; EF sums in decimal).
#
# Usage (token from browser devtools → Network → Authorization header):
#   .\__CJRR-GoldenMaster-Diff.ps1 -Token "eyJ..." -StartDate 2026-06-01 -EndDate 2026-06-30
#   .\__CJRR-GoldenMaster-Diff.ps1 -Token "eyJ..." -JobNames "Thunder Cup 2026","Thunder Cup 2025"
#   (period mode diffs a date window; jobs mode diffs complete history —
#    the old endpoint is fed 2000-01-01..tomorrow to approximate "no bound")
# =====================================================================
param(
    [Parameter(Mandatory = $true)] [string]$Token,
    [string]$BaseUrl = "https://localhost:7215",
    [string]$StartDate,
    [string]$EndDate,
    [string[]]$JobNames = @(),
    [decimal]$PennyTolerance = 0.01
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$headers = @{ Authorization = "Bearer $Token" }

$jobsMode = ($JobNames.Count -gt 0)
if (-not $jobsMode -and (-not $StartDate -or -not $EndDate)) {
    Write-Error "Provide -JobNames, or both -StartDate and -EndDate."
    exit 1
}

function Get-Json($url) {
    Invoke-RestMethod -Uri $url -Headers $headers -Method Get
}

function UrlJobNames($names) {
    ($names | ForEach-Object { "jobNames=" + [uri]::EscapeDataString($_) }) -join "&"
}

# ---- Old endpoint (sproc) ----
if ($jobsMode) {
    $oldStart = "2000-01-01"; $oldEnd = (Get-Date).AddDays(1).ToString("yyyy-MM-dd")
    $oldUrl = "$BaseUrl/api/customer-job-revenue?startDate=$oldStart&endDate=$oldEnd&" + (UrlJobNames $JobNames)
    $newQs = UrlJobNames $JobNames
} else {
    $oldUrl = "$BaseUrl/api/customer-job-revenue?startDate=$StartDate&endDate=$EndDate"
    $newQs = "startDate=$StartDate&endDate=$EndDate"
}

Write-Host "OLD  $oldUrl"
$old = Get-Json $oldUrl
Write-Host "NEW  $BaseUrl/api/customer-job-revenue/rollup?$newQs"
$new = Get-Json "$BaseUrl/api/customer-job-revenue/rollup?$newQs"

$mismatches = New-Object System.Collections.ArrayList
$pennies = New-Object System.Collections.ArrayList

# ---- 1) Revenue rollup cells: key = job|year|month|payMethod → payAmount ----
function ToMap($records) {
    $map = @{}
    foreach ($r in $records) {
        $key = "{0}|{1}|{2}|{3}" -f $r.jobName, $r.year, $r.month, $r.payMethod
        if ($map.ContainsKey($key)) { $map[$key] = $map[$key] + [decimal]$r.payAmount }
        else { $map[$key] = [decimal]$r.payAmount }
    }
    $map
}

$oldMap = ToMap $old.revenueRecords
$newMap = ToMap $new.revenueRecords

foreach ($key in ($oldMap.Keys + $newMap.Keys | Sort-Object -Unique)) {
    $inOld = $oldMap.ContainsKey($key); $inNew = $newMap.ContainsKey($key)
    if (-not $inNew) { [void]$mismatches.Add("ROLLUP missing in NEW: $key = $($oldMap[$key])"); continue }
    if (-not $inOld) { [void]$mismatches.Add("ROLLUP extra in NEW:   $key = $($newMap[$key])"); continue }
    $delta = [math]::Abs($oldMap[$key] - $newMap[$key])
    if ($delta -eq 0) { continue }
    if ($delta -le $PennyTolerance) {
        [void]$pennies.Add(("ROLLUP penny-delta {0}: old={1} new={2}" -f $key, $oldMap[$key], $newMap[$key]))
    } else {
        [void]$mismatches.Add(("ROLLUP MATERIAL {0}: old={1} new={2}" -f $key, $oldMap[$key], $newMap[$key]))
    }
}

# ---- 2) Monthly counts: key = aid ----
$oldCounts = @{}; foreach ($c in $old.monthlyCounts) { $oldCounts[[string]$c.aid] = $c }
foreach ($c in $new.monthlyCounts) {
    $o = $oldCounts[[string]$c.aid]
    if ($null -eq $o) { [void]$mismatches.Add("COUNTS extra in NEW: aid=$($c.aid)"); continue }
    foreach ($f in "countActivePlayersToDate","countActivePlayersToDateLastMonth","countNewPlayersThisMonth","countActiveTeamsToDate","countActiveTeamsToDateLastMonth","countNewTeamsThisMonth") {
        if ([int]$o.$f -ne [int]$c.$f) { [void]$mismatches.Add("COUNTS aid=$($c.aid) $f old=$($o.$f) new=$($c.$f)") }
    }
    $oldCounts.Remove([string]$c.aid)
}
foreach ($k in $oldCounts.Keys) { [void]$mismatches.Add("COUNTS missing in NEW: aid=$k") }

# ---- 3) Admin fees + 4) detail sets: multiset compare on composite keys ----
function MultisetDiff($label, $oldRows, $newRows, $keyScript) {
    $bag = @{}
    foreach ($r in $oldRows) { $k = & $keyScript $r; if ($bag.ContainsKey($k)) { $bag[$k]++ } else { $bag[$k] = 1 } }
    foreach ($r in $newRows) {
        $k = & $keyScript $r
        if ($bag.ContainsKey($k) -and $bag[$k] -gt 0) { $bag[$k]-- } else { [void]$script:mismatches.Add("$label extra in NEW: $k") }
    }
    foreach ($e in $bag.GetEnumerator()) { if ($e.Value -gt 0) { [void]$script:mismatches.Add("$label missing in NEW (x$($e.Value)): $($e.Key)") } }
}

MultisetDiff "ADMINFEES" $old.adminFees $new.adminFees { param($r) "{0}|{1}|{2}|{3}|{4}|{5}" -f $r.jobName, $r.year, $r.month, $r.chargeType, ([decimal]$r.chargeAmount).ToString("0.00"), $r.comment }

# Detail keys normalize serialization noise, not data:
#   amount → 2dp (sproc table vars are money = 4dp; EF returns native column scale)
#   date   → whole seconds (sproc table vars are datetime = 3.33ms truncation; EF returns full precision)
$detailPairs = @(
    @{ Method = "cc";     OldRows = $old.creditCardRecords },
    @{ Method = "check";  OldRows = $old.checkRecords },
    @{ Method = "echeck"; OldRows = $old.echeckRecords }
)
foreach ($p in $detailPairs) {
    $newRows = Get-Json "$BaseUrl/api/customer-job-revenue/details/$($p.Method)?$newQs"
    MultisetDiff ("DETAIL/" + $p.Method) $p.OldRows $newRows { param($r) "{0}|{1}|{2}|{3}|{4}|{5}|{6}" -f $r.jobName, $r.year, $r.month, $r.registrant, $r.paymentMethod, ([datetime]$r.paymentDate).ToString("yyyy-MM-ddTHH:mm:ss"), ([math]::Round([decimal]$r.paymentAmount, 2)).ToString("0.00") }
}

# ---- Report ----
Write-Host ""
Write-Host ("=" * 60)
Write-Host ("Rollup cells:  old={0}  new={1}" -f $oldMap.Count, $newMap.Count)
Write-Host ("Penny-level deltas (float→decimal, tolerance {0}): {1}" -f $PennyTolerance, $pennies.Count)
foreach ($p in $pennies) { Write-Host "  $p" }
Write-Host ("MATERIAL mismatches: {0}" -f $mismatches.Count) -ForegroundColor $(if ($mismatches.Count -eq 0) { "Green" } else { "Red" })
foreach ($m in $mismatches) { Write-Host "  $m" -ForegroundColor Red }
if ($mismatches.Count -eq 0) { Write-Host "GOLDEN MASTER: PASS (subject to penny-delta review above)" -ForegroundColor Green }
