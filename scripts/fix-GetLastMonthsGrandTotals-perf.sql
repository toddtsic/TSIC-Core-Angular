-- =====================================================================
-- [adn].[GetLastMonthsGrandTotals] - performance fix (2026-10-01)
--
-- Symptom: "4) Last Month's Grand Totals (Excel)" 500s with SQL Execution
-- Timeout (30s). On the 10-01 prod restore the proc ran 3m24s before being
-- killed.
--
-- Cause: the CC Credits and CC Payments queries joined adn.vTxs and filtered
-- on year()/month() of SettlementTS - a column the view string-parses out of
-- [Settlement Date Time] while left-joining 9 tables. Non-sargable, so every
-- run walked all of adn.Txs through the view, twice.
--
-- Fix: resolve last month's transaction IDs ONCE from the base table
-- (adn.Txs) into #monthTxs, using the same substring parse vTxs uses
-- (chars 4-6 = month abbrev, chars 8-11 = year), and join both queries to
-- that. vTxs contributed nothing but the date filter. Everything else is
-- unchanged, byte for byte.
--
-- Prefilter + both money queries measured 0.8s on the 10-01 prod restore.
-- =====================================================================
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

-- Last month's transactions, resolved once from the base table. Month abbrev
-- is mapped explicitly (not datename) so it is independent of session language.
declare @settlementMonthAbbrev nvarchar(3) = choose(@settlementMonth,
	'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec')

create table #monthTxs ([Transaction ID] nvarchar(100) not null primary key)
insert into #monthTxs([Transaction ID])
select t.[Transaction ID]
from adn.Txs t
where
	substring(t.[Settlement Date Time], 8, 4) = convert(nvarchar(4), @settlementYear)
	and substring(t.[Settlement Date Time], 4, 3) = @settlementMonthAbbrev

declare @tCCCredits table(customerName varchar(80), jobId uniqueidentifier, jobName varchar(80), sumCCCredits decimal(17,2))
insert into @tCCCredits(customerName, jobId, jobName, sumCCCredits)
select 
	c.customerName, 
	j.jobId,
	j.jobName, 
	sum(ra.payamt) as sumCCCredits
from
	Jobs.Registration_Accounting ra
	inner join #monthTxs txs on ra.adnTransactionID = txs.[Transaction ID]
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationID = r.RegistrationID
	inner join Jobs.Jobs j on r.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
	inner join adn.Monthly_Job_Stats mjs on 
		j.jobId = mjs.jobID 
		and mjs.year = @settlementYear
		and mjs.month = @settlementMonth
where
	c.adnLoginID = 'teamspt52'
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
	inner join #monthTxs txs on ra.adnTransactionID = txs.[Transaction ID]
	inner join reference.Accounting_PaymentMethods apm on ra.paymentMethodID = apm.paymentMethodID
	inner join Jobs.Registrations r on ra.RegistrationID = r.RegistrationID
	inner join Jobs.Jobs j on r.jobId = j.jobId
	inner join Jobs.Customers c on j.customerID = c.customerID
	inner join adn.Monthly_Job_Stats mjs on 
		j.jobId = mjs.jobID 
		and mjs.year = @settlementYear
		and mjs.month = @settlementMonth
where
	c.adnLoginID = 'teamspt52'
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
