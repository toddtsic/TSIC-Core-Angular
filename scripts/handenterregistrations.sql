/* ============================================================================
   HAND-ENTER REGISTRATIONS  —  bulk-import a roster under ONE family account
   ----------------------------------------------------------------------------
   Use when a director hands you a spreadsheet of players to "bang in by hand"
   (e.g. a foreign club / sponsored team paid offline). Creates:

       1 dbo.AspNetUsers      -> family/account holder (mom's info)
       1 dbo.Families         -> Mom_* / Dad_* contact row
       N dbo.AspNetUsers      -> one child player account per roster line
       N dbo.Family_Members   -> link each child to the family
       N Jobs.Registrations   -> active, assigned to @TeamId, fee fully comped ($0)

   The job-specific shape (player RoleId, registration category, form name, fee,
   gender) is DERIVED from @JobId/@TeamId at runtime, so this script is reusable:
   point it at any job+team, paste your players, run.

   This script COMMITS. Everything runs in a single transaction (all-or-nothing
   via XACT_ABORT) and is persisted, then verification counts are printed.
   IDEMPOTENT: safe to re-run. Existing children (matched on name + DOB) and
   existing registrations (same child+job+team) are skipped, so a second run
   inserts nothing. You can also re-run after ADDING players to the roster to
   import only the new ones.

   RUN (local or prod — both are the .\SS2016 named instance, db TSICV5):
       sqlcmd -S "lpc:.\SS2016" -d TSICV5 -I -b -f 65001 -i "scripts\handenterregistrations.sql"
   -f 65001 = UTF-8; REQUIRED so accented names (e.g. Zoe -> Zoë) survive.
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

/* ========================= PARAMETERS — EDIT THESE ========================= */
DECLARE @JobId     uniqueidentifier = '20ED45C5-7872-4F4B-9417-498C444B4C77'; -- target job
DECLARE @TeamId    uniqueidentifier = '4ECD475D-1714-4296-9183-CA8593408100'; -- target team (must belong to @JobId)

/* the single family/account holder these players hang off of.
   Use a FRESH @ParentId GUID for each distinct import group (it is the cleanup key). */
DECLARE @ParentId  nvarchar(450)    = '9e0a7d4b-3c2f-4b1a-bf6e-7a1d2c3e4f50';
DECLARE @AcctUser  nvarchar(256)    = 'TeamEngland';
DECLARE @Email     nvarchar(256)    = 'teamengland.import@maryland-camps.local';
DECLARE @Phone     nvarchar(50)     = '0000000000';
DECLARE @MomFirst  nvarchar(256)    = N'Team England';
DECLARE @MomLast   nvarchar(256)    = N'England';
DECLARE @DadFirst  nvarchar(256)    = N'Team England2';
DECLARE @DadLast   nvarchar(256)    = N'England';
DECLARE @City      nvarchar(128)    = N'London';
DECLARE @Country   nvarchar(128)    = N'United Kingdom';
DECLARE @ClubTeamName nvarchar(256) = N'England';  -- stamped on every registration (Registrations.ClubTeamName)
/* ===================== PASTE YOUR PLAYERS LOWER DOWN ======================= */

DECLARE @Audit     nvarchar(450)    = '71765055-647D-432E-AFB6-0F84218D0247'; -- SuperUserId (audit/leb)
DECLARE @Now       datetime2        = SYSDATETIME();

/* ---------------- validate inputs ---------------- */
IF NOT EXISTS (SELECT 1 FROM Jobs.Jobs WHERE JobId = @JobId)
    BEGIN RAISERROR('JobId not found.', 16, 1); RETURN; END;
IF NOT EXISTS (SELECT 1 FROM Leagues.teams WHERE teamID = @TeamId AND jobID = @JobId)
    BEGIN RAISERROR('TeamId not found on JobId (team must belong to the job).', 16, 1); RETURN; END;

/* ---------------- derive job-specific shape ---------------- */
DECLARE @AgegroupId uniqueidentifier = (SELECT agegroupID FROM Leagues.teams WHERE teamID = @TeamId);
DECLARE @Gender     nvarchar(10)     = (SELECT gender    FROM Leagues.teams WHERE teamID = @TeamId);
DECLARE @FeeBase    decimal(18,2)    = (SELECT TOP 1 Deposit + BalanceDue
                                        FROM fees.JobFees WHERE JobId = @JobId AND AgegroupId = @AgegroupId);

DECLARE @RoleId nvarchar(450), @RegCat nvarchar(256), @FormName nvarchar(256);
SELECT TOP 1 @RoleId = RoleId, @RegCat = RegistrationCategory, @FormName = registrationFormName
FROM Jobs.Registrations
WHERE jobID = @JobId AND bActive = 1 AND RoleId IS NOT NULL AND registrationFormName IS NOT NULL
  AND assigned_teamID IS NOT NULL AND RegistrationCategory LIKE 'Family Player Registration%'
GROUP BY RoleId, RegistrationCategory, registrationFormName
ORDER BY COUNT(*) DESC;   -- most common existing player-reg shape on this job

IF @Gender  IS NULL SET @Gender = N'F';
IF @FeeBase IS NULL BEGIN RAISERROR('No JobFee found for this team''s agegroup — set @FeeBase manually.', 16, 1); RETURN; END;
IF @RoleId  IS NULL BEGIN RAISERROR('No existing player registration on this job to template RoleId/Category/Form from — set them manually.', 16, 1); RETURN; END;

PRINT CONCAT('Derived  -> Gender=', @Gender, '  Fee=', @FeeBase, '  Form=', @FormName);
PRINT CONCAT('RegCat   -> ', @RegCat);

/* ======================= ROSTER — REPLACE WITH YOUR PLAYERS ================= */
/* names get trimmed via the data as pasted; use N'...' literals for accents.   */
DECLARE @players TABLE (
    ChildId   nvarchar(450) NOT NULL DEFAULT LOWER(CONVERT(varchar(36), NEWID())),  -- fresh id; overwritten below if the child already exists
    FirstName nvarchar(256) NOT NULL,
    LastName  nvarchar(256) NOT NULL,
    Dob       datetime2     NOT NULL,   -- 'YYYY-MM-DD'
    GradYear  nvarchar(10)  NOT NULL,
    Existed   bit           NOT NULL DEFAULT 0   -- set to 1 when matched to an existing child account
);
INSERT INTO @players (FirstName, LastName, Dob, GradYear) VALUES
 (N'Alice Sonia',            N'Trikha',          '2010-11-08', N'2029'),
 (N'Olivia Charlotte',       N'Craven',          '2011-03-22', N'2029'),
 (N'Juliana Lauren',         N'Shaw',            '2010-12-08', N'2029'),
 (N'Jamie Florence Victoria',N'Simons',          '2011-02-18', N'2029'),
 (N'Aurelia Rose',           N'Hurst-Clark',     '2010-09-05', N'2029'),
 (N'Charlotte Antoinette',   N'Allen',           '2010-11-01', N'2029'),
 (N'Jessica Lily',           N'Allen',           '2010-11-01', N'2029'),
 (N'Tara Amanda',            N'Vosloo',          '2010-09-01', N'2029'),
 (N'Lauren Zosia',           N'Gleich',          '2010-11-04', N'2029'),
 (N'Annabelle Kathryn',      N'Young',           '2010-09-01', N'2029'),
 (N'Enoki',                  N'Calvert-Ansari',  '2010-09-05', N'2029'),
 (N'Ela Ester Isabella',     N'Gilabert Foxon',  '2011-02-01', N'2029'),
 (N'Isabel Grace',           N'Perrett',         '2010-10-07', N'2029'),
 (N'Heidi Isobel Phoebe',    N'Saddleton',       '2010-11-21', N'2029'),
 (N'Emilia Rose',            N'Sanders',         '2010-12-26', N'2029'),
 (N'Emily Victoria',         N'Florence',        '2010-09-19', N'2029'),
 (N'Amber',                  N'Lu',              '2011-07-20', N'2029'),
 (N'Elodie Poppy Olive',     N'Jennings',        '2010-12-04', N'2029'),
 (N'Elisha Natalia',         N'Karim',           '2010-12-05', N'2029'),
 (N'Elizabeth Kate',         N'Thomas',          '2011-05-19', N'2029'),
 (N'Layla Anne',             N'Al-Falah',        '2010-05-08', N'2028'),
 (N'Charlotte Elizabeth',    N'Richardson',      '2009-12-24', N'2028'),
 (N'Flora Alice Isobel',     N'Turner',          '2009-09-19', N'2028'),
 (N'Amelia Grace',           N'Theodorou',       '2009-09-01', N'2028'),
 (N'Eleonore Ana Teresa',    N'Mighall',         '2009-10-13', N'2028'),
 (N'Zoë Emilie',             N'McIntyre',        '2010-03-12', N'2028'),
 (N'Sophie Maya',            N'Allan',           '2010-05-25', N'2028'),
 (N'Ella Cary',              N'Trowbridge',      '2009-09-21', N'2028'),
 (N'Emily Rebecca',          N'Percival',        '2009-05-11', N'2028'),
 (N'Eira Mair',              N'Hopkins',         '2009-12-20', N'2028'),
 (N'Alyona',                 N'Fetisova',        '2009-10-28', N'2028'),
 (N'Sadie Pearl',            N'Hyner',           '2010-02-23', N'2028'),
 (N'Olivia May',             N'Tufts',           '2008-12-31', N'2027'),
 (N'Erin Kayla',             N'Gregory',         '2008-12-31', N'2027'),
 (N'Anna Juliette',          N'Tiernan',         '2009-04-08', N'2027'),
 (N'Xiu-qi Vera',            N'Huang',           '2009-04-20', N'2027'),
 (N'Emilia Louise',          N'Morgan',          '2009-05-21', N'2027'),
 (N'Olivia Wong',            N'Kraus',           '2008-09-13', N'2027'),
 (N'Lucy May',               N'Robertshaw',      '2008-11-01', N'2027'),
 (N'Jemima Sarah',           N'Cetti',           '2009-10-03', N'2028');
/* ===================== END ROSTER ========================================== */

DECLARE @n int = (SELECT COUNT(*) FROM @players);
PRINT CONCAT('Players parsed: ', @n);
IF @n = 0 BEGIN RAISERROR('No players in @players. Aborting.', 16, 1); RETURN; END;

BEGIN TRAN;

/* idempotency :: point @players at child accounts that ALREADY exist under
   this family (matched on FirstName + LastName + DOB). Matched rows get the
   existing id + Existed=1, so the inserts below skip them and a re-run is a
   no-op. Unmatched rows keep their fresh NEWID() and are created. */
UPDATE p
   SET p.ChildId = u.Id, p.Existed = 1
FROM @players p
JOIN dbo.Family_Members fm ON fm.Family_UserId = @ParentId
JOIN dbo.AspNetUsers    u  ON u.Id = fm.Family_Member_UserId
WHERE u.FirstName = p.FirstName AND u.LastName = p.LastName AND u.dob = p.Dob;
PRINT CONCAT('Players already on file (reused): ', @@ROWCOUNT);

/* 1 + 2 :: family account holder + Families contact row ------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @ParentId)
INSERT INTO dbo.AspNetUsers
    (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
     PasswordHash, SecurityStamp, ConcurrencyStamp,
     PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount,
     FirstName, LastName, cellphone, phone, city, state, postalCode, country,
     lebUserID, modified, CreateDate)
VALUES
    (@ParentId, @AcctUser, UPPER(@AcctUser), @Email, UPPER(@Email), 0,
     NULL, CONVERT(varchar(36), NEWID()), CONVERT(varchar(36), NEWID()),
     @Phone, 0, 0, 1, 0,
     @MomFirst, @MomLast, @Phone, @Phone, @City, N'', N'00000', @Country,
     @Audit, @Now, @Now);

IF NOT EXISTS (SELECT 1 FROM dbo.Families WHERE Family_UserId = @ParentId)
INSERT INTO dbo.Families
    (Family_UserId, Mom_FirstName, Mom_LastName, Mom_Email, Mom_Cellphone,
     Dad_FirstName, Dad_LastName, Dad_Email, Dad_Cellphone, modified, lebUserID)
VALUES
    (@ParentId, @MomFirst, @MomLast, @Email, @Phone,
     @DadFirst, @DadLast, @Email, @Phone, @Now, @Audit);

/* 3 :: child player accounts --------------------------------------------- */
INSERT INTO dbo.AspNetUsers
    (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
     PasswordHash, SecurityStamp, ConcurrencyStamp,
     PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount,
     FirstName, LastName, gender, dob, lebUserID, modified, CreateDate)
SELECT
     p.ChildId, p.ChildId, UPPER(p.ChildId), @Email, NULL, 0,
     NULL, CONVERT(varchar(36), NEWID()), CONVERT(varchar(36), NEWID()),
     0, 0, 1, 0,
     p.FirstName, p.LastName, @Gender, p.Dob, @ParentId, @Now, @Now
FROM @players p WHERE p.Existed = 0;
PRINT CONCAT('New child accounts inserted: ', @@ROWCOUNT);

/* 4 :: link children to the family --------------------------------------- */
INSERT INTO dbo.Family_Members (Family_UserId, Family_Member_UserId, modified, lebUserID)
SELECT @ParentId, p.ChildId, @Now, @ParentId FROM @players p WHERE p.Existed = 0;

/* 5 :: registrations (active, fee comped to $0) -------------------------- */
INSERT INTO Jobs.Registrations
    (RegistrationID, RegistrationCategory, RegistrationTS, RoleId, UserId, Family_UserId,
     bActive, bConfirmationSent, jobID, lebUserID, modified, registrationFormName, assigned_teamID,
     ClubTeamName, fee_processing, fee_base, fee_discount, fee_discount_mp, fee_donation, fee_latefee,
     fee_total, owed_total, paid_total, grad_year)
SELECT
     NEWID(), @RegCat, @Now, @RoleId, p.ChildId, @ParentId,
     1, 1, @JobId, @Audit, @Now, @FormName, @TeamId,
     @ClubTeamName, 0, @FeeBase, @FeeBase, 0, 0, 0,   -- fee_discount = fee_base  => net $0
     0, 0, 0, p.GradYear
FROM @players p
WHERE NOT EXISTS (
    SELECT 1 FROM Jobs.Registrations r
    WHERE r.UserId = p.ChildId AND r.jobID = @JobId AND r.assigned_teamID = @TeamId);
PRINT CONCAT('New registrations inserted: ', @@ROWCOUNT);

/* 6 :: reconcile :: ensure ClubTeamName is stamped on this family's regs for
   the team (backfills rows created before ClubTeamName was added; keeps the
   re-run convergent rather than insert-only). */
UPDATE r SET r.ClubTeamName = @ClubTeamName, r.modified = @Now
FROM Jobs.Registrations r
WHERE r.Family_UserId = @ParentId AND r.assigned_teamID = @TeamId
  AND ISNULL(r.ClubTeamName, N'') <> @ClubTeamName;
PRINT CONCAT('ClubTeamName backfilled on existing regs: ', @@ROWCOUNT);

/* ----------------------- verification ----------------------------------- */
PRINT '--- VERIFICATION (inside txn) ---';
SELECT 'family_acct'  AS what, COUNT(*) AS cnt FROM dbo.AspNetUsers   WHERE Id = @ParentId
UNION ALL SELECT 'families',     COUNT(*) FROM dbo.Families            WHERE Family_UserId = @ParentId
UNION ALL SELECT 'child_accts',  COUNT(*) FROM dbo.Family_Members      WHERE Family_UserId = @ParentId
UNION ALL SELECT 'regs_on_team', COUNT(*) FROM Jobs.Registrations      WHERE Family_UserId = @ParentId AND assigned_teamID = @TeamId AND bActive = 1;

SELECT TOP 5 r.grad_year, u.FirstName, u.LastName, u.gender, u.dob, r.fee_base, r.fee_discount, r.owed_total
FROM Jobs.Registrations r JOIN dbo.AspNetUsers u ON u.Id = r.UserId
WHERE r.Family_UserId = @ParentId ORDER BY u.LastName;

COMMIT;
PRINT '*** COMMITTED ***';

/* ----------------------------------------------------------------------------
   CLEANUP (if ever needed) — undo a committed import for one @ParentId:
       BEGIN TRAN;
       DELETE FROM Jobs.Registrations WHERE Family_UserId = '<@ParentId>';
       DELETE FROM dbo.Family_Members WHERE Family_UserId = '<@ParentId>';
       DELETE FROM dbo.AspNetUsers WHERE Id IN
           (SELECT Family_Member_UserId FROM dbo.Family_Members WHERE Family_UserId='<@ParentId>'); -- run BEFORE the line above
       DELETE FROM dbo.Families WHERE Family_UserId = '<@ParentId>';
       DELETE FROM dbo.AspNetUsers WHERE Id = '<@ParentId>';
       -- COMMIT;  (verify first)
   ---------------------------------------------------------------------------- */
