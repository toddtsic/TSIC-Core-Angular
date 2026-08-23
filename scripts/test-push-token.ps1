<#
.SYNOPSIS
    Diagnose one mobile device end to end: where it sits in the database, which Firebase
    project owns its token, and whether those two agree.

.DESCRIPTION
    Answers "why didn't the push arrive?" in one shot.

    TSIC runs two mobile apps on two separate Firebase projects - TSIC-Events (tsic-events)
    and TSIC-Teams (tsic-teams). A registration token is scoped to the project that minted it.
    Send it through the other project's credential and FCM answers SenderIdMismatch: the call
    succeeds, the audit row looks fine, and nobody's phone rings.

    Two halves, and the interesting failures are where they disagree:

      DATABASE  which pools hold this device, and what audience each of its jobs resolves to
                (tournament/league -> Events; other types -> Teams if bEnableTSICTeams;
                showcase -> neither). See TSIC.Domain/Jobs/PushAudience.cs.

      FIREBASE  the token run against BOTH senders. Exactly one should accept.

    A device whose jobs resolve to Events while its token belongs to tsic-teams is a routing
    bug, and this is what surfaces it.

    DRY RUN BY DEFAULT - validated by Google, delivered to nobody. Safe to point at a
    stranger's device. -Send actually delivers.

.PARAMETER Token
    The FCM registration token. mobile.Devices.Id and .Token hold the same value, so a
    device id from the database works here too.

.PARAMETER Send
    Deliver for real instead of validating. Only for a device you are allowed to buzz.

.PARAMETER Body
    Notification text for a real send.

.PARAMETER SkipDb
    Firebase half only - for a token that was never filed (a fresh Xcode run, say).

.EXAMPLE
    .\scripts\test-push-token.ps1 -Token "eflDQPDi..."
    Full diagnosis, nothing delivered.

.EXAMPLE
    .\scripts\test-push-token.ps1 -Token "eflDQPDi..." -Send -Body "Practice moved to 6pm"
    Same, then actually push to that handset.

.NOTES
    Credentials (FirebaseAuth_TSICEvents.json / FirebaseAuth_TSICTeams.json) live in
    src/backend/TSIC.API and are gitignored - on the box, never in source.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Token,

    [switch]$Send,
    [string]$Body,
    [switch]$SkipDb,
    [string]$SqlServer = ".\SS2016",
    [string]$Database = "TSICV5"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$probeProject = Join-Path $repoRoot "tools\push-probe\PushProbe.csproj"
$credDir = Join-Path $repoRoot "TSIC-Core-Angular\src\backend\TSIC.API"

if (-not (Test-Path $probeProject)) {
    Write-Error "Probe project not found at $probeProject"
    exit 1
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  TSIC push token diagnosis" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------------------------
# Half 1: the database. Where is this device filed, and what does each job resolve to?
# ---------------------------------------------------------------------------------------------
if (-not $SkipDb) {
    Write-Host "--- DATABASE: where this device is filed ---" -ForegroundColor Yellow
    Write-Host ""

    # Read-only throughout. RegistrationID is the app discriminator on Device_Teams: the
    # TSIC-Teams app writes it at login, the TSIC-Events favourite-team toggle never does.
    $sql = @"
SET NOCOUNT ON;
DECLARE @tok varchar(200) = '$($Token -replace "'", "''")';

IF NOT EXISTS (SELECT 1 FROM mobile.Devices WHERE Id = @tok OR Token = @tok)
BEGIN
    PRINT 'No mobile.Devices row - this token has never been filed by any app.';
END
ELSE
BEGIN
    SELECT 'device' AS scope, Type AS detail, CAST(Active AS varchar(5)) AS val,
           CONVERT(varchar(19), modified, 120) AS modified
    FROM mobile.Devices WHERE Id = @tok OR Token = @tok;

    SELECT 'Device_Jobs (Events pool)' AS pool, COUNT(*) AS rows_
    FROM mobile.Device_Jobs WHERE DeviceId = @tok
    UNION ALL
    SELECT 'Device_Teams w/ RegID (Teams pool)', COUNT(*)
    FROM mobile.Device_Teams WHERE DeviceId = @tok AND RegistrationID IS NOT NULL
    UNION ALL
    SELECT 'Device_Teams no RegID (Events favourites)', COUNT(*)
    FROM mobile.Device_Teams WHERE DeviceId = @tok AND RegistrationID IS NULL;

    -- Mirrors PushAudienceResolver. Keep in step if that rule ever changes.
    SELECT jt.JobTypeName AS job_type, j.bEnableTSICTeams AS teams_flag,
           CASE WHEN j.JobTypeID IN (2,3) THEN 'Events'
                WHEN j.JobTypeID = 6      THEN 'None (showcase)'
                WHEN j.bEnableTSICTeams = 1 THEN 'Teams'
                ELSE 'None (Teams not enabled)' END AS resolves_to,
           COUNT(*) AS jobs
    FROM mobile.Device_Jobs dj
    JOIN Jobs.Jobs j ON j.JobID = dj.JobID
    JOIN reference.JobTypes jt ON jt.JobTypeID = j.JobTypeID
    WHERE dj.DeviceId = @tok
    GROUP BY jt.JobTypeName, j.bEnableTSICTeams, j.JobTypeID
    ORDER BY jt.JobTypeName;
END
"@

    & sqlcmd -S $SqlServer -d $Database -E -C -W -s"|" -Q $sql
    if (-not $?) {
        Write-Host "  (database lookup failed - continuing to the Firebase half)" -ForegroundColor DarkYellow
    }
    Write-Host ""
}

# ---------------------------------------------------------------------------------------------
# Half 2: Firebase. Which project actually owns this token?
# ---------------------------------------------------------------------------------------------
Write-Host "--- FIREBASE: which project owns this token ---" -ForegroundColor Yellow
Write-Host ""

if ($Send) {
    Write-Host "  REAL SEND - this will buzz the handset." -ForegroundColor Red
} else {
    Write-Host "  Dry run - validated by Google, delivered to nobody." -ForegroundColor Green
}
Write-Host ""

$probeArgs = @($Token, "--creds=$credDir")
if ($Send) { $probeArgs += "--send" }
if ($Body) { $probeArgs += "--body=$Body" }

& dotnet run --project $probeProject --verbosity quiet -- @probeArgs
$probeExit = $LASTEXITCODE

Write-Host ""
switch ($probeExit) {
    0 { Write-Host "RESULT: healthy - exactly one project owns this token." -ForegroundColor Green }
    2 { Write-Host "RESULT: no project accepted it - dead token, or a broken credential." -ForegroundColor Red }
    3 { Write-Host "RESULT: both projects accepted - investigate before routing anything." -ForegroundColor Red }
    default { Write-Host "RESULT: probe failed to run (exit $probeExit)." -ForegroundColor Red }
}

Write-Host ""
Write-Host "Compare the two halves: if the jobs resolve to one app and the token belongs to the" -ForegroundColor DarkGray
Write-Host "other, that job's pushes to this device fail with SenderIdMismatch." -ForegroundColor DarkGray
Write-Host ""

exit $probeExit
