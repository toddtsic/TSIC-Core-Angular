<#
.SYNOPSIS
    Fires the month-end close and mails the QuickBooks .zip - the real send, end to end.

.DESCRIPTION
    Sends the 1st-of-the-month email, for real: ONE message carrying the close verdict, the day's sweep
    digest, and the QuickBooks .zip.

    Calls POST /api/adn-reconciliation/email-close?includeSweep=true as a SuperUser, which invokes
    IAdnReconciliationService.RunMonthEndCloseWithSweepAsync -- the VERY SAME method
    AdnSweepBackgroundService calls at 5am on the 1st. Run the sweep with its digest suppressed, run the
    close, mail one email, attach the .zip only if the sweep was trustworthy.

    This is not a reconstruction of the scheduled path. It IS the scheduled path: both callers invoke
    the one method, so the two cannot drift.

    It is also the only way to see that email off Production. AdnSweepBackgroundService is gated to
    TSIC-PHOENIX (IsLiveProduction), so on Staging the 5am timer never fires.

    WHY IT REALLY SENDS FROM STAGING
      * The close mails with sendInDevelopment: true, so it bypasses the IsSandbox() short-circuit in
        EmailService - the same bypass the daily sweep digest already uses.
      * EmailSettings.EmailingEnabled defaults to true; no config needed.
      * The reconciliation's ADN pull is hardcoded to AuthorizeNet.Environment.PRODUCTION, so the zip is
        built from REAL production settlements, not sandbox data. The attachment is the real thing.

    WHAT IT DOES NOT PROVE
      The SWEEP, unlike the reconciliation, resolves Authorize.Net from the ambient environment
      (AdnApiService.GetADNEnvironment - sandbox on any non-prod host, deliberately no override). So off
      PHOENIX the sweep queries the SANDBOX account, finds no production batches, and its digest comes
      back EMPTY. The email's composition, the trust gate and the attachment are real; the digest's
      CONTENTS are only real on PHOENIX. You also only ever see the gate's PASS branch here, since an
      empty sandbox sweep succeeds cleanly.

    PREREQUISITE - AWS CREDENTIALS
      SES needs AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY / AWS_REGION on the API's app pool
      (dev-api for Staging). Applied by scripts\IIS-Config-Dev\Setup\07-Apply-Secrets.ps1.
      If they are absent, EmailService.SendAsync returns false and LOGS - it does not throw. So the
      endpoint still returns 200 with a clean-looking body and NO mail arrives. A 200 is not proof of
      delivery; your inbox is. See the post-run checklist this script prints.

.PARAMETER BaseUrl
    API root. Defaults to Staging (dev-api). Use https://localhost:7215/api for a local F5,
    or https://claude-api.teamsportsinfo.com/api for Production.

.PARAMETER Username
    SuperUser login. Prompted for if omitted.

.PARAMETER Password
    SuperUser password. Prompted for (masked) if omitted.

.PARAMETER SettlementMonth
    Month to close (1-12). Defaults to last month - same default the endpoint uses.

.PARAMETER SettlementYear
    Year to close. Defaults to last month's year.

.PARAMETER RegId
    Registration id to authenticate under. Only needed if auto-selection cannot find a single
    SuperUser registration for the account.

.EXAMPLE
    # Staging, last month, prompts for credentials
    .\testEndOfMonthZippedEmail.ps1

.EXAMPLE
    # A specific month
    .\testEndOfMonthZippedEmail.ps1 -SettlementMonth 6 -SettlementYear 2026

.EXAMPLE
    # Local F5
    .\testEndOfMonthZippedEmail.ps1 -BaseUrl https://localhost:7215/api

.NOTES
    Read-only against Authorize.Net. Writes adn.Txs for the month (delete-then-insert, idempotent) and
    runs the reg export sproc, which is NOT read-only - it execs adn.UpdateMonthlyJobStats_CalcFields.
    Re-running is safe; it is exactly what the operator does from the UI.

    ASCII only, deliberately: Windows PowerShell 5.1 reads a UTF-8 file with no BOM as ANSI, and any
    non-ASCII character here becomes mojibake that can break the parse. Do not paste in em-dashes.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://devapi.teamsportsinfo.com/api',
    [string]$Username,
    [string]$Password,
    [ValidateRange(1, 12)]
    [int]$SettlementMonth,
    [int]$SettlementYear,
    [string]$RegId
)

$ErrorActionPreference = 'Stop'

function Write-Info($m) { Write-Host "[INFO]  $m" -ForegroundColor Cyan }
function Write-Done($m) { Write-Host "[OK]    $m" -ForegroundColor Green }
function Write-Warn2($m) { Write-Host "[WARN]  $m" -ForegroundColor Yellow }
function Write-Err($m) { Write-Host "[FAIL]  $m" -ForegroundColor Red }

# --- Target month: default to last month, mirroring the endpoint's own ResolveMonthYear ---
if (-not $SettlementMonth -or -not $SettlementYear) {
    $lastMonth = (Get-Date -Day 1).AddMonths(-1)
    if (-not $SettlementMonth) { $SettlementMonth = $lastMonth.Month }
    if (-not $SettlementYear) { $SettlementYear = $lastMonth.Year }
}
$monthName = (Get-Date -Year $SettlementYear -Month $SettlementMonth -Day 1).ToString('MMMM yyyy')
$monthPad = '{0:D2}' -f $SettlementMonth
$zipName = "TSIC-AdnReconciliation-$SettlementYear-$monthPad.zip"

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host " ADN 1st-of-Month Email - REAL SEND TEST" -ForegroundColor Magenta
Write-Host " API:    $BaseUrl" -ForegroundColor Magenta
Write-Host " Month:  $monthName" -ForegroundColor Magenta
Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host ""
Write-Info "Calls RunMonthEndCloseWithSweepAsync - the SAME method the 5am"
Write-Info "BackgroundService calls on the 1st. Not a reconstruction of it."
Write-Host ""

# --- Credentials ---
if (-not $Username) { $Username = Read-Host "SuperUser username" }
if (-not $Password) {
    $secure = Read-Host "Password for $Username" -AsSecureString
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

# --- Auth ---
# quick-login is the single-call path: it returns an ENRICHED token straight away when the account
# resolves to one registration. When it resolves to several it returns a MINIMAL token plus the role
# list, and we re-call with the SuperUser regId. The enriched token is the one carrying the role claim
# that the [Authorize(Roles = "Superuser")] endpoint requires.
function Invoke-QuickLogin {
    param([string]$Reg)

    $body = @{ username = $Username; password = $Password }
    if ($Reg) { $body.regId = $Reg }

    return Invoke-RestMethod -Method Post -Uri "$BaseUrl/auth/quick-login" `
        -ContentType 'application/json' -Body ($body | ConvertTo-Json) -TimeoutSec 60
}

Write-Info "Authenticating as $Username ..."
try {
    $auth = Invoke-QuickLogin -Reg $RegId
}
catch {
    Write-Err "Login failed: $($_.Exception.Message)"
    Write-Host "  Check the username/password, and that $BaseUrl is reachable." -ForegroundColor White
    exit 1
}

# A registrations list came back, which means the account has more than one role and the token we hold
# is MINIMAL (no role claim). Pick the SuperUser registration and re-login for the enriched token.
if ($auth.registrations) {
    $suRole = $auth.registrations | Where-Object { $_.roleName -match 'superuser' } | Select-Object -First 1
    if (-not $suRole) {
        Write-Err "No SuperUser role found for $Username."
        $roleList = ($auth.registrations | ForEach-Object { $_.roleName }) -join ', '
        Write-Host "  Roles available: $roleList" -ForegroundColor White
        Write-Host "  email-close is [Authorize(Roles = 'Superuser')]." -ForegroundColor White
        exit 1
    }

    $reg = $suRole.roleRegistrations | Select-Object -First 1
    Write-Info "Multiple roles; selecting SuperUser registration $($reg.regId)"

    try {
        $auth = Invoke-QuickLogin -Reg $reg.regId
    }
    catch {
        Write-Err "Role selection failed: $($_.Exception.Message)"
        exit 1
    }
}

if (-not $auth.accessToken) {
    Write-Err "No access token returned. Cannot continue."
    exit 1
}
Write-Done "Authenticated."

# --- Fire it ---
$uri = "$BaseUrl/adn-reconciliation/email-close?settlementMonth=$SettlementMonth&settlementYear=$SettlementYear&includeSweep=true"

Write-Host ""
Write-Info "POST $uri"
Write-Info "Running the sweep (digest suppressed), then the close, then ONE email."
Write-Info "Pulling ADN batches, running both sprocs, building the zip, sending the mail."
Write-Info "This takes a while - the sprocs aggregate a full month. Do not kill it."
Write-Host ""

$started = Get-Date
try {
    $response = Invoke-RestMethod -Method Post -Uri $uri `
        -Headers @{ Authorization = "Bearer $($auth.accessToken)" } -TimeoutSec 900
}
catch {
    Write-Err "email-close failed: $($_.Exception.Message)"
    if ($_.ErrorDetails.Message) {
        Write-Host "  $($_.ErrorDetails.Message)" -ForegroundColor White
    }
    Write-Host ""
    Write-Host "  401 -> not logged in.  403 -> the account is not a SuperUser." -ForegroundColor White
    Write-Host "  500 -> check the API log; the ADN pull or a reconciliation sproc threw." -ForegroundColor White
    exit 1
}
$elapsed = (Get-Date) - $started

Write-Done "Endpoint returned 200 in $([math]::Round($elapsed.TotalSeconds, 1))s."

# --- Unwrap: MonthEndCloseResult { sweep, close, filesAttached, closeError } ---
$sweep = $response.sweep
$result = $response.close
$filesAttached = $response.filesAttached

Write-Host ""
Write-Host "Sweep (the daily pass; its digest is folded into the close email)" -ForegroundColor Cyan
$sweepColor = if ($sweep.succeeded -and $sweep.errored -eq 0) { 'Green' } else { 'Red' }
Write-Host ("  Succeeded:    {0}" -f $sweep.succeeded) -ForegroundColor $sweepColor
Write-Host ("  Checked:      {0}" -f $sweep.checked)
Write-Host ("  ARB imported: {0}" -f $sweep.arbImported)
Write-Host ("  eCheck settled / returns: {0} / {1}" -f $sweep.echeckSettled, $sweep.echeckReturnsProcessed)
Write-Host ("  Orphans:      {0}" -f $sweep.orphansFound)
Write-Host ("  Errored:      {0}" -f $sweep.errored) -ForegroundColor $sweepColor
if ($sweep.errorMessage) {
    Write-Host ("  Error:        {0}" -f $sweep.errorMessage) -ForegroundColor Red
}

if ($sweep.checked -eq 0 -and $sweep.succeeded -and $BaseUrl -notmatch 'claude-api') {
    Write-Warn2 "Checked=0 is EXPECTED off PHOENIX: the sweep queries the ADN SANDBOX there, so it"
    Write-Warn2 "sees no production batches. The digest in the email will be empty. Composition and"
    Write-Warn2 "gate are real; digest contents are only real on PHOENIX."
}

Write-Host ""
if ($filesAttached) {
    Write-Done "Sweep trustworthy -> the .zip WAS attached to the email."
}
else {
    Write-Warn2 "Sweep NOT trustworthy -> the .zip was WITHHELD. The email carries the alarm only."
    Write-Warn2 "That is the gate working: a sweep that did not fully succeed leaves the closing"
    Write-Warn2 "month's payments unbooked, so the .iif would be short them."
}

if ($response.closeError) {
    Write-Host ""
    Write-Err "The close itself failed: $($response.closeError)"
    Write-Warn2 "The fallback mailed the sweep digest alone. No files were produced."
    exit 1
}

# --- Report ---
Write-Host ""
Write-Host "Import" -ForegroundColor Cyan
Write-Host ("  Batches pulled:       {0}" -f $result.batchesPulled)
Write-Host ("  Transactions pulled:  {0}" -f $result.transactionsPulled)
Write-Host ("  Imported (new):       {0}" -f $result.imported)
Write-Host ("  Skipped (duplicates): {0}" -f $result.skippedDuplicates)
Write-Host ""
Write-Host "IIF TRNS parity (consolidated / source - these MUST match)" -ForegroundColor Cyan

$regOk = $result.regConsolidatedTrnsCount -eq $result.regSourceTrnsCount
$merchOk = $result.merchConsolidatedTrnsCount -eq $result.merchSourceTrnsCount

$regColor = if ($regOk) { 'Green' } else { 'Red' }
$merchColor = if ($merchOk) { 'Green' } else { 'Red' }

Write-Host ("  Registration: {0} / {1}" -f $result.regConsolidatedTrnsCount, $result.regSourceTrnsCount) -ForegroundColor $regColor
Write-Host ("  Merch:        {0} / {1}" -f $result.merchConsolidatedTrnsCount, $result.merchSourceTrnsCount) -ForegroundColor $merchColor

if (-not ($regOk -and $merchOk)) {
    Write-Host ""
    Write-Warn2 "TRNS parity mismatch - a source transaction did not survive consolidation."
    Write-Warn2 "The .iif was still produced. Verify it before importing to QuickBooks."
}

if ($result.transactionsPulled -eq 0) {
    Write-Host ""
    Write-Warn2 "Authorize.Net returned no transactions for $monthName. The zip will be near-empty."
    Write-Warn2 "Pick a month you know has settlements before concluding anything about the email."
}

# --- The part that actually matters ---
Write-Host ""
Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host " NOW CHECK YOUR INBOX" -ForegroundColor Magenta
Write-Host "==========================================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "  ONE email - the exact message the 5am job sends on the 1st:" -ForegroundColor White
Write-Host "    Subject:  'AdnSweep + MonthEndClose - $monthName'" -ForegroundColor White
Write-Host "    Top:      the close verdict (accounting match, IIF parity)" -ForegroundColor Gray
Write-Host "    Below:    the day's sweep digest, under a rule" -ForegroundColor Gray
if ($filesAttached) {
    Write-Host "    Attached: $zipName" -ForegroundColor White
    Write-Host "                - reg-consolodated.iif" -ForegroundColor Gray
    Write-Host "                - merch-consolodated.iif" -ForegroundColor Gray
    Write-Host "                - Reg / Merch / Summary .xlsx" -ForegroundColor Gray
}
else {
    Write-Host "    Attached: NOTHING - the sweep was not trustworthy" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "  A 200 above does NOT prove the mail was sent. EmailService.SendAsync returns false and" -ForegroundColor Yellow
Write-Host "  logs on an SES failure - it does not throw, so the endpoint still answers 200." -ForegroundColor Yellow
Write-Host ""
Write-Host "  If no email arrives:" -ForegroundColor Cyan
Write-Host "    1. API log - look for 'SES send failed' or an AWS credential exception." -ForegroundColor White
Write-Host "    2. Seq - @MessageTemplate like '%month-end close emailed%' (logs sent=True/False)." -ForegroundColor White
Write-Host "    3. Most likely cause: AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY / AWS_REGION are not" -ForegroundColor White
Write-Host "       on the API's app pool. Apply with scripts\IIS-Config-Dev\Setup\07-Apply-Secrets.ps1" -ForegroundColor White
Write-Host "       and restart the pool." -ForegroundColor White
Write-Host ""
