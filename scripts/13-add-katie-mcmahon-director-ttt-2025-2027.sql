/* ============================================================================
   Grant Katie McMahon (login: go2tournaments) the Director role on every job
   belonging to customers 'TTT' and 'Top Threat Tournaments', years 2025-2027.

   Mirrors exactly what AdministratorService.AddAdministratorAsync writes for
   one job, repeated set-based over the whole scope.

   Scope verified against TSICV5 on 2026-08-25:
       Top Threat Tournaments 2025 ....... 22 jobs
       Top Threat Tournaments 2026 ....... 19 jobs
       Top Threat Tournaments 2027 ........ 3 jobs
       TTT                    2027 ........ 1 job   (topthreat-morgansjam-2027)
       ------------------------------------------------
       TOTAL ............................. 45 jobs

   Pre-flight facts confirmed by SELECT (not assumed):
     * dbo.AspNetUsers 'go2tournaments' EXISTS
       -> b225c8b3-54d2-42b1-ae7d-e90fb94d5128, Katie McMahon,
          info@ultimategoallacrosse.com
       (NB: a SECOND Katie McMahon login 'go2Sports' / katie@go2sports.co also
        exists. This script deliberately uses 'go2tournaments' as requested.)
     * She holds 0 registrations on any of the 45 target jobs.
     * Her only 2 existing registrations are Director + SuperDirector on
       'Go2Sports:Alliance Fall Invitational 2026' -- i.e. the account is
       lane-pure for the D/SD lane, so the app's own eligibility wall would
       allow every one of these grants.
     * She is not a Family_UserId on any registration (0 rows), so the
       family-credential block does not apply.
     * All 45 jobs already have at least one active Director, so this does NOT
       move any job's PrimaryContactRegistrationId (EnsurePrimaryContactAsync
       only fills a vacancy). The !DIRECTOR substitution is unaffected.
     * All 45 jobs have ExpiryAdmin in the future, so every grant is live at
       the login role-picker immediately.

   Idempotent: re-running inserts nothing. Safe to run twice.
   Run in SSMS against TSICV5. PROD and STAGING share this database.
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @UserName    nvarchar(256)  = N'go2tournaments';
DECLARE @DirectorRole nvarchar(450) = N'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06'; -- RoleConstants.Director
DECLARE @LebUserId   nvarchar(450)  = NULL;  -- optional: AspNetUsers.Id of the SU running this (audit only)
DECLARE @Now         datetime2      = GETDATE();

DECLARE @UserId nvarchar(450) =
    (SELECT u.Id FROM dbo.AspNetUsers u WHERE u.UserName = @UserName);

IF @UserId IS NULL
BEGIN
    RAISERROR (N'Login %s not found in dbo.AspNetUsers -- aborting.', 16, 1, @UserName);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id = @DirectorRole AND Name = N'Director')
BEGIN
    RAISERROR (N'Director role id did not resolve to the Director role -- aborting.', 16, 1);
    RETURN;
END

/* ---- The target set: one place, used by preview, insert and verify -------- */
IF OBJECT_ID('tempdb..#Target') IS NOT NULL DROP TABLE #Target;

SELECT  j.JobId,
        c.CustomerName,
        j.[Year],
        j.JobName,
        j.JobPath,
        j.ExpiryAdmin
INTO    #Target
FROM    Jobs.Jobs j
JOIN    Jobs.Customers c ON c.CustomerId = j.CustomerId
WHERE   c.CustomerName IN (N'TTT', N'Top Threat Tournaments')
  AND   j.[Year] IN (N'2025', N'2026', N'2027');

/* ---- PREVIEW (read-only) -------------------------------------------------- */
SELECT  N'TARGET JOBS' AS Section,
        t.CustomerName, t.[Year], t.JobName, t.JobPath, t.ExpiryAdmin,
        AlreadyDirector = CASE WHEN EXISTS (
                              SELECT 1 FROM Jobs.Registrations r
                              WHERE r.JobId  = t.JobId
                                AND r.UserId = @UserId
                                AND r.RoleId = @DirectorRole)
                          THEN 1 ELSE 0 END
FROM    #Target t
ORDER BY t.CustomerName, t.[Year], t.JobName;

SELECT  N'SUMMARY' AS Section,
        TargetJobs  = (SELECT COUNT(*) FROM #Target),
        WillInsert  = (SELECT COUNT(*) FROM #Target t
                       WHERE NOT EXISTS (SELECT 1 FROM Jobs.Registrations r
                                         WHERE r.JobId  = t.JobId
                                           AND r.UserId = @UserId
                                           AND r.RoleId = @DirectorRole));

/* ---- INSERT --------------------------------------------------------------- */
BEGIN TRANSACTION;

INSERT INTO Jobs.Registrations
(
    RegistrationID, RegistrationTS, RegistrationCategory,
    RoleId, UserId, bActive, jobID, lebUserID, modified,
    bConfirmationSent,
    fee_base, fee_discount, fee_discount_mp, fee_donation,
    fee_latefee, fee_processing, fee_total, owed_total, paid_total
)
SELECT
    NEWID(), @Now, N'Director',
    @DirectorRole, @UserId, 1, t.JobId, @LebUserId, @Now,
    0,
    0, 0, 0, 0,
    0, 0, 0, 0, 0
FROM   #Target t
WHERE  NOT EXISTS (
           SELECT 1 FROM Jobs.Registrations r
           WHERE  r.JobId  = t.JobId
             AND  r.UserId = @UserId
             AND  r.RoleId = @DirectorRole);

DECLARE @Inserted int = @@ROWCOUNT;
PRINT CONCAT(N'Director registrations inserted for ', @UserName, N': ', @Inserted);

-- Sanity wall: never more rows than jobs in scope.
IF @Inserted > (SELECT COUNT(*) FROM #Target)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR (N'Insert count exceeded target job count -- rolled back.', 16, 1);
    RETURN;
END

COMMIT TRANSACTION;

/* ---- VERIFY --------------------------------------------------------------- */
SELECT  N'VERIFY' AS Section,
        t.CustomerName, t.[Year], t.JobName, t.JobPath,
        r.RegistrationID, r.bActive, r.RegistrationTS
FROM    #Target t
LEFT JOIN Jobs.Registrations r
       ON r.JobId  = t.JobId
      AND r.UserId = @UserId
      AND r.RoleId = @DirectorRole
ORDER BY t.CustomerName, t.[Year], t.JobName;

SELECT  N'VERIFY COUNT' AS Section,
        TargetJobs = (SELECT COUNT(*) FROM #Target),
        DirectorRegsNow = (SELECT COUNT(*) FROM #Target t
                           JOIN Jobs.Registrations r
                             ON r.JobId = t.JobId
                            AND r.UserId = @UserId
                            AND r.RoleId = @DirectorRole);

DROP TABLE #Target;
