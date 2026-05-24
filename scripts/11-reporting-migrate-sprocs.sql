-- =============================================================================
-- 11-reporting-migrate-sprocs.sql
-- Crystal Reports -> SP-Excel migration stored procedures.
-- =============================================================================
-- One-time, finite migration. Each Crystal "data dump" report's backing query is
-- copied here, normalized to the SP-Excel contract, so the EXISTING generic
-- export-sp path renders it with no per-report C#:
--   ReportingController.ExportStoredProcedureResults
--     -> ReportingService.ExportStoredProcedureToExcelAsync
--       -> BuildExcelFromDataReader  (Syncfusion XlsIO)
-- The original reporting.* sprocs are left untouched so cr2025 keeps working
-- during the parallel run; when Crystal is retired, this schema can be dropped.
--
-- Idempotent: schema guard + CREATE OR ALTER. Re-runnable. Apply to TSICV5 in
-- every environment (Dev / Staging / Production). Minimum server is SQL 2019.
--
-- Permissions: NONE needed here. App pool users already hold db-wide GRANT
--   EXECUTE, which covers any new schema:
--     dev   -> 00-postdev-db-restore-apppooluser.sql
--     prod  -> IIS-Config-Prod/Deployment/Fix-IIS-DbLogin.sql
--
-- Per-sproc contract (what makes the generic exporter handle it unchanged):
--   * parameter @jobID uniqueidentifier   (executor always binds it)
--   * emit PAIRED result sets:
--         SELECT 'QA Test: <TabName>';     -- result 1: worksheet name marker
--         SELECT <content>;                -- result 2: that sheet's rows
--     (repeat the pair for multi-tab reports)
--   * <TabName> <= 31 chars, no  : \ / ? * [ ]
--
-- The matching JobReports Action map lives in 7-install-reporting-jobreports.sql
-- (@ActionMap); the type1-report-catalog.ts removal is on the frontend.
-- =============================================================================

USE TSICV5;
GO

-- Schema must be the first statement in its batch, so the idempotent guard wraps
-- CREATE SCHEMA in EXEC.
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'reporting_migrate')
    EXEC (N'CREATE SCHEMA reporting_migrate AUTHORIZATION dbo;');
GO

-- -----------------------------------------------------------------------------
-- Report : TournamentRecruitingReport_DataDump  ("College Recruiter Data Dump")
-- Source : reporting.JobRosters_ExportTournament  (copied + cleaned below)
-- Changes vs source:
--   * dropped the dev-test @jobID default GUID (executor always supplies @jobID)
--   * added the @qaTest tab-name marker as result set 1 (house convention,
--     matches reporting.CathyCampCheckinWithVacAndMedform et al.)
--   * corrected the source's misspelled output alias 'satVerval' -> 'satVerbal'
--   * stripped the legacy Crystal-era convert(varchar(...)) casts. Every wrapped
--     column is already nvarchar (verified), so this is cosmetic only: same text
--     output, no truncation, Unicode preserved. KEPT the two non-cosmetic casts:
--       - convert(char(10), u.dob, 101)  -> date formatting (dob is datetime2)
--       - the 'sat' column's convert(int,...) sum + outer cast that unifies the
--         integer total with the '' else-branch
--   * NOTE (left as-is, not a convert issue): the 'sat' filter reads
--     'r.satVerbal is null' where 'not r.satVerbal is null' was likely intended
--     - preserved for output parity; say the word to fix it.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[JobRosters_ExportTournament]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)

select @qaTest = 'Recruiting'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
		roles.Name as role
	,	r.RegistrationID as RegistrationID
	,	u.FirstName as FirstName
	,	u.LastName as LastName
	,	u.FirstName + ' ' + u.LastName as rosterPerson
	,	coalesce(u.Email, uF.Email) as Email
	,	u.gender as gender
	,	convert(char(10), u.dob, 101) as dob
	,	coalesce(u.streetAddress, uF.streetAddress) as streetAddress
	,	coalesce(u.city, uF.city) as city
	,	coalesce(u.state, uF.state) as state
	,	coalesce(u.postalCode, uF.postalCode) as zip
	,	case when len(coalesce(u.cellphone, f.Mom_cellphone)) = 10 then substring(coalesce(u.cellphone, f.Mom_cellphone), 1, 3) + '-' + substring(coalesce(u.cellphone, f.Mom_cellphone), 4, 3) + '-' + substring(coalesce(u.cellphone, f.Mom_cellphone), 7, 4) else coalesce(u.cellphone, f.Mom_cellphone) end as cellphone
	,	r.sportAssnID as UsLaxNo
	,	ag.agegroupName as agegroupName
	,	d.divName as divName
	,	rCR.club_name as club_name
	,	r.club_name as club_name_player
	,	t.teamName as teamName

	,	f.Mom_FirstName + ' ' + f.Mom_lastName as Mom
	,	coalesce(lower(f.Mom_Email), 'not@given.com') as Mom_Email
	,	case when len(f.Mom_Cellphone) = 10 then left(f.Mom_Cellphone, 3) + '-' + substring(f.Mom_Cellphone, 4, 3) + '-' + right(f.Mom_Cellphone, 4) else f.Mom_Cellphone end as Mom_Phone

	,	f.Dad_FirstName + ' ' + f.Dad_LastName as Dad
	,	coalesce(lower(f.Dad_Email), 'not@given.com') as Dad_Email
	,	case when len(f.Dad_Cellphone) = 10 then left(f.Dad_Cellphone, 3) + '-' + substring(f.Dad_Cellphone, 4, 3) + '-' + right(f.Dad_Cellphone, 4) else f.Dad_Cellphone end as Dad_Phone

	,	r.uniform_no as uniform_no
	,	r.grad_year as grad_year
	,	r.school_name as school_name
	,	r.position + ' - ' + r.ClubTeamName as position
	,	r.gpa as gpa
	,	r.psat as psat
	,	case when not r.satMath is null and r.satVerbal is null and not r.satWriting is null then convert(varchar(10), convert(int, r.satMath) + convert(int, r.satVerbal) + convert(int, r.satWriting)) else '' end as sat
	,	r.class_rank as class_rank
	,	r.height_inches as height_inches
	,	r.weight_lbs as weight_lbs
	,	r.act as act
	,	r.satMath as satMath
	,	r.satVerbal as satVerbal
	,	r.satWriting as satWriting
	,	case r.bCollegeCommit when 1 then 'Yes' else 'No' end as bCollegeCommit
	,	r.college_commit as 'College Committed'

	,	case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then '' else uCR.FirstName end as ClubRep_FirstName
	,	case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then '' else uCR.LastName end as ClubRep_LastName
	,	case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then '' else uCR.FirstName + ' ' + uCR.LastName end as rosterCR
	,	case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then '' else uCR.email end as ClubRep_Email
	,	case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then '' else uCR.cellphone end as ClubRep_Phone

from
	Jobs.Registrations r
	inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
	inner join Leagues.teams t on r.assigned_teamID = t.teamID
	inner join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
	inner join dbo.AspNetUsers uCR on rCR.UserId = uCR.Id
	inner join Leagues.leagues l on t.leagueID = l.leagueID
	inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
	inner join Leagues.divisions d on t.divID = d.divID
	inner join dbo.AspNetUsers u on r.UserId = u.Id
	left join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
	left join dbo.Families f on uF.Id = f.Family_UserId
where
	r.jobID = @jobID
	and r.bActive = 1
	and roles.Name in ('Staff', 'Player')
	and not u.FirstName is null
order by
	ag.agegroupName,
	t.teamName,
	roles.Name desc,
	u.LastName,
	u.FirstName;
GO

PRINT 'reporting_migrate.JobRosters_ExportTournament installed.';
GO
