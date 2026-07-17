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
	inner join adn.vtxs vtx on ra.adntransactionid = vtx.[transaction id]
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
    inner join adn.vtxs vtx 
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
	case when vtx.[Transaction Type] = 'echeck' then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end
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
	case when apm.PaymentMethod in ('E-Check Payment','Failed E-Check Payment') then coalesce(j.ECProcessingFeePercent, 1.5) / 100 else coalesce(j.ProcessingFeePercent, 3.5) / 100 end,
	replace(coalesce(j.jobName_QBP, j.jobName), c.customerName, c.customerName + ' MERCH') + ' M' as jobName,
	case when txs.[Transaction Status] = 'Settled Successfully' then convert(money, [Txs].[Settlement Amount]) else 0 end as Plus,
	case when txs.[Transaction Status] =  'Credited' then -convert(money, [Txs].[Settlement Amount]) else 0 end as Minus,
	scba.Paid as PlusMinus,
	(select sum(scbs.FeeProduct) from stores.StoreCartBatchSkus scbs where scbs.StoreCartBatchId = scb.StoreCartBatchId and apm.paymentMethod in ('Credit Card Payment','E-Check Payment')) as SumFeeProductInBatchPlus,
	(select sum(-scbs.FeeProduct) from stores.StoreCartBatchSkus scbs where scbs.StoreCartBatchId = scb.StoreCartBatchId and apm.paymentMethod in ('Credit Card Credit','Failed E-Check Payment')) as SumFeeProductInBatchMinus,
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
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
			txs.jobName + '|' + convert(varchar, txs.processingFeePercent) as [Customer:Job],
			'TRNS' as [!TRNS],
			'' as [TRNSID],
			'STMT CHG' as [TRNSTYPE],
			convert(char(10),(select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [DATE],
			'Accounts Receivable' as [ACCNT],
			txs.jobName as [NAME],
			convert(varchar(max), -convert(decimal(18,2), (sum((txs.Plus - txs.Minus) * txs.processingFeePercent)))) as [AMOUNT],
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.jobName,
			txs.processingFeePercent

		union all 

		--line 2
		select
			txs.jobName + '|' + convert(varchar, txs.processingFeePercent) as [Customer:Job],
			'SPL' as [!TRNS],
			'' as [TRNSID],
			'STMT CHG' as [TRNSTYPE],
			convert(char(10), (select dateadd(day, -1, dateadd(month, 1, DATETIMEFROMPARTS ( @settlementYear, @settlementMonth, 1, 0, 0, 0, 0 )))), 101) as [DATE],
			'Services/Sales:MERCH Processing Fees' as [ACCNT],
			'' as [NAME],
			convert(varchar(max), -convert(decimal(18,2), (sum((txs.Plus - txs.Minus) * txs.processingFeePercent)))) as [AMOUNT],
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.jobName,
			txs.processingFeePercent

		union all 

		--line 3
		select
			txs.jobName + '|' + convert(varchar, txs.processingFeePercent) as [Customer:Job],
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
			and not txs.[transaction status] in ('Declined', 'Voided')
		group by
			txs.jobName,
			txs.processingFeePercent
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
					convert(decimal(18,2), (sum((txs.Plus - txs.Minus) * txs.processingFeePercent)))				
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
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
			'Liability Due MERCH Customers:' + txs.customerName as [ACCNT],
			'' as [NAME],
			convert(varchar(max), convert(
				decimal(18, 2), 
				(
					txs.jobTSICStoreRate * ( convert(decimal(18,2), sum(coalesce(txs.SumFeeProductInBatchPlus, 0) + coalesce(txs.SumFeeProductInBatchMinus, 0))))
					+
					convert(decimal(18,2), (sum((txs.Plus - txs.Minus) * txs.processingFeePercent)))				
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
					convert(decimal(18,2), (sum((txs.Plus - txs.Minus) * txs.processingFeePercent)))				
				)
			)) as [NAMEISTAXABLE],
			'MERCH Retainer:' + txs.customerName as [ADDR1]
		from
			[#txRawData] as [Txs]
			inner join stores.StoreCartBatchAccounting ra on txs.[Transaction ID] = ra.adnTransactionID
		where
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.customerName,
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
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
						convert(decimal(18,2), (sum((txs.Plus - txs.Minus) * txs.processingFeePercent)))				
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.customerGroup,
			txs.jobTSICStoreRate

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
						convert(decimal(18,2), (sum((txs.Plus - txs.Minus) * txs.processingFeePercent)))				
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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
			and not txs.[transaction status] in ('Declined', 'Voided')
			and (not txs.SumFeeProductInBatchPlus is null or not txs.SumFeeProductInBatchMinus is null)
		group by
			txs.customerGroup,
			txs.customerName,
			txs.jobName,
			txs.jobTSICStoreRate

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
			txs.PaymentMethod in ('Credit Card Payment','Credit Card Credit','E-Check Payment','Failed E-Check Payment')
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

