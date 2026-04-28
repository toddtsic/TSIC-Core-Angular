-- =====================================================================
-- Processing-Fee Convention Sweep — half-converted sprocs
-- =====================================================================
-- Purpose: each sproc in this script reads Jobs.ProcessingFeePercent
--          (a decimal(4,2) storing whole percent — e.g. 3.80) but
--          treats the value as a fraction in the math, silently 100x
--          overstating CC fees for any job with a non-NULL value.
--
-- Pattern (matches C# FeeResolutionService convention):
--      coalesce(<alias>.ProcessingFeePercent, 0.035)
--   →  coalesce(<alias>.ProcessingFeePercent, 3.5) / 100
--
-- Whole-percent fallback (3.5) matches FeeConstants.MinProcessingFeePercent;
-- the trailing /100 mirrors Math.Clamp(...) / 100m. Same numeric result
-- for NULL and non-NULL stored values; this shape was chosen to make
-- side-by-side comparison with C# easier.
--
-- Risk: post-deploy, reports on jobs with a non-NULL
-- ProcessingFeePercent will start showing correct (smaller) CC-fee
-- numbers. Reports that have been silently wrong will look different.
-- Flag to anyone doing month-over-month reconciliation.
--
-- NOTE: the trigger Jobs.Job_AfterEdit_TeamFees is NOT in this script.
-- All active triggers will be deactivated at cutover, per user plan.
-- =====================================================================


-- ---------------------------------------------------------------------
-- 1. [reporting].[CustomerJobRevenueRollups]
--    Edits: 2 sites (lines 86, 129 of the original definition)
--    All other ProcessingFeePercent / 0.035 occurrences left alone:
--      - line 62 (temp table default)         — never reaches CC math
--      - lines 75, 118 (column name in INSERT) — metadata only
--      - lines 104, 147 (GROUP BY key)         — same grouping either way
--      - line 289 (rd.payment * rd.proc...)    — reads from temp, fixed upstream
-- ---------------------------------------------------------------------
ALTER procedure [reporting].[CustomerJobRevenueRollups]
(
	@jobID uniqueidentifier = '27d4112e-a6e7-4e91-9a7a-94c749d4de33',
	@startDate datetime = '4/1/2024',
	@endDate datetime = '4/30/2024',
	@listJobsString varchar(max)
)
as
set nocount on

declare @listJobs table (jobName varchar(80))

insert into @listJobs(jobName)
SELECT value
FROM STRING_SPLIT(@listJobsString, ',')
WHERE RTRIM(value) <> '';

declare @customerId uniqueidentifier
select @customerId = customerId from Jobs.Jobs where jobId = @jobId

declare @customerGroupId int
select @customerGroupId = cgc.CustomerGroupId
from
	Jobs.CustomerGroupCustomers cgc
	inner join Jobs.Customers c on cgc.CustomerId = c.customerID
where
	cgc.CustomerId = @customerId

declare @listCustomerIds table (
	customerId uniqueidentifier
)

--update end date to the first of the next month
select @endDate = dateadd(day, 1, @endDate)

insert into @listCustomerIds(customerId)
select cgc.customerId
from
	Jobs.CustomerGroupCustomers cgc
where
	cgc.CustomerGroupId = @customerGroupId

if (not exists(select * from @listCustomerIds)) begin
	insert into @listCustomerIds(customerId) select @customerId
end

declare @txRawData table(
	customerName varchar(80),
	customerId uniqueidentifier,
	jobName varchar(80),
	jobId uniqueidentifier,
	year int,
	month int,
	paymentMethod varchar(80),
	payment float,
	processingFeePercent decimal(8,3) default(0.035)  -- added; replaces hardcoded 0.03500
)

--insert cc payments
insert into @txRawData(
	customerName,
	customerId,
	jobName,
	jobId,
	year,
	month,
	paymentMethod,
	payment,
	processingFeePercent
)
select
	c.customerName,
	c.customerId,
	j.jobName,
	j.jobId,
	year(adn.SettlementTS),
	month(adn.SettlementTS),
	replicate(' ', 4) + 'CC Payments',
	sum(adn.[Settlement Amount]),
	coalesce(j.ProcessingFeePercent, 3.5) / 100
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join adn.vtxs adn on ra.adntransactionid = adn.[transaction id]
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('Credit Card Payment')
	and ( adn.SettlementTS >= @startDate and adn.SettlementTS < @endDate)
	and not adn.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	j.ProcessingFeePercent,
	year(adn.SettlementTS),
	month(adn.SettlementTS)

--insert cc credits
insert into @txRawData(
	customerName,
	customerId,
	jobName,
	jobId,
	year,
	month,
	paymentMethod,
	payment,
	processingFeePercent
)
select
	c.customerName,
	c.customerId,
	j.jobName,
	j.jobId,
	year(adn.SettlementTS),
	month(adn.SettlementTS),
	replicate(' ', 3) + 'CC Credits',
	sum(adn.[Settlement Amount]),
	coalesce(j.ProcessingFeePercent, 3.5) / 100
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join adn.vtxs adn on ra.adntransactionid = adn.[transaction id]
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('Credit Card Credit')
	and ( adn.SettlementTS >= @startDate and adn.SettlementTS < @endDate)
	and not adn.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	j.ProcessingFeePercent,
	year(adn.SettlementTS),
	month(adn.SettlementTS)

--insert check data
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
	j.jobName,
	j.jobId,
	year(ra.createdate),
	month(ra.createdate),
	replicate(' ',6) + 'Check',
	sum(ra.payamt) as payamt
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	ra.active = 1
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and apm.paymentMethod in ('Check Payment By Client', 'Check Payment By TSIC')
	and not exists (select * from adn.vtxs adn where ra.adntransactionId = adn.[transaction id] and adn.[transaction status] in ('Declined', 'Voided'))
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	apm.paymentMethod,
	year(ra.createdate),
	month(ra.createdate)

--insert check data received (to zero check contribution to total payment)
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
	j.jobName,
	j.jobId,
	year(ra.createdate),
	month(ra.createdate),
	replicate(' ', 5) + 'Check Client Rec''d',
	-(sum(ra.payamt)) as payamt
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	ra.active = 1
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and apm.paymentMethod in ('Check Payment By Client', 'Check Payment By TSIC')
	and not exists (select * from adn.vtxs adn where ra.adntransactionId = adn.[transaction id] and adn.[transaction status] in ('Declined', 'Voided'))
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	apm.paymentMethod,
	year(ra.createdate),
	month(ra.createdate)

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
	j.jobName,
	jc.jobId,
	jc.year,
	jc.month,
	replicate(' ', 0) + 'Admin Fees',
	-(sum(jc.chargeAmount))
from
	adn.JobAdminCharges jc
	inner join Jobs.Jobs j on jc.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	( datefromparts(jc.year, jc.month, 1) >= @startDate and datefromparts(jc.year, jc.month, 1) < @endDate)
group by
	c.customerName,
	c.customerID,
	j.jobName,
	jc.jobId,
	jc.year,
	jc.month

-- insert CC Processing Fees
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
	rd.customerName,
	rd.customerID,
	rd.jobName,
	rd.jobId,
	rd.year,
	rd.month,
	replicate(' ', 2) + 'CC Fees',
	-(
		abs(
			rd.payment * rd.processingFeePercent
		)
	)
from
	@txRawData rd
where
	rd.paymentMethod like '%CC%'

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
	j.jobName,
	j.jobId,
	mjs.year,
	mjs.month,
	replicate(' ', 1) + 'TSIC Fees',
	convert(money, -((mjs.Count_NewPlayers_ThisMonth * coalesce(j.perPlayerCharge, 0.00)) + (mjs.Count_NewTeams_ThisMonth * coalesce(j.perTeamCharge, 0.00))))
from
	adn.Monthly_Job_Stats mjs
	inner join Jobs.Jobs j on mjs.jobID = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	( datefromparts(mjs.year, mjs.month, 1) >= @startDate and datefromparts(mjs.year, mjs.month, 1) < @endDate)

--EXPORT JOBREVENUERECORDS
select
	rd.jobName as JobName,
	rd.year as Year,
	rd.month as Month,
	paymentMethod as PayMethod,
	convert(decimal(8,2), sum(rd.payment)) as PayAmount
from
	@txRawData rd
	inner join Jobs.Jobs j on rd.jobID = j.jobId
where
	(
		exists(select * from @listJobs) and exists (select * from @listJobs lj where j.JobName = lj.jobName)
		or not exists(select * from @listJobs)
	)
group by
	rd.jobName,
	rd.year,
	rd.month,
	paymentMethod
order by
	rd.jobName,
	rd.year,
	rd.month,
	rd.paymentMethod

--EXPORT JOB NUMBERS
select
	mjs.aid,
	j.jobName as JobName,
	mjs.Year,
	mjs.Month,
	mjs.Count_ActivePlayersToDate,
	mjs.Count_ActivePlayersToDate_LastMonth,
	mjs.Count_NewPlayers_ThisMonth,
	mjs.Count_ActiveTeamsToDate,
	mjs.Count_ActiveTeamsToDate_LastMonth,
	mjs.Count_NewTeams_ThisMonth
from
	adn.Monthly_Job_Stats mjs
	inner join Jobs.Jobs j on mjs.jobId = j.jobId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	(
		(datefromparts(mjs.year, mjs.month, 1) >= @startDate)
		and
		(datefromparts(mjs.year, mjs.month, 1) < @endDate)
	)
	and
	(
		exists(select * from @listJobs) and exists (select * from @listJobs lj where j.JobName = lj.jobName)
		or not exists(select * from @listJobs)
	)
order by
	j.jobName,
	mjs.Year,
	mjs.Month

--EXPORT JOB ADMIN FEES
select
	j.jobName as JobName,
	jac.Year as Year,
	jac.Month,
	ct.name as ChargeType,
	jac.ChargeAmount,
	coalesce(jac.comment, '') as Comment
from
	adn.JobAdminCharges jac
	inner join [reference].[JobAdminChargeTypes] ct on jac.ChargeTypeId = ct.id
	inner join Jobs.Jobs j on jac.jobId = j.jobId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	(
		(datefromparts(jac.year, jac.month, 1) >= @startDate)
		and
		(datefromparts(jac.year, jac.month, 1) < @endDate)
	)
	and
	(
		exists(select * from @listJobs) and exists (select * from @listJobs lj where j.JobName = lj.jobName)
		or not exists(select * from @listJobs)
	)
order by
	j.jobName,
	jac.Year,
	jac.Month,
	ct.name

declare @rawPaymentRecords table(
	id int primary key not null,
	JobName varchar(80),
	Registrant varchar(max),
	PaymentMethod varchar(80),
	PaymentDate DateTime,
	PaymentAmount money,
	[Year] int,
	[Month] int
)

insert into @rawPaymentRecords(
	id,
	JobName,
	Registrant,
	PaymentMethod,
	PaymentDate,
	[Year],
	[Month],
	PaymentAmount
)
select
	ra.aID,
	j.jobName as JobName,
	case when r.club_name is null then u.FirstName + ' ' + u.LastName else u.FirstName + ' ' + u.LastName + ' (' + r.club_name + ')' end as Registrant,
	apm.paymentMethod as PaymentMethod,
	adn.[SettlementTS] as PaymentDate,
	year(adn.[SettlementTS]) as [Year],
	month(adn.[SettlementTS]) as [Month],
	adn.[Settlement Amount] as PaymentAmount
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join dbo.AspNetUsers u on r.UserId = u.Id
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join adn.vtxs adn on ra.adntransactionid = adn.[transaction id]
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('Credit Card Payment')
	and ( adn.SettlementTS >= @startDate and adn.SettlementTS < @endDate)
	and not adn.[transaction status] in ('Declined', 'Voided')

insert into @rawPaymentRecords(
	id,
	JobName,
	Registrant,
	PaymentMethod,
	PaymentDate,
	[Year],
	[Month],
	PaymentAmount
)
select
	ra.aID,
	j.jobName as JobName,
	case when r.club_name is null then u.FirstName + ' ' + u.LastName else u.FirstName + ' ' + u.LastName + ' (' + r.club_name + ')' end as Registrant,
	'Check' as PaymentMethod,
	ra.CreateDate as PaymentDate,
	year(ra.CreateDate) as [Year],
	month(ra.CreateDate) as [Month],
	ra.payamt as PaymentAmount
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join dbo.AspNetUsers u on r.UserId = u.Id
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	ra.active = 1
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and apm.paymentMethod in ('Check Payment By Client', 'Check Payment By TSIC')
	and not exists (select * from adn.vtxs adn where ra.adntransactionId = adn.[transaction id] and adn.[transaction status] in ('Declined', 'Voided'))
order by j.jobName

--EXPORT JOB CC RECORDS
select
	JobName,
	[Year],
	[Month],
	Registrant,
	PaymentMethod,
	PaymentDate,
	PaymentAmount
from
	@rawPaymentRecords pr
where
	PaymentMethod in ('Credit Card Payment','Credit Card Credit')
	and
	(
		exists(select * from @listJobs) and exists (select * from @listJobs lj where pr.JobName = lj.jobName)
		or not exists(select * from @listJobs)
	)
order by
	JobName, [Year], [Month], PaymentDate

--EXPORT JOB CHECK RECORDS
select
	JobName,
	[Year],
	[Month],
	Registrant,
	PaymentMethod,
	PaymentDate,
	PaymentAmount
from
	@rawPaymentRecords pr
where
	PaymentMethod = 'Check'
	and
	(
		exists(select * from @listJobs) and exists (select * from @listJobs lj where pr.JobName = lj.jobName)
		or not exists(select * from @listJobs)
	)
order by
	JobName, [Year], [Month], PaymentDate

--EXPORT LIST AVAILABLE JOBS
select distinct j.JobName
from
	@listCustomerIds c
	inner join Jobs.Jobs j on c.customerId = j.customerID
where
	(ISNUMERIC(j.year) = 1)
	and convert(int, j.year) >= 2022
order by
	j.JobName
GO



-- ---------------------------------------------------------------------
-- 2. [adn].[customerInvoiceDataPerMonth]  (4 edits)
-- ---------------------------------------------------------------------
/*
exec adn.[customerInvoiceDataPerMonth] @jobIDStr = '%', @settlementYearStr = '2022', @settlementMonthStr = '6'
exec adn.[customerInvoiceDataPerMonth] @jobIDStr = '2f1e82d9-59b3-406f-9924-21b92b7cc6f1', @settlementYearStr = '2022', @settlementMonthStr = '6'
*/

ALTER procedure [adn].[customerInvoiceDataPerMonth]
(
		@jobIDStr varchar(max) = '2f1e82d9-59b3-406f-9924-21b92b7cc6f1'
	,	@settlementYearStr int = 2022
	,	@settlementMonthStr int = 6
)
as
set nocount on

declare @rawData table (
	customerId uniqueidentifier,
	customerName varchar(80),
	jobId uniqueidentifier,
	jobName varchar(max),
	invoicePeriod char(7),
	invoiceMonthNumber int,
	paymentDate Datetime,
	amountPaid money,
	paymentMethod varchar(80),
	ccHandlingFee money,
	perPlayerFeeTSIC money,
	perTeamFeeTSIC money,
	countActivePlayersToDate int,
	countNewPlayers_ThisMonth int,
	countActiveTeamsToDate int,
	countNewTeamsThisMonth  int,
	countActivePlayersToDateLastMonth int,
	countActiveTeamsToDateLastMonth int
)

insert into @rawData(
	customerId,
	customerName,
	jobId,
	jobName,
	invoicePeriod,
	invoiceMonthNumber,
	paymentDate,
	amountPaid,
	paymentMethod,
	ccHandlingFee,
	perPlayerFeeTSIC,
	perTeamFeeTSIC,
	countActivePlayersToDate,
	countNewPlayers_ThisMonth,
	countActiveTeamsToDate,
	countNewTeamsThisMonth,
	countActivePlayersToDateLastMonth,
	countActiveTeamsToDateLastMonth
)
select
	  v1.customerID
	, v1.customerName
	, v1.jobID
	, v1.jobName
	, invoicePeriod
	, [MonthNo]
	, [PaymentDate]
	, Amount
	, Category
	, ccCharges
	, coalesce(j.perPlayerCharge, 0) as perPlayerFeeTSIC
	, coalesce(j.perTeamCharge, 0) as perTeamFeeTSIC
	, coalesce(mjs.Count_ActivePlayersToDate, 0)
	, coalesce(mjs.Count_NewPlayers_ThisMonth, 0)
	, coalesce(mjs.Count_ActiveTeamsToDate, 0)
	, coalesce(mjs.Count_NewTeams_ThisMonth, 0)
	, coalesce(mjs.Count_ActivePlayersToDate_LastMonth, 0)
	, coalesce(mjs.Count_ActiveTeamsToDate_LastMonth, 0)
from
(
	(
		-- Player registrations
		select distinct 
			  c.customerID
			, c.customerName
			, j.jobID
			, j.jobName as jobName
			, convert(varchar, year(coalesce(adn.settlementTS, ra.createdate))) + '-' + right('00' + coalesce(convert(varchar, month(coalesce(adn.settlementTS, ra.createdate))), convert(varchar, month(coalesce(adn.settlementTS, ra.createdate)))), 2) as invoicePeriod
			, coalesce(convert(varchar, month(adn.settlementTS)), convert(varchar, month(ra.[modified]))) as [MonthNo]
			, coalesce(adn.settlementTS, ra.[modified]) as [PaymentDate]
			, case pm.PaymentMethod
					when 'Credit Card Payment' then adn.[settlement amount]
					when 'Credit Card Credit' then adn.[settlement amount]
					else ra.payamt
				end as Amount
			, coalesce(adn.[settlement amount], ra.payamt) as RawAmount
			, adn.[settlement amount]
			, pm.PaymentMethod as Category
			, case 
				when (pm.PaymentMethod = 'Credit Card Payment' or pm.paymentMethod = 'Credit Card Credit')
					and adn.[settlement amount] != 0
				then case when j.JobTypeID = 2 
						then coalesce(j.ProcessingFeePercent, 3.5) / 100 
						else coalesce(j.ProcessingFeePercent, 3.5) / 100 
					 end * abs(adn.[settlement amount])
				else 0 
			  end as ccCharges
			, coalesce(adn.[Invoice Number], ra.[checkNo], '') as invoiceNumber
			, coalesce(adn.[SettlementTS], ra.[modified], '') as invoiceDateTSIC
			, u.LastName as lastName
			, u.FirstName as firstName
			, u.FirstName + ' ' + u.LastName + ' (ID:' + u.UserName + ')' as member
			, u.userName as member_id
			, case 
				when coalesce(adn.[Invoice Number], ra.[checkNo], '') = '' then '' 
				else substring(coalesce(adn.[Invoice Number], ra.[checkNo], ''), CHARINDEX('_', coalesce(adn.[Invoice Number], ra.[checkNo], ''), CHARINDEX('_', coalesce(adn.[Invoice Number], ra.[checkNo], '')) + 1) + 1, LEN(coalesce(adn.[Invoice Number], ra.[checkNo], '')) - CHARINDEX('_', coalesce(adn.[Invoice Number], ra.[checkNo], ''), CHARINDEX('_', coalesce(adn.[Invoice Number], ra.[checkNo], '')) + 1))
			  end as RegID
			, convert(char(10), r.RegistrationTS, 112) as OnlineRegDate
			, ra.adnTransactionID
		from
			Jobs.Registrations r
			inner join Jobs.Jobs j on r.jobID = j.jobID
			inner join Jobs.customers c on j.customerID = c.customerID
			inner join Jobs.Registration_Accounting ra on 
				r.RegistrationID = ra.RegistrationID 
				and ra.active = 1
				and ra.teamID is null
			inner join reference.Accounting_PaymentMethods pm on ra.[PaymentMethodID] = pm.[PaymentMethodID]
			inner join dbo.AspNetUsers u on r.userID = u.Id
			left join adn.vtxs adn on 
				convert(varchar(max), ra.adntransactionid) = adn.[transaction id]
				and not adn.[transaction status] in ('Declined', 'Voided')
		where
			convert(varchar(max), j.jobID) like @jobIDStr
			and coalesce(convert(varchar, year(adn.settlementTS)), convert(varchar, year(ra.[modified]))) like @settlementYearStr
			and coalesce(convert(varchar, month(adn.settlementTS)), convert(varchar, month(ra.[modified]))) like @settlementMonthStr
			and coalesce(ra.payamt, 0) != 0
			and 
			(
				case 
					when pm.paymentMethodID = '30ECA575-A268-E111-9D56-F04DA202060D' then
						case 
							when adn.[Invoice Number] is null then 0
							else 1 
						end
					else 1	
				end					
			) = 1
	) 

	union all

	(
		-- Team registrations
		select distinct 
			  c.customerID
			, c.customerName
			, j.jobID
			, j.jobName as jobName
			, convert(varchar, year(coalesce(adn.[settlementts], ra.[createdate]))) + '-' + right('00' + coalesce(convert(varchar, month(coalesce(adn.[settlementts], ra.[createdate]))), convert(varchar, month(coalesce(adn.[settlementts], ra.[createdate])))), 2) as invoicePeriod
			, coalesce(convert(varchar, month(coalesce(adn.[settlementts], ra.[createdate]))), convert(varchar, month(coalesce(adn.[settlementts], ra.[createdate])))) as [MonthNo]
			, coalesce(adn.settlementTS, ra.[createdate]) as [PaymentDate]
			, case pm.PaymentMethod
					when 'Credit Card Payment' then ra.payamt
					when 'Credit Card Credit' then ra.payamt
					else ra.payamt
				end as Amount
			, ra.payamt as RawAmount
			, ra.payamt as [settlement amount]
			, pm.PaymentMethod as Category
			, case 
				when (pm.PaymentMethod = 'Credit Card Payment' or pm.paymentMethod = 'Credit Card Credit')
					and ra.payamt != 0
				then case when j.JobTypeID = 2 
						then coalesce(j.ProcessingFeePercent, 3.5) / 100 
						else coalesce(j.ProcessingFeePercent, 3.5) / 100 
					 end * abs(ra.payamt)
				else 0 
			  end as ccCharges
			, coalesce(adn.[Invoice Number], ra.[checkNo], '') as invoiceNo
			, coalesce(adn.[SettlementTS], ra.[modified], '') as invoiceDateTSIC
			, adn.Registrant_lastName as lastName
			, adn.Registrant_firstName as firstName
			, coalesce(tClub.customerName, '') + ':' + ag.agegroupname + ':' + teamName as member
			, null as member_id
			, convert(varchar, ra.aID) as RegID
			, convert(char(10), coalesce(ra.createdate, adn.settlementts), 112) as OnlineRegDate
			, ra.adnTransactionID
		from
			Jobs.customers c
			inner join Jobs.jobs j on c.customerid = j.customerid
			inner join reference.jobtypes jt on j.jobtypeid = jt.jobtypeid
			inner join Jobs.Registrations r on j.jobID = r.jobID
			inner join Jobs.Registration_Accounting ra on r.RegistrationID = ra.RegistrationID
			left join adn.txs txs on ra.adnTransactionID = txs.[Transaction ID]
			inner join Leagues.teams t on 
				ra.teamID = t.teamID
				and ra.active = 1
			inner join Leagues.agegroups ag on t.agegroupid = ag.agegroupid
			left join Jobs.Customers tClub on t.customerID = tClub.customerID
			left join adn.vTxs adn on 
				ra.adnTransactionID = adn.[transaction id]
				and not adn.[transaction status] in ('Declined', 'Voided')
			left join reference.Accounting_PaymentMethods pm on ra.[PaymentMethodID] = pm.[PaymentMethodID]			
		where 
			convert(varchar(max), j.jobID) like @jobIDStr
			and convert(varchar, year(coalesce(adn.settlementts, ra.createdate))) like @settlementYearStr
			and convert(varchar, month(coalesce(adn.settlementts, ra.createdate))) like @settlementMonthStr
			and 
			(
				case 
					when pm.paymentMethodID = '30ECA575-A268-E111-9D56-F04DA202060D' then
						case 
							when adn.[Invoice Number] is null then 0
							else 1 
						end
					else 1	
				end					
			) = 1
	)	
) v1
inner join Jobs.Jobs j on v1.jobID = j.jobID
left join adn.Monthly_Job_Stats mjs on v1.jobID = mjs.jobID 
	and CONVERT(int, @settlementYearStr) = mjs.year 
	and CONVERT(int, @settlementMonthStr) = mjs.month
where
	Category != 'Credit Card Void'
order by
		v1.invoicePeriod
	,	v1.customerName
	,	v1.jobName
	,	v1.Category
	,	v1.[PaymentDate]
	,	v1.lastName
	,	v1.firstName

select * from @rawData

GO

-- ---------------------------------------------------------------------
-- 3. [adn].[ExportMonthlyCustomerChecks]  (2 edits)
-- ---------------------------------------------------------------------
ALTER procedure [adn].[ExportMonthlyCustomerChecks]
(
	@settlementMonth int = 11,
	@settlementYear int = 2023
)
as set nocount on

-- @ccRate removed; now uses j.ProcessingFeePercent coalesced to 0.035 per job

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
	round(sum(abs(vtx.[Settlement Amount])) * coalesce(j.ProcessingFeePercent, 3.5) / 100, 2),
	sum(vtx.[Settlement Amount]),
	coalesce(j.ProcessingFeePercent, 3.5) / 100
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join adn.Monthly_Job_Stats mjs on r.jobID = mjs.jobId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join adn.vtxs vtx on ra.adntransactionid = vtx.[transaction id]
where
	mjs.year = @settlementYear and mjs.month = @settlementMonth
	and (year(vtx.SettlementTS) = mjs.year and month(vtx.SettlementTS) = mjs.month)
	and apm.paymentMethod in ('Credit Card Payment','Credit Card Credit')
	and not vtx.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName_QBP,
	j.jobName,
	j.ProcessingFeePercent,
	year(vtx.SettlementTS),
	month(vtx.SettlementTS)
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

-- ---------------------------------------------------------------------
-- 4. [adn].[ExportMonthlyJobRetainers]  (1 edits)
-- ---------------------------------------------------------------------

ALTER PROCEDURE [adn].[ExportMonthlyJobRetainers]
(
    @settlementMonth int = 8,
    @settlementYear int = 2024
)
AS 
SET NOCOUNT ON;

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
        * coalesce(j.ProcessingFeePercent, 3.5) / 100
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
    inner join adn.vtxs vtx 
        on ra.adntransactionid = vtx.[transaction id]
where
    mjs.year = @settlementYear
    and mjs.month = @settlementMonth
    and year(vtx.SettlementTS) = mjs.year
    and month(vtx.SettlementTS) = mjs.month
    and apm.paymentMethod in ('Credit Card Payment','Credit Card Credit')
    and vtx.[transaction status] not in ('Declined', 'Voided')
group by
    c.customerId,
    c.customerName,
    j.jobId,
    j.jobName_QBP,
    j.jobName,
    year(vtx.SettlementTS),
    month(vtx.SettlementTS),
    j.ProcessingFeePercent;

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

-- ---------------------------------------------------------------------
-- 5. [adn].[ExportMonthlyProcessingFees]  (1 edits)
-- ---------------------------------------------------------------------
ALTER procedure [adn].[ExportMonthlyProcessingFees]
(
	@settlementMonth int = 11,
	@settlementYear int = 2023
)
as set nocount on

-- @ccRate removed; now uses j.ProcessingFeePercent coalesced to 0.035 per job

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
	coalesce(j.ProcessingFeePercent, 3.5) / 100
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join adn.Monthly_Job_Stats mjs on r.jobID = mjs.jobId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join adn.vtxs vtx on ra.adntransactionid = vtx.[transaction id]
where
	mjs.year = @settlementYear and mjs.month = @settlementMonth
	and (year(vtx.SettlementTS) = mjs.year and month(vtx.SettlementTS) = mjs.month)
	and apm.paymentMethod in ('Credit Card Payment','Credit Card Credit')
	and not vtx.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName_QBP,
	j.jobName,
	j.ProcessingFeePercent,
	year(vtx.SettlementTS),
	month(vtx.SettlementTS)
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

-- ---------------------------------------------------------------------
-- 6. [adn].[GetLastMonthsGrandTotals]  (4 edits)
-- ---------------------------------------------------------------------
ALTER procedure [adn].[GetLastMonthsGrandTotals]
(
@jobID uniqueidentifier = '445d36fd-11ac-44ce-a063-5c8a92d0af9b' --MD Lax Camps Summer 2018
)
as
set nocount on

declare
		@settlementMonth int = datepart(month, dateadd(month, -1, getdate()))
	,	@settlementYear int = datepart(year, dateadd(month, -1, getdate()))

declare @qaTest varchar(max)

declare @tCCCredits table(customerName varchar(80), jobId uniqueidentifier, jobName varchar(80), sumCCCredits decimal(17,2))
insert into @tCCCredits(customerName, jobId, jobName, sumCCCredits)
select 
	c.customerName, 
	j.jobId,
	j.jobName, 
	sum(ra.payamt) as sumCCCredits
from
	Jobs.Registration_Accounting ra
	inner join adn.vTxs txs on ra.adnTransactionID = txs.[Transaction ID]
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationID = r.RegistrationID
	inner join Jobs.Jobs j on r.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
	inner join adn.Monthly_Job_Stats mjs on 
		j.jobId = mjs.jobID 
		and mjs.year = @settlementYear
		and mjs.month = @settlementMonth
where
	year(txs.SettlementTS)= @settlementYear
	and month(txs.SettlementTS) = @settlementMonth
	and c.adnLoginID = 'teamspt52'
	and ra.active = 1
	and apm.paymentMethod in ('Credit Card Credit')
group by
	c.customerName,
	j.jobId,
	j.jobName

declare @tCCPayments table(customerName varchar(80), jobId uniqueidentifier, jobName varchar(80), sumCCPayments decimal(17,2))
insert into @tCCPayments(customerName, jobId, jobName, sumCCPayments)
select 
	c.customerName, 
	j.jobId,
	j.jobName, 
	sum(ra.payamt) as sumCCCredits
from
	Jobs.Registration_Accounting ra
	inner join adn.vTxs txs on ra.adnTransactionID = txs.[Transaction ID]
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationID = r.RegistrationID
	inner join Jobs.Jobs j on r.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
	inner join adn.Monthly_Job_Stats mjs on 
		j.jobId = mjs.jobID 
		and mjs.year = @settlementYear
		and mjs.month = @settlementMonth
where
	year(txs.SettlementTS)= @settlementYear
	and month(txs.SettlementTS) = @settlementMonth
	and c.adnLoginID = 'teamspt52'
	and ra.active = 1
	and apm.paymentMethod in ('Credit Card Payment')
group by
	c.customerName,
	j.jobId,
	j.jobName

declare @tCCAdminFees table(customerName varchar(80), jobId uniqueidentifier, jobName varchar(80), sumAdminChargeAmount decimal(17,2))
insert into @tCCAdminFees(customerName, jobId, jobName, sumAdminChargeAmount)
select 
	coalesce(cg.CustomerGroupName, c.customerName),
	j.jobId,
	j.jobName,
	sum(jac.ChargeAmount)
from
	adn.JobAdminCharges jac
	inner join Jobs.Jobs j on jac.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
	left join Jobs.CustomerGroupCustomers cgc on c.customerID = cgc.CustomerId
	left join Jobs.CustomerGroups cg on cgc.CustomerGroupId = cg.Id
where
	jac.Year = @settlementYear
	and jac.Month = @settlementMonth
group by
	coalesce(cg.CustomerGroupName, c.customerName),
	j.jobId,
	j.jobName	

declare @tMJS table(customerGroup varchar(max), customerName varchar(80), jobId uniqueidentifier, jobName varchar(80), perPlayerCharge decimal, perTeamCharge decimal, Count_NewPlayers_ThisMonth int, Count_NewTeams_ThisMonth int)
insert into @tMJS(customerName, jobId, jobName, perPlayerCharge, perTeamCharge, Count_NewPlayers_ThisMonth, Count_NewTeams_ThisMonth)
select
	coalesce(cg.CustomerGroupName, c.customerName) as customerName,
	j.jobId,
	j.jobName,
	coalesce(j.perPlayerCharge, 0),
	coalesce(j.perTeamCharge, 0),
	mjs.Count_NewPlayers_ThisMonth,
	mjs.Count_NewTeams_ThisMonth
from
	adn.Monthly_Job_Stats mjs
	inner join Jobs.Jobs j on mjs.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
	left join Jobs.CustomerGroupCustomers cgc on c.customerID = cgc.CustomerId
	left join Jobs.CustomerGroups cg on cgc.CustomerGroupId = cg.Id
where
	mjs.year = @settlementYear
	and mjs.month = @settlementMonth

-- @tJobs carries processingFeePercent as the spine for the final insert
declare @tJobs table (jobId uniqueidentifier, customerName varchar(max), jobName varchar(80), processingFeePercent decimal(8,3))
insert into @tJobs(jobId, customerName, jobName, processingFeePercent) 
	select distinct j.jobId, coalesce(cg.CustomerGroupName, c.customerName), j.jobName, coalesce(j.ProcessingFeePercent, 3.5) / 100 from @tMJS a inner join Jobs.Jobs j on a.jobId = j.jobID inner join Jobs.Customers c on j.customerID = c.customerId left join Jobs.CustomerGroupCustomers cgc on c.customerID = cgc.CustomerId left join Jobs.CustomerGroups cg on cgc.CustomerGroupId = cg.Id where c.adnLoginID = 'teamspt52'
	union select distinct j.jobId, coalesce(cg.CustomerGroupName, c.customerName), j.jobName, coalesce(j.ProcessingFeePercent, 3.5) / 100 from @tCCCredits a inner join Jobs.Jobs j on a.jobId = j.jobID inner join Jobs.Customers c on j.customerID = c.customerId left join Jobs.CustomerGroupCustomers cgc on c.customerID = cgc.CustomerId left join Jobs.CustomerGroups cg on cgc.CustomerGroupId = cg.Id where c.adnLoginID = 'teamspt52'
	union select distinct j.jobId, coalesce(cg.CustomerGroupName, c.customerName), j.jobName, coalesce(j.ProcessingFeePercent, 3.5) / 100 from @tCCPayments a inner join Jobs.Jobs j on a.jobId = j.jobID inner join Jobs.Customers c on j.customerID = c.customerId left join Jobs.CustomerGroupCustomers cgc on c.customerID = cgc.CustomerId left join Jobs.CustomerGroups cg on cgc.CustomerGroupId = cg.Id where c.adnLoginID = 'teamspt52'
	union select distinct j.jobId, coalesce(cg.CustomerGroupName, c.customerName), j.jobName, coalesce(j.ProcessingFeePercent, 3.5) / 100 from @tCCAdminFees a inner join Jobs.Jobs j on a.jobId = j.jobID inner join Jobs.Customers c on j.customerID = c.customerId left join Jobs.CustomerGroupCustomers cgc on c.customerID = cgc.CustomerId left join Jobs.CustomerGroups cg on cgc.CustomerGroupId = cg.Id where c.adnLoginID = 'teamspt52'

declare @tFinalRawData table (customerName varchar(max), jobName varchar(80), sumCCPayments decimal(17,2), sumCCCredits decimal(17,2), sumCCProcessingFees decimal(17,2), TSICFees decimal(17,2), sumAdminCharges decimal(17,2), GrandTotal decimal(17,2))
insert into @tFinalRawData(customerName, jobName, sumCCPayments, sumCCCredits, sumCCProcessingFees, TSICFees, sumAdminCharges, GrandTotal)
select 
	tJobs.customerName,
	tJobs.jobName,

	coalesce(payments.sumCCPayments, 0) as sumCCPayments,
	coalesce(credits.sumCCCredits, 0) as sumCCCredits,
	-(
		convert(decimal(17, 2), 
			tJobs.processingFeePercent * (abs(coalesce(credits.sumCCCredits, 0)) 
			+ abs(coalesce(payments.sumCCPayments, 0)))
		)
	) as sumCCProcessingFees,
	-(coalesce(mjs.perPlayerCharge, 0) * coalesce(mjs.Count_NewPlayers_ThisMonth, 0)
	+ coalesce(mjs.perTeamCharge, 0) * coalesce(mjs.Count_NewTeams_ThisMonth, 0)) as TSICFees,
	coalesce(adminfees.sumAdminChargeAmount, 0) as AdminCharges,
	
	(
		+ coalesce(credits.sumCCCredits, 0) 
		+ coalesce(payments.sumCCPayments, 0) 
		- (coalesce(adminfees.sumAdminChargeAmount, 0))
		-
		(
			coalesce(mjs.perPlayerCharge, 0) * coalesce(mjs.Count_NewPlayers_ThisMonth, 0)
			+ coalesce(mjs.perTeamCharge, 0) * coalesce(mjs.Count_NewTeams_ThisMonth, 0)
		)
		-
		(
			convert(decimal(17, 2), 
				tJobs.processingFeePercent * (abs(coalesce(credits.sumCCCredits, 0)) 
				+ abs(coalesce(payments.sumCCPayments, 0)))
			)
		)
	) as GrandTotal
from 
	@tJobs tJobs
	left join @tMJS mjs on tJobs.jobId = mjs.jobId
	left join @tCCCredits as credits on tJobs.jobId = credits.jobId
	left join @tCCPayments as payments on tJobs.jobId = payments.jobId
	left join @tCCAdminFees as adminfees on tJobs.jobId = adminfees.jobId

select 'QA Test: Grand Total Per Job'
select 
	customerName, 
	jobName, 
	sumCCPayments as sumCCPayments, 
	sumCCCredits as sumCCCredits, 
	sumCCProcessingFees as sumCCProcessingFees, 
	sumAdminCharges as sumAdminCharges, 
	TSICFees as TSICFees, 
	GrandTotal as GrandTotal
from
	@tFinalRawData t
order by
	t.customerName,
	t.jobName

select 'QA Test: Grand Total Per Customer'
select 
	customerName,
	sum(GrandTotal) as GrandTotal
from
	@tFinalRawData v1
group by
	v1.customerName
order by
	v1.customerName

select 'QA Test: Grand Total'
select sum(GrandTotal) as GrandTotal
from
(
	select 
		customerName,
		sum(GrandTotal) as GrandTotal
	from
		@tFinalRawData v1
	group by
		v1.customerName
) v1

GO

-- ---------------------------------------------------------------------
-- 7. [adn].[monthlycustomerrollups]  (1 edits)
-- ---------------------------------------------------------------------
/* 

exec adn.monthlycustomerrollups @jobId = '7D6B5A0B-1CCA-4B3A-92EE-8554C8075D66'  --test steps event

exec adn.monthlycustomerrollups @jobId = '7267cea5-2a49-40cc-9d64-b37b67095d7c'  --test non customergrouped event

exec adn.monthlycustomerrollups @jobId = '064F22B6-0686-4E79-8203-67A793A5F5FD'  --test tracy event

exec adn.monthlycustomerrollups @jobId = 'aa876e42-6f41-49ce-8834-52fe81bec7f8'  --text TOTB event

exec adn.monthlycustomerrollups @jobId = '4af8bf2e-6d40-421f-9159-2ec0c5971bbc' --THE PLAYERS SERIES:FALL 144 SHOWCASE 2023

exec adn.monthlycustomerrollups @jobId = '2d3ed83f-5027-42df-a728-b01cfbc4d68a' --LI YELLOW JACKETS:CLINICS AND LEAGUES 2023-2024


*/
ALTER procedure [adn].[monthlycustomerrollups]
(
		@jobID uniqueidentifier = '2f1e82d9-59b3-406f-9924-21b92b7cc6f1'
)
as

set nocount on


declare @customerId uniqueidentifier
select @customerId = j.customerId from Jobs.Jobs j where j.jobID = @jobID
declare @startDate datetime = dateadd(m,-1,convert(datetime2, convert(char(4), year(getdate())) + '-' + convert(char(2), month(getdate())) + '-01'))
declare @endDate datetime = dateadd(m, 1, @startDate)

declare @listCustomerGroupCustomers table (customerId uniqueidentifier)
insert into @listCustomerGroupCustomers values(@customerId)

declare @listSTEPSCustomerGroupCustomers table (customerId uniqueidentifier)
insert into @listSTEPSCustomerGroupCustomers values( 'ED573C65-944D-4E4E-8284-B41A8BA19286') --CPBLL
insert into @listSTEPSCustomerGroupCustomers values( 'C75B4C3D-A628-4AD8-B197-435921A5CF03') --G8
insert into @listSTEPSCustomerGroupCustomers values( '150DB9A4-0E6D-43D4-A7F8-03DFC740CCD1') --Lax For The Cure
insert into @listSTEPSCustomerGroupCustomers values( '122178D9-4BEF-4FC7-9CDC-7393EFC1966D') --Live Love Lax
insert into @listSTEPSCustomerGroupCustomers values( 'EC268F1B-AE4D-4646-8C57-D222EDB30FCA') --Maryland Cup
insert into @listSTEPSCustomerGroupCustomers values( 'F4EAAED0-8EB9-DE11-8905-00137250256D') --STEPS Lacrosse
insert into @listSTEPSCustomerGroupCustomers values( '0EA213CE-D975-4CC7-9312-88562DB6D8C5') --STEPS Lacrosse California
insert into @listSTEPSCustomerGroupCustomers values( '1C55D2CF-3D74-47C8-862C-533960F4C0CE') --USA Lacrosse
insert into @listSTEPSCustomerGroupCustomers values( 'B818D018-8F4B-43A5-98D6-DFD5F52E6E4C') --XPO

declare @customerIdInSTEPSGroup uniqueidentifier
select @customerIdInSTEPSGroup = scg.customerId
from
	@listSTEPSCustomerGroupCustomers scg
where
	scg.customerId = @customerId

if not  @customerIdInSTEPSGroup is null begin
	insert into @listCustomerGroupCustomers(customerId) select customerId from @listSTEPSCustomerGroupCustomers where customerId != @customerId
end 

declare @listYJTracyCustomerGroupCustomers table (customerId uniqueidentifier)
insert into @listYJTracyCustomerGroupCustomers values( 'ee92d2b8-209b-489c-83c3-de25ad83feb0') --dutchess
insert into @listYJTracyCustomerGroupCustomers values( '4bdb07a7-38a1-402f-90dc-305cb525c37d') --north
insert into @listYJTracyCustomerGroupCustomers values( 'f29428b1-65d4-404e-ae8d-87a859cac20f') --rockland
insert into @listYJTracyCustomerGroupCustomers values( 'c1a71a62-1607-4a3d-a74c-0a231ded05ca') --mid atlantic
insert into @listYJTracyCustomerGroupCustomers values( '72b90fd8-51ac-4f45-aff7-896e95577a1e') --Rivalry Challenge and Showcase

declare @customerIdInYJTracyGroup uniqueidentifier
select @customerIdInYJTracyGroup = scg.customerId
from
	@listYJTracyCustomerGroupCustomers scg
where
	scg.customerId = @customerId

if not  @customerIdInYJTracyGroup is null begin
	insert into @listCustomerGroupCustomers(customerId) select customerId from @listYJTracyCustomerGroupCustomers where customerId != @customerId
end 

declare @listTOTBCustomerGroupCustomers table (customerId uniqueidentifier)
insert into @listTOTBCustomerGroupCustomers values('4ff492dd-c51b-4f7b-bc93-4c6545d309a2') --Top of the Bay Lacrosse
insert into @listTOTBCustomerGroupCustomers values('be9ecdf1-6bb0-4560-acee-4ce4c127340b') --Live Love Lax Showcase
insert into @listTOTBCustomerGroupCustomers values('024415d9-8918-42c3-91d4-d85f0077d652') --Live Love Lax Showcase
--NOW ON ITS OWN insert into @listTOTBCustomerGroupCustomers values('ad297c3f-7668-408e-815e-91ccac3bbeac') --Music City Lacrosse

declare @customerIdinTOPBGroup uniqueidentifier
select @customerIdinTOPBGroup = scg.customerId
from
	@listTOTBCustomerGroupCustomers scg
where
	scg.customerId = @customerId

if not @customerIdinTOPBGroup is null begin
	insert into @listCustomerGroupCustomers(customerId) select customerId from @listTOTBCustomerGroupCustomers where customerId != @customerId
end

declare @ccRawData table(
	customerName varchar(80),
	customerId uniqueidentifier,
	jobName varchar(80),
	jobId uniqueidentifier,
	ccRevenue money,
	ccCharges money,
	ccRefunds money
)

insert into @ccRawData(
	customerName,
	customerId,
	jobName,
	jobId,
	ccRevenue,
	ccCharges,
	ccRefunds
)
select 
	c.customerName,
	c.customerId,
	j.jobName,
	j.jobId,
	sum(case when apm.paymentMethod = 'Credit Card Payment' then adn.[Settlement Amount] else 0 end) as ccRevenue,
	convert(money, round(sum(abs(adn.[Settlement Amount])) * coalesce(j.ProcessingFeePercent, 3.5) / 100, 2)) as ccCharges,
	sum(case when apm.paymentMethod = 'Credit Card Credit' then adn.[Settlement Amount] else 0 end) as ccRefunds
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join adn.vtxs adn on ra.adntransactionid = adn.[transaction id]
	inner join @listCustomerGroupCustomers cgc on c.customerId = cgc.customerId
where
	apm.paymentMethod in ('Credit Card Payment', 'Credit Card Credit')
	and ( adn.SettlementTS >= @startDate and adn.SettlementTS < @endDate)
	and not adn.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	j.ProcessingFeePercent

declare @checkRawData table(
	jobId uniqueidentifier,
	checkRevenue money
)

insert into @checkRawData(
	jobId,
	checkRevenue
)
select 
	r.jobId,
	sum(ra.payamt) as checkRevenue
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join @listCustomerGroupCustomers cgc on j.customerId = cgc.customerId
where
	apm.paymentMethod in ('Check Payment By Client', 'Check Payment By TSIC')
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and ra.active = 1
group by
	r.jobId

declare @jobLevelData table (
	customerName varchar(80),
	jobId uniqueidentifier,
	jobName varchar(80),
	checkRevenue money,
	ccRevenue money,
	ccCharges money,
	ccRefunds money,
	tsicFees money,
	netDeposit money,
	jobIdx int,
	customerIdx int
)
insert into @jobLevelData(
	customerName,
	jobId,
	jobName,
	ccRevenue,
	ccCharges,
	ccRefunds,
	tsicFees,
	netDeposit,
	jobIdx,
	customerIdx
)
select 
	rd.customerName,
	rd.jobId,
	rd.jobName,
	rd.ccRevenue,
	rd.ccCharges,
	ccRefunds,
	(mjs.Count_NewPlayers_ThisMonth * coalesce(j.perPlayerCharge, 0)) + (mjs.Count_NewTeams_ThisMonth * coalesce(j.perTeamCharge, 0)) as TSICFees,
	rd.ccRevenue 
		- rd.ccCharges 
		+ rd.ccRefunds
		-	
			(
				(mjs.Count_NewPlayers_ThisMonth * coalesce(j.perPlayerCharge, 0)) + (mjs.Count_NewTeams_ThisMonth * coalesce(j.perTeamCharge, 0))
			)
	as NetDeposit,
	ROW_NUMBER() over (order by rd.jobName),
	ROW_NUMBER() over (partition by rd.customerName order by rd.jobName) as customerIdx
from
	@ccRawData rd
	inner join Jobs.Jobs j on rd.jobId = j.jobId
	left join adn.Monthly_Job_Stats mjs on rd.jobId = mjs.jobID
where
	mjs.year = year(@startDate)
	and mjs.month = month(@startDate)

-- update job level data check revenue
update jld set checkRevenue = crd.checkRevenue
from
	@checkRawData crd
	inner join @jobLevelData jld on crd.jobId = jld.jobId

declare @customerLevelData table (
	customerName varchar(80),
	jobId uniqueidentifier,
	jobName varchar(80),
	checkRevenue money,
	ccRevenue money,
	ccCharges money,
	ccRefunds money,
	tsicFees money,
	netDeposit money,
	jobIdx int,
	customerIdx int
)
insert into @customerLevelData(
	customerName,
	jobName,
	checkRevenue,
	ccRevenue,
	ccCharges,
	ccRefunds,
	tsicFees,
	netDeposit,
	jobIdx,
	customerIdx
)
select 
	jld.customerName,
	jld.customerName +  ' CUSTOMER JOBS SUMMARY ' + convert(varchar, year(@startDate)) + '-' + right('00' + convert(varchar, month(@startDate)), 2) as jobName,
	sum(coalesce(jld.checkRevenue, 0)) as checkRevenue,
	sum(jld.ccRevenue) as ccRevenue,
	sum(jld.ccCharges) as ccCharges,
	sum(jld.ccRefunds) as ccRefunds,
	sum(jld.tsicFees) as tsicFees,
	sum(jld.netDeposit) as netDeposit,
	max(jld.jobIdx) as jobIdx,
	1000 as customerIdx
from
	@jobLevelData jld
group by
	jld.customerName

declare @customerGroupCustomerCount int
select @customerGroupCustomerCount = count(*) from @listCustomerGroupCustomers

declare @customerGroupLevelData table (
	customerName varchar(80),
	jobId uniqueidentifier,
	jobName varchar(80),
	checkRevenue money, 
	ccRevenue money,
	ccCharges money,
	ccRefunds money,
	tsicFees money,
	netDeposit money,
	jobIdx int,
	customerIdx int
)
if @customerGroupCustomerCount > 1 begin
	insert into @customerGroupLevelData(
		customerName,
		jobName,
		checkRevenue,
		ccRevenue,
		ccCharges,
		ccRefunds,
		tsicFees,
		netDeposit,
		jobIdx,
		customerIdx
	)
	select 
		'ALL CUSTOMERS SUMMARY ' + convert(varchar, year(@startDate)) + '-' + right('00' + convert(varchar, month(@startDate)), 2) as customerName,
		'' as jobName,
		sum(jld.checkRevenue) as checkRevenue,
		sum(jld.ccRevenue) as ccRevenue,
		sum(jld.ccCharges) as ccCharges,
		sum(jld.ccRefunds) as ccRefunds,
		sum(jld.tsicFees) as tsicFees,
		sum(jld.netDeposit) as netDeposit,
		1000 as jobIdx,
		1000 as customerIdx
	from
		@jobLevelData jld
end

declare @qaTest varchar(max)

select @qaTest = 'Monthly Revenues'
select 'QA Test: ' + @qaTest

if @customerGroupCustomerCount > 1 begin
	if @customerIdInSTEPSGroup is null begin
		select 
			customerName,
			jobName,
			format(coalesce(checkRevenue, 0), 'C2') as checkRevenue,
			format(ccRevenue, 'C2') as ccTotalRevenue,
			format(ccRefunds, 'C2') as ccRefunds,
			format(ccRevenue + ccRefunds, 'C2') as ccNetRevenue,
			format(ccCharges, 'C2') as ccFees,
			format(tsicFees, 'C') as tsicFees,
			format(netDeposit, 'C2') as netDeposit
		from
		(
			select * from @jobLevelData
			union all
			select * from @customerLevelData
			union all
			select * from @customerGroupLevelData
		) v1
		order by
			v1.jobIdx,
			v1.customerIdx
	end else begin
		select 
			customerName,
			jobName,
			format(coalesce(checkRevenue, 0), 'C2') as checkRevenue,
			format(ccRevenue, 'C2') as ccTotalRevenue,
			format(ccRefunds, 'C2') as ccRefunds,
			format(ccRevenue + ccRefunds, 'C2') as ccNetRevenue,
			format(ccCharges, 'C2') as ccFees,
			format(tsicFees, 'C') as tsicFees,
			format(netDeposit, 'C2') as netDeposit
		from
		(
			select * from @jobLevelData
			union all
			select * from @customerGroupLevelData
		) v1
		order by
			v1.jobIdx,
			v1.customerIdx
	end
end else begin
	select 
		jobName,
		format(coalesce(checkRevenue, 0), 'C2') as checkRevenue,
		format(ccRevenue, 'C2') as ccTotalRevenue,
		format(ccRefunds, 'C2') as ccRefunds,
		format(ccRevenue + ccRefunds, 'C2') as ccNetRevenue,
		format(ccCharges, 'C2') as ccFees,
		format(tsicFees, 'C') as tsicFees,
		format(netDeposit, 'C2') as netDeposit
	from
	(
		select * from @jobLevelData
		union all
		select * from @customerLevelData
		union all
		select * from @customerGroupLevelData
	) v1
	order by
		v1.jobIdx,
		v1.customerIdx
end

GO

-- ---------------------------------------------------------------------
-- 8. [adn].[MonthyQBPExport_Automated_Merch]  (1 edits)
-- ---------------------------------------------------------------------
ALTER procedure [adn].[MonthyQBPExport_Automated_Merch]
(
		@settlementMonth int = 1
	,	@settlementYear int = 2026
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

-- @ccRate removed; processingFeePercent carried per job in #txRawData
declare @defaultTSICStoreRate decimal(8,3) = 0.10
declare @rawTxId int 

CREATE TABLE #txRawData (
    Id INT IDENTITY(1, 1) PRIMARY KEY,
	[PaymentMethod] varchar(max),
	[Transaction Status] varchar(max),
	[SettlementTS] datetime,
	customerName varchar(max),
	customerGroup varchar(max),
	jobId uniqueidentifier,
	jobTSICStoreRate decimal(8,3),
	processingFeePercent decimal(8,3),  -- added; replaces @ccRate
	jobName varchar(max),
	PlusMinus money,
	Plus money,
	Minus money,
	SumFeeProductInBatchPlus money,
	SumFeeProductInBatchMinus money,
	[Transaction ID] varchar(max),
	[Invoice Number] varchar(max),
	[Invoice Description] varchar(max)
);

insert into #txRawData (
	[PaymentMethod],
	[Transaction Status],
	[SettlementTS],
	customerName,
	customerGroup,
	jobId,
	jobTSICStoreRate,
	processingFeePercent,
	jobName,
	Plus,
	Minus,
	PlusMinus,
	SumFeeProductInBatchPlus,
	SumFeeProductInBatchMinus,
	[Transaction ID],
	[Invoice Number],
	[Invoice Description]
)
select 
	apm.PaymentMethod,
	txs.[Transaction Status] as [Transaction Status],
	convert(datetime, replace([Txs].[Settlement Date Time], ' EDT', '')) as [SettlementTS],
	c.customerName + ' MERCH' as customerName,
	coalesce((select cg.customerGroupName from Jobs.CustomerGroupCustomers cgc inner join Jobs.CustomerGroups cg on cgc.CustomerGroupId = cg.Id where cgc.CustomerId = c.customerID), c.customerName + ' MERCH') as customerGroupName,
	j.jobId,
	coalesce(j.storeTSICRate, @defaultTSICStoreRate),
	coalesce(j.ProcessingFeePercent, 3.5) / 100,
	replace(coalesce(j.jobName_QBP, j.jobName), c.customerName, c.customerName + ' MERCH') + ' M' as jobName,
	case when txs.[Transaction Status] = 'Settled Successfully' then convert(money, [Txs].[Settlement Amount]) else 0 end as Plus,
	case when txs.[Transaction Status] =  'Credited' then -convert(money, [Txs].[Settlement Amount]) else 0 end as Minus,
	scba.Paid as PlusMinus,
	(select sum(scbs.FeeProduct) from stores.StoreCartBatchSkus scbs where scbs.StoreCartBatchId = scb.StoreCartBatchId and apm.paymentMethod = 'Credit Card Payment') as SumFeeProductInBatchPlus,
	(select sum(-scbs.FeeProduct) from stores.StoreCartBatchSkus scbs where scbs.StoreCartBatchId = scb.StoreCartBatchId and apm.paymentMethod = 'Credit Card Credit') as SumFeeProductInBatchMinus,
	txs.[Transaction ID],
	txs.[Invoice Number],
	txs.[Invoice Description]
from
	[adn].[txs] as [Txs]
	inner join stores.StoreCartBatchAccounting scba on txs.[Transaction ID] = scba.AdnTransactionId
	inner join stores.StoreCartBatches scb on scba.StoreCartBatchId = scb.StoreCartBatchId
	inner join stores.StoreCart sc on scb.StoreCartId = sc.StoreCartId
	inner join stores.Stores s on sc.StoreId = s.StoreId
	inner join reference.Accounting_PaymentMethods apm on scba.PaymentMethodId = apm.paymentMethodID
	inner join Jobs.Jobs j on s.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
where
	[Txs].[Settlement Date Time] like ('%' + left(datename(month, @firstDay), 3) + '-' + convert(varchar, @settlementYear) + '%')
	and [transaction status] in ('Settled Successfully', 'Credited')
	and charindex('_M', [Txs].[Invoice Number]) > 0

--CLIENTS LISTING
select @qaTest = @monthYear + ' M Clients'
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
select @qaTest = @monthYear + ' M Dailys'
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
select @qaTest = @monthYear + ' M Job-Dailys'
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
select @qaTest = @monthYear + ' M IIF-Payments'
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
			,	convert(varchar, max(ra.[StoreCartBatchAccountingId])) as [DOCNUM]
			,	'' as [MEMO]
			,	'N' as [CLEAR]
		from
			#txRawData txs
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
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
			,	'Liability Due MERCH Customers:' + 
					case 
						when charindex([Txs].[CustomerName], txs.jobName) > 0 then 
							(SELECT item FROM [dbo].[fnSplit] (txs.jobName,':') where ai = 1)
						else  [Txs].[CustomerName] 
					end as [ACCNT]
			,   txs.jobName as [NAME]
			,	'' as [CLASS]
			,	convert(varchar, -(sum([Txs].Plus))) as [AMOUNT]
			,	convert(varchar, max(ra.[StoreCartBatchAccountingId])) as [DOCNUM]
			,	'' as [MEMO]
			,	'N' as [CLEAR]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
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

/* iif data for credits */
select @qaTest = @monthYear + ' M IIF-Credits'
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
			,	convert(varchar, max(ra.[StoreCartBatchAccountingId])) as [DOCNUM]
			,	'N' as [CLEAR]
			,	'N' as [TOPRINT]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
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
			,	'Liability Due MERCH Customers:' + 
					case 
						when charindex([Txs].[CustomerName], txs.jobName) > 0 then 
							(SELECT item FROM [dbo].[fnSplit] (txs.jobName,':') where ai = 1)
						else  [Txs].[CustomerName] 
					end as [ACCNT]
			,   txs.jobName as [NAME]
			,	'' as [CLASS]
			,	convert(varchar, -(sum([Txs].Minus))) as [AMOUNT]
			,	convert(varchar, max(ra.[StoreCartBatchAccountingId])) as [DOCNUM]
			,	'N' as [CLEAR]
			,	'N' as [TOPRINT]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
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

--MERCH FEES
select @qaTest = @monthYear + ' MERCH-Fees'
select 'QA Test: ' + @qaTest

select [!TRNS], [TRNSID], [TRNSTYPE], [DATE], [ACCNT], [NAME], [AMOUNT], [DOCNUM], [MEMO], [CLEAR], [TOPRINT], [NAMEISTAXABLE], [ADDR1]
from
(
	select '' as [Settlement Day], '' as [Customer:Job], '!TRNS' as [!TRNS],'TRNSID' as [TRNSID],'TRNSTYPE' as [TRNSTYPE],'DATE' as [DATE],'ACCNT' as [ACCNT],'NAME' as [NAME],'AMOUNT' as [AMOUNT],'DOCNUM' as [DOCNUM],'MEMO' as [MEMO],'CLEAR' as [CLEAR],'TOPRINT' as [TOPRINT],'NAMEISTAXABLE' as [NAMEISTAXABLE],'ADDR1' as [ADDR1]
	union all
	select '' as [Settlement Day], '' as [Customer:Job], '!SPL' as [!TRNS],'SPLID' as [TRNSID],'TRNSTYPE' as [TRNSTYPE],'DATE' as [DATE],'ACCNT' as [ACCNT],'NAME' as [NAME],'AMOUNT' as [AMOUNT],'DOCNUM' as [DOCNUM],'MEMO' as [MEMO],'CLEAR' as [CLEAR],'QNTY' as [TOPRINT],'PRICE' as [NAMEISTAXABLE],'INVITEM' as [ADDR1]
	union all
	select '' as [Settlement Day], '' as [Customer:Job], '!ENDTRNS' as [!TRNS],'' as [TRNSID],'' as [TRNSTYPE],'' as [DATE],'' as [ACCNT],'' as [NAME],'' as [AMOUNT],'' as [DOCNUM],'' as [MEMO],'' as [CLEAR],'' as [TOPRINT],'' as [NAMEISTAXABLE],'' as [ADDR1]	
	union all
	select convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [Settlement Day],
			[Customer:Job], [!TRNS], [TRNSID], [TRNSTYPE], [DATE], [ACCNT], [NAME], [AMOUNT], [DOCNUM], [MEMO], [CLEAR], [TOPRINT], [NAMEISTAXABLE], [ADDR1]
	from
	(
		--line 1
		select
			txs.jobName as [Customer:Job],
			'TRNS' as [!TRNS],
			'' as [TRNSID],
			'STMT CHG' as [TRNSTYPE],
			convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [DATE],
			'Accounts Receivable' as [ACCNT],
			txs.jobName as [NAME],
			convert(varchar(max), txs.jobTSICStoreRate * ( convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0))))) as [AMOUNT],
			'' as [DOCNUM],
			txs.jobName as [MEMO],
			'N' as [CLEAR],
			'N' as [TOPRINT],
			'N' as [NAMEISTAXABLE],
			'' as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
		group by
			txs.jobName,
			txs.jobTSICStoreRate

		union all 

		--line 2
		select
			txs.jobName as [Customer:Job],
			'SPL' as [!TRNS],
			'' as [TRNSID],
			'STMT CHG' as [TRNSTYPE],
			convert(char(10), (select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [DATE],
			'Services/Sales:MERCH Fees' as [ACCNT],
			'' as [NAME],
			convert(varchar(max), txs.jobTSICStoreRate * ( -convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0))))) as [AMOUNT],
			'' as [DOCNUM],
			txs.jobName as [MEMO],
			'N' as [CLEAR],
			convert(varchar(max), -convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0)))) as [TOPRINT],
			convert(varchar(max), -txs.jobTSICStoreRate) as [NAMEISTAXABLE],
			'MERCH Fees' as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join Jobs.Jobs j on txs.jobId = j.jobId
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
		group by
			txs.jobName,
			txs.jobTSICStoreRate

		union all 

		--line 3
		select
			txs.jobName as [Customer:Job],
			'ENDTRNS' as [!TRNS],
			'' as [TRNSID],
			'' as [TRNSTYPE],
			'' as [DATE],
			'' as [ACCNT],
			'' as [NAME],
			'' as [AMOUNT],
			'' as [DOCNUM],
			'' as [MEMO],
			'' as [CLEAR],
			'' as [TOPRINT],
			'' as [NAMEISTAXABLE],
			'' as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
		group by
			txs.jobName
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

--CC MERCH PROCESSING FEES
select @rawTxId = min(id) from #txRawData
select @qaTest = @monthYear + ' M CC-Fees'
select 'QA Test: ' + @qaTest

select [!TRNS], [TRNSID], [TRNSTYPE], [DATE], [ACCNT], [NAME], [AMOUNT], [DOCNUM], [MEMO], [CLEAR], [TOPRINT], [NAMEISTAXABLE], [ADDR1]
from
(
	select '' as [Settlement Day], '' as [Customer:Job], '!TRNS' as [!TRNS],'TRNSID' as [TRNSID],'TRNSTYPE' as [TRNSTYPE],'DATE' as [DATE],'ACCNT' as [ACCNT],'NAME' as [NAME],'AMOUNT' as [AMOUNT],'DOCNUM' as [DOCNUM],'MEMO' as [MEMO],'CLEAR' as [CLEAR],'TOPRINT' as [TOPRINT],'NAMEISTAXABLE' as [NAMEISTAXABLE],'ADDR1' as [ADDR1]
	union all
	select '' as [Settlement Day], '' as [Customer:Job], '!SPL' as [!TRNS],'SPLID' as [TRNSID],'TRNSTYPE' as [TRNSTYPE],'DATE' as [DATE],'ACCNT' as [ACCNT],'NAME' as [NAME],'AMOUNT' as [AMOUNT],'DOCNUM' as [DOCNUM],'MEMO' as [MEMO],'CLEAR' as [CLEAR],'QNTY' as [TOPRINT],'PRICE' as [NAMEISTAXABLE],'INVITEM' as [ADDR1]
	union all
	select '' as [Settlement Day], '' as [Customer:Job], '!ENDTRNS' as [!TRNS],'' as [TRNSID],'' as [TRNSTYPE],'' as [DATE],'' as [ACCNT],'' as [NAME],'' as [AMOUNT],'' as [DOCNUM],'' as [MEMO],'' as [CLEAR],'' as [TOPRINT],'' as [NAMEISTAXABLE],'' as [ADDR1]	
	union all
	select convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [Settlement Day],
			[Customer:Job], [!TRNS], [TRNSID], [TRNSTYPE], [DATE], [ACCNT], [NAME], [AMOUNT], [DOCNUM], [MEMO], [CLEAR], [TOPRINT], [NAMEISTAXABLE], [ADDR1]
	from
	(
		--line 1
		select
			txs.jobName as [Customer:Job],
			'TRNS' as [!TRNS],
			'' as [TRNSID],
			'STMT CHG' as [TRNSTYPE],
			convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [DATE],
			'Accounts Receivable' as [ACCNT],
			txs.jobName as [NAME],
			convert(varchar(max), -convert(decimal(18,2), (sum(txs.Plus - txs.Minus) * txs.processingFeePercent))) as [AMOUNT],
			'' as [DOCNUM],
			'' as [MEMO],
			'N' as [CLEAR],
			'N' as [TOPRINT],
			'N' as [NAMEISTAXABLE],
			'' as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.jobName,
			txs.processingFeePercent

		union all 

		--line 2
		select
			txs.jobName as [Customer:Job],
			'SPL' as [!TRNS],
			'' as [TRNSID],
			'STMT CHG' as [TRNSTYPE],
			convert(char(10), (select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [DATE],
			'Services/Sales:MERCH Processing Fees' as [ACCNT],
			'' as [NAME],
			convert(varchar(max), -convert(decimal(18,2), (sum(txs.Plus - txs.Minus) * txs.processingFeePercent))) as [AMOUNT],
			'' as [DOCNUM],
			'' as [MEMO],
			'N' as [CLEAR],
			convert(varchar(max), -convert(decimal(18,2), sum(txs.Plus - txs.Minus))) as [TOPRINT],
			convert(varchar(max), -txs.processingFeePercent) as [NAMEISTAXABLE],
			'MERCH Processing Fees' as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.jobName,
			txs.processingFeePercent

		union all 

		--line 3
		select
			txs.jobName as [Customer:Job],
			'ENDTRNS' as [!TRNS],
			'' as [TRNSID],
			'' as [TRNSTYPE],
			'' as [DATE],
			'' as [ACCNT],
			'' as [NAME],
			'' as [AMOUNT],
			'' as [DOCNUM],
			'' as [MEMO],
			'' as [CLEAR],
			'' as [TOPRINT],
			'' as [NAMEISTAXABLE],
			'' as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
		group by
			txs.jobName
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

--MERCH RETAINERS
select @qaTest = @monthYear + ' M Retainers'
select 'QA Test: ' + @qaTest

select [!TRNS], [TRNSID], [TRNSTYPE], [DATE], [ACCNT], [NAME], [AMOUNT], [DOCNUM], [MEMO], [CLEAR], [TOPRINT], [NAMEISTAXABLE], [ADDR1]
from
(
	select '' as [Settlement Day], '' as [Customer:Job], '!TRNS' as [!TRNS],'TRNSID' as [TRNSID],'TRNSTYPE' as [TRNSTYPE],'DATE' as [DATE],'ACCNT' as [ACCNT],'NAME' as [NAME],'AMOUNT' as [AMOUNT],'DOCNUM' as [DOCNUM],'MEMO' as [MEMO],'CLEAR' as [CLEAR],'TOPRINT' as [TOPRINT],'NAMEISTAXABLE' as [NAMEISTAXABLE],'ADDR1' as [ADDR1]
	union all
	select '' as [Settlement Day], '' as [Customer:Job], '!SPL' as [!TRNS],'SPLID' as [TRNSID],'TRNSTYPE' as [TRNSTYPE],'DATE' as [DATE],'ACCNT' as [ACCNT],'NAME' as [NAME],'AMOUNT' as [AMOUNT],'DOCNUM' as [DOCNUM],'MEMO' as [MEMO],'CLEAR' as [CLEAR],'QNTY' as [TOPRINT],'PRICE' as [NAMEISTAXABLE],'INVITEM' as [ADDR1]
	union all
	select '' as [Settlement Day], '' as [Customer:Job], '!ENDTRNS' as [!TRNS],'' as [TRNSID],'' as [TRNSTYPE],'' as [DATE],'' as [ACCNT],'' as [NAME],'' as [AMOUNT],'' as [DOCNUM],'' as [MEMO],'' as [CLEAR],'' as [TOPRINT],'' as [NAMEISTAXABLE],'' as [ADDR1]	
	union all
	select convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [Settlement Day],
			[Customer:Job], [!TRNS], [TRNSID], [TRNSTYPE], [DATE], [ACCNT], [NAME], [AMOUNT], [DOCNUM], [MEMO], [CLEAR], [TOPRINT], [NAMEISTAXABLE], [ADDR1]
	from
	(
		--line 1
		select
			txs.jobName as [Customer:Job],
			'TRNS' as [!TRNS],
			'' as [TRNSID],
			'STMT CHG' as [TRNSTYPE],
			convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [DATE],
			'Accounts Receivable' as [ACCNT],
			txs.jobName as [NAME],
			convert(varchar(max), convert(
				decimal(18, 2), 
				-(
					txs.jobTSICStoreRate * ( convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0))))
					+
					convert(decimal(18,2), (sum(txs.Plus - txs.Minus) * txs.processingFeePercent))				
				)
			)) as [AMOUNT],
			'' as [DOCNUM],
			'' as [MEMO],
			'N' as [CLEAR],
			'N' as [TOPRINT],
			'N' as [NAMEISTAXABLE],
			'' as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.jobName,
			txs.jobTSICStoreRate,
			txs.processingFeePercent

		union all 

		--line 2
		select
			txs.jobName as [Customer:Job],
			'SPL' as [!TRNS],
			'' as [TRNSID],
			'STMT CHG' as [TRNSTYPE],
			convert(char(10), (select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [DATE],
			'Liability Due MERCH Customers:' + txs.customerName as [ACCNT],
			'' as [NAME],
			convert(varchar(max), convert(
				decimal(18, 2), 
				(
					txs.jobTSICStoreRate * ( convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0))))
					+
					convert(decimal(18,2), (sum(txs.Plus - txs.Minus) * txs.processingFeePercent))				
				)
			)) as [AMOUNT],
			'' as [DOCNUM],
			'' as [MEMO],
			'N' as [CLEAR],
			'' as [TOPRINT],
			convert(varchar(max), convert(
				decimal(18, 2), 
				(
					txs.jobTSICStoreRate * ( convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0))))
					+
					convert(decimal(18,2), (sum(txs.Plus - txs.Minus) * txs.processingFeePercent))				
				)
			)) as [NAMEISTAXABLE],
			'MERCH Retainer:' + txs.customerName as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.customerName,
			txs.jobName,
			txs.jobTSICStoreRate,
			txs.processingFeePercent

		union all 

		--line 3
		select
			txs.jobName as [Customer:Job],
			'ENDTRNS' as [!TRNS],
			'' as [TRNSID],
			'' as [TRNSTYPE],
			'' as [DATE],
			'' as [ACCNT],
			'' as [NAME],
			'' as [AMOUNT],
			'' as [DOCNUM],
			'' as [MEMO],
			'' as [CLEAR],
			'' as [TOPRINT],
			'' as [NAMEISTAXABLE],
			'' as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
		group by
			txs.jobName
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

--MERCH CHECKS
select @qaTest = @monthYear + ' M Checks'
select 'QA Test: ' + @qaTest

select 	
	[!VEND], [NAME], REFNUM, [TIMESTAMP], PRINTAS, ADDR1, ADDR2, ADDR3, ADDR4, ADDR5,
	VTYPE, CONT1, CONT2, PHONE1, PHONE2, FAXNUM, EMAIL, NOTE, TAXID, LIMIT, TERMS,
	NOTEPAD, SALUTATION, COMPANYNAME, FIRSTNAME, MIDINIT, LASTNAME,
	CUSTFLD1, CUSTFLD2, CUSTFLD3, CUSTFLD4, CUSTFLD5, CUSTFLD6, CUSTFLD7, CUSTFLD8,
	CUSTFLD9, CUSTFLD10, CUSTFLD11, CUSTFLD12, CUSTFLD13, CUSTFLD14, CUSTFLD15,
	[1099], [HIDDEN], DELCOUNT
from
(
	select 	
		'' as [Settlement Day], '' as [Customer:Job], 
		'!TRNS' as [!VEND], 'TRNSID' as [NAME], 'TRNSTYPE' as REFNUM, 'DATE' as [TIMESTAMP],
		'ACCNT' as PRINTAS, 'NAME' as ADDR1, 'CLASS' as ADDR2, 'AMOUNT' as ADDR3, 'DOCNUM' as ADDR4,
		'CLEAR' as ADDR5, 'TOPRINT' as VTYPE, 'MEMO' as CONT1, '' as CONT2, '' as PHONE1,
		'' as PHONE2, '' as FAXNUM, '' as EMAIL, '' as NOTE, '' as TAXID, '' as LIMIT,
		'' as TERMS, '' as NOTEPAD, '' as SALUTATION, '' as COMPANYNAME, '' as FIRSTNAME,
		'' as MIDINIT, '' as LASTNAME, '' as CUSTFLD1, '' as CUSTFLD2, '' as CUSTFLD3,
		'' as CUSTFLD4, '' as CUSTFLD5, '' as CUSTFLD6, '' as CUSTFLD7, '' as CUSTFLD8,
		'' as CUSTFLD9, '' as CUSTFLD10, '' as CUSTFLD11, '' as CUSTFLD12, '' as CUSTFLD13,
		'' as CUSTFLD14, '' as CUSTFLD15, '' as [1099], '' as [HIDDEN], '' as DELCOUNT
	union all
	select 	
		'' as [Settlement Day], '' as [Customer:Job], 
		'!SPL' as [!VEND], 'SPLID' as [NAME], 'TRNSTYPE' as REFNUM, 'DATE' as [TIMESTAMP],
		'ACCNT' as PRINTAS, 'NAME' as ADDR1, 'CLASS' as ADDR2, 'AMOUNT' as ADDR3, 'DOCNUM' as ADDR4,
		'CLEAR' as ADDR5, 'QNTY' as VTYPE, 'REIMBEXP' as CONT1, '' as CONT2, '' as PHONE1,
		'' as PHONE2, '' as FAXNUM, '' as EMAIL, '' as NOTE, '' as TAXID, '' as LIMIT,
		'' as TERMS, '' as NOTEPAD, '' as SALUTATION, '' as COMPANYNAME, '' as FIRSTNAME,
		'' as MIDINIT, '' as LASTNAME, '' as CUSTFLD1, '' as CUSTFLD2, '' as CUSTFLD3,
		'' as CUSTFLD4, '' as CUSTFLD5, '' as CUSTFLD6, '' as CUSTFLD7, '' as CUSTFLD8,
		'' as CUSTFLD9, '' as CUSTFLD10, '' as CUSTFLD11, '' as CUSTFLD12, '' as CUSTFLD13,
		'' as CUSTFLD14, '' as CUSTFLD15, '' as [1099], '' as [HIDDEN], '' as DELCOUNT
	union all
	select 	
		'' as [Settlement Day], '' as [Customer:Job], 
		'!ENDTRNS' as [!VEND], '' as [NAME], '' as REFNUM, '' as [TIMESTAMP],
		'' as PRINTAS, '' as ADDR1, '' as ADDR2, '' as ADDR3, '' as ADDR4,
		'' as ADDR5, '' as VTYPE, '' as CONT1, '' as CONT2, '' as PHONE1,
		'' as PHONE2, '' as FAXNUM, '' as EMAIL, '' as NOTE, '' as TAXID, '' as LIMIT,
		'' as TERMS, '' as NOTEPAD, '' as SALUTATION, '' as COMPANYNAME, '' as FIRSTNAME,
		'' as MIDINIT, '' as LASTNAME, '' as CUSTFLD1, '' as CUSTFLD2, '' as CUSTFLD3,
		'' as CUSTFLD4, '' as CUSTFLD5, '' as CUSTFLD6, '' as CUSTFLD7, '' as CUSTFLD8,
		'' as CUSTFLD9, '' as CUSTFLD10, '' as CUSTFLD11, '' as CUSTFLD12, '' as CUSTFLD13,
		'' as CUSTFLD14, '' as CUSTFLD15, '' as [1099], '' as [HIDDEN], '' as DELCOUNT
	union all
	select 
		convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [Settlement Day],
		[Customer:Job], [!VEND], [NAME], REFNUM, [TIMESTAMP], PRINTAS, ADDR1, ADDR2, ADDR3,
		ADDR4, ADDR5, VTYPE, CONT1, CONT2, PHONE1, PHONE2, FAXNUM, EMAIL, NOTE,
		'' as TAXID, '' as LIMIT, '' as TERMS, '' as NOTEPAD, '' as SALUTATION,
		'' as COMPANYNAME, '' as FIRSTNAME, '' as MIDINIT, '' as LASTNAME,
		'' as CUSTFLD1, '' as CUSTFLD2, '' as CUSTFLD3, '' as CUSTFLD4, '' as CUSTFLD5,
		'' as CUSTFLD6, '' as CUSTFLD7, '' as CUSTFLD8, '' as CUSTFLD9, '' as CUSTFLD10,
		'' as CUSTFLD11, '' as CUSTFLD12, '' as CUSTFLD13, '' as CUSTFLD14, '' as CUSTFLD15,
		'' as [1099], '' as [HIDDEN], '' as DELCOUNT
	from
	(
		--line 1
		select
			txs.customerGroup as [Customer:Job],
			'TRNS' as [!VEND],
			'' as [NAME],
			'CHECK' as [REFNUM],
			convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [TIMESTAMP],
			'Checking' as [PRINTAS],
			txs.customerGroup as [ADDR1],
			'' as [ADDR2],
			convert(varchar(max),
				-convert(decimal(18, 2), 
					convert(decimal(18, 2), (sum(txs.Plus + txs.Minus)))
					-
					(
						txs.jobTSICStoreRate * ( convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0))))
						+
						convert(decimal(18,2), (sum(txs.Plus - txs.Minus) * txs.processingFeePercent))				
					)
				)
			) as [ADDR3],
			'' as [ADDR4],
			'N' as [ADDR5],
			'N' as [VTYPE],
			'EFT Balance Due ' + txs.customerGroup as [CONT1],
			'' as CONT2, '' as PHONE1, '' as PHONE2, '' as FAXNUM, '' as EMAIL, '' as NOTE
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.customerGroup,
			txs.jobTSICStoreRate,
			txs.processingFeePercent

		union all 

		--line 2
		select
			txs.customerName as [Customer:Job],
			'SPL' as [!VEND],
			'' as [NAME],
			'CHECK' as [REFNUM],
			convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [TIMESTAMP],
			'Liability Due MERCH Customers:' + txs.customerName as [PRINTAS],
			txs.jobName as [ADDR1],
			'' as [ADDR2],
			convert(varchar(max),
				convert(decimal(18, 2), 
					convert(decimal(18, 2), (sum(txs.Plus + txs.Minus)))
					-
					(
						txs.jobTSICStoreRate * ( convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0))))
						+
						convert(decimal(18,2), (sum(txs.Plus - txs.Minus) * txs.processingFeePercent))				
					)
				)
			) as [ADDR3],
			'' as [ADDR4],
			'' as [ADDR5],
			'N' as [VTYPE],
			'' as [CONT1],
			'' as CONT2, '' as PHONE1, '' as PHONE2, 'N' as FAXNUM, 'N' as EMAIL, 'NOTHING' as NOTE
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.customerGroup,
			txs.customerName,
			txs.jobName,
			txs.jobTSICStoreRate,
			txs.processingFeePercent

		union all 

		--line 3
		select
			txs.jobName as [Customer:Job],
			'ENDTRNS' as [!VEND],
			'' as [NAME], '' as [REFNUM], '' as [TIMESTAMP], '' as [PRINTAS],
			'' as [ADDR1], '' as [ADDR2], '' as [ADDR3], '' as [ADDR4], '' as [ADDR5],
			'' as [VTYPE], '' as [CONT1], '' as CONT2, '' as PHONE1, '' as PHONE2,
			'' as FAXNUM, '' as EMAIL, '' as NOTE
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit')
			and not txs.[transaction status] in ('Declined', 'Voided')
		group by
			txs.jobName
	) v2 
) v1
order by 
		[Settlement Day]
	,	[Customer:Job]
	,	case [!VEND]
			when '!TRNS' then 1
			when '!SPL' then 2
			when '!ENDTRNS' then 3
			when 'TRNS' then 4
			when 'SPL' then 5
			when 'ENDTRNS' then 6
		end

drop table #txRawData

GO

-- ---------------------------------------------------------------------
-- 9. [adn].[rpt_invoice]  (4 edits)
-- ---------------------------------------------------------------------
ALTER procedure [adn].[rpt_invoice]
(
		@jobIDStr varchar(max) = '416d4fc4-6e4e-4051-b5a8-dff220bce104'
	,	@settlementYearStr char(4) = '2020'
	,	@settlementMonthStr varchar(2) = '3'
)
as
select 
	  v1.jobID
	, invoicePeriod
	, [MonthNo]
	, [PaymentDate]
	, Amount
	, RawAmount
	, [settlement amount]
	, Category
	, ccCharges
	, invoiceNumber
	, invoiceDateTSIC

	, venue
	, coalesce(j.perPlayerCharge, 0) as perPlayerFeeTSIC
	, coalesce(j.perTeamCharge, 0) as perTeamFeeTSIC

	, lastName
	, firstName

	, convert(varchar(max), member) as [Member]
	, member_id

	, RegID
	, OnlineRegDate
	, coalesce(mjs.Count_ActivePlayersToDate, 0) as Count_ActivePlayersToDate
	, coalesce(mjs.Count_NewPlayers_ThisMonth , 0) as Count_NewPlayers_ThisMonth
	, coalesce(mjs.Count_ActiveTeamsToDate, 0) as Count_ActiveTeamsToDate
	, coalesce(mjs.Count_NewTeams_ThisMonth , 0) as Count_NewTeams_ThisMonth 
	, coalesce(mjs.Count_ActivePlayersToDate_LastMonth, 0) as Count_ActivePlayersToDate_LastMonth
	, coalesce(mjs.Count_ActiveTeamsToDate_LastMonth, 0) as Count_ActiveTeamsToDate_LastMonth
	, j.JobTypeID
from
(
	(
		-- Player registrations
		select distinct j.jobID,
			  convert(varchar, year(coalesce(adn.settlementTS, ata.createdate))) + '-' + right('00' + coalesce(convert(varchar, month(coalesce(adn.settlementTS, ata.createdate))), convert(varchar, month(coalesce(adn.settlementTS, ata.createdate)))), 2) as invoicePeriod
			, coalesce(convert(varchar, month(adn.settlementTS)), convert(varchar, month(ata.[modified]))) as [MonthNo]
			, coalesce(adn.settlementTS, ata.[modified]) as [PaymentDate]
			, case pm.PaymentMethod
					when 'Credit Card Payment' then adn.[settlement amount]
					when 'Credit Card Credit' then adn.[settlement amount]
					else ata.payamt
				end as Amount
			, coalesce(adn.[settlement amount], ata.payamt) as RawAmount
			, adn.[settlement amount]
			, pm.PaymentMethod as Category
			, case when (pm.PaymentMethod = 'Credit Card Payment' or pm.paymentMethod='Credit Card Credit') and adn.[settlement amount] != 0
				then case when j.JobTypeID = 2
						then coalesce(j.ProcessingFeePercent, 3.5) / 100
						else coalesce(j.ProcessingFeePercent, 3.5) / 100
					 end * abs(adn.[settlement amount])
				else 0
			  end as ccCharges
			, coalesce(adn.[Invoice Number], ata.[checkNo], '') as invoiceNumber
			, coalesce(adn.[SettlementTS], ata.[modified], '') as invoiceDateTSIC
			, c.customerName + ':' + j.jobName as venue
			, u.LastName as lastName
			, u.FirstName as firstName
			, u.FirstName + ' ' + u.LastName + ' (ID:' + u.UserName + ')' as member
			, u.userName as member_id
			, case 
				when coalesce(adn.[Invoice Number], ata.[checkNo], '') = '' then '' 
				else substring(coalesce(adn.[Invoice Number], ata.[checkNo], ''), CHARINDEX('_', coalesce(adn.[Invoice Number], ata.[checkNo], ''), CHARINDEX('_', coalesce(adn.[Invoice Number], ata.[checkNo], '')) + 1) + 1, LEN(coalesce(adn.[Invoice Number], ata.[checkNo], '')) - CHARINDEX('_', coalesce(adn.[Invoice Number], ata.[checkNo], ''), CHARINDEX('_', coalesce(adn.[Invoice Number], ata.[checkNo], '')) + 1))
			  end as RegID
			, convert(char(10), at.RegistrationTS, 112) as OnlineRegDate
			, ata.adnTransactionID
		from
			Jobs.Registrations at
			inner join Jobs.Jobs j on at.jobID = j.jobID
			inner join Jobs.customers c on j.customerID = c.customerID
			inner join Jobs.Registration_Accounting ata on 
				at.RegistrationID = ata.RegistrationID 
				and ata.active = 1
				and ata.teamID is null
			inner join reference.Accounting_PaymentMethods pm on ata.[PaymentMethodID] = pm.[PaymentMethodID]
			inner join dbo.AspNetUsers u on at.userID = u.Id
			left join adn.vtxs adn on 
				convert(varchar(max), ata.adntransactionid) = adn.[transaction id]
				and not adn.[transaction status] in ('Declined', 'Voided')
		where
			convert(varchar(max), j.jobID) like @jobIDStr
			and coalesce(convert(varchar, year(adn.settlementTS)), convert(varchar, year(ata.[modified]))) like @settlementYearStr
			and coalesce(convert(varchar, month(adn.settlementTS)), convert(varchar, month(ata.[modified]))) like @settlementMonthStr
			and coalesce(ata.payamt, 0) != 0
			and 
			(
				case 
					when pm.paymentMethodID = '30ECA575-A268-E111-9D56-F04DA202060D' then
						case 
							when adn.[Invoice Number] is null then 0
							else 1 
						end
					else 1	
				end					
			) = 1
	) 

	union all
	(
		-- Team registrations
		select distinct j.jobID, 
			  convert(varchar, year(coalesce(adn.[settlementts], ja.[createdate]))) + '-' + right('00' + coalesce(convert(varchar, month(coalesce(adn.[settlementts], ja.[createdate]))), convert(varchar, month(coalesce(adn.[settlementts], ja.[createdate])))), 2) as invoicePeriod
			, coalesce(convert(varchar, month(coalesce(adn.[settlementts], ja.[createdate]))), convert(varchar, month(coalesce(adn.[settlementts], ja.[createdate])))) as [MonthNo]
			, coalesce(adn.settlementTS, ja.[createdate]) as [PaymentDate]
			, case pm.PaymentMethod
					when 'Credit Card Payment' then ja.payamt
					when 'Credit Card Credit' then ja.payamt
					else ja.payamt
				end as Amount
			, ja.payamt as RawAmount
			, ja.payamt as [settlement amount]
			, pm.PaymentMethod as Category
			, case when (pm.PaymentMethod = 'Credit Card Payment' or pm.paymentMethod='Credit Card Credit') and ja.payamt != 0
				then case when j.JobTypeID = 2
						then coalesce(j.ProcessingFeePercent, 3.5) / 100
						else coalesce(j.ProcessingFeePercent, 3.5) / 100
					 end * abs(ja.payamt)
				else 0
			  end as ccCharges
			, coalesce(adn.[Invoice Number], ja.[checkNo], '') as invoiceNo
			, coalesce(adn.[SettlementTS], ja.[modified], '') as invoiceDateTSIC
			, c.customerName + ':' + j.jobName as venue
			, adn.Registrant_lastName as lastName
			, adn.Registrant_firstName as firstName
			, coalesce(tClub.customerName, '') + ':' + ag.agegroupname + ':' + teamName as member
			, null as member_id
			, convert(varchar, ja.aID) as RegID
			, convert(char(10), coalesce(ja.createdate, adn.settlementts), 112) as OnlineRegDate
			, ja.adnTransactionID
		from
			Jobs.customers c
			inner join Jobs.jobs j on c.customerid = j.customerid
			inner join reference.jobtypes jt on j.jobtypeid = jt.jobtypeid
			inner join Jobs.Registrations r on j.jobID = r.jobID
			inner join Jobs.Registration_Accounting ja on r.RegistrationID = ja.RegistrationID
			left join adn.txs txs on ja.adnTransactionID = txs.[Transaction ID]
			inner join Leagues.teams t on 
				ja.teamID = t.teamID
				and ja.active = 1
			inner join Leagues.agegroups ag on t.agegroupid = ag.agegroupid
			left join Jobs.Customers tClub on t.customerID = tClub.customerID
			left join adn.vTxs adn on 
				ja.adnTransactionID = adn.[transaction id]
				and not adn.[transaction status] in ('Declined', 'Voided')
			left join reference.Accounting_PaymentMethods pm on ja.[PaymentMethodID] = pm.[PaymentMethodID]			
		where 
			convert(varchar(max), j.jobID) like @jobIDStr
			and convert(varchar, year(coalesce(adn.settlementts, ja.createdate))) like @settlementYearStr
			and convert(varchar, month(coalesce(adn.settlementts, ja.createdate))) like @settlementMonthStr
			and 
			(
				case 
					when pm.paymentMethodID = '30ECA575-A268-E111-9D56-F04DA202060D' then
						case 
							when adn.[Invoice Number] is null then 0
							else 1 
						end
					else 1	
				end					
			) = 1
	)	
) v1
inner join Jobs.Jobs j on v1.jobID = j.jobID
left join adn.Monthly_Job_Stats mjs on v1.jobID = mjs.jobID and CONVERT(int, @settlementYearStr) = mjs.year and CONVERT(int, @settlementMonthStr) = mjs.month
where
	Category != 'Credit Card Void'
order by
		v1.invoicePeriod
	,	v1.venue
	,	v1.Category
	,	v1.[PaymentDate]
	,	v1.lastName
	,	v1.firstName

GO

-- ---------------------------------------------------------------------
-- 10. [utility].[cathycampsfeeupdate]  (5 edits)
-- ---------------------------------------------------------------------
/*
exec utility.cathycampsfeeupdate '13837f75-3e48-4561-a6cf-af692156072d', 'f46286e4-28a0-40ef-9bd7-7a6994129536' --camp 2 overnight
exec utility.cathycampsfeeupdate '13837f75-3e48-4561-a6cf-af692156072d', 'd278a07a-4ab5-40e9-b1f9-0472be260683' --camp 2 commuter
exec utility.cathycampsfeeupdate '13837f75-3e48-4561-a6cf-af692156072d', 'b438805b-4cd3-4dc4-bfd5-561fcd03b0ff' --camp 3 overnight
exec utility.cathycampsfeeupdate '13837f75-3e48-4561-a6cf-af692156072d', '0f932e9f-4e81-4d98-ba7e-89029882c228' --camp 3 commuter
*/
ALTER procedure [utility].[cathycampsfeeupdate] 
(
	@jobId uniqueidentifier = '13837f75-3e48-4561-a6cf-af692156072d',
	@agegroupId uniqueidentifier = 'd278a07a-4ab5-40e9-b1f9-0472be260683' --camp 2 commuter
)
as set nocount off
update r set
	fee_base = ag.teamFee,
	fee_processing = CAST(round((ag.teamFee * coalesce(j.ProcessingFeePercent, 3.5) / 100), 2) AS DECIMAL(10,2)),
	fee_total = r.fee_total + (ag.teamFee - ag.rosterFee) + (CAST(round((ag.teamFee * coalesce(j.ProcessingFeePercent, 3.5) / 100), 2) AS DECIMAL(10,2)) - (CAST(round((ag.rosterFee * coalesce(j.ProcessingFeePercent, 3.5) / 100), 2) AS DECIMAL(10,2)))) ,
	owed_total = r.owed_total + (ag.teamFee - ag.rosterFee) + (CAST(round((ag.teamFee * coalesce(j.ProcessingFeePercent, 3.5) / 100), 2) AS DECIMAL(10,2)) - CAST(round((ag.rosterFee * coalesce(j.ProcessingFeePercent, 3.5) / 100), 2) AS DECIMAL(10,2)))
from
	Jobs.Registrations r
	inner join Jobs.Jobs j on r.jobId = j.jobId
	inner join dbo.AspNetUsers u on r.UserId = u.Id
	inner join dbo.AspNetRoles roles on r.RoleId = roles.Id
	inner join Leagues.teams t on r.assigned_teamID = t.teamId
	inner join Leagues.agegroups ag on t.agegroupID = ag.agegroupID
where
	r.JobId = @jobId
	and ag.agegroupID = @agegroupId
	and r.bActive = 1
	and t.active = 1
	and r.fee_base = ag.rosterFee

GO
