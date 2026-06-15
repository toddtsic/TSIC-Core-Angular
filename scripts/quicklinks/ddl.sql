-- =============================================================================
-- Quick Links feature schema
-- =============================================================================
-- DB-FIRST: apply this by hand, then re-scaffold EF entities
--   (scripts/3) RE-Scaffold-Db-Entities.ps1). Do NOT hand-edit entities.
-- Target (local/dev): server .\SS2016, database TSICV5.
-- NOT for production until later in the rollout.
--
-- Model:
--   quicklinks.LinkType     = the domain of possible links (superuser-editable).
--                             Grounded types name the Jobs.Jobs column that IS
--                             their on/off truth; ungrounded types (NULL) are
--                             deliberate-on only. No mirrored/synced state.
--   quicklinks.JobQuickLink = per-job instances. `enabled` is authoritative ONLY
--                             for ungrounded links; grounded links leave it NULL
--                             and reflect their grounding property at resolve time.
--   Invariant: "no marker = OFF" (fail-closed).
-- =============================================================================

IF SCHEMA_ID('quicklinks') IS NULL EXEC('CREATE SCHEMA [quicklinks]');
GO

-- DOMAIN: the possible link types (superuser-editable). Grounded types name the
-- job setting that IS their truth; ungrounded types have groundingSetting = NULL.
IF OBJECT_ID('quicklinks.LinkType','U') IS NULL
CREATE TABLE [quicklinks].[LinkType] (
    [linkTypeID]        uniqueidentifier NOT NULL CONSTRAINT [DF_quicklinks.LinkType_id] DEFAULT (newsequentialid()),
    [linkKey]           varchar(50)      NOT NULL,
    [defaultLabel]      nvarchar(100)    NOT NULL,
    [defaultIcon]       varchar(50)      NULL,
    [routeTemplate]     nvarchar(400)    NULL,        -- new-app relative route, e.g. 'registration/player'
    [navigateUrl]       nvarchar(400)    NULL,        -- external URL alternative
    [target]            varchar(20)      NULL,
    [groundingSetting]  varchar(60)      NULL,        -- Jobs.Jobs column that owns on/off; NULL = ungrounded (deliberate-on)
    [groundingInverted] bit              NOT NULL CONSTRAINT [DF_quicklinks.LinkType_grdInv]    DEFAULT ((0)),  -- e.g. bRestrictPublicRosters
    [defaultSortOrder]  int              NOT NULL CONSTRAINT [DF_quicklinks.LinkType_sortOrder] DEFAULT ((0)),
    [active]            bit              NOT NULL CONSTRAINT [DF_quicklinks.LinkType_active]    DEFAULT ((1)),
    [modified]          datetime2        NOT NULL CONSTRAINT [DF_quicklinks.LinkType_modified]  DEFAULT (getdate()),
    [lebUserID]         nvarchar(450)    NULL,
    CONSTRAINT [PK_quicklinks.LinkType] PRIMARY KEY CLUSTERED ([linkTypeID]),
    CONSTRAINT [UQ_quicklinks.LinkType_linkKey] UNIQUE ([linkKey]),
    CONSTRAINT [FK_quicklinks.LinkType_AspNetUsers_lebUserID] FOREIGN KEY ([lebUserID])
        REFERENCES [dbo].[AspNetUsers]([Id])
);
GO

-- INSTANCES: per-job. `enabled` is authoritative ONLY for ungrounded links
-- (non-null); for grounded links it stays NULL and the link reflects its property.
-- label/sortOrder are optional per-job overrides over LinkType defaults.
IF OBJECT_ID('quicklinks.JobQuickLink','U') IS NULL
CREATE TABLE [quicklinks].[JobQuickLink] (
    [jobQuickLinkID] uniqueidentifier NOT NULL CONSTRAINT [DF_quicklinks.JobQuickLink_id] DEFAULT (newsequentialid()),
    [jobID]          uniqueidentifier NOT NULL,
    [linkKey]        varchar(50)      NOT NULL,
    [enabled]        bit              NULL,      -- ungrounded: 1/0 authoritative; grounded: NULL (reflects property)
    [label]          nvarchar(100)    NULL,      -- override; NULL = LinkType.defaultLabel
    [sortOrder]      int              NULL,      -- override; NULL = LinkType.defaultSortOrder
    [modified]       datetime2        NOT NULL CONSTRAINT [DF_quicklinks.JobQuickLink_modified] DEFAULT (getdate()),
    [lebUserID]      nvarchar(450)    NULL,
    CONSTRAINT [PK_quicklinks.JobQuickLink] PRIMARY KEY CLUSTERED ([jobQuickLinkID]),
    CONSTRAINT [UQ_quicklinks.JobQuickLink_jobID_linkKey] UNIQUE ([jobID],[linkKey]),
    CONSTRAINT [FK_quicklinks.JobQuickLink_LinkType_linkKey] FOREIGN KEY ([linkKey])
        REFERENCES [quicklinks].[LinkType]([linkKey]),
    CONSTRAINT [FK_quicklinks.JobQuickLink_Jobs.Jobs_jobID] FOREIGN KEY ([jobID])
        REFERENCES [Jobs].[Jobs]([jobID]),
    CONSTRAINT [FK_quicklinks.JobQuickLink_AspNetUsers_lebUserID] FOREIGN KEY ([lebUserID])
        REFERENCES [dbo].[AspNetUsers]([Id])
);
GO

CREATE NONCLUSTERED INDEX [IX_quicklinks.JobQuickLink_jobID]
    ON [quicklinks].[JobQuickLink]([jobID]) INCLUDE ([linkKey],[enabled],[label],[sortOrder]);
GO

-- ---- Domain seed (one-time; 9 rows). groundingSetting = exact Jobs.Jobs column name ----
-- Icons are first-pass Bootstrap (bi-*) guesses; adjust labels/icons freely.
-- 'store' is seeded ungrounded; if a job-level "store enabled" flag exists, set it later.
IF NOT EXISTS (SELECT 1 FROM [quicklinks].[LinkType])
INSERT INTO [quicklinks].[LinkType]
    ([linkKey],[defaultLabel],[defaultIcon],[routeTemplate],[groundingSetting],[groundingInverted],[defaultSortOrder])
VALUES
    ('register-player',    'Register Player',             'bi-person-plus',    'registration/player',        'bRegistrationAllowPlayer',       0, 10),
    ('register-team',      'Register Team',               'bi-people',         'registration/team',          'bRegistrationAllowTeam',         0, 20),
    ('register-coach',     'Register Coach',              'bi-person-badge',   'registration/adult',          NULL,                            0, 30),
    ('view-schedule',      'View Schedule',               'bi-calendar-event', 'scheduling/view-schedule',   'bScheduleAllowPublicAccess',     0, 40),
    ('master-schedule',    'Master Schedule',             'bi-calendar3',      'scheduling/master-schedule', 'bScheduleAllowPublicAccess',     0, 50),
    ('public-rosters',     'Rosters',                     'bi-list-ul',        'rosters/public',             'bRestrictPublicRosters',         1, 60),
    ('player-insurance',   'Insurance Update',            'bi-shield-check',   'playerviupdate',             'bOfferPlayerRegsaverInsurance',  0, 70),
    ('clubrep-insurance',  'Insurance Update (Club Rep)', 'bi-shield-check',   'clubrepviupdate',            'bOfferTeamRegsaverInsurance',    0, 80),
    ('store',              'Store',                       'bi-bag',            'store',                       NULL,                            0, 90);
GO
