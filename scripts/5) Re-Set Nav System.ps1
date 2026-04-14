# ============================================================================
# 5) Re-Set Nav System.ps1
#
# Generates scripts/5) Re-Set Nav System.sql — the T-SQL that rebuilds nav
# platform defaults (JobId IS NULL) from scratch. Job-level overrides
# (JobId IS NOT NULL) are preserved.
#
# Model:
#   Admin tier (Director / SuperDirector / SuperUser) — single manifest with
#   per-role BIT flags. Per-item visibility mirrors the backing controller's
#   [Authorize(Roles = "...")] attributes.
#
#   Narrow admin roles (RefAssignor, StoreAdmin) — single-purpose menus.
#
#   Registrant roles (Family, ClubRep, Player, Staff) — hand-authored here.
#   No schedule items; rosters top-level; account-edit lives in the header
#   avatar dropdown, not in role menus.
#
#   UnassignedAdult — Nav row with zero items.
#
# No Guard ladder, no string <= compares. No VisibilityRules emitted in seed;
# per-item rules are preserved across runs.
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
    Staff           = '1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA'
    RefAssignor     = '122075A3-2C42-4092-97F1-9673DF5B6A2C'
    StoreAdmin      = '5B9B7055-4530-4E46-B403-1019FD8B8418'
    Family          = 'E0A8A5C3-A36C-417F-8312-E7083F1AA5A0'
    Player          = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
    ClubRep         = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'
    UnassignedAdult = 'C92D71A9-464D-40C5-BA35-DFD9111CC7EA'
}

# -- Admin manifest ------------------------------------------------------
# BIT flags: 1 = role sees this item, 0 = hidden. Values mirror backing
# controller's [Authorize(Roles = "...")] attributes.
# D = Director, SD = SuperDirector, SU = SuperUser.

function New-AdminItem {
    param($Ctrl, $CtrlIcon, $CtrlSort, $Text, $Icon, $Route, $ItemSort, $D, $SD, $SU)
    [PSCustomObject]@{
        Controller    = $Ctrl
        ControllerIcon = $CtrlIcon
        ControllerSort = $CtrlSort
        Text          = $Text
        Icon          = $Icon
        RouterLink    = $Route
        ItemSort      = $ItemSort
        ForDirector   = $D
        ForSuperDir   = $SD
        ForSuperUser  = $SU
    }
}

$adminManifest = @(
    # Search
    (New-AdminItem 'Search' 'search' 1 'Registrations' 'people' 'search/registrations' 1 1 1 1)
    (New-AdminItem 'Search' 'search' 1 'Teams'         'shield' 'search/teams'         2 1 1 1)

    # Configure
    (New-AdminItem 'Configure' 'gear' 2 'Job'              'briefcase'    'configure/job'               1 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Age Ranges'       'sliders'      'configure/age-ranges'        2 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Discount Codes'   'tags'         'configure/discount-codes'    3 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Uniform Upload'   'upload'       'configure/uniform-upload'    4 1 1 1)
    (New-AdminItem 'Configure' 'gear' 2 'Administrators'   'person-badge' 'configure/administrators'   10 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Customer Groups'  'people'       'configure/customer-groups'  11 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Dropdown Options' 'list'         'configure/ddl-options'      12 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Customers'        'building'     'configure/customers'        13 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Theme'            'palette'      'configure/theme'            14 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Menus'            'list'         'configure/nav-editor'       15 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Widgets'          'grid'         'configure/widget-editor'    16 0 0 1)
    (New-AdminItem 'Configure' 'gear' 2 'Job Clone'        'copy'         'configure/job-clone'        17 0 0 1)

    # Communications
    (New-AdminItem 'Communications' 'envelope' 3 'Bulletins'         'megaphone'     'communications/bulletins'         1 1 1 1)
    (New-AdminItem 'Communications' 'envelope' 3 'Email Log'         'envelope-open' 'communications/email-log'         2 1 1 1)
    (New-AdminItem 'Communications' 'envelope' 3 'Push Notification' 'bell'          'communications/push-notification' 3 1 1 1)

    # LADT
    (New-AdminItem 'LADT' 'diagram-3' 4 'Editor'          'pencil'            'ladt/editor'          1 1 1 1)
    (New-AdminItem 'LADT' 'diagram-3' 4 'Roster Swapper'  'arrow-left-right'  'ladt/roster-swapper'  2 1 1 1)
    (New-AdminItem 'LADT' 'diagram-3' 4 'Pool Assignment' 'people'            'ladt/pool-assignment' 3 1 1 1)

    # Scheduling
    (New-AdminItem 'Scheduling' 'calendar' 5 'Schedule Hub'   'grid'         'scheduling/schedule-hub'   1 1 1 1)
    (New-AdminItem 'Scheduling' 'calendar' 5 'View Schedule'  'eye'          'scheduling/view-schedule'  2 1 1 1)
    (New-AdminItem 'Scheduling' 'calendar' 5 'Rescheduler'    'arrow-repeat' 'scheduling/rescheduler'    3 1 1 1)
    (New-AdminItem 'Scheduling' 'calendar' 5 'Mobile Scorers' 'phone'        'scheduling/mobile-scorers' 4 1 1 1)

    # ARB
    (New-AdminItem 'ARB' 'credit-card' 6 'Health Check' 'heart-pulse' 'arb/health' 1 1 1 1)

    # Tools
    (New-AdminItem 'Tools' 'tools' 7 'US Lax Tester'     'check-circle'  'tools/uslax-test'            1 1 1 1)
    (New-AdminItem 'Tools' 'tools' 7 'US Lax Rankings'   'trophy'        'tools/uslax-rankings'        2 1 1 1)
    (New-AdminItem 'Tools' 'tools' 7 'Profile Migration' 'arrow-right'   'tools/profile-migration'    10 0 0 1)
    (New-AdminItem 'Tools' 'tools' 7 'Profile Editor'    'pencil-square' 'tools/profile-editor'       11 0 0 1)
    (New-AdminItem 'Tools' 'tools' 7 'Change Password'   'key'           'tools/change-password'      12 0 0 1)
    (New-AdminItem 'Tools' 'tools' 7 'Job Revenue'       'cash-stack'    'tools/customer-job-revenue' 13 0 0 1)

    # Store
    (New-AdminItem 'Store' 'cart' 8 'Store Admin' 'shop' 'store/admin' 1 1 1 1)
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

$familyMenu = @(
    (New-L1 1 'Registration' 'pencil-square')
      (New-L2 'Registration' 1 'Register Player' 'person-plus' 'registration/entry')
      (New-L2 'Registration' 2 'Pay Balance Due' 'credit-card' 'registration/player?mode=pay')
    (New-L1 2 'Store' 'cart')
      (New-L2 'Store' 1 'Event Store' 'shop' 'store')
    (New-Leaf 3 'View Rosters' 'people' 'rosters')
)

$clubRepMenu = @(
    (New-L1 1 'Registration' 'pencil-square')
      (New-L2 'Registration' 1 'Register Teams' 'shield-plus' 'registration/entry')
      (New-L2 'Registration' 2 'Club Rosters'   'people'      'club-rosters')
    (New-L1 2 'Accounting' 'cash-stack')
      (New-L2 'Accounting' 1 'Team Accounting' 'receipt' 'registration/team?mode=accounting')
    (New-Leaf 3 'View Rosters' 'people' 'rosters')
)

$playerMenu = @(
    (New-Leaf 1 'View Rosters' 'people' 'rosters')
)

$staffMenu = @(
    (New-Leaf 1 'View Rosters' 'people' 'rosters')
)

# UnassignedAdult: no items

# -- Emit T-SQL ----------------------------------------------------------

function Esc([string]$s) { if ($null -eq $s) { return '' } $s -replace "'", "''" }

$sqlOutputPath = Join-Path $PSScriptRoot '5) Re-Set Nav System.sql'
$sql = [System.Text.StringBuilder]::new()

[void]$sql.AppendLine("-- ============================================================================")
[void]$sql.AppendLine("-- 5) Re-Set Nav System.sql")
[void]$sql.AppendLine("-- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') by 5) Re-Set Nav System.ps1")
[void]$sql.AppendLine("-- Role-scoped manifest; no ladder, no VisibilityRules emitted.")
[void]$sql.AppendLine("-- Preserves: job-level overrides, reporting items, existing visibility rules.")
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
SELECT ni.NavItemId, ni.NavId, ni.ParentNavItemId, ni.Active, ni.SortOrder,
       ni.[Text], ni.IconName, ni.RouterLink, ni.NavigateUrl, ni.[Target]
INTO #ReportingItems
FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId = n.NavId
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
    Controller   NVARCHAR(50)  NOT NULL,
    Icon         NVARCHAR(50)  NULL,
    CtrlSort     INT           NOT NULL,
    [Action]     NVARCHAR(100) NOT NULL,
    ActionIcon   NVARCHAR(50)  NULL,
    RouterLink   NVARCHAR(200) NOT NULL,
    ActionSort   INT           NOT NULL,
    ForDirector  BIT           NOT NULL,
    ForSuperDir  BIT           NOT NULL,
    ForSuperUser BIT           NOT NULL
);
"@)
foreach ($item in $adminManifest) {
    [void]$sql.AppendLine(
        "INSERT INTO #AdminManifest VALUES (" +
        "N'$(Esc $item.Controller)', N'$(Esc $item.ControllerIcon)', $($item.ControllerSort), " +
        "N'$(Esc $item.Text)', N'$(Esc $item.Icon)', N'$(Esc $item.RouterLink)', $($item.ItemSort), " +
        "$($item.ForDirector), $($item.ForSuperDir), $($item.ForSuperUser));"
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

    DECLARE @ctrl NVARCHAR(50), @ctrlIcon NVARCHAR(50), @ctrlSort INT;
    DECLARE ctrl_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT Controller, Icon, CtrlSort
        FROM #AdminManifest
        WHERE CASE @roleId
                 WHEN @Director      THEN ForDirector
                 WHEN @SuperDirector THEN ForSuperDir
                 WHEN @SuperUser     THEN ForSuperUser
                 ELSE 0
             END = 1
        ORDER BY CtrlSort;

    OPEN ctrl_cursor;
    FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified)
        VALUES (@navId, NULL, 1, @ctrlSort, @ctrl, @ctrlIcon, GETDATE());
        SET @parentId = SCOPE_IDENTITY();

        INSERT INTO nav.NavItem (NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, Modified)
        SELECT @navId, @parentId, 1, ActionSort, [Action], ActionIcon, RouterLink, GETDATE()
        FROM #AdminManifest
        WHERE Controller = @ctrl
          AND CASE @roleId
                 WHEN @Director      THEN ForDirector
                 WHEN @SuperDirector THEN ForSuperDir
                 WHEN @SuperUser     THEN ForSuperUser
                 ELSE 0
             END = 1
        ORDER BY ActionSort;

        FETCH NEXT FROM ctrl_cursor INTO @ctrl, @ctrlIcon, @ctrlSort;
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

# 7-12. Per-role hand-authored menus
[void]$sql.AppendLine("-- -- 7. RefAssignor -----------------------------------------------------")
Emit-RoleMenu $sql 'RefAssignor' $refAssignorMenu 'RefAssignor: Referee Assignment + Referee Calendar'

[void]$sql.AppendLine("-- -- 8. StoreAdmin ------------------------------------------------------")
Emit-RoleMenu $sql 'StoreAdmin' $storeAdminMenu 'StoreAdmin: Store Admin'

[void]$sql.AppendLine("-- -- 9. Family ----------------------------------------------------------")
Emit-RoleMenu $sql 'Family' $familyMenu 'Family: Registration + Store + View Rosters'

[void]$sql.AppendLine("-- -- 10. ClubRep --------------------------------------------------------")
Emit-RoleMenu $sql 'ClubRep' $clubRepMenu 'ClubRep: Registration + Accounting + View Rosters'

[void]$sql.AppendLine("-- -- 11. Player ---------------------------------------------------------")
Emit-RoleMenu $sql 'Player' $playerMenu 'Player: View Rosters'

[void]$sql.AppendLine("-- -- 12. Staff ----------------------------------------------------------")
Emit-RoleMenu $sql 'Staff' $staffMenu 'Staff: View Rosters'

[void]$sql.AppendLine("-- UnassignedAdult: Nav row from section 5; no items emitted (intentional).")
[void]$sql.AppendLine("PRINT 'UnassignedAdult: no menu items (intentional)';")
[void]$sql.AppendLine("")

# 13. Restore reporting items
[void]$sql.AppendLine("-- -- 13. Restore preserved reporting items ------------------------------")
[void]$sql.AppendLine(@"
SELECT @cnt = COUNT(*) FROM #ReportingItems;
IF @cnt > 0
BEGIN
    DECLARE @suNavId INT;
    SELECT @suNavId = NavId FROM nav.Nav WHERE RoleId = @SuperUser AND JobId IS NULL;

    DECLARE @apId INT;
    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL)
    BEGIN
        INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, Modified)
        VALUES (@suNavId, NULL, 1, 9, N'Analyze', N'bar-chart', GETDATE());
        SET @apId = SCOPE_IDENTITY();
    END
    ELSE
        SELECT @apId = NavItemId FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL;

    INSERT INTO nav.NavItem(NavId, ParentNavItemId, Active, SortOrder, [Text], IconName, RouterLink, NavigateUrl, [Target], Modified)
    SELECT @suNavId, @apId, r.Active, r.SortOrder, r.[Text], r.IconName, r.RouterLink, r.NavigateUrl, r.[Target], GETDATE()
    FROM #ReportingItems r;
    PRINT CONCAT('Restored ', @cnt, ' reporting item(s) under Analyze');
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
Write-Host " Registrant menus: Family, ClubRep, Player, Staff (hand-authored)" -ForegroundColor Green
Write-Host " Narrow admins: RefAssignor, StoreAdmin (hand-authored)" -ForegroundColor Green
Write-Host " UnassignedAdult: Nav row, no items" -ForegroundColor Green
Write-Host ""
Write-Host " Apply with: sqlcmd -i `"5) Re-Set Nav System.sql`"" -ForegroundColor Green
Write-Host ("=" * 64) -ForegroundColor Green
Write-Host ""
