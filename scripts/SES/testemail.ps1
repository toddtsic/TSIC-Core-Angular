<#
.SYNOPSIS
  Send a test email through Amazon SES (one clean probe PER recipient) and
  report each outcome using the account-level CloudWatch metrics SES publishes
  automatically.

.DESCRIPTION
  No SNS topic, no SQS queue, no configuration set. Per recipient it:
    1. Checks the account-level suppression list (definitive, synchronous -
       catches a whole class of "not delivered" on its own).
    2. Sends one test message and records the MessageId.
    3. Polls the AWS/SES CloudWatch namespace, which SES populates automatically,
       watching whether Delivery / Bounce / Reject ticks up after the send.

  Multiple recipients are probed ONE AT A TIME (separate send + verdict each),
  not bundled into a single message - so each address gets an attributable
  result. Suppression is per-address, and a single multi-To message would tick
  the account-wide Bounce counter +1 with no way to tell which address bounced.

  TRADEOFFS (read these):
    * CloudWatch AWS/SES metrics are ACCOUNT-WIDE totals, not per-MessageId.
      Run this when the account is otherwise quiet, or accept that a concurrent
      send could move the same counter. The MessageId is printed so you can
      correlate against feedback-forwarding email or the console. With several
      recipients in one run, a slow delivery from probe A can land during probe
      B's poll window - space them out or test one at a time if it matters.
    * These counts tell you WHICH outcome, not WHY. The bounce diagnostic text
      arrives separately by email via Email Feedback Forwarding (on by default).
    * SES account metrics lag a few minutes, hence the longer default timeout.

.BEFORE YOU RUN (TSIC-specific)
    * REGION MUST MATCH PRODUCTION. The suppression list, the CloudWatch metrics,
      and the identity verification are ALL per-region. The TSIC app reads its
      region from the Phoenix IIS app pool env var (AWS_REGION / AWS_DEFAULT_REGION),
      NOT from appsettings. Default here is us-west-2 (Oregon) to match. If you
      probe the wrong region, "not suppressed / no bounce" proves nothing.
    * Production sends through SES v1 (Amazon.SimpleEmail / SendRawEmailAsync). This
      script uses the SES v2 cmdlets, which is fine: the suppression list and the
      AWS/SES metrics it reads are account-scoped, shared across both API versions.
    * Default sender identity is support@teamsportsinfo.com (just press Enter at
      the From prompt). Must be a verified SES identity in -Region.

.REQUIREMENTS
  AWS.Tools.SimpleEmailV2 and AWS.Tools.CloudWatch modules. The script always
  PROMPTS at run time for sender, recipient(s), and credentials (Access Key ID =
  username, Secret Access Key = password); nothing is stored. It is an
  interactive diagnostic, not an unattended job.

.EXAMPLE
  ./testemail.ps1
    # prompts: From (Enter = support@), recipients (semicolon-separated), credentials

  ./testemail.ps1 -To "a@x.com; b@y.com" -TimeoutSec 420
    # recipients inline; still prompts From + credentials
#>

#Requires -Modules AWS.Tools.SimpleEmailV2, AWS.Tools.CloudWatch

param(
  [string]$From,                        # sender; prompted (Enter = support@teamsportsinfo.com)
  [string]$To,                          # recipient(s), semicolon/comma-separated; prompted if absent
  [string]$Region      = "us-west-2",   # Oregon - TSIC production SES region
  [switch]$WaitForOutcome,              # OFF by default: skip the slow CloudWatch poll; just suppression + send
  [int]   $TimeoutSec  = 360,           # only used with -WaitForOutcome
  [string]$Subject     = "TSIC Email Test",
  [string]$Body        = "This is an automated email delivery test from TEAMSPORTSINFO.COM. No action is needed - please disregard."
)

$ErrorActionPreference = "Stop"
$DefaultFrom = "support@teamsportsinfo.com"

# ---- sender ---------------------------------------------------------------
if (-not $From) {
  $inFrom = (Read-Host "Sender (From) [press Enter for $DefaultFrom]").Trim()
  $From   = if ($inFrom) { $inFrom } else { $DefaultFrom }
}

# ---- recipient(s) ---------------------------------------------------------
if (-not $To) { $To = Read-Host "Recipient(s) to probe (semicolon-separated for multiple)" }
$recipients = @($To -split '[;,]' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if (-not $recipients) { throw "No recipient supplied; aborting." }

# ---- 0. credentials (always prompted; nothing stored) ---------------------
# The keys live on the Phoenix claude-api app pool (AWS_ACCESS_KEY_ID /
# AWS_SECRET_ACCESS_KEY) if you need to recover them. In the prompt, enter the
# Access Key ID as the username and the Secret Access Key as the password (masked).
$cred = Get-Credential -UserName 'AKIA...' `
          -Message 'AWS SES credentials - USERNAME = Access Key ID, PASSWORD = Secret Access Key'
if (-not $cred -or -not $cred.UserName) { throw "No credentials entered; aborting." }
Set-AWSCredential -AccessKey $cred.UserName -SecretKey $cred.GetNetworkCredential().Password

function Get-SesMetricSum {
  param([string]$Metric, [datetime]$Since)
  # Account-level AWS/SES metrics carry no dimensions. Sum all datapoints in window.
  $end = (Get-Date).ToUniversalTime().AddMinutes(5)
  $stats = Get-CWMetricStatistic -Region $Region -Namespace "AWS/SES" `
             -MetricName $Metric -StartTime $Since -EndTime $end `
             -Period 60 -Statistic Sum
  if (-not $stats.Datapoints) { return 0 }
  [double](($stats.Datapoints | Measure-Object -Property Sum -Sum).Sum)
}

function Invoke-SesProbe {
  param([string]$Recipient)

  Write-Host ""
  Write-Host "########## PROBE: $Recipient ##########" -ForegroundColor White

  # ---- 1. suppression pre-check (synchronous, definitive) -----------------
  $supState  = "Unknown"   # No | Yes | Unknown - fed into the client report
  $supReason = $null
  Write-Host "== Checking account-level suppression list for $Recipient" -ForegroundColor Cyan
  try {
    $sup = Get-SES2SuppressedDestination -Region $Region -EmailAddress $Recipient
    $supReason = if ($sup.Reason) { $sup.Reason } else { $sup.SuppressedDestination.Reason }
    $supState  = "Yes"
    Write-Host "   !! $Recipient IS suppressed (reason: $supReason)." -ForegroundColor Yellow
    Write-Host "      SES will treat the send as a hard bounce. Remove it first with:"
    Write-Host "      Remove-SES2SuppressedDestination -Region $Region -EmailAddress '$Recipient'"
    Write-Host "      Continuing so you can observe the bounce..."
  }
  catch {
    if ($_.Exception.GetType().Name -match 'NotFound' -or $_.Exception.Message -match 'NotFound|does not exist') {
      $supState = "No"
      Write-Host "   ok: not on the account-level suppression list." -ForegroundColor Green
    } else {
      $supState = "Unknown"
      Write-Host "   (suppression check inconclusive: $($_.Exception.Message))"
    }
  }

  # ---- 2. baseline (only when waiting for outcome), then send -------------
  if ($WaitForOutcome) {
    $start = (Get-Date).ToUniversalTime().AddMinutes(-1)   # small backstop for clock skew
    $baseDelivery = Get-SesMetricSum -Metric "Delivery" -Since $start
    $baseBounce   = Get-SesMetricSum -Metric "Bounce"   -Since $start
    $baseReject   = Get-SesMetricSum -Metric "Reject"   -Since $start
  }

  Write-Host "== Sending test  $From -> $Recipient" -ForegroundColor Cyan
  $msgId = Send-SES2Email -Region $Region `
             -FromEmailAddress $From `
             -Destination_ToAddress $Recipient `
             -Subject_Data $Subject `
             -Text_Data $Body
  Write-Host "   accepted by SES. MessageId = $msgId" -ForegroundColor Green

  # ---- 3. outcome ---------------------------------------------------------
  if (-not $WaitForOutcome) {
    # Fast path: the instant facts (not suppressed + SES accepted) are the answer.
    $verdict = "SENT"
  }
  else {
    Write-Host "== Polling AWS/SES CloudWatch metrics (up to ${TimeoutSec}s; ~minutes of lag is normal)..." -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $verdict  = $null

    while ((Get-Date) -lt $deadline) {
      Start-Sleep -Seconds 20

      $dDelivery = (Get-SesMetricSum -Metric "Delivery" -Since $start) - $baseDelivery
      $dBounce   = (Get-SesMetricSum -Metric "Bounce"   -Since $start) - $baseBounce
      $dReject   = (Get-SesMetricSum -Metric "Reject"   -Since $start) - $baseReject

      $remaining = [int]($deadline - (Get-Date)).TotalSeconds
      Write-Host ("   delta  Delivery=+{0}  Bounce=+{1}  Reject=+{2}   ({3}s left)" -f $dDelivery,$dBounce,$dReject,$remaining)

      if     ($dBounce   -ge 1) { $verdict = "BOUNCE";   break }
      elseif ($dReject   -ge 1) { $verdict = "REJECT";   break }
      elseif ($dDelivery -ge 1) { $verdict = "DELIVERY"; break }
    }
  }

  # ---- 4. report ----------------------------------------------------------
  Write-Host ""
  Write-Host "============ RESULT: $Recipient ============" -ForegroundColor White
  switch ($verdict) {
    "SENT" {
      Write-Host "VERDICT: SENT (accepted by SES)" -ForegroundColor Green
      Write-Host "  Not on the suppression list, and SES accepted the message for delivery."
      Write-Host "  That clears the two things on OUR side. If it doesn't arrive, it is being"
      Write-Host "  filtered/quarantined on the receiving side (junk, spam, or gateway block)."
    }
    "DELIVERY" {
      Write-Host "VERDICT: DELIVERED" -ForegroundColor Green
      Write-Host "  SES handed the message to the recipient's mail server."
      Write-Host "  If the recipient still says they never got it, the issue is on their"
      Write-Host "  side (spam folder, filtering) - not SES."
    }
    "BOUNCE" {
      Write-Host "VERDICT: BOUNCED" -ForegroundColor Red
      Write-Host "  A bounce was recorded. The metric doesn't carry the reason - the"
      Write-Host "  diagnostic text was emailed to your identity's Return-Path via"
      Write-Host "  Email Feedback Forwarding. Check that inbox for the 'why'."
    }
    "REJECT" {
      Write-Host "VERDICT: REJECTED by SES" -ForegroundColor Red
      Write-Host "  SES accepted then dropped it (commonly: message contained a virus)."
    }
    default {
      Write-Host "VERDICT: NO outcome metric moved within ${TimeoutSec}s." -ForegroundColor Yellow
      Write-Host "  The send was accepted (MessageId $msgId) but no Delivery/Bounce/Reject"
      Write-Host "  surfaced yet. Likely the recipient domain is deferring (greylisting) and"
      Write-Host "  SES is still retrying, or metrics are just lagging. Re-run with a larger"
      Write-Host "  -TimeoutSec, and check the feedback-forwarding inbox."
    }
  }
  if ($WaitForOutcome) {
    Write-Host "  (Account-wide counters - if you sent other mail during this window, correlate"
    Write-Host "   with MessageId $msgId via the feedback inbox or SES console to be sure.)"
  }
  Write-Host "===========================================" -ForegroundColor White

  $verdictText = if ($verdict) { $verdict } else { "NO-SIGNAL" }
  [pscustomobject]@{
    Recipient  = $Recipient
    Verdict    = $verdictText
    MessageId  = $msgId
    Suppressed = $supState
    SupReason  = $supReason
  }
}

# ---- client-facing findings + conclusion, in Amazon SES's "voice" ---------
function Get-SesFindingLines {
  param($Row)
  $L = @()
  $L += "Recipient tested:  $($Row.Recipient)"
  $L += ""
  $L += "What Amazon SES reported:"

  switch ($Row.Suppressed) {
    "No"    { $L += "  - Block / suppression list: NOT BLOCKED. This address is not on Amazon SES's do-not-send list." }
    "Yes"   { $L += "  - Block / suppression list: BLOCKED. This address is on Amazon SES's suppression list (reason: $($Row.SupReason))." }
    default { $L += "  - Block / suppression list: could not be checked (test credentials lack permission)." }
  }
  switch ($Row.Verdict) {
    "SENT"     { $L += "  - Send attempt: ACCEPTED. Amazon SES accepted the message and dispatched it toward the recipient's mail server." }
    "DELIVERY" { $L += "  - Send attempt: DELIVERED. Amazon SES handed the message to the recipient's mail server, which accepted it." }
    "BOUNCE"   { $L += "  - Send attempt: BOUNCED. The recipient's mail server refused the message." }
    "REJECT"   { $L += "  - Send attempt: REJECTED. Amazon SES stopped the message before sending it." }
    default    { $L += "  - Send attempt: ACCEPTED, but no delivery confirmation appeared within the wait window." }
  }
  if ($Row.MessageId) { $L += "  - Amazon SES tracking reference (Message ID): $($Row.MessageId)" }

  $L += ""
  $L += "CONCLUSION:"
  if ($Row.Suppressed -eq "Yes") {
    $L += "  This is on the SENDING side and is fixable by us. Amazon SES is"
    $L += "  withholding delivery because this address is on our suppression list"
    $L += "  (reason: $($Row.SupReason)) from a previous bounce or complaint. The"
    $L += "  address must be removed from suppression - and the original cause"
    $L += "  resolved - before mail will reach this recipient."
  }
  elseif ($Row.Verdict -eq "BOUNCE") {
    $L += "  This is on the RECIPIENT'S side. Their mail server actively refused"
    $L += "  the message (a bounce). The address may be mistyped or no longer"
    $L += "  exist, or their server rejected it. Please confirm the exact address."
  }
  elseif ($Row.Verdict -eq "REJECT") {
    $L += "  This is on the SENDING side. Amazon SES blocked the message before"
    $L += "  sending (commonly flagged content or an attachment). The message"
    $L += "  content needs review."
  }
  elseif ($Row.Verdict -eq "SENT" -or $Row.Verdict -eq "DELIVERY") {
    $L += "  The message left TEAMSPORTSINFO.COM successfully. Amazon SES confirmed"
    $L += "  this address is NOT blocked on our side and accepted the message for"
    $L += "  delivery. There is nothing wrong on the sending side."
    $L += ""
    $L += "  >> If the message was not received, it is being filtered or held on the"
    $L += "     RECIPIENT'S end - almost always a junk/spam folder, or a mail-gateway"
    $L += "     or security filter that quarantined it silently."
    $L += ""
    $L += "  Recommended: the recipient (or their email/IT provider) should check the"
    $L += "  spam/junk folder and any quarantine, and add support@teamsportsinfo.com"
    $L += "  to their safe-senders / allowed list."
  }
  else {
    $L += "  Inconclusive. Amazon SES accepted the message for sending, but no"
    $L += "  delivery confirmation was available in the time allowed. Re-test, or"
    $L += "  check the support inbox for any bounce notification."
  }
  $L
}

# ---- run one probe per recipient ------------------------------------------
if ($recipients.Count -gt 1) {
  Write-Host "Probing $($recipients.Count) recipients sequentially: $($recipients -join ', ')" -ForegroundColor Cyan
}
$summary = foreach ($r in $recipients) { Invoke-SesProbe -Recipient $r }

# ---- copyable, client-ready summary ---------------------------------------
$stamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'")
$lines  = @()
$lines += "============================================================"
$lines += "  TEAMSPORTSINFO.COM - Email Delivery Test"
$lines += "============================================================"
$lines += "  Performed:      $stamp"
$lines += "  Sent from:      $From"
$lines += "  Email service:  Amazon SES (Amazon Web Services), region $Region"
$lines += ""
$lines += "  This is an automated delivery test performed through Amazon SES,"
$lines += "  the email-sending service used by teamsportsinfo.com. Each step"
$lines += "  below is what Amazon SES itself reported."
$lines += "============================================================"
foreach ($row in $summary) {
  $lines += ""
  $lines += (Get-SesFindingLines $row)
  $lines += ""
  $lines += "------------------------------------------------------------"
}
$report = ($lines -join [Environment]::NewLine)

Write-Host ""
Write-Host "v v v v v   COPY FROM HERE   v v v v v" -ForegroundColor White
Write-Host $report -ForegroundColor Gray
Write-Host "^ ^ ^ ^ ^   COPY TO HERE     ^ ^ ^ ^ ^" -ForegroundColor White

try {
  Set-Clipboard -Value $report
  Write-Host ""
  Write-Host "[copied] The summary above is now on your clipboard - just paste (Ctrl+V) into your reply." -ForegroundColor Green
}
catch {
  Write-Host ""
  Write-Host "[note] Could not reach the clipboard automatically; select the block above and copy it manually." -ForegroundColor Yellow
}
