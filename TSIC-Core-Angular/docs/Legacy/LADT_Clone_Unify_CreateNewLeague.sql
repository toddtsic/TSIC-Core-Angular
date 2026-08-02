/*

*/
CREATE procedure [utility].[LADT_Clone_Unify_CreateNewLeague]
(
		@leagueIDOld uniqueidentifier = 'f8f3dd69-a2b6-df11-9d30-00137250256d'
	,	@leagueName_Target varchar(80) = 'asdf'
	,	@jobID_Source uniqueidentifier = '484f37aa-3bcd-40f9-858c-a204130d1569'
	,	@jobID_Target uniqueidentifier = 'C2D85957-2604-409B-B565-133176413E4F'

	,	@jobSeason_Source varchar(20) = 'summer'
	,	@jobSeason_Target varchar(20) = 'summer'
	,	@TeamsToClone int = 1
	,	@CloneAgegroupsAndDivisions bit
)
as set nocount off

declare @leagueId_Source uniqueidentifier
declare @leagueId_Target uniqueidentifier = NEWID()
declare @superuserId nvarchar(254) = (select id from dbo.AspNetUsers where UserName = 'TSICSuperuser')

select top 1 @leagueId_Source = jl.leagueId 
from
	Jobs.Job_Leagues jl
where
	jl.JobId = @jobID_Source
order by
	jl.modified desc

declare @leagueName_Source varchar(80)
select @leagueName_Source = leagueName
from
	Leagues.leagues
where
	leagueId = @leagueId_Source

select @leagueId_Source as LeagueId_Source

/*****START*****clone LEAGUE*****/
if (ltrim(rtrim(@leagueName_Target)) != ltrim(rtrim(@leagueName_Source))) begin

	declare @columnsLessIdentity nvarchar(max)
	declare @sql nvarchar(max)

	select @columnsLessIdentity = (
		select name + ', ' as [text()] 
		from sys.columns 
		where object_id = object_id('Leagues.Leagues') 
		for xml path('')
	)

	--remove trailing ','
	select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))

	set @sql = N'' +
		'insert into Leagues.Leagues(' + @columnsLessIdentity + ') '
		+ 'select ' 
		+		replace(@columnsLessIdentity, 'leagueId, ', '''' + convert(varchar(max), @leagueId_Target) + ''', ') + ' '  
		+ 'from Leagues.Leagues '
		+ 'where leagueId=''' + cast(@leagueId_Source as varchar(max)) + ''' '
		+ 'and not exists(select * from Leagues.Leagues where leagueId = ''' + convert(varchar(max), @leagueId_Target) + ''')'

	exec sp_executesql @sql

	update l set
		leagueName = @leagueName_Target,
		modified = getdate(),
		lebUserID = @superuserId
	from 
		Leagues.leagues l 
	where 
		l.LeagueId = @leagueId_Target


	insert into Jobs.Job_Leagues(
		[leagueID], 
		[jobID], 
		[lebUserID], 
		[modified]
	)
	select 
		@leagueId_Target, 
		@jobID_Target, 
		@superuserId, 
		getdate()
end

/*****END*****clone LEAGUE*****/

/*****START*****clone AGEGROUPS*****/
if (@CloneAgegroupsAndDivisions = 1) begin
	IF EXISTS(SELECT [name] FROM tempdb.sys.tables WHERE [name] = '#Ags') 
	BEGIN
	   DROP TABLE #Ags ;
	END;

	select NEWID() as AgegroupID_Target, ag.*
	into #Ags
	from Leagues.Agegroups ag
	where 
		ag.leagueId = @leagueId_Source 
		and ag.season = @jobSeason_Source
		and charindex('WAITLIST', ag.agegroupName) = 0

	select @columnsLessIdentity = (
		select name + ', ' as [text()] 
		from sys.columns 
		where object_id = object_id('Leagues.Agegroups') 
		for xml path('')
	)

	--remove trailing ','
	select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))

	set @sql = N'' +
		'insert into Leagues.Agegroups(' + @columnsLessIdentity + ') '
		+ 'select ' 
		+		replace(replace(replace(@columnsLessIdentity, 'agegroupId, ', 'AgegroupId_Target, '), 'season, ', '''' + @jobSeason_Target + ''', '), 'leagueId, ', '''' + convert(varchar(max), @leagueId_Target) + ''', ') + ' '  
		+ 'from #Ags '
		+ 'where not exists(select * from Leagues.Agegroups where leagueId = ''' + convert(varchar(max), @leagueId_Target) + ''')'

	select '@jobSeason_Target', @jobSeason_Target
	select '@leagueId_Target', @leagueId_Target
	select 'debug @sql', @sql
	exec sp_executesql @sql

	update ag set
		modified = getdate(),
		lebUserID = @superuserId
	from 
		Leagues.agegroups ag 
	where 
		ag.LeagueId = @leagueId_Target
		and ag.season = @jobSeason_Target
end
/*****END*****clone AGEGROUPS*****/

/*****START*****clone DIVISIONS*****/
if (@CloneAgegroupsAndDivisions = 1) begin
	IF EXISTS(SELECT [name] FROM tempdb.sys.tables WHERE [name] = '#Divs') 
	BEGIN
	   DROP TABLE #Divs ;
	END;

	select NEWID() as DivID_Target, d.*
	into #Divs
	from 
		Leagues.Divisions d
		inner join Leagues.agegroups ag on d.agegroupID = ag.agegroupID
	where 
		ag.leagueId = @leagueId_Source 
		and ag.season = @jobSeason_Source
		and charindex('WAITLIST', ag.agegroupName) = 0


	select @columnsLessIdentity = (
		select name + ', ' as [text()] 
		from sys.columns 
		where object_id = object_id('Leagues.Divisions') 
		for xml path('')
	)

	--remove trailing ','
	select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))

	set @sql = N'' +
		'insert into Leagues.Divisions(' + @columnsLessIdentity + ') '
		+ 'select ' 
		+		replace(replace(@columnsLessIdentity, 'divId, ', 'DivID_Target, '), 'agegroupId, ', '' + '(select AgegroupId_Target from [#Ags] where agegroupId = d.agegroupId)' + ', ') + ' '  
		+ 'from #Divs d '

	select @sql
	exec sp_executesql @sql

	update d set
		modified = getdate(),
		lebUserID = @superuserId
	from 
		Leagues.agegroups ag 
		inner join Leagues.divisions d on ag.agegroupId = d.agegroupId
	where 
		ag.leagueId = @leagueId_Target
		and ag.season = @jobSeason_Target
end
/*****END*****clone DIVISIONS*****/

/*****START*****clone TEAMS*****/
if (@CloneAgegroupsAndDivisions = 1) begin
	IF EXISTS(SELECT [name] FROM tempdb.sys.tables WHERE [name] = '#Teams') 
	BEGIN
		DROP TABLE #Teams ;
	END;

	select NEWID() as TeamId_Target, t.*
	into #Teams
	from 
		Leagues.teams t
		inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
	where 
		t.jobID = @jobID_Source
		and t.active = 1
		and charindex('waitlist', ag.agegroupName) = 0
		and charindex('dropped', ag.agegroupName) = 0

	update t
		set fee_base = 0
	from
		#Teams t

	/*
	0 = no teams
	1 = registration teams
	anything else = all teams
	*/

	if @TeamsToClone = 0 begin
			delete from #Teams
			delete from #Ags 
	end else if @TeamsToClone = 1 begin
		declare @agegroupID_Registration_Source uniqueidentifier 
		select top 1 @agegroupID_Registration_Source = ag.agegroupId
		from
			Leagues.agegroups ag
		where
			ag.leagueID = @leagueId_Source
			and ag.season = @jobSeason_Source
			and ag.agegroupName = 'Registration'

		if not @agegroupID_Registration_Source  is null begin
			delete from #Teams
			where agegroupID != @agegroupID_Registration_Source

			delete from #Ags 
			where agegroupID != @agegroupID_Registration_Source
		end
	end 

	if @TeamsToClone > 0 begin
		select @columnsLessIdentity = (
			select name + ', ' as [text()] 
			from sys.columns 
			where object_id = object_id('Leagues.Teams') 
			for xml path('')
		)

		--remove trailing ','
		select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))
		select @columnsLessIdentity = (select replace(@columnsLessIdentity, 'TeamAI, ', ''))

		set @sql = N'' +
			'insert into Leagues.Teams(' + @columnsLessIdentity + ') '
			+ 'select ' 
			+		replace(replace(replace(replace(@columnsLessIdentity, ', teamId, ', ', TeamID_Target, '), 'agegroupId, ', '' + '(select AgegroupId_Target from [#Ags] where agegroupId = t.agegroupId)' + ', '), 'divId, ', '' + '(select DivId_Target from [#Divs] where divId = t.divId)' + ', '), 'jobId, ','''' + convert(varchar(max), @jobID_Target) + ''', ') + ' '  
			+ 'from #Teams t '

		select @sql
		exec sp_executesql @sql

		update t set 
			teamName = teamName,
			leagueId = @leagueId_Target,
			season = @jobSeason_Target,
			year = (select year from Jobs.Jobs where jobId = @jobID_Target),

			prevTeamId = null,
			leagueTeamId = null,
			teamFullName = null,

			modified = getdate(),
			lebUserID = @superuserId
		from 
			Leagues.teams t
		where 
			t.jobID = @jobID_Target
	end
end
/*****END*****clone TEAMS*****/

select 'debug', l.* from Leagues.Leagues l where l.LeagueId = @leagueId_Target
select 'debug', ag.* from Leagues.agegroups ag where ag.leagueId = @leagueId_Target and ag.season = @jobSeason_Target
select 'debug', d.* from Leagues.agegroups ag inner join Leagues.divisions d on ag.agegroupId = d.agegroupId where 	ag.leagueId = @leagueId_Target	and ag.season = @jobSeason_Target
select 'debug', t.* from Leagues.teams t where t.jobID = @jobID_Target

--delete t from Leagues.teams t where t.jobID = @jobID_Target
--delete d from Leagues.agegroups ag inner join Leagues.divisions d on ag.agegroupId = d.agegroupId where 	ag.leagueId = @leagueId_Target	and ag.season = @jobSeason_Target
--delete ag from Leagues.agegroups ag where ag.leagueId = @leagueId_Target and ag.season = @jobSeason_Target
--delete l from Leagues.Leagues l where l.LeagueId = @leagueId_Target

