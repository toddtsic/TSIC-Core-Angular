-- =============================================================================
-- adn.MonthyQBPExport_Automated_EcheckReturns
-- =============================================================================
-- The eCheck RETURNS file: negative-line mirror of adn.MonthyQBPExport_Automated.
--
-- Source rows: adn.Txs with [Transaction Status] = 'Returned Item' — written by the
-- month-end import when Authorize.Net reports a bounced eCheck. Each is its OWN
-- transaction, dated the day the bank said no ([Settlement Date Time] = return-day
-- batch), carrying [Reference_Transaction_ID] = the original draft it reverses.
-- The import's paired-settlement guard means every row here has its +amount
-- counterpart in adn.Txs (this month or a prior close) — an orphan negative is
-- excluded upstream and never reaches this file.
--
-- Accounting shape mirrors the Credits (refund) section of the payments sproc:
--   TRNS  Checking                        -amount   (money left our bank)
--   SPL   Liability Due to Customers:<X>  +amount   (we owe that client less)
-- so a bounce posting after a month closed nets the client's NEXT remittance.
-- DOCNUM = the reversal Registration_Accounting aID (the Failed eCheck ledger row
-- the sweep/backstop wrote, which carries the return's transaction id).
--
-- Merch (_M) is omitted, matching the reg sproc; the store checkout does not offer
-- eCheck. Job_Monthly_Stats is deliberately NOT updated here.
--
-- TO DEPLOY (Todd): review, then run against TSICV5 (and prod at cutover).
-- The API degrades gracefully while this sproc is absent (close ships without the
-- returns file and logs an error) — but the clawback only exists once this runs.
-- =============================================================================

CREATE procedure [adn].[MonthyQBPExport_Automated_EcheckReturns]
(
		@settlementMonth int = 11
	,	@settlementYear int = 2023
)
as
set nocount on

declare @monthYear varchar(20) = convert(varchar, @settlementMonth) + '-' + convert(varchar, @settlementYear)
declare @firstDay date = convert(varchar, @settlementMonth) + '/' + '01/' + convert(varchar, @settlementYear)
declare @qaTest varchar(max)

IF OBJECT_ID(N'tempdb..#retRawData') IS NOT NULL
BEGIN
	DROP TABLE #retRawData
END

select *
into #retRawData
from
(
	select
		txs.[Transaction Status] as [Transaction Status],
		convert(datetime, replace([Txs].[Settlement Date Time], ' EDT', '')) as [SettlementTS],
		c.customerName,
		j.jobId,
		coalesce(j.jobName_QBP, j.jobName) as jobName,
		-convert(money, [Txs].[Settlement Amount]) as Minus,
		txs.[Transaction ID],
		txs.[Reference_Transaction_ID],
		txs.[Invoice Number]
	from
		[adn].[txs] as [Txs]
		inner join Jobs.Jobs j on (SELECT item FROM [dbo].[fnSplit]([Txs].[Invoice Number], '_') where ai = 2) = j.jobAI
		inner join Jobs.Customers c on j.customerID = c.customerID
	where
		[Txs].[Settlement Date Time] like ('%' + left(datename(month, @firstDay), 3) + '-' + convert(varchar, @settlementYear) + '%')
		and [transaction status] = 'Returned Item'
		and charindex('_M', [Txs].[Invoice Number]) = 0  --OMIT MERCH PURCHASES
) v1

--RETURNS PAIRING (audit: every return traces to a settled original)
select @qaTest = @monthYear + ' Returns-Pairing'
select 'QA Test: ' + @qaTest

select
		r.[customerName] + ':' + r.[jobName] as [Customer:Job]
	,	r.[Transaction ID] as [Return Tx]
	,	r.[Reference_Transaction_ID] as [Original Tx]
	,	orig.[Settlement Date Time] as [Original Settled]
	,	convert(char(10), r.[SettlementTS], 101) as [Returned Day]
	,	-r.Minus as [Amount]
	,	case when orig.[Transaction ID] is null then 'MISSING ORIGINAL - INVESTIGATE' else 'OK' end as [Pairing]
from
	#retRawData r
	left join [adn].[txs] orig
		on orig.[Transaction ID] = r.[Reference_Transaction_ID]
		and orig.[Transaction Status] = 'Settled Successfully'
order by
		r.[customerName]
	,	r.[jobName]
	,	r.[SettlementTS]

--RETURNS BY DATE AND VENUE
select @qaTest = @monthYear + ' Returns-Dailys'
select 'QA Test: ' + @qaTest

select
		[Ret].jobName as [Customer:Job]
	,	convert(char(10), [Ret].[SettlementTS], 101) as [Returned Day]
	,	count(*) as [Returns]
	,	sum([Ret].Minus) as [Clawback]
from
	[#retRawData] as [Ret]
group by
	[Ret].jobName,
	convert(char(10), [Ret].[SettlementTS], 101)
order by
	[Ret].jobName,
	convert(char(10), [Ret].[SettlementTS], 101)

/* iif data for echeck returns */
select @qaTest = @monthYear + ' IIF-EcheckReturns'
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
				convert(char(10), [Ret].[SettlementTS], 101) as [Settlement Day]
			,   ret.jobName as [Customer:Job]
			,	'TRNS' as [!TRNS]
			,	'' as [TRNSID]
			,	'CHECK' as [TRNSTYPE]
			,	convert(char(10), [Ret].[SettlementTS], 101) as [DATE]
			,	'Checking' as [ACCNT]
			,   ret.jobName as [NAME]
			,	'' as [CLASS]
			,	convert(varchar, (sum([Ret].Minus))) as [AMOUNT]
			,	convert(varchar, max(ra.[aID])) as [DOCNUM]
			,	'eCheck returned item' as [MEMO]
			,	'N' as [CLEAR]
		from
			#retRawData ret
			inner join Jobs.Registration_Accounting ra on ret.[Transaction ID] = ra.adnTransactionID
		group by
			convert(char(10), [Ret].[SettlementTS], 101),
			ret.customerName,
			ret.jobName

		union all

		select
				convert(char(10), [Ret].[SettlementTS], 101) as [Settlement Day]
			,   ret.jobName as [Customer:Job]
			,	'SPL' as [!TRNS]
			,	'' as [TRNSID]
			,	'CHECK' as [TRNSTYPE]
			,	convert(char(10), [Ret].[SettlementTS], 101) as [DATE]
			,	'Liability Due to Customers:' +
					case
						when charindex([Ret].[CustomerName], ret.jobName) > 0 then
							(SELECT item FROM [dbo].[fnSplit] (ret.jobName,':') where ai = 1)
						else  [Ret].[CustomerName]
					end as [ACCNT]
			,   ret.jobName as [NAME]
			,	'' as [CLASS]
			,	convert(varchar, -(sum([Ret].Minus))) as [AMOUNT]
			,	convert(varchar, max(ra.[aID])) as [DOCNUM]
			,	'eCheck returned item' as [MEMO]
			,	'N' as [CLEAR]
		from
			[#retRawData] as [Ret]
			inner join Jobs.Registration_Accounting ra on ret.[Transaction ID] = ra.adnTransactionID
		group by
			convert(char(10), [Ret].[SettlementTS], 101),
			ret.customerName,
			ret.jobName

		union all

		select
				convert(char(10), [Ret].[SettlementTS], 101) as [Settlement Day]
			,   ret.jobName as [Customer:Job]
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
			[#retRawData] as [Ret]
		group by
			convert(char(10), [Ret].[SettlementTS], 101),
			ret.customerName,
			ret.jobName
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
drop table #retRawData
