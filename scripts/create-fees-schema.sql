-- ============================================================
-- Fee Schema: fees.JobFees + fees.FeeModifiers
-- Idempotent: safe to run multiple times
--
-- After running: re-scaffold EF entities from database
-- ============================================================

-- Schema
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'fees')
    EXEC('CREATE SCHEMA fees');
GO

-- ============================================================
-- fees.JobFees
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'fees' AND t.name = 'JobFees')
BEGIN
    CREATE TABLE fees.JobFees (
        JobFeeId        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        JobId           UNIQUEIDENTIFIER NOT NULL,
        RoleId          NVARCHAR(450)    NOT NULL,
        AgegroupId      UNIQUEIDENTIFIER NULL,
        TeamId          UNIQUEIDENTIFIER NULL,
        Deposit         DECIMAL(18,2)    NULL,
        BalanceDue      DECIMAL(18,2)    NULL,
        Modified        DATETIME         NOT NULL DEFAULT GETUTCDATE(),
        LebUserId       NVARCHAR(450)    NULL,

        CONSTRAINT PK_JobFees PRIMARY KEY (JobFeeId),

        CONSTRAINT FK_JobFees_Jobs
            FOREIGN KEY (JobId) REFERENCES Jobs.Jobs(JobId),
        CONSTRAINT FK_JobFees_Agegroups
            FOREIGN KEY (AgegroupId) REFERENCES Leagues.agegroups(AgegroupId),
        CONSTRAINT FK_JobFees_Teams
            FOREIGN KEY (TeamId) REFERENCES Leagues.teams(TeamId),
        CONSTRAINT FK_JobFees_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );

    -- Unique scope: one row per (Job, Role, Agegroup, Team) combination.
    -- SQL Server treats NULLs as equal in unique indexes, so:
    --   (JobId, 'Player', NULL, NULL) can only exist once = job-wide default
    --   (JobId, 'Player', AgX, NULL)  can only exist once = agegroup default
    --   (JobId, 'Player', AgX, TmY)  can only exist once = team override
    CREATE UNIQUE INDEX UX_JobFees_Scope
        ON fees.JobFees (JobId, RoleId, AgegroupId, TeamId);

    -- Query patterns: "all fees for a job", "fees for an agegroup", "fees for a team"
    CREATE INDEX IX_JobFees_JobId ON fees.JobFees (JobId);
    CREATE INDEX IX_JobFees_AgegroupId ON fees.JobFees (AgegroupId) WHERE AgegroupId IS NOT NULL;
    CREATE INDEX IX_JobFees_TeamId ON fees.JobFees (TeamId) WHERE TeamId IS NOT NULL;

    PRINT 'Created fees.JobFees';
END
ELSE
    PRINT 'fees.JobFees already exists — skipped';
GO

-- ============================================================
-- fees.FeeModifiers
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'fees' AND t.name = 'FeeModifiers')
BEGIN
    CREATE TABLE fees.FeeModifiers (
        FeeModifierId   UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        JobFeeId        UNIQUEIDENTIFIER NOT NULL,
        ModifierType    NVARCHAR(50)     NOT NULL,
        Amount          DECIMAL(18,2)    NOT NULL,
        StartDate       DATETIME2        NULL,
        EndDate         DATETIME2        NULL,
        Modified        DATETIME         NOT NULL DEFAULT GETUTCDATE(),
        LebUserId       NVARCHAR(450)    NULL,

        CONSTRAINT PK_FeeModifiers PRIMARY KEY (FeeModifierId),

        CONSTRAINT FK_FeeModifiers_JobFees
            FOREIGN KEY (JobFeeId) REFERENCES fees.JobFees(JobFeeId)
            ON DELETE CASCADE,
        CONSTRAINT FK_FeeModifiers_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );

    CREATE INDEX IX_FeeModifiers_JobFeeId ON fees.FeeModifiers (JobFeeId);

    PRINT 'Created fees.FeeModifiers';
END
ELSE
    PRINT 'fees.FeeModifiers already exists — skipped';
GO

-- ============================================================
-- Verification
-- ============================================================
SELECT
    s.name AS [Schema],
    t.name AS [Table],
    (SELECT COUNT(*) FROM sys.columns c WHERE c.object_id = t.object_id) AS [Columns]
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE s.name = 'fees'
ORDER BY t.name;
GO
