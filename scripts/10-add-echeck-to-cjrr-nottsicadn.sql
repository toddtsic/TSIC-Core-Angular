-- =====================================================================
-- eCheck additions to CustomerJobRevenueRollups_NotTSICADN
-- =====================================================================
-- Companion to script #9. Re-defines [reporting].[CustomerJobRevenueRollups_NotTSICADN]
-- so its result-set shape matches the (post-#9) TSICADN variant exactly:
--
--   Set #1: Revenue rollup pivot (gains e-check pay-method columns when present)
--   Set #2: Monthly counts
--   Set #3: Admin fees
--   Set #4: CC records
--   Set #5: Check records
--   Set #6: AVAILABLE JOBS               -- legacy stops reading here
--   Set #7: E-Check records              -- new system reads this; empty when absent
--
-- Why the symmetry matters:
--   - Both legacy and the new system can hit BOTH SP variants
--   - Identical 7-set shape means the new repo needs no isTsicAdn branching
--     for result-set count
--   - Legacy still reads only 6 sets (its #6 is AvailableJobs in both variants)
--   - For non-TSIC merchants who don't process e-check, set #7 is empty —
--     correct semantics, no special-casing
--
-- Differences from script #9 (these reflect non-TSIC ADN reality):
--   - Payments come from Registration_Accounting (ra.payamt), NOT adn.vtxs.
--     Non-TSIC merchants have no visibility into TSIC's ADN settlement table.
--   - PayAmount is float (Registration_Accounting.payamt). Repo handles via
--     reader.GetDouble().
--   - NO CC processing fees rollup (block remains commented out, as before).
--     Non-TSIC merchants pay their own CC processing fees, not TSIC's.
--   - NO E-Check fees rollup, for the same reason.
--   - Failed E-Check Payments are filtered by 'Failed E-Check Payment' in
--     paymentMethod, same as script #9.
--
-- Idempotent ALTER PROCEDURE; safe to re-run.
-- =====================================================================


ALTER procedure [reporting].[CustomerJobRevenueRollups_NotTSICADN]
(
	@jobID uniqueidentifier = '2f1e82d9-59b3-406f-9924-21b92b7cc6f1',
	@startDate datetime,
	@endDate datetime,
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
	payment float
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
	payment
)
select
	c.customerName,
	c.customerId,
	j.jobName,
	j.jobId,
	year(ra.createdate),
	month(ra.createdate),
	replicate(' ', 4) + 'CC Payments',
	sum(ra.payamt)
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('Credit Card Payment')
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and ra.active = 1
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	year(ra.createdate),
	month(ra.createdate)

--insert cc credits
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
	replicate(' ', 3) + 'CC Credits',
	sum(ra.payamt)
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('Credit Card Credit')
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and ra.active = 1
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	year(ra.createdate),
	month(ra.createdate)

--insert echeck payments
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
	replicate(' ', 4) + 'E-Check Payments',
	sum(ra.payamt)
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('E-Check Payment')
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and ra.active = 1
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	year(ra.createdate),
	month(ra.createdate)

--insert failed echeck payments
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
	replicate(' ', 4) + 'Failed E-Check Payments',
	sum(ra.payamt)
from
	Jobs.Registration_Accounting ra
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationId = r.RegistrationId
	inner join Jobs.Jobs j on r.jobID = j.jobId
	inner join Jobs.Customers c on j.customerId = c.customerId
	inner join @listCustomerIds lc on j.customerID = lc.customerId
where
	apm.paymentMethod in ('Failed E-Check Payment')
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and ra.active = 1
group by
	c.customerId,
	c.customerName,
	j.jobId,
	j.jobName,
	year(ra.createdate),
	month(ra.createdate)

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

-- (intentionally omitted) CC Processing Fees rollup
-- (intentionally omitted) E-Check Processing Fees rollup
--   Non-TSIC ADN merchants pay their own processing fees; they don't surface
--   in TSIC's reporting. Mirrors the legacy behavior of this SP.

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

--EXPORT JOBREVENUERECORDS  (set #1)
select
	rd.jobName as JobName,
	rd.year as Year,
	rd.month as Month,
	paymentMethod as PayMethod,
	rd.payment as PayAmount
from
	@txRawData rd
	inner join Jobs.Jobs j on rd.jobID = j.jobId
where
	(
		exists(select * from @listJobs) and exists (select * from @listJobs lj where j.JobName = lj.jobName)
		or not exists(select * from @listJobs)
	)
order by
	rd.jobName,
	rd.year,
	rd.month,
	rd.paymentMethod

--EXPORT JOB NUMBERS  (set #2)
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

--EXPORT JOB ADMIN FEES  (set #3)
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
	ra.createdate as PaymentDate,
	year(ra.createdate),
	month(ra.createdate),
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
	apm.paymentMethod in ('Credit Card Payment')
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and ra.active = 1

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
	ra.createdate as PaymentDate,
	year(ra.createdate) as [Year],
	month(ra.createdate) as [Month],
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
	apm.paymentMethod in ('E-Check Payment', 'Failed E-Check Payment')
	and ( ra.createdate >= @startDate and ra.createdate < @endDate)
	and ra.active = 1

--EXPORT JOB CC RECORDS  (set #4)
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

--EXPORT JOB CHECK RECORDS  (set #5)
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

--EXPORT LIST AVAILABLE JOBS  (set #6 — legacy stops reading here)
select distinct j.JobName
from
	@listCustomerIds c
	inner join Jobs.Jobs j on c.customerId = j.customerID
where
	(ISNUMERIC(j.year) = 1)
	and convert(int, j.year) >= 2022
order by
	j.JobName

--EXPORT JOB ECHECK RECORDS  (set #7 — only the new system reads this)
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
GO
