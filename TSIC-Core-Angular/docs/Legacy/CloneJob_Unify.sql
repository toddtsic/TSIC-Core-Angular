/*
USE [TSICV5]
GO

DECLARE @RC int
DECLARE @JobId_Source uniqueidentifier = '4393b576-94a6-44c5-add5-4d6c185ad649' --Top Threat Tournaments:Fall Draw 2021
DECLARE @CustomerId_Target uniqueidentifier = 'e66412c9-7dee-4e15-8fed-33e2cd4f8a7d' --Top Threat Tournaments
DECLARE @JobPath_Target varchar(80) = 'topthreat-falldraw-2022'
DECLARE @JobName_Target varchar(80) = 'Top Threat Tournaments:Fall Draw 2022'
DECLARE @JobYear_Target varchar(80) = '2022'
DECLARE @JobSeason_Target varchar(80) = 'Fall'
DECLARE @JobLabel_Target varchar(80) = '<i>Fall Draw 2022</i>'
DECLARE @ExpiryAdmin datetime = '11/30/2023'
DECLARE @ExpiryUsers datetime = '11/30/2022'
DECLARE @DisplayName varchar(80) = 'Top Threat Tournaments'
DECLARE @RegForm_from varchar(max) = 'matt@topthreattouraments.com'
DECLARE @LeagueName_Target varchar(80) = 'Top Threat Fall Draw 2021'
DECLARE @TeamsToClone int = 0
DECLARE @AdvanceAgeRangesOneYear bit = 0
DECLARE @CloneAgegroupsAndDivisions bit = 0

-- TODO: Set parameter values here.

EXECUTE @RC = [utility].[CloneJob_Unify] 
   @JobId_Source
  ,@CustomerId_Target
  ,@JobPath_Target
  ,@JobName_Target
  ,@JobYear_Target
  ,@JobSeason_Target
  ,@JobLabel_Target
  ,@ExpiryAdmin
  ,@ExpiryUsers
  ,@DisplayName
  ,@RegForm_from
  ,@LeagueName_Target
  ,@TeamsToClone
  ,@AdvanceAgeRangesOneYear
  ,@CloneAgegroupsAndDivisions
GO


*/

CREATE procedure [utility].[CloneJob_Unify]
(
	@JobId_Source uniqueidentifier,
	@CustomerId_Target uniqueidentifier,

	@JobPath_Target varchar(80),
	@JobName_Target varchar(80),
	@JobYear_Target varchar(80),
	@JobSeason_Target varchar(80),
	@JobLabel_Target varchar(80),

	@ExpiryAdmin datetime,
	@ExpiryUsers datetime,
	@DisplayName varchar(80),
	@RegForm_from varchar(max),


	@LeagueName_Target varchar(80),
	@TeamsToClone int,
	@AdvanceAgeRangesOneYear bit,
	@CloneAgegroupsAndDivisions bit
)

as set nocount off

/*****START*****variables needed throughout*****/
declare @superuserId nvarchar(254) = (select id from dbo.AspNetUsers where UserName = 'TSICSuperuser')
declare @columnsLessIdentity varchar(max)
declare @sql nvarchar(max)
declare @JobId_Target uniqueidentifier = NEWID()
declare	@LeagueId_Source uniqueidentifier = (select top 1 jl.leagueId from Jobs.Job_Leagues jl where jl.jobId = @JobId_Source)
declare @jobSeason_Source varchar(20) = (select season from Jobs.Jobs where jobId = @jobID_Source)

/*****END*****variables needed throughout******/

/*****START*****clone original JOBS.JOBS record*****/
select @columnsLessIdentity = (
	select name + ', ' as [text()] 
	from sys.columns 
	where object_id = object_id('Jobs.Jobs') 
	for xml path('')
)

--remove trailing ','
select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))
--remove identity column
select @columnsLessIdentity = (select replace(@columnsLessIdentity, ', jobAI', ''))

set @sql = N'' +
	'insert into Jobs.Jobs(' + @columnsLessIdentity + ') '
	+ 'select ' 
	+		replace(replace(@columnsLessIdentity, ', jobId', ', ''' + convert(varchar(max), @JobID_Target) + ''''), ', jobPath', ',''''') + ' '  
	+ 'from Jobs.Jobs '
	+ 'where jobId=''' + cast(@JobId_Source as varchar(max)) + ''' '
	+ 'and not exists(select * from Jobs.Jobs where jobId = ''' + convert(varchar(max), @JobId_Target) + ''')'

exec sp_executesql @sql

update j
	set 
		jobName_QBP = null,
		bRegistrationAllowPlayer = 0, --force to false initially 8/1/2024
		bRegistrationAllowTeam = 0,   --force to false initially 8/1/2024
		customerId = @CustomerId_Target,
		jobPath = @JobPath_Target,
		jobName = @JobName_Target,
		jobDescription = @JobName_Target,
		season = @JobSeason_Target,
		year = @JobYear_Target,

		ExpiryAdmin = @ExpiryAdmin,
		ExpiryUsers = @ExpiryUsers,
		DisplayName = @DisplayName,
		@RegForm_from = @RegForm_from,		

		BTeamsFullPaymentRequired = 0,
		USLaxNumberValidThroughDate = dateadd(year, 1, USLaxNumberValidThroughDate),

		bSuspendPublic = 1,                     --force initial defaults 11/21/2024
		bAllowMobileLogin = 0,                  --force initial defaults 11/21/2024
		bEnableTSICTeams = 0,                   --force initial defaults 11/21/2024
		bScheduleAllowPublicAccess = 0,         --force initial defaults 11/21/2024

		EventStartDate = null,
		EventEndDate = null,

		modified = getdate(),
		lebUserID = @superuserId
from 
	Jobs.Jobs j 
where 
	j.jobId = @JobId_Target
/*****END*****clone original JOBS.JOBS record*****/

/*****START*****clone original JOBS.JOBDISPLAYOPTIONS record*****/
select @columnsLessIdentity = (
	select name + ', ' as [text()] 
	from sys.columns 
	where object_id = object_id('Jobs.JobDisplayOptions') 
	for xml path('')
)

--remove trailing ','
select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))

set @sql = N'' +
	'insert into Jobs.JobDisplayOptions(' + @columnsLessIdentity + ') '
	+ 'select ' 
	+		replace(@columnsLessIdentity, 'jobId, ', '''' + convert(varchar(max), @JobID_Target) + ''', ') + ' '  
	+ 'from Jobs.JobDisplayOptions '
	+ 'where jobId=''' + cast(@JobId_Source as varchar(max)) + ''' '
	+ 'and not exists(select * from Jobs.JobDisplayOptions where jobId = ''' + convert(varchar(max), @JobId_Target) + ''')'

exec sp_executesql @sql

declare @JobLabel_Source varchar(80);
select @JobLabel_Source = jdo.parallaxSlide1Text1 from Jobs.JobDisplayOptions jdo where jdo.jobId = @JobId_Source
update jdo
	set 
		--parallaxSlide1Text1 = '<span style="line-height: 20.7999992370605px;">&lt;i&gt;' + @JobLabel_Target + '&lt;br&gt;' + @JobYear_Target + '&lt;/i&gt;</span>',
		--parallaxSlide1Text1 = '<i>' + @JobLabel_Target + '<br />' + @JobYear_Target + '</i>',
		parallaxSlide1Text1 = @JobLabel_Source,
		modified = getdate(),
		lebUserID = @superuserId
from 
	Jobs.JobDisplayOptions jdo 
where 
	jdo.jobId = @JobId_Target
/*****END*****clone original JOBS.JOBDISPLAYOPTIONS*****/

/*****START*****clone original JOBS.JOBOWLIMAGES record*****/
select @columnsLessIdentity = (
	select name + ', ' as [text()] 
	from sys.columns 
	where object_id = object_id('Jobs.JobOwlImages') 
	for xml path('')
)

--remove trailing ','
select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))

set @sql = N'' +
	'insert into Jobs.JobOwlImages(' + @columnsLessIdentity + ') '
	+ 'select ' 
	+		replace(@columnsLessIdentity, 'jobId, ', '''' + convert(varchar(max), @JobID_Target) + ''', ') + ' '  
	+ 'from Jobs.JobOwlImages '
	+ 'where jobId=''' + cast(@JobId_Source as varchar(max)) + ''' '
	+ 'and not exists(select * from Jobs.JobOwlImages where jobId = ''' + convert(varchar(max), @JobId_Target) + ''')'

exec sp_executesql @sql

update joi
	set 
		modified = getdate(),
		lebUserID = @superuserId
from 
	Jobs.JobOwlImages joi 
where 
	joi.jobId = @JobId_Target
/*****END*****clone original JOBS.JOBOWLIMAGES*****/

/*****START*****clone original JOBS.BULLETINS record*****/
select @columnsLessIdentity = (
	select name + ', ' as [text()] 
	from sys.columns 
	where object_id = object_id('Jobs.Bulletins') 
	for xml path('')
)

--remove trailing ','
select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))
select @columnsLessIdentity = (select replace(@columnsLessIdentity, ', bulletinID', ''))

set @sql = N'' +
	'insert into Jobs.Bulletins(' + @columnsLessIdentity + ') '
	+ 'select ' 
	+	replace(
			replace(
				replace(
					@columnsLessIdentity, 
					', jobId', 
					', ''' + convert(varchar(max), @JobID_Target) + ''''
				), 
				', createDate', 
				',	dateadd(
						day, 
						datediff(
							day, 
							(select max(createDate) from Jobs.Bulletins j where j.jobId = ''' + convert(varchar(max), @JobId_Source) + '''),
							getdate() 
						), 
						createdate
					)'
			),
			', modified',
			',''' + convert(char(10), getdate(), 101) + ''''
		) + ' '  
	+ 'from Jobs.Bulletins b  '
	+ 'where b.jobId=''' + cast(@JobId_Source as varchar(max)) + ''' '
	+ 'and not exists(select * from Jobs.Bulletins where jobId = ''' + convert(varchar(max), @JobId_Target) + ''')'

exec sp_executesql @sql

declare @maxBulletinCreateDateJobOld datetime
select @maxBulletinCreateDateJobOld = max(createDate) from Jobs.Bulletins where jobID = @JobId_Source
declare @timeSinceMaxBulletinCreateDateJobIdOld int
select @timeSinceMaxBulletinCreateDateJobIdOld =  datediff(hour, @maxBulletinCreateDateJobOld, getdate())

update b
	set 
		active = 0,  --start cloned bulletin as inactive 8/1/2024
		createdate = dateadd(hour, @timeSinceMaxBulletinCreateDateJobIdOld, b.createdate),  --technique to adjust dates in clone 8/1/2024
		StartDate = dateadd(hour, @timeSinceMaxBulletinCreateDateJobIdOld, b.StartDate),  --technique to adjust dates in clone 8/1/2024
		EndDate = dateadd(hour, @timeSinceMaxBulletinCreateDateJobIdOld, b.EndDate),  --technique to adjust dates in clone 8/1/2024
		modified = getdate(),
		lebUserID = @superuserId
from 
	Jobs.Bulletins b 
where 
	b.jobId = @JobId_Target
/*****END*****clone original JOBS.BULLETINS*****/

/*****START*****clone MENUS*****/
EXECUTE [Jobs].[Menus_Clone] 
   @jobIDSource = @JobId_Source
  ,@jobIDTarget = @JobId_Target

update jm set 
	modified = getdate(),	
	lebUserID = @superuserId
from Jobs.JobMenus jm 
where jm.jobID = @JobId_Target

update jmi set 
	modified = getdate(), 
	lebUserID = @superuserId 
from Jobs.JobMenu_Items jmi inner join Jobs.JobMenus jm on jmi.menuID = jm.menuID 
where jm.jobID = @JobId_Target

/*****END*****clone MENUS*****/

/*****START*****clone JOBS.JobAgeRanges record*****/
insert into Jobs.JobAgeRanges(
	[jobID], 
	[lebUserID], 
	[modified], 
	[rangeLeft], 
	[rangeName], 
	[rangeRight]
)
select 
	@JobId_Target,
	@superuserId,
	getdate(), 
	dateadd(year, convert(int, @AdvanceAgeRangesOneYear), jar.rangeLeft),
	jar.rangeName,
	dateadd(year, convert(int, @AdvanceAgeRangesOneYear), jar.rangeRight)
from
	Jobs.JobAgeRanges jar
where
	jar.jobId = @JobId_Source
	and not exists (select * from Jobs.JobAgeRanges where jobId = @JobId_Target)

/*****END*****clone JOBS.JobAgeRanges*****/

/*****START*****clone ADMINISTRATORS (role=Superuser, Director, SuperDirector)*****/
select @columnsLessIdentity = (
	select '[' + name + ']' + ', ' as [text()] 
	from sys.columns 
	where object_id = object_id('Jobs.Registrations') 
	for xml path('')
)

--remove trailing ','
select @columnsLessIdentity = (select Left(@columnsLessIdentity, LEN(@columnsLessIdentity)-1))
select @columnsLessIdentity = (select replace(@columnsLessIdentity, '[RegistrationID], ', ''))
select @columnsLessIdentity = (select replace(@columnsLessIdentity, '[RegistrationAI], ', ''))

set @sql = N'' +
	'insert into Jobs.Registrations(' + @columnsLessIdentity + ') '
	+ 'select ' 
	+		replace(
				replace(
					replace(
						@columnsLessIdentity, 
						', [jobId]', 
						', ''' + convert(varchar(max), @JobID_Target) + ''''
					), 
					', [RegistrationTS]', 
					',''' + convert(char(10), getdate(), 101) + ''''
				),
				', [modified]',
				',''' + convert(char(10), getdate(), 101) + ''''
			) + ' '   
	+ 'from Jobs.Registrations '
	+ 'where jobId=''' + cast(@JobId_Source as varchar(max)) + ''' and  roleId in (''7B9EB503-53C9-44FA-94A0-17760C512440'',''CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9'',''FF4D1C27-F6DA-4745-98CC-D7E8121A5D06'')'
	+ 'and not exists(select * from Jobs.Registrations where jobId = ''' + convert(varchar(max), @JobId_Target) + ''' and  roleId in (''7B9EB503-53C9-44FA-94A0-17760C512440'',''CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9'',''FF4D1C27-F6DA-4745-98CC-D7E8121A5D06''))'
select @sql

exec sp_executesql @sql


update r
	set 
		bActive = case when roles.name = 'Superuser' then 1 else 0 end,  --on clone leave only the superuser active 8/1/2024
		registrationTS = dateadd(YEAR, 1, rOld.registrationTS),
		modified = getdate(),
		lebUserID = @superuserId
from 
	Jobs.Registrations r 
	inner join dbo.AspNetRoles roles on r.RoleId = roles.id
	inner join Jobs.Registrations rOld on rOld.jobId = @JobId_Source and r.UserId = rOld.userId and r.RoleId = rOld.RoleId 
where 
	r.jobId = @JobId_Target
	and  r.roleId in ('7B9EB503-53C9-44FA-94A0-17760C512440','CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9','FF4D1C27-F6DA-4745-98CC-D7E8121A5D06')
/*****END*****clone JADMINISTRATORS (role=Superuser, Director, SuperDirector)*****/

/*****START*****clone LADT*****/

EXECUTE [utility].[LADT_Clone_Unify_CreateNewLeague] 
	@leagueIDOld = @LeagueId_Source
  ,	@leagueName_Target = @leagueName_Target
  ,	@jobID_Source = @jobID_Source
  ,	@jobID_Target = @jobID_Target

  ,	@jobSeason_Source = @jobSeason_Source
  ,	@jobSeason_Target = @JobSeason_Target

  ,	@TeamsToClone = @TeamsToClone
  ,	@CloneAgegroupsAndDivisions = @CloneAgegroupsAndDivisions

/*****END*****clone LADT*****/

/*
		@leagueIDOld uniqueidentifier = 'f8f3dd69-a2b6-df11-9d30-00137250256d'
	,	@leagueName_Target varchar(80) = 'asdf'
	,	@jobID_Source uniqueidentifier = '484f37aa-3bcd-40f9-858c-a204130d1569'
	,	@jobID_Target uniqueidentifier = 'C2D85957-2604-409B-B565-133176413E4F'

	,	@jobSeason_Source varchar(20) = 'summer'
	,	@jobSeason_Target varchar(20) = 'summer'
	,	@TeamsToCone int = 1
	,	@CloneAgegroupsAndDivisions bit

*/

/* BEGIN select dx and DELETES (remove when debugged)*/
select 'Job', * from Jobs.Jobs j where jobId = @JobId_Target
select 'JobDisplayOptions', * from Jobs.JobDisplayOptions jdo where jobId = @JobId_Target
select 'JobOwlImages', * from Jobs.JobOwlImages joi where jobId = @JobId_Target
select 'Bulletins', * from Jobs.Bulletins b where jobId = @JobId_Target
select 'Menus', * from Jobs.JobMenus jm where jm.jobID = @JobId_Target
select 'MenuItems-Parent', * from Jobs.JobMenu_Items jmi inner join Jobs.JobMenus jm on jmi.menuID = jm.menuID and jmi.parentMenuItemID is null  where jm.jobID = @JobId_Target
select 'MenuItems-Children', * from Jobs.JobMenu_Items jmi inner join Jobs.JobMenus jm on jmi.menuID = jm.menuID and NOT jmi.parentMenuItemID is null  where jm.jobID = @JobId_Target
select 'JobAgeRanges', * from Jobs.JobAgeRanges jar where jobId = @JobId_Target
select 'Administrators', * from Jobs.Registrations r where r.jobId = @JobId_Target and  r.roleId in ('7B9EB503-53C9-44FA-94A0-17760C512440','CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9','FF4D1C27-F6DA-4745-98CC-D7E8121A5D06')

select @JobId_Target as '@JobId_Target'

--delete r from Jobs.Registrations r where r.jobId = @JobId_Target and  r.roleId in ('7B9EB503-53C9-44FA-94A0-17760C512440','CD9DC8D7-19A0-47C3-A3E5-ACB19FB90DA9','FF4D1C27-F6DA-4745-98CC-D7E8121A5D06')
--delete jar from Jobs.JobAgeRanges jar where jobId = @JobId_Target
--delete jmi from Jobs.JobMenu_Items jmi inner join Jobs.JobMenus jm on jmi.menuID = jm.menuID and NOT jmi.parentMenuItemID is null  where jm.jobID = @JobId_Target
--delete jmi from Jobs.JobMenu_Items jmi inner join Jobs.JobMenus jm on jmi.menuID = jm.menuID and jmi.parentMenuItemID is null  where jm.jobID = @JobId_Target
--delete jm from Jobs.JobMenus jm where jm.jobID = @JobId_Target
--delete b from Jobs.Bulletins b where jobId = @JobId_Target
--delete joi from Jobs.JobOwlImages joi where jobId = @JobId_Target
--delete jdo from Jobs.JobDisplayOptions jdo where jobId = @JobId_Target
--delete j from Jobs.Jobs j where jobId = @JobId_Target
/* END select dx and DELETES (remove when debugged)*/

