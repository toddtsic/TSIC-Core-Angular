/*
    AR-014 — [reporting].[TournamentKeyAttributes-ALL]

    Change: in the cross-job branch (@jobId = all-zeros, which is how the X-Job Report
    Library always runs it — bUseJobId=false), restore the `ExpiryAdmin > getdate()`
    predicate that was commented out. Nothing else in the proc changes.

    Effect: 375 rows -> 151 (live tournament/league jobs only). Rows with no
    EventStartDate go 261 -> 50, and those 50 are a real build-QA finding rather than
    archive noise. The year filter from the other branch is deliberately NOT applied.

    Note: EventStart / EventEnd were ALREADY in this report. Ann filed AR-014 asking for
    them because SQL Server sorts NULLs first, so the top of her spreadsheet was blank.

    The proc had no definition in source control; this file banks it. Body below is the
    live definition as of 2026-08-23 with the one-line change applied, converted
    CREATE -> CREATE OR ALTER.
*/

/*
exec [reporting].[JobKeyAttributes-ALL] @jobID = '00000000-0000-0000-0000-000000000000'
exec [reporting].[JobKeyAttributes-ALL] @jobID = 'f97eddb3-fee7-4fb2-8e6d-78e302c6b49b' --LI YELLOW JACKETS:NASSAU PLAYERS 2025
exec [reporting].[JobKeyAttributes-ALL] @jobID = 'caef9822-2278-453a-bd08-e5f162267b50' --STEPS LACROSSE:GIRLS ELITE PLAYERS 2024-2025
*/
CREATE OR ALTER procedure [reporting].[TournamentKeyAttributes-ALL]
(
	@jobId uniqueidentifier = '00000000-0000-0000-0000-000000000000'
)

as
set nocount on

declare @qaTest varchar(max)

select @qaTest = 'RawData'
select 'QA Test: ' + @qaTest

declare @thisYear int = datepart(year, getdate())
select convert(varchar, @thisYear)
declare @listAllowedYears table (jobYear varchar(max))
insert into @listAllowedYears select convert(varchar, @thisYear)
insert into @listAllowedYears select convert(varchar, @thisYear + 1)

if @jobId = '00000000-0000-0000-0000-000000000000' begin
select 
	j.jobName,
	convert(char(10), j.EventStartDate, 23) + ' ' + datename(weekday, j.EventStartDate) as EventStart,
	convert(char(10), j.EventEndDate, 23) + ' ' + datename(weekday, j.EventEndDate) as EventEnd,
	j.perTeamCharge,
	(
		select count(*) 
		from 
			Jobs.Registrations a 
			inner join dbo.AspNetRoles b on a.RoleId = b.id 
		where 
			a.jobId = j.jobId 
			and a.bActive = 1 and b.Name = 'Club Rep' 
	) as countCR,
	(
		select count(*)
		from
			Leagues.teams a
			inner join Leagues.Agegroups b on a.agegroupId = b.agegroupId
		where
			a.jobId = j.jobId
			and charindex('WAITLIST', b.agegroupName) = 0
			and charindex('DROPPED', b.agegroupName) = 0
			and a.active = 1
	) as countTeams,
	(
		select count(*)
		from
			Jobs.Registrations a
			inner join dbo.AspNetRoles b on a.RoleId = b.Id
		where
			a.JobId = j.jobId
			and b.Name = 'Player'
			and a.bActive = 1
	) as countPlayers,
	(select l.bHideContacts from Jobs.Job_Leagues jl inner join Leagues.Leagues l on jl.LeagueId = l.leagueID where jl.jobId = j.jobId) as bHideContacts,
	j.bTeamsFullPaymentRequired as fullPayOn,
	j.bRegistrationAllowPlayer as playerRegOn,
	j.bRegistrationAllowTeam as teamRegOn,
	j.bClubRepAllowAdd as crAdd,
	j.bClubRepAllowEdit as crEdit,
	j.bClubRepAllowDelete as crDelete,
	j.bAllowMobileLogin,
	j.bSuspendPublic,
	j.bAddProcessingFees,
	j.bApplyProcessingFeesToTeamDeposit,
	j.bOfferPlayerRegsaverInsurance as bVIEnabled,
	c.customerName,
	j.year,
	j.season
from
	Jobs.Jobs j
	inner join Jobs.Customers c on j.customerID = c.customerId
	inner join reference.Billing_Types bt on j.BillingTypeID = bt.BillingTypeID
	inner join reference.JobTypes jt on j.JobTypeID = jt.JobTypeID
	--inner join @listAllowedYears ay on j.year = ay.jobYear
where
	j.ExpiryAdmin > getdate()
	and jt.JobTypeName in ('Tournament Scheduling', 'League Scheduling')
order by
	j.EventStartDate,
	j.EventEndDate
	--c.customerName,
	--j.year desc,
	--j.season desc,
	--jt.JobTypeName,
	--j.jobName

end else begin
select 
	j.jobName,
	convert(char(10), j.EventStartDate, 23) + ' ' + datename(weekday, j.EventStartDate) as EventStart,
	convert(char(10), j.EventEndDate, 23) + ' ' + datename(weekday, j.EventEndDate) as EventEnd,
	j.perTeamCharge,
	(
		select count(*) 
		from 
			Jobs.Registrations a 
			inner join dbo.AspNetRoles b on a.RoleId = b.id 
		where 
			a.jobId = j.jobId 
			and a.bActive = 1 and b.Name = 'Club Rep' 
	) as countCR,
	(
		select count(*)
		from
			Leagues.teams a
			inner join Leagues.Agegroups b on a.agegroupId = b.agegroupId
		where
			a.jobId = j.jobId
			and charindex('WAITLIST', b.agegroupName) = 0
			and charindex('DROPPED', b.agegroupName) = 0
			and a.active = 1
	) as countTeams,
	(
		select count(*)
		from
			Jobs.Registrations a
			inner join dbo.AspNetRoles b on a.RoleId = b.Id
		where
			a.JobId = j.jobId
			and b.Name = 'Player'
			and a.bActive = 1
	) as countPlayers,
	(select l.bHideContacts from Jobs.Job_Leagues jl inner join Leagues.Leagues l on jl.LeagueId = l.leagueID where jl.jobId = j.jobId) as bHideContacts,
	j.bTeamsFullPaymentRequired as fullPayOn,
	j.bRegistrationAllowPlayer as playerRegOn,
	j.bRegistrationAllowTeam as teamRegOn,
	j.bClubRepAllowAdd as crAdd,
	j.bClubRepAllowEdit as crEdit,
	j.bClubRepAllowDelete as crDelete,
	j.bAllowMobileLogin,
	j.bSuspendPublic,
	j.bAddProcessingFees,
	j.bApplyProcessingFeesToTeamDeposit,
	j.bOfferPlayerRegsaverInsurance as bVIEnabled,
	c.customerName,
	j.year,
	j.season
from
	Jobs.Jobs j
	inner join Jobs.Customers c on j.customerID = c.customerId
	inner join reference.Billing_Types bt on j.BillingTypeID = bt.BillingTypeID
	inner join reference.JobTypes jt on j.JobTypeID = jt.JobTypeID
	inner join @listAllowedYears ay on j.year = ay.jobYear
where
	j.ExpiryAdmin > getdate()
	and jt.JobTypeName in ('Tournament Scheduling', 'League Scheduling')

order by
	j.EventStartDate,
	j.EventEndDate
	--c.customerName,
	--j.year desc,
	--j.season desc,
	--jt.JobTypeName,
	--j.jobName
end
