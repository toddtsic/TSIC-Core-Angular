-- =====================================================================
-- eCheck additions to CustomerJobRevenueRollups
-- =====================================================================
-- Builds on top of script #8 (which fixed the ProcessingFeePercent unit
-- bug). Re-defines [reporting].[CustomerJobRevenueRollups] to add:
--   - 3 new rollup INSERTs into @txRawData:
--       E-Check Payments        (4-space indent, sorts after CC Payments)
--       Failed E-Check Payments (4-space indent, sorts after E-Check Payments)
--       E-Check Fees            (2-space indent, sorts after CC Fees)
--   - 1 new INSERT into @rawPaymentRecords for E-Check details
--   - 1 new SELECT result set #6 (JOB ECHECK RECORDS)
--
-- Rollup pivot (Syncfusion PivotView, payMethod as column dimension)
-- automatically displays the new pay-method values as new columns -
-- no frontend pivot config change required.
--
-- Result-set ordering shifts: Available Jobs moves from #6 to #7.
-- Backend repository updated in lockstep - see CustomerJobRevenueRepository.cs.
--
-- E-Check Fees apply ONLY to positive E-Check Payments (filter:
-- payment > 0). Per business rule: ADN does NOT refund the original
-- 1.5% transaction fee on an NSF return, so no negative fee row is
-- generated against Failed E-Check Payments - the merchant eats it.
--
-- Idempotent ALTER PROCEDURE; safe to re-run.
-- NOTE: this script SUPERSEDES script #8's CJRR ALTER. Re-running #8
-- after this would revert the eCheck additions.
-- =====================================================================


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

--insert echeck payments
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
	replicate(' ', 4) + 'E-Check Payments',
	sum(adn.[Settlement Amount]),
	coalesce(j.ECProcessingFeePercent, 1.5) / 100
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join adn.vtxs adn on ra.adntransactionid = adn.[transaction id]
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('E-Check Payment')
	and ( adn.SettlementTS >= @startDate and adn.SettlementTS < @endDate)
	and not adn.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	j.ECProcessingFeePercent,
	year(adn.SettlementTS),
	month(adn.SettlementTS)

--insert failed echeck payments (NSF returns; merchant eats original fee, no fee credit row)
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
	replicate(' ', 4) + 'Failed E-Check Payments',
	sum(adn.[Settlement Amount]),
	coalesce(j.ECProcessingFeePercent, 1.5) / 100
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join adn.vtxs adn on ra.adntransactionid = adn.[transaction id]
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('Failed E-Check Payment')
	and ( adn.SettlementTS >= @startDate and adn.SettlementTS < @endDate)
	and not adn.[transaction status] in ('Declined', 'Voided')
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	j.ECProcessingFeePercent,
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

-- insert E-Check Processing Fees (only on positive eCheck payments - ADN doesn't refund the fee on NSF)
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
	replicate(' ', 2) + 'E-Check Fees',
	-(
		abs(
			rd.payment * rd.processingFeePercent
		)
	)
from
	@txRawData rd
where
	rd.paymentMethod like '%E-Check%'
	and rd.payment > 0

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
	apm.paymentMethod in ('E-Check Payment', 'Failed E-Check Payment')
	and ( adn.SettlementTS >= @startDate and adn.SettlementTS < @endDate)
	and not adn.[transaction status] in ('Declined', 'Voided')

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

--EXPORT JOB ECHECK RECORDS
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
	PaymentMethod in ('E-Check Payment', 'Failed E-Check Payment')
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
