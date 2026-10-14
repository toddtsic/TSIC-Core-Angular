using System.Globalization;
using TSIC.Contracts.Dtos;
using SheetData = TSIC.API.Services.Shared.Files.ExcelSheet;

namespace TSIC.API.Services.Reporting;

// The month-end close: the sheets adn.MonthyQBPExport_Automated (registrations) and
// adn.MonthyQBPExport_Automated_Merch (store sales) return, built from sets already read. The
// QuickBooks .iif files are cut from these sheets, so every cell is the text the procedure wrote.
public sealed partial class ReportingService
{
    private static readonly string[] MonthEndCardMethods =
        ["Credit Card Payment", "Credit Card Credit", "E-Check Payment", "Failed E-Check Payment"];

    private static readonly string[] MonthEndChargeColumns =
        ["!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "TOPRINT", "NAMEISTAXABLE", "ADDR1"];

    private static readonly string[] MonthEndCheckColumns =
    [
        "!VEND", "NAME", "REFNUM", "TIMESTAMP", "PRINTAS", "ADDR1", "ADDR2", "ADDR3", "ADDR4", "ADDR5",
        "VTYPE", "CONT1", "CONT2", "PHONE1", "PHONE2", "FAXNUM", "EMAIL", "NOTE", "TAXID", "LIMIT", "TERMS",
        "NOTEPAD", "SALUTATION", "COMPANYNAME", "FIRSTNAME", "MIDINIT", "LASTNAME",
        "CUSTFLD1", "CUSTFLD2", "CUSTFLD3", "CUSTFLD4", "CUSTFLD5", "CUSTFLD6", "CUSTFLD7", "CUSTFLD8",
        "CUSTFLD9", "CUSTFLD10", "CUSTFLD11", "CUSTFLD12", "CUSTFLD13", "CUSTFLD14", "CUSTFLD15",
        "1099", "HIDDEN", "DELCOUNT",
    ];

    /// <summary>Text compared the way the database compares it: case and trailing spaces ignored.</summary>
    private static string MonthEndKey(string? text) => text == null ? "\u0000" : text.TrimEnd(' ').ToUpperInvariant();

    /// <summary>
    /// Text ordered the way the database orders a NON-Unicode (varchar) column, which is what the
    /// procedures' working tables hold: character by character, punctuation (in code order)
    /// before digits before letters, case ignored, a shorter text padded with spaces. Null first.
    /// </summary>
    private static readonly Comparer<string?> SqlVarcharOrder = Comparer<string?>.Create((x, y) =>
    {
        if (x == null || y == null)
        {
            return (x == null ? 0 : 1) - (y == null ? 0 : 1);
        }

        static int Weight(char c) => c switch
        {
            >= 'a' and <= 'z' => 2000 + (c - 'a'),
            >= 'A' and <= 'Z' => 2000 + (c - 'A'),
            >= '0' and <= '9' => 1000 + c,
            < (char)128 => c,
            _ => 3000 + c,
        };

        var length = Math.Max(x.Length, y.Length);
        for (var i = 0; i < length; i++)
        {
            var difference = Weight(i < x.Length ? x[i] : ' ') - Weight(i < y.Length ? y[i] : ' ');
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    });

    private static decimal MonthEndCents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>A number at a fixed count of decimal places, as the procedures write one into a text column; zero is never signed.</summary>
    private static string? MonthEndFixed(decimal? value, int places) =>
        value == null
            ? null
            : (Math.Round(value.Value, places, MidpointRounding.AwayFromZero) + 0m)
                .ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>A two-decimal amount as the procedures write it into a text column.</summary>
    private static string? MonthEndAmount(decimal? value) => MonthEndFixed(value, 2);

    /// <summary>
    /// The n-th item (1-based) of a delimited text the way dbo.fnSplit numbers them: items are
    /// trimmed of spaces and empty ones are not counted. Null when there is no such item.
    /// </summary>
    private static string? MonthEndSplitItem(string? text, char delimiter, int index)
    {
        if (text == null)
        {
            return null;
        }

        var seen = 0;
        foreach (var part in text.Split(delimiter))
        {
            var item = part.Trim(' ');
            if (item.Length > 0 && ++seen == index)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// The gateway's settlement text ("13-Sep-2026 04:12:31 PM EDT") as a date and time. The
    /// procedures strip " EDT" only and fail on an " EST" stamp; both are read here.
    /// </summary>
    private static DateTime MonthEndSettlement(string text)
    {
        var cleaned = SqlReplace(SqlReplace(text, " EDT", string.Empty), " EST", string.Empty)!;
        return DateTime.Parse(cleaned, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One settled or credited gateway transaction placed on a job, with the ids of the
    /// accounting records carrying it. The orders rank the customer and job names for sorting.
    /// </summary>
    private sealed record MonthEndSettledLine(
        string Day, string? Customer, string? JobName, int CustomerOrder, int JobOrder,
        bool Settled, decimal? Plus, decimal? Minus, IReadOnlyList<int> AccountingIds);

    /// <summary>One job's card money for the month at one processing rate.</summary>
    private sealed record MonthEndCardLine(MonthEndJobDto Job, decimal Rate, decimal? SumUnsigned, decimal? SumSigned)
    {
        public string? Reported => Job.JobNameQbp ?? Job.JobName;
    }

    /// <summary>
    /// The registration month-end close for one settlement month, read and built without the
    /// procedures. As they do, it first STORES the month's billing counts for every job that has
    /// none yet (a job already stored keeps its row), then builds the sheets from what is stored.
    /// </summary>
    private async Task<List<SheetData>> BuildMonthEndRegistrationSheetsAsync(int month, int year, CancellationToken cancellationToken)
    {
        await _reportingRepository.AddMissingMonthlyJobStatsAsync(year, month, cancellationToken);
        var monthStats = await _reportingRepository.GetMonthlyJobStatsAsync(year, month, cancellationToken);
        var data = await _reportingRepository.GetMonthEndRegistrationDataAsync(year, month, cancellationToken);
        return BuildMonthEndRegistrationSheets(year, month, data, monthStats);
    }

    /// <summary>
    /// The ten sheets of the registration month-end close for one settlement month, each named
    /// "m-yyyy …": Clients, Dailys, Job-Dailys, IIF-Payments, IIF-Credits, TSIC-Fees, CC-Fees,
    /// Admin-Fees, Retainers and Checks.
    /// <paramref name="monthStats"/> are the month's billing counts (adn.Monthly_Job_Stats rows):
    /// the last five sheets are driven by them, and a job without one has no card fees.
    /// </summary>
    private static List<SheetData> BuildMonthEndRegistrationSheets(
        int year,
        int month,
        MonthEndRegistrationDataDto data,
        IReadOnlyList<MonthEndJobStatDto> monthStats)
    {
        var first = new DateTime(year, month, 1);
        var monthYear = string.Create(CultureInfo.InvariantCulture, $"{month}-{year}");
        var yearText = year.ToString(CultureInfo.InvariantCulture);
        var monthText = first.ToString("MMM", CultureInfo.InvariantCulture);
        var lastDay = SqlUsDate(first.AddMonths(1).AddDays(-1))!;
        var firstOfNext = SqlUsDate(first.AddMonths(1))!;
        var secondOfNext = SqlUsDate(first.AddMonths(1).AddDays(1))!;

        var jobsById = data.Jobs.ToDictionary(j => j.JobId);
        var jobsByAi = data.Jobs.ToLookup(j => j.JobAi);
        var recordIdsByTx = data.AccountingRecords
            .GroupBy(a => a.TransactionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(a => a.AccountingId).ToList());

        // The month's settled and credited transactions, merchandise left out, each placed on
        // the job its invoice number names (second item: "customer_job_…").
        var lines = new List<MonthEndSettledLine>();
        foreach (var tx in data.Transactions)
        {
            var settled = SqlEquals(tx.TransactionStatus, "Settled Successfully");
            var credited = SqlEquals(tx.TransactionStatus, "Credited");
            if (tx.SettlementDateTime == null
                || !tx.SettlementDateTime.Contains(monthText + "-" + yearText, StringComparison.OrdinalIgnoreCase)
                || !(settled || credited)
                || tx.InvoiceNumber == null
                || tx.InvoiceNumber.Contains("_M", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(MonthEndSplitItem(tx.InvoiceNumber, '_', 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var jobAi))
            {
                continue;
            }

            var day = SqlUsDate(MonthEndSettlement(tx.SettlementDateTime))!;
            var amount = SqlMoney(tx.SettlementAmount);
            foreach (var job in jobsByAi[jobAi])
            {
                lines.Add(new MonthEndSettledLine(
                    day, job.CustomerName, job.JobNameQbp ?? job.JobName, job.CustomerNameOrder, job.ReportedNameOrder,
                    settled, settled ? amount : 0m, credited ? -amount : 0m,
                    recordIdsByTx.GetValueOrDefault(tx.TransactionId, [])));
            }
        }

        return
        [
            .. BuildMonthEndSettlementSheets(monthYear, lines, "Liability Due to Customers:"),
            BuildMonthEndTsicFeesSheet($"{monthYear} TSIC-Fees", monthStats, jobsById, lastDay, firstOfNext),
            .. BuildMonthEndFeeSheets(monthYear, yearText, monthText, data, monthStats, jobsById, lastDay, secondOfNext),
        ];
    }

    /// <summary>
    /// The five sheets both closes open with, each named "<paramref name="prefix"/> …":
    /// "Clients" (transactions per customer and job), "Dailys" (money in and out per settlement
    /// day), "Job-Dailys" (the same per job, with a running total), and the two deposit sheets.
    /// </summary>
    private static List<SheetData> BuildMonthEndSettlementSheets(
        string prefix,
        List<MonthEndSettledLine> lines,
        string liabilityAccount)
    {
        var clients = lines
            .GroupBy(r => (MonthEndKey(r.Customer), MonthEndKey(r.JobName)))
            .Select(g => (Line: g.First(), Count: g.Count()))
            .OrderBy(g => g.Line.CustomerOrder).ThenBy(g => g.Line.JobOrder)
            .ToList();

        var dailys = lines
            .GroupBy(r => r.Day, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Day: g.Key, Positive: SqlSum(g.Select(r => r.Plus)), Negative: SqlSum(g.Select(r => r.Minus))))
            .ToList();

        var jobDailys = new List<(string Day, string? Job, decimal? Positive, decimal? Negative, decimal? Total, decimal? Cumulative)>();
        foreach (var job in lines.GroupBy(r => MonthEndKey(r.JobName)).OrderBy(g => g.First().JobOrder))
        {
            decimal? cumulative = null;
            foreach (var day in job.GroupBy(r => r.Day, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var positive = SqlSum(day.Select(r => r.Plus));
                var negative = SqlSum(day.Select(r => r.Minus));
                var total = positive + negative;
                cumulative = total == null ? cumulative : (cumulative ?? 0m) + total;
                jobDailys.Add((day.Key, job.First().JobName, positive, negative, total, cumulative));
            }
        }

        return
        [
            ToSheet($"{prefix} Clients", clients, ["Customer:Job", "Count of ADN Txs"],
                c => [SqlConcat(c.Line.Customer, ":", c.Line.JobName), c.Count]),
            ToSheet($"{prefix} Dailys", dailys, ["Settlement Day", "Positive", "Negative", "DailyTotal"],
                d => [d.Day, d.Positive, d.Negative, d.Positive + d.Negative]),
            ToSheet($"{prefix} Job-Dailys", jobDailys,
                ["Settlement Day", "Customer:Job", "Positive", "Negative", "DailyTotal", "Cumulative"],
                d => [d.Day, d.Job, d.Positive, d.Negative, d.Total, d.Cumulative]),
            BuildMonthEndDepositSheet($"{prefix} IIF-Payments", lines, liabilityAccount, payments: true),
            BuildMonthEndDepositSheet($"{prefix} IIF-Credits", lines, liabilityAccount, payments: false),
        ];
    }

    /// <summary>
    /// "IIF-Payments" / "IIF-Credits": per settlement day and job, a bank line and the matching
    /// liability line, then the end marker. A transaction counts once per accounting record on
    /// it; one with no accounting record contributes only the end marker. Kept.
    /// </summary>
    private static SheetData BuildMonthEndDepositSheet(
        string sheetName,
        List<MonthEndSettledLine> lines,
        string liabilityAccount,
        bool payments)
    {
        // Payments carry a MEMO column and no TOPRINT; credits the reverse.
        string[] columns = payments
            ? ["!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "CLASS", "AMOUNT", "DOCNUM", "MEMO", "CLEAR"]
            : ["!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "CLASS", "AMOUNT", "DOCNUM", "CLEAR", "TOPRINT"];
        List<object?[]> rows =
        [
            [.. columns],
            payments
                ? ["!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "CLASS", "AMOUNT", "DOCNUM", "MEMO", "CLEAR"]
                : ["!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "CLASS", "AMOUNT", "DOCNUM", "CLEAR", ""],
            ["!ENDTRNS", "", "", "", "", "", "", "", "", "", ""],
        ];

        var body = new List<(string Day, int JobOrder, int Kind, object?[] Row)>();
        var groups = lines
            .Where(r => r.Settled == payments)
            .GroupBy(r => (r.Day, MonthEndKey(r.Customer), MonthEndKey(r.JobName)));
        foreach (var g in groups)
        {
            var head = g.First();
            var joined = g.SelectMany(r => r.AccountingIds.Select(id => new { Line = r, AccountingId = id })).ToList();
            if (joined.Count > 0)
            {
                var sum = SqlSum(joined.Select(x => payments ? x.Line.Plus : x.Line.Minus));
                var type = sum > 0 ? "DEPOSIT" : "CHECK";
                var docNum = joined.Max(x => x.AccountingId).ToString(CultureInfo.InvariantCulture);

                // The customer's own name where the job name contains it, as the job name spells it.
                var owner = !string.IsNullOrEmpty(head.Customer) && head.JobName != null
                        && head.JobName.Contains(head.Customer, StringComparison.OrdinalIgnoreCase)
                    ? MonthEndSplitItem(head.JobName, ':', 1)
                    : head.Customer;
                var liability = owner == null ? null : liabilityAccount + owner;

                body.Add((head.Day, head.JobOrder, 4, payments
                    ? ["TRNS", "", type, head.Day, "Checking", head.JobName, "", MonthEndAmount(sum), docNum, "", "N"]
                    : ["TRNS", "", type, head.Day, "Checking", head.JobName, "", MonthEndAmount(sum), docNum, "N", "N"]));
                body.Add((head.Day, head.JobOrder, 5, payments
                    ? ["SPL", "", type, head.Day, liability, head.JobName, "", MonthEndAmount(-sum), docNum, "", "N"]
                    : ["SPL", "", type, head.Day, liability, head.JobName, "", MonthEndAmount(-sum), docNum, "N", "N"]));
            }

            body.Add((head.Day, head.JobOrder, 6, ["ENDTRNS", "", "", "", "", "", "", "", "", "", ""]));
        }

        rows.AddRange(body
            .OrderBy(l => l.Day, StringComparer.Ordinal).ThenBy(l => l.JobOrder).ThenBy(l => l.Kind)
            .Select(l => l.Row));
        return ToSheet(sheetName, rows, columns, row => row);
    }

    /// <summary>
    /// "TSIC-Fees": TSIC's per-player and per-team charges on the month's new registrations, one
    /// statement charge per job and kind, in the order the month's counts were written. A
    /// customer settling through TSIC's gateway is charged on the last day of the month with the
    /// job name as the memo; any other on the 1st of the next with the count spelled out.
    /// </summary>
    private static SheetData BuildMonthEndTsicFeesSheet(
        string sheetName,
        IReadOnlyList<MonthEndJobStatDto> monthStats,
        Dictionary<Guid, MonthEndJobDto> jobsById,
        string lastDay,
        string firstOfNext)
    {
        var rows = MonthEndChargeHeader();
        foreach (var stat in monthStats.OrderBy(s => s.Aid ?? int.MaxValue))
        {
            if (!jobsById.TryGetValue(stat.JobId, out var job) || !job.HasJobType)
            {
                continue;
            }

            var jobName = job.JobNameQbp ?? job.JobName;
            var date = job.SettlesThroughTsic ? lastDay : firstOfNext;
            var item = SqlConcat(MonthEndJobTypeName(job), ":", "Registration");

            void Charge(decimal? rate, int? count, int? toDate, string unit, string each)
            {
                if (!(rate > 0) || count == null || count == 0)
                {
                    return;
                }

                var memo = job.SettlesThroughTsic
                    ? jobName
                    : toDate == null
                        ? null
                        : string.Create(CultureInfo.InvariantCulture,
                            $"{count} {unit} @ ${MonthEndAmount(rate)} per {each} (Total {toDate} to date)");
                var amount = rate.Value * count.Value;
                rows.Add(["TRNS", "", "STMT CHG", date, "Accounts Receivable", jobName, MonthEndAmount(amount), "", memo, "N", "N", "N", ""]);
                rows.Add(
                [
                    "SPL", "", "STMT CHG", date, "Services/Sales", "", MonthEndAmount(-amount), "", memo, "N",
                    (-count.Value).ToString(CultureInfo.InvariantCulture), MonthEndAmount(-rate), item,
                ]);
                rows.Add(MonthEndChargeEnd());
            }

            Charge(job.PerPlayerCharge, stat.NewPlayersThisMonth, stat.ActivePlayersToDate, "registrations", "player");
            Charge(job.PerTeamCharge, stat.NewTeamsThisMonth, stat.ActiveTeamsToDate, "teams", "team");
        }

        return ToSheet(sheetName, rows, MonthEndChargeColumns, row => row);
    }

    private static List<object?[]> MonthEndChargeHeader() =>
    [
        ["!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "TOPRINT", "NAMEISTAXABLE", "ADDR1"],
        ["!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "QNTY", "PRICE", "INVITEM"],
        ["!ENDTRNS", "", "", "", "", "", "", "", "", "", "", "", ""],
    ];

    private static object?[] MonthEndChargeEnd() => ["ENDTRNS", "", "", "", "", "", "", "", "", "", "", "", ""];

    /// <summary>A line of the checks sheet: the given leading cells, every other cell empty.</summary>
    private static object?[] MonthEndCheckLine(params object?[] leading)
    {
        var row = new object?[MonthEndCheckColumns.Length];
        Array.Fill(row, string.Empty);
        leading.CopyTo(row, 0);
        return row;
    }

    private static List<object?[]> MonthEndCheckHeader() =>
    [
        MonthEndCheckLine("!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "CLASS", "AMOUNT", "DOCNUM", "CLEAR", "TOPRINT", "MEMO"),
        MonthEndCheckLine("!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "CLASS", "AMOUNT", "DOCNUM", "CLEAR", "QNTY", "REIMBEXP"),
        MonthEndCheckLine("!ENDTRNS"),
    ];

    /// <summary>The QuickBooks item family a job's type bills under.</summary>
    private static string? MonthEndJobTypeName(MonthEndJobDto job) => job.JobTypeId switch
    {
        1 => "Club",
        2 => "Tournament",
        3 => "League",
        4 => "Camp",
        6 => "Showcase",
        _ => job.JobTypeDesc,
    };

    /// <summary>
    /// "CC-Fees", "Admin-Fees", "Retainers" and "Checks".
    /// Card money is the month's card and e-check accounting records on jobs that have a count
    /// row for the month, each transaction counted once per accounting record on it — and, as
    /// the procedures do by reading a view that repeats a transaction per accounting record,
    /// once more per accounting record on it again. Declined and voided transactions are left
    /// out. A job's money is split by processing rate: the e-check rate (1.5% unless the job has
    /// its own) for e-check transactions, else the card rate (3.5% unless the job has its own).
    /// </summary>
    private static List<SheetData> BuildMonthEndFeeSheets(
        string monthYear,
        string yearText,
        string monthText,
        MonthEndRegistrationDataDto data,
        IReadOnlyList<MonthEndJobStatDto> monthStats,
        Dictionary<Guid, MonthEndJobDto> jobsById,
        string lastDay,
        string secondOfNext)
    {
        var statRowsByJob = monthStats.GroupBy(s => s.JobId).ToDictionary(g => g.Key, g => g.Count());
        var recordsPerTx = data.AccountingRecords.GroupBy(a => a.TransactionId).ToDictionary(g => g.Key, g => g.Count());
        var monthTxs = data.Transactions
            .Where(t => SqlSubstring(t.SettlementDateTime, 8, 4) == yearText
                && string.Equals(SqlSubstring(t.SettlementDateTime, 4, 3), monthText, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(t => t.TransactionId);

        var cardMoney = new List<(MonthEndJobDto Job, decimal Rate, int Weight, decimal? Signed)>();
        foreach (var record in data.AccountingRecords)
        {
            if (!MonthEndCardMethods.Any(m => SqlEquals(record.PaymentMethod, m))
                || record.RegistrationJobId is not { } jobId
                || !statRowsByJob.TryGetValue(jobId, out var statRows)
                || !jobsById.TryGetValue(jobId, out var job)
                || !monthTxs.TryGetValue(record.TransactionId, out var tx)
                || tx.TransactionStatus == null
                || SqlEquals(tx.TransactionStatus, "Declined")
                || SqlEquals(tx.TransactionStatus, "Voided"))
            {
                continue;
            }

            var amount = SqlMoney(tx.SettlementAmount);
            var rate = (SqlEquals(tx.TransactionType, "echeck")
                ? job.EcheckProcessingFeePercent ?? 1.5m
                : job.ProcessingFeePercent ?? 3.5m) / 100m;
            cardMoney.Add((job, rate, recordsPerTx[record.TransactionId] * statRows,
                SqlEquals(tx.TransactionStatus, "Credited") ? -amount : amount));
        }

        var cardLines = cardMoney
            .GroupBy(x => (x.Job.JobId, x.Rate))
            .Select(g => new MonthEndCardLine(
                g.First().Job,
                g.Key.Rate,
                SqlSum(g.Select(x => x.Signed == null ? (decimal?)null : Math.Abs(x.Signed.Value) * x.Weight)),
                SqlSum(g.Select(x => x.Signed * x.Weight))))
            .ToList();

        // TSIC's own charges on the month's new registrations, for customers on TSIC's gateway.
        var tsicFees = monthStats
            .Where(s => jobsById.TryGetValue(s.JobId, out var job) && job.SettlesThroughTsic)
            .Select(s => (
                Job: jobsById[s.JobId],
                Payment: (s.NewPlayersThisMonth * (jobsById[s.JobId].PerPlayerCharge ?? 0m))
                    + (s.NewTeamsThisMonth * (jobsById[s.JobId].PerTeamCharge ?? 0m))))
            .ToList();

        return
        [
            BuildMonthEndCardFeesSheet($"{monthYear} CC-Fees", cardLines, lastDay),
            BuildMonthEndAdminFeesSheet($"{monthYear} Admin-Fees", data.AdminCharges, jobsById),
            BuildMonthEndRetainersSheet($"{monthYear} Retainers", cardLines, data.AdminCharges, tsicFees, jobsById, lastDay),
            BuildMonthEndChecksSheet($"{monthYear} Checks", cardLines, data.AdminCharges, tsicFees, jobsById, secondOfNext),
        ];
    }

    /// <summary>
    /// "CC-Fees": the processing fee on each job's card money at each rate — the unsigned money
    /// to the cent, times the rate held to three decimal places as a fraction, to the cent.
    /// </summary>
    private static SheetData BuildMonthEndCardFeesSheet(string sheetName, List<MonthEndCardLine> cardLines, string lastDay)
    {
        var rows = MonthEndChargeHeader();
        foreach (var line in cardLines.OrderBy(l => l.Job.JobNameOrder).ThenBy(l => l.Rate))
        {
            decimal? dollars = line.SumUnsigned == null ? null : MonthEndCents(line.SumUnsigned.Value);
            var rate = Math.Round(line.Rate, 3, MidpointRounding.AwayFromZero);
            decimal? fee = dollars == null ? null : MonthEndCents(dollars.Value * rate);
            rows.Add(["TRNS", "", "STMT CHG", lastDay, "Accounts Receivable", line.Reported, MonthEndAmount(fee), "", "", "N", "N", "N", ""]);
            rows.Add(
            [
                "SPL", "", "STMT CHG", lastDay, "Services/Sales:Processing Fees", "", MonthEndAmount(-fee), "", "", "N",
                MonthEndAmount(-dollars), MonthEndFixed(-rate, 3), "Processing Fees",
            ]);
            rows.Add(MonthEndChargeEnd());
        }

        return ToSheet(sheetName, rows, MonthEndChargeColumns, row => row);
    }

    /// <summary>
    /// "Admin-Fees": one transaction per admin charge entered for the month. A chargeback (type
    /// 1) is a check and its reversal (type 11) a deposit, both against the customer's
    /// liability; every other type is a statement charge to the job.
    /// </summary>
    private static SheetData BuildMonthEndAdminFeesSheet(
        string sheetName,
        IReadOnlyList<MonthEndAdminChargeDto> adminCharges,
        Dictionary<Guid, MonthEndJobDto> jobsById)
    {
        var rows = MonthEndChargeHeader();
        foreach (var charge in adminCharges)
        {
            if (!jobsById.TryGetValue(charge.JobId, out var job) || !job.HasJobType)
            {
                continue;
            }

            var type = charge.ChargeTypeId;
            var payment = MonthEndCents(charge.ChargeAmount);
            var jobName = job.JobNameQbp ?? job.JobName;
            var date = SqlUsDate(charge.CreateDate);
            var registrationItem = SqlConcat(MonthEndJobTypeName(job), ":", "Registration");
            var liability = job.CustomerName == null ? null : "Liability Due to Customers:" + job.CustomerName;
            string? transactionType = type switch { 1 => "CHECK", 11 => "DEPOSIT", 2 or 3 or 4 or 5 or 6 => "STMT CHG", _ => null };
            var againstBank = type is 1 or 11;

            rows.Add(
            [
                "TRNS", "", transactionType, date, againstBank ? "Checking" : "Accounts Receivable", jobName,
                MonthEndAmount(againstBank ? -payment : payment), "", charge.Comment, "N", "N", "N", "",
            ]);
            rows.Add(
            [
                "SPL", "", transactionType, date,
                type switch { 1 or 11 => liability, 2 or 6 => "Services/Sales:Admin Fees", 3 or 4 or 5 => "Services/Sales", _ => null },
                jobName, MonthEndAmount(againstBank ? payment : -payment), "", charge.Comment, "N",
                MonthEndAmount(type == 1 ? payment : -payment), MonthEndAmount(type == 1 ? payment : -payment),
                type switch
                {
                    1 or 11 => registrationItem,
                    2 or 3 or 6 => "Admin Fees",
                    4 => "Mobile App Services",
                    5 => "Refund",
                    _ => null,
                },
            ]);
            rows.Add(MonthEndChargeEnd());
        }

        return ToSheet(sheetName, rows, MonthEndChargeColumns, row => row);
    }

    /// <summary>
    /// "Retainers": what TSIC keeps from each job for the month — processing fees (each rate's
    /// money held to four places, times the rate, to the cent), admin charges other than
    /// chargebacks and their reversals, and TSIC's own charges — moved from receivables to the
    /// customer's liability. Jobs sharing a customer name and a reported name are one line; a
    /// line that nets to zero is left out.
    /// </summary>
    private static SheetData BuildMonthEndRetainersSheet(
        string sheetName,
        List<MonthEndCardLine> cardLines,
        IReadOnlyList<MonthEndAdminChargeDto> adminCharges,
        List<(MonthEndJobDto Job, decimal? Payment)> tsicFees,
        Dictionary<Guid, MonthEndJobDto> jobsById,
        string lastDay)
    {
        var parts = new List<(MonthEndJobDto Job, decimal? Payment)>();
        parts.AddRange(cardLines.Select(l => (
            l.Job,
            l.SumUnsigned == null
                ? (decimal?)null
                : MonthEndCents(Math.Round(l.SumUnsigned.Value, 4, MidpointRounding.AwayFromZero) * l.Rate))));
        parts.AddRange(adminCharges
            .Where(c => c.ChargeTypeId is not (1 or 11) && jobsById.ContainsKey(c.JobId))
            .GroupBy(c => c.JobId)
            .Select(g => (jobsById[g.Key], (decimal?)MonthEndCents(g.Sum(c => c.ChargeAmount)))));
        parts.AddRange(tsicFees.Select(f => (f.Job, f.Payment == null ? (decimal?)null : MonthEndCents(f.Payment.Value))));

        var rows = MonthEndChargeHeader();
        var retainers = parts
            .GroupBy(p => (MonthEndKey(p.Job.CustomerName), MonthEndKey(p.Job.JobNameQbp ?? p.Job.JobName)))
            .Select(g => (g.First().Job, Payment: SqlSum(g.Select(p => p.Payment))))
            .Where(r => r.Payment != null && r.Payment != 0m)
            .OrderBy(r => r.Job.CustomerNameOrder).ThenBy(r => r.Job.ReportedNameOrder);
        foreach (var (job, payment) in retainers)
        {
            var customer = job.CustomerName;
            rows.Add(["TRNS", "", "STMT CHG", lastDay, "Accounts Receivable", job.JobNameQbp ?? job.JobName, MonthEndAmount(-payment), "", "", "N", "N", "N", ""]);
            rows.Add(
            [
                "SPL", "", "STMT CHG", lastDay, customer == null ? null : "Liability Due To Customers:" + customer, "",
                MonthEndAmount(payment), "", "", "", "", MonthEndAmount(payment), customer == null ? null : "Retainer:" + customer,
            ]);
            rows.Add(MonthEndChargeEnd());
        }

        return ToSheet(sheetName, rows, MonthEndChargeColumns, row => row);
    }

    /// <summary>
    /// "Checks": the balance due each payee — a customer, or the group it is paid under — as one
    /// check dated the 2nd of the next month, split by job: the job's signed card money less
    /// what TSIC retains (processing fees, ALL admin charges, TSIC's own charges). Each payee's
    /// block carries its own three header lines; with no payee the sheet is empty.
    /// </summary>
    private static SheetData BuildMonthEndChecksSheet(
        string sheetName,
        List<MonthEndCardLine> cardLines,
        IReadOnlyList<MonthEndAdminChargeDto> adminCharges,
        List<(MonthEndJobDto Job, decimal? Payment)> tsicFees,
        Dictionary<Guid, MonthEndJobDto> jobsById,
        string secondOfNext)
    {
        var parts = new List<(MonthEndJobDto Job, decimal? Retained, decimal? Received)>();
        parts.AddRange(cardLines.Select(l => (
            l.Job,
            l.SumUnsigned == null ? (decimal?)null : MonthEndCents(l.SumUnsigned.Value * l.Rate),
            l.SumSigned)));
        parts.AddRange(adminCharges
            .Where(c => jobsById.ContainsKey(c.JobId))
            .GroupBy(c => c.JobId)
            .Select(g => (jobsById[g.Key], (decimal?)g.Sum(c => c.ChargeAmount), (decimal?)0m)));
        parts.AddRange(tsicFees.Select(f => (f.Job, f.Payment, (decimal?)0m)));

        static string? Payee(MonthEndJobDto job) => job.CustomerGroupNames.Count > 0 ? job.CustomerGroupNames[0] : job.CustomerName;

        var jobLines = parts
            .GroupBy(p => (MonthEndKey(Payee(p.Job)), MonthEndKey(p.Job.CustomerName), MonthEndKey(p.Job.JobNameQbp ?? p.Job.JobName)))
            .Select(g => (
                g.First().Job,
                Retained: SqlSum(g.Select(p => p.Retained)) is { } retained ? MonthEndCents(retained) : (decimal?)null,
                Received: SqlSum(g.Select(p => p.Received)) is { } received ? MonthEndCents(received) : (decimal?)null))
            .ToList();

        var rows = new List<object?[]>();
        var payees = jobLines
            .GroupBy(l => MonthEndKey(Payee(l.Job)))
            .OrderBy(g => Payee(g.First().Job), SqlVarcharOrder);
        foreach (var payee in payees)
        {
            var name = Payee(payee.First().Job);
            var retained = SqlSum(payee.Select(l => l.Retained));
            var received = SqlSum(payee.Select(l => l.Received));

            rows.AddRange(MonthEndCheckHeader());
            rows.Add(MonthEndCheckLine(
                "TRNS", "", "CHECK", secondOfNext, "Checking", name, "", MonthEndAmount(-(received - retained)), "", "N", "N",
                name == null ? null : "EFT Balance Due " + name));
            foreach (var line in payee.OrderBy(l => l.Job.CustomerNameOrder).ThenBy(l => l.Job.ReportedNameOrder))
            {
                var customer = line.Job.CustomerName;
                rows.Add(MonthEndCheckLine(
                    "SPL", "", "CHECK", secondOfNext, customer == null ? null : "Liability Due To Customers:" + customer,
                    line.Job.JobNameQbp ?? line.Job.JobName, "", MonthEndAmount(line.Received - line.Retained), "", "", "N",
                    "", "", "", "", "N", "N", "NOTHING"));
            }

            rows.Add(MonthEndCheckLine("ENDTRNS"));
        }

        return ToSheet(sheetName, rows, MonthEndCheckColumns, row => row);
    }

    /// <summary>
    /// One store payment record on a settled or credited merchandise transaction.
    /// <c>Weight</c> is how many payment records carry the transaction: the fee sheets count the
    /// record that many times, as the procedure does by joining the payment records twice.
    /// </summary>
    private sealed record MonthEndMerchLine(
        string? Customer, string? Payee, string? JobName, decimal StoreRate, decimal FeeRate,
        decimal? Plus, decimal? Minus, decimal? FeesIn, decimal? FeesOut, int Weight)
    {
        /// <summary>The payment's cart has product fees on record, in or out.</summary>
        public bool HasFees => FeesIn != null || FeesOut != null;
    }

    /// <summary>What one group of merchandise payment records comes to.</summary>
    private sealed record MonthEndMerchTotals(decimal ProductFees, decimal? CardFees, decimal? CardMoney, decimal? Net)
    {
        public static MonthEndMerchTotals Of(IEnumerable<MonthEndMerchLine> lines)
        {
            var list = lines.ToList();
            return new MonthEndMerchTotals(
                MonthEndCents(list.Sum(l => ((l.FeesIn ?? 0m) + (l.FeesOut ?? 0m)) * l.Weight)),
                SqlSum(list.Select(l => (l.Plus - l.Minus) * l.FeeRate * l.Weight)) is { } cardFees ? MonthEndCents(cardFees) : null,
                SqlSum(list.Select(l => (l.Plus - l.Minus) * l.Weight)) is { } cardMoney ? MonthEndCents(cardMoney) : null,
                SqlSum(list.Select(l => (l.Plus + l.Minus) * l.Weight)) is { } net ? MonthEndCents(net) : null);
        }

        /// <summary>What TSIC keeps: its share of the product fees at <paramref name="storeRate"/>, plus the card fees.</summary>
        public decimal? Retained(decimal storeRate) =>
            CardFees == null ? null : MonthEndCents((storeRate * ProductFees) + CardFees.Value);
    }

    /// <summary>The merchandise month-end close for one settlement month, read and built without the procedure.</summary>
    private async Task<List<SheetData>> BuildMonthEndMerchandiseSheetsAsync(int month, int year, CancellationToken cancellationToken)
    {
        var data = await _reportingRepository.GetMonthEndMerchandiseDataAsync(year, month, cancellationToken);
        return BuildMonthEndMerchandiseSheets(year, month, data);
    }

    /// <summary>
    /// The nine sheets of the merchandise month-end close for one settlement month: the five
    /// settlement sheets ("m-yyyy M Clients" … "M IIF-Credits"), then "m-yyyy MERCH-Fees",
    /// "M CC-Fees", "M Retainers" and "M Checks".
    /// A store sale is reported under the customer's name with " MERCH" added and the job's
    /// reported name with the customer's name so extended and " M" added. TSIC's share of a
    /// sale's product fees is the job's store rate (10% unless it has its own); the processing
    /// fee is the job's e-check rate (1.5% unless it has its own) for e-check payment methods,
    /// else its card rate (3.5% unless it has its own), held to three decimal places as a fraction.
    /// </summary>
    private static List<SheetData> BuildMonthEndMerchandiseSheets(int year, int month, MonthEndMerchandiseDataDto data)
    {
        var first = new DateTime(year, month, 1);
        var monthYear = string.Create(CultureInfo.InvariantCulture, $"{month}-{year}");
        var marker = first.ToString("MMM", CultureInfo.InvariantCulture) + "-" + year.ToString(CultureInfo.InvariantCulture);
        var lastDay = SqlUsDate(first.AddMonths(1).AddDays(-1))!;

        var jobsById = data.Jobs.ToDictionary(j => j.JobId);
        var paymentsByTx = data.Payments.ToLookup(p => p.TransactionId);

        var settled = new List<(MonthEndSettledLine Line, MonthEndMerchLine Merch, bool CardMethod)>();
        foreach (var tx in data.Transactions)
        {
            var isSettled = SqlEquals(tx.TransactionStatus, "Settled Successfully");
            var isCredited = SqlEquals(tx.TransactionStatus, "Credited");
            if (tx.SettlementDateTime == null
                || !tx.SettlementDateTime.Contains(marker, StringComparison.OrdinalIgnoreCase)
                || !(isSettled || isCredited)
                || tx.InvoiceNumber == null
                || !tx.InvoiceNumber.Contains("_M", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var day = SqlUsDate(MonthEndSettlement(tx.SettlementDateTime))!;
            var amount = SqlMoney(tx.SettlementAmount);
            var accountingIds = paymentsByTx[tx.TransactionId].Select(p => p.AccountingId).ToList();
            foreach (var payment in paymentsByTx[tx.TransactionId])
            {
                if (!payment.Resolved || payment.JobId is not { } jobId || !jobsById.TryGetValue(jobId, out var job))
                {
                    continue;
                }

                var customer = job.CustomerName == null ? null : job.CustomerName + " MERCH";
                var jobName = SqlReplace(job.JobNameQbp ?? job.JobName, job.CustomerName, customer!) is { } renamed
                    ? renamed + " M"
                    : null;
                var echeck = SqlEquals(payment.PaymentMethod, "E-Check Payment") || SqlEquals(payment.PaymentMethod, "Failed E-Check Payment");
                var moneyIn = SqlEquals(payment.PaymentMethod, "Credit Card Payment") || SqlEquals(payment.PaymentMethod, "E-Check Payment");
                var moneyOut = SqlEquals(payment.PaymentMethod, "Credit Card Credit") || SqlEquals(payment.PaymentMethod, "Failed E-Check Payment");
                decimal? plus = isSettled ? amount : 0m;
                decimal? minus = isCredited ? -amount : 0m;

                settled.Add((
                    new MonthEndSettledLine(day, customer, jobName, 0, 0, isSettled, plus, minus, accountingIds),
                    new MonthEndMerchLine(
                        customer,
                        job.CustomerGroupNames.Count > 0 ? job.CustomerGroupNames[0] : customer,
                        jobName,
                        job.StoreTsicRate ?? 0.10m,
                        Math.Round((echeck ? job.EcheckProcessingFeePercent ?? 1.5m : job.ProcessingFeePercent ?? 3.5m) / 100m, 3, MidpointRounding.AwayFromZero),
                        plus,
                        minus,
                        moneyIn ? payment.BatchProductFees : null,
                        moneyOut ? -payment.BatchProductFees : null,
                        accountingIds.Count),
                    MonthEndCardMethods.Any(m => SqlEquals(payment.PaymentMethod, m))));
            }
        }

        // The procedure's working table holds these names as non-Unicode text and sorts them so.
        static Dictionary<string, int> Ranks(IEnumerable<string?> names) => names
            .GroupBy(MonthEndKey)
            .OrderBy(g => g.First(), SqlVarcharOrder)
            .Select((g, i) => (g.Key, Rank: i))
            .ToDictionary(x => x.Key, x => x.Rank);
        var customerOrder = Ranks(settled.Select(s => s.Line.Customer));
        var jobOrder = Ranks(settled.Select(s => s.Line.JobName));
        var lines = settled
            .Select(s => s.Line with { CustomerOrder = customerOrder[MonthEndKey(s.Line.Customer)], JobOrder = jobOrder[MonthEndKey(s.Line.JobName)] })
            .ToList();

        // The fee sheets read card and e-check payment records only.
        var merch = settled.Where(s => s.CardMethod).Select(s => s.Merch).ToList();

        return
        [
            .. BuildMonthEndSettlementSheets($"{monthYear} M", lines, "Liability Due MERCH Customers:"),
            BuildMonthEndMerchFeesSheet($"{monthYear} MERCH-Fees", merch, lastDay),
            BuildMonthEndMerchCardFeesSheet($"{monthYear} M CC-Fees", merch, lastDay),
            BuildMonthEndMerchRetainersSheet($"{monthYear} M Retainers", merch, lastDay),
            BuildMonthEndMerchChecksSheet($"{monthYear} M Checks", merch, lastDay),
        ];
    }

    /// <summary>Header lines, then the given lines by name (as non-Unicode text) and kind: 4 bank line, 5 split, 6 end marker.</summary>
    private static SheetData MonthEndMerchSheet(
        string sheetName,
        string[] columns,
        List<object?[]> header,
        List<(string? Name, int Kind, object?[] Row)> body)
    {
        header.AddRange(body.OrderBy(l => l.Name, SqlVarcharOrder).ThenBy(l => l.Kind).Select(l => l.Row));
        return ToSheet(sheetName, header, columns, row => row);
    }

    /// <summary>
    /// "MERCH-Fees": TSIC's share of each job's product fees — the fees taken in less the fees
    /// given back, to the cent, times the job's store rate (written to five decimal places).
    /// </summary>
    private static SheetData BuildMonthEndMerchFeesSheet(string sheetName, List<MonthEndMerchLine> merch, string lastDay)
    {
        var body = new List<(string? Name, int Kind, object?[] Row)>();
        foreach (var g in merch.GroupBy(l => (MonthEndKey(l.JobName), l.StoreRate)))
        {
            var jobName = g.First().JobName;
            var rate = g.Key.StoreRate;
            var fees = MonthEndMerchTotals.Of(g).ProductFees;
            body.Add((jobName, 4,
                ["TRNS", "", "STMT CHG", lastDay, "Accounts Receivable", jobName, MonthEndFixed(rate * fees, 5), "", jobName, "N", "N", "N", ""]));
            body.Add((jobName, 5,
            [
                "SPL", "", "STMT CHG", lastDay, "Services/Sales:MERCH Fees", "", MonthEndFixed(rate * -fees, 5), "", jobName, "N",
                MonthEndAmount(-fees), MonthEndFixed(-rate, 3), "MERCH Fees",
            ]));
        }

        foreach (var g in merch.GroupBy(l => MonthEndKey(l.JobName)))
        {
            body.Add((g.First().JobName, 6, MonthEndChargeEnd()));
        }

        return MonthEndMerchSheet(sheetName, MonthEndChargeColumns, MonthEndChargeHeader(), body);
    }

    /// <summary>
    /// "M CC-Fees": the processing fee on each job's card money at each rate, credited back to
    /// receivables. Only payments whose cart has product fees on record are charged; the end
    /// marker is written for every job and rate regardless.
    /// </summary>
    private static SheetData BuildMonthEndMerchCardFeesSheet(string sheetName, List<MonthEndMerchLine> merch, string lastDay)
    {
        var body = new List<(string? Name, int Kind, object?[] Row)>();
        foreach (var g in merch.GroupBy(l => (MonthEndKey(l.JobName), l.FeeRate)))
        {
            var jobName = g.First().JobName;
            var name = jobName == null ? null : jobName + "|" + MonthEndFixed(g.Key.FeeRate, 3);
            var charged = g.Where(l => l.HasFees).ToList();
            if (charged.Count > 0)
            {
                var totals = MonthEndMerchTotals.Of(charged);
                body.Add((name, 4,
                    ["TRNS", "", "STMT CHG", lastDay, "Accounts Receivable", jobName, MonthEndAmount(-totals.CardFees), "", "", "N", "N", "N", ""]));
                body.Add((name, 5,
                [
                    "SPL", "", "STMT CHG", lastDay, "Services/Sales:MERCH Processing Fees", "", MonthEndAmount(-totals.CardFees), "", "", "N",
                    MonthEndAmount(-totals.CardMoney), MonthEndFixed(-g.Key.FeeRate, 3), "MERCH Processing Fees",
                ]));
            }

            body.Add((name, 6, MonthEndChargeEnd()));
        }

        return MonthEndMerchSheet(sheetName, MonthEndChargeColumns, MonthEndChargeHeader(), body);
    }

    /// <summary>
    /// "M Retainers": what TSIC keeps from each job's store sales — its share of the product
    /// fees plus the processing fees — moved from receivables to the customer's liability.
    /// </summary>
    private static SheetData BuildMonthEndMerchRetainersSheet(string sheetName, List<MonthEndMerchLine> merch, string lastDay)
    {
        var body = new List<(string? Name, int Kind, object?[] Row)>();
        var charged = merch.Where(l => l.HasFees).ToList();
        foreach (var g in charged.GroupBy(l => (MonthEndKey(l.JobName), l.StoreRate)))
        {
            var jobName = g.First().JobName;
            var retained = MonthEndMerchTotals.Of(g).Retained(g.Key.StoreRate);
            body.Add((jobName, 4,
                ["TRNS", "", "STMT CHG", lastDay, "Accounts Receivable", jobName, MonthEndAmount(-retained), "", "", "N", "N", "N", ""]));
        }

        foreach (var g in charged.GroupBy(l => (MonthEndKey(l.Customer), MonthEndKey(l.JobName), l.StoreRate)))
        {
            var customer = g.First().Customer;
            var retained = MonthEndAmount(MonthEndMerchTotals.Of(g).Retained(g.Key.StoreRate));
            body.Add((g.First().JobName, 5,
            [
                "SPL", "", "STMT CHG", lastDay, customer == null ? null : "Liability Due MERCH Customers:" + customer, "",
                retained, "", "", "N", "", retained, customer == null ? null : "MERCH Retainer:" + customer,
            ]));
        }

        foreach (var g in merch.GroupBy(l => MonthEndKey(l.JobName)))
        {
            body.Add((g.First().JobName, 6, MonthEndChargeEnd()));
        }

        return MonthEndMerchSheet(sheetName, MonthEndChargeColumns, MonthEndChargeHeader(), body);
    }

    /// <summary>
    /// "M Checks": the balance due on store sales, dated the last day of the month — the net
    /// card money less what TSIC keeps. One bank line per payee (the customer's group, else the
    /// customer) and one split per job, with an end marker per job. The lines are placed by
    /// NAME, as the procedure places them: the bank line under the payee's name, a split under
    /// its customer's, an end marker under its job's.
    /// </summary>
    private static SheetData BuildMonthEndMerchChecksSheet(string sheetName, List<MonthEndMerchLine> merch, string lastDay)
    {
        var body = new List<(string? Name, int Kind, object?[] Row)>();
        var charged = merch.Where(l => l.HasFees).ToList();

        static decimal? Balance(MonthEndMerchTotals totals, decimal storeRate) =>
            totals.Net == null || totals.CardFees == null
                ? null
                : MonthEndCents(totals.Net.Value - ((storeRate * totals.ProductFees) + totals.CardFees.Value));

        foreach (var g in charged.GroupBy(l => (MonthEndKey(l.Payee), l.StoreRate)))
        {
            var payee = g.First().Payee;
            body.Add((payee, 4, MonthEndCheckLine(
                "TRNS", "", "CHECK", lastDay, "Checking", payee, "", MonthEndAmount(-Balance(MonthEndMerchTotals.Of(g), g.Key.StoreRate)),
                "", "N", "N", payee == null ? null : "EFT Balance Due " + payee)));
        }

        foreach (var g in charged.GroupBy(l => (MonthEndKey(l.Payee), MonthEndKey(l.Customer), MonthEndKey(l.JobName), l.StoreRate)))
        {
            var customer = g.First().Customer;
            body.Add((customer, 5, MonthEndCheckLine(
                "SPL", "", "CHECK", lastDay, customer == null ? null : "Liability Due MERCH Customers:" + customer, g.First().JobName, "",
                MonthEndAmount(Balance(MonthEndMerchTotals.Of(g), g.Key.StoreRate)), "", "", "N", "", "", "", "", "N", "N", "NOTHING")));
        }

        foreach (var g in merch.GroupBy(l => MonthEndKey(l.JobName)))
        {
            body.Add((g.First().JobName, 6, MonthEndCheckLine("ENDTRNS")));
        }

        return MonthEndMerchSheet(sheetName, MonthEndCheckColumns, MonthEndCheckHeader(), body);
    }
}
