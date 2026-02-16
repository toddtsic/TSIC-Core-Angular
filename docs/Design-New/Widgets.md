# Widget-Based Dashboard Architecture

> **Status**: Design Phase
> **Date**: 2026-02-15
> **Scope**: Replace menu-driven navigation with role+jobType-aware, configurable dashboards

---

## 1. Design Philosophy

### The Problem with Menu Systems
The legacy system organizes features around **entities** (Players page, Teams page, Schedules page). Users must map their intent ("I need to set up Saturday's games") to system structure ("go to Scheduling > Manage Timeslots"). This is developer thinking, not user thinking.

### The Dashboard Approach
Dashboards organize around **tasks and context**: "What do I need to do right now?" and "What's the state of things?" The user lands on a surface tailored to their role and job type, with live status indicators and direct action launchers.

### Key Principles
- **Task-oriented, not entity-oriented** - organize around what users do, not what tables exist
- **Role + JobType aware** - a Director on a scheduling job sees different widgets than a Director on a registration job
- **Data-driven configurability** - which widgets appear, their order, and their grouping are all stored in the database, not hardcoded
- **Progressive migration** - features that don't have widgets yet fall through to the existing menu system; the nav shrinks as dashboards grow

---

## 2. Dashboard Layout Structure

Every role's dashboard follows a three-section layout:

```
+-------------------------------------------------------+
|  HEALTH STRIP (always visible, compact)                |
|  Status indicators - glanceable, not actionable        |
|  e.g. "42 Teams | 12 Clubs" "3 Unpaid | $1,200"       |
|        "Schedule: Complete"                             |
+-------------------------------------------------------+
|  ACTION ZONE (primary area)                            |
|  Grouped by domain, ordered by importance              |
|                                                        |
|  +-- Event Setup --------------------------------+     |
|  |  [Scheduling Pipeline]  [Pool Assignment]     |     |
|  +-----------------------------------------------+     |
|                                                        |
|  +-- Registrations ------------------------------+     |
|  |  [Search Registrations]  [View by Club]       |     |
|  |  [Teams Awaiting Payment (!) 3]               |     |
|  +-----------------------------------------------+     |
|                                                        |
|  +-- Communication ------------------------------+     |
|  |  [Compose Email]  [Manage Bulletins]          |     |
|  +-----------------------------------------------+     |
+-------------------------------------------------------+
|  INSIGHT ZONE (lower priority)                         |
|  Reports and informational views                       |
|                                                        |
|  +-- Reports ------------------------------------+     |
|  |  [Registration Summary]  [Custom Report A]    |     |
|  |  [Job Revenue]                                |     |
|  +-----------------------------------------------+     |
+-------------------------------------------------------+
```

### Sections
| Section | Purpose | Content Type |
|---------|---------|-------------|
| `health` | At-a-glance status, scannable in 2 seconds | Status cards with counts/indicators |
| `action` | The things the user does, grouped by domain | Quick actions, workflow launchers, links |
| `insight` | Things the user checks occasionally | Reports, informational views, analytics |

### Widget Categories (groups within sections)
Categories create visual card groupings within a section. Examples:
- Event Setup (section: action)
- Registrations (section: action)
- Communication (section: action)
- Reports (section: insight)
- Financial Overview (section: health)

Categories are stored in a lookup table, so new groupings can be added without code changes.

---

## 3. Configuration Model

### Three-Tier Resolution

```
Tier 1: WidgetDefault  (Role + JobType)
        "Every Director on a scheduling job gets these 8 widgets"

Tier 2: JobWidget       (Per-job overrides and additions)
        "Job 47 also gets 3 custom report links and cross-job revenue"

Runtime: Merge Tier 1 + Tier 2
         JobWidget wins where it exists; WidgetDefault fills the rest
```

### Resolution Logic (pseudocode)
```
Dashboard for (jobId, roleId):
    1. Look up job's jobTypeId
    2. SELECT from WidgetDefault WHERE jobTypeId AND roleId  --> baseline
    3. SELECT from JobWidget WHERE jobId AND roleId          --> overrides
    4. LEFT JOIN defaults to overrides on widgetId
    5. COALESCE(override.categoryId, default.categoryId)
    6. COALESCE(override.displayOrder, default.displayOrder)
    7. COALESCE(override.config, default.config)
    8. Exclude where override.isEnabled = 0
    9. Include overrides that have no matching default (job-specific additions)
```

### What This Buys Us
- **Defaults are first-class data** - not a convention on a magic job
- **Edit defaults in one place** - all jobs of that type inherit changes (unless overridden)
- **Per-job overrides are explicit** - admin can see "inherited" vs "customized"
- **No cloning** - new jobs with no JobWidget rows just use defaults
- **Cross-job features** (like Customer Job Revenue) are just Tier 2 widgets enabled on specific jobs

---

## 4. Database Schema

**Schema**: `widgets`

### 4.1 widgets.WidgetCategory
Lookup table defining groups/sections for widget display.

| Column | Type | Notes |
|--------|------|-------|
| `CategoryId` | `int identity` | PK |
| `Name` | `nvarchar(100)` | e.g. 'Event Setup', 'Registrations', 'Communication', 'Reports' |
| `Section` | `nvarchar(20)` | 'health' \| 'action' \| 'insight' |
| `Icon` | `nvarchar(50)` | Bootstrap icon class (nullable) |
| `DefaultOrder` | `int` | Category sort order within its section |

### 4.2 widgets.Widget
Master catalog of all available widgets.

| Column | Type | Notes |
|--------|------|-------|
| `WidgetId` | `int identity` | PK |
| `Name` | `nvarchar(100)` | e.g. 'Scheduling Pipeline', 'Search Registrations' |
| `WidgetType` | `nvarchar(30)` | 'status-card' \| 'quick-action' \| 'workflow-pipeline' \| 'link-group' |
| `ComponentKey` | `nvarchar(100)` | Angular component binding key |
| `CategoryId` | `int` | FK -> WidgetCategory (default category) |
| `Description` | `nvarchar(500)` | For admin editor display (nullable) |

### 4.3 widgets.WidgetDefault
Default widget assignments by Role + JobType.

| Column | Type | Notes |
|--------|------|-------|
| `WidgetDefaultId` | `int identity` | PK |
| `JobTypeId` | `int` | FK -> reference.JobTypes |
| `RoleId` | `nvarchar(450)` | FK -> dbo.AspNetRoles(Id) |
| `WidgetId` | `int` | FK -> widgets.Widget |
| `CategoryId` | `int` | FK -> widgets.WidgetCategory (can override widget's default) |
| `DisplayOrder` | `int` | Sort order within category |
| `Config` | `nvarchar(max)` | Widget-specific JSON config (nullable) |

**Unique constraint**: `(JobTypeId, RoleId, WidgetId)`

### 4.4 widgets.JobWidget
Per-job widget overrides and additions.

| Column | Type | Notes |
|--------|------|-------|
| `JobWidgetId` | `int identity` | PK |
| `JobId` | `uniqueidentifier` | FK -> Jobs.Jobs |
| `WidgetId` | `int` | FK -> widgets.Widget |
| `RoleId` | `nvarchar(450)` | FK -> dbo.AspNetRoles(Id) |
| `CategoryId` | `int` | FK -> widgets.WidgetCategory (override) |
| `DisplayOrder` | `int` | Sort order override |
| `IsEnabled` | `bit` | Can disable a default widget for this job |
| `Config` | `nvarchar(max)` | Override widget-specific JSON config (nullable) |

**Unique constraint**: `(JobId, WidgetId, RoleId)`

---

## 5. Angular Integration

### Widget Registry (code-side)
A map from `ComponentKey` strings to Angular components:

```typescript
const widgetRegistry = new Map<string, Type<any>>([
    ['scheduling-pipeline',    SchedulingDashboardComponent],
    ['scheduling-status',      SchedulingStatusCardComponent],
    ['registration-status',    RegistrationStatusCardComponent],
    ['financial-status',       FinancialStatusCardComponent],
    ['search-registrations',   SearchRegistrationsLinkComponent],
    ['compose-email',          ComposeEmailLinkComponent],
    ['manage-bulletins',       ManageBulletinsLinkComponent],
    ['report-link',            ReportLinkComponent],         // generic, config-driven
    ['status-card',            GenericStatusCardComponent],   // generic, config-driven
]);
```

Some widgets are **unique components** with their own data fetching (scheduling pipeline). Others are **generic renderers** driven by the `Config` JSON (a report link just needs a URL and label).

### Widget Types
| WidgetType | Renders As | Config Driven? |
|-----------|-----------|---------------|
| `status-card` | Compact metric with icon + count | Yes - endpoint, label, thresholds |
| `quick-action` | Button/link that launches a workflow | Yes - route, label, icon |
| `workflow-pipeline` | Multi-step progress indicator | No - dedicated component |
| `link-group` | Titled group of links (fallback) | Yes - array of {label, route} |

### Dashboard Component Flow
1. Auth resolves `jobId` + `roleId`
2. API call: `GET /api/dashboard/widgets` (backend performs the merge)
3. Backend returns merged widget list with resolved categories and config
4. Dashboard component groups by section -> category -> displayOrder
5. For each widget, look up `componentKey` in registry, render dynamically

---

## 6. Reference Example: Director on Scheduling Job

**Role**: Director | **JobType**: League Scheduling (2) / Tournament Scheduling (3)

### Tier 1 Defaults (WidgetDefault)

| Widget | Type | Category | Section | Order |
|--------|------|----------|---------|-------|
| Registration Count | status-card | Registration Overview | health | 1 |
| Financial Status | status-card | Financial Overview | health | 2 |
| Scheduling Status | status-card | Scheduling Overview | health | 3 |
| Scheduling Pipeline | workflow-pipeline | Event Setup | action | 1 |
| Pool Assignment | quick-action | Event Setup | action | 2 |
| Search Registrations | quick-action | Registrations | action | 1 |
| View by Club | quick-action | Registrations | action | 2 |
| Compose Email | quick-action | Communication | action | 1 |
| Manage Bulletins | quick-action | Communication | action | 2 |

### Tier 2 Per-Job Additions (JobWidget) - example

| Widget | Type | Category | Section | Notes |
|--------|------|----------|---------|-------|
| Club Rep Attendance Report | link-group | Reports | insight | Custom report Todd built |
| Tournament Summary Report | link-group | Reports | insight | Custom report Todd built |
| Customer Job Revenue | workflow-pipeline | Reports | insight | Cross-job; enabled on flagship job only |

### What the Director Sees
All Tier 1 defaults + this job's Tier 2 additions, merged into the health/action/insight layout.

---

## 7. Report Registry (Future Extension)

The widget model naturally extends to a report catalog:

```
reference.Reports          (already exists in DB)
    ReportId, Name, Description, Category, ReportType, TemplatePath, Parameters[]

widgets.Widget             (one Widget entry per report, WidgetType = 'report-link')
    ComponentKey = 'report-link', Config = { reportId, defaultParams }

widgets.JobWidget          (enables specific reports per job)
    Config = { reportId, customLabel, paramDefaults }
```

An admin editor allows enabling/disabling reports per job using the same `JobWidget` mechanism. The "Reports" category on the dashboard self-populates from enabled report widgets.

---

## 8. Admin Editor UX (Future)

For a given job, the admin sees:

**Available Widgets** (from Widget catalog, filtered by job type applicability)
- Toggle to enable/disable
- Set display order within category
- Assign category (or accept default)
- Set role visibility
- Configure widget-specific settings

**Active Widgets** (what this job's dashboard shows)
- Visual preview of layout
- "Inherited from default" vs "Customized" indicators
- Drag to reorder within category

This editor works identically for any job. There is no special-case UI for default jobs vs real jobs.

---

## 9. Migration Path

1. **Phase 1**: Build schema, seed WidgetCategory and Widget tables, create WidgetDefault entries for Director + Scheduling JobTypes
2. **Phase 2**: Build dashboard API endpoint (merge logic) and Angular dashboard component with widget registry
3. **Phase 3**: Director/Scheduling dashboard goes live - hardcoded Angular components backed by DB config
4. **Phase 4**: Build admin widget editor for per-job customization (JobWidget management)
5. **Phase 5**: Extend to additional roles and job types
6. **Phase 6**: Menu system progressively retired as widget coverage grows

Throughout: existing menu system remains functional as fallback. No big-bang cutover.

---

## 10. Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Defaults storage | Dedicated `WidgetDefault` table | First-class data, not convention on a magic job. Changes propagate automatically. |
| Default key | Role + JobType | A Director on a scheduling job has fundamentally different needs than a Director on a registration job |
| Per-job overrides | `JobWidget` with merge semantics | Explicit overrides, no cloning, no stale copies |
| Cross-job features | Just a per-job widget enabled on flagship job | No separate customer-level routing tier needed |
| Schema | `widgets.*` | Consistent with existing schema organization (Menus, Leagues, stores, etc.) |
| PK types | `int identity` for widget tables | Reference/config data, consistent with similar tables |
| FK types | Match existing: `uniqueidentifier` (JobId), `nvarchar(450)` (RoleId), `int` (JobTypeId) | DB-first scaffolding compatibility |
| Config column | `nvarchar(max)` JSON | Flexible per-widget configuration without schema changes |

---

## 11. T-SQL Creation Script

> **Safety**: Fully idempotent. IF NOT EXISTS guards on all objects. No DROP, no ALTER on
> existing tables, no seed data. Safe to run against production multiple times with zero side effects.

<details>
<summary>📋 Click to expand T-SQL script</summary>

```sql
/*
    Widget Dashboard Schema
    -----------------------
    Creates the [widgets] schema and four tables:
      - widgets.WidgetCategory   (lookup: sections and groups)
      - widgets.Widget           (master catalog of available widgets)
      - widgets.WidgetDefault    (default assignments by Role + JobType)
      - widgets.JobWidget        (per-job overrides and additions)

    Prerequisites:
      - reference.JobTypes       (JobTypeId int PK)
      - dbo.AspNetRoles          (Id nvarchar(450) PK)
      - Jobs.Jobs                (JobId uniqueidentifier PK)

    Idempotent: safe to run multiple times.
    No side effects on existing tables.
*/

-- ============================================================
-- Schema
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'widgets')
BEGIN
    EXEC('CREATE SCHEMA [widgets] AUTHORIZATION [dbo]');
END
GO

-- ============================================================
-- widgets.WidgetCategory
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'WidgetCategory')
BEGIN
    CREATE TABLE [widgets].[WidgetCategory]
    (
        [CategoryId]    INT IDENTITY(1,1)   NOT NULL,
        [Name]          NVARCHAR(100)       NOT NULL,
        [Section]       NVARCHAR(20)        NOT NULL,   -- 'content' | 'health' | 'action' | 'insight'
        [Icon]          NVARCHAR(50)        NULL,       -- Bootstrap icon class
        [DefaultOrder]  INT                 NOT NULL    DEFAULT 0,

        CONSTRAINT [PK_widgets_WidgetCategory]
            PRIMARY KEY CLUSTERED ([CategoryId]),

        CONSTRAINT [CK_widgets_WidgetCategory_Section]
            CHECK ([Section] IN ('content', 'health', 'action', 'insight'))
    );
END
GO

-- ============================================================
-- widgets.Widget
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'Widget')
BEGIN
    CREATE TABLE [widgets].[Widget]
    (
        [WidgetId]      INT IDENTITY(1,1)   NOT NULL,
        [Name]          NVARCHAR(100)       NOT NULL,
        [WidgetType]    NVARCHAR(30)        NOT NULL,   -- 'content' | 'status-card' | 'quick-action' | 'workflow-pipeline' | 'link-group'
        [ComponentKey]  NVARCHAR(100)       NOT NULL,   -- Angular component binding key
        [CategoryId]    INT                 NOT NULL,   -- Default category
        [Description]   NVARCHAR(500)       NULL,

        CONSTRAINT [PK_widgets_Widget]
            PRIMARY KEY CLUSTERED ([WidgetId]),

        CONSTRAINT [FK_widgets_Widget_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),

        CONSTRAINT [CK_widgets_Widget_WidgetType]
            CHECK ([WidgetType] IN ('content', 'chart', 'status-card', 'quick-action', 'workflow-pipeline', 'link-group'))
    );
END
GO

-- ============================================================
-- widgets.WidgetDefault
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'WidgetDefault')
BEGIN
    CREATE TABLE [widgets].[WidgetDefault]
    (
        [WidgetDefaultId]   INT IDENTITY(1,1)   NOT NULL,
        [JobTypeId]         INT                 NOT NULL,
        [RoleId]            NVARCHAR(450)       NOT NULL,
        [WidgetId]          INT                 NOT NULL,
        [CategoryId]        INT                 NOT NULL,   -- Can override widget's default category
        [DisplayOrder]      INT                 NOT NULL    DEFAULT 0,
        [Config]            NVARCHAR(MAX)       NULL,       -- Widget-specific JSON

        CONSTRAINT [PK_widgets_WidgetDefault]
            PRIMARY KEY CLUSTERED ([WidgetDefaultId]),

        CONSTRAINT [FK_widgets_WidgetDefault_JobTypeId]
            FOREIGN KEY ([JobTypeId])
            REFERENCES [reference].[JobTypes] ([JobTypeId]),

        CONSTRAINT [FK_widgets_WidgetDefault_RoleId]
            FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[AspNetRoles] ([Id]),

        CONSTRAINT [FK_widgets_WidgetDefault_WidgetId]
            FOREIGN KEY ([WidgetId])
            REFERENCES [widgets].[Widget] ([WidgetId]),

        CONSTRAINT [FK_widgets_WidgetDefault_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),

        CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget]
            UNIQUE ([JobTypeId], [RoleId], [WidgetId])
    );
END
GO

-- ============================================================
-- widgets.JobWidget
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'JobWidget')
BEGIN
    CREATE TABLE [widgets].[JobWidget]
    (
        [JobWidgetId]   INT IDENTITY(1,1)       NOT NULL,
        [JobId]         UNIQUEIDENTIFIER        NOT NULL,
        [WidgetId]      INT                     NOT NULL,
        [RoleId]        NVARCHAR(450)           NOT NULL,
        [CategoryId]    INT                     NOT NULL,   -- Override category
        [DisplayOrder]  INT                     NOT NULL    DEFAULT 0,
        [IsEnabled]     BIT                     NOT NULL    DEFAULT 1,
        [Config]        NVARCHAR(MAX)           NULL,       -- Override widget-specific JSON

        CONSTRAINT [PK_widgets_JobWidget]
            PRIMARY KEY CLUSTERED ([JobWidgetId]),

        CONSTRAINT [FK_widgets_JobWidget_JobId]
            FOREIGN KEY ([JobId])
            REFERENCES [Jobs].[Jobs] ([JobId]),

        CONSTRAINT [FK_widgets_JobWidget_WidgetId]
            FOREIGN KEY ([WidgetId])
            REFERENCES [widgets].[Widget] ([WidgetId]),

        CONSTRAINT [FK_widgets_JobWidget_RoleId]
            FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[AspNetRoles] ([Id]),

        CONSTRAINT [FK_widgets_JobWidget_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),

        CONSTRAINT [UQ_widgets_JobWidget_Job_Widget_Role]
            UNIQUE ([JobId], [WidgetId], [RoleId])
    );
END
GO
```

</details>

---

## 12. Seed Data Script — All Roles (Scheduling + Anonymous Public)

> **Purpose**: Populates widget categories, widgets, and defaults for:
> - **Scheduling roles** (Director / SuperDirector / SuperUser) on League (JobTypeId=2) and Tournament (JobTypeId=3) job types
> - **Chart widgets** (Player Trend, Team Trend, Age Group Distribution) for Director / SuperDirector / SuperUser
> - **Anonymous public** role — content widgets (banner + bulletins) for ALL job types
>
> Role inheritance: Director ⊂ SuperDirector ⊂ SuperUser.
> Idempotent — MERGE-based, safe to run on fresh or existing data.

<details>
<summary>Click to expand seed data script</summary>

```sql
/*
    Widget Dashboard — Seed Data (All Roles)
    -----------------------------------------
    Scheduling roles (Director ⊂ SuperDirector ⊂ SuperUser):
      Director (15 widgets)  ⊂  SuperDirector (+1 = 16)  ⊂  SuperUser (+4 = 20)

    Anonymous public role:
      Content widgets (banner + bulletins) for ALL job types

    Populates:
      1. widgets.WidgetCategory   — 10 categories across 4 sections
      2. widgets.Widget           — 22 widgets (17 scheduling + 3 chart + 2 content)
      3. widgets.WidgetDefault    — 102 scheduling rows + N anonymous rows (all job types)

    Prerequisites:
      - widgets schema and tables (run Section 11 creation script first)
      - reference.JobTypes: all job types
      - dbo.AspNetRoles: Director, SuperDirector, Superuser, Anonymous

    Idempotent: safe to run multiple times.
    Uses MERGE to avoid duplicate inserts.
*/

SET NOCOUNT ON;

-- ============================================================
-- 1. Widget Categories (10)
-- ============================================================

MERGE [widgets].[WidgetCategory] AS target
USING (VALUES
    -- content section (renders before all others)
    ('Content',                 'content',  NULL,                   0),
    ('Dashboard Charts',        'content',  NULL,                   1),
    -- health section
    ('Registration Overview',   'health',   'bi-people',            1),
    ('Financial Overview',      'health',   'bi-currency-dollar',   2),
    ('Scheduling Overview',     'health',   'bi-calendar-check',    3),
    -- action section
    ('Event Setup',             'action',   'bi-gear',              1),
    ('Registrations',           'action',   'bi-person-lines-fill', 2),
    ('Communication',           'action',   'bi-envelope',          3),
    ('Job Administration',      'action',   'bi-shield-lock',       4),
    -- insight section
    ('Reports',                 'insight',  'bi-bar-chart',         1)
) AS source ([Name], [Section], [Icon], [DefaultOrder])
ON target.[Name] = source.[Name]
WHEN NOT MATCHED THEN
    INSERT ([Name], [Section], [Icon], [DefaultOrder])
    VALUES (source.[Name], source.[Section], source.[Icon], source.[DefaultOrder]);

PRINT 'WidgetCategory seed complete (10 categories).';

-- ============================================================
-- 2. Widgets (22: 17 scheduling + 3 chart + 2 content)
-- ============================================================

MERGE [widgets].[Widget] AS target
USING (
    SELECT
        s.[Name], s.[WidgetType], s.[ComponentKey], s.[Description],
        wc.[CategoryId]
    FROM (VALUES
        -- Content widgets (Anonymous public)
        ('Client Banner',          'content',              'client-banner',            'Content',                  'Job banner with logo and images'),
        ('Bulletins',              'content',              'bulletins',                'Content',                  'Active job bulletins and announcements'),

        -- Chart widgets (Director + SuperDirector + SuperUser)
        ('Player Registration Trend', 'chart',            'player-trend-chart',       'Dashboard Charts',         'Daily player registration counts and cumulative revenue over time'),
        ('Team Registration Trend',   'chart',            'team-trend-chart',         'Dashboard Charts',         'Daily team registration counts and cumulative revenue over time'),
        ('Age Group Distribution',    'chart',            'agegroup-distribution',    'Dashboard Charts',         'Player and team counts broken down by age group'),

        -- Director baseline (9): health + action
        ('Registration Count',     'status-card',          'registration-status',      'Registration Overview',    'Club and team registration counts'),
        ('Financial Status',       'status-card',          'financial-status',         'Financial Overview',       'Payment status and outstanding balances'),
        ('Scheduling Status',      'status-card',          'scheduling-status',        'Scheduling Overview',      'Schedule completion status'),
        ('Scheduling Pipeline',    'workflow-pipeline',    'scheduling-pipeline',      'Event Setup',              'Step-by-step scheduling workflow'),
        ('Pool Assignment',        'quick-action',         'pool-assignment',          'Event Setup',              'Assign teams to pools'),
        ('Search Registrations',   'quick-action',         'search-registrations',     'Registrations',            'Search and manage registrations'),
        ('View by Club',           'quick-action',         'view-by-club',             'Registrations',            'Browse registrations grouped by club'),
        ('Compose Email',          'quick-action',         'compose-email',            'Communication',            'Send email to registrants'),
        ('Manage Bulletins',       'quick-action',         'manage-bulletins',         'Communication',            'Create and manage job bulletins'),

        -- Shared: Director + SuperDirector + SuperUser (3)
        ('LADT Editor',            'quick-action',         'ladt-editor',              'Event Setup',              'Configure leagues, age groups, divisions, and teams'),
        ('Roster Swapper',         'quick-action',         'roster-swapper',           'Registrations',            'Move players between rosters'),
        ('Discount Codes',         'quick-action',         'discount-codes',           'Registrations',            'Manage registration discount codes'),

        -- SuperDirector addition (1)
        ('Cross-Job Financials',   'quick-action',         'cross-job-financials',     'Reports',                  'Revenue overview across all customer jobs'),

        -- SuperUser additions (4)
        ('Job Administrators',     'quick-action',         'job-administrators',       'Job Administration',       'Manage administrator access for this job'),
        ('Profile Editor',         'quick-action',         'profile-editor',           'Job Administration',       'Edit user profile data'),
        ('Profile Migration',      'quick-action',         'profile-migration',        'Job Administration',       'Migrate legacy user profiles'),
        ('Theme Editor',           'quick-action',         'theme-editor',             'Job Administration',       'Customize job theme and branding')
    ) AS s ([Name], [WidgetType], [ComponentKey], [CategoryName], [Description])
    INNER JOIN [widgets].[WidgetCategory] wc ON wc.[Name] = s.[CategoryName]
) AS source
ON target.[ComponentKey] = source.[ComponentKey]
WHEN NOT MATCHED THEN
    INSERT ([Name], [WidgetType], [ComponentKey], [CategoryId], [Description])
    VALUES (source.[Name], source.[WidgetType], source.[ComponentKey], source.[CategoryId], source.[Description]);

PRINT 'Widget seed complete (22 widgets).';

-- ============================================================
-- Role GUIDs
-- ============================================================
DECLARE @DirectorRoleId      NVARCHAR(450) = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06';
DECLARE @SuperDirectorRoleId NVARCHAR(450) = '7B9EB503-53C9-44FA-94A0-17760C512440';
DECLARE @SuperuserRoleId     NVARCHAR(450) = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';

-- ============================================================
-- Helper: reusable widget config values
-- ============================================================
-- Config JSON per ComponentKey (shared across all roles and job types)

DECLARE @WidgetConfigs TABLE (ComponentKey NVARCHAR(100), Config NVARCHAR(MAX));
INSERT INTO @WidgetConfigs VALUES
    -- status-card: endpoint for live data fetch
    ('registration-status',  '{"endpoint":"api/registrations/summary","label":"Registrations","icon":"bi-people","format":"count"}'),
    ('financial-status',     '{"endpoint":"api/payments/summary","label":"Financials","icon":"bi-currency-dollar","format":"currency"}'),
    ('scheduling-status',    '{"endpoint":"api/scheduling/status","label":"Schedule","icon":"bi-calendar-check","format":"status"}'),
    -- workflow-pipeline: dedicated component
    ('scheduling-pipeline',  '{"route":"admin/scheduling","icon":"bi-calendar-range"}'),
    -- quick-action: route + label + icon
    ('pool-assignment',      '{"route":"admin/pool-assignment","label":"Pool Assignment","icon":"bi-diagram-3"}'),
    ('search-registrations', '{"route":"admin/search","label":"Search Registrations","icon":"bi-search"}'),
    ('view-by-club',         '{"route":"admin/team-search","label":"View by Club","icon":"bi-building"}'),
    ('compose-email',        '{"route":"email/compose","label":"Compose Email","icon":"bi-envelope-plus"}'),
    ('manage-bulletins',     '{"route":"bulletins/manage","label":"Manage Bulletins","icon":"bi-megaphone"}'),
    -- shared widgets
    ('ladt-editor',          '{"route":"ladt/admin","label":"LADT Editor","icon":"bi-list-nested"}'),
    ('roster-swapper',       '{"route":"admin/roster-swapper","label":"Roster Swapper","icon":"bi-arrow-left-right"}'),
    ('discount-codes',       '{"route":"jobdiscountcodes/admin","label":"Discount Codes","icon":"bi-tags"}'),
    -- SuperDirector addition
    ('cross-job-financials', '{"route":"reporting/job-revenue","label":"Cross-Job Financials","icon":"bi-cash-stack"}'),
    -- SuperUser additions
    ('job-administrators',   '{"route":"jobadministrator/admin","label":"Job Administrators","icon":"bi-person-gear"}'),
    ('profile-editor',       '{"route":"admin/profile-editor","label":"Profile Editor","icon":"bi-person-badge"}'),
    ('profile-migration',    '{"route":"admin/profile-migration","label":"Profile Migration","icon":"bi-arrow-repeat"}'),
    ('theme-editor',         '{"route":"admin/theme","label":"Theme Editor","icon":"bi-palette"}');

-- ============================================================
-- 3. Director defaults (15 widgets × 2 job types = 30 rows)
-- ============================================================
-- Director gets: 9 baseline + LADT + Roster Swapper + Discount Codes + 3 charts

DECLARE @DirectorWidgets TABLE (ComponentKey NVARCHAR(100), DisplayOrder INT);
INSERT INTO @DirectorWidgets VALUES
    ('registration-status', 1), ('financial-status', 2), ('scheduling-status', 3),
    ('scheduling-pipeline', 1), ('pool-assignment', 2), ('ladt-editor', 3),
    ('search-registrations', 1), ('view-by-club', 2), ('roster-swapper', 3), ('discount-codes', 4),
    ('compose-email', 1), ('manage-bulletins', 2),
    ('player-trend-chart', 1), ('team-trend-chart', 2), ('agegroup-distribution', 3);

-- JobTypeId = 2 (League Scheduling)
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 2 AS [JobTypeId], @DirectorRoleId AS [RoleId],
           w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @DirectorWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- JobTypeId = 3 (Tournament Scheduling)
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 3 AS [JobTypeId], @DirectorRoleId AS [RoleId],
           w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @DirectorWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

PRINT 'Director defaults seeded (15 widgets x 2 job types).';

-- ============================================================
-- 4. SuperDirector defaults (16 widgets × 2 job types = 32 rows)
-- ============================================================
-- SuperDirector = Director (15) + Cross-Job Financials

DECLARE @SuperDirectorWidgets TABLE (ComponentKey NVARCHAR(100), DisplayOrder INT);
INSERT INTO @SuperDirectorWidgets
    SELECT * FROM @DirectorWidgets;  -- inherit Director's set
INSERT INTO @SuperDirectorWidgets VALUES
    ('cross-job-financials', 1);     -- add SuperDirector-specific

-- JobTypeId = 2
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 2 AS [JobTypeId], @SuperDirectorRoleId AS [RoleId],
           w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @SuperDirectorWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- JobTypeId = 3
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 3 AS [JobTypeId], @SuperDirectorRoleId AS [RoleId],
           w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @SuperDirectorWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

PRINT 'SuperDirector defaults seeded (16 widgets x 2 job types).';

-- ============================================================
-- 5. SuperUser defaults (20 widgets × 2 job types = 40 rows)
-- ============================================================
-- SuperUser = SuperDirector (16) + Job Administrators + Profile Editor
--             + Profile Migration + Theme Editor

DECLARE @SuperuserWidgets TABLE (ComponentKey NVARCHAR(100), DisplayOrder INT);
INSERT INTO @SuperuserWidgets
    SELECT * FROM @SuperDirectorWidgets;  -- inherit SuperDirector's set
INSERT INTO @SuperuserWidgets VALUES
    ('job-administrators', 1), ('profile-editor', 2),
    ('profile-migration', 3), ('theme-editor', 4);

-- JobTypeId = 2
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 2 AS [JobTypeId], @SuperuserRoleId AS [RoleId],
           w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @SuperuserWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- JobTypeId = 3
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 3 AS [JobTypeId], @SuperuserRoleId AS [RoleId],
           w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @SuperuserWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

PRINT 'SuperUser defaults seeded (20 widgets x 2 job types).';

-- ============================================================
-- 6. Anonymous defaults (2 content widgets × ALL job types)
-- ============================================================
-- Anonymous role gets: Client Banner + Bulletins for every job type

DECLARE @AnonymousRoleId NVARCHAR(450) = 'CBF3F384-190F-4962-BF58-40B095628DC8';

DECLARE @BannerWidgetId INT = (SELECT WidgetId FROM [widgets].[Widget] WHERE ComponentKey = 'client-banner');
DECLARE @BulletinsWidgetId INT = (SELECT WidgetId FROM [widgets].[Widget] WHERE ComponentKey = 'bulletins');
DECLARE @ContentCatId INT = (SELECT CategoryId FROM [widgets].[WidgetCategory] WHERE Name = 'Content' AND Section = 'content');

-- Banner for all job types
INSERT INTO [widgets].[WidgetDefault] ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
SELECT jt.[JobTypeId], @AnonymousRoleId, @BannerWidgetId, @ContentCatId, 1, NULL
FROM [reference].[JobTypes] jt
WHERE NOT EXISTS (
    SELECT 1 FROM [widgets].[WidgetDefault] wd
    WHERE wd.[JobTypeId] = jt.[JobTypeId]
      AND wd.[RoleId] = @AnonymousRoleId
      AND wd.[WidgetId] = @BannerWidgetId
);

-- Bulletins for all job types
INSERT INTO [widgets].[WidgetDefault] ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
SELECT jt.[JobTypeId], @AnonymousRoleId, @BulletinsWidgetId, @ContentCatId, 2, NULL
FROM [reference].[JobTypes] jt
WHERE NOT EXISTS (
    SELECT 1 FROM [widgets].[WidgetDefault] wd
    WHERE wd.[JobTypeId] = jt.[JobTypeId]
      AND wd.[RoleId] = @AnonymousRoleId
      AND wd.[WidgetId] = @BulletinsWidgetId
);

PRINT 'Anonymous defaults seeded (2 content widgets x all job types).';

-- ============================================================
-- Summary
-- ============================================================
PRINT '';
PRINT '=== Seed Summary ===';
SELECT 'Categories' AS [Table], COUNT(*) AS [Count] FROM [widgets].[WidgetCategory]
UNION ALL
SELECT 'Widgets', COUNT(*) FROM [widgets].[Widget]
UNION ALL
SELECT 'Defaults', COUNT(*) FROM [widgets].[WidgetDefault]
UNION ALL
SELECT 'JobWidgets', COUNT(*) FROM [widgets].[JobWidget];

SET NOCOUNT OFF;
```

</details>

---

## 13. All-in-One Script (for prod restore / fresh DB)

> **Purpose**: After restoring a production backup to dev, run this single script to
> create the widgets schema + tables + seed data in one shot. Combines Section 11 (creation)
> and Section 12 (seed). Fully idempotent — safe to run against any state.
>
> **Workflow**: Restore prod backup → run this script → dev DB has widget dashboard support.

<details>
<summary>Click to expand all-in-one script</summary>

```sql
/*
    Widget Dashboard — All-in-One Setup Script
    -------------------------------------------
    Creates schema, tables, and seeds data for the widget dashboard.
    Idempotent: safe to run on fresh DB, existing dev, or restored prod backup.

    What it does:
      1. Creates [widgets] schema (if not exists)
      2. Creates 4 tables: WidgetCategory, Widget, WidgetDefault, JobWidget
      3. Seeds 10 categories, 22 widgets
      4. Seeds scheduling defaults: 102 rows (3 roles × 2 job types)
      5. Seeds anonymous defaults: 2 content widgets × all job types

    Scheduling roles: Director (15) ⊂ SuperDirector (16) ⊂ SuperUser (20)
    Anonymous public: Client Banner + Bulletins (content section)
*/

-- ============================================================
-- PART 1: Schema + Tables (from Section 11)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'widgets')
BEGIN
    EXEC('CREATE SCHEMA [widgets] AUTHORIZATION [dbo]');
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'WidgetCategory')
BEGIN
    CREATE TABLE [widgets].[WidgetCategory]
    (
        [CategoryId]    INT IDENTITY(1,1)   NOT NULL,
        [Name]          NVARCHAR(100)       NOT NULL,
        [Section]       NVARCHAR(20)        NOT NULL,
        [Icon]          NVARCHAR(50)        NULL,
        [DefaultOrder]  INT                 NOT NULL    DEFAULT 0,

        CONSTRAINT [PK_widgets_WidgetCategory]
            PRIMARY KEY CLUSTERED ([CategoryId]),
        CONSTRAINT [CK_widgets_WidgetCategory_Section]
            CHECK ([Section] IN ('content', 'health', 'action', 'insight'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'Widget')
BEGIN
    CREATE TABLE [widgets].[Widget]
    (
        [WidgetId]      INT IDENTITY(1,1)   NOT NULL,
        [Name]          NVARCHAR(100)       NOT NULL,
        [WidgetType]    NVARCHAR(30)        NOT NULL,
        [ComponentKey]  NVARCHAR(100)       NOT NULL,
        [CategoryId]    INT                 NOT NULL,
        [Description]   NVARCHAR(500)       NULL,

        CONSTRAINT [PK_widgets_Widget]
            PRIMARY KEY CLUSTERED ([WidgetId]),
        CONSTRAINT [FK_widgets_Widget_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [CK_widgets_Widget_WidgetType]
            CHECK ([WidgetType] IN ('content', 'chart', 'status-card', 'quick-action', 'workflow-pipeline', 'link-group'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'WidgetDefault')
BEGIN
    CREATE TABLE [widgets].[WidgetDefault]
    (
        [WidgetDefaultId]   INT IDENTITY(1,1)   NOT NULL,
        [JobTypeId]         INT                 NOT NULL,
        [RoleId]            NVARCHAR(450)       NOT NULL,
        [WidgetId]          INT                 NOT NULL,
        [CategoryId]        INT                 NOT NULL,
        [DisplayOrder]      INT                 NOT NULL    DEFAULT 0,
        [Config]            NVARCHAR(MAX)       NULL,

        CONSTRAINT [PK_widgets_WidgetDefault]
            PRIMARY KEY CLUSTERED ([WidgetDefaultId]),
        CONSTRAINT [FK_widgets_WidgetDefault_JobTypeId]
            FOREIGN KEY ([JobTypeId])
            REFERENCES [reference].[JobTypes] ([JobTypeId]),
        CONSTRAINT [FK_widgets_WidgetDefault_RoleId]
            FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_widgets_WidgetDefault_WidgetId]
            FOREIGN KEY ([WidgetId])
            REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_WidgetDefault_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget]
            UNIQUE ([JobTypeId], [RoleId], [WidgetId])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'widgets' AND t.name = 'JobWidget')
BEGIN
    CREATE TABLE [widgets].[JobWidget]
    (
        [JobWidgetId]   INT IDENTITY(1,1)       NOT NULL,
        [JobId]         UNIQUEIDENTIFIER        NOT NULL,
        [WidgetId]      INT                     NOT NULL,
        [RoleId]        NVARCHAR(450)           NOT NULL,
        [CategoryId]    INT                     NOT NULL,
        [DisplayOrder]  INT                     NOT NULL    DEFAULT 0,
        [IsEnabled]     BIT                     NOT NULL    DEFAULT 1,
        [Config]        NVARCHAR(MAX)           NULL,

        CONSTRAINT [PK_widgets_JobWidget]
            PRIMARY KEY CLUSTERED ([JobWidgetId]),
        CONSTRAINT [FK_widgets_JobWidget_JobId]
            FOREIGN KEY ([JobId])
            REFERENCES [Jobs].[Jobs] ([JobId]),
        CONSTRAINT [FK_widgets_JobWidget_WidgetId]
            FOREIGN KEY ([WidgetId])
            REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_JobWidget_RoleId]
            FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_widgets_JobWidget_CategoryId]
            FOREIGN KEY ([CategoryId])
            REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_JobWidget_Job_Widget_Role]
            UNIQUE ([JobId], [WidgetId], [RoleId])
    );
END
GO

-- ============================================================
-- PART 2: Seed Data (from Section 12)
-- ============================================================

SET NOCOUNT ON;

-- Categories (10)
MERGE [widgets].[WidgetCategory] AS target
USING (VALUES
    ('Content',                 'content',  NULL,                   0),
    ('Dashboard Charts',        'content',  NULL,                   1),
    ('Registration Overview',   'health',   'bi-people',            1),
    ('Financial Overview',      'health',   'bi-currency-dollar',   2),
    ('Scheduling Overview',     'health',   'bi-calendar-check',    3),
    ('Event Setup',             'action',   'bi-gear',              1),
    ('Registrations',           'action',   'bi-person-lines-fill', 2),
    ('Communication',           'action',   'bi-envelope',          3),
    ('Job Administration',      'action',   'bi-shield-lock',       4),
    ('Reports',                 'insight',  'bi-bar-chart',         1)
) AS source ([Name], [Section], [Icon], [DefaultOrder])
ON target.[Name] = source.[Name]
WHEN NOT MATCHED THEN
    INSERT ([Name], [Section], [Icon], [DefaultOrder])
    VALUES (source.[Name], source.[Section], source.[Icon], source.[DefaultOrder]);

-- Widgets (22: 17 scheduling + 3 chart + 2 content)
MERGE [widgets].[Widget] AS target
USING (
    SELECT s.[Name], s.[WidgetType], s.[ComponentKey], s.[Description], wc.[CategoryId]
    FROM (VALUES
        ('Client Banner',          'content',              'client-banner',            'Content',                  'Job banner with logo and images'),
        ('Bulletins',              'content',              'bulletins',                'Content',                  'Active job bulletins and announcements'),
        ('Player Registration Trend', 'chart',            'player-trend-chart',       'Dashboard Charts',         'Daily player registration counts and cumulative revenue over time'),
        ('Team Registration Trend',   'chart',            'team-trend-chart',         'Dashboard Charts',         'Daily team registration counts and cumulative revenue over time'),
        ('Age Group Distribution',    'chart',            'agegroup-distribution',    'Dashboard Charts',         'Player and team counts broken down by age group'),
        ('Registration Count',     'status-card',          'registration-status',      'Registration Overview',    'Club and team registration counts'),
        ('Financial Status',       'status-card',          'financial-status',         'Financial Overview',       'Payment status and outstanding balances'),
        ('Scheduling Status',      'status-card',          'scheduling-status',        'Scheduling Overview',      'Schedule completion status'),
        ('Scheduling Pipeline',    'workflow-pipeline',    'scheduling-pipeline',      'Event Setup',              'Step-by-step scheduling workflow'),
        ('Pool Assignment',        'quick-action',         'pool-assignment',          'Event Setup',              'Assign teams to pools'),
        ('Search Registrations',   'quick-action',         'search-registrations',     'Registrations',            'Search and manage registrations'),
        ('View by Club',           'quick-action',         'view-by-club',             'Registrations',            'Browse registrations grouped by club'),
        ('Compose Email',          'quick-action',         'compose-email',            'Communication',            'Send email to registrants'),
        ('Manage Bulletins',       'quick-action',         'manage-bulletins',         'Communication',            'Create and manage job bulletins'),
        ('LADT Editor',            'quick-action',         'ladt-editor',              'Event Setup',              'Configure leagues, age groups, divisions, and teams'),
        ('Roster Swapper',         'quick-action',         'roster-swapper',           'Registrations',            'Move players between rosters'),
        ('Discount Codes',         'quick-action',         'discount-codes',           'Registrations',            'Manage registration discount codes'),
        ('Cross-Job Financials',   'quick-action',         'cross-job-financials',     'Reports',                  'Revenue overview across all customer jobs'),
        ('Job Administrators',     'quick-action',         'job-administrators',       'Job Administration',       'Manage administrator access for this job'),
        ('Profile Editor',         'quick-action',         'profile-editor',           'Job Administration',       'Edit user profile data'),
        ('Profile Migration',      'quick-action',         'profile-migration',        'Job Administration',       'Migrate legacy user profiles'),
        ('Theme Editor',           'quick-action',         'theme-editor',             'Job Administration',       'Customize job theme and branding')
    ) AS s ([Name], [WidgetType], [ComponentKey], [CategoryName], [Description])
    INNER JOIN [widgets].[WidgetCategory] wc ON wc.[Name] = s.[CategoryName]
) AS source
ON target.[ComponentKey] = source.[ComponentKey]
WHEN NOT MATCHED THEN
    INSERT ([Name], [WidgetType], [ComponentKey], [CategoryId], [Description])
    VALUES (source.[Name], source.[WidgetType], source.[ComponentKey], source.[CategoryId], source.[Description]);

-- Role GUIDs
DECLARE @DirectorRoleId      NVARCHAR(450) = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06';
DECLARE @SuperDirectorRoleId NVARCHAR(450) = '7B9EB503-53C9-44FA-94A0-17760C512440';
DECLARE @SuperuserRoleId     NVARCHAR(450) = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9';

-- Config JSON lookup
DECLARE @WidgetConfigs TABLE (ComponentKey NVARCHAR(100), Config NVARCHAR(MAX));
INSERT INTO @WidgetConfigs VALUES
    ('registration-status',  '{"endpoint":"api/registrations/summary","label":"Registrations","icon":"bi-people","format":"count"}'),
    ('financial-status',     '{"endpoint":"api/payments/summary","label":"Financials","icon":"bi-currency-dollar","format":"currency"}'),
    ('scheduling-status',    '{"endpoint":"api/scheduling/status","label":"Schedule","icon":"bi-calendar-check","format":"status"}'),
    ('scheduling-pipeline',  '{"route":"admin/scheduling","icon":"bi-calendar-range"}'),
    ('pool-assignment',      '{"route":"admin/pool-assignment","label":"Pool Assignment","icon":"bi-diagram-3"}'),
    ('search-registrations', '{"route":"admin/search","label":"Search Registrations","icon":"bi-search"}'),
    ('view-by-club',         '{"route":"admin/team-search","label":"View by Club","icon":"bi-building"}'),
    ('compose-email',        '{"route":"email/compose","label":"Compose Email","icon":"bi-envelope-plus"}'),
    ('manage-bulletins',     '{"route":"bulletins/manage","label":"Manage Bulletins","icon":"bi-megaphone"}'),
    ('ladt-editor',          '{"route":"ladt/admin","label":"LADT Editor","icon":"bi-list-nested"}'),
    ('roster-swapper',       '{"route":"admin/roster-swapper","label":"Roster Swapper","icon":"bi-arrow-left-right"}'),
    ('discount-codes',       '{"route":"jobdiscountcodes/admin","label":"Discount Codes","icon":"bi-tags"}'),
    ('cross-job-financials', '{"route":"reporting/job-revenue","label":"Cross-Job Financials","icon":"bi-cash-stack"}'),
    ('job-administrators',   '{"route":"jobadministrator/admin","label":"Job Administrators","icon":"bi-person-gear"}'),
    ('profile-editor',       '{"route":"admin/profile-editor","label":"Profile Editor","icon":"bi-person-badge"}'),
    ('profile-migration',    '{"route":"admin/profile-migration","label":"Profile Migration","icon":"bi-arrow-repeat"}'),
    ('theme-editor',         '{"route":"admin/theme","label":"Theme Editor","icon":"bi-palette"}');

-- Director (15 widgets)
DECLARE @DirectorWidgets TABLE (ComponentKey NVARCHAR(100), DisplayOrder INT);
INSERT INTO @DirectorWidgets VALUES
    ('registration-status', 1), ('financial-status', 2), ('scheduling-status', 3),
    ('scheduling-pipeline', 1), ('pool-assignment', 2), ('ladt-editor', 3),
    ('search-registrations', 1), ('view-by-club', 2), ('roster-swapper', 3), ('discount-codes', 4),
    ('compose-email', 1), ('manage-bulletins', 2),
    ('player-trend-chart', 1), ('team-trend-chart', 2), ('agegroup-distribution', 3);

-- SuperDirector (16 widgets) = Director + cross-job financials
DECLARE @SuperDirectorWidgets TABLE (ComponentKey NVARCHAR(100), DisplayOrder INT);
INSERT INTO @SuperDirectorWidgets SELECT * FROM @DirectorWidgets;
INSERT INTO @SuperDirectorWidgets VALUES ('cross-job-financials', 1);

-- SuperUser (20 widgets) = SuperDirector + 4 admin tools
DECLARE @SuperuserWidgets TABLE (ComponentKey NVARCHAR(100), DisplayOrder INT);
INSERT INTO @SuperuserWidgets SELECT * FROM @SuperDirectorWidgets;
INSERT INTO @SuperuserWidgets VALUES
    ('job-administrators', 1), ('profile-editor', 2),
    ('profile-migration', 3), ('theme-editor', 4);

-- Seed defaults for all 3 roles × 2 job types (6 MERGE statements)
-- Director × JobType 2
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 2, @DirectorRoleId, w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @DirectorWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- Director × JobType 3
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 3, @DirectorRoleId, w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @DirectorWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- SuperDirector × JobType 2
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 2, @SuperDirectorRoleId, w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @SuperDirectorWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- SuperDirector × JobType 3
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 3, @SuperDirectorRoleId, w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @SuperDirectorWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- SuperUser × JobType 2
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 2, @SuperuserRoleId, w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @SuperuserWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- SuperUser × JobType 3
MERGE [widgets].[WidgetDefault] AS target
USING (
    SELECT 3, @SuperuserRoleId, w.[WidgetId], w.[CategoryId], dw.[DisplayOrder], wc.[Config]
    FROM @SuperuserWidgets dw
    INNER JOIN [widgets].[Widget] w ON w.[ComponentKey] = dw.[ComponentKey]
    LEFT JOIN @WidgetConfigs wc ON wc.[ComponentKey] = dw.[ComponentKey]
) AS source ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
ON target.[JobTypeId] = source.[JobTypeId] AND target.[RoleId] = source.[RoleId] AND target.[WidgetId] = source.[WidgetId]
WHEN NOT MATCHED THEN
    INSERT ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
    VALUES (source.[JobTypeId], source.[RoleId], source.[WidgetId], source.[CategoryId], source.[DisplayOrder], source.[Config]);

-- Anonymous defaults (2 content widgets × all job types)
DECLARE @AnonymousRoleId NVARCHAR(450) = 'CBF3F384-190F-4962-BF58-40B095628DC8';
DECLARE @BannerWidgetId INT = (SELECT WidgetId FROM [widgets].[Widget] WHERE ComponentKey = 'client-banner');
DECLARE @BulletinsWidgetId INT = (SELECT WidgetId FROM [widgets].[Widget] WHERE ComponentKey = 'bulletins');
DECLARE @ContentCatId INT = (SELECT CategoryId FROM [widgets].[WidgetCategory] WHERE Name = 'Content' AND Section = 'content');

INSERT INTO [widgets].[WidgetDefault] ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
SELECT jt.[JobTypeId], @AnonymousRoleId, @BannerWidgetId, @ContentCatId, 1, NULL
FROM [reference].[JobTypes] jt
WHERE NOT EXISTS (
    SELECT 1 FROM [widgets].[WidgetDefault] wd
    WHERE wd.[JobTypeId] = jt.[JobTypeId] AND wd.[RoleId] = @AnonymousRoleId AND wd.[WidgetId] = @BannerWidgetId
);

INSERT INTO [widgets].[WidgetDefault] ([JobTypeId], [RoleId], [WidgetId], [CategoryId], [DisplayOrder], [Config])
SELECT jt.[JobTypeId], @AnonymousRoleId, @BulletinsWidgetId, @ContentCatId, 2, NULL
FROM [reference].[JobTypes] jt
WHERE NOT EXISTS (
    SELECT 1 FROM [widgets].[WidgetDefault] wd
    WHERE wd.[JobTypeId] = jt.[JobTypeId] AND wd.[RoleId] = @AnonymousRoleId AND wd.[WidgetId] = @BulletinsWidgetId
);

-- Summary
PRINT '';
PRINT '=== Widget Dashboard Setup Complete ===';
SELECT 'Categories' AS [Table], COUNT(*) AS [Count] FROM [widgets].[WidgetCategory]
UNION ALL SELECT 'Widgets', COUNT(*) FROM [widgets].[Widget]
UNION ALL SELECT 'Defaults', COUNT(*) FROM [widgets].[WidgetDefault]
UNION ALL SELECT 'JobWidgets', COUNT(*) FROM [widgets].[JobWidget];

SET NOCOUNT OFF;
```

</details>
