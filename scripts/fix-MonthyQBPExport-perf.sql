-- =====================================================================
-- Month-end QBP export (ADN EndOfMonth/IIF) - performance fix (2026-10-01)
--
-- adn.MonthyQBPExport_Automated ran 17.7s on the 10-01 prod restore against
-- the app's 30s SQL command timeout. 8.5s after this script.
--
-- Verified on the 10-01 prod restore, Sep 2026: original and rewritten
-- chains run side by side as session temp procs; all 2,947 output lines of
-- every result set IDENTICAL in order, plus the 71 Monthly_Job_Stats rows
-- UpdateMonthlyJobStats_CalcFields computes (guard bypassed, written to a
-- temp table) IDENTICAL.
--
-- Changes (everything else in each proc is byte-for-byte the live text):
--   ExportMonthlyProcessingFees / ExportMonthlyJobRetainers /
--   ExportMonthlyCustomerChecks:
--     join a month slice of adn.vTxs (#vTxsMonth) instead of the whole view.
--     Slice = the view's own rows for the month's transaction IDs, so columns
--     and row multiplicity are the view's. year()/month() predicates kept.
--   UpdateMonthlyJobStats_CalcFields:
--     same slice over @month + next month (its filter is BETWEEN start/end,
--     end = midnight on the 1st), and the per-job active-player COUNT is
--     computed once into #regCounts instead of per row of a table variable.
--   MonthyQBPExport_Automated:
--     adn.Txs filtered on its own predicates BEFORE the per-row fnSplit job
--     lookup.
--
-- NOT changed, flagged for a separate ruling: UpdateMonthlyJobStats_CalcFields
-- player prep reads "ra.active = 1 and (createdate between) or SettlementTS
-- between" - AND binds tighter than OR, so the settlement leg ignores
-- ra.active. Preserved as-is so output stays identical.
--
-- Hand DDL - apply in SSMS (TSICV5).
-- =====================================================================

/*
select 
	r.RegistrationTS
from
	Jobs.Registrations r 
	inner join Jobs.Jobs j on r.JobId = j.jobId
	left join Jobs.Registration_Accounting ra on r.RegistrationId = ra.RegistrationId
where
	r.JobId = 'ab1a0e54-75fa-445b-99c4-74401059f175'
order by
	r.RegistrationTS desc
*/
ALTER procedure [adn].[UpdateMonthlyJobStats_CalcFields]
(
		@year int = 2024
	,	@month int = 4
)
as
set nocount on

declare @startDate DateTime = datefromparts(@year, @month, 1)
declare @endDate DateTime = dateadd(month, 1, datefromparts(@year, @month, 1))

declare @superUserId nvarchar(254) = (select Id from dbo.AspNetUsers where UserName = 'TSICSuperUser')

-- PERF (2026-10-01): a slice of adn.vTxs covering @month AND the following month, materialized
-- once. The two queries below test "SettlementTS between @startDate and @endDate" and @endDate is
-- midnight on the 1st of the NEXT month, so the slice spans both months; the predicates themselves
-- are unchanged and still applied to it. Rows come from the view itself (same columns, same row
-- multiplicity); only those months' transaction IDs are admitted, resolved from adn.Txs by the same
-- substring parse the view uses (chars 8-11 = year, 4-6 = month abbrev). Both reads are LEFT JOINs
-- whose only use of the view is that predicate under SELECT DISTINCT, so a transaction outside the
-- slice (NULL here, out-of-range there) fails it identically. Joining the whole view instead walked
-- all of adn.Txs through its 9 joins, twice.
declare @sliceMonth1Abbrev nvarchar(3) = choose(month(@startDate),
	'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec')
declare @sliceMonth2Abbrev nvarchar(3) = choose(month(@endDate),
	'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec')

-- IDs land in a keyed temp table FIRST: its row count is known to the optimizer. Filtering the
-- view by a variable-driven subquery instead let it expand the whole view (minutes, not ms).
create table #periodTxIds ([Transaction ID] nvarchar(100) not null primary key)
insert into #periodTxIds([Transaction ID])
select t.[Transaction ID]
from adn.Txs t
where
	(substring(t.[Settlement Date Time], 8, 4) = convert(nvarchar(4), year(@startDate))
		and substring(t.[Settlement Date Time], 4, 3) = @sliceMonth1Abbrev)
	or (substring(t.[Settlement Date Time], 8, 4) = convert(nvarchar(4), year(@endDate))
		and substring(t.[Settlement Date Time], 4, 3) = @sliceMonth2Abbrev)

select v.*
into #vTxsPeriod
from adn.vTxs v
where v.[Transaction ID] in (select p.[Transaction ID] from #periodTxIds p)

declare  @pPrepTable table (
		jobID uniqueidentifier
	, 	year int
	, 	month int
	, 	prevNonZeroPlayerCount_RowId int
	,	Count_ActivePlayers_LastMonth int
)

insert into @pPrepTable(
		jobID
	,	year
	,	month
	,	prevNonZeroPlayerCount_RowId
)
select distinct 
		r.jobID
	,	@year as year
	,	@month as month
	,	(
			select max(a.aid)
			from
				adn.Monthly_Job_Stats a
			where
				jobId = r.jobId
				and coalesce(a.Count_ActivePlayersToDate, 0) > 0
				and datefromparts(a.year, a.month, 1) < datefromparts(@year, @month, 1)
		)
from
	Jobs.Registrations r 
	inner join Jobs.Jobs j on r.JobId = j.jobId
	left join Jobs.Registration_Accounting ra on r.RegistrationId = ra.RegistrationId
	left join #vTxsPeriod adn on ra.adnTransactionID = adn.[Transaction ID]
where
	(
		(r.bActive = 1 and r.fee_base > 0 and r.RegistrationTS between @startDate and @endDate)
		or ( ra.active = 1 and 
			(ra.createdate between @startDate and @endDate)
			or adn.SettlementTS between @startDate and @endDate
		)
	)
	and r.RoleId in ('DAC0C570-94AA-4A88-8D73-6034F1F72F3A', '15411BA2-96D7-482D-8745-40B123EE4687') --player and unassigned player

--select 'debug', * from @pPrepTable where JobId = 'ab1a0e54-75fa-445b-99c4-74401059f175'	

update prep set 
	Count_ActivePlayers_LastMonth = 
		(
			select 
				mjs.Count_ActivePlayersToDate 
			from 
				adn.Monthly_Job_Stats mjs
			where 
				mjs.aid = prep.prevNonZeroPlayerCount_RowId
		)
from
	@pPrepTable prep

declare @tmpMDS table(
		jobID uniqueidentifier
	,	year int
	,	month int
	,	Count_ActivePlayersToDate int
	,	Count_ActiveTeamsToDate int
	,	Count_ActivePlayers_LastMonth int
	,	Count_ActiveTeams_LastMonth int
)

-- PERF (2026-10-01): the former correlated per-job COUNT was planned per row of the table variable
-- (costed as 1 row) and took ~4.4s; one filtered pass over Jobs.Registrations takes ~0.1s.
-- Same filters, same COUNT(DISTINCT), computed ONCE per job into #regCounts (no table variable in
-- that statement), then joined back. A job with no qualifying registrations gets 0, exactly as
-- the correlated COUNT over an empty set did.
select
		a.jobID
	,	COUNT(
			distinct a.RegistrationId
		) as Count_ActivePlayersToDate
into #regCounts
from
	Jobs.Registrations a
where
	a.RegistrationTS <  @endDate
	and a.bActive = 1 --must restrict to active in order to bill for a registration
	and a.RoleId  in ('15411BA2-96D7-482D-8745-40B123EE4687', 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A') --Unassigned Player, Player
	and a.fee_base > 0
group by
	a.jobID

insert into @tmpMDS(
		jobID
	,	year
	,	month
	,	Count_ActivePlayersToDate
	,	Count_ActivePlayers_LastMonth
)
select
		prep.jobID
	,	prep.year
	,	prep.month
	,	coalesce(cnt.Count_ActivePlayersToDate, 0) as Count_ActivePlayersToDate
	,	prep.Count_ActivePlayers_LastMonth
from
	@pPrepTable prep
	left join #regCounts cnt on cnt.jobID = prep.jobID
group by
		prep.jobID
	,	prep.year
	,	prep.month
	,	prep.Count_ActivePlayers_LastMonth
	,	cnt.Count_ActivePlayersToDate

declare  @tPrepTable table (
		jobID uniqueidentifier
	,	year int
	,	month int
	,	prevNonZeroTeamCount_RowId int
	,	Count_ActiveTeams_LastMonth int
)

insert into @tPrepTable(
		jobID
	,	year
	,	month
	,	prevNonZeroTeamCount_RowId
)
select distinct
		t.jobID
	,	@year as year
	,	@month as month
	,	(
			select max(a.aid)
			from
				adn.Monthly_Job_Stats a
			where
				jobId = t.jobId
				and datefromparts(a.year, a.month, 1) < datefromparts(@year, @month, 1)
		)
from
	Leagues.Teams t	
	left join Jobs.Registration_Accounting ra on t.teamId = ra.teamId
	left join #vTxsPeriod adn on ra.adnTransactionID = adn.[Transaction ID]
where
	(
		(t.active = 1 and t.fee_base > 0 and t.createdate between @startDate and @endDate)
		or (
			(ra.active = 1 and 
				(
					(ra.createdate between @startDate and @endDate)
					or
					(adn.SettlementTS between @startDate and @endDate)
				)
			)
		)
	)

insert into @tPrepTable(jobId, year, month, prevNonZeroTeamCount_RowId)
select p.jobID, p.year, p.month, p.prevNonZeroPlayerCount_RowId
from
	@pPrepTable p
where
	not exists(select * from @tPrepTable where jobId = p.jobId)

update prep set 
	Count_ActiveTeams_LastMonth = 
		(
			select 
				mjs.Count_ActiveTeamsToDate 
			from 
				adn.Monthly_Job_Stats mjs
			where 
				mjs.aid = prep.prevNonZeroTeamCount_RowId
		)
from
	@tPrepTable prep

insert into @tmpMDS(
		jobID
	,	year
	,	month
	,	Count_ActiveTeamsToDate
	,	Count_ActiveTeams_LastMonth
)
select 
		prep.jobID
	,	prep.year
	,	prep.month
	,	(
			select COUNT(*) 
			from 
				Leagues.teams a 
				inner join Leagues.agegroups b on a.agegroupID = b.agegroupID
			where 
				a.jobID = prep.jobid 
				and a.createdate < @endDate
				and a.active = 1
				and a.fee_base > 0
		) as Count_ActiveTeamsToDate
	,	prep.Count_ActiveTeams_LastMonth
from 
	@tPrepTable prep
group by
		prep.jobID
	,	prep.year
	,	prep.month
	,	prep.Count_ActiveTeams_LastMonth

--ONLY INSERT IF A RECORD DOESN'T EXIST FOR THIS JOB, YEAR, AND MONTH (EXISTS CLAUSE BELOW ADDRESSES...)
insert into adn.Monthly_Job_Stats(
		jobID
	,	year
	,	month
	,	Count_ActivePlayersToDate
	,	Count_ActivePlayersToDate_LastMonth
	,	Count_NewPlayers_ThisMonth
	,	Count_ActiveTeamsToDate
	,	Count_ActiveTeamsToDate_LastMonth
	,	Count_NewTeams_ThisMonth
	,	lebUserID
)

select --(select x.jobName from Jobs.Jobs x where x.jobID = coalesce(p.jobId, t.jobId)),
		coalesce(p.jobID, t.jobID)
	,	coalesce(p.YEAR, t.year)
	,	coalesce(p.MONTH, t.month)
	,	coalesce(p.Count_ActivePlayersToDate, 0)
	,	coalesce(p.Count_ActivePlayers_LastMonth, 0)
	,	case 
			when coalesce(p.Count_ActivePlayersToDate, 0) > 0 
			then coalesce(p.Count_ActivePlayersToDate, 0) - coalesce(p.Count_ActivePlayers_LastMonth, 0) 
			else coalesce(p.Count_ActivePlayersToDate, 0) 
		end 
	,	coalesce(t.Count_ActiveTeamsToDate, 0) 
	,	coalesce(t.Count_ActiveTeams_LastMonth, 0) 
	,	case 
			when coalesce(t.Count_ActiveTeamsToDate, 0) > 0 
			then coalesce(t.Count_ActiveTeamsToDate, 0) - coalesce(t.Count_ActiveTeams_LastMonth, 0) 
			else coalesce(t.Count_ActiveTeamsToDate, 0) 
		end 
	,	@superUserId 
from
	(
		select 
			*
		from 
			@tmpMDS tmp
		where
			not tmp.Count_ActivePlayersToDate is null
	) p
	full outer join 
	(
		select 
			* 
		from 
			@tmpMDS tmp
		where
			not tmp.Count_ActiveTeamsToDate is null
	) t on p.jobID = t.jobID and p.year = t.year and p.month = t.month
where	
	not exists(
		select * 
		from adn.Monthly_Job_Stats mds 
		where 
			mds.jobID = coalesce(p.jobID, t.jobID) 
			and mds.year = coalesce(p.year, t.year) 
			and mds.month = coalesce(p.month, t.month)
	)
--order by 1

--declare @superUserId nvarchar(254) = (select Id from dbo.AspNetUsers where UserName = 'TSICSuperUser')

--declare  @pPrepTable table (
--		jobID uniqueidentifier
--	, 	year int
--	, 	month int
--	, 	prevNonZeroPlayerCount_RowId int
--	,	Count_ActivePlayers_LastMonth int
--)

--insert into @pPrepTable(
--		jobID
--	,	year
--	,	month
--	,	prevNonZeroPlayerCount_RowId
--)
--select distinct 
--		txs.jobID
--	,	@year as year
--	,	@month as month
--	,	(
--			select max(a.aid)
--			from
--				adn.Monthly_Job_Stats a
--			where
--				jobId = txs.jobId
--				and a.Count_ActivePlayersToDate > 0
--				and 
--				(
--						convert(
--							datetime, 
--							CONVERT(varchar, a.month) + '/01/' + CONVERT(varchar, a.year)
--						) <
--						convert(
--							datetime, 
--							CONVERT(varchar, @month) + '/01/' + CONVERT(varchar, @year)
--						)
--				)
--		)
--from
--	adn.vtxs txs
--where
--	txs.[transaction status] in ('Settled Successfully', 'Credited')
--	and convert(money, [Txs].[Settlement Amount]) != 0
--	and txs.SettlementYear = @year
--	and txs.SettlementMonth = @month
	
--	and not exists (
--		select * from adn.Monthly_Job_Stats mjs where mjs.jobID = txs.jobID and mjs.year = @year and month = @month
--	)

--update prep set 
--	Count_ActivePlayers_LastMonth = 
--		(
--			select 
--				mjs.Count_ActivePlayersToDate 
--			from 
--				adn.Monthly_Job_Stats mjs
--			where mjs.aid = prep.prevNonZeroPlayerCount_RowId
--		)
--from
--	@pPrepTable prep


--declare @tmpMDS table(
--		jobID uniqueidentifier
--	,	year int
--	,	month int
--	,	Count_ActivePlayersToDate int
--	,	Count_ActiveTeamsToDate int
--	,	Count_ActivePlayers_LastMonth int
--	,	Count_ActiveTeams_LastMonth int

--)

--insert into @tmpMDS(
--		jobID
--	,	year
--	,	month
--	,	Count_ActivePlayersToDate
--	,	Count_ActivePlayers_LastMonth
--)
--select 
--		prep.jobID
--	,	prep.year
--	,	prep.month
--	,	(
--			select COUNT(
--				distinct convert(varchar(MAX), a.UserId) + convert(varchar(max), a.assigned_teamID)
--			) 
--			from 
--				Jobs.Registrations a 
--				inner join Leagues.Teams b on a.assigned_teamID = b.teamID
--				inner join Leagues.agegroups c on b.agegroupID = c.agegroupID
--			where 
--				a.jobID = prep.jobid 
--				and a.RegistrationTS <  
--					dateadd(
--						month, 
--						1, 
--						convert(
--							datetime, 
--							CONVERT(varchar, prep.month) + '/01/' + CONVERT(varchar, prep.year)
--						)
--					)
--				and a.bActive = 1
--				and a.RoleId  in ('15411BA2-96D7-482D-8745-40B123EE4687', 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A') --Unassigned Player, Player
--				and charindex('WAITLIST', c.agegroupName) = 0
--				--and charindex('REGISTRATION', c.agegroupName) = 0
--				and (j.customerId = '76586de3-acb3-42ee-91eb-48597ca06802' or (charindex('TRYOUT', b.teamName) = 0))
--				and (j.customerId = '76586de3-acb3-42ee-91eb-48597ca06802' or (charindex('TRYOUT', c.agegroupName) = 0))
--		) as Count_ActivePlayersToDate
--	,	prep.Count_ActivePlayers_LastMonth
--from 
--	@pPrepTable prep
--	inner join Jobs.Jobs j on prep.jobId = j.jobId
--group by
--		j.customerId
--	,	prep.jobID
--	,	prep.year
--	,	prep.month
--	,	prep.Count_ActivePlayers_LastMonth

--declare  @tPrepTable table (
--		jobID uniqueidentifier
--	,	year int
--	,	month int
--	,	prevNonZeroTeamCount_RowId int
--	,	Count_ActiveTeams_LastMonth int
--)

--insert into @tPrepTable(
--		jobID
--	,	year
--	,	month
--	,	prevNonZeroTeamCount_RowId
--)
--select distinct
--		t.jobID
--	,	@year as year
--	,	@month as month
--	,	(
--			select max(a.aid)
--			from
--				adn.Monthly_Job_Stats a
--			where
--				jobId = t.jobId
--				and a.Count_ActiveTeamsToDate > 0
--				and 
--				(
--						convert(
--							datetime, 
--							CONVERT(varchar, a.month) + '/01/' + CONVERT(varchar, a.year)
--						) <
--						convert(
--							datetime, 
--							CONVERT(varchar, @month) + '/01/' + CONVERT(varchar, @year)
--						)
--				)
--		)
--from
--	Leagues.teams t
--	inner join Jobs.Registration_Accounting ja on t.teamID = ja.teamID
--	inner join Jobs.Registrations r on ja.RegistrationID = r.RegistrationID
--where
--	year(ja.createdate) = @year
--	and month(ja.createdate) = @month

--	and ja.active = 1
--	and t.active = 1

--	and not exists (
--		select * from adn.Monthly_Job_Stats mjs where mjs.jobID = r.jobID and mjs.year = @year and month = @month
--	)

--update prep set 
--	Count_ActiveTeams_LastMonth = 
--		(
--			select 
--				mjs.Count_ActiveTeamsToDate 
--			from 
--				adn.Monthly_Job_Stats mjs
--			where mjs.aid = prep.prevNonZeroTeamCount_RowId
--		)
--from
--	@tPrepTable prep

--insert into @tmpMDS(
--		jobID
--	,	year
--	,	month
--	,	Count_ActiveTeamsToDate
--	,	Count_ActiveTeams_LastMonth
--)
--select 
--		prep.jobID
--	,	prep.year
--	,	prep.month
--	,	(
--			select COUNT(*) 
--			from 
--				Leagues.teams a 
--				inner join Leagues.agegroups b on a.agegroupID = b.agegroupID
--			where 
--				a.jobID = prep.jobid 
--				and a.createdate <  
--					dateadd(
--						month, 
--						1, 
--						convert(
--							datetime, 
--							CONVERT(varchar, prep.month) + '/01/' + CONVERT(varchar, prep.year)
--						)
--					)
--				and a.active = 1
--				and charindex('WAITLIST', b.agegroupName) = 0
--				and charindex('DROPPED', b.agegroupName) = 0
--		) as Count_ActiveTeamsToDate
--	,	prep.Count_ActiveTeams_LastMonth
--from 
--	@tPrepTable prep
--group by
--		prep.jobID
--	,	prep.year
--	,	prep.month
--	,	prep.Count_ActiveTeams_LastMonth

----ONLY INSERT IF A RECORD DOESN'T EXIST FOR THIS JOB, YEAR, AND MONTH (EXISTS CLAUSE BELOW ADDRESSES...)
--insert into adn.Monthly_Job_Stats(
--		jobID
--	,	year
--	,	month
--	,	Count_ActivePlayersToDate
--	,	Count_ActivePlayersToDate_LastMonth
--	,	Count_NewPlayers_ThisMonth
--	,	Count_ActiveTeamsToDate
--	,	Count_ActiveTeamsToDate_LastMonth
--	,	Count_NewTeams_ThisMonth
--	,	lebUserID
--)
--select
--		coalesce(p.jobID, t.jobID)
--	,	coalesce(p.YEAR, t.year)
--	,	coalesce(p.MONTH, t.month)
--	,	coalesce(p.Count_ActivePlayersToDate, 0)
--	,	coalesce(p.Count_ActivePlayers_LastMonth, 0)
--	,	case 
--			when coalesce(p.Count_ActivePlayersToDate, 0) > 0 
--			then coalesce(p.Count_ActivePlayersToDate, 0) - coalesce(p.Count_ActivePlayers_LastMonth, 0) 
--			else coalesce(p.Count_ActivePlayersToDate, 0) 
--		end
--	,	coalesce(t.Count_ActiveTeamsToDate, 0)
--	,	coalesce(t.Count_ActiveTeams_LastMonth, 0)
--	,	case 
--			when coalesce(t.Count_ActiveTeamsToDate, 0) > 0 
--			then coalesce(t.Count_ActiveTeamsToDate, 0) - coalesce(t.Count_ActiveTeams_LastMonth, 0) 
--			else coalesce(t.Count_ActiveTeamsToDate, 0) 
--		end
--	,	@superUserId
--from
--	(
--		select 
--			*
--		from 
--			@tmpMDS tmp
--		where
--			not tmp.Count_ActivePlayersToDate is null
--	) p
--	full outer join 
--	(
--		select 
--			* 
--		from 
--			@tmpMDS tmp
--		where
--			not tmp.Count_ActiveTeamsToDate is null
--	) t on p.jobID = t.jobID and p.year = t.year and p.month = t.month
--where	
--	not exists(
--		select * 
--		from adn.Monthly_Job_Stats mds 
--		where 
--			mds.jobID = coalesce(p.jobID, t.jobID) 
--			and mds.year = coalesce(p.year, t.year) 
--			and mds.month = coalesce(p.month, t.month)
--	)





--ALTER procedure [adn].[UpdateMonthlyJobStats_CalcFields]
--(
--		@year int = 2016
--	,	@month int = 8
--)
--as
--set nocount on
--declare  @pPrepTable table (jobID uniqueidentifier, year int, month int)
--insert into @pPrepTable(jobID, year, month)
--select distinct
--		atP.jobID
--	,	@year as year
--	,	@month as month
--from
--	Jobs.Registrations atP
--	inner join Jobs.Registration_Accounting ata on atP.RegistrationID = ata.RegistrationID
--where
--	--ata.active = 1
--	--and atP.bActive = 1
	
--	atP.RoleId in ('15411BA2-96D7-482D-8745-40B123EE4687', 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A')
--	and year(ata.createdate) = @year
--	and month(ata.createdate) = @month
--	and not exists (
--		select * from adn.Monthly_Job_Stats mjs where mjs.jobID = atP.jobID and mjs.year = @year and month = @month
--	)

--declare @tmpMDS table(jobID uniqueidentifier, year int, month int, Count_ActivePlayersToDate int, Count_ActiveTeamsToDate int)
--insert into @tmpMDS(jobID, year, month, Count_ActivePlayersToDate)
--select 
--		t.jobID
--	,	t.year
--	,	t.month
--	,	(

--			select COUNT(distinct convert(varchar(MAX), a.UserId) + convert(varchar(max), a.assigned_teamID)) from Jobs.Registrations a 
--			--select COUNT(*) from Jobs.Registrations a 
--			where 
--				a.jobID = t.jobid 
--				and a.RegistrationTS <  
--					dateadd(
--						month, 
--						1, 
--						convert(
--							datetime, 
--							CONVERT(varchar, t.month) + '/01/' + CONVERT(varchar, t.year)
--						)
--					)
--				and a.bActive = 1
--				and a.RoleId  in ('15411BA2-96D7-482D-8745-40B123EE4687', 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A')
--		) as Count_ActivePlayersToDate
--from 
--	@pPrepTable t
--group by
--		t.jobID
--	,	t.year
--	,	t.month

--declare  @tPrepTable table (jobID uniqueidentifier, year int, month int)
--insert into @tPrepTable(jobID, year, month)
--select distinct
--		t.jobID
--	,	@year as year
--	,	@month as month
--from
--	Leagues.teams t
--	inner join Jobs.Registration_Accounting ja on t.teamID = ja.teamID
--	inner join Jobs.Registrations r on ja.RegistrationID = r.RegistrationID
--where
--	--ja.active = 1
--	--and t.active = 1
--	year(ja.createdate) = @year
--	and month(ja.createdate) = @month
--	and not exists (
--		select * from adn.Monthly_Job_Stats mjs where mjs.jobID = r.jobID and mjs.year = @year and month = @month
--	)

--insert into @tmpMDS(jobID, year, month, Count_ActiveTeamsToDate)
--select 
--		t.jobID
--	,	t.year
--	,	t.month
--	,	(
--			select COUNT(*) from Leagues.teams a 
--			where 
--				a.jobID = t.jobid 
--				and a.createdate <  
--					dateadd(
--						month, 
--						1, 
--						convert(
--							datetime, 
--							CONVERT(varchar, t.month) + '/01/' + CONVERT(varchar, t.year)
--						)
--					)
--				and a.active = 1
--		) as Count_ActiveTeamsToDate
--from 
--	@tPrepTable t
--group by
--		t.jobID
--	,	t.year
--	,	t.month

--insert into adn.Monthly_Job_Stats(jobID, year, month, Count_ActivePlayersToDate, Count_ActiveTeamsToDate, lebUserID)
--select
--		coalesce(p.jobID, t.jobID)
--	,	coalesce(p.YEAR, t.year)
--	,	coalesce(p.MONTH, t.month)
--	,	p.Count_ActivePlayersToDate
--	,	t.Count_ActiveTeamsToDate
--	,	(select Id from dbo.AspNetUsers where UserName = 'TSICSuperUser')
--from
--	(
--		select 
--			* 
--		from 
--			@tmpMDS tmp
--		where
--			not tmp.Count_ActivePlayersToDate is null
--	) p
--	full outer join 
--	(
--		select 
--			* 
--		from 
--			@tmpMDS tmp
--		where
--			not tmp.Count_ActiveTeamsToDate is null
--	) t on p.jobID = t.jobID and p.year = t.year and p.month = t.month
--where	
--	not exists(
--		select * from adn.Monthly_Job_Stats mds where mds.jobID = coalesce(p.jobID, t.jobID) and mds.year = coalesce(p.year, t.year) and mds.month = coalesce(p.month, t.month)
--	)


--update mjs set
--		Count_NewPlayers_ThisMonth = Count_ActivePlayersToDate - 
--		coalesce((	
--			select a.Count_ActivePlayersToDate from adn.Monthly_Job_Stats a 
--			where
--				a.jobID = mjs.jobID
--				and a.year = year(
--							dateadd(
--								month, 
--								-1, 
--								convert(datetime,CONVERT(varchar, mjs.month) + '/01/' + CONVERT(varchar, mjs.year))
--							)
--						)
--				and a.month = month(
--							dateadd(
--								month, 
--								-1, 
--								convert(datetime,CONVERT(varchar, mjs.month) + '/01/' + CONVERT(varchar, mjs.year))
--							)
--						)
--		),0) 
		
--		,Count_NewTeams_ThisMonth = Count_ActiveTeamsToDate - 
--		coalesce((	
--			select a.Count_ActiveTeamsToDate from adn.Monthly_Job_Stats a 
--			where
--				a.jobID = mjs.jobID
--				and a.year = year(
--							dateadd(
--								month, 
--								-1, 
--								convert(datetime,CONVERT(varchar, mjs.month) + '/01/' + CONVERT(varchar, mjs.year))
--							)
--						)
--				and a.month = month(
--							dateadd(
--								month, 
--								-1, 
--								convert(datetime,CONVERT(varchar, mjs.month) + '/01/' + CONVERT(varchar, mjs.year))
--							)
--						)
--		), 0) 
		
--		, Count_ActivePlayersToDate_LastMonth = 
--		coalesce((	
--			select a.Count_ActivePlayersToDate from adn.Monthly_Job_Stats a 
--			where
--				a.jobID = mjs.jobID
--				and a.year = year(
--							dateadd(
--								month, 
--								-1, 
--								convert(datetime,CONVERT(varchar, mjs.month) + '/01/' + CONVERT(varchar, mjs.year))
--							)
--						)
--				and a.month = month(
--							dateadd(
--								month, 
--								-1, 
--								convert(datetime,CONVERT(varchar, mjs.month) + '/01/' + CONVERT(varchar, mjs.year))
--							)
--						)
--		), 0) 
--	,	Count_ActiveTeamsToDate_LastMonth = 
--		coalesce((	
--			select a.Count_ActiveTeamsToDate from adn.Monthly_Job_Stats a 
--			where
--				a.jobID = mjs.jobID
--				and a.year = year(
--							dateadd(
--								month, 
--								-1, 
--								convert(datetime,CONVERT(varchar, mjs.month) + '/01/' + CONVERT(varchar, mjs.year))
--							)
--						)
--				and a.month = month(
--							dateadd(
--								month, 
--								-1, 
--								convert(datetime,CONVERT(varchar, mjs.month) + '/01/' + CONVERT(varchar, mjs.year))
--							)
--						)
--		), 0) 
--from
--	adn.Monthly_Job_Stats mjs	
--where
--	mjs.year = @year
--	and mjs.month = @month
GO


ALTER procedure [adn].[ExportMonthlyProcessingFees]
(
	@settlementMonth int = 11,
	@settlementYear int = 2023
)
as set nocount on

-- @ccRate removed; now uses j.ProcessingFeePercent coalesced to 0.035 per job

-- PERF (2026-10-01): the month's slice of adn.vTxs, materialized once. Rows come from the view
-- itself (same columns, same row multiplicity); only the month's transaction IDs are admitted,
-- resolved from adn.Txs by the same substring parse the view uses (chars 8-11 = year, 4-6 = month
-- abbrev). The year()/month() predicates below are unchanged and still applied to this slice.
-- Joining the whole view instead walked all of adn.Txs through its 9 joins.
declare @sliceMonthAbbrev nvarchar(3) = choose(@settlementMonth,
	'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec')

-- IDs land in a keyed temp table FIRST: its row count is known to the optimizer. Filtering the
-- view by a variable-driven subquery instead let it expand the whole view (minutes, not ms).
create table #monthTxIds ([Transaction ID] nvarchar(100) not null primary key)
insert into #monthTxIds([Transaction ID])
select t.[Transaction ID]
from adn.Txs t
where
	substring(t.[Settlement Date Time], 8, 4) = convert(nvarchar(4), @settlementYear)
	and substring(t.[Settlement Date Time], 4, 3) = @sliceMonthAbbrev

select v.*
into #vTxsMonth
from adn.vTxs v
where v.[Transaction ID] in (select m.[Transaction ID] from #monthTxIds m)

declare @jobTypesPrimary table (
	Id int not null primary key,
	Name varchar(80) not null
)
insert into @jobTypesPrimary
select 
	jt.jobTypeId, 
	case jt.jobTypeId
		when 1 then 'Club'
		when 2 then 'Tournament'
		when 3 then 'League'
		when 4 then 'Camp'
		when 6 then 'Showcase'
		else jt.JobTypeDesc
	end
from
	reference.JobTypes jt

declare @txRawData table(
	id int identity(1,1) not null primary key,
	customerName varchar(80),
	customerId uniqueidentifier,
	jobName varchar(80),
	jobId uniqueidentifier,
	year int,
	month int,
	payment float,
	ProcessingFeePercent decimal(8,3)  -- added to carry rate through to while loop
)

insert into @txRawData(
	customerName,
	customerId,
	jobName,
	jobId,
	year,
	month,
	payment,
	ProcessingFeePercent
)
select 
	c.customerName,
	c.customerId,
	coalesce(j.jobName_QBP, j.jobName) as jobName,
	j.jobId,
	year(vtx.SettlementTS) as settlementYear,
	month(vtx.SettlementTS) as settlementMonth,
	sum(abs(vtx.[Settlement Amount])),
	case when vtx.[Transaction Type] = 'echeck' then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join adn.Monthly_Job_Stats mjs on r.jobID = mjs.jobId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join #vTxsMonth vtx on ra.adntransactionid = vtx.[transaction id]
where
	mjs.year = @settlementYear and mjs.month = @settlementMonth
	and (year(vtx.SettlementTS) = mjs.year and month(vtx.SettlementTS) = mjs.month)
	and apm.paymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
	and not vtx.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName_QBP,
	j.jobName,
	year(vtx.SettlementTS),
	month(vtx.SettlementTS),
	case when vtx.[Transaction Type] = 'echeck' then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end
order by
	j.jobName

declare @tStatementCharge table(
	id int not null identity(1,1) primary key,
	[!TRNS]  varchar(max),
	[TRNSID]  varchar(max),
	[TRNSTYPE]  varchar(max),
	[DATE]  varchar(max),
	[ACCNT]  varchar(max),
	[NAME]  varchar(max),
	[AMOUNT]  varchar(max),
	[DOCNUM]  varchar(max),
	[MEMO]  varchar(max),
	[CLEAR]  varchar(max),
	[TOPRINT]  varchar(max),
	[NAMEISTAXABLE]  varchar(max),
	[ADDR1] varchar(max)
)

--header top line
insert into @tStatementCharge values('!TRNS','TRNSID','TRNSTYPE','DATE','ACCNT','NAME','AMOUNT','DOCNUM','MEMO','CLEAR','TOPRINT','NAMEISTAXABLE','ADDR1')

--header middle line
insert into @tStatementCharge values('!SPL','SPLID','TRNSTYPE','DATE','ACCNT','NAME','AMOUNT','DOCNUM','MEMO','CLEAR','QNTY','PRICE','INVITEM')

--header bottom line
insert into @tStatementCharge values('!ENDTRNS','','','','','','','','','','','', '')

declare @rawTxId int 
select @rawTxId = min(id) from @txRawData

--select 'debug', * from @txRawData where jobName = 'All American Aim:Camps and Clinics 2024'
--select 'debug', convert(decimal(18,2), (payment * ProcessingFeePercent)), jobName from @txRawData where jobName = 'LI Yellow Jackets:Players 2024'

while not @rawTxId is null begin
	declare @ccDollars decimal(18,2)
	declare @jobRate decimal(8,3)

	select 
		@ccDollars = rawTxs.payment,
		@jobRate = rawTxs.ProcessingFeePercent
	from
		@txRawData rawTxs
	where
		rawTxs.id = @rawTxId

	--body top line
	insert into @tStatementCharge
	select 
		'TRNS',
		'',
		'STMT CHG',
		convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101),
		'Accounts Receivable',
		rawTxs.JobName,
		convert(decimal(18,2), (@ccDollars * @jobRate)),
		'',
		'',
		'N',
		'N',
		'N',
		''
	from
		@txRawData rawTxs
	where
		rawTxs.id = @rawTxId

	--body middle line
	insert into @tStatementCharge
	select 
		'SPL',
		'',
		'STMT CHG',
		convert(char(10), (select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101),
		'Services/Sales:Processing Fees',
		'',
		-convert(decimal(18,2), (@ccDollars * @jobRate)),
		'',
		'',
		'N',
		-convert(decimal(18,2), @ccDollars),
		-@jobRate,
		'Processing Fees'
	from
		@txRawData rawTxs
	where
		rawTxs.id = @rawTxId

	--body bottom line
	insert into @tStatementCharge values('ENDTRNS','','','','','','','','','','','', '')

	select @rawTxId = min(id) from @txRawData where id > @rawTxId
end

select 
	[!TRNS],
	[TRNSID],
	[TRNSTYPE],
	[DATE],
	[ACCNT],
	[NAME],
	[AMOUNT],
	[DOCNUM],
	[MEMO],
	[CLEAR],
	[TOPRINT],
	[NAMEISTAXABLE],
	[ADDR1]
from @tStatementCharge 
order by id
GO


ALTER PROCEDURE [adn].[ExportMonthlyJobRetainers]
(
    @settlementMonth int = 8,
    @settlementYear int = 2024
)
AS 
SET NOCOUNT ON;

-- PERF (2026-10-01): the month's slice of adn.vTxs, materialized once. Rows come from the view
-- itself (same columns, same row multiplicity); only the month's transaction IDs are admitted,
-- resolved from adn.Txs by the same substring parse the view uses (chars 8-11 = year, 4-6 = month
-- abbrev). The year()/month() predicates below are unchanged and still applied to this slice.
-- Joining the whole view instead walked all of adn.Txs through its 9 joins.
declare @sliceMonthAbbrev nvarchar(3) = choose(@settlementMonth,
    'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec');

-- IDs land in a keyed temp table FIRST: its row count is known to the optimizer. Filtering the
-- view by a variable-driven subquery instead let it expand the whole view (minutes, not ms).
create table #monthTxIds ([Transaction ID] nvarchar(100) not null primary key);
insert into #monthTxIds([Transaction ID])
select t.[Transaction ID]
from adn.Txs t
where
    substring(t.[Settlement Date Time], 8, 4) = convert(nvarchar(4), @settlementYear)
    and substring(t.[Settlement Date Time], 4, 3) = @sliceMonthAbbrev;

select v.*
into #vTxsMonth
from adn.vTxs v
where v.[Transaction ID] in (select m.[Transaction ID] from #monthTxIds m);

------------------------------------------------------------
-- RAW DATA TABLE
------------------------------------------------------------
declare @txRawData table(
    customerName varchar(80),
    customerId uniqueidentifier,
    jobName varchar(80),
    jobId uniqueidentifier not null,
    year int,
    month int,
    paymentMethod varchar(80),
    payment decimal(18,2)
);

------------------------------------------------------------
-- CC PROCESSING FEES (dynamic ProcessingFeePercent)
------------------------------------------------------------
insert into @txRawData(
    customerName,
    customerId,
    jobName,
    jobId,
    year,
    month,
    paymentMethod,
    payment
)
select 
    c.customerName,
    c.customerId,
    coalesce(j.jobName_QBP, j.jobName) as jobName,
    j.jobId,
    year(vtx.SettlementTS),
    month(vtx.SettlementTS),
    'CC Fees',
    convert(decimal(18,2),
        convert(decimal(18,4), sum(abs(vtx.[Settlement Amount])))
        * case when vtx.[Transaction Type] = 'echeck' then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end
    )
from
    Jobs.Registration_Accounting ra
    inner join reference.Accounting_PaymentMethods apm 
        on ra.paymentMethodID = apm.paymentMethodID
    inner join Jobs.Registrations r 
        on ra.RegistrationId = r.RegistrationId
    inner join adn.Monthly_Job_Stats mjs 
        on r.jobID = mjs.jobId
    inner join Jobs.Jobs j 
        on r.jobID = j.jobId
    inner join Jobs.Customers c 
        on j.customerId = c.customerId
    inner join #vTxsMonth vtx
        on ra.adntransactionid = vtx.[transaction id]
where
    mjs.year = @settlementYear
    and mjs.month = @settlementMonth
    and year(vtx.SettlementTS) = mjs.year
    and month(vtx.SettlementTS) = mjs.month
    and apm.paymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
    and vtx.[transaction status] not in ('Declined', 'Voided')
group by
    c.customerId,
    c.customerName,
    j.jobId,
    j.jobName_QBP,
    j.jobName,
    year(vtx.SettlementTS),
    month(vtx.SettlementTS),
    case when vtx.[Transaction Type] = 'echeck' then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end;

------------------------------------------------------------
-- ADMIN FEES
------------------------------------------------------------
insert into @txRawData(
    customerName,
    customerId,
    jobName,
    jobId,
    year,
    month,
    paymentMethod,
    payment
)
select 
    c.customerName,
    c.customerID,
    coalesce(j.jobName_QBP, j.jobName),
    jc.jobId,
    jc.year,
    jc.month,
    'Admin Fees',
    sum(jc.chargeAmount)
from
    adn.JobAdminCharges jc
    inner join Jobs.Jobs j 
        on jc.jobId = j.jobId
    inner join Jobs.Customers c 
        on j.customerID = c.customerID
where
    jc.year = @settlementYear
    and jc.month = @settlementMonth
    and jc.ChargeTypeId not in (1, 11)
group by
    c.customerName,
    c.customerID,
    j.jobName_QBP,
    j.jobName,
    jc.jobId,
    jc.year,
    jc.month;

------------------------------------------------------------
-- TSIC FEES
------------------------------------------------------------
insert into @txRawData(
    customerName,
    customerId,
    jobName,
    jobId,
    year,
    month,
    paymentMethod,
    payment
)
select 
    c.customerName,
    c.customerID,
    coalesce(j.jobName_QBP, j.jobName),
    j.jobId,
    mjs.year,
    mjs.month,
    'TSIC Fees',
    (mjs.Count_NewPlayers_ThisMonth * coalesce(j.perPlayerCharge, 0.00))
    + (mjs.Count_NewTeams_ThisMonth * coalesce(j.perTeamCharge, 0.00))
from
    adn.Monthly_Job_Stats mjs
    inner join Jobs.Jobs j 
        on mjs.jobID = j.jobId
    inner join Jobs.Customers c 
        on j.customerID = c.customerID
where
    mjs.year = @settlementYear
    and mjs.month = @settlementMonth
    and c.adnLoginId = 'teamspt52';

------------------------------------------------------------
-- BUILD RETAINERS TABLE
------------------------------------------------------------
declare @tJobRetainers table (
    id int identity(1,1) not null primary key,
    customerName varchar(80),
    jobName varchar(80),
    payment decimal(18,2)
);

insert into @tJobRetainers(customerName, jobName, payment)
select 
    rd.customerName,
    rd.jobName,
    sum(rd.payment)
from @txRawData rd
group by rd.customerName, rd.jobName;

------------------------------------------------------------
-- STATEMENT CHARGE TABLE (IIF EXPORT)
------------------------------------------------------------
declare @tStatementCharge table(
    id int identity(1,1) primary key,
    [!TRNS] varchar(max),
    [TRNSID] varchar(max),
    [TRNSTYPE] varchar(max),
    [DATE] varchar(max),
    [ACCNT] varchar(max),
    [NAME] varchar(max),
    [AMOUNT] varchar(max),
    [DOCNUM] varchar(max),
    [MEMO] varchar(max),
    [CLEAR] varchar(max),
    [TOPRINT] varchar(max),
    [NAMEISTAXABLE] varchar(max),
    [ADDR1] varchar(max)
);

-- headers
insert into @tStatementCharge values
('!TRNS','TRNSID','TRNSTYPE','DATE','ACCNT','NAME','AMOUNT','DOCNUM','MEMO','CLEAR','TOPRINT','NAMEISTAXABLE','ADDR1'),
('!SPL','SPLID','TRNSTYPE','DATE','ACCNT','NAME','AMOUNT','DOCNUM','MEMO','CLEAR','QNTY','PRICE','INVITEM'),
('!ENDTRNS','','','','','','','','','','','', '');

------------------------------------------------------------
-- LOOP THROUGH RETAINERS
------------------------------------------------------------
declare @retId int;
select @retId = min(id) from @tJobRetainers where payment != 0;

while @retId is not null
begin
    -- top line
    insert into @tStatementCharge
    select 
        'TRNS',
        '',
        'STMT CHG',
        convert(char(10), dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS(@settlementYear, @settlementMonth, 1, 0, 0, 0, 0))), 101),
        'Accounts Receivable',
        jr.JobName,
        -jr.payment,
        '',
        '',
        'N',
        'N',
        'N',
        ''
    from @tJobRetainers jr
    where jr.id = @retId;

    -- middle line
    insert into @tStatementCharge
    select 
        'SPL',
        '',
        'STMT CHG',
        convert(char(10), dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS(@settlementYear, @settlementMonth, 1, 0, 0, 0, 0))), 101),
        'Liability Due To Customers:' + jr.customerName,
        '',
        jr.payment,
        '',
        '',
        '',
        '',
        jr.payment,
        'Retainer:' + jr.customerName
    from @tJobRetainers jr
    where jr.id = @retId;

    -- bottom line
    insert into @tStatementCharge 
    values ('ENDTRNS','','','','','','','','','','','', '');

    select @retId = min(id) 
    from @tJobRetainers 
    where payment != 0 and id > @retId;
end;

------------------------------------------------------------
-- FINAL OUTPUT
------------------------------------------------------------
select 
    [!TRNS],
    [TRNSID],
    [TRNSTYPE],
    [DATE],
    [ACCNT],
    [NAME],
    [AMOUNT],
    [DOCNUM],
    [MEMO],
    [CLEAR],
    [TOPRINT],
    [NAMEISTAXABLE],
    [ADDR1]
from @tStatementCharge
order by id;
GO

-- =====================================================================
-- eCheck rate support for the month-end QuickBooks export sprocs
-- =====================================================================
-- Extracted from 8-update-processingfee-calc.sql: the four export sprocs
-- that bill/remit processing fees, now tender-aware:
--     eCheck  1.5%  (Jobs.Jobs.ECProcessingFeePercent)
--     CC      3.5%  (Jobs.Jobs.ProcessingFeePercent)
--
-- Sprocs (order-independent -- none call each other):
--   [adn].[ExportMonthlyCustomerChecks]     reg: customer check payout + fee
--   [adn].[ExportMonthlyJobRetainers]       reg: retainer billing
--   [adn].[ExportMonthlyProcessingFees]     reg: CC/eCheck processing-fee STMT CHG
--   [adn].[MonthyQBPExport_Automated_Merch] merch: monolithic export
--
-- PRECONDITIONS on the target DB (verify BEFORE running):
--   1. Jobs.Jobs.ECProcessingFeePercent column exists
--   2. adn.vtxs view exposes [Transaction Type]
--   3. adn.Txs.[Transaction Type] carries 'eCheck' for the month being exported
--      (requires the tender-capture import, commit 60fb295a, to have run;
--       otherwise eCheck rows fall to the 3.5% CC branch)
--   4. reference.Accounting_PaymentMethods has 'E-Check Payment' and
--      'Failed E-Check Payment'
--
-- CC-only months produce byte-identical output to the prior versions.
-- Does NOT include the /100 convention-sweep sprocs or any utility sproc.
-- =====================================================================

ALTER procedure [adn].[ExportMonthlyCustomerChecks]
(
	@settlementMonth int = 11,
	@settlementYear int = 2023
)
as set nocount on

-- @ccRate removed; now uses j.ProcessingFeePercent coalesced to 0.035 per job

-- PERF (2026-10-01): the month's slice of adn.vTxs, materialized once. Rows come from the view
-- itself (same columns, same row multiplicity); only the month's transaction IDs are admitted,
-- resolved from adn.Txs by the same substring parse the view uses (chars 8-11 = year, 4-6 = month
-- abbrev). The year()/month() predicates below are unchanged and still applied to this slice.
-- Joining the whole view instead walked all of adn.Txs through its 9 joins.
declare @sliceMonthAbbrev nvarchar(3) = choose(@settlementMonth,
	'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec')

-- IDs land in a keyed temp table FIRST: its row count is known to the optimizer. Filtering the
-- view by a variable-driven subquery instead let it expand the whole view (minutes, not ms).
create table #monthTxIds ([Transaction ID] nvarchar(100) not null primary key)
insert into #monthTxIds([Transaction ID])
select t.[Transaction ID]
from adn.Txs t
where
	substring(t.[Settlement Date Time], 8, 4) = convert(nvarchar(4), @settlementYear)
	and substring(t.[Settlement Date Time], 4, 3) = @sliceMonthAbbrev

select v.*
into #vTxsMonth
from adn.vTxs v
where v.[Transaction ID] in (select m.[Transaction ID] from #monthTxIds m)

declare @txRawData table(
	rdId int not null identity(1,1) primary key,
	customerGroupName varchar(max),
	customerName varchar(80),
	customerId uniqueidentifier,
	jobName varchar(80),
	jobId uniqueidentifier not null,
	year int,
	month int,
	paymentMethod varchar(80),
	payment float,
	ccDollarsReceived float default(0.00),
	processingFeePercent decimal(8,3) default(0.035)  -- added to carry rate through
)

--insert CC Processing Fees
insert into @txRawData(
	customerName,
	customerId,
	jobName,
	jobId,
	year,
	month,
	paymentMethod,
	payment,
	ccDollarsReceived,
	processingFeePercent
)
select 
	c.customerName,
	c.customerId,
	coalesce(j.jobName_QBP, j.jobName) as jobName,
	j.jobId,
	year(vtx.SettlementTS) as settlementYear,
	month(vtx.SettlementTS) as settlementMonth,
	'CC Fees',
	round(sum(abs(vtx.[Settlement Amount])) * case when vtx.[Transaction Type] = 'echeck' then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end, 2),
	sum(vtx.[Settlement Amount]),
	case when vtx.[Transaction Type] = 'echeck' then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join adn.Monthly_Job_Stats mjs on r.jobID = mjs.jobId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join #vTxsMonth vtx on ra.adntransactionid = vtx.[transaction id]
where
	mjs.year = @settlementYear and mjs.month = @settlementMonth
	and (year(vtx.SettlementTS) = mjs.year and month(vtx.SettlementTS) = mjs.month)
	and apm.paymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
	and not vtx.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName_QBP,
	j.jobName,
	year(vtx.SettlementTS),
	month(vtx.SettlementTS),
	case when vtx.[Transaction Type] = 'echeck' then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end
order by
	j.jobName

-- insert Admin Charges
insert into @txRawData(
	customerName,
	customerId,
	jobName,
	jobId,
	year,
	month,
	paymentMethod,
	payment
)
select distinct 
	c.customerName,
	c.customerID,
	coalesce(j.jobName_QBP, j.jobName) as jobName,
	jc.jobId,
	jc.year,
	jc.month,
	'Admin Fees',
	sum(jc.chargeAmount)
from
	adn.JobAdminCharges jc
	inner join Jobs.Jobs j on jc.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
where
	jc.year = @settlementYear and jc.month = @settlementMonth
group by
	c.customerName,
	c.customerID,
	j.jobName_QBP,
	j.jobName,
	jc.jobId,
	jc.year,
	jc.month

-- insert TSIC Fees
insert into @txRawData(
	customerName,
	customerId,
	jobName,
	jobId,
	year,
	month,
	paymentMethod,
	payment
)
select distinct 
	c.customerName,
	c.customerID,
	coalesce(j.jobName_QBP, j.jobName) as jobName,
	j.jobId,
	mjs.year,
	mjs.month,
	'TSIC Fees',
	(mjs.Count_NewPlayers_ThisMonth * coalesce(j.perPlayerCharge, 0.00)) + (mjs.Count_NewTeams_ThisMonth * coalesce(j.perTeamCharge, 0.00))
from
	adn.Monthly_Job_Stats mjs
	inner join Jobs.Jobs j on mjs.jobID = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
where
	mjs.year = @settlementYear
	and mjs.month = @settlementMonth
	and c.adnLoginId = 'teamspt52'

update rd
	set customerGroupName = coalesce(cg.CustomerGroupName, rd.customerName)
from
	@txRawData rd
	left join Jobs.CustomerGroupCustomers cgc on rd.customerId = cgc.CustomerId
	left join Jobs.CustomerGroups cg on cgc.CustomerGroupId = cg.Id

declare @tCustomerGroups table (
	cgId int not null identity(1,1) primary key,
	customerGroupName varchar(max),
	sumOfRetainers decimal(18,2),
	sumOfCCDollarsReceived decimal(18,2)
)

declare @tCheckData table(
	id int not null identity(1,1) primary key,
	customerGroupName varchar(max),
	customerName varchar(80),
	jobName varchar(80),
	dollarsRetained decimal(18,2),
	ccDollarsReceived decimal(18,2) default(0.00)
)

insert into @tCheckData(customerGroupName, customerName, jobName, dollarsRetained, ccDollarsReceived)
select
	rd.customerGroupName,
	rd.customerName,
	rd.jobName,
	sum(rd.payment),
	sum(rd.ccDollarsReceived)
from
	@txRawData rd
group by
	rd.customerGroupName,
	rd.customerName,
	rd.jobName

--select 'debug', * from @tCheckData where jobName = 'LI Yellow Jackets:Players 2024'

insert into @tCustomerGroups(customerGroupName, sumOfRetainers, sumOfCCDollarsReceived)
select
	rd.customerGroupName,
	sum(rd.dollarsRetained),
	sum(rd.ccDollarsReceived)
from	
	@tCheckData rd
group by
	rd.customerGroupName
order by
	rd.customerGroupName

--select 'debug', * from @tCustomerGroups
	
declare @tStatementCharge table(
	id int not null identity(1,1) primary key,
	[!VEND] varchar(max),
	[NAME] varchar(max),
	REFNUM varchar(max),
	[TIMESTAMP] varchar(max),
	PRINTAS varchar(max),
	ADDR1 varchar(max),
	ADDR2 varchar(max),
	ADDR3 varchar(max),
	ADDR4 varchar(max),
	ADDR5 varchar(max),
	VTYPE varchar(max),
	CONT1 varchar(max),
	CONT2 varchar(max),
	PHONE1 varchar(max),
	PHONE2 varchar(max),
	FAXNUM varchar(max),
	EMAIL varchar(max),
	NOTE varchar(max),
	TAXID varchar(max),
	LIMIT varchar(max),
	TERMS varchar(max),
	NOTEPAD varchar(max),
	SALUTATION varchar(max),
	COMPANYNAME varchar(max),
	FIRSTNAME varchar(max),
	MIDINIT varchar(max),
	LASTNAME varchar(max),
	CUSTFLD1 varchar(max),
	CUSTFLD2 varchar(max),
	CUSTFLD3 varchar(max),
	CUSTFLD4 varchar(max),
	CUSTFLD5 varchar(max),
	CUSTFLD6 varchar(max),
	CUSTFLD7 varchar(max),
	CUSTFLD8 varchar(max),
	CUSTFLD9 varchar(max),
	CUSTFLD10 varchar(max),
	CUSTFLD11 varchar(max),
	CUSTFLD12 varchar(max),
	CUSTFLD13 varchar(max),
	CUSTFLD14 varchar(max),
	CUSTFLD15 varchar(max),
	[1099] varchar(max),
	[HIDDEN] varchar(max),
	DELCOUNT varchar(max)
)

declare @cgId int
select @cgId = min(cgId) from @tCustomerGroups

while not @cgId is null begin
	declare @customerGroupName varchar(max)
	declare @sumOfRetainers float
	declare @sumOfCCDollarsReceived float
	select
		@customerGroupName = customerGroupName,
		@sumOfRetainers = sumOfRetainers,
		@sumOfCCDollarsReceived = sumOfCCDollarsReceived
	from @tCustomerGroups 
	where cgId = @cgId

--start header lines
----DO NOT INSERT VENDOR, THEY ALREADY EXIST
--insert into @tStatementCharge values('!VEND','NAME','REFNUM','TIMESTAMP','PRINTAS','ADDR1','ADDR2','ADDR3','ADDR4','ADDR5','VTYPE','CONT1','CONT2','PHONE1','PHONE2','FAXNUM','EMAIL','NOTE','TAXID','LIMIT','TERMS','NOTEPAD','SALUTATION','COMPANYNAME','FIRSTNAME','MIDINIT','LASTNAME','CUSTFLD1','CUSTFLD2','CUSTFLD3','CUSTFLD4','CUSTFLD5','CUSTFLD6','CUSTFLD7','CUSTFLD8','CUSTFLD9','CUSTFLD10','CUSTFLD11','CUSTFLD12','CUSTFLD13','CUSTFLD14','CUSTFLD15','1099','HIDDEN','DELCOUNT')
--insert into @tStatementCharge values('VEND',@customerGroupName,'','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','N','N','0')

insert into @tStatementCharge values('!TRNS','TRNSID','TRNSTYPE','DATE','ACCNT','NAME','CLASS','AMOUNT','DOCNUM','CLEAR','TOPRINT','MEMO','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','')
insert into @tStatementCharge values('!SPL','SPLID','TRNSTYPE','DATE','ACCNT','NAME','CLASS','AMOUNT','DOCNUM','CLEAR','QNTY','REIMBEXP','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','')
insert into @tStatementCharge values('!ENDTRNS','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','')
--end header lines

	--body top line
	insert into @tStatementCharge
	select 
		'TRNS',
		'',
		'CHECK',
		convert(char(10),(select dateadd(day, 1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101),
		'Checking',
		@customerGroupName,
		'',
		-convert(decimal(18,2), (@sumOfCCDollarsReceived - @sumOfRetainers)),
		'',
		'N',
		'N',
		'EFT Balance Due ' + customerGroupName,
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		'',
		''		
	from @tCustomerGroups 
	where cgId = @cgId


	declare @rdId int
	select @rdId = min(rd.id) from @tCheckData rd where rd.customerGroupName = @customerGroupName 

	while not @rdId is null begin

		--select 'debug', rd.jobName, rd.ccDollarsReceived, rd.dollarsRetained
		--from @tCheckData rd where rd.customerGroupName = @customerGroupName and rd.id = @rdId

		insert into @tStatementCharge
		select 
			'SPL',
			'',
			'CHECK',
			convert(char(10),(select dateadd(day, 1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101),
			'Liability Due To Customers:' + rd.customerName,
			rd.jobName,
			'',
			convert(decimal(18,2), (rd.ccDollarsReceived -  rd.dollarsRetained)),
			'',
			'',
			'N',
			'',
			'',
			'',
			'',
			'N',
			'N',
			'NOTHING',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			'',
			''		
		from @tCheckData rd 
		where 
			rd.customerGroupName = @customerGroupName 
			and rd.id = @rdId

		select @rdId = min(id) from @tCheckData where customerGroupName = @customerGroupName and id > @rdId
	end

	--body bottom line
	insert into @tStatementCharge values('ENDTRNS','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','','')

	select @cgId = min(cgId) from @tCustomerGroups where cgId > @cgId
end

select 
	[!VEND],
	[NAME],
	REFNUM,
	[TIMESTAMP],
	PRINTAS,
	ADDR1,
	ADDR2,
	ADDR3,
	ADDR4,
	ADDR5,
	VTYPE,
	CONT1,
	CONT2,
	PHONE1,
	PHONE2,
	FAXNUM,
	EMAIL,
	NOTE,
	TAXID,
	LIMIT,
	TERMS,
	NOTEPAD,
	SALUTATION,
	COMPANYNAME,
	FIRSTNAME,
	MIDINIT,
	LASTNAME,
	CUSTFLD1,
	CUSTFLD2,
	CUSTFLD3,
	CUSTFLD4,
	CUSTFLD5,
	CUSTFLD6,
	CUSTFLD7,
	CUSTFLD8,
	CUSTFLD9,
	CUSTFLD10,
	CUSTFLD11,
	CUSTFLD12,
	CUSTFLD13,
	CUSTFLD14,
	CUSTFLD15,
	[1099],
	[HIDDEN],
	DELCOUNT
from @tStatementCharge 
order by id
GO


ALTER procedure [adn].[MonthyQBPExport_Automated]
(
		@settlementMonth int = 11
	,	@settlementYear int = 2023
)
as
set nocount on

declare @monthYear varchar(20) = convert(varchar, @settlementMonth) + '-' + convert(varchar, @settlementYear)
declare @firstDay date = convert(varchar, @settlementMonth) + '/' + '01/' + convert(varchar, @settlementYear)
declare @qaTest varchar(max)

IF OBJECT_ID(N'tempdb..#txRawData') IS NOT NULL
BEGIN
	DROP TABLE #txRawData
END

-- PERF (2026-10-01): the month's settled/credited non-merch rows, filtered on adn.Txs alone FIRST
-- (predicates verbatim from the original WHERE, all on adn.Txs), so the per-row fnSplit job
-- lookup below runs on this month's rows only instead of across the whole table.
select [Txs].*
into #monthTxsRaw
from
	[adn].[txs] as [Txs]
where
	[Txs].[Settlement Date Time] like ('%' + left(datename(month, @firstDay), 3) + '-' + convert(varchar, @settlementYear) + '%')
	and [transaction status] in ('Settled Successfully', 'Credited')
	and charindex('_M', [Txs].[Invoice Number]) = 0  --OMIT MERCH PURCHASES

select *
into #txRawData
from
(
	select 
		txs.[Transaction Status] as [Transaction Status],
		convert(datetime, replace([Txs].[Settlement Date Time], ' EDT', '')) as [SettlementTS],
		c.customerName,
		j.jobId,
		coalesce(j.jobName_QBP, j.jobName) as jobName,
		case when txs.[Transaction Status] = 'Settled Successfully' then convert(money, [Txs].[Settlement Amount]) else 0 end as Plus,
		case when txs.[Transaction Status] =  'Credited' then -convert(money, [Txs].[Settlement Amount]) else 0 end as Minus,
		txs.[Transaction ID],
		txs.[Invoice Number],
		txs.[Invoice Description]
	from
		#monthTxsRaw as [Txs]
		inner join Jobs.Jobs j on (SELECT item FROM [dbo].[fnSplit]([Txs].[Invoice Number], '_') where ai = 2) = j.jobAI
		inner join Jobs.Customers c on j.customerID = c.customerID
) v1

drop table #monthTxsRaw


--CLIENTS LISTING

select @qaTest = @monthYear + ' Clients'
select 'QA Test: ' + @qaTest

select
		[Txs].[CustomerName] + ':' + [Txs].[JobName] as [Customer:Job]
	,	count(*) as [Count of ADN Txs]
from
	#txRawData as [Txs]
group by
	[Txs].[CustomerName]
	,	[Txs].[JobName]
order by
		[Txs].[CustomerName]
	,	[Txs].[JobName]

--ADN TXS BY DATE
select @qaTest = @monthYear + ' Dailys'
select 'QA Test: ' + @qaTest

select
		convert(char(10), [Txs].[SettlementTS], 101) as [Settlement Day]
	,	sum([Txs].Plus) as [Positive]
	,	sum([Txs].Minus) as Negative
	,	sum([Txs].Plus) + sum([Txs].Minus) as [DailyTotal]
from
	[#txRawData] as [Txs]
group by
		convert(char(10), [Txs].[SettlementTS], 101)
order by
		convert(char(10), [Txs].[SettlementTS], 101)

--ADN TXS BY DATE AND VENUE
select @qaTest = @monthYear + ' Job-Dailys'
select 'QA Test: ' + @qaTest

select 
	v1.[Settlement Day],
	v1.jobName as [Customer:Job],
	v1.Positive,
	v1.Negative,
	v1.DailyTotal,
	sum(v1.DailyTotal) over (partition by v1.jobName order by v1.[Settlement Day]) as Cumulative
from
(
	select
			[Txs].JobName
		,	convert(char(10), [Txs].[SettlementTS], 101) as [Settlement Day]
		,	sum([Txs].Plus) as [Positive]
		,	sum([Txs].Minus) as Negative
		,	sum([Txs].Plus) + sum([Txs].Minus) as [DailyTotal]
	from
		[#txRawData] as [Txs]
	group by
		[Txs].jobName,
		convert(char(10), [Txs].[SettlementTS], 101)

) v1
order by
	v1.jobName,
	v1.[Settlement Day]

/* iif data for payments */
select @qaTest = @monthYear + ' IIF-Payments'
select 'QA Test: ' + @qaTest

select
		[!TRNS]
	,	[TRNSID]
	,	[TRNSTYPE]
	,	[DATE]
	,	[ACCNT]
	,	[NAME]
	,	[CLASS]
	,	[AMOUNT]
	,	[DOCNUM]
	,	[MEMO]
	,	[CLEAR]
from
(
	select '' as [Settlement Day], '' as [Customer:Job], '!TRNS' as [!TRNS], 'TRNSID' as [TRNSID], 'TRNSTYPE' as [TRNSTYPE], 'DATE' as [DATE], 'ACCNT' as [ACCNT], 'NAME' as [NAME], 'CLASS' as [CLASS], 'AMOUNT' as [AMOUNT], 'DOCNUM' as [DOCNUM], 'MEMO' as [MEMO], 'CLEAR' as [CLEAR]
	union all 
	select '' as [Settlement Day], '' as [Customer:Job], '!SPL' as [!TRNS], 'SPLID' as [TRNSID], 'TRNSTYPE' as [TRNSTYPE], 'DATE' as [DATE], 'ACCNT' as [ACCNT], 'NAME' as [NAME], 'CLASS' as [CLASS], 'AMOUNT' as [AMOUNT], 'DOCNUM' as [DOCNUM], 'MEMO' as [MEMO], 'CLEAR' as [CLEAR]
	union all select '' as [Settlement Day], '' as [Customer:Job], '!ENDTRNS' as [!TRNS], '' as [TRNSID], '' as [TRNSTYPE], '' as [DATE], '' as [ACCNT], '' as [NAME], '' as [CLASS], '' as [AMOUNT], '' as [DOCNUM], '' as [MEMO], '' as [CLEAR]
	union all
	select
			[Settlement Day]
		,	[Customer:Job]
		,	[!TRNS]
		,	[TRNSID]
		,	[TRNSTYPE]
		,	[DATE]
		,	[ACCNT]
		,	[NAME]
		,	[CLASS]
		,	[AMOUNT]
		,	[DOCNUM]
		,	[MEMO]
		,	[CLEAR]
	from
	(
		select 
				convert(char(10), [Txs].[SettlementTS], 101) as [Settlement Day]
			,   txs.jobName as [Customer:Job]
			,	'TRNS' as [!TRNS]
			,	'' as [TRNSID]
			,	case when sum([Txs].Plus) > 0 then 'DEPOSIT' else 'CHECK' end as [TRNSTYPE]
			,	convert(char(10), [Txs].[SettlementTS], 101) as [DATE]
			,	'Checking' as [ACCNT]
			,   txs.jobName as [NAME]
			,	'' as [CLASS]
			,	convert(varchar, (sum([Txs].Plus))) as [AMOUNT]
			,	convert(varchar, max(ra.[aID])) as [DOCNUM]
			--,	max(Txs.[Transaction ID]) as [DOCNUM]
			,	'' as [MEMO]
			,	'N' as [CLEAR]
		from
			#txRawData txs
			inner join Jobs.Registration_Accounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.[Transaction Status] = 'Settled Successfully'
		group by
			txs.[Transaction Status],
			convert(char(10), [Txs].[SettlementTS], 101),
			txs.customerName,
			txs.jobName

		union all

		select
				convert(char(10), [Txs].[SettlementTS], 101) as [Settlement Day]
			,   txs.jobName as [Customer:Job]
			,	'SPL' as [!TRNS]
			,	'' as [TRNSID]
			,	case when sum([Txs].Plus) > 0 then 'DEPOSIT' else 'CHECK' end as [TRNSTYPE]
			,	convert(char(10), [Txs].[SettlementTS], 101) as [DATE]
			,	'Liability Due to Customers:' + 
					case 
						when charindex([Txs].[CustomerName], txs.jobName) > 0 then 
							(SELECT item FROM [dbo].[fnSplit] (txs.jobName,':') where ai = 1)
						else  [Txs].[CustomerName] 
					end as [ACCNT]
			,   txs.jobName as [NAME]
			,	'' as [CLASS]
			,	convert(varchar, -(sum([Txs].Plus))) as [AMOUNT]
			,	convert(varchar, max(ra.[aID])) as [DOCNUM]
			--,	max(Txs.[Transaction ID]) as [DOCNUM]
			,	'' as [MEMO]
			,	'N' as [CLEAR]
		from
			[#txRawData] as [Txs]
			inner join Jobs.Registration_Accounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.[Transaction Status] = 'Settled Successfully'
		group by
				txs.[Transaction Status]
			,	convert(char(10), [Txs].[SettlementTS], 101)
			,	txs.customerName
			,	txs.jobName

		union all 
		
		select 
				convert(char(10), [Txs].[SettlementTS], 101) as [Settlement Day]
			,   txs.jobName as [Customer:Job]
			,	'ENDTRNS' as [!TRNS]
			, '' as [TRNSID]
			, '' as [TRNSTYPE]
			, '' as [DATE]
			, '' as [ACCNT]
			, '' as [AMOUNT]
			, '' as [NAME]
			, '' as [CLASS]
			, '' as [DOCNUM]
			, '' as [MEMO]
			, '' as [CLEAR]
		from
			[#txRawData] as [Txs]
		where
			txs.[Transaction Status] = 'Settled Successfully'
		group by
				txs.[Transaction Status]
			,	convert(char(10), [Txs].[SettlementTS], 101)
			,	txs.customerName
			,	txs.jobName
	) v2
) v1
order by 
		[Settlement Day]
	,	[Customer:Job]
	,	case [!TRNS]
			when '!TRNS' then 1
			when '!SPL' then 2
			when '!ENDTRNS' then 3
			when 'TRNS' then 4
			when 'SPL' then 5
			when 'ENDTRNS' then 6
		end

/* iif data for payments */
select @qaTest = @monthYear + ' IIF-Credits'
select 'QA Test: ' + @qaTest

select
		[!TRNS]
	,	[TRNSID]
	,	[TRNSTYPE]
	,	[DATE]
	,	[ACCNT]
	,	[NAME]
	,	[CLASS]
	,	[AMOUNT]
	,	[DOCNUM]
	,	[CLEAR]
	,	[TOPRINT]
from
(
	select '' as [Settlement Day], '' as [Customer:Job], '!TRNS' as [!TRNS], 'TRNSID' as [TRNSID], 'TRNSTYPE' as [TRNSTYPE], 'DATE' as [DATE], 'ACCNT' as [ACCNT], 'NAME' as [NAME], 'CLASS' as [CLASS], 'AMOUNT' as [AMOUNT], 'DOCNUM' as [DOCNUM], 'CLEAR' as [CLEAR], 'TOPRINT' as [TOPRINT]
	union all 
	select '' as [Settlement Day], '' as [Customer:Job], '!SPL' as [!TRNS], 'SPLID' as [TRNSID], 'TRNSTYPE' as [TRNSTYPE], 'DATE' as [DATE], 'ACCNT' as [ACCNT], 'NAME' as [NAME], 'CLASS' as [CLASS], 'AMOUNT' as [AMOUNT], 'DOCNUM' as [DOCNUM], 'CLEAR' as [CLEAR], '' as [TOPRINT]
	union all select '' as [Settlement Day], '' as [Customer:Job], '!ENDTRNS' as [!TRNS], '' as [TRNSID], '' as [TRNSTYPE], '' as [DATE], '' as [ACCNT], '' as [NAME], '' as [CLASS], '' as [AMOUNT], '' as [DOCNUM], '' as [CLEAR], '' as [TOPRINT]
	union all
	select
			[Settlement Day]
		,	[Customer:Job]
		,	[!TRNS]
		,	[TRNSID]
		,	[TRNSTYPE]
		,	[DATE]
		,	[ACCNT]
		,	[NAME]
		,	[CLASS]
		,	[AMOUNT]
		,	[DOCNUM]
		,	[CLEAR]
		,	[TOPRINT]
	from
	(
		select
				convert(char(10), [Txs].[SettlementTS], 101) as [Settlement Day]
			,   txs.jobName as [Customer:Job]
			,	'TRNS' as [!TRNS]
			,	'' as [TRNSID]
			,	case when sum([Txs].Minus) > 0 then 'DEPOSIT' else 'CHECK' end as [TRNSTYPE]
			,	convert(char(10), [Txs].[SettlementTS], 101) as [DATE]
			,	'Checking' as [ACCNT]
			,   txs.jobName as [NAME]
			,	'' as [CLASS]
			,	convert(varchar, (sum([Txs].Minus))) as [AMOUNT]
			,	convert(varchar, max(ra.[aID])) as [DOCNUM]
			--,	max(Txs.[Transaction ID]) as [DOCNUM]
			,	'N' as [CLEAR]
			,	'N' as [TOPRINT]
		from
			[#txRawData] as [Txs]
			inner join Jobs.Registration_Accounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.[Transaction Status] = 'Credited'
		group by
				txs.[Transaction Status]
			,	convert(char(10), [Txs].[SettlementTS], 101)
			,	Txs.customerName
			,	txs.jobName

		union all

		select
				convert(char(10), [Txs].[SettlementTS], 101) as [Settlement Day]
			,   txs.jobName as [Customer:Job]
			,	'SPL' as [!TRNS]
			,	'' as [TRNSID]
			,	case when sum([Txs].Minus) > 0 then 'DEPOSIT' else 'CHECK' end as [TRNSTYPE]
			,	convert(char(10), [Txs].[SettlementTS], 101) as [DATE]
			,	'Liability Due to Customers:' + 
					case 
						when charindex([Txs].[CustomerName], txs.jobName) > 0 then 
							(SELECT item FROM [dbo].[fnSplit] (txs.jobName,':') where ai = 1)
						else  [Txs].[CustomerName] 
					end as [ACCNT]
			,   txs.jobName as [NAME]
			,	'' as [CLASS]
			,	convert(varchar, -(sum([Txs].Minus))) as [AMOUNT]
			,	convert(varchar, max(ra.[aID])) as [DOCNUM]
			--,	max(Txs.[Transaction ID]) as [DOCNUM]
			,	'N' as [CLEAR]
			,	'N' as [TOPRINT]
		from
			[#txRawData] as [Txs]
			inner join Jobs.Registration_Accounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.[Transaction Status] = 'Credited'
		group by
				txs.[Transaction Status]
			,	convert(char(10), [Txs].[SettlementTS], 101)
			,	Txs.customerName
			,	txs.jobName

		union all 
		
		select 
				convert(char(10), [Txs].[SettlementTS], 101) as [Settlement Day]
			,   txs.jobName as [Customer:Job]
			,	'ENDTRNS' as [!TRNS]
			, '' as [TRNSID]
			, '' as [TRNSTYPE]
			, '' as [DATE]
			, '' as [ACCNT]
			, '' as [AMOUNT]
			, '' as [NAME]
			, '' as [CLASS]
			, '' as [DOCNUM]
			, '' as [CLEAR]
			, '' as [TOPRINT]
		from
			[#txRawData] as [Txs]
		where
			txs.[Transaction Status] = 'Credited'
		group by
				txs.[Transaction Status]
			,	convert(char(10), [Txs].[SettlementTS], 101)
			,	Txs.customerName
			,	txs.jobName

	) v2
) v1
order by 
		[Settlement Day]
	,	[Customer:Job]
	,	case [!TRNS]
			when '!TRNS' then 1
			when '!SPL' then 2
			when '!ENDTRNS' then 3
			when 'TRNS' then 4
			when 'SPL' then 5
			when 'ENDTRNS' then 6
		end

--drop tmp table
drop table #txRawData

--now update the job_monthly_stats
--THIS WILL ONLY INSERT NEW ROWS IF NEEDED -- WILL NOT OVERWRITE ROWS ALREADY WRITTEN
exec adn.UpdateMonthlyJobStats_CalcFields @settlementYear, @settlementMonth

--begin production of remaining iif files

	--[adn].[ExportMonthlyTSICFees]
	select @qaTest = @monthYear + ' TSIC-Fees'
	select 'QA Test: ' + @qaTest
	exec [adn].[ExportMonthlyTSICFees] @settlementMonth, @settlementYear

	--[adn].[ExportMonthlyProcessingFees]
	select @qaTest = @monthYear + ' CC-Fees'
	select 'QA Test: ' + @qaTest
	exec [adn].ExportMonthlyProcessingFees @settlementMonth, @settlementYear

	--[adn].[ExportMonthlyJobAdminFees]
	select @qaTest = @monthYear + ' Admin-Fees'
	select 'QA Test: ' + @qaTest
	exec [adn].ExportMonthlyJobAdminFees @settlementMonth, @settlementYear

	--[adn].[ExportMonthlyJobRetainers]
	select @qaTest = @monthYear + ' Retainers'
	select 'QA Test: ' + @qaTest
	exec [adn].ExportMonthlyJobRetainers @settlementMonth, @settlementYear

	--[adn].[ExportMonthlyJobRetainers]
	select @qaTest = @monthYear + ' Checks'
	select 'QA Test: ' + @qaTest
	exec [adn].[ExportMonthlyCustomerChecks] @settlementMonth, @settlementYear

--end production of remaining iif files
GO

