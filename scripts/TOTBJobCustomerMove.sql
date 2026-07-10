-- =====================================================================
-- TOTB — Move Jobs from "Top of the Bay Lacrosse" to "Signature Sports"
-- =====================================================================
-- Author-note for review (Chelsea): please read this header before the
-- body. The script is a data migration, not a schema change. It runs as
-- a DRY-RUN by default and changes NOTHING until you deliberately flip a
-- flag (see "HOW TO RUN"). Row counts are printed for every step.
--
-- ---------------------------------------------------------------------
-- WHAT THIS DOES
-- ---------------------------------------------------------------------
-- Reassigns 7 named jobs (listed in @strJobIds below) from the existing
-- customer "Top of the Bay Lacrosse" to a NEW customer "Signature
-- Sports", and cleans up every place the old customer's identity is
-- carried on those jobs. Concretely:
--
--   (1) Create customer "Signature Sports" in [Jobs].[Customers].
--       - New customerID is minted (or an existing "Signature Sports"
--         row is reused — creation is guarded, never duplicated).
--       - theme / lebUserID / bAllowAmex / ADN credentials are COPIED
--         from Top of the Bay. TZ_ID is set to 35 (obsolete but NOT
--         NULL column).
--   (2) For each of the 7 jobs, in [Jobs].[Jobs]:
--         customerID     -> new customer
--         jobName        -> "Top of the Bay Lacrosse" replaced with
--                           "Signature Sports"
--         jobDescription -> same name replacement as jobName
--         jobPath        -> slug token "topofthebay" -> "signaturesports"
--         DisplayName    -> "Signature Sports"
--   (3) Add "Signature Sports" to customer group #3 (guarded — no dup).
--   (4) Rename customer group #3 to "Signature Sports".
--   (5) Rename each moved job's league(s): leagueName becomes the new
--       jobName with the ':' separator turned into a space, e.g.
--       "Signature Sports:Youth Nationals 2027"
--         -> "Signature Sports Youth Nationals 2027".
--
-- ---------------------------------------------------------------------
-- SAFETY MODEL
-- ---------------------------------------------------------------------
--   * DRY-RUN by default: @commit = 0 wraps all writes in a transaction
--     that is ROLLED BACK. You see the row counts with zero risk.
--   * SET XACT_ABORT ON: any error aborts and rolls back the whole batch.
--   * Preflight aborts BEFORE any write if the old customer or group is
--     missing, if no jobIds parse, or if ANY supplied jobId is missing
--     or not currently owned by Top of the Bay.
--   * Every creation is idempotent (guarded by pre-existence), so a
--     second run does not duplicate rows.
--
-- ---------------------------------------------------------------------
-- REVIEWER DECISIONS — please confirm before we set @commit = 1
-- ---------------------------------------------------------------------
--   A) ADN MERCHANT CREDENTIALS (money): the new customer currently
--      inherits Top of the Bay's adnLoginID / adnTransactionKey, so the
--      moved jobs keep settling into the SAME merchant account. If
--      Signature Sports has its own gateway, override @newAdnLoginId /
--      @newAdnTxnKey (or NULL them) before committing.
--   B) DENORMALIZED customerID ON CHILD ROWS: Teams.customerID and
--      Registrations.customerID for these jobs still reference the OLD
--      customer, as does the [Jobs].[JobCustomers] join. This script
--      does NOT touch them yet — decide whether they must follow.
--   C) TRIGGER: updating [Jobs].[Jobs] fires Job_AfterEdit_TeamFees.
--      Expected, but noting it since it recomputes team fees.
--
-- ---------------------------------------------------------------------
-- HOW TO RUN
-- ---------------------------------------------------------------------
--   1. Run as-is (dry-run). Read the printed "(1)..(5)" row counts and
--      confirm they match expectations (e.g. "(2) ... : 7").
--   2. Resolve decisions A and B above.
--   3. Set @commit = 1 and re-run to persist.
--
-- Tables touched all live in the [Jobs] schema except leagues, which
-- are in [Leagues].[Leagues] (join via [jobs].[Job_Leagues]).
-- =====================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ---------------------------------------------------------------------
-- Input parameters
-- ---------------------------------------------------------------------
DECLARE @oldCustomerGroupId int             = 3;                                        -- Top of the Bay Lacrosse
DECLARE @oldCustomerId      uniqueidentifier = '4FF492DD-C51B-4F7B-BC93-4C6545D309A2';  -- Top of the Bay Lacrosse
DECLARE @newCustomerName    nvarchar(450)    = N'Signature Sports';
DECLARE @oldPathToken       nvarchar(100)    = N'topofthebay';      -- jobPath slug token to replace
DECLARE @newPathToken       nvarchar(100)    = N'signaturesports';
-- semicolon-delimited, single-quoted jobIds
DECLARE @strJobIds          varchar(max)     =
    '''D7164C35-86A5-46D0-B9F7-16D35D73B23C'';' + -- Youth Nationals 2027
    '''8528AAA5-A940-46BD-A935-A060F9F6054B'';' + -- Laxin Out Loud 2027
    '''3CEF9DA9-4B81-4F9F-AA01-C110401F6E89'';' + -- #LaxisLife 2027
    '''18F35897-E59C-407E-BCF3-C40DFF7D78F5'';' + -- Rock The Fields 2027
    '''4794B647-843F-45A0-AAED-48DA0765A47E'';' + -- Lax Clash 2026
    '''BB2BDF9C-6965-486F-A2AF-7B6E47A0DC87'';' + -- Fall Premier Showcase 2026
    '''1624FE4B-8AFB-46B3-AF7C-B3DDAAB0AACE''';   -- The Draft 2026

DECLARE @commit             bit              = 0;    -- 0 = dry-run (rollback), 1 = persist

-- ---------------------------------------------------------------------
-- New customer's identity + attributes
--   customerID default is newsequentialid(); we mint it up front so we
--   can reference it in the jobs UPDATE and the group-customer INSERT.
--   customerAI is an identity column and is intentionally omitted.
--
--   !! DECISION: ADN merchant gateway credentials !!
--   These default to Top of the Bay's values, meaning the moved jobs
--   would keep charging through the SAME merchant account. If Signature
--   Sports has its OWN ADN account, override the two vars below (or set
--   them NULL) BEFORE running. This governs where real money settles.
-- ---------------------------------------------------------------------
--   Reuse an existing "Signature Sports" customer if one already exists
--   (guard against duplicate creation); otherwise mint a fresh id.
DECLARE @newCustomerId    uniqueidentifier;
DECLARE @customerExisted  bit = 0;

SELECT  @newCustomerId = customerID
FROM    [Jobs].[Customers]
WHERE   customerName = @newCustomerName;

IF @newCustomerId IS NULL
    SET @newCustomerId = NEWID();        -- new customer
ELSE
    SET @customerExisted = 1;            -- reuse the saved pre-existing id

DECLARE @tzId             int = 35,   -- TZ_ID is obsolete but NOT NULL; hardcode a valid value
        @theme            nvarchar(450),
        @lebUserId        nvarchar(450),
        @bAllowAmex       bit,
        @newAdnLoginId    varchar(100),
        @newAdnTxnKey     varchar(100),
        @oldCustomerName  nvarchar(450);   -- used to rewrite the jobName prefix

SELECT  @theme          = c.theme,
        @lebUserId      = c.lebUserID,
        @bAllowAmex     = c.bAllowAmex,
        @newAdnLoginId  = c.adnLoginID,       -- <-- copies Top of the Bay merchant login; review
        @newAdnTxnKey   = c.adnTransactionKey,-- <-- copies Top of the Bay merchant key;   review
        @oldCustomerName = c.customerName
FROM    [Jobs].[Customers] c
WHERE   c.customerID = @oldCustomerId;

-- =====================================================================
-- Parse @strJobIds → @JobIds table
--   Input is single-quoted + semicolon-delimited; strip quotes/blanks.
-- =====================================================================
DECLARE @JobIds TABLE (JobId uniqueidentifier PRIMARY KEY);

INSERT INTO @JobIds (JobId)
SELECT DISTINCT TRY_CONVERT(uniqueidentifier, REPLACE(REPLACE(LTRIM(RTRIM(value)), '''', ''), '"', ''))
FROM   STRING_SPLIT(@strJobIds, ';')
WHERE  LEN(LTRIM(RTRIM(value))) > 0
  AND  TRY_CONVERT(uniqueidentifier, REPLACE(REPLACE(LTRIM(RTRIM(value)), '''', ''), '"', '')) IS NOT NULL;

-- =====================================================================
-- Preflight validation (no writes)
-- =====================================================================
DECLARE @err nvarchar(max) = NULL;

IF NOT EXISTS (SELECT 1 FROM [Jobs].[Customers]      WHERE customerID = @oldCustomerId)
    SET @err = CONCAT(ISNULL(@err + N' | ', N''), N'Old customer not found: ', CONVERT(nvarchar(50), @oldCustomerId));

IF NOT EXISTS (SELECT 1 FROM [Jobs].[CustomerGroups] WHERE Id = @oldCustomerGroupId)
    SET @err = CONCAT(ISNULL(@err + N' | ', N''), N'Customer group not found: ', @oldCustomerGroupId);

IF NOT EXISTS (SELECT 1 FROM @JobIds)
    SET @err = CONCAT(ISNULL(@err + N' | ', N''), N'No valid jobIds parsed from @strJobIds.');

-- every supplied jobId must exist AND currently belong to @oldCustomerId
DECLARE @badJobs nvarchar(max);
SELECT @badJobs = STRING_AGG(CONVERT(nvarchar(50), j.JobId), N', ')
FROM   @JobIds j
LEFT   JOIN [Jobs].[Jobs] jb ON jb.jobID = j.JobId
WHERE  jb.jobID IS NULL OR jb.customerID <> @oldCustomerId;

IF @badJobs IS NOT NULL
    SET @err = CONCAT(ISNULL(@err + N' | ', N''),
                      N'These jobIds are missing or not owned by the old customer: ', @badJobs);

IF @err IS NOT NULL
BEGIN
    RAISERROR('Preflight failed: %s', 16, 1, @err);
    RETURN;
END

DECLARE @jobCount int = (SELECT COUNT(*) FROM @JobIds);
PRINT CONCAT('Preflight OK. Jobs to move: ', @jobCount,
             '  |  New customerID: ', CONVERT(nvarchar(50), @newCustomerId));

-- =====================================================================
-- Mutations (transactional; rolls back unless @commit = 1)
-- =====================================================================
BEGIN TRAN;

-- (1) Create the new customer (guarded against pre-existence) ---------
IF @customerExisted = 0
BEGIN
    INSERT INTO [Jobs].[Customers]
            (customerID,     customerName,     TZ_ID, theme, lebUserID, bAllowAmex, adnLoginID,     adnTransactionKey, modified)
    VALUES  (@newCustomerId, @newCustomerName, @tzId, @theme, @lebUserId, @bAllowAmex, @newAdnLoginId, @newAdnTxnKey,     GETDATE());
    PRINT CONCAT('(1) Inserted customer rows: ', @@ROWCOUNT);
END
ELSE
    PRINT CONCAT('(1) Customer already existed; reusing saved customerID ', CONVERT(nvarchar(50), @newCustomerId), '; skipped insert.');

-- (2) Repoint the jobs + rewrite jobName prefix -----------------------
--     (fires Job_AfterEdit_TeamFees). jobName carries the old customer
--     as a prefix, e.g. "Top of the Bay Lacrosse:Youth Nationals 2027".
UPDATE  jb
SET     jb.customerID     = @newCustomerId,
        jb.jobName        = REPLACE(jb.jobName,        @oldCustomerName, @newCustomerName),
        jb.jobDescription = REPLACE(jb.jobDescription, @oldCustomerName, @newCustomerName),
        jb.jobPath        = REPLACE(jb.jobPath, @oldPathToken,    @newPathToken),
        jb.DisplayName    = @newCustomerName
FROM    [Jobs].[Jobs] jb
JOIN    @JobIds j ON j.JobId = jb.jobID
WHERE   jb.customerID = @oldCustomerId;   -- idempotency guard
PRINT CONCAT('(2) Jobs repointed + renamed: ', @@ROWCOUNT);

-- (3) Add new customer to the customer group --------------------------
IF NOT EXISTS (SELECT 1 FROM [Jobs].[CustomerGroupCustomers]
               WHERE CustomerGroupId = @oldCustomerGroupId AND CustomerId = @newCustomerId)
BEGIN
    INSERT INTO [Jobs].[CustomerGroupCustomers] (CustomerGroupId, CustomerId)
    VALUES (@oldCustomerGroupId, @newCustomerId);
    PRINT CONCAT('(3) CustomerGroupCustomers rows added: ', @@ROWCOUNT);
END
ELSE
    PRINT '(3) CustomerGroupCustomers row already present; skipped.';

-- (4) Rename the customer group ---------------------------------------
UPDATE  [Jobs].[CustomerGroups]
SET     CustomerGroupName = @newCustomerName
WHERE   Id = @oldCustomerGroupId;
PRINT CONCAT('(4) CustomerGroups renamed: ', @@ROWCOUNT);

-- (5) Rename each moved job's league(s) -------------------------------
--     leagueName := the (already-renamed) jobName with ':' -> ' '
--     e.g. "Signature Sports:Youth Nationals 2027"
--        -> "Signature Sports Youth Nationals 2027"
UPDATE  l
SET     l.leagueName = REPLACE(jb.jobName, ':', ' ')
FROM    [Leagues].[Leagues] l
JOIN    [jobs].[Job_Leagues] jl ON jl.leagueId = l.leagueId
JOIN    [Jobs].[Jobs]        jb ON jb.jobID   = jl.jobId
JOIN    @JobIds              j  ON j.JobId     = jb.jobID;
PRINT CONCAT('(5) Leagues renamed: ', @@ROWCOUNT);

-- ---------------------------------------------------------------------
-- Commit / rollback
-- ---------------------------------------------------------------------
IF @commit = 1
BEGIN
    COMMIT TRAN;
    PRINT '=== COMMITTED ===';
END
ELSE
BEGIN
    ROLLBACK TRAN;
    PRINT '=== DRY-RUN: rolled back. Set @commit = 1 to persist. ===';
END

-- =====================================================================
-- OPEN QUESTIONS (not handled above — confirm before committing):
--   * Teams.customerID and Registrations.customerID for these jobs
--     still point at the OLD customer. Move them too?
--   * [Jobs].[JobCustomers] join rows (customerID/jobID) — repoint?
--   * Txs.customerID is a string column on a money table — untouched.
-- =====================================================================
