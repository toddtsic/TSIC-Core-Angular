/*
    26 — AmericanSelect 2027: "Check In Report (pdf)" in the Reports Library
    Customer: AmericanSelect  (76586DE3-ACB3-42EE-91EB-48597CA06802)

    WHAT THIS DOES
    Legacy put "Check In Report (pdf)" (Reporting/AmericanSelectTournyCheckin) under the Docs parent
    on the regional Director + SuperDirector job menus. That report is now rendered natively
    (EF + Syncfusion, commit 8b23eb233) instead of by Crystal, but it has no Reports Library entry
    and no job shelf rows, so nobody can reach it. This:
        1. adds ONE reporting.ReportLibrary row (JobOnly, Director and above)
        2. shelves it (reporting.JobReports) on every target job for Director, SuperDirector and
           Superuser, linked to that library row

    TARGET JOBS — SEEDED FROM THE DATA, NOT A HARDCODED LIST
    AmericanSelect 2027 jobs that have at least one team in a "Registration" (tryout) age group.
    That is how americanselect-mainevent-2027 and americanselect-INDIVIDUALshowcase-2027 stay out
    (Todd, 2026-09-16: showcase not needed). Neither has a Registration age group.

    Expected on dev (09-16): 1 library row, 23 jobs, 69 shelf rows (23 x 3 roles).

    PROD ORDER
    Run on prod ONLY AFTER 8b23eb233 is deployed. Before that, prod still routes this action to
    Crystal, which is off, so the new tile would be a broken link.

    RE-RUN SAFE
    Library row: reused if ReportKey already exists (aborts if its Kind/Controller/Action differ).
    Shelf rows: a job/role that already holds this action (any group, active or not) is skipped
    and reported.

    job clone (JobCloneResetRules.CloneJobReports) copies shelf rows, so 2028 inherits these.

    RUN
    @Commit = 0 (default) previews inside a transaction and rolls back.
    @Commit = 1 writes.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @CustomerId uniqueidentifier = '76586DE3-ACB3-42EE-91EB-48597CA06802';
DECLARE @Year       varchar(4)       = '2027';
DECLARE @Commit     bit              = 0;

DECLARE @ReportKey  nvarchar(200) = N'AmericanSelectTournyCheckin';
DECLARE @Title      nvarchar(400) = N'Check In Report (pdf)';   -- legacy menu text
DECLARE @Kind       nvarchar(40)  = N'CrystalReport';           -- named-endpoint bucket (same as AmericanSelectEvaluation)
DECLARE @Controller nvarchar(100) = N'Reporting';
DECLARE @Action     nvarchar(500) = N'AmericanSelectTournyCheckin';
DECLARE @Category   nvarchar(100) = N'Registrations';           -- beside the existing Check-In tool
DECLARE @LibSort    int           = 200;                        -- same as AmericanSelectEvaluation

DECLARE @DirectorRoleId nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Director');

-- ── 1. Target job/role pairs ────────────────────────────────────────────────────────
IF OBJECT_ID('tempdb..#pairs') IS NOT NULL DROP TABLE #pairs;

SELECT  j.JobId, j.JobPath, r.Id AS RoleId, r.Name AS RoleName
INTO    #pairs
FROM    Jobs.Jobs j
CROSS JOIN (SELECT Id, Name FROM dbo.AspNetRoles
            WHERE Name IN ('Director', 'SuperDirector', 'Superuser')) r
WHERE   j.customerID = @CustomerId
  AND   j.year       = @Year
  AND   EXISTS (SELECT 1
                FROM Leagues.teams t
                JOIN Leagues.agegroups a ON a.agegroupID = t.agegroupID
                WHERE t.JobId = j.JobId
                  AND a.agegroupName = 'Registration');

SELECT  p.JobPath, p.RoleName, 'SKIPPED - report already on this shelf' AS Note
FROM    #pairs p
WHERE   EXISTS (SELECT 1 FROM reporting.JobReports jr
                WHERE jr.JobId = p.JobId AND jr.RoleId = p.RoleId
                  AND jr.Controller = @Controller AND jr.Action = @Action)
ORDER BY p.JobPath, p.RoleName;

DELETE  p
FROM    #pairs p
WHERE   EXISTS (SELECT 1 FROM reporting.JobReports jr
                WHERE jr.JobId = p.JobId AND jr.RoleId = p.RoleId
                  AND jr.Controller = @Controller AND jr.Action = @Action);

BEGIN TRANSACTION;

-- ── 2. Library row (reuse if present) ───────────────────────────────────────────────
IF EXISTS (SELECT 1 FROM reporting.ReportLibrary
           WHERE ReportKey = @ReportKey
             AND (Kind <> @Kind OR Controller <> @Controller OR Action <> @Action))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('ABORT: ReportLibrary row %s exists with a different Kind/Controller/Action. Nothing written.', 16, 1, @ReportKey);
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM reporting.ReportLibrary WHERE ReportKey = @ReportKey)
BEGIN
    INSERT INTO reporting.ReportLibrary
           (ReportKey, Title, Description, Tags, CategoryCode, IconName, Kind, Controller, Action,
            Scope, MinRoleId, OwnerCustomerId, SortOrder, LebUserId)
    VALUES (@ReportKey, @Title, NULL, NULL, @Category, NULL, @Kind, @Controller, @Action,
            N'JobOnly', @DirectorRoleId, NULL, @LibSort, NULL);
END;

DECLARE @LibraryId uniqueidentifier = (SELECT ReportLibraryId FROM reporting.ReportLibrary WHERE ReportKey = @ReportKey);

IF @LibraryId IS NULL OR @DirectorRoleId IS NULL
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('ABORT: library row or Director role did not resolve. Nothing written.', 16, 1);
    RETURN;
END;

-- ── 3. Shelf rows, appended after each job/role's existing reports in the category ───
INSERT INTO reporting.JobReports
       (JobId, RoleId, Title, IconName, Controller, Action, Kind, GroupLabel, SortOrder, Active, LebUserId, ReportLibraryId)
SELECT  p.JobId, p.RoleId, @Title, NULL, @Controller, @Action, @Kind, @Category,
        ISNULL((SELECT MAX(jr.SortOrder) FROM reporting.JobReports jr
                WHERE jr.JobId = p.JobId AND jr.RoleId = p.RoleId AND jr.GroupLabel = @Category), 0) + 1,
        1, NULL, @LibraryId
FROM    #pairs p;

-- ── 4. Preview / verify ─────────────────────────────────────────────────────────────
SELECT  lib.ReportKey, lib.Title, lib.CategoryCode, lib.Kind, lib.Action, lib.Scope, mr.Name AS MinRole
FROM    reporting.ReportLibrary lib
JOIN    dbo.AspNetRoles mr ON mr.Id = lib.MinRoleId
WHERE   lib.ReportLibraryId = @LibraryId;

SELECT  COUNT(DISTINCT jr.JobId) AS jobs,
        COUNT(*)                 AS shelf_rows
FROM    reporting.JobReports jr
JOIN    #pairs p ON p.JobId = jr.JobId AND p.RoleId = jr.RoleId
WHERE   jr.ReportLibraryId = @LibraryId;

SELECT  p.JobPath, p.RoleName, jr.GroupLabel, jr.SortOrder, jr.Title, jr.Action
FROM    #pairs p
JOIN    reporting.JobReports jr ON jr.JobId = p.JobId AND jr.RoleId = p.RoleId AND jr.ReportLibraryId = @LibraryId
ORDER BY p.JobPath, p.RoleName;

IF @Commit = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT 'COMMITTED.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'PREVIEW ONLY - rolled back. Set @Commit = 1 to write.';
END;
