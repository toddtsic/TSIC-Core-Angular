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

-- -----------------------------------------------------------------------------
-- Report : JobStaff_Excel  ("Staff Data (Excel)")
-- Source : reporting.Get_Staff_ForExcelExport  (copied + cleaned below)
-- Changes vs source:
--   * param @jobID varchar(max) (+ dev-test default GUID) -> uniqueidentifier
--     (executor always supplies @jobID; Registrations.jobID is uniqueidentifier)
--   * added the 'QA Test: Staff' tab-name marker as result set 1 (house convention)
--   * stripped ALL five convert(varchar(...)) casts. Every wrapped column is
--     already nvarchar, so the casts only downcast nvarchar->varchar (ASCII) and,
--     on the three NO-LENGTH ones, silently truncated to varchar's default 30
--     chars. Removing them: full-width output, Unicode preserved. The author had
--     specified widths where intended (Email 180, cellphone 100) and forgot the
--     length on [Staff]/agegroupName/teamName -- the classic accidental-30 bug.
--     Truncation impact measured at migration time (whole DB): 41 staff names,
--     229 agegroup names, 2,796 team names exceeded 30 chars in cr2025 output.
--   * everything else preserved verbatim: roles filter ('Staff','Referee'),
--     left joins to teams/agegroups, NO bActive filter (report shows [Active?]
--     and orders by it), order by bActive, LastName, FirstName.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[Get_Staff_ForExcelExport]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)

select @qaTest = 'Staff'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
    r.RegistrationID,
    r.bActive as [Active?],
    u.FirstName,
    u.LastName,
    u.Email as Email,
    u.Username,
    u.LastName + ', ' + u.FirstName as [Staff],
    ag.agegroupName as agegroupName,
    t.teamName as teamName,
    u.cellphone as cellphone
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    left join Leagues.teams t on r.assigned_teamID = t.teamID
    left join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
where
    r.jobID = @jobID
    and roles.Name in ('Staff', 'Referee')
order by
    r.bActive,
    u.LastName,
    u.FirstName;
GO

PRINT 'reporting_migrate.Get_Staff_ForExcelExport installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_JobPlayer_Transactions  ("Player Transactions (Excel)")
-- Source : reporting.Get_Player_Transactions_ForExcelExport  (copied + cleaned)
-- Changes vs source:
--   * param @jobID varchar(max) (+ dev-test default GUID) -> uniqueidentifier
--   * added the 'QA Test: Player Transactions' tab-name marker as result set 1
--   * NO casts stripped: this proc's only two convert()s are FUNCTIONAL date
--     formatting, kept verbatim --
--       - convert(date, u.dob) as [DOB]            (strips the time component)
--       - convert(char(19), ra.createdate, 120)    (ISO 'YYYY-MM-DD HH:MI:SS')
--     There are no no-purpose nvarchar->varchar casts here; agegroupName/teamName
--     are already uncast, so no truncation concern.
--   * everything else preserved verbatim: filters (bActive=1, roles.Name='Player',
--     ra.active=1), all inner joins, order by LastName, FirstName, createdate.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[Get_Player_Transactions_ForExcelExport]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)

select @qaTest = 'Player Transactions'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
    r.RegistrationID,
    u.FirstName,
    u.LastName,
    u.gender,
    convert(date, u.dob) as [DOB],

    ag.agegroupName as agegroupName,
    t.teamName as teamName,

    r.paid_total,
    r.owed_total,

    apm.paymentMethod,
    ra.aID,
    ra.dueamt,
    ra.payamt,
    ra.adnCC4,
    ra.adnCCExpDate,
    ra.adnInvoiceNo,
    ra.adnTransactionID,
    ra.checkNo,
    ra.comment,
    convert(char(19), ra.createdate, 120) as createdate
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Jobs.Registration_Accounting ra on r.RegistrationID = ra.RegistrationID
    inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
    and ra.active = 1
order by
        u.LastName
    ,   u.FirstName
    ,   ra.createdate;
GO

PRINT 'reporting_migrate.Get_Player_Transactions_ForExcelExport installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_DiscountedPlayers  ("Discounted Players (Excel)")
-- Source : reporting.Get_DiscountedPlayers  (copied + cleaned below)
-- Changes vs source:
--   * dropped the dev-test @jobID default GUID (param was already uniqueidentifier)
--   * added the 'QA Test: Discounted Players' tab-name marker as result set 1
--   * NO casts in this proc (none to strip or keep); agegroupName/teamName uncast.
--   * everything else preserved verbatim: filters (bActive=1, roles.name='Player',
--     fee_discount not null and != 0), all joins, order by LastName, FirstName.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[Get_DiscountedPlayers]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)

select @qaTest = 'Discounted Players'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
    u.LastName,
    u.FirstName,
    ag.agegroupName,
    t.teamName,
    f.Mom_FirstName,
    f.Mom_Email,
    f.Mom_Cellphone,
    f.Mom_LastName,
    f.Dad_FirstName,
    f.Dad_LastName,
    f.Dad_Email,
    f.Dad_Cellphone,
    r.fee_discount,
    dc.codeName,
    dc.active,
    dc.codeStartDate,
    dc.codeEndDate
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.Families f on uF.id = f.Family_UserId
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    left join Jobs.Job_DiscountCodes dc on r.DiscountCodeID = dc.ai
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.name = 'Player'
    and not r.fee_discount is null
    and r.fee_discount != 0
order by
        u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.Get_DiscountedPlayers installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : PlayerStats_ParisiExportExcel  ("Player Stats Export - Parisi (Excel)")
-- Source : reporting.PlayerStats_ParisiResults  (copied + cleaned below)
-- Changes vs source:
--   * dropped the dev-test @jobID default GUID (param was already uniqueidentifier)
--   * added the 'QA Test: Parisi Stats' tab-name marker as result set 1
--   * NO casts in this proc (none to strip or keep); agegroupName/teamName uncast.
--   * everything else preserved verbatim: filters (roles.Name='Player', bActive=1,
--     divName != 'Unassigned'), all joins (divisions kept for the divName filter),
--     order by agegroupName, LastName, FirstName.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[PlayerStats_ParisiResults]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)

select @qaTest = 'Parisi Stats'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        ag.agegroupName
    ,   u.FirstName
    ,   u.LastName
    ,   t.teamName
    ,   r.uniform_no
    ,   r.school_name
    ,   r.club_name
    ,   r.fastestshot
    ,   r.five_ten_five
    ,   r.fourtyyarddash
    ,   r.threehundredshuttle
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
where
    r.jobID = @jobID
    and roles.Name = 'Player'
    and r.bActive = 1
    and d.divName != 'Unassigned'
order by
        ag.agegroupName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.PlayerStats_ParisiResults installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_TeamFieldDistribution  ("Team Field Distribution")
-- Source : reporting.TeamsFieldDistribution  (copied + cleaned below)
-- Changes vs source:
--   * dropped the dev-test @jobID default GUID (param was already uniqueidentifier)
--   * added the 'QA Test: Field Distribution' tab-name marker as result set 1
--   * KEPT the convert(char(10), s.G_Date, 101) casts (SELECT + GROUP BY + ORDER
--     BY) -- FUNCTIONAL MM/DD/YYYY date formatting, not a no-purpose varchar cast.
--     No useless nvarchar->varchar casts present; club_name/agegroupName/teamName/
--     fName are already uncast.
--   * everything else preserved verbatim: aggregate count(*) grouped by club/age/
--     team/field/GameDay, all joins (divisions kept though only join-filtering),
--     filter s.jobID=@jobID, order by club, age, team, GameDay, field.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[TeamsFieldDistribution]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)

select @qaTest = 'Field Distribution'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        r.club_name
    ,   ag.agegroupName
    ,   t.teamName
    ,   f.fName
    ,   convert(char(10), s.G_Date, 101) as GameDay
    ,   count(*) as [Count]
from
    Leagues.schedule s
    inner join reference.Fields f on s.fieldID = f.fieldID
    inner join Leagues.teams t on t.teamID in (s.T1_ID, s.T2_ID)
    inner join Jobs.Registrations r on t.clubrep_registrationid = r.RegistrationID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
where
    s.jobID = @jobID
group by
        r.club_name
    ,   ag.agegroupName
    ,   t.teamName
    ,   f.fName
    ,   convert(char(10), s.G_Date, 101)
order by
        r.club_name
    ,   ag.agegroupName
    ,   t.teamName
    ,   convert(char(10), s.G_Date, 101)
    ,   f.fName;
GO

PRINT 'reporting_migrate.TeamsFieldDistribution installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Mobile_JobUsers  ("Mobile Users (Excel)")
-- Source : mobile.UsageByJobAndRegistrant  (copied + cleaned below)
--   NOTE: source proc lives in the `mobile` schema; the migrate copy lives in
--   reporting_migrate like all the others (its tables stay fully-qualified mobile.*).
-- Changes vs source:
--   * dropped the dev-test @jobID default GUID (param was already uniqueidentifier)
--   * added the 'QA Test: Mobile Users' tab-name marker as result set 1
--   * NO casts in this proc (none to strip or keep).
--   * everything else preserved verbatim, incl. the unreferenced `left join
--     Leagues.teams t` (no effect on output; kept for parity), filter r.jobId=@jobId,
--     order by customerName, jobName, Role, LastName, FirstName.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[UsageByJobAndRegistrant]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)

select @qaTest = 'Mobile Users'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        c.customerName
    ,   j.jobName
    ,   roles.Name as [Role]
    ,   u.LastName
    ,   u.FirstName
    ,   d.Type
    ,   dr.modified
from
    mobile.Device_RegistrationIds dr
    inner join mobile.Devices d on dr.DeviceId = d.Id
    inner join Jobs.Registrations r on dr.RegistrationID = r.RegistrationID
    left join Leagues.teams t on r.assigned_teamID = t.teamId
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join Jobs.Jobs j on r.jobID = j.jobID
    inner join Jobs.Customers c on j.customerID = c.customerID
where
    r.jobId = @jobID
order by
        c.customerName
    ,   j.jobName
    ,   roles.Name
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.UsageByJobAndRegistrant installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : League_Teams  ("Teams Export (Excel)")
-- Source : reporting.League_Teams  (copied + cleaned below)
-- Changes vs source:
--   * DROPPED the source proc's side-effect UPDATE (it back-filled NULL
--     Leagues.teams.FieldID1 with the '--unassigned--' placeholder field so the
--     report's INNER JOIN wouldn't drop field-less teams). A report must not
--     mutate data. Instead: changed `INNER JOIN reference.Fields` -> `LEFT JOIN`
--     so field-less teams still appear (HomeField = NULL), read-only. (user: option A)
--   * param dev-test default GUID dropped (was already uniqueidentifier)
--   * added the 'QA Test: Teams Export' tab-name marker as result set 1
--   * stripped the useless convert(varchar(80))/convert(varchar(100),level_of_play)
--     nvarchar->varchar downcasts (all confirmed nvarchar; no truncation, Unicode kept)
--   * KEPT convert(varchar(100), t.teamID) (uniqueidentifier->string, functional)
--     and replace(t.team_comments, char(13), ',') (strips CRs for CSV safety) --
--     just dropped the inner varchar(256) cast inside the replace.
--   * everything else verbatim: filters (t.active=1, roles.Name='Club Rep',
--     r.bActive=1), order by agegroupName, divName, club_name, teamName.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[League_Teams]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)

select @qaTest = 'Teams Export'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        ag.agegroupName as Agegroup
    ,   d.divName as Division
    ,   t.teamName as Team
    ,   r.club_name as Club
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   u.Email as Email
    ,   u.city as City
    ,   u.state as State
    ,   u.postalCode as ZIP
    ,   u.cellphone as Phone
    ,   replace(t.team_comments, char(13), ',') as Comment
    ,   f.fName as HomeField
    ,   convert(varchar(100), t.teamID) as teamID
    ,   t.level_of_play as LOP
from
    Leagues.teams t
    left join reference.Fields f on t.fieldID1 = f.fieldID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    inner join Jobs.Registrations r on t.clubrep_registrationid = r.RegistrationID
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
where
    t.jobID = @jobID
    and t.active = 1
    and roles.Name = 'Club Rep'
    and r.bActive = 1
order by
        ag.agegroupName
    ,   d.divName
    ,   r.club_name
    ,   t.teamName;
GO

PRINT 'reporting_migrate.League_Teams installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_CustomerPlayers1  ("Player Data - All Customer Jobs (Excel)")
-- Source : reporting.Get_Players_ForExcelExport_AllCustomers  (copied + cleaned)
--   Cross-customer dump: derives @customerID from @jobID, then returns every
--   non-expired job (j.ExpiryAdmin > getdate()) for that customer. Pure SELECT
--   (the @customerID assignment emits no result set).
-- Changes vs source:
--   * param @jobID varchar(max) (+ dev-test default GUID) -> uniqueidentifier
--   * added the 'QA Test: Customer Players' tab-name marker as result set 1
--   * stripped the no-length convert(varchar, …) on [Player] (LastName, FirstName)
--     -- defaulted to 30 chars and truncated, same accidental-30 bug as Staff.
--   * KEPT convert(date, u.dob) as [DOB] (functional date formatting).
--   * everything else verbatim: filters (customerID, ExpiryAdmin>getdate(),
--     bActive=1, roles.Name='Player'), all joins, order by customer/job/last/first/age/team.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[Get_Players_ForExcelExport_AllCustomers]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @customerID uniqueidentifier
select @customerID = j.customerID from Jobs.Jobs j where j.jobID = @jobID

declare @qaTest varchar(max)
select @qaTest = 'Customer Players'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
    c.customerName,
    j.jobName,

    r.RegistrationID,
    r.RegistrationTS,
    u.FirstName,
    u.LastName,

    u.LastName + ', ' + u.FirstName as [Player],
    u.gender,
    convert(date, u.dob) as [DOB],
    f.Mom_FirstName,
    f.Mom_LastName,
    f.Mom_FirstName + ' ' + f.Mom_LastName as Mom,
    f.Mom_Cellphone,
    f.Mom_Email,
    f.Dad_FirstName,
    f.Dad_LastName,
    f.Dad_FirstName + ' ' + f.Dad_LastName as Dad,
    f.Dad_Cellphone,
    f.Dad_Email,

    ag.agegroupName as agegroupName,
    t.teamName as teamName,

    r.club_name,
    r.paid_total,
    r.owed_total,
    r.grad_year,
    r.school_name,
    r.school_grade,
    r.position,
    r.sportAssnID as USLaxNo,
    r.sportAssnIDExpDate as UsLaxNoExpiry,
    r.jersey_size as jersey,
    r.shorts_size as shorts,
    r.kilt as kilt,
    r.[t-shirt],
    r.sweatshirt,
    r.reversible,
    r.gloves,
    r.gpa,
    r.sat,
    r.act,
    r.height_inches,
    r.weight_lbs,

    r.uniform_no,
    uF.streetAddress as address,
    uF.city,
    uF.[state],
    uF.postalCode as zip
from
    Jobs.Registrations r
    inner join Jobs.Jobs j on r.jobID = j.jobID
    inner join Jobs.Customers c on j.customerID = c.customerID
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
where
    j.customerID = @customerID
    and j.ExpiryAdmin > getdate()
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        c.customerName
    ,   j.jobName
    ,   u.LastName
    ,   u.FirstName
    ,   ag.agegroupName
    ,   t.teamName;
GO

PRINT 'reporting_migrate.Get_Players_ForExcelExport_AllCustomers installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_JobRosters_RecruitingReport_DumpExcel  ("Recruiting Rosters Data Dump (Excel)")
-- Source : reporting.JobRosters_RecruitingReport  (copied + cleaned)
--   Admin recruiting roster dump: every active player on active teams, with full
--   contact PII (player/parent phone, email, home address) + club-rep contact +
--   recruiting data (school, grad year, SAT/ACT, GPA, position). Pure SELECT.
-- Changes vs source:
--   * params (@jobID, @CustomerName, @JobName) -> @jobID only; the customer/job
--     name args were header-only, never referenced in the body.
--   * added the 'QA Test: Recruiting Rosters' tab-name marker as result set 1.
--   * stripped the no-purpose convert(varchar(100), …) nvarchar->varchar downcasts
--     on text columns (agegroupName, teamName, last/firstName, rosterPerson, email,
--     Address, City, school).
--   * KEPT functional expressions verbatim: replace(uniform_no,'#',''), the phone
--     substring formatting, all coalesce(...) defaults, the clubrep CASE guards,
--     and the duplicate [grad_year] column (preserves the legacy result shape).
--   * FIXED a legacy defect: source had clubrep = uCR.FirstName+' '+uCR.FirstName
--     (concatenated the first name twice); corrected to FirstName+' '+LastName so the
--     club-rep name renders properly. Approved deviation from legacy (2026-05-25).
--   * everything else verbatim: joins, where (jobID, Player role, active team,
--     bActive), order by agegroup/team/uniform-numeric/last/first.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[JobRosters_RecruitingReport_DumpExcel]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Recruiting Rosters'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        ag.agegroupName as agegroupName
    ,   t.teamName as teamName
    ,   u.LastName as lastName
    ,   u.FirstName as firstName
    ,   replace(r.uniform_no, '#', '') as uniform_no
    ,   u.FirstName + ' ' + u.LastName as rosterPerson
    ,   substring(coalesce(u.cellphone, f.Mom_Cellphone), 1, 3) + '-' + substring(coalesce(u.cellphone, f.Mom_Cellphone), 4, 3) + '-' + substring(coalesce(u.cellphone, f.Mom_Cellphone), 7, 4) as phone
    ,   coalesce(u.Email, f.mom_email) as email
    ,   uF.streetAddress as [Address]
    ,   uF.City as [City]
    ,   uF.State
    ,   uF.postalCode as ZIP
    ,   r.school_name as school
    ,   r.grad_year as grad_year
    ,   coalesce(r.satMath, '0') as satMath
    ,   coalesce(r.satVerbal, '0') as satVerbal
    ,   coalesce(r.satWriting, '0') as satWriting
    ,   coalesce(r.act, '0') as act
    ,   r.gpa
    ,   r.position
    ,   coalesce(r.club_name, r.ClubTeamName) as club_name
    ,   case when rCR.RegistrationID is null then null else uCR.FirstName + ' ' + uCR.LastName end as clubrep
    ,   case when rCR.RegistrationID is null then null else uCR.Email end as clubrepEmail
    ,   case when rCR.RegistrationID is null then null else substring(uCR.cellphone, 1, 3) + '-' + substring(uCR.cellphone, 4, 3) + '-' + substring(uCR.cellphone, 7, 4) end as clubrepPhone
    ,   convert(varchar(100), r.grad_year) as [grad_year]
    ,   r.ClubTeamName
from
    Leagues.teams t
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
    left join dbo.AspNetUsers uCR on rCR.UserId = uCR.Id
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    inner join Jobs.Registrations r on t.teamID = r.assigned_teamID
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
where
    t.jobID = @jobID
    and roles.Name = 'Player'
    and t.active = 1
    and r.bActive = 1
order by
        ag.agegroupName
    ,   t.teamName
    ,   case when ISNUMERIC(replace(r.uniform_no, '#', '')) = 1 and ltrim(rtrim(r.uniform_no)) != '.' then CONVERT(int, replace(r.uniform_no, '#', '')) else null end
    ,   u.lastName
    ,   u.firstName;
GO

PRINT 'reporting_migrate.JobRosters_RecruitingReport_DumpExcel installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_JobRosters_RecruitingReport_Public_DumpExcel  ("Recruiting Rosters (Public, Excel)")
-- Source : reporting.JobRosters_RecruitingReport  (the SAME proc the legacy Public
--   Crystal report ran — the Public .rpt simply hid columns at render time).
--   PUBLIC-SAFE SUBSET: identity + team + school/grad/position only. ALL contact
--   PII (player/parent phone, email, home address, club-rep contact) and academic
--   scores (SAT/ACT/GPA) are dropped, matching the legacy Public .rpt column set
--   verified against job americanselect-connecticut-2026 (2026-05-25):
--     agegroupName, teamName, lastName, firstName, uniform_no, club_name,
--     school, grad_year, position.
-- The FROM/WHERE/ORDER BY are IDENTICAL to the admin DumpExcel proc above, so the
--   row set is identical to legacy Public — only the SELECT list is reduced.
--   NOTE: the Families (f), address-user (uF) and club-rep (rCR/uCR) joins are
--   retained even though their columns are dropped — they preserve the exact row
--   filtering of the shared legacy proc. Do NOT remove them. Pure SELECT.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[JobRosters_RecruitingReport_Public]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Recruiting Public'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        ag.agegroupName as agegroupName
    ,   t.teamName as teamName
    ,   u.LastName as lastName
    ,   u.FirstName as firstName
    ,   replace(r.uniform_no, '#', '') as uniform_no
    ,   coalesce(r.club_name, r.ClubTeamName) as club_name
    ,   r.school_name as school
    ,   r.grad_year as grad_year
    ,   r.position
from
    Leagues.teams t
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
    left join dbo.AspNetUsers uCR on rCR.UserId = uCR.Id
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    inner join Jobs.Registrations r on t.teamID = r.assigned_teamID
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
where
    t.jobID = @jobID
    and roles.Name = 'Player'
    and t.active = 1
    and r.bActive = 1
order by
        ag.agegroupName
    ,   t.teamName
    ,   case when ISNUMERIC(replace(r.uniform_no, '#', '')) = 1 and ltrim(rtrim(r.uniform_no)) != '.' then CONVERT(int, replace(r.uniform_no, '#', '')) else null end
    ,   u.lastName
    ,   u.firstName;
GO

PRINT 'reporting_migrate.JobRosters_RecruitingReport_Public installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_JobPlayers_STEPS_Excel  ("Player Data (Excel)" / STEPS layout)
-- Source : reporting.Get_Players_ForExcelExport  (shared 75-col superset; the
--   STEPS .rpt rendered a 40-col subset). Column set + order recovered from a live
--   legacy run on americanselect-newjersey-2026 (2026-05-25). Pure SELECT.
-- 40 columns, in the legacy .rpt order:
--   RegistrationID, FirstName, LastName, gender, DOB, Mom_{FirstName,LastName,
--   Cellphone,Email}, Dad_{FirstName,LastName,Cellphone,Email}, agegroupName,
--   club_name, teamName, paid_total, owed_total, grad_year, school_name,
--   school_grade, position, USLaxNo, UsLaxNoExpiry, jersey, shorts, kilt,
--   t-shirt, reversible, gloves, uniform_no, address, city, state, zip,
--   Email, Cellphone, Requests, medical_note, shoes.
-- Changes vs source: param varchar(max)->uniqueidentifier; added 'QA Test' tab
--   marker; stripped the no-purpose convert(varchar(N),…) casts; KEPT convert(date,
--   u.dob) [DOB] and coalesce(ClubTeamName, rCR.club_name) for club_name. Joins/
--   where/order identical to the shared proc (rCR/l/d joins retained for club_name
--   + exact row parity).
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[Get_JobPlayers_STEPS_Excel]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Player Data'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        r.RegistrationID
    ,   u.FirstName as FirstName
    ,   u.LastName as LastName
    ,   u.gender as gender
    ,   convert(date, u.dob) as [DOB]
    ,   f.Mom_FirstName as Mom_FirstName
    ,   f.Mom_LastName as Mom_LastName
    ,   f.Mom_Cellphone as Mom_Cellphone
    ,   f.Mom_Email as Mom_Email
    ,   f.Dad_FirstName as Dad_FirstName
    ,   f.Dad_LastName as Dad_LastName
    ,   f.Dad_Cellphone as Dad_Cellphone
    ,   f.Dad_Email as Dad_Email
    ,   ag.agegroupName as agegroupName
    ,   coalesce(r.ClubTeamName, rCR.club_name) as club_name
    ,   t.teamName as teamName
    ,   r.paid_total
    ,   r.owed_total
    ,   r.grad_year as grad_year
    ,   r.school_name as school_name
    ,   r.school_grade as school_grade
    ,   r.position as position
    ,   r.sportAssnID as USLaxNo
    ,   r.sportAssnIDExpDate as UsLaxNoExpiry
    ,   r.jersey_size as jersey
    ,   r.shorts_size as shorts
    ,   r.kilt as kilt
    ,   r.[t-shirt]
    ,   r.reversible as reversible
    ,   r.gloves as gloves
    ,   r.uniform_no as uniform_no
    ,   uF.streetAddress as address
    ,   uF.city as city
    ,   uF.[state] as state
    ,   uF.postalCode as zip
    ,   u.email as Email
    ,   u.cellphone as Cellphone
    ,   r.specialRequests as Requests
    ,   r.medical_note as medical_note
    ,   r.shoes as shoes
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.Get_JobPlayers_STEPS_Excel installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : JobPlayers_YJ_Excel  ("Player Details (Excel)" / Yellow Jackets layout)
-- Source : reporting.Get_Players_ForExcelExport  (shared 75-col superset; the YJ
--   .rpt rendered a 36-col subset). Column set + order recovered from a live legacy
--   run on yj-players-2026 (2026-05-25). Pure SELECT.
-- 36 columns, in the legacy .rpt order:
--   RegistrationID, uniform_no, LastName, FirstName, address, city, state, zip,
--   agegroupName, teamName, school_name, grad_year, position, USLaxNo,
--   UsLaxNoExpiry, jersey, shorts, t-shirt, Mom_{FirstName,LastName,Cellphone,
--   Email}, Dad_{FirstName,LastName,Cellphone,Email}, owed_total, paid_total,
--   school_grade, Requests, gender, RegDate, DOB, Cellphone, Email, ClubTeamName.
-- Changes vs source: param varchar(max)->uniqueidentifier; added 'QA Test' tab
--   marker; stripped the no-purpose convert(varchar(N),…) casts; KEPT convert(date,
--   r.RegistrationTS)[RegDate] and convert(date,u.dob)[DOB]. Uses RAW r.ClubTeamName
--   (not the coalesce club_name), so rCR is unused here but joins/where/order are
--   kept identical to the shared proc for exact row parity.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[JobPlayers_YJ_Excel]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Player Details'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        r.RegistrationID
    ,   r.uniform_no as uniform_no
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   uF.streetAddress as address
    ,   uF.city as city
    ,   uF.[state] as state
    ,   uF.postalCode as zip
    ,   ag.agegroupName as agegroupName
    ,   t.teamName as teamName
    ,   r.school_name as school_name
    ,   r.grad_year as grad_year
    ,   r.position as position
    ,   r.sportAssnID as USLaxNo
    ,   r.sportAssnIDExpDate as UsLaxNoExpiry
    ,   r.jersey_size as jersey
    ,   r.shorts_size as shorts
    ,   r.[t-shirt]
    ,   f.Mom_FirstName as Mom_FirstName
    ,   f.Mom_LastName as Mom_LastName
    ,   f.Mom_Cellphone as Mom_Cellphone
    ,   f.Mom_Email as Mom_Email
    ,   f.Dad_FirstName as Dad_FirstName
    ,   f.Dad_LastName as Dad_LastName
    ,   f.Dad_Cellphone as Dad_Cellphone
    ,   f.Dad_Email as Dad_Email
    ,   r.owed_total
    ,   r.paid_total
    ,   r.school_grade as school_grade
    ,   r.specialRequests as Requests
    ,   u.gender as gender
    ,   convert(date, r.RegistrationTS) as [RegDate]
    ,   convert(date, u.dob) as [DOB]
    ,   u.cellphone as Cellphone
    ,   u.email as Email
    ,   r.ClubTeamName as ClubTeamName
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.JobPlayers_YJ_Excel installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_JobPlayers_E120_Excel  ("Player Data (Excel)" / E120 layout)
-- Source : reporting.Get_Players_ForExcelExport  (shared 75-col superset; the E120
--   .rpt rendered a 39-col subset). Column set + order recovered from a live legacy
--   run on xpo-florida-girls-2026 (2026-05-25). Pure SELECT.
-- 39 columns, in the legacy .rpt order:
--   RegistrationID, FirstName, LastName, Player, gender, DOB, Mom, Mom_Cellphone,
--   Mom_Email, Dad, Dad_Cellphone, Dad_Email, agegroupName, teamName, club_name,
--   paid_total, owed_total, grad_year, school_name, position, USLaxNo,
--   UsLaxNoExpiry, jersey, shorts, kilt, t-shirt, sweatshirt, reversible, gloves,
--   gpa, sat, act, height_inches, weight_lbs, uniform_no, address, city, state, zip.
-- Changes vs source: param varchar(max)->uniqueidentifier; added 'QA Test' tab
--   marker; stripped the convert(varchar(N),…) casts INCLUDING the no-length
--   convert(varchar,…) on [Player] (would truncate to 30); KEPT convert(date,u.dob)
--   [DOB] and the name-concat expressions [Player]/Mom/Dad. Uses coalesce club_name
--   (rCR needed). Joins/where/order identical to the shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[Get_JobPlayers_E120_Excel]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Player Data'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        r.RegistrationID
    ,   u.FirstName as FirstName
    ,   u.LastName as LastName
    ,   u.LastName + ', ' + u.FirstName as [Player]
    ,   u.gender as gender
    ,   convert(date, u.dob) as [DOB]
    ,   f.Mom_FirstName + ' ' + f.Mom_LastName as Mom
    ,   f.Mom_Cellphone as Mom_Cellphone
    ,   f.Mom_Email as Mom_Email
    ,   f.Dad_FirstName + ' ' + f.Dad_LastName as Dad
    ,   f.Dad_Cellphone as Dad_Cellphone
    ,   f.Dad_Email as Dad_Email
    ,   ag.agegroupName as agegroupName
    ,   t.teamName as teamName
    ,   coalesce(r.ClubTeamName, rCR.club_name) as club_name
    ,   r.paid_total
    ,   r.owed_total
    ,   r.grad_year as grad_year
    ,   r.school_name as school_name
    ,   r.position as position
    ,   r.sportAssnID as USLaxNo
    ,   r.sportAssnIDExpDate as UsLaxNoExpiry
    ,   r.jersey_size as jersey
    ,   r.shorts_size as shorts
    ,   r.kilt as kilt
    ,   r.[t-shirt]
    ,   r.sweatshirt as sweatshirt
    ,   r.reversible as reversible
    ,   r.gloves as gloves
    ,   r.gpa as gpa
    ,   r.sat as sat
    ,   r.act as act
    ,   r.height_inches as height_inches
    ,   r.weight_lbs as weight_lbs
    ,   r.uniform_no as uniform_no
    ,   uF.streetAddress as address
    ,   uF.city as city
    ,   uF.[state] as state
    ,   uF.postalCode as zip
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.Get_JobPlayers_E120_Excel installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : Get_JobPlayers_Liberty_Excel  ("Player Data (Excel)" / Liberty layout)
-- Source : reporting.Get_Players_ForExcelExport  (shared 75-col superset; the
--   Liberty .rpt rendered a 41-col subset). Column set + order recovered from a
--   live legacy run on tlc-signups-summer-2026 (2026-05-25). Pure SELECT.
-- 41 columns, in the legacy .rpt order:
--   RegistrationID, Player, gender, DOB, Mom, Mom_Cellphone, Mom_Email, Dad,
--   Dad_Cellphone, Dad_Email, agegroupName, teamName, paid_total, owed_total,
--   grad_year, school_name, position, USLaxNo, UsLaxNoExpiry, jersey, shorts,
--   kilt, t-shirt, sweatshirt, reversible, gloves, gpa, sat, act, height_inches,
--   weight_lbs, uniform_no, FirstName, LastName, address, city, state, zip,
--   Email, Cellphone, Requests.
-- Changes vs source: param varchar(max)->uniqueidentifier; added 'QA Test' tab
--   marker; stripped the convert(varchar(N),…) casts INCLUDING the no-length
--   convert(varchar,…) on [Player]; KEPT convert(date,u.dob) [DOB] and name-concat
--   for [Player]/Mom/Dad. No club column in this layout (Liberty = club jobs).
--   Joins/where/order identical to the shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[Get_JobPlayers_Liberty_Excel]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Player Data'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        r.RegistrationID
    ,   u.LastName + ', ' + u.FirstName as [Player]
    ,   u.gender as gender
    ,   convert(date, u.dob) as [DOB]
    ,   f.Mom_FirstName + ' ' + f.Mom_LastName as Mom
    ,   f.Mom_Cellphone as Mom_Cellphone
    ,   f.Mom_Email as Mom_Email
    ,   f.Dad_FirstName + ' ' + f.Dad_LastName as Dad
    ,   f.Dad_Cellphone as Dad_Cellphone
    ,   f.Dad_Email as Dad_Email
    ,   ag.agegroupName as agegroupName
    ,   t.teamName as teamName
    ,   r.paid_total
    ,   r.owed_total
    ,   r.grad_year as grad_year
    ,   r.school_name as school_name
    ,   r.position as position
    ,   r.sportAssnID as USLaxNo
    ,   r.sportAssnIDExpDate as UsLaxNoExpiry
    ,   r.jersey_size as jersey
    ,   r.shorts_size as shorts
    ,   r.kilt as kilt
    ,   r.[t-shirt]
    ,   r.sweatshirt as sweatshirt
    ,   r.reversible as reversible
    ,   r.gloves as gloves
    ,   r.gpa as gpa
    ,   r.sat as sat
    ,   r.act as act
    ,   r.height_inches as height_inches
    ,   r.weight_lbs as weight_lbs
    ,   r.uniform_no as uniform_no
    ,   u.FirstName as FirstName
    ,   u.LastName as LastName
    ,   uF.streetAddress as address
    ,   uF.city as city
    ,   uF.[state] as state
    ,   uF.postalCode as zip
    ,   u.email as Email
    ,   u.cellphone as Cellphone
    ,   r.specialRequests as Requests
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.Get_JobPlayers_Liberty_Excel installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : camp_datadump  ("Camp Data Dump (Excel)")
-- Source : reporting.Get_Players_ForExcelExport  (shared 75-col superset; the
--   camp_datadump .rpt rendered a 17-col subset — despite the "all columns" catalog
--   description, the actual layout is a focused contact+basics dump). Column set +
--   order recovered from a live legacy run on um-summercamps-2026 (2026-05-25).
--   Pure SELECT.
-- 17 columns, in the legacy .rpt order:
--   FirstName, LastName, address, city, state, zip, Mom_Cellphone, Mom_Email,
--   Dad_Cellphone, Dad_Email, Cellphone, Email, DOB, grad_year, school_name,
--   club_name, position.
-- Changes vs source: param varchar(max)->uniqueidentifier; added 'QA Test' tab
--   marker; stripped the convert(varchar(N),…) casts; KEPT convert(date,u.dob)
--   [DOB] and coalesce club_name (rCR used). Joins/where/order identical to the
--   shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[camp_datadump]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Camp Data Dump'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        u.FirstName as FirstName
    ,   u.LastName as LastName
    ,   uF.streetAddress as address
    ,   uF.city as city
    ,   uF.[state] as state
    ,   uF.postalCode as zip
    ,   f.Mom_Cellphone as Mom_Cellphone
    ,   f.Mom_Email as Mom_Email
    ,   f.Dad_Cellphone as Dad_Cellphone
    ,   f.Dad_Email as Dad_Email
    ,   u.cellphone as Cellphone
    ,   u.email as Email
    ,   convert(date, u.dob) as [DOB]
    ,   r.grad_year as grad_year
    ,   r.school_name as school_name
    ,   coalesce(r.ClubTeamName, rCR.club_name) as club_name
    ,   r.position as position
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.camp_datadump installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : camp_excelexport_long  ("Camp Export (Long)(Excel)")
-- Source : reporting.Get_Players_ForExcelExport  (20-col subset of the shared
--   75-col superset). Column set + order recovered from a live legacy run on
--   um-summercamps-2026 (2026-05-26). Pure SELECT.
-- 20 columns, in the legacy .rpt order:
--   RegistrationID, teamName, uniform_no, LastName, FirstName, address, city,
--   state, zip, Mom_Cellphone, Mom_Email, Dad_Cellphone, Dad_Email, Cellphone,
--   Email, school_name, club_name, position, grad_year, roommate_pref.
-- Changes vs source: param varchar(max)->uniqueidentifier; added 'QA Test' tab
--   marker; stripped the convert(varchar(N),…) casts; KEPT coalesce club_name
--   (rCR used). Joins/where/order identical to the shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[camp_excelexport_long]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Camp Export Long'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        r.RegistrationID
    ,   t.teamName as teamName
    ,   r.uniform_no as uniform_no
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   uF.streetAddress as address
    ,   uF.city as city
    ,   uF.[state] as state
    ,   uF.postalCode as zip
    ,   f.Mom_Cellphone as Mom_Cellphone
    ,   f.Mom_Email as Mom_Email
    ,   f.Dad_Cellphone as Dad_Cellphone
    ,   f.Dad_Email as Dad_Email
    ,   u.cellphone as Cellphone
    ,   u.email as Email
    ,   r.school_name as school_name
    ,   coalesce(r.ClubTeamName, rCR.club_name) as club_name
    ,   r.position as position
    ,   r.grad_year as grad_year
    ,   r.roommate_pref as roommate_pref
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.camp_excelexport_long installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : camp_excelexport_short  ("Camp Export (Short)(Excel)")
-- Source : reporting.Get_Players_ForExcelExport  (6-col subset of the shared
--   75-col superset). Column set + order recovered from a live legacy run on
--   um-summercamps-2026 (2026-05-26). Pure SELECT.
-- 6 columns, in the legacy .rpt order:
--   teamName, uniform_no, LastName, FirstName, school_name, club_name.
-- Joins/where/order identical to the shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[camp_excelexport_short]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Camp Export Short'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        t.teamName as teamName
    ,   r.uniform_no as uniform_no
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   r.school_name as school_name
    ,   coalesce(r.ClubTeamName, rCR.club_name) as club_name
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.camp_excelexport_short installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : camp_excelexport_veryshort  ("Camper Export (Very Short)(Excel)")
-- Source : reporting.Get_Players_ForExcelExport  (5-col subset). Column set+order
--   from a live legacy run on um-summercamps-2026 (2026-05-26). Pure SELECT.
-- 5 columns, in order: teamName, uniform_no, LastName, FirstName, position.
-- Joins/where/order identical to the shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[camp_excelexport_veryshort]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Camper Very Short'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        t.teamName as teamName
    ,   r.uniform_no as uniform_no
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   r.position as position
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.camp_excelexport_veryshort installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : camp_excelexport_daygroups  ("Day/Night Groups (Excel)")
-- Source : reporting.Get_Players_ForExcelExport  (9-col subset). Column set+order
--   from a live legacy run on um-summercamps-2026 (2026-05-26). Pure SELECT.
-- 9 columns, in order:
--   agegroupName, dayGroup, nightGroup, LastName, FirstName, roommate_pref,
--   school_name, position, uniform_no.
-- Joins/where/order identical to the shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[camp_excelexport_daygroups]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Day Night Groups'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        ag.agegroupName as agegroupName
    ,   r.dayGroup as dayGroup
    ,   r.nightGroup as nightGroup
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   r.roommate_pref as roommate_pref
    ,   r.school_name as school_name
    ,   r.position as position
    ,   r.uniform_no as uniform_no
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.camp_excelexport_daygroups installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : camp_excelexport_roomies  ("Roommates (Excel)")
-- Source : reporting.Get_Players_ForExcelExport  (9-col subset). Column set+order
--   from a live legacy run on um-summercamps-2026 (2026-05-26). Pure SELECT.
-- 9 columns, in order:
--   teamName, LastName, FirstName, DOB, roommate_pref, school_name, school_grade,
--   club_name, grad_year.
-- Joins/where/order identical to the shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[camp_excelexport_roomies]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Roommates'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        t.teamName as teamName
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   convert(date, u.dob) as [DOB]
    ,   r.roommate_pref as roommate_pref
    ,   r.school_name as school_name
    ,   r.school_grade as school_grade
    ,   coalesce(r.ClubTeamName, rCR.club_name) as club_name
    ,   r.grad_year as grad_year
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.camp_excelexport_roomies installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : camp_excelexport_room_position  ("Roommates (with position)(Excel)")
-- Source : reporting.Get_Players_ForExcelExport  (11-col subset). Column set+order
--   from a live legacy run on um-summercamps-2026 (2026-05-26). Pure SELECT.
-- 11 columns, in order:
--   teamName, roomStatus, LastName, FirstName, roommate_pref, school_name,
--   school_grade, club_name, DOB, position, grad_year.
-- roomStatus = left(teamName,1) — first char of team name (e.g. 'C'=COMMUTER,
--   'O'=OVERNIGHT) per the shared proc's expression. Kept verbatim.
-- Joins/where/order identical to the shared proc.
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[camp_excelexport_room_position]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Roommates w Position'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        t.teamName as teamName
    ,   left(t.teamName, 1) as roomStatus
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   r.roommate_pref as roommate_pref
    ,   r.school_name as school_name
    ,   r.school_grade as school_grade
    ,   coalesce(r.ClubTeamName, rCR.club_name) as club_name
    ,   convert(date, u.dob) as [DOB]
    ,   r.position as position
    ,   r.grad_year as grad_year
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.camp_excelexport_room_position installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : camp_excelexport_summer  ("Campers Export (Excel)")
-- Source : reporting.Get_Players_ForExcelExport  (9-col subset). Column set+order
--   from a live legacy run on steps-summerprograms-2026 (2026-05-26). Pure SELECT.
-- 9 columns, in order:
--   teamName, LastName, FirstName, gender, position, school_grade, RegDate,
--   paid_total, owed_total.
-- Joins/where/order identical to the shared proc. No catalog entry to remove
--   (this action was not in TYPE1_REPORT_CATALOG).
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[camp_excelexport_summer]
    @jobID uniqueidentifier
AS
SET NOCOUNT ON;

declare @qaTest varchar(max)
select @qaTest = 'Campers Summer'
select 'QA Test: ' + @qaTest    -- result 1: worksheet name

select
        t.teamName as teamName
    ,   u.LastName as LastName
    ,   u.FirstName as FirstName
    ,   u.gender as gender
    ,   r.position as position
    ,   r.school_grade as school_grade
    ,   convert(date, r.RegistrationTS) as [RegDate]
    ,   r.paid_total
    ,   r.owed_total
from
    Jobs.Registrations r
    inner join dbo.AspNetUsers uF on r.Family_UserId = uF.Id
    inner join dbo.AspNetUsers u on r.UserId = u.Id
    inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
    inner join dbo.Families f on r.Family_UserId = f.Family_UserId
    inner join Leagues.teams t on r.assigned_teamID = t.teamID
    inner join Leagues.leagues l on t.leagueID = l.leagueID
    inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d on t.divID = d.divID
    left join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
where
    r.jobID = @jobID
    and r.bActive = 1
    and roles.Name = 'Player'
order by
        ag.agegroupName
    ,   t.teamName
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.camp_excelexport_summer installed.';
GO

-- -----------------------------------------------------------------------------
-- DEPRECATED 2026-05-28 -- superseded by reporting_migrate.TournamentRosterPacked_Flat
-- below. The flat proc returns one row per registrant (team header denormalized)
-- which lets the RDL render the entire report from a single dataset + single
-- Tablix with a 3-up matrix wrap, eliminating the N+1 SP-call pattern and
-- the brittle subreport ReportName-as-absolute-path issue.
-- This proc is left in place so existing TSICV5 installs don't break on
-- re-run; safe to drop manually once no caller remains.
-- -----------------------------------------------------------------------------
-- Report : TournamentRosterPacked  (PDF, Bold/RDL master dataset)
-- Source : reporting.tourneyteams_for_masterdetail  (copied + cleaned + extended)
-- Contract: plain parameterized SELECT for Bold/RDL dataset binding. Does NOT
--           follow the SP-Excel @qaTest paired-result-set contract used by the
--           other sprocs in this file -- Bold renders this as the MASTER of a
--           master-detail layout (one row per team; detail comes from a
--           subreport on reporting.JobRosters_ExportTournament_ByTeam, which
--           will get its own reporting_migrate.* counterpart later).
-- Changes vs source:
--   * stripped convert(varchar(...)) casts per cast policy (all wrapped columns
--     are nvarchar; cosmetic only -- no truncation, Unicode preserved)
--   * UNCOMMENTED t.active = 1                       -- user-requested 2026-05-27
--   * ADDED ag.agegroupName NOT LIKE '%WAITLIST%'    -- user-requested 2026-05-27
--   * ADDED ag.agegroupName NOT LIKE '%DROPPED%'    -- user-requested 2026-05-27
--   * ADDED LEFT JOIN to Jobs.Registrations on t.clubrep_registrationid for
--     club-name lookup (clubrep's registrant carries the club name field)
--   * ADDED output column ClubName (raw r.Club_Name)
--   * ADDED output column ClubTeamName (computed 'CLUB:TEAM' header format used
--     in the legacy PDF, e.g. 'COPPERMINE:2027 NORTH'; falls back to TeamName
--     when ClubName is NULL -- standalone clubrep-less teams)
--   * RENAMED output alias teamNameShort -> TeamName (more honest -- it's just
--     t.teamName). Dropped the source's t.teamFullName-as-teamName column (the
--     ClubTeamName computed field is now the report header)
--   * ORDER BY: agegroupName, divName, ClubTeamName -- groups by age, then
--     division, then club:team within each division
--   * KEPT a dev-time default for @jobID (lftc-summer-2026) -- executor always
--     overrides at runtime; default just eases interactive EXEC during testing
--
-- Proposed JobReports action (placeholder -- to be enforced once the Bold
-- viewer endpoint exists; @ActionMap row goes in scripts/7 at that point):
--     ExportBoldReport?reportName=TournamentRosterPacked&bUseJobId=true
-- Sibling Bold-rendered reports should follow the same shape:
--     ExportBoldReport?reportName=<RdlName>&bUseJobId=true
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[tourneyteams_for_masterdetail]
    @jobID uniqueidentifier = '66036c7b-e1e8-4110-977f-f786353f2498' -- lftc-summer-2026 (dev-time default; executor overrides)
AS
SET NOCOUNT ON;

select distinct
        t.teamID
    ,   ag.agegroupName
    ,   d.divName
    ,   t.teamName     as TeamName
    ,   r.Club_Name    as ClubName
    ,   case when r.Club_Name is null then t.teamName else r.Club_Name + ':' + t.teamName end as ClubTeamName
from
    Leagues.schedule s
    inner join Leagues.teams t       on t.teamID in (s.T1_ID, s.T2_ID)
    inner join Leagues.agegroups ag  on t.agegroupID = ag.agegroupID
    inner join Leagues.divisions d   on t.divID = d.divID
    left join  Jobs.Registrations r  on t.clubrep_registrationid = r.RegistrationID
where
    s.jobID = @jobID
    and t.active = 1
    and ag.agegroupName not like '%WAITLIST%'
    and ag.agegroupName not like '%DROPPED%'
order by
        ag.agegroupName
    ,   d.divName
    ,   case when r.Club_Name is null then t.teamName else r.Club_Name + ':' + t.teamName end;
GO

PRINT 'reporting_migrate.tourneyteams_for_masterdetail installed.';
GO

-- -----------------------------------------------------------------------------
-- DEPRECATED 2026-05-28 -- superseded by reporting_migrate.TournamentRosterPacked_Flat
-- below. Subreport architecture retired: the flat proc denormalizes registrant
-- + team-header into one result set so the RDL no longer calls a per-team
-- subreport. Safe to drop manually once no caller remains.
-- -----------------------------------------------------------------------------
-- Report : TournamentRosterPacked  (PDF, Bold/RDL DETAIL dataset, subreport)
-- Source : reporting.JobRosters_ExportTournament_ByTeam  (copied + cast-stripped)
-- Contract: parameterized SELECT for Bold/RDL subreport dataset binding. Called
--           once per team from the master's row, with @teamID passed in.
--           Returns one row per registrant (Staff + Players) for that team.
-- Changes vs source:
--   * stripped all convert(varchar(...)) casts per cast policy (every wrapped
--     value is already nvarchar -- upper/substring/replace preserve source type;
--     casts were Crystal-era ASCII downcasts, cosmetic only)
--   * removed source's commented-out exploratory SELECT block
--   * KEPT a dev-time default for @teamID -- subreport always overrides at
--     runtime; default just eases interactive EXEC during testing
--   * NO filter additions -- master SP already filters out WAITLIST/DROPPED
--     agegroups and inactive teams, so by the time this is called per @teamID
--     the team has already been pre-vetted upstream
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[JobRosters_ExportTournament_ByTeam]
    @teamID uniqueidentifier = '41864a3e-c385-44ee-a2d6-515ee248f6eb' -- dev-time default; subreport always overrides
AS
SET NOCOUNT ON;

select
        upper(u.FirstName) + ' ' + upper(u.LastName) as player
    ,   replace(r.uniform_no, '#', '') as uniform_no
    ,   upper(t.teamName) as teamName
    ,   upper(rCR.club_name) as clubAssociation
    ,   case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then '' else upper(uCR.FirstName + ' ' + uCR.LastName) end as coach
    ,   case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then '' else uCR.email end as coach_email
    ,   upper(ag.agegroupName) as agegroupName
    ,   case when roles.name = 'Staff'
            then substring(u.cellphone, 1, 3) + '-' + substring(u.cellphone, 4, 3) + '-' + substring(u.cellphone, 7, 4)
            else r.school_name
        end as school_name
    ,   r.position
    ,   r.grad_year
    ,   case coalesce(r.bCollegeCommit, 0) when 0 then '' else 'yes' end as bCollegeCommit
    ,   r.gpa
    ,   r.college_commit
    ,   case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then '' else substring(uCR.cellphone, 1, 3) + '-' + substring(uCR.cellphone, 4, 3) + '-' + substring(uCR.cellphone, 7, 4) end as coach_cellphone
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
    t.teamID = @teamID
    and r.bActive = 1
    and roles.Name in ('Staff', 'Player')
order by
    roles.Name desc, -- Staff first, then Players
    u.LastName,
    u.FirstName;
GO

PRINT 'reporting_migrate.JobRosters_ExportTournament_ByTeam installed.';
GO

-- -----------------------------------------------------------------------------
-- Report : TournamentRosterPacked  (PDF, Bold/RDL flat dataset; single Tablix)
-- Replaces: reporting_migrate.tourneyteams_for_masterdetail  (master)
--           reporting_migrate.JobRosters_ExportTournament_ByTeam  (subreport)
-- Why    : Master-detail subreport architecture forced N+1 SP calls (1 master +
--          one per team) and required cross-RDL file resolution (subreport
--          ReportName as an absolute Windows path -- not deploy-portable).
--          Also: SSRS column-group scope rules forbid RowNumber("Details") in
--          the column hierarchy, so a per-division 3-up matrix wrap is not
--          expressible in the layout layer alone. Cleanest fix is a flat
--          dataset with a precomputed divTeamRow.
-- Contract: one row per registrant (Staff + Player) of every team in the
--           tournament. Team-header fields (clubTeamName) denormalized onto
--           every registrant row. Includes divTeamRow = ROW_NUMBER() OVER
--           (PARTITION BY agegroupName, divName ORDER BY clubTeamName, teamID)
--           so the parent RDL can drive a 3-up matrix wrap via
--             row group   = Ceiling(divTeamRow / 3)
--             column group = (divTeamRow - 1) Mod 3
--           and a teamID row group for the team panel.
-- Filters : Same as legacy master (active teams, exclude WAITLIST/DROPPED
--           agegroups, scheduled-for-this-job teams via Leagues.schedule).
-- -----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE [reporting_migrate].[TournamentRosterPacked_Flat]
    @jobID uniqueidentifier = '66036c7b-e1e8-4110-977f-f786353f2498' -- lftc-summer-2026 dev-time default; executor overrides
AS
SET NOCOUNT ON;

with TournamentTeams as (
    select distinct
            t.teamID
        ,   ag.agegroupName
        ,   d.divName
        ,   case when rCR.Club_Name is null then upper(t.teamName)
                 else upper(rCR.Club_Name + ':' + t.teamName)
            end as clubTeamName
        -- Club rep contact (per-team, denormalized onto every registrant row).
        -- 'Club Rep' is the dummy placeholder user for teams that have no real
        -- club rep assigned; suppress those to empty strings (legacy convention).
        ,   case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then ''
                 else upper(uCR.FirstName + ' ' + uCR.LastName)
            end as clubRepName
        ,   case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then ''
                 else substring(uCR.cellphone, 1, 3) + '-' + substring(uCR.cellphone, 4, 3) + '-' + substring(uCR.cellphone, 7, 4)
            end as clubRepCellphone
        ,   case when uCR.FirstName = 'Club' and uCR.LastName = 'Rep' then ''
                 else uCR.email
            end as clubRepEmail
    from
        Leagues.schedule s
        inner join Leagues.teams t        on t.teamID in (s.T1_ID, s.T2_ID)
        inner join Leagues.agegroups ag   on t.agegroupID = ag.agegroupID
        inner join Leagues.divisions d    on t.divID = d.divID
        left  join Jobs.Registrations rCR on t.clubrep_registrationid = rCR.RegistrationID
        left  join dbo.AspNetUsers uCR    on rCR.UserId = uCR.Id
    where
        s.jobID = @jobID
        and t.active = 1
        and ag.agegroupName not like '%WAITLIST%'
        and ag.agegroupName not like '%DROPPED%'
)
select
        tt.agegroupName
    ,   tt.divName
    ,   tt.teamID
    ,   tt.clubTeamName
    ,   tt.clubRepName
    ,   tt.clubRepCellphone
    ,   tt.clubRepEmail
    ,   dense_rank() over (partition by tt.agegroupName, tt.divName
                           order by tt.clubTeamName, tt.teamID) as divTeamRow
    ,   u.FirstName + ' ' + u.LastName as player
    ,   replace(r.uniform_no, '#', '') as uniform_no
    ,   r.position
    ,   case when roles.Name = 'Staff'
            then substring(u.cellphone, 1, 3) + '-' + substring(u.cellphone, 4, 3) + '-' + substring(u.cellphone, 7, 4)
            else r.school_name
        end as school_name
    ,   case coalesce(r.bCollegeCommit, 0) when 0 then '' else 'yes' end as bCollegeCommit
    ,   roles.Name as roleName
    ,   case when roles.Name = 'Staff' then 0 else 1 end as roleSort
    -- isLastRow: drives the RDL to suppress the row-separator line under the
    -- last roster row of each card. Partition + ordering mirror the ORDER BY.
    ,   case when row_number() over (
                 partition by tt.teamID
                 order by case when roles.Name = 'Staff' then 0 else 1 end,
                          case when roles.Name = 'Staff' then null
                               else try_cast(replace(r.uniform_no, '#', '') as int) end,
                          u.LastName, u.FirstName
             ) = count(*) over (partition by tt.teamID)
             then 1 else 0
        end as isLastRow
from
    TournamentTeams tt
    inner join Jobs.Registrations r   on r.assigned_teamID = tt.teamID
    inner join dbo.AspNetRoles roles  on r.RoleId = roles.Id
    inner join dbo.AspNetUsers u      on r.UserId = u.Id
where
    r.bActive = 1
    and roles.Name in ('Staff', 'Player')
order by
        tt.agegroupName
    ,   tt.divName
    ,   tt.clubTeamName
    ,   tt.teamID
    ,   case when roles.Name = 'Staff' then 0 else 1 end                                -- Staff cards-top
    ,   case when roles.Name = 'Staff' then null
             else try_cast(replace(r.uniform_no, '#', '') as int) end                    -- Players: uniform # asc
    ,   u.LastName
    ,   u.FirstName;
GO

PRINT 'reporting_migrate.TournamentRosterPacked_Flat installed.';
GO
