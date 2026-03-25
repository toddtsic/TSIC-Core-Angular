# ============================================================================
# 0-Restore-DevConfig-DEV.ps1
#
# Run this IN DEV to export the current dev configuration as a deployable SQL
# script. The output is "0-Restore-DevConfig-PROD.sql" — run THAT against any
# target database (fresh, prod restore, etc.) to stand up all new-system config.
#
# What it exports:
#   Section 1 — Schema DDL (schemas, tables, columns, constraints)
#   Section 2 — Data from: widgets.*, nav.*, stores.StoreItemImage
#
# Safety:
#   * Output SQL is 100% idempotent
#   * Schema changes use IF NOT EXISTS
#   * Data seeding targets ONLY new-system tables
#   * Legacy tables (Jobs, Registrations, Users, Teams) are UNTOUCHED
#   * ZERO writes to legacy tables
#
# Usage:
#   .\scripts\0-Restore-DevConfig-DEV.ps1                          # uses appsettings
#   .\scripts\0-Restore-DevConfig-DEV.ps1 -ConnectionString "..."  # explicit
#
# Workflow:
#   1. Configure widgets/navs/store in dev via their respective editors
#   2. Run this script to snapshot dev config
#   3. Give Todd the output SQL. He runs it after restoring a prod backup.
# ============================================================================

param(
    [string]$ConnectionString
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve connection string ──
if (-not $ConnectionString) {
    $apiDir = Join-Path $PSScriptRoot '..\TSIC-Core-Angular\src\backend\TSIC.API'
    $settingsFile = Join-Path $apiDir 'appsettings.Development.json'
    if (-not (Test-Path $settingsFile)) {
        $settingsFile = Join-Path $apiDir 'appsettings.json'
    }
    if (-not (Test-Path $settingsFile)) {
        Write-Error "Cannot find appsettings. Use -ConnectionString parameter."
        return
    }
    $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json
    $ConnectionString = $settings.ConnectionStrings.DefaultConnection
    Write-Host "Using connection: $($ConnectionString -replace 'Password=[^;]+','Password=***')" -ForegroundColor Cyan
}

$OutputPath = Join-Path $PSScriptRoot '0-Restore-DevConfig-PROD.sql'

# ── Helpers ──
function Invoke-Sql {
    param([string]$Query)
    $conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $Query
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
    $table = New-Object System.Data.DataTable
    $adapter.Fill($table) | Out-Null
    $conn.Close()
    return ,$table  # comma prevents PowerShell from unwrapping DataTable rows
}

function Esc([string]$val) {
    if ($null -eq $val) { return 'NULL' }
    return "N'" + $val.Replace("'", "''") + "'"
}

function StrOrNull($val) {
    if ($null -eq $val -or $val -is [System.DBNull] -or $val -eq '') { return 'NULL' }
    return Esc $val
}

function IntOrNull($val) {
    if ($null -eq $val -or $val -is [System.DBNull]) { return 'NULL' }
    return [string]$val
}

function GuidOrNull($val) {
    if ($null -eq $val -or $val -is [System.DBNull]) { return 'NULL' }
    return "'" + $val.ToString() + "'"
}

function BitOrNull($val) {
    if ($null -eq $val -or $val -is [System.DBNull]) { return 'NULL' }
    if ($val) { return '1' } else { return '0' }
}

function Invoke-SqlNonQuery {
    param([string]$Query)
    $conn = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $Query
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
}

# ── Ensure schemas & tables exist in dev DB ──
# After a prod restore, these schemas won't exist yet. Create them so the
# export queries below don't fail. Data will be empty, which is fine —
# the generated PROD script will just have 0 rows for those sections.
Write-Host ""
Write-Host "Ensuring schemas & tables exist in dev DB..." -ForegroundColor Cyan

$ensureDdl = @"
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'widgets')
    EXEC('CREATE SCHEMA [widgets] AUTHORIZATION [dbo]');

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'WidgetCategory')
    CREATE TABLE [widgets].[WidgetCategory] (
        [CategoryId]   INT IDENTITY(1,1) NOT NULL,
        [Name]         NVARCHAR(100)     NOT NULL,
        [Workspace]    NVARCHAR(20)      NOT NULL,
        [Icon]         NVARCHAR(50)      NULL,
        [DefaultOrder] INT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_widgets_WidgetCategory] PRIMARY KEY CLUSTERED ([CategoryId])
    );

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'Widget')
    CREATE TABLE [widgets].[Widget] (
        [WidgetId]      INT IDENTITY(1,1) NOT NULL,
        [Name]          NVARCHAR(100)     NOT NULL,
        [WidgetType]    NVARCHAR(30)      NOT NULL,
        [ComponentKey]  NVARCHAR(100)     NOT NULL,
        [CategoryId]    INT               NOT NULL,
        [Description]   NVARCHAR(500)     NULL,
        [DefaultConfig] NVARCHAR(MAX)     NULL,
        CONSTRAINT [PK_widgets_Widget] PRIMARY KEY CLUSTERED ([WidgetId]),
        CONSTRAINT [FK_widgets_Widget_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [CK_widgets_Widget_WidgetType] CHECK ([WidgetType] IN ('content','chart-tile','status-tile'))
    );

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'WidgetDefault')
    CREATE TABLE [widgets].[WidgetDefault] (
        [WidgetDefaultId] INT IDENTITY(1,1) NOT NULL,
        [JobTypeId]       INT               NOT NULL,
        [RoleId]          NVARCHAR(450)     NOT NULL,
        [WidgetId]        INT               NOT NULL,
        [CategoryId]      INT               NOT NULL,
        [DisplayOrder]    INT               NOT NULL DEFAULT 0,
        [Config]          NVARCHAR(MAX)     NULL,
        CONSTRAINT [PK_widgets_WidgetDefault] PRIMARY KEY CLUSTERED ([WidgetDefaultId]),
        CONSTRAINT [FK_widgets_WidgetDefault_JobTypeId] FOREIGN KEY ([JobTypeId]) REFERENCES [reference].[JobTypes] ([JobTypeId]),
        CONSTRAINT [FK_widgets_WidgetDefault_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_widgets_WidgetDefault_WidgetId] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_WidgetDefault_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category] UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId])
    );

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'JobWidget')
    CREATE TABLE [widgets].[JobWidget] (
        [JobWidgetId]  INT IDENTITY(1,1)    NOT NULL,
        [JobId]        UNIQUEIDENTIFIER     NOT NULL,
        [WidgetId]     INT                  NOT NULL,
        [RoleId]       NVARCHAR(450)        NOT NULL,
        [CategoryId]   INT                  NOT NULL,
        [DisplayOrder] INT                  NOT NULL DEFAULT 0,
        [IsEnabled]    BIT                  NOT NULL DEFAULT 1,
        [Config]       NVARCHAR(MAX)        NULL,
        CONSTRAINT [PK_widgets_JobWidget] PRIMARY KEY CLUSTERED ([JobWidgetId]),
        CONSTRAINT [FK_widgets_JobWidget_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs] ([JobId]),
        CONSTRAINT [FK_widgets_JobWidget_WidgetId] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_JobWidget_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_widgets_JobWidget_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_JobWidget_Job_Widget_Role] UNIQUE ([JobId], [WidgetId], [RoleId])
    );

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'UserWidget')
    CREATE TABLE [widgets].[UserWidget] (
        [UserWidgetId]   INT IDENTITY(1,1)    NOT NULL,
        [RegistrationId] UNIQUEIDENTIFIER     NOT NULL,
        [WidgetId]       INT                  NOT NULL,
        [CategoryId]     INT                  NOT NULL,
        [DisplayOrder]   INT                  NOT NULL DEFAULT 0,
        [IsHidden]       BIT                  NOT NULL DEFAULT 0,
        [Config]         NVARCHAR(MAX)        NULL,
        CONSTRAINT [PK_widgets_UserWidget] PRIMARY KEY CLUSTERED ([UserWidgetId]),
        CONSTRAINT [FK_widgets_UserWidget_Widget] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),
        CONSTRAINT [FK_widgets_UserWidget_Category] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),
        CONSTRAINT [UQ_widgets_UserWidget_Reg_Widget] UNIQUE ([RegistrationId], [WidgetId])
    );

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')
    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'Nav')
BEGIN
    CREATE TABLE [nav].[Nav] (
        [NavId]      INT IDENTITY(1,1)  NOT NULL,
        [RoleId]     NVARCHAR(450)      NOT NULL,
        [JobId]      UNIQUEIDENTIFIER   NULL,
        [Active]     BIT                NOT NULL DEFAULT 1,
        [Modified]   DATETIME2          NOT NULL DEFAULT GETDATE(),
        [ModifiedBy] NVARCHAR(450)      NULL,
        CONSTRAINT [PK_nav_Nav] PRIMARY KEY CLUSTERED ([NavId]),
        CONSTRAINT [FK_nav_Nav_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),
        CONSTRAINT [FK_nav_Nav_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs] ([JobId]),
        CONSTRAINT [FK_nav_Nav_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])
    );
    CREATE UNIQUE INDEX [UQ_nav_Nav_Role_Job] ON [nav].[Nav] ([RoleId], [JobId]) WHERE [JobId] IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'NavItem')
    CREATE TABLE [nav].[NavItem] (
        [NavItemId]       INT IDENTITY(1,1) NOT NULL,
        [NavId]           INT               NOT NULL,
        [ParentNavItemId] INT               NULL,
        [Active]          BIT               NOT NULL DEFAULT 1,
        [SortOrder]       INT               NOT NULL DEFAULT 0,
        [Text]            NVARCHAR(200)     NOT NULL,
        [IconName]        NVARCHAR(100)     NULL,
        [RouterLink]      NVARCHAR(500)     NULL,
        [NavigateUrl]     NVARCHAR(500)     NULL,
        [Target]          NVARCHAR(20)      NULL,
        [Modified]        DATETIME2         NOT NULL DEFAULT GETDATE(),
        [ModifiedBy]      NVARCHAR(450)     NULL,
        CONSTRAINT [PK_nav_NavItem] PRIMARY KEY CLUSTERED ([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_NavId] FOREIGN KEY ([NavId]) REFERENCES [nav].[Nav] ([NavId]) ON DELETE CASCADE,
        CONSTRAINT [FK_nav_NavItem_ParentNavItemId] FOREIGN KEY ([ParentNavItemId]) REFERENCES [nav].[NavItem] ([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])
    );

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'logs')
    EXEC('CREATE SCHEMA logs');

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'logs' AND t.name = 'AppLog')
    CREATE TABLE logs.AppLog (
        Id            bigint IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
        TimeStamp     datetimeoffset(7)    NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        Level         nvarchar(16)         NOT NULL,
        Message       nvarchar(max)        NULL,
        Exception     nvarchar(max)        NULL,
        Properties    nvarchar(max)        NULL,
        SourceContext  nvarchar(512)        NULL,
        RequestPath   nvarchar(512)        NULL,
        StatusCode    int                  NULL,
        Elapsed       float                NULL
    );

IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'stores' AND t.name = 'StoreItemImage')
    CREATE TABLE stores.StoreItemImage (
        StoreItemImageId INT IDENTITY(1,1) PRIMARY KEY,
        StoreItemId      INT NOT NULL,
        ImageUrl         NVARCHAR(500) NOT NULL,
        DisplayOrder     INT NOT NULL DEFAULT 0,
        AltText          NVARCHAR(200) NULL,
        Modified         DATETIME NOT NULL DEFAULT GETUTCDATE(),
        LebUserId        NVARCHAR(128) NOT NULL,
        CONSTRAINT FK_StoreItemImage_StoreItem FOREIGN KEY (StoreItemId) REFERENCES stores.StoreItems(StoreItemId)
    );

IF COL_LENGTH('Jobs.Jobs', 'AdultProfileMetadataJson') IS NULL
    ALTER TABLE Jobs.Jobs ADD AdultProfileMetadataJson NVARCHAR(MAX) NULL;
"@

Invoke-SqlNonQuery -Query $ensureDdl
Write-Host "  Schemas & tables verified." -ForegroundColor Gray

# ── Query dev DB ──
Write-Host ""
Write-Host "Querying dev database..." -ForegroundColor Cyan

# Widgets
$categories = Invoke-Sql "SELECT CategoryId, Name, Workspace, Icon, DefaultOrder FROM widgets.WidgetCategory ORDER BY CategoryId"
$widgets    = Invoke-Sql "SELECT WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig FROM widgets.Widget ORDER BY WidgetId"
$defaults   = Invoke-Sql "SELECT WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config FROM widgets.WidgetDefault ORDER BY WidgetDefaultId"
$jobWidgets = Invoke-Sql "SELECT JobWidgetId, JobId, WidgetId, RoleId, CategoryId, DisplayOrder, IsEnabled, Config FROM widgets.JobWidget ORDER BY JobWidgetId"

Write-Host "  Widgets:     $($categories.Rows.Count) categories, $($widgets.Rows.Count) widgets, $($defaults.Rows.Count) defaults, $($jobWidgets.Rows.Count) job overrides" -ForegroundColor Gray

# Nav — generated from inline route manifest (not read from DB)
# This ensures nav is always correct regardless of dev DB state.
# Manifest mirrors app.routes.ts guard data + display metadata.

$menuManifest = @(
    # Search (requireAdmin)
    @{ Controller='Search'; Icon='search'; Sort=1; Action='Registrations'; ActionIcon='people'; Route='search/registrations'; ActionSort=1; Guard='admin' }
    @{ Controller='Search'; Icon='search'; Sort=1; Action='Teams'; ActionIcon='shield'; Route='search/teams'; ActionSort=2; Guard='admin' }
    # Configure (mixed guards)
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Job'; ActionIcon='briefcase'; Route='configure/job'; ActionSort=1; Guard='admin' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Age Ranges'; ActionIcon='sliders'; Route='configure/age-ranges'; ActionSort=2; Guard='admin' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Discount Codes'; ActionIcon='tags'; Route='configure/discount-codes'; ActionSort=3; Guard='admin' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Uniform Upload'; ActionIcon='upload'; Route='configure/uniform-upload'; ActionSort=4; Guard='admin' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Administrators'; ActionIcon='person-badge'; Route='configure/administrators'; ActionSort=10; Guard='superuser' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Customer Groups'; ActionIcon='people'; Route='configure/customer-groups'; ActionSort=11; Guard='superuser' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Dropdown Options'; ActionIcon='list'; Route='configure/ddl-options'; ActionSort=12; Guard='superuser' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Customers'; ActionIcon='building'; Route='configure/customers'; ActionSort=13; Guard='superuser' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Theme'; ActionIcon='palette'; Route='configure/theme'; ActionSort=14; Guard='superuser' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Menus'; ActionIcon='list'; Route='configure/nav-editor'; ActionSort=15; Guard='superuser' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Widgets'; ActionIcon='grid'; Route='configure/widget-editor'; ActionSort=16; Guard='superuser' }
    @{ Controller='Configure'; Icon='gear'; Sort=2; Action='Job Clone'; ActionIcon='copy'; Route='configure/job-clone'; ActionSort=17; Guard='superuser' }
    # Communications (requireAdmin)
    @{ Controller='Communications'; Icon='envelope'; Sort=3; Action='Bulletins'; ActionIcon='megaphone'; Route='communications/bulletins'; ActionSort=1; Guard='admin' }
    @{ Controller='Communications'; Icon='envelope'; Sort=3; Action='Email Log'; ActionIcon='envelope-open'; Route='communications/email-log'; ActionSort=2; Guard='admin' }
    @{ Controller='Communications'; Icon='envelope'; Sort=3; Action='Push Notification'; ActionIcon='bell'; Route='communications/push-notification'; ActionSort=3; Guard='admin' }
    # LADT (requireAdmin)
    @{ Controller='LADT'; Icon='diagram-3'; Sort=4; Action='Editor'; ActionIcon='pencil'; Route='ladt/editor'; ActionSort=1; Guard='admin' }
    @{ Controller='LADT'; Icon='diagram-3'; Sort=4; Action='Roster Swapper'; ActionIcon='arrow-left-right'; Route='ladt/roster-swapper'; ActionSort=2; Guard='admin' }
    @{ Controller='LADT'; Icon='diagram-3'; Sort=4; Action='Pool Assignment'; ActionIcon='people'; Route='ladt/pool-assignment'; ActionSort=3; Guard='admin' }
    # Scheduling (requireAdmin)
    @{ Controller='Scheduling'; Icon='calendar'; Sort=5; Action='Schedule Hub'; ActionIcon='grid'; Route='scheduling/schedule-hub'; ActionSort=1; Guard='admin' }
    @{ Controller='Scheduling'; Icon='calendar'; Sort=5; Action='View Schedule'; ActionIcon='eye'; Route='scheduling/view-schedule'; ActionSort=2; Guard='admin' }
    @{ Controller='Scheduling'; Icon='calendar'; Sort=5; Action='Rescheduler'; ActionIcon='arrow-repeat'; Route='scheduling/rescheduler'; ActionSort=3; Guard='admin' }
    @{ Controller='Scheduling'; Icon='calendar'; Sort=5; Action='Mobile Scorers'; ActionIcon='phone'; Route='scheduling/mobile-scorers'; ActionSort=4; Guard='admin' }
    # ARB (requireAdmin)
    @{ Controller='ARB'; Icon='credit-card'; Sort=6; Action='Health Check'; ActionIcon='heart-pulse'; Route='arb/health'; ActionSort=1; Guard='admin' }
    # Tools (mixed guards)
    @{ Controller='Tools'; Icon='tools'; Sort=7; Action='US Lax Tester'; ActionIcon='check-circle'; Route='tools/uslax-test'; ActionSort=1; Guard='admin' }
    @{ Controller='Tools'; Icon='tools'; Sort=7; Action='US Lax Rankings'; ActionIcon='trophy'; Route='tools/uslax-rankings'; ActionSort=2; Guard='admin' }
    @{ Controller='Tools'; Icon='tools'; Sort=7; Action='Profile Migration'; ActionIcon='arrow-right'; Route='tools/profile-migration'; ActionSort=10; Guard='superuser' }
    @{ Controller='Tools'; Icon='tools'; Sort=7; Action='Profile Editor'; ActionIcon='pencil-square'; Route='tools/profile-editor'; ActionSort=11; Guard='superuser' }
    @{ Controller='Tools'; Icon='tools'; Sort=7; Action='Change Password'; ActionIcon='key'; Route='tools/change-password'; ActionSort=12; Guard='superuser' }
    @{ Controller='Tools'; Icon='tools'; Sort=7; Action='Job Revenue'; ActionIcon='cash-stack'; Route='tools/customer-job-revenue'; ActionSort=13; Guard='superuser' }
    # Store (requireAdmin)
    @{ Controller='Store'; Icon='cart'; Sort=8; Action='Store Admin'; ActionIcon='shop'; Route='store/admin'; ActionSort=1; Guard='admin' }
)

# Admin roles get menu items based on guard level; non-admin roles get Nav record only (dashboard-only)
$adminRoles = @(
    @{ RoleId='FF4D1C27-F6DA-4745-98CC-D7E8121A5D06'; GuardLevel='admin';     Name='Director' }
    @{ RoleId='7B9EB503-53C9-44FA-94A0-17760C512440'; GuardLevel='admin';     Name='SuperDirector' }
    @{ RoleId='CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9'; GuardLevel='superuser'; Name='Superuser' }
    @{ RoleId='1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA'; GuardLevel='admin';     Name='Staff' }
    @{ RoleId='122075A3-2C42-4092-97F1-9673DF5B6A2C'; GuardLevel='admin';     Name='RefAssignor' }
    @{ RoleId='5B9B7055-4530-4E46-B403-1019FD8B8418'; GuardLevel='admin';     Name='StoreAdmin' }
)
$nonAdminRoles = @(
    @{ RoleId='E0A8A5C3-A36C-417F-8312-E7083F1AA5A0'; Name='Family' }
    @{ RoleId='DAC0C570-94AA-4A88-8D73-6034F1F72F3A'; Name='Player' }
    @{ RoleId='6A26171F-4D94-4928-94FA-2FEFD42C3C3E'; Name='ClubRep' }
    @{ RoleId='CE2CB370-5880-4624-A43E-048379C64331'; Name='UnassignedAdult' }
)
$allRoles = $adminRoles + $nonAdminRoles

# Pre-compute nav stats for console output
$navRoleCount = $allRoles.Count
$navParentCount = 0
$navChildCount = 0
foreach ($role in $adminRoles) {
    $gl = $role.GuardLevel
    $visible = @($menuManifest | Where-Object { $_.Guard -eq 'admin' -or ($gl -eq 'superuser' -and $_.Guard -eq 'superuser') })
    $ctrlGroups = $visible | Group-Object { $_.Controller }
    $navParentCount += $ctrlGroups.Count
    $navChildCount += $visible.Count
}
$navItemCount = $navParentCount + $navChildCount
Write-Host "  Nav:         $navRoleCount navs, $navItemCount items ($navParentCount parents, $navChildCount children) [from route manifest]" -ForegroundColor Gray

# Store images
$storeImages = Invoke-Sql "SELECT img.StoreItemId, img.ImageUrl, img.DisplayOrder, si.StoreId FROM stores.StoreItemImage img JOIN stores.StoreItems si ON img.StoreItemId = si.StoreItemId ORDER BY si.StoreId, img.StoreItemId, img.DisplayOrder"

Write-Host "  Store:       $($storeImages.Rows.Count) images" -ForegroundColor Gray

# ── Build output SQL ──
$sb = [System.Text.StringBuilder]::new()

$timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

$sb.AppendLine("-- ============================================================================") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("--   0-Restore-DevConfig-PROD.sql") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("--   RUN THIS IMMEDIATELY AFTER RESTORING A PRODUCTION BACKUP.") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("--   Todd -- this is the ONE script. Run it, verify the summary at the") | Out-Null
$sb.AppendLine("--   bottom, you're done.") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("-- ============================================================================") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("-- Generated: $timestamp") | Out-Null
$sb.AppendLine("-- Source:    0-Restore-DevConfig-DEV.ps1 (widgets/store from dev DB, nav from route manifest)") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("-- WHAT THIS DOES:") | Out-Null
$sb.AppendLine("--   Section 1 -- Creates schemas/tables/columns that don't exist yet") | Out-Null
$sb.AppendLine("--   Section 2 -- Seeds dev configuration into new-system tables only") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("-- SAFETY GUARANTEES:") | Out-Null
$sb.AppendLine("--   * 100% idempotent -- safe to run multiple times") | Out-Null
$sb.AppendLine("--   * ZERO writes to legacy tables (Jobs, Registrations, Users, Teams)") | Out-Null
$sb.AppendLine("--   * Schema changes use IF NOT EXISTS") | Out-Null
$sb.AppendLine("--   * Data targets ONLY: widgets.*, nav.*, logs.*, stores.StoreItemImage") | Out-Null
$sb.AppendLine("--   * ZERO writes to legacy tables") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("-- Snapshot: $($categories.Rows.Count) widget categories, $($widgets.Rows.Count) widgets, $($defaults.Rows.Count) defaults, $($jobWidgets.Rows.Count) job overrides") | Out-Null
$sb.AppendLine("--           $navRoleCount navs, $navItemCount nav items [from route manifest]") | Out-Null
$sb.AppendLine("--           $($storeImages.Rows.Count) store images") | Out-Null
$sb.AppendLine("--") | Out-Null
$sb.AppendLine("-- Prerequisites: reference.JobTypes + dbo.AspNetRoles + dbo.AspNetUsers populated") | Out-Null
$sb.AppendLine("-- ============================================================================") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("SET NOCOUNT ON;") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '';") | Out-Null
$sb.AppendLine("PRINT '==========================================================';") | Out-Null
$sb.AppendLine("PRINT '  0-Restore-DevConfig-PROD.sql';") | Out-Null
$sb.AppendLine("PRINT '  Generated: $timestamp';") | Out-Null
$sb.AppendLine("PRINT '==========================================================';") | Out-Null
$sb.AppendLine("PRINT '';") | Out-Null
$sb.AppendLine("") | Out-Null

# ████████████████████████████████████████████████████████████████████
# SECTION 1: SCHEMA SAFETY NET
# ████████████████████████████████████████████████████████████████████

$sb.AppendLine("-- ========================================================================") | Out-Null
$sb.AppendLine("-- SECTION 1: SCHEMA SAFETY NET") | Out-Null
$sb.AppendLine("-- Creates schemas, tables, columns that don't exist yet.") | Out-Null
$sb.AppendLine("-- Every statement is IF NOT EXISTS -- completely safe on any DB state.") | Out-Null
$sb.AppendLine("-- ========================================================================") | Out-Null
$sb.AppendLine("") | Out-Null

# ── 1A: widgets schema + tables ──
$sb.AppendLine("PRINT '-- 1A: widgets schema + tables';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'widgets')") | Out-Null
$sb.AppendLine("    EXEC('CREATE SCHEMA [widgets] AUTHORIZATION [dbo]');") | Out-Null
$sb.AppendLine("") | Out-Null

# WidgetCategory
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'WidgetCategory')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE [widgets].[WidgetCategory] (") | Out-Null
$sb.AppendLine("        [CategoryId]   INT IDENTITY(1,1) NOT NULL,") | Out-Null
$sb.AppendLine("        [Name]         NVARCHAR(100)     NOT NULL,") | Out-Null
$sb.AppendLine("        [Workspace]    NVARCHAR(20)      NOT NULL,") | Out-Null
$sb.AppendLine("        [Icon]         NVARCHAR(50)      NULL,") | Out-Null
$sb.AppendLine("        [DefaultOrder] INT               NOT NULL DEFAULT 0,") | Out-Null
$sb.AppendLine("        CONSTRAINT [PK_widgets_WidgetCategory] PRIMARY KEY CLUSTERED ([CategoryId])") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null

# Widget
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'Widget')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE [widgets].[Widget] (") | Out-Null
$sb.AppendLine("        [WidgetId]      INT IDENTITY(1,1) NOT NULL,") | Out-Null
$sb.AppendLine("        [Name]          NVARCHAR(100)     NOT NULL,") | Out-Null
$sb.AppendLine("        [WidgetType]    NVARCHAR(30)      NOT NULL,") | Out-Null
$sb.AppendLine("        [ComponentKey]  NVARCHAR(100)     NOT NULL,") | Out-Null
$sb.AppendLine("        [CategoryId]    INT               NOT NULL,") | Out-Null
$sb.AppendLine("        [Description]   NVARCHAR(500)     NULL,") | Out-Null
$sb.AppendLine("        [DefaultConfig] NVARCHAR(MAX)     NULL,") | Out-Null
$sb.AppendLine("        CONSTRAINT [PK_widgets_Widget] PRIMARY KEY CLUSTERED ([WidgetId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_Widget_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [CK_widgets_Widget_WidgetType] CHECK ([WidgetType] IN ('content','chart-tile','status-tile'))") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null

# WidgetDefault
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'WidgetDefault')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE [widgets].[WidgetDefault] (") | Out-Null
$sb.AppendLine("        [WidgetDefaultId] INT IDENTITY(1,1) NOT NULL,") | Out-Null
$sb.AppendLine("        [JobTypeId]       INT               NOT NULL,") | Out-Null
$sb.AppendLine("        [RoleId]          NVARCHAR(450)     NOT NULL,") | Out-Null
$sb.AppendLine("        [WidgetId]        INT               NOT NULL,") | Out-Null
$sb.AppendLine("        [CategoryId]      INT               NOT NULL,") | Out-Null
$sb.AppendLine("        [DisplayOrder]    INT               NOT NULL DEFAULT 0,") | Out-Null
$sb.AppendLine("        [Config]          NVARCHAR(MAX)     NULL,") | Out-Null
$sb.AppendLine("        CONSTRAINT [PK_widgets_WidgetDefault] PRIMARY KEY CLUSTERED ([WidgetDefaultId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_WidgetDefault_JobTypeId] FOREIGN KEY ([JobTypeId]) REFERENCES [reference].[JobTypes] ([JobTypeId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_WidgetDefault_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_WidgetDefault_WidgetId] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_WidgetDefault_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category] UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId])") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null

# JobWidget
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'JobWidget')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE [widgets].[JobWidget] (") | Out-Null
$sb.AppendLine("        [JobWidgetId]  INT IDENTITY(1,1)    NOT NULL,") | Out-Null
$sb.AppendLine("        [JobId]        UNIQUEIDENTIFIER     NOT NULL,") | Out-Null
$sb.AppendLine("        [WidgetId]     INT                  NOT NULL,") | Out-Null
$sb.AppendLine("        [RoleId]       NVARCHAR(450)        NOT NULL,") | Out-Null
$sb.AppendLine("        [CategoryId]   INT                  NOT NULL,") | Out-Null
$sb.AppendLine("        [DisplayOrder] INT                  NOT NULL DEFAULT 0,") | Out-Null
$sb.AppendLine("        [IsEnabled]    BIT                  NOT NULL DEFAULT 1,") | Out-Null
$sb.AppendLine("        [Config]       NVARCHAR(MAX)        NULL,") | Out-Null
$sb.AppendLine("        CONSTRAINT [PK_widgets_JobWidget] PRIMARY KEY CLUSTERED ([JobWidgetId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_JobWidget_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs] ([JobId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_JobWidget_WidgetId] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_JobWidget_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_JobWidget_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [UQ_widgets_JobWidget_Job_Widget_Role] UNIQUE ([JobId], [WidgetId], [RoleId])") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null

# UserWidget
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'UserWidget')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE [widgets].[UserWidget] (") | Out-Null
$sb.AppendLine("        [UserWidgetId]   INT IDENTITY(1,1)    NOT NULL,") | Out-Null
$sb.AppendLine("        [RegistrationId] UNIQUEIDENTIFIER     NOT NULL,") | Out-Null
$sb.AppendLine("        [WidgetId]       INT                  NOT NULL,") | Out-Null
$sb.AppendLine("        [CategoryId]     INT                  NOT NULL,") | Out-Null
$sb.AppendLine("        [DisplayOrder]   INT                  NOT NULL DEFAULT 0,") | Out-Null
$sb.AppendLine("        [IsHidden]       BIT                  NOT NULL DEFAULT 0,") | Out-Null
$sb.AppendLine("        [Config]         NVARCHAR(MAX)        NULL,") | Out-Null
$sb.AppendLine("        CONSTRAINT [PK_widgets_UserWidget] PRIMARY KEY CLUSTERED ([UserWidgetId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_UserWidget_Widget] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_widgets_UserWidget_Category] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [UQ_widgets_UserWidget_Reg_Widget] UNIQUE ([RegistrationId], [WidgetId])") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null

# Widget schema migrations
$sb.AppendLine("-- Prod backup compatibility: Section -> Workspace") | Out-Null
$sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Section')") | Out-Null
$sb.AppendLine("    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Section];") | Out-Null
$sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')") | Out-Null
$sb.AppendLine("    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Workspace];") | Out-Null
$sb.AppendLine("IF COL_LENGTH('widgets.WidgetCategory', 'Section') IS NOT NULL AND COL_LENGTH('widgets.WidgetCategory', 'Workspace') IS NULL") | Out-Null
$sb.AppendLine("    EXEC sp_rename 'widgets.WidgetCategory.Section', 'Workspace', 'COLUMN';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF COL_LENGTH('widgets.Widget', 'DefaultConfig') IS NULL") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    ALTER TABLE [widgets].[Widget] ADD [DefaultConfig] NVARCHAR(MAX) NULL;") | Out-Null
$sb.AppendLine("    PRINT '  Added column: widgets.Widget.DefaultConfig';") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_widgets_WidgetDefault_JobType_Role_Widget' AND object_id = OBJECT_ID('widgets.WidgetDefault'))") | Out-Null
$sb.AppendLine("    ALTER TABLE [widgets].[WidgetDefault] DROP CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget];") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_widgets_WidgetDefault_JobType_Role_Widget_Category' AND object_id = OBJECT_ID('widgets.WidgetDefault'))") | Out-Null
$sb.AppendLine("    ALTER TABLE [widgets].[WidgetDefault] ADD CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category] UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId]);") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')") | Out-Null
$sb.AppendLine("    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];") | Out-Null
$sb.AppendLine("ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]") | Out-Null
$sb.AppendLine("    CHECK ([WidgetType] IN ('content','chart-tile','status-tile'));") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')") | Out-Null
$sb.AppendLine("    ALTER TABLE [widgets].[WidgetCategory] ADD CONSTRAINT [CK_widgets_WidgetCategory_Workspace]") | Out-Null
$sb.AppendLine("        CHECK ([Workspace] IN ('dashboard','public'));") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '  1A complete: widgets';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

# ── 1B: nav schema + tables ──
$sb.AppendLine("PRINT '-- 1B: nav schema + tables';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')") | Out-Null
$sb.AppendLine("    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'Nav')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE [nav].[Nav] (") | Out-Null
$sb.AppendLine("        [NavId]      INT IDENTITY(1,1)  NOT NULL,") | Out-Null
$sb.AppendLine("        [RoleId]     NVARCHAR(450)      NOT NULL,") | Out-Null
$sb.AppendLine("        [JobId]      UNIQUEIDENTIFIER   NULL,") | Out-Null
$sb.AppendLine("        [Active]     BIT                NOT NULL DEFAULT 1,") | Out-Null
$sb.AppendLine("        [Modified]   DATETIME2          NOT NULL DEFAULT GETDATE(),") | Out-Null
$sb.AppendLine("        [ModifiedBy] NVARCHAR(450)      NULL,") | Out-Null
$sb.AppendLine("        CONSTRAINT [PK_nav_Nav] PRIMARY KEY CLUSTERED ([NavId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_nav_Nav_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_nav_Nav_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs] ([JobId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_nav_Nav_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("    CREATE UNIQUE INDEX [UQ_nav_Nav_Role_Job] ON [nav].[Nav] ([RoleId], [JobId]) WHERE [JobId] IS NOT NULL;") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'NavItem')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE [nav].[NavItem] (") | Out-Null
$sb.AppendLine("        [NavItemId]       INT IDENTITY(1,1) NOT NULL,") | Out-Null
$sb.AppendLine("        [NavId]           INT               NOT NULL,") | Out-Null
$sb.AppendLine("        [ParentNavItemId] INT               NULL,") | Out-Null
$sb.AppendLine("        [Active]          BIT               NOT NULL DEFAULT 1,") | Out-Null
$sb.AppendLine("        [SortOrder]       INT               NOT NULL DEFAULT 0,") | Out-Null
$sb.AppendLine("        [Text]            NVARCHAR(200)     NOT NULL,") | Out-Null
$sb.AppendLine("        [IconName]        NVARCHAR(100)     NULL,") | Out-Null
$sb.AppendLine("        [RouterLink]      NVARCHAR(500)     NULL,") | Out-Null
$sb.AppendLine("        [NavigateUrl]     NVARCHAR(500)     NULL,") | Out-Null
$sb.AppendLine("        [Target]          NVARCHAR(20)      NULL,") | Out-Null
$sb.AppendLine("        [Modified]        DATETIME2         NOT NULL DEFAULT GETDATE(),") | Out-Null
$sb.AppendLine("        [ModifiedBy]      NVARCHAR(450)     NULL,") | Out-Null
$sb.AppendLine("        CONSTRAINT [PK_nav_NavItem] PRIMARY KEY CLUSTERED ([NavItemId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_NavId] FOREIGN KEY ([NavId]) REFERENCES [nav].[Nav] ([NavId]) ON DELETE CASCADE,") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_ParentNavItemId] FOREIGN KEY ([ParentNavItemId]) REFERENCES [nav].[NavItem] ([NavItemId]),") | Out-Null
$sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '  1B complete: nav';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

# ── 1C: logs schema + table ──
$sb.AppendLine("PRINT '-- 1C: logs schema + table';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'logs')") | Out-Null
$sb.AppendLine("    EXEC('CREATE SCHEMA logs');") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'logs' AND t.name = 'AppLog')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE logs.AppLog (") | Out-Null
$sb.AppendLine("        Id            bigint IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,") | Out-Null
$sb.AppendLine("        TimeStamp     datetimeoffset(7)    NOT NULL DEFAULT SYSDATETIMEOFFSET(),") | Out-Null
$sb.AppendLine("        Level         nvarchar(16)         NOT NULL,") | Out-Null
$sb.AppendLine("        Message       nvarchar(max)        NULL,") | Out-Null
$sb.AppendLine("        Exception     nvarchar(max)        NULL,") | Out-Null
$sb.AppendLine("        Properties    nvarchar(max)        NULL,") | Out-Null
$sb.AppendLine("        SourceContext  nvarchar(512)        NULL,") | Out-Null
$sb.AppendLine("        RequestPath   nvarchar(512)        NULL,") | Out-Null
$sb.AppendLine("        StatusCode    int                  NULL,") | Out-Null
$sb.AppendLine("        Elapsed       float                NULL") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("    CREATE NONCLUSTERED INDEX IX_AppLog_TimeStamp_Level ON logs.AppLog (TimeStamp DESC, Level) INCLUDE (Message, SourceContext, RequestPath, StatusCode);") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '  1C complete: logs';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

# ── 1D: stores.StoreItemImage ──
$sb.AppendLine("PRINT '-- 1D: stores.StoreItemImage table';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'stores' AND t.name = 'StoreItemImage')") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    CREATE TABLE stores.StoreItemImage (") | Out-Null
$sb.AppendLine("        StoreItemImageId INT IDENTITY(1,1) PRIMARY KEY,") | Out-Null
$sb.AppendLine("        StoreItemId      INT NOT NULL,") | Out-Null
$sb.AppendLine("        ImageUrl         NVARCHAR(500) NOT NULL,") | Out-Null
$sb.AppendLine("        DisplayOrder     INT NOT NULL DEFAULT 0,") | Out-Null
$sb.AppendLine("        AltText          NVARCHAR(200) NULL,") | Out-Null
$sb.AppendLine("        Modified         DATETIME NOT NULL DEFAULT GETUTCDATE(),") | Out-Null
$sb.AppendLine("        LebUserId        NVARCHAR(128) NOT NULL,") | Out-Null
$sb.AppendLine("        CONSTRAINT FK_StoreItemImage_StoreItem FOREIGN KEY (StoreItemId) REFERENCES stores.StoreItems(StoreItemId)") | Out-Null
$sb.AppendLine("    );") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '  1D complete: StoreItemImage';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

# ── 1E: AdultProfileMetadataJson column ──
$sb.AppendLine("PRINT '-- 1E: AdultProfileMetadataJson column';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF COL_LENGTH('Jobs.Jobs', 'AdultProfileMetadataJson') IS NULL") | Out-Null
$sb.AppendLine("BEGIN") | Out-Null
$sb.AppendLine("    ALTER TABLE Jobs.Jobs ADD AdultProfileMetadataJson NVARCHAR(MAX) NULL;") | Out-Null
$sb.AppendLine("    PRINT '  Added: Jobs.Jobs.AdultProfileMetadataJson';") | Out-Null
$sb.AppendLine("END") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '  Section 1 complete -- all schemas/tables/columns verified.';") | Out-Null
$sb.AppendLine("PRINT '';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

# ████████████████████████████████████████████████████████████████████
# SECTION 2: SEED DATA
# ████████████████████████████████████████████████████████████████████

$sb.AppendLine("-- ========================================================================") | Out-Null
$sb.AppendLine("-- SECTION 2: SEED DEV DATA") | Out-Null
$sb.AppendLine("-- Only touches new-system tables. Legacy tables are UNTOUCHED.") | Out-Null
$sb.AppendLine("-- ========================================================================") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("SET NOCOUNT ON;") | Out-Null
$sb.AppendLine("") | Out-Null

# ── 2A: Widget data ──
$sb.AppendLine("PRINT '-- 2A: Widget data';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("DELETE FROM widgets.UserWidget;") | Out-Null
$sb.AppendLine("DELETE FROM widgets.JobWidget;") | Out-Null
$sb.AppendLine("DELETE FROM widgets.WidgetDefault;") | Out-Null
$sb.AppendLine("DELETE FROM widgets.Widget;") | Out-Null
$sb.AppendLine("DELETE FROM widgets.WidgetCategory;") | Out-Null
$sb.AppendLine("") | Out-Null

$sb.AppendLine("SET IDENTITY_INSERT widgets.WidgetCategory ON;") | Out-Null
foreach ($row in $categories.Rows) {
    $sb.AppendLine("INSERT INTO widgets.WidgetCategory (CategoryId, Name, Workspace, Icon, DefaultOrder)") | Out-Null
    $sb.AppendLine("VALUES ($($row.CategoryId), $(Esc $row.Name), $(Esc $row.Workspace), $(StrOrNull $row.Icon), $($row.DefaultOrder));") | Out-Null
}
$sb.AppendLine("SET IDENTITY_INSERT widgets.WidgetCategory OFF;") | Out-Null
$sb.AppendLine("PRINT '  Loaded $($categories.Rows.Count) categories';") | Out-Null
$sb.AppendLine("") | Out-Null

$sb.AppendLine("SET IDENTITY_INSERT widgets.Widget ON;") | Out-Null
foreach ($row in $widgets.Rows) {
    $sb.AppendLine("INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)") | Out-Null
    $sb.AppendLine("VALUES ($($row.WidgetId), $(Esc $row.Name), $(Esc $row.WidgetType), $(Esc $row.ComponentKey), $($row.CategoryId), $(StrOrNull $row.Description), $(StrOrNull $row.DefaultConfig));") | Out-Null
}
$sb.AppendLine("SET IDENTITY_INSERT widgets.Widget OFF;") | Out-Null
$sb.AppendLine("PRINT '  Loaded $($widgets.Rows.Count) widgets';") | Out-Null
$sb.AppendLine("") | Out-Null

$sb.AppendLine("SET IDENTITY_INSERT widgets.WidgetDefault ON;") | Out-Null
foreach ($row in $defaults.Rows) {
    $sb.AppendLine("INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)") | Out-Null
    $sb.AppendLine("VALUES ($($row.WidgetDefaultId), $($row.JobTypeId), $(Esc $row.RoleId), $($row.WidgetId), $($row.CategoryId), $($row.DisplayOrder), $(StrOrNull $row.Config));") | Out-Null
}
$sb.AppendLine("SET IDENTITY_INSERT widgets.WidgetDefault OFF;") | Out-Null
$sb.AppendLine("PRINT '  Loaded $($defaults.Rows.Count) defaults';") | Out-Null
$sb.AppendLine("") | Out-Null

if ($jobWidgets.Rows.Count -gt 0) {
    $sb.AppendLine("SET IDENTITY_INSERT widgets.JobWidget ON;") | Out-Null
    foreach ($row in $jobWidgets.Rows) {
        $sb.AppendLine("INSERT INTO widgets.JobWidget (JobWidgetId, JobId, WidgetId, RoleId, CategoryId, DisplayOrder, IsEnabled, Config)") | Out-Null
        $sb.AppendLine("VALUES ($($row.JobWidgetId), $(GuidOrNull $row.JobId), $($row.WidgetId), $(Esc $row.RoleId), $($row.CategoryId), $($row.DisplayOrder), $(BitOrNull $row.IsEnabled), $(StrOrNull $row.Config));") | Out-Null
    }
    $sb.AppendLine("SET IDENTITY_INSERT widgets.JobWidget OFF;") | Out-Null
}
$sb.AppendLine("PRINT '  Loaded $($jobWidgets.Rows.Count) job overrides';") | Out-Null
$sb.AppendLine("PRINT '  2A complete';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

# ── 2B: Nav data (generated from route manifest) ──
$sb.AppendLine("SET NOCOUNT ON;") | Out-Null
$sb.AppendLine("PRINT '-- 2B: Nav data (generated from route manifest)';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("-- Clear platform defaults only (job-specific overrides survive)") | Out-Null
$sb.AppendLine("DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);") | Out-Null
$sb.AppendLine("DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;") | Out-Null
$sb.AppendLine("") | Out-Null

# Generate Nav records (one per role)
$navId = 1
$navItemId = 1
$roleNavIdMap = @{}

$sb.AppendLine("SET IDENTITY_INSERT [nav].[Nav] ON;") | Out-Null
foreach ($role in $allRoles) {
    $roleNavIdMap[$role.RoleId] = $navId
    $sb.AppendLine("INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])") | Out-Null
    $sb.AppendLine("VALUES ($navId, N'$($role.RoleId)', NULL, 1, GETDATE());") | Out-Null
    $navId++
}
$sb.AppendLine("SET IDENTITY_INSERT [nav].[Nav] OFF;") | Out-Null
$sb.AppendLine("PRINT '  Loaded $($allRoles.Count) platform default navs';") | Out-Null
$sb.AppendLine("") | Out-Null

# Generate NavItem records from manifest — parents first, then children
$sb.AppendLine("SET IDENTITY_INSERT [nav].[NavItem] ON;") | Out-Null
$sb.AppendLine("") | Out-Null

# Track parent/child counts for output
$genParentCount = 0
$genChildCount = 0

# Collect all inserts: parents first, then children (FK constraint order)
$parentInserts = [System.Collections.Generic.List[string]]::new()
$childInserts  = [System.Collections.Generic.List[string]]::new()

foreach ($role in $adminRoles) {
    $rNavId = $roleNavIdMap[$role.RoleId]
    $gl = $role.GuardLevel

    # Get visible items for this role's guard level
    $visibleItems = @($menuManifest | Where-Object { $_.Guard -eq 'admin' -or ($gl -eq 'superuser' -and $_.Guard -eq 'superuser') })

    # Get distinct controllers (group by name, take first for icon/sort metadata)
    $controllerGroups = $visibleItems | Group-Object { $_.Controller } | Sort-Object { $_.Group[0].Sort }

    foreach ($group in $controllerGroups) {
        $firstItem = $group.Group[0]
        $parentItemId = $navItemId
        $navItemId++
        $genParentCount++

        $ctrlText = $firstItem.Controller.Replace("'", "''")
        $ctrlIcon = if ($firstItem.Icon) { "N'$($firstItem.Icon)'" } else { 'NULL' }

        $parentInserts.Add("INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])")
        $parentInserts.Add("VALUES ($parentItemId, $rNavId, NULL, 1, $($firstItem.Sort), N'$ctrlText', $ctrlIcon, NULL, NULL, NULL, GETDATE());")

        # Children under this controller
        $ctrlChildren = $group.Group | Sort-Object { $_.ActionSort }
        foreach ($child in $ctrlChildren) {
            $childText = $child.Action.Replace("'", "''")
            $childIcon = if ($child.ActionIcon) { "N'$($child.ActionIcon)'" } else { 'NULL' }
            $childRoute = "N'$($child.Route)'"

            $childInserts.Add("INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])")
            $childInserts.Add("VALUES ($navItemId, $rNavId, $parentItemId, 1, $($child.ActionSort), N'$childText', $childIcon, $childRoute, NULL, NULL, GETDATE());")
            $navItemId++
            $genChildCount++
        }
    }
}

$sb.AppendLine("-- Parents") | Out-Null
foreach ($line in $parentInserts) { $sb.AppendLine($line) | Out-Null }
$sb.AppendLine("") | Out-Null
$sb.AppendLine("-- Children") | Out-Null
foreach ($line in $childInserts) { $sb.AppendLine($line) | Out-Null }
$sb.AppendLine("") | Out-Null

$sb.AppendLine("SET IDENTITY_INSERT [nav].[NavItem] OFF;") | Out-Null
$genTotalItems = $genParentCount + $genChildCount
$sb.AppendLine("PRINT '  Loaded $genTotalItems nav items ($genParentCount parents, $genChildCount children)';") | Out-Null
$sb.AppendLine("PRINT '  2B complete';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

# ── 2C: Store image data ──
$sb.AppendLine("SET NOCOUNT ON;") | Out-Null
$sb.AppendLine("PRINT '-- 2C: Store image data';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("DELETE FROM stores.StoreItemImage;") | Out-Null
$sb.AppendLine("") | Out-Null

if ($storeImages.Rows.Count -gt 0) {
    $sb.AppendLine("DECLARE @sys NVARCHAR(128) = '71765055-647D-432E-AFB6-0F84218D0247';") | Out-Null
    $sb.AppendLine("DECLARE @now DATETIME = GETUTCDATE();") | Out-Null
    $sb.AppendLine("") | Out-Null
    foreach ($row in $storeImages.Rows) {
        $sb.AppendLine("INSERT INTO stores.StoreItemImage (StoreItemId, ImageUrl, DisplayOrder, Modified, LebUserId)") | Out-Null
        $sb.AppendLine("VALUES ($($row.StoreItemId), $(Esc $row.ImageUrl), $($row.DisplayOrder), @now, @sys);") | Out-Null
    }
    $sb.AppendLine("") | Out-Null
}
$sb.AppendLine("PRINT '  Loaded $($storeImages.Rows.Count) StoreItemImage rows';") | Out-Null
$sb.AppendLine("") | Out-Null

$sb.AppendLine("PRINT '  2C complete';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

# ████████████████████████████████████████████████████████████████████
# VERIFICATION
# ████████████████████████████████████████████████████████████████████

$sb.AppendLine("-- ========================================================================") | Out-Null
$sb.AppendLine("-- VERIFICATION SUMMARY") | Out-Null
$sb.AppendLine("-- ========================================================================") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("SET NOCOUNT ON;") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '';") | Out-Null
$sb.AppendLine("PRINT '==========================================================';") | Out-Null
$sb.AppendLine("PRINT '  VERIFICATION SUMMARY';") | Out-Null
$sb.AppendLine("PRINT '==========================================================';") | Out-Null
$sb.AppendLine("PRINT '';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '-- Widgets:';") | Out-Null
$sb.AppendLine("SELECT 'WidgetCategory' AS [Table], COUNT(*) AS [Rows] FROM widgets.WidgetCategory") | Out-Null
$sb.AppendLine("UNION ALL SELECT 'Widget', COUNT(*) FROM widgets.Widget") | Out-Null
$sb.AppendLine("UNION ALL SELECT 'WidgetDefault', COUNT(*) FROM widgets.WidgetDefault") | Out-Null
$sb.AppendLine("UNION ALL SELECT 'JobWidget', COUNT(*) FROM widgets.JobWidget") | Out-Null
$sb.AppendLine("UNION ALL SELECT 'UserWidget', COUNT(*) FROM widgets.UserWidget;") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '-- Nav:';") | Out-Null
$sb.AppendLine("SELECT 'Nav (platform defaults)' AS [Table], COUNT(*) AS [Rows] FROM nav.Nav WHERE JobId IS NULL") | Out-Null
$sb.AppendLine("UNION ALL SELECT 'NavItem (in defaults)', (SELECT COUNT(*) FROM nav.NavItem WHERE NavId IN (SELECT NavId FROM nav.Nav WHERE JobId IS NULL));") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '-- Store Images:';") | Out-Null
$sb.AppendLine("SELECT 'StoreItemImage' AS [Table], COUNT(*) AS [Rows] FROM stores.StoreItemImage") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '-- Schema Columns:';") | Out-Null
$sb.AppendLine("SELECT 'Jobs.AdultProfileMetadataJson' AS [Column],") | Out-Null
$sb.AppendLine("    CASE WHEN COL_LENGTH('Jobs.Jobs', 'AdultProfileMetadataJson') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END AS [Status]") | Out-Null
$sb.AppendLine("UNION ALL") | Out-Null
$sb.AppendLine("SELECT 'widgets.Widget.DefaultConfig',") | Out-Null
$sb.AppendLine("    CASE WHEN COL_LENGTH('widgets.Widget', 'DefaultConfig') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END;") | Out-Null
$sb.AppendLine("") | Out-Null
# ████████████████████████████████████████████████████████████████████
# SECTION 3: IIS APP POOL DB LOGIN
# ████████████████████████████████████████████████████████████████████

$sb.AppendLine("-- ========================================================================") | Out-Null
$sb.AppendLine("-- SECTION 3: IIS APP POOL DB LOGIN") | Out-Null
$sb.AppendLine("-- After a restore, the IIS app pool identity loses database access.") | Out-Null
$sb.AppendLine("-- This idempotently creates the login + user mapping.") | Out-Null
$sb.AppendLine("-- ========================================================================") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '-- 3: IIS App Pool DB Login';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'IIS APPPOOL\TSIC.Api')") | Out-Null
$sb.AppendLine("    CREATE LOGIN [IIS APPPOOL\TSIC.Api] FROM WINDOWS;") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'IIS APPPOOL\TSIC.Api')") | Out-Null
$sb.AppendLine("    CREATE USER [IIS APPPOOL\TSIC.Api] FOR LOGIN [IIS APPPOOL\TSIC.Api];") | Out-Null
$sb.AppendLine("ELSE") | Out-Null
$sb.AppendLine("    ALTER USER [IIS APPPOOL\TSIC.Api] WITH LOGIN = [IIS APPPOOL\TSIC.Api];") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\TSIC.Api];") | Out-Null
$sb.AppendLine("ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\TSIC.Api];") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("PRINT '  IIS APPPOOL\TSIC.Api login ensured.';") | Out-Null
$sb.AppendLine("PRINT '  Section 3 complete.';") | Out-Null
$sb.AppendLine("GO") | Out-Null
$sb.AppendLine("") | Out-Null

$sb.AppendLine("PRINT '';") | Out-Null
$sb.AppendLine("PRINT '==========================================================';") | Out-Null
$sb.AppendLine("PRINT '  0-Restore-DevConfig-PROD.sql -- COMPLETE';") | Out-Null
$sb.AppendLine("PRINT '  All schemas, tables, dev config, and IIS login are in place.';") | Out-Null
$sb.AppendLine("PRINT '  Legacy tables were NOT modified.';") | Out-Null
$sb.AppendLine("PRINT '==========================================================';") | Out-Null
$sb.AppendLine("") | Out-Null
$sb.AppendLine("SET NOCOUNT OFF;") | Out-Null

# ── Write file ──
$sb.ToString() | Out-File -FilePath $OutputPath -Encoding UTF8

Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host "  Export complete!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Output: $OutputPath" -ForegroundColor White
Write-Host ""
Write-Host "  Widgets:  $($categories.Rows.Count) categories, $($widgets.Rows.Count) widgets, $($defaults.Rows.Count) defaults, $($jobWidgets.Rows.Count) job overrides" -ForegroundColor Gray
Write-Host "  Nav:      $navRoleCount navs, $navItemCount items ($navParentCount parents, $navChildCount children) [from route manifest]" -ForegroundColor Gray
Write-Host "  Store:    $($storeImages.Rows.Count) images" -ForegroundColor Gray
Write-Host ""
Write-Host "  Next: Run 0-Restore-DevConfig-PROD.sql against target DB." -ForegroundColor Yellow
Write-Host ""
