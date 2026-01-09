/*******************************************************************************
 * Seed ClubTeams Library from 2025/2026 Teams Data
 * 
 * Purpose: Create Clubs, ClubReps, and ClubTeams from existing Teams records 
 *          in 2025/2026 jobs and link Teams.ClubTeamId to ClubTeams.
 * 
 * Parameters:
 *   @bCleanOutFirst - If 1, clears all existing Club data before building
 *   @StartYear      - Starting year (inclusive, default 2025)
 *   @EndYear        - Ending year (inclusive, default 2026)
 * 
 * Usage:
 *   -- Incremental mode (default - preserve existing Club data):
 *   EXEC Clubs.SeedClubTeamsFrom2025 @bCleanOutFirst = 0, @StartYear = 2025, @EndYear = 2026;
 *   
 *   -- Clean mode (delete all existing Club data first):
 *   EXEC Clubs.SeedClubTeamsFrom2025 @bCleanOutFirst = 1, @StartYear = 2025, @EndYear = 2026;
 *   
 *   -- Custom year range:
 *   EXEC Clubs.SeedClubTeamsFrom2025 @StartYear = 2024, @EndYear = 2027;
 * 
 * Scope:   Only Teams with clubrep_registrationId NOT NULL from specified year range
 * 
 * Strategy:
 *   1. Optional: Clean out existing Club data
 *   2. Create Clubs from unique club representatives
 *   3. Create ClubReps linking users to clubs
 *   4. Identify unique club team identities by grouping on:
 *      - ClubId, TeamName, ClubTeamGradYear, level_of_play
 *   5. Insert into ClubTeams for each unique identity
 *   6. Update Teams.ClubTeamId to reference the created ClubTeams
 * 
 * Author:  GitHub Copilot
 * Date:    2026-01-07
 *******************************************************************************/

/*******************************************************************************
 * DATA CLEANUP QUERIES - Run BEFORE migration to fix typos in source data
 *******************************************************************************/

-- Fix typo: "3d Georgoa" → "3d Georgia"
/*
UPDATE r 
SET club_name = '3d Georgia'
FROM Jobs.Registrations r
INNER JOIN Jobs.Jobs j ON r.JobId = j.JobId
WHERE j.JobName = 'Live Love Lax:Girls Summer 2026'
  AND r.club_name = '3d Georgoa';

update r set Userid = '344b58cf-863b-4441-b906-7fa642ef4ad1'
from
	Jobs.Registrations r 
	inner join Jobs.Jobs j on r.jobId = j.jobId
	inner join dbo.AspNetUsers u on r.Userid = u.Id
	inner join dbo.AspNetRoles roles on r.RoleID = roles.id
where
	u.username = 'stepsnj'
	and roles.Name = 'club rep'
	and j.jobName like '%isp%'*/

-- Add additional data cleanup queries here as needed
-- Template:
/*
UPDATE r 
SET club_name = 'Corrected Club Name'
FROM Jobs.Registrations r
INNER JOIN Jobs.Jobs j ON r.JobId = j.JobId
WHERE j.JobName = 'Specific Job Name'
  AND r.club_name = 'Typo Club Name';
*/

/*******************************************************************************
 * END DATA CLEANUP QUERIES
 *******************************************************************************/

/*******************************************************************************
 * Helper Function: Clean team name by removing club prefix
 * Returns the team name with club prefix removed, using flexible token-based matching
 * Handles variant abbreviations (e.g., "3DG" for "3D Georgia", "3d" for "3D Georgia")
 *******************************************************************************/
IF OBJECT_ID('Clubs.fnCleanTeamName', 'FN') IS NOT NULL
    DROP FUNCTION Clubs.fnCleanTeamName;
GO

CREATE FUNCTION Clubs.fnCleanTeamName(
    @TeamName NVARCHAR(255),
    @ClubName NVARCHAR(255),
    @GradYear NVARCHAR(50)
)
RETURNS NVARCHAR(255)
AS
BEGIN
    DECLARE @TrimmedTeam NVARCHAR(255) = LTRIM(RTRIM(@TeamName));
    DECLARE @TrimmedClub NVARCHAR(255) = LTRIM(RTRIM(@ClubName));
    
    -- Parse club name into tokens (up to 4 words)
    DECLARE @FirstSpace INT = CHARINDEX(' ', @TrimmedClub);
    DECLARE @SecondSpace INT = CHARINDEX(' ', @TrimmedClub, @FirstSpace + 1);
    DECLARE @ThirdSpace INT = CHARINDEX(' ', @TrimmedClub, @SecondSpace + 1);
    
    DECLARE @Word1 NVARCHAR(100) = CASE WHEN @FirstSpace > 0 THEN LEFT(@TrimmedClub, @FirstSpace - 1) ELSE @TrimmedClub END;
    DECLARE @Word2 NVARCHAR(100) = CASE WHEN @SecondSpace > 0 THEN SUBSTRING(@TrimmedClub, @FirstSpace + 1, @SecondSpace - @FirstSpace - 1) 
                                        WHEN @FirstSpace > 0 THEN SUBSTRING(@TrimmedClub, @FirstSpace + 1, 8000) ELSE NULL END;
    DECLARE @Word3 NVARCHAR(100) = CASE WHEN @ThirdSpace > 0 THEN SUBSTRING(@TrimmedClub, @SecondSpace + 1, @ThirdSpace - @SecondSpace - 1)
                                        WHEN @SecondSpace > 0 THEN SUBSTRING(@TrimmedClub, @SecondSpace + 1, 8000) ELSE NULL END;
    DECLARE @Word4 NVARCHAR(100) = CASE WHEN @ThirdSpace > 0 THEN SUBSTRING(@TrimmedClub, @ThirdSpace + 1, 8000) ELSE NULL END;
    
    -- Strategy 1: Exact full club name match → return grad year
    IF @TrimmedTeam = @TrimmedClub COLLATE Latin1_General_CI_AI
        RETURN @GradYear;
    
    -- Strategy 2: Full club name prefix (most common case - check first)
    IF @TrimmedTeam LIKE @TrimmedClub + ' %' COLLATE Latin1_General_CI_AI
        RETURN LTRIM(SUBSTRING(@TrimmedTeam, LEN(@TrimmedClub) + 2, 8000));
    
    -- Strategy 3: Progressive token matching for multi-word clubs
    IF @Word2 IS NOT NULL
    BEGIN
        -- Try first 2 words (common for 3+ word clubs)
        IF @Word3 IS NOT NULL AND @TrimmedTeam LIKE @Word1 + ' ' + @Word2 + ' %' COLLATE Latin1_General_CI_AI
            RETURN LTRIM(SUBSTRING(@TrimmedTeam, LEN(@Word1 + ' ' + @Word2) + 2, 8000));
        
        -- Try second word alone (for two-word clubs)
        IF @Word3 IS NULL AND @TrimmedTeam LIKE @Word2 + ' %' COLLATE Latin1_General_CI_AI
            RETURN LTRIM(SUBSTRING(@TrimmedTeam, LEN(@Word2) + 2, 8000));
        
        -- Try first word (with safeguard to avoid over-stripping)
        IF @TrimmedTeam LIKE @Word1 + ' %' COLLATE Latin1_General_CI_AI
        BEGIN
            DECLARE @Remainder NVARCHAR(255) = LTRIM(SUBSTRING(@TrimmedTeam, LEN(@Word1) + 2, 8000));
            IF @Remainder <> @Word2 COLLATE Latin1_General_CI_AI
                RETURN @Remainder;
        END
        
        -- Check if team equals first word alone → return grad year
        IF @TrimmedTeam = @Word1 COLLATE Latin1_General_CI_AI
            RETURN @GradYear;
    END
    
    -- Strategy 4: Abbreviation matching (handles "3DG", "3G", etc.)
    IF @Word2 IS NOT NULL
    BEGIN
        DECLARE @TeamPrefix NVARCHAR(10);
        DECLARE @PrefixLen INT;
        
        -- Extract potential abbreviation prefix (up to first space in team name)
        DECLARE @TeamFirstSpace INT = CHARINDEX(' ', @TrimmedTeam);
        IF @TeamFirstSpace > 0
        BEGIN
            SET @TeamPrefix = LEFT(@TrimmedTeam, @TeamFirstSpace - 1);
            SET @PrefixLen = LEN(@TeamPrefix);
            
            -- Only check abbreviations if prefix is 2-4 characters
            IF @PrefixLen >= 2 AND @PrefixLen <= 4
            BEGIN
                -- Check if prefix matches: Standard initials (e.g., "3G")
                DECLARE @StdInitials NVARCHAR(10) = LEFT(@Word1, 1) + LEFT(@Word2, 1);
                IF @TeamPrefix = @StdInitials COLLATE Latin1_General_CI_AI
                    RETURN LTRIM(SUBSTRING(@TrimmedTeam, @TeamFirstSpace + 1, 8000));
                
                -- Check if prefix matches: First word + initial (e.g., "3DG")
                DECLARE @Word1Initial NVARCHAR(10) = @Word1 + LEFT(@Word2, 1);
                IF @TeamPrefix = @Word1Initial COLLATE Latin1_General_CI_AI
                    RETURN LTRIM(SUBSTRING(@TrimmedTeam, @TeamFirstSpace + 1, 8000));
            END
        END
    END
    ELSE -- Single-word club
    BEGIN
        -- Try first 2-3 characters for single-word clubs
        IF LEN(@Word1) > 3 AND @TrimmedTeam LIKE LEFT(@Word1, 3) + ' %' COLLATE Latin1_General_CI_AI
            RETURN LTRIM(SUBSTRING(@TrimmedTeam, 4, 8000));
        
        IF @TrimmedTeam LIKE LEFT(@Word1, 2) + ' %' COLLATE Latin1_General_CI_AI
            RETURN LTRIM(SUBSTRING(@TrimmedTeam, 3, 8000));
    END
    
    -- Fallback: return original
    RETURN @TrimmedTeam;
END
GO

IF OBJECT_ID('Clubs.SeedClubTeamsFrom2025', 'P') IS NOT NULL
    DROP PROCEDURE Clubs.SeedClubTeamsFrom2025;
GO

CREATE PROCEDURE Clubs.SeedClubTeamsFrom2025
    @bCleanOutFirst BIT = 0,
    @StartYear INT = 2025,
    @EndYear INT = 2026
AS
BEGIN
    SET NOCOUNT ON;

    -- Variables for tracking
    DECLARE @ClubsCreated INT = 0;
    DECLARE @ClubRepsCreated INT = 0;
    DECLARE @ClubTeamsCreated INT = 0;
    DECLARE @TeamsUpdated INT = 0;
    DECLARE @LebUserId NVARCHAR(450) = '71765055-647D-432E-AFB6-0F84218D0247'; -- TSICSuperUser
    DECLARE @Now DATETIME = GETUTCDATE();

    PRINT '========================================';
    PRINT 'ClubTeams Seeding Migration - Start';
    PRINT '========================================';
    PRINT 'Timestamp: ' + CONVERT(VARCHAR(30), @Now, 121);
    PRINT 'Clean Out First: ' + CASE WHEN @bCleanOutFirst = 1 THEN 'Yes' ELSE 'No' END;
    PRINT 'Year Range: ' + CAST(@StartYear AS VARCHAR) + ' - ' + CAST(@EndYear AS VARCHAR);
    PRINT '';

    -- ============================================================================
    -- STEP 0: Optional - Clean out existing Club data
    -- ============================================================================
    IF @bCleanOutFirst = 1
    BEGIN
        PRINT 'STEP 0: Cleaning out existing Club data...';
        PRINT '';

        BEGIN TRANSACTION;

        BEGIN TRY
            -- Clear Teams.ClubTeamId references
            UPDATE Leagues.Teams SET ClubTeamId = NULL 
            WHERE ClubTeamId IS NOT NULL;
            DECLARE @TeamsCleared INT = @@ROWCOUNT;

            -- Delete all ClubTeams
            DELETE FROM Clubs.ClubTeams;
            DECLARE @ClubTeamsDeleted INT = @@ROWCOUNT;

            -- Delete all ClubReps
            DELETE FROM Clubs.ClubReps;
            DECLARE @ClubRepsDeleted INT = @@ROWCOUNT;

            -- Delete all Clubs
            DELETE FROM Clubs.Clubs;
            DECLARE @ClubsDeleted INT = @@ROWCOUNT;

            COMMIT TRANSACTION;

            PRINT '  ✓ Cleared ' + CAST(@TeamsCleared AS VARCHAR) + ' Teams.ClubTeamId references';
            PRINT '  ✓ Deleted ' + CAST(@ClubTeamsDeleted AS VARCHAR) + ' ClubTeams';
            PRINT '  ✓ Deleted ' + CAST(@ClubRepsDeleted AS VARCHAR) + ' ClubReps';
            PRINT '  ✓ Deleted ' + CAST(@ClubsDeleted AS VARCHAR) + ' Clubs';
            PRINT '';

        END TRY
        BEGIN CATCH
            ROLLBACK TRANSACTION;
            
            PRINT '';
            PRINT 'ERROR: Failed to clean out Club data!';
            PRINT 'Error Message: ' + ERROR_MESSAGE();
            PRINT 'Error Line: ' + CAST(ERROR_LINE() AS VARCHAR);
            
            THROW;
        END CATCH
    END

    -- ============================================================================
    -- STEP 1: Validation - Check prerequisites
    -- ============================================================================
    PRINT 'STEP 1: Validating prerequisites...';

-- Check if ClubTeams table exists and has correct schema
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ClubTeams' AND SCHEMA_NAME(schema_id) = 'Clubs')
BEGIN
    PRINT 'ERROR: Clubs.ClubTeams table does not exist!';
    RAISERROR('Clubs.ClubTeams table missing', 16, 1);
    RETURN;
END

-- Check if Teams.ClubTeamId column exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Leagues.Teams') AND name = 'ClubTeamId')
BEGIN
    PRINT 'ERROR: Leagues.Teams.ClubTeamId column does not exist!';
    RAISERROR('Leagues.Teams.ClubTeamId column missing', 16, 1);
    RETURN;
END

PRINT '  ✓ Schema validation passed';
PRINT '';

-- ============================================================================
-- STEP 2: Analysis - Count eligible Teams records
-- ============================================================================
PRINT 'STEP 2: Analyzing eligible Teams records...';

DECLARE @EligibleTeamsCount INT;

SELECT @EligibleTeamsCount = COUNT(*)
FROM Leagues.Teams t
INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
INNER JOIN Leagues.Agegroups ag ON t.AgegroupId = ag.AgegroupId
WHERE ISNUMERIC(j.Year) = 1
  AND CAST(j.Year AS INT) >= @StartYear
  AND CAST(j.Year AS INT) <= @EndYear
  AND t.clubrep_registrationId IS NOT NULL;

PRINT '  Found ' + CAST(@EligibleTeamsCount AS VARCHAR) + ' Teams records from ' + CAST(@StartYear AS VARCHAR) + '-' + CAST(@EndYear AS VARCHAR) + ' with clubrep_registrationId';

IF @EligibleTeamsCount = 0
BEGIN
    PRINT '  WARNING: No eligible Teams found. Nothing to seed.';
    RETURN;
END

PRINT '';

-- ============================================================================
-- STEP 3: Create Clubs from unique club representatives
-- ============================================================================
PRINT 'STEP 3: Creating Clubs from 2025/2026 Teams data...';
PRINT '';

BEGIN TRANSACTION;

BEGIN TRY
    -- Build staging table of unique (UserId, ClubName) combinations
    -- Business rules: One user CAN rep multiple clubs, one club CAN have multiple reps
    IF OBJECT_ID('tempdb..#UniqueClubReps') IS NOT NULL
        DROP TABLE #UniqueClubReps;

    CREATE TABLE #UniqueClubReps (
        UserId NVARCHAR(450) NOT NULL,
        ClubName NVARCHAR(255) NOT NULL
    );
    
    -- Create unique nonclustered index to avoid 900-byte limit
    CREATE UNIQUE NONCLUSTERED INDEX IX_UniqueClubReps_UserClub 
        ON #UniqueClubReps (UserId, ClubName);

    -- Extract unique (UserId, ClubName) combinations from Teams data
    INSERT INTO #UniqueClubReps (UserId, ClubName)
    SELECT DISTINCT
        r.UserId,
        COALESCE(LTRIM(RTRIM(r.club_name)), 'N.A.') AS ClubName
    FROM Leagues.Teams t
    INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
    INNER JOIN Jobs.Registrations r ON t.clubrep_registrationId = r.RegistrationId
    INNER JOIN dbo.AspNetUsers u ON r.UserId = u.Id
    WHERE ISNUMERIC(j.Year) = 1
      AND CAST(j.Year AS INT) >= @StartYear
      AND CAST(j.Year AS INT) <= @EndYear
      AND t.clubrep_registrationId IS NOT NULL;

    DECLARE @UniqueClubRepsCount INT = @@ROWCOUNT;
    PRINT '  Found ' + CAST(@UniqueClubRepsCount AS VARCHAR) + ' unique (UserId, ClubName) pairs';

    -- Insert Clubs (one per unique ClubName, even if multiple users rep the same club)
    -- Only insert NEW clubs that don't already exist
    INSERT INTO Clubs.Clubs (ClubName, LebUserId, Modified)
    SELECT DISTINCT
        ucr.ClubName,
        @LebUserId,
        @Now
    FROM #UniqueClubReps ucr
    WHERE NOT EXISTS (
        SELECT 1 FROM Clubs.Clubs c WHERE c.ClubName = ucr.ClubName
    );

    SET @ClubsCreated = @@ROWCOUNT;
    PRINT '  ✓ Created ' + CAST(@ClubsCreated AS VARCHAR) + ' Clubs';

    -- Insert ClubReps (link UserId to ClubId)
    -- Create relationships for ALL clubs in #UniqueClubReps, not just newly created ones
    INSERT INTO Clubs.ClubReps (ClubId, ClubRepUserId, LebUserId, Modified)
    SELECT 
        c.ClubId,
        ucr.UserId,
        @LebUserId,
        @Now
    FROM #UniqueClubReps ucr
    INNER JOIN Clubs.Clubs c ON c.ClubName = ucr.ClubName
    WHERE NOT EXISTS (
        SELECT 1 FROM Clubs.ClubReps cr 
        WHERE cr.ClubId = c.ClubId AND cr.ClubRepUserId = ucr.UserId
    );

    SET @ClubRepsCreated = @@ROWCOUNT;
    PRINT '  ✓ Created ' + CAST(@ClubRepsCreated AS VARCHAR) + ' ClubReps';

    COMMIT TRANSACTION;
    PRINT '  ✓ Clubs and ClubReps created successfully';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    
    PRINT '';
    PRINT 'ERROR: Failed to create Clubs/ClubReps!';
    PRINT 'Error Message: ' + ERROR_MESSAGE();
    PRINT 'Error Line: ' + CAST(ERROR_LINE() AS VARCHAR);
    
    THROW;
END CATCH

DROP TABLE #UniqueClubReps;

PRINT '';

-- ============================================================================
-- STEP 4: Create staging table with resolved ClubId and grouping keys
-- ============================================================================
PRINT 'STEP 4: Building staging table with resolved relationships...';

-- Phase 1: Extract basic data
IF OBJECT_ID('tempdb..#Phase1') IS NOT NULL DROP TABLE #Phase1;

SELECT 
    t.TeamId,
    cr.ClubId,
    c.ClubName,
    j.JobName,
    t.TeamName AS OriginalTeamName,
    CASE WHEN ag.AgegroupName LIKE 'WAITLIST - %' AND LEN(ag.AgegroupName) > 11
         THEN LTRIM(SUBSTRING(ag.AgegroupName, 12, LEN(ag.AgegroupName) - 11))
         ELSE ag.AgegroupName END AS RawGradYear,
    t.level_of_play AS LevelOfPlay
INTO #Phase1
FROM Leagues.Teams t
INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
INNER JOIN Leagues.Agegroups ag ON t.AgegroupId = ag.AgegroupId
INNER JOIN Jobs.Registrations r ON t.clubrep_registrationId = r.RegistrationId
INNER JOIN Clubs.Clubs c ON COALESCE(LTRIM(RTRIM(r.club_name)), 'N.A.') = c.ClubName
INNER JOIN Clubs.ClubReps cr ON r.UserId = cr.ClubRepUserId AND cr.ClubId = c.ClubId
WHERE ISNUMERIC(j.Year) = 1
  AND CAST(j.Year AS INT) >= @StartYear
  AND CAST(j.Year AS INT) <= @EndYear
  AND t.clubrep_registrationId IS NOT NULL
  AND cr.ClubId IS NOT NULL;

DECLARE @Phase1Count INT = @@ROWCOUNT;

-- Diagnostic: Check for teams that were excluded due to missing ClubReps
DECLARE @ExcludedTeamsCount INT;
SELECT @ExcludedTeamsCount = COUNT(*)
FROM Leagues.Teams t
INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
INNER JOIN Jobs.Registrations r ON t.clubrep_registrationId = r.RegistrationId
LEFT JOIN Clubs.Clubs c ON COALESCE(LTRIM(RTRIM(r.club_name)), 'N.A.') = c.ClubName
LEFT JOIN Clubs.ClubReps cr ON r.UserId = cr.ClubRepUserId AND c.ClubId = cr.ClubId
WHERE ISNUMERIC(j.Year) = 1
  AND CAST(j.Year AS INT) >= @StartYear
  AND CAST(j.Year AS INT) <= @EndYear
  AND t.clubrep_registrationId IS NOT NULL
  AND (c.ClubId IS NULL OR cr.ClubId IS NULL);

IF @ExcludedTeamsCount > 0
BEGIN
    PRINT '  ⚠ WARNING: ' + CAST(@ExcludedTeamsCount AS VARCHAR) + ' teams excluded due to missing Club or ClubRep records';
    PRINT '    (This may indicate ClubReps were not created for all users in STEP 3)';
END

-- Phase 1.5: Pre-compute club name tokens and abbreviations (once per club instead of per team)
IF OBJECT_ID('tempdb..#ClubTokens') IS NOT NULL DROP TABLE #ClubTokens;
SELECT DISTINCT
    ClubId,
    ClubName,
    CHARINDEX(' ', ClubName) AS FirstSpace,
    CHARINDEX(' ', ClubName, CHARINDEX(' ', ClubName) + 1) AS SecondSpace,
    CHARINDEX(' ', ClubName, CHARINDEX(' ', ClubName, CHARINDEX(' ', ClubName) + 1) + 1) AS ThirdSpace,
    -- Pre-compute common abbreviations
    CASE 
        WHEN CHARINDEX(' ', ClubName) > 0 THEN
            -- Multi-word: Calculate standard initials (first letter of each word)
            LEFT(ClubName, 1) + 
            SUBSTRING(ClubName, CHARINDEX(' ', ClubName) + 1, 1) +
            CASE WHEN CHARINDEX(' ', ClubName, CHARINDEX(' ', ClubName) + 1) > 0 
                 THEN SUBSTRING(ClubName, CHARINDEX(' ', ClubName, CHARINDEX(' ', ClubName) + 1) + 1, 1) 
                 ELSE '' END
        ELSE
            -- Single word: First 2-3 chars
            LEFT(ClubName, CASE WHEN LEN(ClubName) > 3 THEN 3 ELSE 2 END)
    END AS Initials1,
    -- Variant: First word + initial of second (e.g., "AllA" for "All American")
    CASE 
        WHEN CHARINDEX(' ', ClubName) > 0 THEN
            LEFT(ClubName, CHARINDEX(' ', ClubName) - 1) + 
            SUBSTRING(ClubName, CHARINDEX(' ', ClubName) + 1, 1)
        ELSE NULL
    END AS Initials2
INTO #ClubTokens
FROM #Phase1;

-- Phase 2: Clean team names (inline logic to avoid function call overhead)
IF OBJECT_ID('tempdb..#Phase2') IS NOT NULL DROP TABLE #Phase2;
SELECT 
    p1.TeamId,
    p1.ClubId,
    p1.ClubName,
    p1.JobName,
    p1.OriginalTeamName,
    p1.LevelOfPlay,
    -- Normalize grad year: Use actual year if valid 4-digit, otherwise 'N.A.'
    CASE WHEN ISNUMERIC(p1.RawGradYear) = 1 AND LEN(p1.RawGradYear) = 4
         THEN p1.RawGradYear
         ELSE 'N.A.' END AS GradYear,
    -- Preserve original casing
    LTRIM(NormalizedName) AS CleanedName
INTO #Phase2
FROM #Phase1 p1
INNER JOIN #ClubTokens ct ON p1.ClubId = ct.ClubId
CROSS APPLY (
    -- Use normalized grad year for team name cleaning
    SELECT CASE WHEN ISNUMERIC(p1.RawGradYear) = 1 AND LEN(p1.RawGradYear) = 4
                THEN p1.RawGradYear
                ELSE 'N.A.' END AS NormalizedGradYear
) AS GradYearNorm
CROSS APPLY (
    SELECT 
        CASE
            -- Exact club match → grad year
            WHEN p1.OriginalTeamName = ct.ClubName COLLATE Latin1_General_CI_AI 
            THEN NormalizedGradYear
            -- Full club prefix
            WHEN p1.OriginalTeamName LIKE ct.ClubName + ' %' COLLATE Latin1_General_CI_AI 
            THEN LTRIM(SUBSTRING(p1.OriginalTeamName, LEN(ct.ClubName) + 2, 8000))
            -- First 2 words (3+ word clubs)
            WHEN ct.SecondSpace > 0 AND p1.OriginalTeamName LIKE LEFT(ct.ClubName, ct.SecondSpace - 1) + ' %' COLLATE Latin1_General_CI_AI
            THEN LTRIM(SUBSTRING(p1.OriginalTeamName, ct.SecondSpace + 1, 8000))
            -- Second word only (2-word clubs)
            WHEN ct.FirstSpace > 0 AND ct.SecondSpace = 0 
                 AND p1.OriginalTeamName LIKE SUBSTRING(ct.ClubName, ct.FirstSpace + 1, 8000) + ' %' COLLATE Latin1_General_CI_AI
            THEN LTRIM(SUBSTRING(p1.OriginalTeamName, LEN(SUBSTRING(ct.ClubName, ct.FirstSpace + 1, 8000)) + 2, 8000))
            -- Abbreviation: Standard initials (e.g., "AAA" for "All American Aim")
            WHEN ct.Initials1 IS NOT NULL AND LEN(ct.Initials1) >= 2
                 AND p1.OriginalTeamName LIKE ct.Initials1 + ' %' COLLATE Latin1_General_CI_AI
            THEN LTRIM(SUBSTRING(p1.OriginalTeamName, LEN(ct.Initials1) + 2, 8000))
            -- Abbreviation: First word + initial (e.g., "AllA" for "All American")
            WHEN ct.Initials2 IS NOT NULL AND LEN(ct.Initials2) >= 2
                 AND p1.OriginalTeamName LIKE ct.Initials2 + ' %' COLLATE Latin1_General_CI_AI
            THEN LTRIM(SUBSTRING(p1.OriginalTeamName, LEN(ct.Initials2) + 2, 8000))
            -- First word only (with safeguard)
            WHEN ct.FirstSpace > 0 
                 AND p1.OriginalTeamName LIKE LEFT(ct.ClubName, ct.FirstSpace - 1) + ' %' COLLATE Latin1_General_CI_AI
                 AND LTRIM(SUBSTRING(p1.OriginalTeamName, ct.FirstSpace + 1, 8000)) <> SUBSTRING(ct.ClubName, ct.FirstSpace + 1, 8000) COLLATE Latin1_General_CI_AI
            THEN LTRIM(SUBSTRING(p1.OriginalTeamName, ct.FirstSpace + 1, 8000))
            -- Team equals first word → grad year
            WHEN ct.FirstSpace > 0 AND p1.OriginalTeamName = LEFT(ct.ClubName, ct.FirstSpace - 1) COLLATE Latin1_General_CI_AI
            THEN NormalizedGradYear
            -- Fallback
            ELSE p1.OriginalTeamName
        END AS StrippedName
) AS Strip
CROSS APPLY (
    SELECT
        CASE 
            WHEN LEFT(LTRIM(StrippedName), 1) IN ('-', ':', '|', '/')
            THEN LTRIM(SUBSTRING(StrippedName, 2, 8000))
            ELSE StrippedName
        END AS NormalizedName
) AS Normalize;

DROP TABLE #ClubTokens;

-- Phase 3: Apply transformations and normalize level of play
IF OBJECT_ID('tempdb..#TeamIdentities') IS NOT NULL DROP TABLE #TeamIdentities;
SELECT 
    TeamId,
    ClubId,
    ClubName,
    JobName,
    OriginalTeamName,
    CASE
        WHEN CHARINDEX(' ', CleanedName) > 0
             AND LEN(LEFT(CleanedName, CHARINDEX(' ', CleanedName) - 1)) = 2
             AND ISNUMERIC(LEFT(CleanedName, CHARINDEX(' ', CleanedName) - 1)) = 1
        THEN '20' + CleanedName
        -- Only append grad year if it's a valid year (not N.A.)
        WHEN CHARINDEX(' ', CleanedName) = 0 
             AND LEN(CleanedName) > 0
             AND GradYear <> 'N.A.'
             AND NOT (ISNUMERIC(CleanedName) = 1 AND LEN(CleanedName) = 4)
        THEN GradYear + ' ' + CleanedName
        ELSE CleanedName
    END AS ClubTeamName,
    ClubName + ':' + 
    CASE
        WHEN CHARINDEX(' ', CleanedName) > 0
             AND LEN(LEFT(CleanedName, CHARINDEX(' ', CleanedName) - 1)) = 2
             AND ISNUMERIC(LEFT(CleanedName, CHARINDEX(' ', CleanedName) - 1)) = 1
        THEN '20' + CleanedName
        -- Only append grad year if it's a valid year (not N.A.)
        WHEN CHARINDEX(' ', CleanedName) = 0 
             AND LEN(CleanedName) > 0
             AND GradYear <> 'N.A.'
             AND NOT (ISNUMERIC(CleanedName) = 1 AND LEN(CleanedName) = 4)
        THEN GradYear + ' ' + CleanedName
        ELSE CleanedName
    END AS ScheduleTeamName,
    GradYear AS ClubTeamGradYear,
    LevelOfPlay,
    CASE 
        WHEN PATINDEX('[0-9]%', LTRIM(RTRIM(LevelOfPlay))) = 1
        THEN LEFT(LTRIM(RTRIM(LevelOfPlay)), PATINDEX('%[^0-9]%', LTRIM(RTRIM(LevelOfPlay)) + 'X') - 1)
        WHEN LOWER(LTRIM(RTRIM(LevelOfPlay))) = 'competitive' THEN '2'
        WHEN LOWER(LTRIM(RTRIM(LevelOfPlay))) = 'intermediate' THEN '3'
        WHEN LOWER(LTRIM(RTRIM(LevelOfPlay))) = 'very competitive' THEN '4'
        WHEN LOWER(LTRIM(RTRIM(LevelOfPlay))) = 'most competitive' THEN '5'
        ELSE LevelOfPlay
    END AS NormalizedLevelOfPlay
INTO #TeamIdentities
FROM #Phase2;

DROP TABLE #Phase1;
DROP TABLE #Phase2;

DECLARE @StagedCount INT = @@ROWCOUNT;
PRINT '  ✓ Staged ' + CAST(@StagedCount AS VARCHAR) + ' Teams records with resolved ClubId';

-- Phase 4: Infer grad year from team name when GradYear is "N.A."
-- This MUST happen before #UniqueIdentities is created to avoid mismatches
UPDATE ti
SET ti.ClubTeamGradYear = LEFT(ti.ClubTeamName, 4)
FROM #TeamIdentities ti
WHERE ti.ClubTeamGradYear = 'N.A.'
  AND LEN(ti.ClubTeamName) >= 4
  AND ISNUMERIC(LEFT(ti.ClubTeamName, 4)) = 1
  AND LEFT(ti.ClubTeamName, 4) BETWEEN '2020' AND '2040';

DECLARE @InferredFromNameEarly INT = @@ROWCOUNT;
IF @InferredFromNameEarly > 0
    PRINT '  ✓ Inferred grad year from team name for ' + CAST(@InferredFromNameEarly AS VARCHAR) + ' teams';

-- Show sample of staging data
PRINT '';
PRINT '  Sample staging data (first 5 rows):';
PRINT '  (Shows resolved ClubId and identity components for each team)';
PRINT '';
SELECT 'Sample Staging Data';
SELECT 
    ClubId,
    ClubName,
    JobName,
    OriginalTeamName,
    ClubTeamName,
    ScheduleTeamName,
    ClubTeamGradYear,
    NormalizedLevelOfPlay AS LevelOfPlay,
    COUNT(*) OVER (PARTITION BY ClubId, ClubTeamName, ClubTeamGradYear, NormalizedLevelOfPlay) AS TeamsWithThisIdentity
FROM #TeamIdentities
ORDER BY ClubId, ClubTeamGradYear, ClubTeamName, NormalizedLevelOfPlay;

PRINT '';

-- ============================================================================
-- STEP 5: Identify unique club team identities
-- ============================================================================
PRINT 'STEP 5: Identifying unique club team identities...';

IF OBJECT_ID('tempdb..#UniqueIdentities') IS NOT NULL
    DROP TABLE #UniqueIdentities;

CREATE TABLE #UniqueIdentities (
    ClubId INT NOT NULL,
    ClubTeamName NVARCHAR(255) NOT NULL,
    ClubTeamGradYear NVARCHAR(50) NOT NULL,
    LevelOfPlay NVARCHAR(50) NULL,
    TeamCount INT NOT NULL
);

INSERT INTO #UniqueIdentities (ClubId, ClubTeamName, ClubTeamGradYear, LevelOfPlay, TeamCount)
SELECT 
    ClubId,
    ClubTeamName,
    ClubTeamGradYear,
    NormalizedLevelOfPlay AS LevelOfPlay,
    COUNT(*) AS TeamCount
FROM #TeamIdentities
GROUP BY 
    ClubId,
    ClubTeamName,
    ClubTeamGradYear,
    NormalizedLevelOfPlay;

DECLARE @UniqueIdentitiesCount INT = @@ROWCOUNT;
PRINT '  ✓ Found ' + CAST(@UniqueIdentitiesCount AS VARCHAR) + ' unique club team identities';

-- Show distribution
PRINT '';
PRINT '  Identity distribution:';
PRINT '  (Shows how many Teams share each unique ClubTeam identity)';
PRINT '';
SELECT 'Teams per Identity Distribution';
SELECT 
    MIN(TeamCount) AS [Min],
    CAST(AVG(CAST(TeamCount AS DECIMAL(10,2))) AS DECIMAL(10,2)) AS [Avg],
    MAX(TeamCount) AS [Max]
FROM #UniqueIdentities;

PRINT '';

-- ============================================================================
-- STEP 6: Insert new ClubTeams records
-- ============================================================================
PRINT 'STEP 6: Creating ClubTeams records...';
PRINT '';

BEGIN TRANSACTION;

BEGIN TRY
    -- Create staging table to capture new ClubTeamIds
    IF OBJECT_ID('tempdb..#NewClubTeams') IS NOT NULL
        DROP TABLE #NewClubTeams;

    CREATE TABLE #NewClubTeams (
        ClubTeamId INT NOT NULL PRIMARY KEY,
        ClubId INT NOT NULL,
        ClubTeamName NVARCHAR(255) NOT NULL,
        ClubTeamGradYear NVARCHAR(50) NOT NULL,
        ClubTeamLevelOfPlay NVARCHAR(50) NULL,
        UNIQUE (ClubId, ClubTeamName, ClubTeamGradYear, ClubTeamLevelOfPlay)
    );

    -- Insert into ClubTeams (without OUTPUT - Gender not in ClubTeams table)
    -- Only insert if ClubTeam doesn't already exist
    INSERT INTO Clubs.ClubTeams (ClubId, ClubTeamName, ClubTeamGradYear, ClubTeamLevelOfPlay, Active, LebUserId, Modified)
    SELECT 
        ui.ClubId,
        ui.ClubTeamName,
        ui.ClubTeamGradYear,
        NULLIF(ui.LevelOfPlay, ''),  -- Convert empty string to NULL for consistency
        1 AS Active, -- Set as active by default
        @LebUserId,
        @Now
    FROM #UniqueIdentities ui
    WHERE NOT EXISTS (
        SELECT 1 
        FROM Clubs.ClubTeams ct 
        WHERE ct.ClubId = ui.ClubId 
          AND ct.ClubTeamName = ui.ClubTeamName 
          AND ct.ClubTeamGradYear = ui.ClubTeamGradYear
          AND (ct.ClubTeamLevelOfPlay = ui.LevelOfPlay 
               OR (ct.ClubTeamLevelOfPlay IS NULL AND ui.LevelOfPlay IS NULL)
               OR (ct.ClubTeamLevelOfPlay IS NULL AND ui.LevelOfPlay = '')
               OR (ct.ClubTeamLevelOfPlay = '' AND ui.LevelOfPlay IS NULL))
    );

    DECLARE @NewClubTeamsInserted INT = @@ROWCOUNT;
    
    -- Populate #NewClubTeams by matching back (includes both newly created AND pre-existing)
    -- Use nested ROW_NUMBER to handle both duplicate ClubTeams AND duplicate matches
    WITH AllMatches AS (
        SELECT 
            ct.ClubTeamId,
            ct.ClubId,
            ct.ClubTeamName,
            ct.ClubTeamGradYear,
            ct.ClubTeamLevelOfPlay,
            ui.LevelOfPlay AS UILevelOfPlay
        FROM #UniqueIdentities ui
        INNER JOIN Clubs.ClubTeams ct 
            ON ct.ClubId = ui.ClubId
            AND ct.ClubTeamName = ui.ClubTeamName
            AND ct.ClubTeamGradYear = ui.ClubTeamGradYear
            AND (ct.ClubTeamLevelOfPlay = ui.LevelOfPlay 
                 OR (ct.ClubTeamLevelOfPlay IS NULL AND ui.LevelOfPlay IS NULL)
                 OR (ct.ClubTeamLevelOfPlay IS NULL AND ui.LevelOfPlay = '')
                 OR (ct.ClubTeamLevelOfPlay = '' AND ui.LevelOfPlay IS NULL))
    ),
    -- Deduplicate by actual ClubTeam identity (not by source identity)
    RankedByIdentity AS (
        SELECT 
            ClubTeamId,
            ClubId,
            ClubTeamName,
            ClubTeamGradYear,
            ClubTeamLevelOfPlay,
            ROW_NUMBER() OVER (
                PARTITION BY ClubId, ClubTeamName, ClubTeamGradYear, ISNULL(ClubTeamLevelOfPlay, '')
                ORDER BY ClubTeamId
            ) AS rn
        FROM AllMatches
    )
    INSERT INTO #NewClubTeams (ClubTeamId, ClubId, ClubTeamName, ClubTeamGradYear, ClubTeamLevelOfPlay)
    SELECT ClubTeamId, ClubId, ClubTeamName, ClubTeamGradYear, ClubTeamLevelOfPlay
    FROM RankedByIdentity
    WHERE rn = 1;

    DECLARE @TotalClubTeamsResolved INT = @@ROWCOUNT;
    SET @ClubTeamsCreated = @NewClubTeamsInserted;
    PRINT '  ✓ Created ' + CAST(@ClubTeamsCreated AS VARCHAR) + ' new ClubTeams';
    PRINT '  ✓ Resolved ' + CAST(@TotalClubTeamsResolved AS VARCHAR) + ' total ClubTeams (new + existing)';
    PRINT '';

    -- ============================================================================
    -- STEP 6.5: Deduplicate "N.A." ClubTeams - Consolidate to canonical versions
    -- ============================================================================
    PRINT 'STEP 6.5: Deduplicating "N.A." ClubTeams...';
    PRINT '';

    -- Identify canonical ClubTeams (non-"N.A." grad year versions)
    -- For each (ClubId, ClubTeamName, LevelOfPlay) combo, prefer the version with a real grad year
    IF OBJECT_ID('tempdb..#CanonicalTeams') IS NOT NULL DROP TABLE #CanonicalTeams;
    
    CREATE TABLE #CanonicalTeams (
        ClubId INT NOT NULL,
        ClubTeamName NVARCHAR(255) NOT NULL,
        ClubTeamLevelOfPlay NVARCHAR(50) NOT NULL DEFAULT '',  -- NOT NULL to support PRIMARY KEY
        CanonicalGradYear NVARCHAR(50) NOT NULL,
        CanonicalClubTeamId INT NOT NULL,
        HasNADuplicate BIT NOT NULL,
        PRIMARY KEY (ClubId, ClubTeamName, ClubTeamLevelOfPlay)
    );

    -- Find canonical versions: For each (ClubId, ClubTeamName, LevelOfPlay), pick the non-"N.A." version
    -- If multiple non-"N.A." versions exist, pick MIN(ClubTeamId) to be deterministic
    WITH TeamGroups AS (
        SELECT 
            ClubId,
            ClubTeamName,
            ISNULL(ClubTeamLevelOfPlay, '') AS ClubTeamLevelOfPlay,
            MIN(CASE WHEN ClubTeamGradYear <> 'N.A.' THEN ClubTeamGradYear ELSE NULL END) AS CanonicalGradYear,
            MIN(CASE WHEN ClubTeamGradYear <> 'N.A.' THEN ClubTeamId ELSE NULL END) AS CanonicalClubTeamId,
            MAX(CASE WHEN ClubTeamGradYear = 'N.A.' THEN 1 ELSE 0 END) AS HasNADuplicate
        FROM #NewClubTeams
        GROUP BY ClubId, ClubTeamName, ISNULL(ClubTeamLevelOfPlay, '')
        HAVING MAX(CASE WHEN ClubTeamGradYear = 'N.A.' THEN 1 ELSE 0 END) = 1  -- Only groups with N.A. duplicates
           AND MIN(CASE WHEN ClubTeamGradYear <> 'N.A.' THEN ClubTeamId ELSE NULL END) IS NOT NULL  -- Must have canonical version
    )
    INSERT INTO #CanonicalTeams (ClubId, ClubTeamName, ClubTeamLevelOfPlay, CanonicalGradYear, CanonicalClubTeamId, HasNADuplicate)
    SELECT 
        ClubId,
        ClubTeamName,
        ClubTeamLevelOfPlay,
        CanonicalGradYear,
        CanonicalClubTeamId,
        1 AS HasNADuplicate
    FROM TeamGroups;

    DECLARE @TeamsWithDuplicates INT = @@ROWCOUNT;
    PRINT '  Found ' + CAST(@TeamsWithDuplicates AS VARCHAR) + ' ClubTeams with "N.A." duplicates';

    IF @TeamsWithDuplicates > 0
    BEGIN
        -- Update #TeamIdentities to use canonical grad year instead of "N.A."
        UPDATE ti
        SET ti.ClubTeamGradYear = ct.CanonicalGradYear
        FROM #TeamIdentities ti
        INNER JOIN #CanonicalTeams ct 
            ON ti.ClubId = ct.ClubId
            AND ti.ClubTeamName = ct.ClubTeamName
            AND (ISNULL(ti.NormalizedLevelOfPlay, '') = ct.ClubTeamLevelOfPlay
                 OR (ti.NormalizedLevelOfPlay IS NULL AND ct.ClubTeamLevelOfPlay = ''))
        WHERE ti.ClubTeamGradYear = 'N.A.';

        DECLARE @TeamIdentitiesUpdated INT = @@ROWCOUNT;
        PRINT '  ✓ Updated ' + CAST(@TeamIdentitiesUpdated AS VARCHAR) + ' #TeamIdentities rows to use canonical grad year';

        -- Delete "N.A." duplicates from #NewClubTeams (they won't be referenced anymore)
        DELETE nct
        FROM #NewClubTeams nct
        INNER JOIN #CanonicalTeams ct 
            ON nct.ClubId = ct.ClubId
            AND nct.ClubTeamName = ct.ClubTeamName
            AND (ISNULL(nct.ClubTeamLevelOfPlay, '') = ct.ClubTeamLevelOfPlay
                 OR (nct.ClubTeamLevelOfPlay IS NULL AND ct.ClubTeamLevelOfPlay = ''))
        WHERE nct.ClubTeamGradYear = 'N.A.';

        DECLARE @NADuplicatesRemoved INT = @@ROWCOUNT;
        PRINT '  ✓ Removed ' + CAST(@NADuplicatesRemoved AS VARCHAR) + ' "N.A." duplicate ClubTeams from staging table';

        -- Delete "N.A." duplicates from actual ClubTeams table (if they were inserted)
        DELETE ct
        FROM Clubs.ClubTeams ct
        INNER JOIN #CanonicalTeams can 
            ON ct.ClubId = can.ClubId
            AND ct.ClubTeamName = can.ClubTeamName
            AND (ISNULL(ct.ClubTeamLevelOfPlay, '') = can.ClubTeamLevelOfPlay
                 OR (ct.ClubTeamLevelOfPlay IS NULL AND can.ClubTeamLevelOfPlay = ''))
        WHERE ct.ClubTeamGradYear = 'N.A.'
          AND ct.ClubTeamId NOT IN (SELECT ClubTeamId FROM #NewClubTeams); -- Don't delete canonical version

        DECLARE @NADuplicatesDeletedFromDB INT = @@ROWCOUNT;
        PRINT '  ✓ Deleted ' + CAST(@NADuplicatesDeletedFromDB AS VARCHAR) + ' "N.A." duplicate ClubTeams from database';
    END
    ELSE
    BEGIN
        PRINT '  ✓ No "N.A." duplicates found - all ClubTeams have unique identities';
    END

    DROP TABLE #CanonicalTeams;
    PRINT '';

    -- ============================================================================
    -- STEP 7: Update Teams.ClubTeamId to reference new ClubTeams
    -- ============================================================================
    PRINT 'STEP 7: Linking Teams records to ClubTeams...';
    PRINT '';

    UPDATE t
    SET 
        t.ClubTeamId = nct.ClubTeamId,
        t.Modified = @Now,
        t.LebUserId = @LebUserId
    FROM Leagues.Teams t
    INNER JOIN #TeamIdentities ti ON t.TeamId = ti.TeamId
    INNER JOIN #NewClubTeams nct 
        ON ti.ClubId = nct.ClubId
        AND ti.ClubTeamName = nct.ClubTeamName
        AND ti.ClubTeamGradYear = nct.ClubTeamGradYear
        AND (ti.NormalizedLevelOfPlay = nct.ClubTeamLevelOfPlay
             OR (ti.NormalizedLevelOfPlay IS NULL AND nct.ClubTeamLevelOfPlay IS NULL)
             OR (ti.NormalizedLevelOfPlay IS NULL AND nct.ClubTeamLevelOfPlay = '')
             OR (ti.NormalizedLevelOfPlay = '' AND nct.ClubTeamLevelOfPlay IS NULL))
    WHERE t.ClubTeamId IS NULL;  -- Only update teams that don't have ClubTeamId yet

    SET @TeamsUpdated = @@ROWCOUNT;
    PRINT '  ✓ Updated ' + CAST(@TeamsUpdated AS VARCHAR) + ' Teams.ClubTeamId references';
    PRINT '';

    COMMIT TRANSACTION;
    PRINT '  ✓ Transaction committed successfully';

END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    
    PRINT '';
    PRINT 'ERROR: Transaction rolled back!';
    PRINT 'Error Message: ' + ERROR_MESSAGE();
    PRINT 'Error Line: ' + CAST(ERROR_LINE() AS VARCHAR);
    
    THROW;
END CATCH

PRINT '';

-- ============================================================================
-- STEP 8: Verification and Summary
-- ============================================================================
PRINT '========================================';
PRINT 'STEP 8: Verification & Summary';
PRINT '========================================';
PRINT '';

-- Summary statistics
PRINT 'Migration Summary:';
PRINT '  Clubs created:         ' + CAST(@ClubsCreated AS VARCHAR);
PRINT '  ClubReps created:      ' + CAST(@ClubRepsCreated AS VARCHAR);
PRINT '  ClubTeams created:     ' + CAST(@ClubTeamsCreated AS VARCHAR);
PRINT '  Teams updated:         ' + CAST(@TeamsUpdated AS VARCHAR);
PRINT '';

-- Verify no orphaned Teams
DECLARE @OrphanedTeams INT;
SELECT @OrphanedTeams = COUNT(*)
FROM Leagues.Teams t
INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
INNER JOIN Leagues.Agegroups ag ON t.AgegroupId = ag.AgegroupId
WHERE ISNUMERIC(j.Year) = 1
  AND CAST(j.Year AS INT) >= @StartYear
  AND CAST(j.Year AS INT) <= @EndYear
  AND t.clubrep_registrationId IS NOT NULL
  AND t.ClubTeamId IS NULL;

IF @OrphanedTeams > 0
BEGIN
    PRINT '  ⚠ WARNING: ' + CAST(@OrphanedTeams AS VARCHAR) + ' Teams records still have NULL ClubTeamId';
END
ELSE
BEGIN
    PRINT '  ✓ All eligible Teams records successfully linked to ClubTeams';
END

PRINT '';

-- Clubs created this run
PRINT 'Clubs created this run:';
PRINT '(New club organizations established during migration)';
PRINT '';
SELECT 'Clubs Created This Run';
SELECT 
    c.ClubId,
    c.ClubName,
    COUNT(DISTINCT cr.ClubRepUserId) AS ClubRepsCount,
    COUNT(DISTINCT ct.ClubTeamId) AS ClubTeamsCount
FROM Clubs.Clubs c
LEFT JOIN Clubs.ClubReps cr ON c.ClubId = cr.ClubId
LEFT JOIN Clubs.ClubTeams ct ON c.ClubId = ct.ClubId
WHERE c.Modified = @Now
GROUP BY c.ClubId, c.ClubName
ORDER BY c.ClubName;

PRINT '';

-- Sample verification query
PRINT 'ClubTeams with linked Teams:';
PRINT '(Newly created ClubTeams ordered by number of linked Teams)';
PRINT '';
SELECT 'ClubTeams Created This Run';
SELECT 
    ct.ClubTeamId,
    c.ClubName,
    ct.ClubTeamName,
    ct.ClubTeamGradYear,
    COUNT(t.TeamId) AS LinkedTeamsCount
FROM Clubs.ClubTeams ct
INNER JOIN Clubs.Clubs c ON ct.ClubId = c.ClubId
LEFT JOIN Leagues.Teams t ON ct.ClubTeamId = t.ClubTeamId
WHERE ct.Modified = @Now -- Only show newly created
GROUP BY 
    ct.ClubTeamId,
    c.ClubName,
    ct.ClubTeamName,
    ct.ClubTeamGradYear
ORDER BY LinkedTeamsCount DESC;

PRINT '';

-- ClubReps created this run
PRINT 'ClubReps created this run:';
PRINT '(User-Club relationships established during migration)';
PRINT '';
SELECT 'ClubReps Created This Run';
SELECT 
    c.ClubName,
    u.UserName,
    u.Email
FROM Clubs.ClubReps cr
INNER JOIN Clubs.Clubs c ON cr.ClubId = c.ClubId
INNER JOIN dbo.AspNetUsers u ON cr.ClubRepUserId = u.Id
WHERE cr.Modified = @Now
ORDER BY c.ClubName, u.UserName;

PRINT '';

-- All ClubTeams
PRINT 'All ClubTeams in database:';
PRINT '(Complete list of all ClubTeams with their linked Teams count)';
PRINT '';
SELECT 'All ClubTeams in Database';
SELECT 
    ct.ClubTeamId,
    c.ClubName,
    ct.ClubTeamName,
    ct.ClubTeamGradYear,
    ct.Active,
    COUNT(t.TeamId) AS LinkedTeamsCount
FROM Clubs.ClubTeams ct
INNER JOIN Clubs.Clubs c ON ct.ClubId = c.ClubId
LEFT JOIN Leagues.Teams t ON ct.ClubTeamId = t.ClubTeamId
GROUP BY 
    ct.ClubTeamId,
    c.ClubName,
    ct.ClubTeamName,
    ct.ClubTeamGradYear,
    ct.Active
ORDER BY c.ClubName, ct.ClubTeamGradYear, ct.ClubTeamName;

PRINT '';

-- Database Statistics (single row summary)
PRINT 'Database Statistics:';
PRINT '(Comprehensive summary of all Club-related tables and linkage status)';
PRINT '';
SELECT 'Database Statistics';
SELECT 
    (SELECT COUNT(*) FROM Clubs.Clubs) AS TotalClubs,
    (SELECT COUNT(*) FROM Clubs.ClubReps) AS TotalClubReps,
    (SELECT COUNT(*) FROM Clubs.ClubTeams) AS TotalClubTeams,
    COUNT(*) AS TotalTeams,
    SUM(CASE WHEN t.ClubTeamId IS NOT NULL THEN 1 ELSE 0 END) AS TeamsLinked,
    SUM(CASE WHEN t.ClubTeamId IS NULL THEN 1 ELSE 0 END) AS TeamsOrphaned,
    CAST(
        ROUND(
            (SUM(CASE WHEN t.ClubTeamId IS NOT NULL THEN 1.0 ELSE 0 END) / COUNT(*)) * 100,
            1
        ) AS DECIMAL(5,1)
    ) AS [LinkRate%]
FROM Leagues.Teams t
INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
INNER JOIN Leagues.Agegroups ag ON t.AgegroupId = ag.AgegroupId
WHERE ISNUMERIC(j.Year) = 1
  AND CAST(j.Year AS INT) >= @StartYear
  AND CAST(j.Year AS INT) <= @EndYear
  AND t.clubrep_registrationId IS NOT NULL;

PRINT '';
PRINT '========================================';
PRINT 'Migration Complete!';
PRINT '========================================';
PRINT '';

-- ============================================================================
-- Data Quality Diagnostics
-- ============================================================================
PRINT '';
PRINT '========================================';
PRINT 'Data Quality Diagnostics';
PRINT '========================================';
PRINT '';
PRINT 'The following diagnostics help identify:';
PRINT '  - Teams that failed to link (orphaned records)';
PRINT '  - Data quality issues requiring cleanup';
PRINT '  - Distribution patterns across clubs';
PRINT '  - Edge cases and unusual data values';
PRINT '';
PRINT 'Results with 0 rows indicate no issues found (good!)';
PRINT '';
PRINT '----------------------------------------';
PRINT '';

-- ============================================================================
-- BURN SCENARIO #1: Teams missing ClubTeamId after script
-- ============================================================================
PRINT 'BURN #1: Teams with clubrep but missing ClubTeamId';
PRINT '   Risk: Valid teams excluded from library';
PRINT '   Expected: 0 rows';
PRINT '';
SELECT 'BURN #1: Teams missing ClubTeamId' AS BurnScenario;
SELECT 
    j.JobName,
    j.Year,
    t.TeamName,
    ag.AgegroupName,
    t.level_of_play,
    r.club_name AS RegistrationClubName,
    u.UserName AS ClubRepUsername,
    t.TeamId,
    t.clubrep_registrationId,
    t.ClubTeamId
FROM Leagues.Teams t
INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
INNER JOIN Leagues.Agegroups ag ON t.AgegroupId = ag.AgegroupId
INNER JOIN Jobs.Registrations r ON t.clubrep_registrationId = r.RegistrationId
INNER JOIN dbo.AspNetUsers u ON r.UserId = u.Id
WHERE ISNUMERIC(j.Year) = 1
  AND CAST(j.Year AS INT) BETWEEN @StartYear AND @EndYear
  AND t.clubrep_registrationId IS NOT NULL
  AND t.ClubTeamId IS NULL
ORDER BY r.club_name, ag.AgegroupName, t.TeamName;

PRINT '';
PRINT '----------------------------------------';
PRINT '';

-- ============================================================================
-- BURN SCENARIO #2: Wrong ClubTeamId assignment (mismatch between clubs)
-- ============================================================================
PRINT 'BURN #2: Teams linked to ClubTeam from different Club';
PRINT '   Risk: Team shows up in wrong club''s library';
PRINT '   Expected: 0 rows';
PRINT '';
SELECT 'BURN #2: Wrong ClubTeamId assignment' AS BurnScenario;
SELECT 
    j.JobName,
    j.Year,
    t.TeamName,
    ag.AgegroupName,
    r.club_name AS RegistrationClubName,
    c1.ClubId AS RegistrationClubId,
    c1.ClubName AS RegistrationClubNameResolved,
    ct.ClubTeamId,
    ct.ClubId AS ClubTeamClubId,
    c2.ClubName AS ClubTeamClubName,
    ct.ClubTeamName,
    ct.ClubTeamGradYear,
    t.TeamId
FROM Leagues.Teams t
INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
INNER JOIN Leagues.Agegroups ag ON t.AgegroupId = ag.AgegroupId
INNER JOIN Jobs.Registrations r ON t.clubrep_registrationId = r.RegistrationId
INNER JOIN Clubs.Clubs c1 ON COALESCE(LTRIM(RTRIM(r.club_name)), 'N.A.') = c1.ClubName
INNER JOIN Clubs.ClubTeams ct ON t.ClubTeamId = ct.ClubTeamId
INNER JOIN Clubs.Clubs c2 ON ct.ClubId = c2.ClubId
WHERE ISNUMERIC(j.Year) = 1
  AND CAST(j.Year AS INT) BETWEEN @StartYear AND @EndYear
  AND t.clubrep_registrationId IS NOT NULL
  AND t.ClubTeamId IS NOT NULL
  AND c1.ClubId <> ct.ClubId  -- Mismatch: Registration club != ClubTeam club
ORDER BY r.club_name, ag.AgegroupName, t.TeamName;

PRINT '';
PRINT '----------------------------------------';
PRINT '';

-- ============================================================================
-- BURN SCENARIO #3: Duplicate ClubTeams for same logical team
-- ============================================================================
PRINT 'BURN #3: Duplicate ClubTeams (same Club, Name, Year, LOP)';
PRINT '   Risk: Teams split across multiple ClubTeam records';
PRINT '   Expected: 0 rows';
PRINT '';
SELECT 'BURN #3: Duplicate ClubTeams' AS BurnScenario;
SELECT 
    c.ClubName,
    ct.ClubTeamName,
    ct.ClubTeamGradYear,
    ISNULL(ct.ClubTeamLevelOfPlay, '') AS ClubTeamLevelOfPlay,
    COUNT(*) AS DuplicateCount,
    STRING_AGG(CAST(ct.ClubTeamId AS VARCHAR), ', ') AS ClubTeamIds
FROM Clubs.ClubTeams ct
INNER JOIN Clubs.Clubs c ON ct.ClubId = c.ClubId
GROUP BY 
    ct.ClubId,
    c.ClubName,
    ct.ClubTeamName,
    ct.ClubTeamGradYear,
    ISNULL(ct.ClubTeamLevelOfPlay, '')
HAVING COUNT(*) > 1
ORDER BY c.ClubName, ct.ClubTeamName, ct.ClubTeamGradYear;

PRINT '';
PRINT '----------------------------------------';
PRINT '';

-- ============================================================================
-- BURN SCENARIO #4: Orphaned ClubTeams (no Teams reference them)
-- ============================================================================
PRINT 'BURN #4: ClubTeams with no linked Teams';
PRINT '   Risk: Dead records cluttering library';
PRINT '   Expected: 0 rows (or only manually-created placeholders)';
PRINT '';
SELECT 'BURN #4: Orphaned ClubTeams' AS BurnScenario;
SELECT 
    c.ClubName,
    ct.ClubTeamId,
    ct.ClubTeamName,
    ct.ClubTeamGradYear,
    ct.ClubTeamLevelOfPlay,
    ct.Active,
    ct.Modified
FROM Clubs.ClubTeams ct
INNER JOIN Clubs.Clubs c ON ct.ClubId = c.ClubId
WHERE NOT EXISTS (
    SELECT 1 FROM Leagues.Teams t WHERE t.ClubTeamId = ct.ClubTeamId
)
ORDER BY c.ClubName, ct.ClubTeamName, ct.ClubTeamGradYear;

PRINT '';
PRINT '----------------------------------------';
PRINT '';

-- ============================================================================
-- BURN SCENARIO #5: ClubId chain mismatch (Team→Registration→Club vs Team→ClubTeam→Club)
-- ============================================================================
PRINT 'BURN #5: ClubId chain mismatch (Registration vs ClubTeam path)';
PRINT '   Risk: Navigation broken - team in library but wrong club lineage';
PRINT '   Expected: 0 rows';
PRINT '';
SELECT 'BURN #5: ClubId chain mismatch' AS BurnScenario;
SELECT 
    j.JobName,
    j.Year,
    t.TeamName,
    ag.AgegroupName,
    r.club_name AS RegistrationClubName,
    c1.ClubId AS RegistrationClubId,
    c1.ClubName AS RegistrationClubNameResolved,
    ct.ClubTeamId,
    ct.ClubId AS ClubTeamClubId,
    c2.ClubName AS ClubTeamClubName,
    ct.ClubTeamName,
    ct.ClubTeamGradYear,
    t.TeamId,
    t.clubrep_registrationId
FROM Leagues.Teams t
INNER JOIN Jobs.Jobs j ON t.JobId = j.JobId
INNER JOIN Leagues.Agegroups ag ON t.AgegroupId = ag.AgegroupId
INNER JOIN Jobs.Registrations r ON t.clubrep_registrationId = r.RegistrationId
INNER JOIN Clubs.Clubs c1 ON COALESCE(LTRIM(RTRIM(r.club_name)), 'N.A.') = c1.ClubName
INNER JOIN Clubs.ClubTeams ct ON t.ClubTeamId = ct.ClubTeamId
INNER JOIN Clubs.Clubs c2 ON ct.ClubId = c2.ClubId
WHERE ISNUMERIC(j.Year) = 1
  AND CAST(j.Year AS INT) BETWEEN @StartYear AND @EndYear
  AND t.clubrep_registrationId IS NOT NULL
  AND t.ClubTeamId IS NOT NULL
  AND c1.ClubId <> c2.ClubId  -- Paths diverge: Registration path vs ClubTeam path
ORDER BY r.club_name, ag.AgegroupName, t.TeamName;

PRINT '';
PRINT '========================================';
PRINT 'Diagnostics Complete!';
PRINT '========================================';
PRINT '';

    -- Cleanup temp tables
    DROP TABLE #TeamIdentities;
    DROP TABLE #UniqueIdentities;
    DROP TABLE #NewClubTeams;

END
GO
