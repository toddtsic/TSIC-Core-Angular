-- ============================================================
-- brackets schema — strategy-driven championship bracketing
-- (single-elim now; double-elim & others later as DATA, not code)
--
-- DDL ONLY. No seed data here (SE template rows are a follow-up
-- script once template/route values are defined).
--
-- Conventions:
--   * Idempotent: safe to run multiple times.
--   * Audit fields on every table: Modified + LebUserId.
--   * NO ON DELETE/UPDATE CASCADE anywhere — default NO ACTION
--     (project rule; deletes fail loud rather than ripple).
--   * NO triggers.
--
-- After running: re-scaffold EF entities from database
--   (scripts\3) RE-Scaffold-Db-Entities.ps1)
--
-- Model: every bracket-game slot (Leagues.schedule T1*/T2*) is
-- filled by EXACTLY ONE of:
--   * a SeedAssignment  (slot <- standings rank), or
--   * an AdvancementFeed (slot <- winner/loser of another game).
-- That XOR is enforced in the application layer (cannot be a
-- single DB constraint across two tables); within each table a
-- UNIQUE (target, slot) prevents double-filling from that source.
-- ============================================================

-- Schema
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'brackets')
    EXEC('CREATE SCHEMA brackets');
GO

-- ============================================================
-- brackets.Strategies  — bracketing strategy catalog
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'brackets' AND t.name = 'Strategies')
BEGIN
    CREATE TABLE brackets.Strategies (
        StrategyId      INT              NOT NULL IDENTITY(1,1),
        Code            NVARCHAR(10)     NOT NULL,   -- 'SE','DE',...
        Name            NVARCHAR(100)    NOT NULL,
        Description     NVARCHAR(500)    NULL,
        IsActive        BIT              NOT NULL DEFAULT 1,
        Modified        DATETIME         NOT NULL DEFAULT GETDATE(),
        LebUserId       NVARCHAR(450)    NULL,

        CONSTRAINT PK_brackets_Strategies PRIMARY KEY (StrategyId),
        CONSTRAINT UX_brackets_Strategies_Code UNIQUE (Code),
        CONSTRAINT FK_brackets_Strategies_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );
    PRINT 'Created brackets.Strategies';
END
ELSE PRINT 'brackets.Strategies already exists — skipped';
GO

-- ============================================================
-- brackets.Templates  — a strategy at a given bracket size
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'brackets' AND t.name = 'Templates')
BEGIN
    CREATE TABLE brackets.Templates (
        TemplateId      INT              NOT NULL IDENTITY(1,1),
        StrategyId      INT              NOT NULL,
        BracketSize     INT              NOT NULL,   -- 2,4,8,16,32,64
        Variant         NVARCHAR(20)     NOT NULL    -- 'Standard','3rdPlace',...
            CONSTRAINT DF_brackets_Templates_Variant DEFAULT 'Standard',
        Name            NVARCHAR(100)    NULL,
        Modified        DATETIME         NOT NULL DEFAULT GETDATE(),
        LebUserId       NVARCHAR(450)    NULL,

        CONSTRAINT PK_brackets_Templates PRIMARY KEY (TemplateId),
        CONSTRAINT UX_brackets_Templates_Strategy_Size_Variant
            UNIQUE (StrategyId, BracketSize, Variant),
        CONSTRAINT CK_brackets_Templates_Size
            CHECK (BracketSize BETWEEN 2 AND 256),
        CONSTRAINT FK_brackets_Templates_Strategy
            FOREIGN KEY (StrategyId) REFERENCES brackets.Strategies(StrategyId),
        CONSTRAINT FK_brackets_Templates_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );
    PRINT 'Created brackets.Templates';
END
ELSE PRINT 'brackets.Templates already exists — skipped';
GO

-- --- Evolve brackets.Templates to include Variant (idempotent self-heal) ---
-- For DBs created before Variant existed: add the column and swap the
-- size-only unique key for the (Strategy, Size, Variant) composite.
IF COL_LENGTH('brackets.Templates', 'Variant') IS NULL
    ALTER TABLE brackets.Templates
        ADD Variant NVARCHAR(20) NOT NULL
            CONSTRAINT DF_brackets_Templates_Variant DEFAULT 'Standard';
GO
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'UX_brackets_Templates_Strategy_Size'
             AND parent_object_id = OBJECT_ID('brackets.Templates'))
    ALTER TABLE brackets.Templates DROP CONSTRAINT UX_brackets_Templates_Strategy_Size;
GO
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
               WHERE name = 'UX_brackets_Templates_Strategy_Size_Variant'
                 AND parent_object_id = OBJECT_ID('brackets.Templates'))
    ALTER TABLE brackets.Templates
        ADD CONSTRAINT UX_brackets_Templates_Strategy_Size_Variant
            UNIQUE (StrategyId, BracketSize, Variant);
GO

-- ============================================================
-- brackets.TemplateGames  — abstract games within a template
--   (supersedes reference.BracketDataSingleElimination)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'brackets' AND t.name = 'TemplateGames')
BEGIN
    CREATE TABLE brackets.TemplateGames (
        TemplateGameId  INT              NOT NULL IDENTITY(1,1),
        TemplateId      INT              NOT NULL,
        RoundType       NVARCHAR(4)      NOT NULL,   -- F,S,Q,X,Y,Z,C,B (+LB* future)
        GameKey         INT              NOT NULL,   -- canonical game id within round
        Slot1Seed       INT              NULL,       -- leaf seed position (else NULL)
        Slot2Seed       INT              NULL,
        SortOrder       INT              NOT NULL DEFAULT 0,
        IsOptional      BIT              NOT NULL DEFAULT 0,  -- e.g. 'B' bronze: auto-placed, schedulable-away
        Modified        DATETIME         NOT NULL DEFAULT GETDATE(),
        LebUserId       NVARCHAR(450)    NULL,

        CONSTRAINT PK_brackets_TemplateGames PRIMARY KEY (TemplateGameId),
        CONSTRAINT UX_brackets_TemplateGames_Key
            UNIQUE (TemplateId, RoundType, GameKey),
        CONSTRAINT FK_brackets_TemplateGames_Template
            FOREIGN KEY (TemplateId) REFERENCES brackets.Templates(TemplateId),
        CONSTRAINT FK_brackets_TemplateGames_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );
    PRINT 'Created brackets.TemplateGames';
END
ELSE PRINT 'brackets.TemplateGames already exists — skipped';
GO

-- --- Evolve brackets.TemplateGames to include IsOptional (idempotent self-heal) ---
IF COL_LENGTH('brackets.TemplateGames', 'IsOptional') IS NULL
    ALTER TABLE brackets.TemplateGames
        ADD IsOptional BIT NOT NULL
            CONSTRAINT DF_brackets_TemplateGames_IsOptional DEFAULT 0;
GO

-- ============================================================
-- brackets.AdvancementRoutes  — abstract winner/loser routing
--   SE = Winner-only rows; DE adds Loser rows + LB round types.
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'brackets' AND t.name = 'AdvancementRoutes')
BEGIN
    CREATE TABLE brackets.AdvancementRoutes (
        AdvancementRouteId   INT         NOT NULL IDENTITY(1,1),
        SourceTemplateGameId INT         NOT NULL,
        SourceResult         NVARCHAR(6) NOT NULL,   -- 'Winner' | 'Loser'
        TargetTemplateGameId INT         NOT NULL,
        TargetSlot           TINYINT     NOT NULL,   -- 1 | 2
        Modified             DATETIME    NOT NULL DEFAULT GETDATE(),
        LebUserId            NVARCHAR(450) NULL,

        CONSTRAINT PK_brackets_AdvancementRoutes PRIMARY KEY (AdvancementRouteId),
        -- each source result routes to exactly one place
        CONSTRAINT UX_brackets_AdvRoutes_Source
            UNIQUE (SourceTemplateGameId, SourceResult),
        -- each target slot is fed by at most one route
        CONSTRAINT UX_brackets_AdvRoutes_Target
            UNIQUE (TargetTemplateGameId, TargetSlot),
        CONSTRAINT CK_brackets_AdvRoutes_Result
            CHECK (SourceResult IN ('Winner','Loser')),
        CONSTRAINT CK_brackets_AdvRoutes_Slot
            CHECK (TargetSlot IN (1,2)),
        CONSTRAINT FK_brackets_AdvRoutes_Source
            FOREIGN KEY (SourceTemplateGameId) REFERENCES brackets.TemplateGames(TemplateGameId),
        CONSTRAINT FK_brackets_AdvRoutes_Target
            FOREIGN KEY (TargetTemplateGameId) REFERENCES brackets.TemplateGames(TemplateGameId),
        CONSTRAINT FK_brackets_AdvRoutes_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );
    PRINT 'Created brackets.AdvancementRoutes';
END
ELSE PRINT 'brackets.AdvancementRoutes already exists — skipped';
GO

-- ============================================================
-- brackets.BracketInstances  — a concrete bracket for a
--   job/agegroup(/division), recording its strategy template.
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'brackets' AND t.name = 'BracketInstances')
BEGIN
    CREATE TABLE brackets.BracketInstances (
        BracketInstanceId INT            NOT NULL IDENTITY(1,1),
        JobId            UNIQUEIDENTIFIER NOT NULL,
        AgegroupId       UNIQUEIDENTIFIER NOT NULL,
        DivId            UNIQUEIDENTIFIER NULL,     -- NULL = spans pools
        TemplateId       INT              NOT NULL,
        Modified         DATETIME         NOT NULL DEFAULT GETDATE(),
        LebUserId        NVARCHAR(450)    NULL,

        CONSTRAINT PK_brackets_BracketInstances PRIMARY KEY (BracketInstanceId),
        CONSTRAINT FK_brackets_BracketInstances_Job
            FOREIGN KEY (JobId) REFERENCES Jobs.Jobs(JobId),
        CONSTRAINT FK_brackets_BracketInstances_Agegroup
            FOREIGN KEY (AgegroupId) REFERENCES Leagues.agegroups(AgegroupId),
        CONSTRAINT FK_brackets_BracketInstances_Div
            FOREIGN KEY (DivId) REFERENCES Leagues.divisions(DivId),
        CONSTRAINT FK_brackets_BracketInstances_Template
            FOREIGN KEY (TemplateId) REFERENCES brackets.Templates(TemplateId),
        CONSTRAINT FK_brackets_BracketInstances_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );
    PRINT 'Created brackets.BracketInstances';
END
ELSE PRINT 'brackets.BracketInstances already exists — skipped';
GO

-- ============================================================
-- brackets.SeedAssignments  — slot <- standings rank
--   Single-pool:  SeedDivId set,  AcrossPoolRank NULL
--   Cross-pool :  SeedDivId NULL, AcrossPoolRank set
--                 (candidate set = all pools in the agegroup;
--                  SeedRank = within-pool finishing position)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'brackets' AND t.name = 'SeedAssignments')
BEGIN
    CREATE TABLE brackets.SeedAssignments (
        SeedAssignmentId  INT            NOT NULL IDENTITY(1,1),
        BracketInstanceId INT            NOT NULL,
        Gid               INT            NOT NULL,   -- FK Leagues.schedule
        TargetSlot        TINYINT        NOT NULL,   -- 1 | 2  -> T1*/T2*
        SeedDivId         UNIQUEIDENTIFIER NULL,     -- pool (NULL = cross-pool)
        SeedRank          INT            NOT NULL,   -- within-pool finishing rank
        AcrossPoolRank    INT            NULL,       -- cross-pool rank (NULL = single-pool)
        Modified          DATETIME       NOT NULL DEFAULT GETDATE(),
        LebUserId         NVARCHAR(450)  NULL,

        CONSTRAINT PK_brackets_SeedAssignments PRIMARY KEY (SeedAssignmentId),
        CONSTRAINT UX_brackets_SeedAssignments_Slot UNIQUE (Gid, TargetSlot),
        CONSTRAINT CK_brackets_SeedAssignments_Slot CHECK (TargetSlot IN (1,2)),
        -- enforce exactly one mode (single-pool XOR cross-pool)
        CONSTRAINT CK_brackets_SeedAssignments_Mode CHECK (
            (SeedDivId IS NOT NULL AND AcrossPoolRank IS NULL) OR
            (SeedDivId IS NULL     AND AcrossPoolRank IS NOT NULL)
        ),
        CONSTRAINT FK_brackets_SeedAssignments_Instance
            FOREIGN KEY (BracketInstanceId) REFERENCES brackets.BracketInstances(BracketInstanceId),
        CONSTRAINT FK_brackets_SeedAssignments_Gid
            FOREIGN KEY (Gid) REFERENCES Leagues.schedule(Gid),
        CONSTRAINT FK_brackets_SeedAssignments_Div
            FOREIGN KEY (SeedDivId) REFERENCES Leagues.divisions(DivId),
        CONSTRAINT FK_brackets_SeedAssignments_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );
    PRINT 'Created brackets.SeedAssignments';
END
ELSE PRINT 'brackets.SeedAssignments already exists — skipped';
GO

-- ============================================================
-- brackets.AdvancementFeeds  — slot <- winner/loser of a game
--   Materialized at generation. R3 cascade-invalidate walks
--   WHERE SourceGid = @correctedGid, voids targets, recurses.
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'brackets' AND t.name = 'AdvancementFeeds')
BEGIN
    CREATE TABLE brackets.AdvancementFeeds (
        AdvancementFeedId INT            NOT NULL IDENTITY(1,1),
        BracketInstanceId INT            NOT NULL,
        SourceGid         INT            NOT NULL,   -- FK Leagues.schedule (game decided)
        SourceResult      NVARCHAR(6)    NOT NULL,   -- 'Winner' | 'Loser'
        TargetGid         INT            NOT NULL,   -- FK Leagues.schedule (game being filled)
        TargetSlot        TINYINT        NOT NULL,   -- 1 | 2
        Modified          DATETIME       NOT NULL DEFAULT GETDATE(),
        LebUserId         NVARCHAR(450)  NULL,

        CONSTRAINT PK_brackets_AdvancementFeeds PRIMARY KEY (AdvancementFeedId),
        -- each target slot fed by at most one source
        CONSTRAINT UX_brackets_AdvancementFeeds_Target UNIQUE (TargetGid, TargetSlot),
        CONSTRAINT CK_brackets_AdvancementFeeds_Result
            CHECK (SourceResult IN ('Winner','Loser')),
        CONSTRAINT CK_brackets_AdvancementFeeds_Slot
            CHECK (TargetSlot IN (1,2)),
        CONSTRAINT FK_brackets_AdvancementFeeds_Instance
            FOREIGN KEY (BracketInstanceId) REFERENCES brackets.BracketInstances(BracketInstanceId),
        CONSTRAINT FK_brackets_AdvancementFeeds_SourceGid
            FOREIGN KEY (SourceGid) REFERENCES Leagues.schedule(Gid),
        CONSTRAINT FK_brackets_AdvancementFeeds_TargetGid
            FOREIGN KEY (TargetGid) REFERENCES Leagues.schedule(Gid),
        CONSTRAINT FK_brackets_AdvancementFeeds_LebUser
            FOREIGN KEY (LebUserId) REFERENCES dbo.AspNetUsers(Id)
    );
    PRINT 'Created brackets.AdvancementFeeds';
END
ELSE PRINT 'brackets.AdvancementFeeds already exists — skipped';
GO

-- ============================================================
-- Indexes (idempotent — guarded independently of table creation)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_brackets_BracketInstances_Job')
    CREATE INDEX IX_brackets_BracketInstances_Job ON brackets.BracketInstances (JobId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_brackets_BracketInstances_Agegroup')
    CREATE INDEX IX_brackets_BracketInstances_Agegroup ON brackets.BracketInstances (AgegroupId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_brackets_SeedAssignments_Instance')
    CREATE INDEX IX_brackets_SeedAssignments_Instance ON brackets.SeedAssignments (BracketInstanceId);
GO
-- R3 cascade walk: find downstream feeds by SourceGid
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_brackets_AdvancementFeeds_SourceGid')
    CREATE INDEX IX_brackets_AdvancementFeeds_SourceGid ON brackets.AdvancementFeeds (SourceGid);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_brackets_AdvancementFeeds_Instance')
    CREATE INDEX IX_brackets_AdvancementFeeds_Instance ON brackets.AdvancementFeeds (BracketInstanceId);
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
WHERE s.name = 'brackets'
ORDER BY t.name;
GO
