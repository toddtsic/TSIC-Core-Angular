# ============================================================================
# 5) Re-Set Nav System.ps1
#
# Walks the views/ folder structure to discover all nav-worthy components,
# then generates an idempotent T-SQL script that rebuilds nav platform
# defaults (JobId IS NULL) from scratch.
# Job-level overrides (JobId IS NOT NULL) are preserved.
#
# Source of truth: views/ folder tree (L1 = section, L2 = item)
# Guard metadata: cross-referenced from app.routes.ts
#
# Output: scripts/5) Re-Set Nav System.sql  (run on any target server)
#
# Usage:
#   .\scripts\"5) Re-Set Nav System.ps1"
# ============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host ""
Write-Host "+================================================+" -ForegroundColor Cyan
Write-Host "|   Nav System Reset -- Route-Driven Rebuild    |" -ForegroundColor Cyan
Write-Host "+================================================+" -ForegroundColor Cyan
Write-Host ""

# --Walk views/ folder structure ------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\TSIC-Core-Angular')).Path
$viewsDir = Join-Path $repoRoot 'src\frontend\tsic-app\src\app\views'
if (-not (Test-Path $viewsDir)) {
    Write-Error "Cannot find views/ directory at: $viewsDir"
    return
}

# L1 folders to skip (not admin nav sections)
$skipSections = @('auth', 'errors', 'registration', 'home', 'reporting')

# L2 subfolders to skip (support/internal, not standalone nav items)
$skipSubfolders = @('shared', 'dashboard', 'auto-build', 'wizards', 'wizards-v2')

# Store: only 'admin' subfolder is an admin nav item
$storeNavItems = @('admin')

# Build guard lookup from app.routes.ts (folder = source of truth, routes = guard metadata)
$routesFile = Join-Path $repoRoot 'src\frontend\tsic-app\src\app\app.routes.ts'
$guardMap = @{}
if (Test-Path $routesFile) {
    $routesContent = Get-Content $routesFile -Raw
    # Match all path+data blocks to extract guard levels
    $routePattern = "path:\s*'([^']+)'[^}]*?data:\s*\{([^}]+)\}"
    $routeMatches = [regex]::Matches($routesContent, $routePattern)
    foreach ($m in $routeMatches) {
        $rPath = $m.Groups[1].Value
        $rData = $m.Groups[2].Value
        if ($rData -match 'requireSuperUser') { $guardMap[$rPath] = 'superuser' }
        elseif ($rData -match 'requireAdmin') { $guardMap[$rPath] = 'admin' }
    }
    Write-Host "Guard map: $($guardMap.Count) entries from app.routes.ts" -ForegroundColor DarkGray
}

# Walk the folder tree
$routes = @()
$l1Dirs = Get-ChildItem -Path $viewsDir -Directory | Sort-Object Name

foreach ($l1 in $l1Dirs) {
    $section = $l1.Name
    if ($skipSections -contains $section) { continue }

    $l2Dirs = @(Get-ChildItem -Path $l1.FullName -Directory | Sort-Object Name)

    foreach ($l2 in $l2Dirs) {
        $item = $l2.Name
        if ($skipSubfolders -contains $item) { continue }

        # Store: only include whitelisted subfolders
        if ($section -eq 'store' -and $storeNavItems -notcontains $item) { continue }

        $routePath = "$section/$item"

        # Look up guard: try full path, then just the subfolder name (for nested route children)
        $guard = 'admin'  # default
        if ($guardMap.ContainsKey($routePath)) {
            $guard = $guardMap[$routePath]
        } elseif ($guardMap.ContainsKey($item)) {
            $guard = $guardMap[$item]
        }

        $routes += [PSCustomObject]@{
            Path  = $routePath
            Guard = $guard
        }
    }
}

Write-Host "Discovered $($routes.Count) nav items from $($l1Dirs.Count - $skipSections.Count) sections in views/" -ForegroundColor Green

# --Section & display name mapping ----------------------------------------

$sectionMap = @{
    'configure'      = @{ Text = 'Configure';      Icon = 'gear' }
    'search'         = @{ Text = 'Search';          Icon = 'search' }
    'communications' = @{ Text = 'Communications';  Icon = 'envelope' }
    'ladt'           = @{ Text = 'LADT';            Icon = 'diagram-3' }
    'scheduling'     = @{ Text = 'Scheduling';      Icon = 'calendar' }
    'arb'            = @{ Text = 'ARB';             Icon = 'credit-card' }
    'tools'          = @{ Text = 'Tools';           Icon = 'tools' }
    'store'          = @{ Text = 'Store';           Icon = 'cart' }
    'home'           = @{ Text = 'Home';            Icon = 'house' }
}

$itemNameOverrides = @{
    'configure/job'              = 'Job Settings'
    'configure/ddl-options'      = 'Dropdown Options'
    'configure/nav-editor'       = 'Menus'
    'configure/widget-editor'    = 'Widgets'
    'configure/job-clone'        = 'Job Clone'
    'configure/uniform-upload'   = 'Uniform Upload'
    'configure/age-ranges'       = 'Age Ranges'
    'configure/discount-codes'   = 'Discount Codes'
    'configure/customer-groups'  = 'Customer Groups'
    'ladt/editor'                = 'Editor'
    'ladt/roster-swapper'        = 'Roster Swapper'
    'ladt/pool-assignment'       = 'Pool Assignment'
    'arb/health'                 = 'Health Check'
    'tools/uslax-test'           = 'US Lax Tester'
    'tools/uslax-rankings'       = 'US Lax Rankings'
    'tools/profile-migration'    = 'Profile Migration'
    'tools/profile-editor'       = 'Profile Editor'
    'tools/change-password'      = 'Change Password'
    'tools/customer-job-revenue' = 'Job Revenue'
    'store/admin'                = 'Store Admin'
    'scheduling/schedule-hub'    = 'Schedule Hub'
    'scheduling/view-schedule'   = 'View Schedule'
    'scheduling/master-schedule' = 'Master Schedule'
    'scheduling/mobile-scorers'  = 'Mobile Scorers'
    'scheduling/tournament-parking' = 'Tournament Parking'
    'scheduling/bracket-seeds'   = 'Bracket Seeds'
    'scheduling/referee-assignment' = 'Referee Assignment'
    'scheduling/referee-calendar'   = 'Referee Calendar'
    'scheduling/qa-results'      = 'QA Results'
    'communications/email-log'   = 'Email Log'
    'communications/push-notification' = 'Push Notification'
}

$itemIconMap = @{
    'search/registrations'       = 'people'
    'search/teams'               = 'shield'
    'configure/job'              = 'briefcase'
    'configure/age-ranges'       = 'sliders'
    'configure/discount-codes'   = 'tags'
    'configure/uniform-upload'   = 'upload'
    'tools/uniform-upload'       = 'upload'
    'configure/administrators'   = 'person-badge'
    'configure/customer-groups'  = 'people'
    'configure/ddl-options'      = 'list'
    'configure/customers'        = 'building'
    'configure/theme'            = 'palette'
    'configure/nav-editor'       = 'list'
    'configure/widget-editor'    = 'grid'
    'configure/job-clone'        = 'copy'
    'communications/bulletins'   = 'megaphone'
    'communications/email-log'   = 'envelope-open'
    'communications/push-notification' = 'bell'
    'ladt/editor'                = 'pencil'
    'ladt/roster-swapper'        = 'arrow-left-right'
    'ladt/pool-assignment'       = 'people'
    'scheduling/schedule-hub'    = 'grid'
    'scheduling/view-schedule'   = 'eye'
    'scheduling/master-schedule' = 'table'
    'scheduling/rescheduler'     = 'arrow-repeat'
    'scheduling/mobile-scorers'  = 'phone'
    'scheduling/tournament-parking' = 'p-circle'
    'scheduling/bracket-seeds'   = 'trophy'
    'scheduling/referee-assignment' = 'person-check'
    'scheduling/referee-calendar'   = 'calendar-check'
    'scheduling/qa-results'      = 'clipboard-check'
    'scheduling/fields'          = 'geo-alt'
    'scheduling/pairings'        = 'arrow-left-right'
    'scheduling/timeslots'       = 'clock'
    'arb/health'                 = 'heart-pulse'
    'tools/uslax-test'           = 'check-circle'
    'tools/uslax-rankings'       = 'trophy'
    'tools/profile-migration'    = 'arrow-right'
    'tools/profile-editor'       = 'pencil-square'
    'tools/change-password'      = 'key'
    'tools/customer-job-revenue' = 'cash-stack'
    'store/admin'                = 'shop'
    'home'                       = 'house'
    'brand-preview'              = 'palette2'
}

# --Helper: kebab-case to Title Case -------------------------------------

function ConvertTo-TitleCase([string]$kebab) {
    ($kebab -split '-' | ForEach-Object {
        if ($_.Length -gt 0) { $_.Substring(0,1).ToUpper() + $_.Substring(1) } else { $_ }
    }) -join ' '
}

# --Build manifest --------------------------------------------------------

$manifest = @()
$sectionSortCounter = @{}

$sortedRoutes = $routes | Sort-Object Path

foreach ($route in $sortedRoutes) {
    $segments = $route.Path -split '/'
    $section = $segments[0]

    $sectionInfo = if ($sectionMap.ContainsKey($section)) {
        $sectionMap[$section]
    } else {
        @{ Text = (ConvertTo-TitleCase $section); Icon = 'folder' }
    }

    $itemText = if ($itemNameOverrides.ContainsKey($route.Path)) {
        $itemNameOverrides[$route.Path]
    } elseif ($segments.Count -gt 1) {
        ConvertTo-TitleCase $segments[-1]
    } else {
        $sectionInfo.Text
    }

    $itemIcon = if ($itemIconMap.ContainsKey($route.Path)) {
        $itemIconMap[$route.Path]
    } else {
        'circle'
    }

    if (-not $sectionSortCounter.ContainsKey($section)) {
        $sectionSortCounter[$section] = 1
    } else {
        $sectionSortCounter[$section]++
    }

    $manifest += [PSCustomObject]@{
        Section     = $sectionInfo.Text
        SectionIcon = $sectionInfo.Icon
        ItemText    = $itemText
        ItemIcon    = $itemIcon
        RouterLink  = $route.Path
        ItemSort    = $sectionSortCounter[$section]
        Guard       = $route.Guard
    }
}

$sectionOrder = @(
    'Search', 'Configure', 'Communications', 'LADT',
    'Scheduling', 'ARB', 'Tools', 'Store', 'Home'
)
$sectionSortMap = @{}
for ($i = 0; $i -lt $sectionOrder.Count; $i++) {
    $sectionSortMap[$sectionOrder[$i]] = $i + 1
}

# --Display manifest -----------------------------------------------------

Write-Host ""
Write-Host "Nav Manifest:" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

$currentSection = ''
foreach ($item in $manifest | Sort-Object { if ($sectionSortMap.ContainsKey($_.Section)) { $sectionSortMap[$_.Section] } else { 99 } }, ItemSort) {
    if ($item.Section -ne $currentSection) {
        $currentSection = $item.Section
        $sSort = if ($sectionSortMap.ContainsKey($currentSection)) { $sectionSortMap[$currentSection] } else { 99 }
        Write-Host ""
        Write-Host "  [$sSort] $currentSection (bi-$($item.SectionIcon))" -ForegroundColor Cyan
    }
    $guardTag = if ($item.Guard -eq 'superuser') { ' [SU]' } else { '' }
    Write-Host "      $($item.ItemSort). $($item.ItemText)  ->  $($item.RouterLink)  (bi-$($item.ItemIcon))$guardTag" -ForegroundColor White
}

Write-Host ""
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "Total: $($manifest.Count) items in $($manifest | Select-Object -ExpandProperty Section -Unique | Measure-Object | Select-Object -ExpandProperty Count) sections" -ForegroundColor Green
Write-Host ""

# --Generate idempotent T-SQL script ---------------------------------------

$sqlOutputPath = Join-Path $PSScriptRoot '5) Re-Set Nav System.sql'

$sql = [System.Text.StringBuilder]::new()

[void]$sql.AppendLine("-- ============================================================================")
[void]$sql.AppendLine("-- 5) Re-Set Nav System.sql")
[void]$sql.AppendLine("-- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') by 5) Re-Set Nav System.ps1")
[void]$sql.AppendLine("-- Idempotent: safe to run multiple times on any target database")
[void]$sql.AppendLine("-- Preserves: job-level overrides, reporting items, visibility rules")
[void]$sql.AppendLine("-- ============================================================================")
[void]$sql.AppendLine("")
[void]$sql.AppendLine("SET NOCOUNT ON;")
[void]$sql.AppendLine("SET XACT_ABORT ON;")
[void]$sql.AppendLine("BEGIN TRANSACTION;")
[void]$sql.AppendLine("")

# --1. Ensure schema + tables exist --

[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("-- 1. Ensure schema + tables exist")
[void]$sql.AppendLine("-- ================================================================")
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

# --2. Preserve reporting items + visibility rules --

[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("-- 2. Preserve reporting items + visibility rules")
[void]$sql.AppendLine("-- ================================================================")
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

# --3. Clear platform defaults --

[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("-- 3. Clear platform defaults (job overrides preserved)")
[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine(@"
DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);
DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;
PRINT 'Cleared platform defaults (job overrides preserved)';
"@)
[void]$sql.AppendLine("")

# --4. Insert Nav records (one per role) --

$roleGuids = @{
    Director       = 'FF4D1C27-F6DA-4745-98CC-D7E8121A5D06'
    SuperDirector  = '7B9EB503-53C9-44FA-94A0-17760C512440'
    SuperUser      = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9'
    Staff          = '1DB2EBF0-F12B-43DC-A960-CFC7DD4642FA'
    RefAssignor    = '122075A3-2C42-4092-97F1-9673DF5B6A2C'
    StoreAdmin     = '5B9B7055-4530-4E46-B403-1019FD8B8418'
    Family         = 'E0A8A5C3-A36C-417F-8312-E7083F1AA5A0'
    Player         = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A'
    ClubRep        = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'
    UnassignedAdult = 'CE2CB370-5880-4624-A43E-048379C64331'
}

[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("-- 4. Insert Nav records (one per role)")
[void]$sql.AppendLine("-- ================================================================")
foreach ($roleId in $roleGuids.Values) {
    [void]$sql.AppendLine("INSERT INTO [nav].[Nav]([RoleId],[JobId],[Active],[Modified]) VALUES('$roleId',NULL,1,GETDATE());")
}
[void]$sql.AppendLine("PRINT 'Inserted $($roleGuids.Count) Nav records';")
[void]$sql.AppendLine("")

# --5. Admin roles: insert sections + items using T-SQL variables --

[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("-- 5. Insert nav items per admin role")
[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("DECLARE @navId INT, @parentId INT;")
[void]$sql.AppendLine("")

$adminRoles = @('Director','SuperDirector','SuperUser','Staff','RefAssignor','StoreAdmin')

foreach ($roleName in $adminRoles) {
    $roleId = $roleGuids[$roleName]
    $guardLevel = if ($roleName -eq 'SuperUser') { 'superuser' } else { 'admin' }

    [void]$sql.AppendLine("-- --- $roleName ---")
    [void]$sql.AppendLine("SELECT @navId = NavId FROM nav.Nav WHERE RoleId = '$roleId' AND JobId IS NULL;")

    # Get sections visible to this role
    $visibleSections = @($manifest | Where-Object {
        $_.Guard -eq 'admin' -or ($guardLevel -eq 'superuser' -and $_.Guard -eq 'superuser')
    } | Select-Object -ExpandProperty Section -Unique)

    foreach ($sectionName in $visibleSections) {
        $sSort = if ($sectionSortMap.ContainsKey($sectionName)) { $sectionSortMap[$sectionName] } else { 99 }
        $sIcon = ($manifest | Where-Object { $_.Section -eq $sectionName } | Select-Object -First 1).SectionIcon

        # Get items in this section visible to this role
        $sectionItems = @($manifest | Where-Object {
            $_.Section -eq $sectionName -and
            ($_.Guard -eq 'admin' -or ($guardLevel -eq 'superuser' -and $_.Guard -eq 'superuser'))
        } | Sort-Object ItemSort)

        if ($sectionItems.Count -eq 0) { continue }

        $escapedSection = $sectionName -replace "'", "''"
        [void]$sql.AppendLine("INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified) VALUES(@navId,NULL,1,$sSort,N'$escapedSection',N'$sIcon',GETDATE());")
        [void]$sql.AppendLine("SET @parentId = SCOPE_IDENTITY();")

        foreach ($item in $sectionItems) {
            $escapedText = $item.ItemText -replace "'", "''"
            [void]$sql.AppendLine("INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,Modified) VALUES(@navId,@parentId,1,$($item.ItemSort),N'$escapedText',N'$($item.ItemIcon)',N'$($item.RouterLink)',GETDATE());")
        }
    }

    [void]$sql.AppendLine("")
    Write-Host "  $roleName`: $($visibleSections.Count) sections" -ForegroundColor DarkGray
}

Write-Host "  Non-admin roles (Family, Player, ClubRep, UnassignedAdult): nav records only" -ForegroundColor DarkGray

# --6. Restore reporting items --

[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("-- 6. Restore reporting items")
[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine(@"
SELECT @cnt = COUNT(*) FROM #ReportingItems;
IF @cnt > 0
BEGIN
    DECLARE @suNavId INT;
    SELECT @suNavId = NavId FROM nav.Nav WHERE RoleId = 'CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9' AND JobId IS NULL;

    DECLARE @apId INT;
    IF NOT EXISTS (SELECT 1 FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL)
    BEGIN
        INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,Modified)
        VALUES(@suNavId,NULL,1,9,N'Analyze',N'bar-chart',GETDATE());
        SET @apId = SCOPE_IDENTITY();
    END
    ELSE
        SELECT @apId = NavItemId FROM nav.NavItem WHERE NavId = @suNavId AND [Text] = 'Analyze' AND ParentNavItemId IS NULL;

    INSERT INTO nav.NavItem(NavId,ParentNavItemId,Active,SortOrder,[Text],IconName,RouterLink,NavigateUrl,[Target],Modified)
    SELECT @suNavId, @apId, r.Active, r.SortOrder, r.[Text], r.IconName, r.RouterLink, r.NavigateUrl, r.[Target], GETDATE()
    FROM #ReportingItems r;
    PRINT CONCAT('Restored ', @cnt, ' reporting item(s) under Analyze section');
END
DROP TABLE #ReportingItems;
"@)
[void]$sql.AppendLine("")

# --7. Restore visibility rules --

[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("-- 7. Restore visibility rules")
[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine(@"
UPDATE ni
SET ni.VisibilityRules = vr.VisibilityRules
FROM nav.NavItem ni
JOIN nav.Nav n ON ni.NavId = n.NavId
JOIN #VisRules vr ON vr.RoleId = n.RoleId AND vr.RouterLink = ni.RouterLink
WHERE n.JobId IS NULL;
PRINT CONCAT('Restored ', @@ROWCOUNT, ' visibility rule(s)');
DROP TABLE #VisRules;
"@)
[void]$sql.AppendLine("")

# --8. Summary + commit --

[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine("-- 8. Summary")
[void]$sql.AppendLine("-- ================================================================")
[void]$sql.AppendLine(@"
COMMIT TRANSACTION;

SELECT
    (SELECT COUNT(*) FROM nav.Nav WHERE JobId IS NULL) AS [Platform Navs],
    (SELECT COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId=n.NavId WHERE n.JobId IS NULL) AS [Platform Items],
    (SELECT COUNT(*) FROM nav.NavItem ni JOIN nav.Nav n ON ni.NavId=n.NavId WHERE n.JobId IS NULL AND ni.ParentNavItemId IS NULL) AS [Sections],
    (SELECT COUNT(*) FROM nav.Nav WHERE JobId IS NOT NULL) AS [Job Overrides (preserved)];

PRINT 'Nav system reset complete.';
"@)

# --Write the file --------------------------------------------------------

$sql.ToString() | Set-Content -Path $sqlOutputPath -Encoding UTF8
Write-Host ""
Write-Host ("=" * 64) -ForegroundColor Green
Write-Host " Generated: $sqlOutputPath" -ForegroundColor Green
Write-Host " $($manifest.Count) nav items across $($adminRoles.Count) admin roles" -ForegroundColor Green
Write-Host " Copy to server and run with: sqlcmd -i `"5) Re-Set Nav System.sql`"" -ForegroundColor Green
Write-Host ("=" * 64) -ForegroundColor Green
Write-Host ""
