-- =====================================================================
-- eCheck Settlement Tracking — Schema DDL
-- =====================================================================
-- Adds: 1 new schema, 2 new tables, 2 indexes
-- Risk: ZERO to existing schema (additive only, no alters / no backfills)
-- Idempotent: every object guarded individually. Safe to re-run.
-- =====================================================================
--
-- Payment method rows are NOT seeded here — they already exist in
-- reference.Accounting_PaymentMethods (canonical 2012 seed):
--   2EECA575-A268-E111-9D56-F04DA202060D  "E-Check Payment"
--   2FECA575-A268-E111-9D56-F04DA202060D  "Failed E-Check Payment"
--
-- TWO MODES:
--   1. Fresh install — just run the whole script.
--   2. Redeploy after schema change — uncomment the RESET block below
--      to drop existing eCheck objects, then run the rest.
--
-- Re-scaffold EF entities after running:
--   dotnet ef dbcontext scaffold ... (your usual command)
-- =====================================================================


-- =====================================================================
-- RESET (uncomment to drop existing eCheck objects before recreating)
-- =====================================================================
-- IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('echeck') AND name = 'Settlement')
--     DROP TABLE echeck.Settlement;
-- IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('echeck') AND name = 'SweepLog')
--     DROP TABLE echeck.SweepLog;
-- IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'echeck')
--     DROP SCHEMA echeck;
-- GO


-- ---------------------------------------------------------------------
-- 1. Schema: echeck
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'echeck')
BEGIN
    EXEC('CREATE SCHEMA echeck AUTHORIZATION dbo');
END
GO


-- ---------------------------------------------------------------------
-- 2. echeck.Settlement
--    One row per eCheck submission. Tracks lifecycle from submitted
--    through settled or returned.
--    1:1 with the paired Jobs.Registration_Accounting row
--      (FK column: registrationAccountingId → aID).
--    Column named registrationAccountingId (not aID) so EF's scaffold
--    convention generates a clean .RegistrationAccounting navigation
--    instead of .AIdNavigation.
--    The RA row is NEVER mutated for status — sweep updates this row only.
--    NSF reversals create a NEW negative RA row (Correction-style),
--    which has its own aID and gets NO Settlement row.
-- ---------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE schema_id = SCHEMA_ID('echeck') AND name = 'Settlement'
)
BEGIN
    CREATE TABLE echeck.Settlement
    (
        settlementID                UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_echeck_Settlement_id DEFAULT (NEWSEQUENTIALID()),
        registrationAccountingId    INT              NOT NULL,
        adnTransactionID            NVARCHAR(50)     NOT NULL,
        status                      NVARCHAR(20)     NOT NULL
            CONSTRAINT DF_echeck_Settlement_status DEFAULT ('Pending'),
        submittedAt                 DATETIME2(0)     NOT NULL
            CONSTRAINT DF_echeck_Settlement_submittedAt DEFAULT (SYSUTCDATETIME()),
        nextCheckAt                 DATETIME2(0)     NOT NULL,
        lastCheckedAt               DATETIME2(0)     NULL,
        settledAt                   DATETIME2(0)     NULL,
        returnReasonCode            NVARCHAR(50)     NULL,
        returnReasonText            NVARCHAR(500)    NULL,
        accountLast4                NVARCHAR(4)      NULL,
        accountType                 NVARCHAR(20)     NULL,
        nameOnAccount               NVARCHAR(100)    NULL,
        modified                    DATETIME2(0)     NOT NULL
            CONSTRAINT DF_echeck_Settlement_modified DEFAULT (SYSUTCDATETIME()),
        lebUserID                   NVARCHAR(450)    NULL,

        CONSTRAINT PK_echeck_Settlement       PRIMARY KEY CLUSTERED (settlementID),
        CONSTRAINT UQ_echeck_Settlement_raID  UNIQUE (registrationAccountingId),
        CONSTRAINT UQ_echeck_Settlement_txID  UNIQUE (adnTransactionID),
        CONSTRAINT FK_echeck_Settlement_RA    FOREIGN KEY (registrationAccountingId)
            REFERENCES Jobs.Registration_Accounting(aID),
        CONSTRAINT CK_echeck_Settlement_status
            CHECK (status IN ('Pending', 'Settled', 'Returned', 'Voided')),
        CONSTRAINT CK_echeck_Settlement_acctType
            CHECK (accountType IS NULL OR accountType IN ('checking', 'savings', 'businessChecking'))
    );
END
GO

-- Sweep's primary query: pending records due for re-check
-- Guarded separately so a manually-created table still gets the index
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_echeck_Settlement_status_nextCheck'
      AND object_id = OBJECT_ID('echeck.Settlement')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_echeck_Settlement_status_nextCheck
        ON echeck.Settlement (status, nextCheckAt)
        INCLUDE (registrationAccountingId, adnTransactionID);
END
GO


-- ---------------------------------------------------------------------
-- 3. echeck.SweepLog
--    One row per sweep run (ops + audit). Lets us see when sweeps ran,
--    what they did, and whether anything errored.
-- ---------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE schema_id = SCHEMA_ID('echeck') AND name = 'SweepLog'
)
BEGIN
    CREATE TABLE echeck.SweepLog
    (
        sweepLogID         BIGINT           NOT NULL IDENTITY(1,1),
        startedAt          DATETIME2(0)     NOT NULL
            CONSTRAINT DF_echeck_SweepLog_startedAt DEFAULT (SYSUTCDATETIME()),
        completedAt        DATETIME2(0)     NULL,
        triggeredBy        NVARCHAR(20)     NOT NULL,
        recordsChecked     INT              NOT NULL
            CONSTRAINT DF_echeck_SweepLog_checked DEFAULT (0),
        recordsSettled     INT              NOT NULL
            CONSTRAINT DF_echeck_SweepLog_settled DEFAULT (0),
        recordsReturned    INT              NOT NULL
            CONSTRAINT DF_echeck_SweepLog_returned DEFAULT (0),
        recordsErrored     INT              NOT NULL
            CONSTRAINT DF_echeck_SweepLog_errored DEFAULT (0),
        errorMessage       NVARCHAR(MAX)    NULL,

        CONSTRAINT PK_echeck_SweepLog PRIMARY KEY CLUSTERED (sweepLogID),
        CONSTRAINT CK_echeck_SweepLog_triggeredBy
            CHECK (triggeredBy IN ('Scheduled', 'Manual'))
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_echeck_SweepLog_startedAt'
      AND object_id = OBJECT_ID('echeck.SweepLog')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_echeck_SweepLog_startedAt
        ON echeck.SweepLog (startedAt DESC);
END
GO


-- ---------------------------------------------------------------------
-- Verification (run manually after script completes)
-- ---------------------------------------------------------------------
-- SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('echeck');
--
-- SELECT name FROM sys.indexes
--   WHERE object_id IN (OBJECT_ID('echeck.Settlement'), OBJECT_ID('echeck.SweepLog'))
--   AND name IS NOT NULL;


-- =====================================================================
-- DESIGN NOTES
-- =====================================================================
-- FK column named registrationAccountingId (not aID):
--   EF Core scaffolder uses the FK column name to derive the navigation
--   property name. With column = 'aID', it generates .AIdNavigation
--   (ugly) because 'AId' clashes with another scalar. Renaming to
--   registrationAccountingId yields a clean .RegistrationAccounting nav.
--
-- Status enum as NVARCHAR(20) + CHECK constraint (vs int + lookup):
--   readable in queries, no JOIN noise, easy to add states later.
--
-- UQ on registrationAccountingId:
--   1:1 with RA row. NSF reversals create a NEW negative RA row
--   (different aID), no Settlement row for the reversal.
-- UQ on adnTransactionID:
--   prevents duplicate Settlement rows for the same ADN transaction
--   (sweep idempotency safety net).
--
-- accountType allows 'businessChecking' — ADN supports it.
--
-- Timestamps use SYSUTCDATETIME() (UTC). Existing schema uses local
-- getdate(); new schema = clean UTC start.
--
-- No paymentMethodID column on Settlement — the RA row already carries
-- it (always "E-Check Payment" 2EECA575-...). Avoids redundancy.
-- NSF reversal RA rows use "Failed E-Check Payment" 2FECA575-...
-- =====================================================================
