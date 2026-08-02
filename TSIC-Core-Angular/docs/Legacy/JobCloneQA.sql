CREATE procedure [utility].[JobCloneQA]
(
	@jobId uniqueidentifier = 'f97eddb3-fee7-4fb2-8e6d-78e302c6b49b' --LI YELLOW JACKETS:NASSAU PLAYERS 2025
)
as
set nocount on

declare @qaTest varchar(max)


select @qaTest = 'Job Fields'
select 'QA Test: ' + @qaTest

declare @tFvFr table (FieldName varchar(80), FieldValue varchar(max))

insert into @tFvFr(FieldName, FieldValue) select 'CustomerName', c.customerName from Jobs.Jobs j inner join Jobs.Customers c on j.customerID = c.customerID where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'AdnLoginId', c.AdnLoginId from Jobs.Jobs j inner join Jobs.Customers c on j.customerID = c.customerID where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'JobName', j.JobName from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'JobName_QBP', j.jobName_QBP from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'JobDescription', j.JobDescription from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'JobPath', j.JobPath from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'ParallaxSlide1Text1', jdo.parallaxSlide1Text1 from Jobs.JobDisplayOptions jdo where jdo.jobId = @jobID
insert into @tFvFr(FieldName, FieldValue) select 'ParallaxSlide1Text2', jdo.parallaxSlide1Text2 from Jobs.JobDisplayOptions jdo where jdo.jobId = @jobID
insert into @tFvFr(FieldName, FieldValue) select 'BRegistrationAllowPlayer', j.BRegistrationAllowPlayer from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BRegistrationAllowTeam', j.BRegistrationAllowTeam from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'ExpiryAdmin', convert(char(10), j.ExpiryAdmin, 101) from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'ExpiryUsers', convert(char(10), j.ExpiryUsers, 101) from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'Season', j.Season from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'Year', j.Year from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'JobType', jt.JobTypeName from Jobs.Jobs j inner join reference.JobTypes jt on j.JobTypeID = jt.JobTypeID where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'Sport', s.sportName from Jobs.Jobs j inner join reference.Sports s on j.sportID = s.sportID where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'AdnArb', j.AdnArb from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'AdnArbBillingOccurences', j.AdnArbBillingOccurences from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'AdnArbIntervalLength', j.AdnArbIntervalLength from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'AdnArbStartDate', j.AdnArbStartDate from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BAddProcessingFees', j.BAddProcessingFees from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BApplyProcessingFeesToTeamDeposit', j.BApplyProcessingFeesToTeamDeposit from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BTeamsFullPaymentRequired', j.BTeamsFullPaymentRequired from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'PaymentMethodsAllowed_Code', case j.PaymentMethodsAllowed_Code when 1 then 'Credit Card ONLY' when 2 then 'Credit Card or Check' when 3 then 'Check ONLY' else '?' end from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'PlayerReg_RefundPolicy', j.PlayerReg_RefundPolicy from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BillingType', bt.BillingTypeName from Jobs.Jobs j inner join reference.Billing_Types bt on j.BillingTypeID = bt.BillingTypeID where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'PerPlayerCharge', j.perPlayerCharge from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'PerTeamCharge', j.PerTeamCharge from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'CoreRegformPlayer', j.CoreRegformPlayer from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BOfferPlayerRegsaverInsurance', j.BOfferPlayerRegsaverInsurance from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BUseWaitlists', j.BUseWaitlists from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BAllowRosterViewPlayer', j.BAllowRosterViewPlayer from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'bRestrictPlayerTeamsToAgerange', j.bRestrictPlayerTeamsToAgerange from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'PlayerReg_MultiPlayerDiscount_Percent', j.PlayerReg_MultiPlayerDiscount_Percent from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'RegformName_ClubRep', j.RegformName_ClubRep from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'RegformName_Coach', j.RegformName_Coach from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'RegformName_Team', j.RegformName_Team from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BAllowRosterViewAdult', j.BAllowRosterViewAdult from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BClubRepAllowEdit', j.BClubRepAllowEdit from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BClubRepAllowDelete', j.BClubRepAllowDelete from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BClubRepAllowAdd', j.BClubRepAllowAdd from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BScheduleAllowPublicAccess', j.BScheduleAllowPublicAccess from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BAllowMobileLogin', j.BAllowMobileLogin from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BSuspendPublic', j.BSuspendPublic from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BAllowMobileRegn', j.BAllowMobileRegn from Jobs.Jobs j where j.jobId = @jobId
insert into @tFvFr(FieldName, FieldValue) select 'BEnableTSICTeams', coalesce(j.bEnableTSICTeams, 0) from Jobs.Jobs j where j.jobId = @jobId

select * from @tFvFr


