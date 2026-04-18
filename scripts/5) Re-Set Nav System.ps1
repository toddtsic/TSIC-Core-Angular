# ============================================================================
# 5) Re-Set Nav System.ps1
#
# Generates scripts/5) Re-Set Nav System.sql — the T-SQL that rebuilds nav
# platform defaults (JobId IS NULL) from scratch. Job-level overrides
# (JobId IS NOT NULL) are preserved.
#
# Model:
#   Admin tier (Director / SuperDirector / SuperUser) — single manifest with
#   per-role BIT flags. Structure mirrors legacy Jobs.JobMenu_Items grouping
#   for Director familiarity at cutover (Configure mega-L1; Search; Reports;
#   Scheduling; ARB).
#
#   Narrow admin roles (RefAssignor, StoreAdmin) — single-purpose menus.
#
#   Non-admin roles (Family, ClubRep, Player, Staff, UnassignedAdult) are NOT
#   served by this nav system. Their tasks live in the header avatar dropdown
#   (client-header-bar). No nav rows or items are emitted for those roles.
#
# VisibilityRules: seeded on L1 (section parent) rows when a section should
# be JobType/sport/customer-conditional. The runtime evaluator
# (NavRepository.PassesVisibilityRules) removes the whole section when rules
# fail — L2 rules are unnecessary. Hand-authored rules are preserved across
# re-runs (step 14) and override seeded values on items with matching RouterLink.
#
# Output: scripts/5) Re-Set Nav System.sql
# Apply:  sqlcmd -i "5) Re-Set Nav System.sql"   (or run via SSMS)
# ============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host ""
Write-Host "+================================================+" -ForegroundColor Cyan
Write-Host "|   Nav System Reset -- Role-Scoped Rebuild     |" -ForegroundColor Cyan
Write-Host "+================================================+" -ForegroundColor Cyan
Write-Host ""

# -- Role GUIDs ----------------------------------------------------------

$roleGuids = [ordered]@{
    Director        = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06'
    SuperDirector   = '7B9EB503-53C9-44FA-94A0-17760C512440'
    SuperUser       = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9'
    RefAssignor     = '122075A3-2C42-4092-97F1-9673DF5B6A2C'
    StoreAdmin      = '5B9B7055-4530-4E46-B403-1019FD8B8418'
}

# -- Admin manifest ------------------------------------------------------
# BIT flags: 1 = role sees this item, 0 = hidden. Values mirror backing
# controller's [Authorize(Roles = "...")] attributes.
# D = Director, SD = SuperDirector, SU = SuperUser.

function New-AdminItem {
    param($Ctrl, $CtrlIcon, $CtrlSort, $Text, $Icon, $Route, $ItemSort, $D, $SD, $SU, $VisRules = $null)
    [PSCustomObject]@{
        Controller      = $Ctrl
        ControllerIcon  = $CtrlIcon
        ControllerSort  = $CtrlSort
        Text            = $Text
        Icon            = $Icon
        RouterLink      = $Route
        ItemSort        = $ItemSort
        ForDirector     = $D
        ForSuperDir     = $SD
        ForSuperUser    = $SU
        VisibilityRules = $VisRules   # applied to the L1 section; NULL = always visible
    }
}

# VisibilityRules JSON presets — runtime evaluator (NavRepository.PassesVisibilityRules) consumes as-is.
# JobTypeName values match reference.JobTypes.JobTypeName.
# Flag names are derived in NavRepository.GetJobNavContextAsync from Jobs entity columns:
#   storeEnabled           <- BEnableStore
#   adnArb                 <- AdnArb
#   mobileEnabled          <- BenableStp
#   teamEligibilityByAge   <- CoreRegformPlayer (2nd pipe == 'BYAGERANGE')
#   playerSiteOnly         <- JobTypeId IN (1,4,6)
$rulesTournamentLeague   = '{"jobTypes":["Tournament Scheduling","League Scheduling"]}'
$rulesStoreEnabled       = '{"requiresFlags":["storeEnabled"]}'
$rulesMobileEnabled      = '{"requiresFlags":["mobileEnabled"]}'
$rulesTeamEligByAge      = '{"requiresFlags":["teamEligibilityByAge"]}'
$rulesAdnArb             = '{"requiresFlags":["adnArb"]}'
$rulesLacrosse           = '{"sports":["Lacrosse"]}'

# Section-level rules keyed by Controller name. These override any value inferred
# from per-item aggregation and land on the L1 section parent. Use this when the
# section is gated as a whole but individual items carry additional per-item rules
# (e.g. Scheduling is Tournament/League only, while Mobile Scorers adds mobileEnabled).
$sectionRules = @{
    'Scheduling' = $rulesTournamentLeague
    'ARB'        = $rulesAdnArb
}

$adminManifest = @(
    # -- Search ------------------------------------------------------------
    (New-AdminItem 'Search' 'search' 1 'Registrations' 'people' 'search/registrations' 1 1 1 1)
    (New-AdminItem 'Search' 'search' 1 'Teams'         'shield' 'search/teams'         2 1 1 1)

    # -- Configure (mega-L1 preserving legacy Director familiarity) --------
    # Director-visible items (sort 1-19)
    (New-AdminItem 'Configure' 'gear' 2 'Job Settings'      'briefcase'         'configure/job'                      1 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Age Ranges'        'sliders'           'configure/age-ranges'               2 1 1 1 $rulesTeamEligByAge)
    (New-AdminItem 'Configure' 'gear' 2 'Discount Codes'    'tags'              'configure/discount-codes'           3 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'L-A-D-T'           'diagram-3'         'ladt/editor'                        4 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Rosters'           'arrow-left-right'  'ladt/roster-swapper'                5 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Pools'             'people'            'ladt/pool-assignment'               6 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Bulletins'         'megaphone'         'communications/bulletins'           7 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Email Log'         'envelope-open'     'communications/email-log'           8 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Push Notification' 'bell'              'communications/push-notification'   9 1 1 1 $rulesMobileEnabled)
    (New-AdminItem 'Configure' 'gear' 2 'Uniform Upload'    'upload'            'configure/uniform-upload'          10 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Store Admin'       'shop'              'store/admin'                       11 1 1 1 $rulesStoreEnabled)
    (New-AdminItem 'Configure' 'gear' 2 'US Lax Tester'     'check-circle'      'tools/uslax-test'                  12 1 1 1 $rulesLacrosse)
    (New-AdminItem 'Configure' 'gear' 2 'US Lax Rankings'   'trophy'            'tools/uslax-rankings'              13 1 1 1 $rulesLacrosse)

    # SuperUser-only items (sort 20+)
    (New-AdminItem 'Configure' 'gear' 2 'Administrators'    'person-badge'      'configure/administrators'          20 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Customer Groups'   'people'            'configure/customer-groups'         21 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Dropdown Options'  'list'              'configure/ddl-options'             22 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Customers'         'building'          'configure/customers'               23 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Theme'             'palette'           'configure/theme'                   24 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Menus'             'list'              'configure/nav-editor'              25 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Widgets'           'grid'              'configure/widget-editor'           26 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Job Clone'         'copy'              'configure/job-clone'               27 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Profile Migration' 'arrow-right'       'tools/profile-migration'           28 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Profile Editor'    'pencil-square'     'tools/profile-editor'              29 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Change Password'   'key'               'tools/change-password'             30 0 0 1)

    # -- Reports (single leaf today → future Report Library; Customer Job Revenue SD+ only) --
    (New-AdminItem 'Reports' 'file-earmark-bar-graph' 3 'Report Library'       'collection' 'reports'                     1 1 1 1)
    (New-AdminItem 'Reports' 'file-earmark-bar-graph' 3 'Customer Job Revenue' 'cash-stack' 'tools/customer-job-revenue'  2 0 1 1)

    # -- Scheduling (section-gated to Tournament/League via $sectionRules; Mobile Scorers adds mobileEnabled) --
    (New-AdminItem 'Scheduling' 'calendar' 4 'Schedule Hub'   'grid'         'scheduling/schedule-hub'   1 1 1 1)
    (New-AdminItem 'Scheduling' 'calendar' 4 'View Schedule'  'eye'          'scheduling/view-schedule'  2 1 1 1)
    (New-AdminItem 'Scheduling' 'calendar' 4 'Rescheduler'    'arrow-repeat' 'scheduling/rescheduler'    3 1 1 1)
    (New-AdminItem 'Scheduling' 'calendar' 4 'Mobile Scorers' 'phone'        'scheduling/mobile-scorers' 4 1 1 1 $rulesMobileEnabled)

    # -- ARB (section-gated on adnArb flag via $sectionRules) -------------
    (New-AdminItem 'ARB' 'credit-card' 5 'Health Check' 'heart-pulse' 'arb/health' 1 1 1 1)
)

Write-Host "Admin manifest: $($adminManifest.Count) items" -ForegroundColor DarkGray

# -- Narrow admin menus (single-purpose) ---------------------------------

function New-L1 { param($Sort, $Text, $Icon)
    [PSCustomObject]@{ Type='L1'; Sort=$Sort; Text=$Text; Icon=$Icon; Route=$null; Parent=$null }
}
function New-L2 { param($Parent, $Sort, $Text, $Icon, $Route)
    [PSCustomObject]@{ Type='L2'; Parent=$Parent; Sort=$Sort; Text=$Text; Icon=$Icon; Route=$Route }
}
function New-Leaf { param($Sort, $Text, $Icon, $Route)
    [PSCustomObject]@{ Type='Leaf'; Sort=$Sort; Text=$Text; Icon=$Icon; Route=$Route; Parent=$null }
}

$refAssignorMenu = @(
    (New-L1 1 'RefAssignor' 'person-check')
      (New-L2 'RefAssignor' 1 'Referee Assignment' 'clipboard-check' 'scheduling/referee-assignment')
      (New-L2 'RefAssignor' 2 'Referee Calendar'   'calendar-week'   'scheduling/referee-calendar')
)

$storeAdminMenu = @(
    (New-L1 1 'StoreAdmin' 'shop')
      (New-L2 'StoreAdmin' 1 'Store Admin' 'speedometer2' 'store/admin')
)

# -- Emit T-SQL ----------------------------------------------------------

function Esc([string]$s) { if ($null -eq $s) { return '' } $s -replace "'", "''" }

$sqlOutputPath = Join-Path $PSScriptRoot '5) Re-Set Nav System.sql'
$sql = [System.Text.StringBuilder]::new()

[void]$sql.AppendLine("-- ============================================================================")
[void]$sql.AppendLine("-- 5) Re-Set Nav System.sql")
[void]$sql.AppendLine("-- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') by 5) Re-Set Nav System.ps1")
[void]$sql.AppendLine("-- Role-scoped manifest; VisibilityRules seeded on L1 section parents where")
[void]$sql.AppendLine("-- the section is JobType/sport/customer-conditional (e.g. Scheduling).")
[void]$sql.AppendLine("-- Preserves: job-level overrides, reporting items, hand-authored L2 rules.")
[void]$sql.AppendLine("-- ============================================================================")
[void]$sql.AppendLine("")
[void]$sql.AppendLine("SET NOCOUNT ON;")
[void]$sql.AppendLine("SET XACT_ABORT ON;")
[void]$sql.AppendLine("BEGIN TRANSACTION;")
[void]$sql.AppendLine("")

# 1. Schema + tables
[void]$sql.AppendLine("-- -- 1. Ensure schema + tables exist -------------------------------------")
[void]$sql.AppendLine(@"
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')
    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'Nav')
BEGIN
    CREATE TABLE [nav].[Nav] (
        [NavId] INT IDENTITY(1,1) NOT NULL, [RoleId] NVARCHAR(450) NOT NULL,
        [JobId] UNIQUEIDENTIFIER NULL, [Active] BIT NOT NULL DEFAULT 1,
        [Modified] DATETIME2 NOT NULL DEFAULT GETDATE(), [ModifiedBy] NVARCHAR(450) NULL,
        CONSTRAINT [PK_nav_Nav] PRIMARY KEY CLUSTERED ([NavId]),
        CONSTRAINT [FK_nav_Nav_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles]([Id]),
        CONSTRAINT [FK_nav_Nav_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs]([JobId]),
        CONSTRAINT [FK_nav_Nav_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers]([Id])
    );
    CREATE UNIQUE INDEX [UQ_nav_Nav_Role_Job] ON [nav].[Nav]([RoleId],[JobId]) WHERE [JobId] IS NOT NULL;
    PRINT 'Created table: nav.Nav';
END

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'NavItem')
BEGIN
    CREATE TABLE [nav].[NavItem] (
        [NavItemId] INT IDENTITY(1,1) NOT NULL, [NavId] INT NOT NULL,
        [ParentNavItemId] INT NULL, [Active] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0, [Text] NVARCHAR(200) NOT NULL,
        [IconName] NVARCHAR(100) NULL, [RouterLink] NVARCHAR(500) NULL,
        [NavigateUrl] NVARCHAR(500) NULL, [Target] NVARCHAR(20) NULL,
        [VisibilityRules] NVARCHAR(MAX) NULL,
        [Modified] DATETIME2 NOT NULL DEFAULT GETDATE(), [ModifiedBy] NVARCHAR(450) NULL,
        CONSTRAINT [PK_nav_NavItem] PRIMARY KEY CLUSTERED ([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_NavId] FOREIGN KEY ([NavId]) REFERENCES [nav].[Nav]([NavId]) ON DELETE CASCADE,
        CONSTRAINT [FK_nav_NavItem_ParentNavItemId] FOREIGN KEY ([ParentNavItemId]) REFERENCES [nav].[NavItem]([NavItemId]),
        CONSTRAINT [FK_nav_NavItem_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers]([Id])
    );
    PRINT 'Created table: nav.NavItem';
END
"@)
[void]$sql.AppendLine("")

# 2. Role GUID declarations
[void]$sql.AppendLine("-- -- 2. Role GUIDs -------------------------------------------------------")
foreach ($k in $roleGuids.Keys) {
    [void]$sql.AppendLine("DECLARE @$k NVARCHAR(450) = '$($roleGuids[$k])';")
}
[void]$sql.AppendLine("")

# 3. Preserve reporting items + visibility rules
[void]$sql.AppendLine("-- -- 3. Preserve reporting items + visibility rules ----------------------")
[void]$sql.AppendLine(@"
DECLARE @cnt INT;

IF OBJECT_ID('tempdb..#ReportingItems') IS NOT NULL DROP TABLE #ReportingItems;
SELECT n.RoleId,
       parent.[Text]      AS ParentText,
       parent.IconName    AS ParentIcon,
       parent.SortOrder   AS ParentSort,
       ni.Active, ni.SortOrder,
       ni.[Text], ni.IconName, ni.RouterLink, ni.NavigateUrl, ni.[Target]
INTO #ReportingItems
FROM nav.NavItem ni
JOIN nav.Nav n ON ni.NavId = n.NavId
LEFT JOIN nav.NavItem parent ON ni.ParentNavItemId = parent.NavItemId
WHERE n.JobId IS NULL AND ni.RouterLink LIKE 'reporting/%';
SELECT @cnt = COUNT(*) FROM #ReportingItems;
PRINT CONCAT('Preserved ', @cnt, ' reporting item(s)');

IF OBJECT_ID('tempdb..#VisRules') IS NOT NULL DROP TABLE #VisRules;
SELECT n.RoleId, ni.RouterLink, ni.VisibilityRules
INTO #VisRules
FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId
WHERE n.JobId IS NULL
  AND ni.RouterLink IS NOT NULL
  AND ni.VisibilityRules IS NOT NULL
  AND ni.VisibilityRules <> '';
SELECT @cnt = COUNT(*) FROM #VisRules;
PRINT CONCAT('Preserved ', @cnt, ' visibility rule(s)');
"@)
[void]$sql.AppendLine("")

# 4. Clear platform defaults
[void]$sql.AppendLine("-- -- 4. Clear platform defaults (job overrides preserved) ----------------")
[void]$sql.AppendLine(@"
DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);
DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;
PRINT 'Cleared platform defaults';
"@)
[void]$sql.AppendLine("")

# 5. Insert Nav records (one per role)
[void]$sql.AppendLine("-- -- 5. Insert Nav records (one per role) -------------------------------")
foreach ($k in $roleGuids.Keys) {
    [void]$sql.AppendLine("INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES (@$k, NULL, 1, GETDATE());")
}
[void]$sql.AppendLine("PRINT 'Inserted $($roleGuids.Count) Nav records';")
[void]$sql.AppendLine("")

# 6. Admin manifest + fan-out
[void]$sql.AppendLine("-- -- 6. Admin manifest (Director / SuperDirector / SuperUser) -----------")
[void]$sql.AppendLine(@"
IF OBJECT_ID('tempdb..#AdminManifest') IS NOT NULL DROP TABLE #AdminManifest;
CREATE TABLE #AdminManifest (
    Controller      NVARCHAR(50)  NOT NULL,
    Icon            NVARCHAR(50)  NULL,
    CtrlSort        INT           NOT NULL,
    [Action]        NVARCHAR(100) NOT NULL,
    ActionIcon      NVARCHAR(50)  NULL,
    RouterLink      NVARCHAR(200) NOT NULL,
    ActionSort      INT           NOT NULL,
    ForDirector     BIT           NOT NULL,
    ForSuperDir     BIT           NOT NULL,
    ForSuperUser    BIT           NOT NULL,
    VisibilityRules NVARCHAR(MAX) NULL    -- JSON, applied to L1 section; NULL = always visible
);
"@)
foreach ($item in $adminManifest) {
    $rulesCol = if ([string]::IsNullOrEmpty($item.VisibilityRules)) { 'NULL' } else { "N'$(Esc $item.VisibilityRules)'" }
    [void]$sql.AppendLine(
        "INSERT INTO #AdminManifest VALUES (" +
        "N'$(Esc $item.Controller)', N'$(Esc $item.ControllerIcon)', $($item.ControllerSort), " +
        "N'$(Esc $item.Text)', N'$(Esc $item.Icon)', N'$(Esc $item.RouterLink)', $($item.ItemSort), " +
        "$($item.ForDirector), $($item.ForSuperDir), $($item.ForSuperUser), $rulesCol);"
    )
}
[void]$sql.AppendLine("")

# Section-rule overlay: explicit L1 rules for whole sections, independent of per-item rules.
[void]$sql.AppendLine("-- Section-level rules applied to L1 independent of per-item aggregation")
[void]$sql.AppendLine(@"
IF OBJECT_ID('tempdb..#SectionRules') IS NOT NULL DROP TABLE #SectionRules;
CREATE TABLE #SectionRules (
    Controller      NVARCHAR(50)  NOT NULL PRIMARY KEY,
    VisibilityRules NVARCHAR(MAX) NOT NULL
);
"@)
foreach ($ctrl in $sectionRules.Keys) {
    [void]$sql.AppendLine(
        "INSERT INTO #SectionRules VALUES (N'$(Esc $ctrl)', N'$(Esc $sectionRules[$ctrl])');"
    )
}
[void]$sql.AppendLine("")

[void]$sql.AppendLine("-- Fan out admin manifest per admin role")
[void]$sql.AppendLine(@"
DECLARE @navId INT, @parentId INT, @roleId NVARCHAR(450);

DECLARE role_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT RoleId FROM nav.Nav
    WHERE JobId IS NULL AND RoleId IN (@Director, @SuperDirector, @SuperUser);

OPEN role_cursor;
FETCH NEXT FROM role_cursor INTO @roleId;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @roleId AND JobId IS NULL;

    -- Per-L1 cursor. L1 rules are inherited ONLY when every visible item under the
    -- controller carries the same non-null rule (treated as a section-wide rule).
    -- Mixed / partial rules stay on their individual L2 rows so the L1 remains visible
    -- while per-item gating applies.
    DECLARE @ctrl NVARCHAR(50), @ctrlIcon NVARCHAR(50), @ctrlSort INT, @ctrlVisRules NVARCHAR(MAX);
    DECLARE ctrl_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT am.Controller,
               MIN(am.Icon)     AS Icon,
               MIN(am.CtrlSort) AS CtrlSort,
               COALESCE(
                    MIN(sr.VisibilityRules),
                    CASE
                        WHEN COUNT(*) = COUNT(am.VisibilityRules)
                         AND COUNT(DISTINCT am.VisibilityRules) = 1
                        THEN MIN(am.VisibilityRules)
                        ELSE NULL
                    END
               ) AS VisibilityRules
        FROM #AdminManifest am
        LEFT JOIN #SectionRules sr ON sr.Controller = am.Controller
        WHERE CASE @roleId
                 WHEN @Director      THEN ForDirector
                 WHEN @SuperDirector THEN ForSuperDir
                 WHEN @SuperUser     THEN ForSuperUser
                 ELSE 0
             END = 1
        GROUP BY am.Controller
        ORDER BY MIN(am.CtrlSort);

    OPEN ctrl_cursor;
    FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort, @ctrlVisRules;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- L1 (section parent) — gets rules only when every child shares them.
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, VisibilityRules, Modified)
        VALUES (@navId, NULL, 1, @ctrlSort, @ctrl, @ctrlIcon, @ctrlVisRules, GETDATE());
        SET @parentId = SCOPE_IDENTITY();

        -- L2 leaves — carry their own rules for per-item gating.
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, VisibilityRules, Modified)
        SELECT @navId, @parentId, 1, ActionSort, [Action], ActionIcon, RouterLink, VisibilityRules, GETDATE()
        FROM #AdminManifest
        WHERE Controller = @ctrl
          AND CASE @roleId
                 WHEN @Director      THEN ForDirector
                 WHEN @SuperDirector THEN ForSuperDir
                 WHEN @SuperUser     THEN ForSuperUser
                 ELSE 0
             END = 1
        ORDER BY ActionSort;

        FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort, @ctrlVisRules;
    END
    CLOSE ctrl_cursor;
    DEALLOCATE ctrl_cursor;

    FETCH NEXT FROM role_cursor INTO @roleId;
END
CLOSE role_cursor;
DEALLOCATE role_cursor;

PRINT 'Fanned admin manifest to Director / SuperDirector / SuperUser';
"@)
[void]$sql.AppendLine("")

# -- Helper: emit SQL for a hand-authored role menu ----------------------
function Emit-RoleMenu {
    param($Builder, $RoleVar, $Menu, $RoleDescription)

    [void]$Builder.AppendLine("-- $RoleDescription")
    [void]$Builder.AppendLine("SELECT @navId = NavId FROM nav.Nav WHERE RoleId = @$RoleVar AND JobId IS NULL;")

    $currentL1 = $null

    foreach ($entry in $Menu) {
        switch ($entry.Type) {
            'L1' {
                $txt = Esc $entry.Text
                $ico = Esc $entry.Icon
                [void]$Builder.AppendLine(
                    "INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified) " +
                    "VALUES (@navId, NULL, 1, $($entry.Sort), N'$txt', N'$ico', GETDATE());")
                [void]$Builder.AppendLine("SET @parentId = SCOPE_IDENTITY();")
                $currentL1 = $entry.Text
            }
            'L2' {
                if ($entry.Parent -ne $currentL1) {
                    throw "Role $RoleDescription menu: L2 '$($entry.Text)' declares parent '$($entry.Parent)' but current L1 is '$currentL1'. Order L2s directly after their L1."
                }
                $txt = Esc $entry.Text
                $ico = Esc $entry.Icon
                $rte = Esc $entry.Route
                [void]$Builder.AppendLine(
                    "INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) " +
                    "VALUES (@navId, @parentId, 1, $($entry.Sort), N'$txt', N'$ico', N'$rte', GETDATE());")
            }
            'Leaf' {
                $txt = Esc $entry.Text
                $ico = Esc $entry.Icon
                $rte = Esc $entry.Route
                [void]$Builder.AppendLine(
                    "INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified) " +
                    "VALUES (@navId, NULL, 1, $($entry.Sort), N'$txt', N'$ico', N'$rte', GETDATE());")
            }
        }
    }
    [void]$Builder.AppendLine("PRINT '$RoleDescription';")
    [void]$Builder.AppendLine("")
}

# 7-8. Narrow-admin hand-authored menus
[void]$sql.AppendLine("-- -- 7. RefAssignor -----------------------------------------------------")
Emit-RoleMenu $sql 'RefAssignor' $refAssignorMenu 'RefAssignor: Referee Assignment + Referee Calendar'

[void]$sql.AppendLine("-- -- 8. StoreAdmin ------------------------------------------------------")
Emit-RoleMenu $sql 'StoreAdmin' $storeAdminMenu 'StoreAdmin: Store Admin'

# 13. Restore reporting items
[void]$sql.AppendLine("-- -- 13. Restore preserved reporting items ------------------------------")
[void]$sql.AppendLine(@"
SELECT @cnt = COUNT(*) FROM #ReportingItems;
IF @cnt > 0
BEGIN
    DECLARE @rptRoleId NVARCHAR(450), @rptParentText NVARCHAR(200),
            @rptParentIcon NVARCHAR(100), @rptParentSort INT;
    DECLARE @rptNavId INT, @rptParentId INT;

    -- Restore grouped reporting items (with parent hierarchy, per role)
    DECLARE rpt_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT RoleId, ParentText, ParentIcon, ParentSort
        FROM #ReportingItems
        WHERE ParentText IS NOT NULL;

    OPEN rpt_cursor;
    FETCH NEXT FROM rpt_cursor INTO @rptRoleId, @rptParentText, @rptParentIcon, @rptParentSort;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SELECT @rptNavId = NavId FROM nav.Nav WHERE RoleId = @rptRoleId AND JobId IS NULL;

        -- Find or create the parent controller item
        IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @rptNavId AND [Text] = @rptParentText AND ParentNavItemId IS NULL)
        BEGIN
            INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified)
            VALUES (@rptNavId, NULL, 1, @rptParentSort, @rptParentText, @rptParentIcon, GETDATE());
            SET @rptParentId = SCOPE_IDENTITY();
        END
        ELSE
            SELECT @rptParentId = NavItemId FROM nav.NavItem WHERE NavId = @rptNavId AND [Text] = @rptParentText AND ParentNavItemId IS NULL;

        INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, NavigateUrl, [Target], Modified)
        SELECT @rptNavId, @rptParentId, ri.Active, ri.SortOrder, ri.[Text], ri.IconName, ri.RouterLink, ri.NavigateUrl, ri.[Target], GETDATE()
        FROM #ReportingItems ri
        WHERE ri.RoleId = @rptRoleId AND ri.ParentText = @rptParentText;

        FETCH NEXT FROM rpt_cursor INTO @rptRoleId, @rptParentText, @rptParentIcon, @rptParentSort;
    END
    CLOSE rpt_cursor;
    DEALLOCATE rpt_cursor;

    -- Restore orphaned reporting items (no parent — top-level leaves)
    INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, NavigateUrl, [Target], Modified)
    SELECT n.NavId, NULL, ri.Active, ri.SortOrder, ri.[Text], ri.IconName, ri.RouterLink, ri.NavigateUrl, ri.[Target], GETDATE()
    FROM #ReportingItems ri
    JOIN nav.Nav n ON n.RoleId = ri.RoleId AND n.JobId IS NULL
    WHERE ri.ParentText IS NULL;

    PRINT CONCAT('Restored ', @cnt, ' reporting item(s) with original role + parent grouping');
END
DROP TABLE #ReportingItems;
"@)
[void]$sql.AppendLine("")

# 14. Restore visibility rules
[void]$sql.AppendLine("-- -- 14. Restore preserved visibility rules -----------------------------")
[void]$sql.AppendLine(@"
UPDATE ni
SET ni.VisibilityRules = vr.VisibilityRules
FROM nav.NavItem ni
JOIN nav.Nav n ON ni.NavId = n.NavId
JOIN #VisRules vr ON vr.RoleId = n.RoleId AND vr.RouterLink = ni.RouterLink
WHERE n.JobId IS NULL;
PRINT CONCAT('Restored ', @@ROWCOUNT, ' visibility rule(s)');
DROP TABLE #VisRules;
DROP TABLE #AdminManifest;
DROP TABLE #SectionRules;
"@)
[void]$sql.AppendLine("")

# 15. Commit + summary
[void]$sql.AppendLine("-- -- 15. Commit + summary -----------------------------------------------")
[void]$sql.AppendLine(@"
COMMIT TRANSACTION;

SELECT
    (SELECT COUNT(*) FROM nav.Nav WHERE JobId IS NULL)                                                                      AS [Platform Navs],
    (SELECT COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId WHERE n.JobId IS NULL)                        AS [Platform Items],
    (SELECT COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId WHERE n.JobId IS NULL AND ni.ParentNavItemId IS NULL) AS [Sections or Top-level Leaves],
    (SELECT COUNT(*) FROM nav.Nav WHERE JobId IS NOT NULL)                                                                  AS [Job Overrides (preserved)];

SELECT
    r.Name AS [Role],
    parent.Text AS [Section],
    parent.SortOrder AS [SectionOrder],
    parent.RouterLink AS [SectionRoute],
    child.Text AS [Item],
    child.SortOrder AS [ItemOrder],
    child.RouterLink AS [ItemRoute]
FROM [nav].[Nav] n
JOIN [dbo].[AspNetRoles] r ON n.RoleId = r.Id
LEFT JOIN [nav].[NavItem] parent ON parent.NavId = n.NavId AND parent.ParentNavItemId IS NULL
LEFT JOIN [nav].[NavItem] child  ON child.ParentNavItemId = parent.NavItemId
WHERE n.JobId IS NULL
ORDER BY r.Name, parent.SortOrder, child.SortOrder;

PRINT 'Nav system reset complete.';
SET NOCOUNT OFF;
"@)

# -- Write file ----------------------------------------------------------

$sql.ToString() | Set-Content -Path $sqlOutputPath -Encoding UTF8
Write-Host ""
Write-Host ("=" * 64) -ForegroundColor Green
Write-Host " Generated: $sqlOutputPath" -ForegroundColor Green
Write-Host " Admin manifest: $($adminManifest.Count) items" -ForegroundColor Green
Write-Host " Narrow admins: RefAssignor, StoreAdmin (hand-authored)" -ForegroundColor Green
Write-Host " Non-admin roles: intentionally excluded (see header dropdown)" -ForegroundColor Green
Write-Host ""
Write-Host " Apply with: sqlcmd -i `"5) Re-Set Nav System.sql`"" -ForegroundColor Green
Write-Host ("=" * 64) -ForegroundColor Green
Write-Host ""
