using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.CustomerJobRevenue;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class CustomerJobRevenueRepository : ICustomerJobRevenueRepository
{
    private readonly SqlDbContext _context;

    public CustomerJobRevenueRepository(SqlDbContext context)
    {
        _context = context;
    }

    // =====================================================================
    // LEGACY SPROC READER — comparison baseline ONLY.
    // Kept (restored after the 2026-07-29 golden-master pass) so SuperUser can
    // run a live QA diff of the EF port against the deployed sprocs at any
    // time until cutover retires legacy. Not reachable from the report UI.
    // =====================================================================
    public async Task<JobRevenueDataDto> GetLegacySprocDataAsync(
        Guid jobId, DateTime startDate, DateTime endDate,
        string listJobsString, bool isTsicAdn,
        CancellationToken ct = default)
    {
        var connection = _context.Database.GetDbConnection();
        var cmd = connection.CreateCommand();

        cmd.CommandText = isTsicAdn
            ? "[reporting].[CustomerJobRevenueRollups]"
            : "[reporting].[CustomerJobRevenueRollups_NotTSICADN]";
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.Add(new SqlParameter("@jobId", SqlDbType.UniqueIdentifier) { Value = jobId });
        cmd.Parameters.Add(new SqlParameter("@startDate", SqlDbType.DateTime) { Value = startDate });
        cmd.Parameters.Add(new SqlParameter("@endDate", SqlDbType.DateTime) { Value = endDate });
        cmd.Parameters.Add(new SqlParameter("@listJobsString", SqlDbType.VarChar) { Value = listJobsString });

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        // Result set 1: Revenue rollup records
        var revenueRecords = new List<JobRevenueRecordDto>();
        while (await reader.ReadAsync(ct))
        {
            revenueRecords.Add(new JobRevenueRecordDto
            {
                JobName = reader.GetString(reader.GetOrdinal("JobName")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Month = reader.GetInt32(reader.GetOrdinal("Month")),
                PayMethod = reader.GetString(reader.GetOrdinal("PayMethod")),
                PayAmount = isTsicAdn
                    ? reader.GetDecimal(reader.GetOrdinal("PayAmount"))
                    : (decimal)reader.GetDouble(reader.GetOrdinal("PayAmount"))
            });
        }

        // Result set 2: Monthly counts
        await reader.NextResultAsync(ct);
        var monthlyCounts = new List<JobMonthlyCountDto>();
        while (await reader.ReadAsync(ct))
        {
            monthlyCounts.Add(new JobMonthlyCountDto
            {
                Aid = reader.GetInt32(reader.GetOrdinal("aid")),
                JobName = reader.GetString(reader.GetOrdinal("JobName")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Month = reader.GetInt32(reader.GetOrdinal("Month")),
                CountActivePlayersToDate = reader.GetInt32(reader.GetOrdinal("Count_ActivePlayersToDate")),
                CountActivePlayersToDateLastMonth = reader.GetInt32(reader.GetOrdinal("Count_ActivePlayersToDate_LastMonth")),
                CountNewPlayersThisMonth = reader.GetInt32(reader.GetOrdinal("Count_NewPlayers_ThisMonth")),
                CountActiveTeamsToDate = reader.GetInt32(reader.GetOrdinal("Count_ActiveTeamsToDate")),
                CountActiveTeamsToDateLastMonth = reader.GetInt32(reader.GetOrdinal("Count_ActiveTeamsToDate_LastMonth")),
                CountNewTeamsThisMonth = reader.GetInt32(reader.GetOrdinal("Count_NewTeams_ThisMonth"))
            });
        }

        // Result set 3: Admin fees
        await reader.NextResultAsync(ct);
        var adminFees = new List<JobAdminFeeDto>();
        while (await reader.ReadAsync(ct))
        {
            adminFees.Add(new JobAdminFeeDto
            {
                JobName = reader.GetString(reader.GetOrdinal("JobName")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Month = reader.GetInt32(reader.GetOrdinal("Month")),
                ChargeType = reader.GetString(reader.GetOrdinal("ChargeType")),
                ChargeAmount = reader.GetDecimal(reader.GetOrdinal("ChargeAmount")),
                Comment = reader.GetString(reader.GetOrdinal("Comment"))
            });
        }

        // Result set 4: Credit card records
        await reader.NextResultAsync(ct);
        var ccRecords = await ReadPaymentRecords(reader, ct);

        // Result set 5: Check records
        await reader.NextResultAsync(ct);
        var checkRecords = await ReadPaymentRecords(reader, ct);

        // Result set 6: Available jobs (legacy SP shape places this at #6 so the
        // legacy TSIC_Unify CustomerJobRevenueController stays compatible).
        await reader.NextResultAsync(ct);
        var availableJobs = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            availableJobs.Add(reader.GetString(reader.GetOrdinal("JobName")));
        }

        // Result set 7: E-Check records (empty when the merchant doesn't process e-check).
        await reader.NextResultAsync(ct);
        var echeckRecords = await ReadPaymentRecords(reader, ct);

        return new JobRevenueDataDto
        {
            RevenueRecords = revenueRecords,
            MonthlyCounts = monthlyCounts,
            AdminFees = adminFees,
            CreditCardRecords = ccRecords,
            CheckRecords = checkRecords,
            EcheckRecords = echeckRecords,
            AvailableJobs = availableJobs
        };
    }

    private static async Task<List<JobPaymentRecordDto>> ReadPaymentRecords(
        System.Data.Common.DbDataReader reader, CancellationToken ct)
    {
        var records = new List<JobPaymentRecordDto>();
        while (await reader.ReadAsync(ct))
        {
            records.Add(new JobPaymentRecordDto
            {
                JobName = reader.GetString(reader.GetOrdinal("JobName")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Month = reader.GetInt32(reader.GetOrdinal("Month")),
                Registrant = reader.GetString(reader.GetOrdinal("Registrant")),
                PaymentMethod = reader.GetString(reader.GetOrdinal("PaymentMethod")),
                PaymentDate = reader.GetDateTime(reader.GetOrdinal("PaymentDate")),
                PaymentAmount = reader.GetDecimal(reader.GetOrdinal("PaymentAmount"))
            });
        }
        return records;
    }

    // =====================================================================
    // EF port of the CustomerJobRevenueRollups sproc pair.
    // Specs: scripts/9-add-echeck-to-cjrr.sql (TSICADN, settlement-based)
    //        scripts/10-add-echeck-to-cjrr-nottsicadn.sql (non-ADN, RA-based)
    // The legacy sprocs stay frozen for TSIC-Unify; only the new system uses
    // these methods. Deltas from the sprocs (deliberate, golden-master vetted):
    //   - decimal-correct math end-to-end (sprocs sum in float);
    //   - the job filter is applied at the SOURCE queries, not at export
    //     (same final rows, far less work);
    //   - dates are optional: null = complete history (specific-jobs scope).
    // =====================================================================

    // Rollup PayMethod labels. Leading spaces are the sprocs' column-ordering
    // mechanism (pivot sorts alphabetically; more spaces sort further right).
    private const string LblCcPayments = "    CC Payments";
    private const string LblCcCredits = "   CC Credits";
    private const string LblEcheckPayments = "    E-Check Payments";
    private const string LblFailedEcheck = "    Failed E-Check Payments";
    private const string LblCheck = "      Check";
    private const string LblCheckClientRecd = "     Check Client Rec'd";
    private const string LblAdminFees = "Admin Fees";
    private const string LblCcFees = "  CC Fees";
    private const string LblEcheckFees = "  E-Check Fees";
    private const string LblTsicFees = " TSIC Fees";

    private sealed record RawRollupRow(
        string JobName, int Year, int Month, string Label, decimal Payment, decimal FeePct);

    /// <summary>Customer-group scope: jobId → customerId → group members (or just the one customer).</summary>
    private async Task<List<Guid>> GetCustomerGroupIdsAsync(Guid jobId, CancellationToken ct)
    {
        var customerId = await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => j.CustomerId)
            .FirstAsync(ct);

        var groupId = await _context.CustomerGroupCustomers
            .Where(c => c.CustomerId == customerId)
            .Select(c => (int?)c.CustomerGroupId)
            .FirstOrDefaultAsync(ct);

        if (groupId == null)
        {
            return [customerId];
        }

        var ids = await _context.CustomerGroupCustomers
            .AsNoTracking()
            .Where(c => c.CustomerGroupId == groupId)
            .Select(c => c.CustomerId)
            .ToListAsync(ct);

        return ids.Count > 0 ? ids : [customerId];
    }

    public async Task<List<string>> GetAvailableJobNamesAsync(Guid jobId, CancellationToken ct = default)
    {
        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);

        // ISNUMERIC(year) = 1 AND year >= 2022 — Year is varchar; parse client-side.
        var jobs = await _context.Jobs
            .AsNoTracking()
            .Where(j => customerIds.Contains(j.CustomerId) && j.JobName != null && j.Year != null)
            .Select(j => new { j.JobName, j.Year })
            .ToListAsync(ct);

        return jobs
            .Where(j => int.TryParse(j.Year, out var y) && y >= 2022)
            .Select(j => j.JobName!)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<RevenueRollupResponseDto> GetRollupAsync(
        Guid jobId, bool isTsicAdn,
        DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default)
    {
        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);
        var jobFilter = jobNames.ToList();
        // Sproc contract: @endDate is advanced one day, then all comparisons are < end.
        DateTime? endEx = endDate?.Date.AddDays(1);
        DateTime? start = startDate;

        var raw = new List<RawRollupRow>();

        if (isTsicAdn)
        {
            // --- Settlement-based categories (CC / E-Check families) from adn.vTxs ---
            string[] adnMethods = ["Credit Card Payment", "Credit Card Credit", "E-Check Payment", "Failed E-Check Payment"];
            var vtxRows = await (
                from ra in _context.RegistrationAccounting
                join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
                join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                join j in _context.Jobs on r.JobId equals j.JobId
                join v in _context.VTxs on ra.AdnTransactionId equals v.TransactionId
                where customerIds.Contains(j.CustomerId)
                    && adnMethods.Contains(apm.PaymentMethod!)
                    && v.TransactionStatus != "Declined" && v.TransactionStatus != "Voided"
                    && v.SettlementTs != null
                    && (start == null || v.SettlementTs >= start)
                    && (endEx == null || v.SettlementTs < endEx)
                    && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
                group new { v.SettlementAmount } by new
                {
                    j.JobName,
                    Year = v.SettlementTs!.Value.Year,
                    Month = v.SettlementTs.Value.Month,
                    Method = apm.PaymentMethod,
                    j.ProcessingFeePercent,
                    j.EcprocessingFeePercent
                } into g
                select new
                {
                    g.Key.JobName,
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Method,
                    Amount = g.Sum(x => x.SettlementAmount ?? 0m),
                    g.Key.ProcessingFeePercent,
                    g.Key.EcprocessingFeePercent
                })
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var row in vtxRows)
            {
                var (label, feePct) = row.Method switch
                {
                    "Credit Card Payment" => (LblCcPayments, (row.ProcessingFeePercent ?? 3.5m) / 100m),
                    "Credit Card Credit" => (LblCcCredits, (row.ProcessingFeePercent ?? 3.5m) / 100m),
                    "E-Check Payment" => (LblEcheckPayments, (row.EcprocessingFeePercent ?? 1.5m) / 100m),
                    _ => (LblFailedEcheck, (row.EcprocessingFeePercent ?? 1.5m) / 100m)
                };
                raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, label, row.Amount, feePct));
            }
        }
        else
        {
            // --- Non-ADN: same categories, but amounts/dates come from Registration_Accounting ---
            string[] raMethods = ["Credit Card Payment", "Credit Card Credit", "E-Check Payment", "Failed E-Check Payment"];
            var raRows = await (
                from ra in _context.RegistrationAccounting
                join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
                join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                join j in _context.Jobs on r.JobId equals j.JobId
                where customerIds.Contains(j.CustomerId)
                    && raMethods.Contains(apm.PaymentMethod!)
                    && ra.Active == true
                    && ra.Createdate != null
                    && (start == null || ra.Createdate >= start)
                    && (endEx == null || ra.Createdate < endEx)
                    && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
                group new { ra.Payamt } by new
                {
                    j.JobName,
                    Year = ra.Createdate!.Value.Year,
                    Month = ra.Createdate.Value.Month,
                    Method = apm.PaymentMethod
                } into g
                select new { g.Key.JobName, g.Key.Year, g.Key.Month, g.Key.Method, Amount = g.Sum(x => x.Payamt ?? 0m) })
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var row in raRows)
            {
                var label = row.Method switch
                {
                    "Credit Card Payment" => LblCcPayments,
                    "Credit Card Credit" => LblCcCredits,
                    "E-Check Payment" => LblEcheckPayments,
                    _ => LblFailedEcheck
                };
                raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, label, row.Amount, 0m));
            }
        }

        // --- Checks (both variants; TSICADN additionally excludes rows whose ADN tx is Declined/Voided) ---
        string[] checkMethods = ["Check Payment By Client", "Check Payment By TSIC"];
        var checkRows = await (
            from ra in _context.RegistrationAccounting
            join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
            join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
            join j in _context.Jobs on r.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && checkMethods.Contains(apm.PaymentMethod!)
                && ra.Active == true
                && ra.Createdate != null
                && (start == null || ra.Createdate >= start)
                && (endEx == null || ra.Createdate < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
                && (!isTsicAdn || !_context.VTxs.Any(v =>
                        v.TransactionId == ra.AdnTransactionId
                        && (v.TransactionStatus == "Declined" || v.TransactionStatus == "Voided")))
            group new { ra.Payamt } by new { j.JobName, Year = ra.Createdate!.Value.Year, Month = ra.Createdate.Value.Month } into g
            select new { g.Key.JobName, g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.Payamt ?? 0m) })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var row in checkRows)
        {
            raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, LblCheck, row.Amount, 0m));
            // Negative mirror: zeroes the check contribution so the pivot grand total stays CC-only.
            raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, LblCheckClientRecd, -row.Amount, 0m));
        }

        // --- Admin Fees (negative) — month-bucketed table, filtered on first-of-month like the sprocs ---
        var adminChargeRows = await (
            from jc in _context.JobAdminCharges
            join j in _context.Jobs on jc.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (start == null || EF.Functions.DateFromParts(jc.Year, jc.Month, 1) >= start)
                && (endEx == null || EF.Functions.DateFromParts(jc.Year, jc.Month, 1) < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            group new { jc.ChargeAmount } by new { j.JobName, jc.Year, jc.Month } into g
            select new { g.Key.JobName, g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.ChargeAmount) })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var row in adminChargeRows)
        {
            raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, LblAdminFees, -row.Amount, 0m));
        }

        // --- CC / E-Check processing fee rollups (TSICADN only; derived per raw row like the sproc) ---
        if (isTsicAdn)
        {
            var feeRows = new List<RawRollupRow>();
            foreach (var row in raw)
            {
                if (row.Label.Contains("CC"))
                {
                    feeRows.Add(row with { Label = LblCcFees, Payment = -Math.Abs(row.Payment * row.FeePct), FeePct = 0m });
                }
                // Fee only on positive e-check payments — ADN does not refund the fee on NSF returns.
                else if (row.Label.Contains("E-Check") && row.Payment > 0)
                {
                    feeRows.Add(row with { Label = LblEcheckFees, Payment = -Math.Abs(row.Payment * row.FeePct), FeePct = 0m });
                }
            }
            // The sprocs' fee INSERTs are SELECT DISTINCT over a tuple that excludes the pct —
            // identical fee rows (e.g. a same-month payment + equal refund) collapse to one.
            // FeePct is zeroed above so record equality mirrors that tuple exactly.
            raw.AddRange(feeRows.Distinct());
        }

        // --- TSIC Fees (negative) from Monthly_Job_Stats × per-player/team charges ---
        var statRows = await (
            from mjs in _context.MonthlyJobStats
            join j in _context.Jobs on mjs.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (start == null || EF.Functions.DateFromParts(mjs.Year, mjs.Month, 1) >= start)
                && (endEx == null || EF.Functions.DateFromParts(mjs.Year, mjs.Month, 1) < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            select new
            {
                j.JobName,
                mjs.Year,
                mjs.Month,
                mjs.CountNewPlayersThisMonth,
                mjs.CountNewTeamsThisMonth,
                j.PerPlayerCharge,
                j.PerTeamCharge
            })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var row in statRows)
        {
            // Sproc NULL semantics: a NULL count nullifies the whole expression and the row
            // contributes nothing (SUM skips NULLs) — replicate, don't coalesce counts to 0.
            if (row.CountNewPlayersThisMonth == null || row.CountNewTeamsThisMonth == null)
            {
                continue;
            }
            var fee = (row.CountNewPlayersThisMonth.Value * (row.PerPlayerCharge ?? 0m))
                      + (row.CountNewTeamsThisMonth.Value * (row.PerTeamCharge ?? 0m));
            raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, LblTsicFees, -fee, 0m));
        }

        // --- Final rollup: group to PayMethod cells, decimal(8,2) semantics ---
        var revenueRecords = raw
            .GroupBy(r => new { r.JobName, r.Year, r.Month, r.Label })
            .Select(g => new JobRevenueRecordDto
            {
                JobName = g.Key.JobName,
                Year = g.Key.Year,
                Month = g.Key.Month,
                PayMethod = g.Key.Label,
                PayAmount = Math.Round(g.Sum(x => x.Payment), 2, MidpointRounding.AwayFromZero)
            })
            .OrderBy(r => r.JobName, StringComparer.Ordinal)
            .ThenBy(r => r.Year)
            .ThenBy(r => r.Month)
            .ThenBy(r => r.PayMethod, StringComparer.Ordinal)
            .ToList();

        // --- Monthly counts (sproc set #2) ---
        var monthlyCounts = await (
            from mjs in _context.MonthlyJobStats
            join j in _context.Jobs on mjs.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (start == null || EF.Functions.DateFromParts(mjs.Year, mjs.Month, 1) >= start)
                && (endEx == null || EF.Functions.DateFromParts(mjs.Year, mjs.Month, 1) < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            orderby j.JobName, mjs.Year, mjs.Month
            select new JobMonthlyCountDto
            {
                Aid = mjs.Aid,
                JobName = j.JobName!,
                Year = mjs.Year,
                Month = mjs.Month,
                CountActivePlayersToDate = mjs.CountActivePlayersToDate ?? 0,
                CountActivePlayersToDateLastMonth = mjs.CountActivePlayersToDateLastMonth ?? 0,
                CountNewPlayersThisMonth = mjs.CountNewPlayersThisMonth ?? 0,
                CountActiveTeamsToDate = mjs.CountActiveTeamsToDate ?? 0,
                CountActiveTeamsToDateLastMonth = mjs.CountActiveTeamsToDateLastMonth ?? 0,
                CountNewTeamsThisMonth = mjs.CountNewTeamsThisMonth ?? 0
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // --- Admin fees (sproc set #3) ---
        var adminFees = await (
            from jac in _context.JobAdminCharges
            join j in _context.Jobs on jac.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (start == null || EF.Functions.DateFromParts(jac.Year, jac.Month, 1) >= start)
                && (endEx == null || EF.Functions.DateFromParts(jac.Year, jac.Month, 1) < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            orderby j.JobName, jac.Year, jac.Month, jac.ChargeType.Name
            select new JobAdminFeeDto
            {
                JobName = j.JobName!,
                Year = jac.Year,
                Month = jac.Month,
                ChargeType = jac.ChargeType.Name ?? string.Empty,
                ChargeAmount = jac.ChargeAmount,
                Comment = jac.Comment ?? string.Empty
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return new RevenueRollupResponseDto
        {
            RevenueRecords = revenueRecords,
            MonthlyCounts = monthlyCounts,
            AdminFees = adminFees
        };
    }

    public async Task<List<JobPaymentRecordDto>> GetPaymentDetailsAsync(
        Guid jobId, bool isTsicAdn, string method,
        DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default)
    {
        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);
        var jobFilter = jobNames.ToList();
        DateTime? endEx = endDate?.Date.AddDays(1);
        DateTime? start = startDate;

        // Sproc detail families. The DEPLOYED procs include 'Credit Card Credit' in the CC
        // detail insert — repo scripts 9/10 are one line stale there (verified via
        // OBJECT_DEFINITION diff against dev DB, 2026-07-29).
        string[] methods = method switch
        {
            "cc" => ["Credit Card Payment", "Credit Card Credit"],
            "check" => ["Check Payment By Client", "Check Payment By TSIC"],
            "echeck" => ["E-Check Payment", "Failed E-Check Payment"],
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Expected cc | check | echeck.")
        };

        var query =
            from ra in _context.RegistrationAccounting
            join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
            join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && methods.Contains(apm.PaymentMethod!)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            select new { ra, apm, r, u, j };

        // Check rows are RA-dated in both variants; CC/E-Check rows are settlement-dated for
        // TSICADN (join adn.vTxs) and RA-dated for non-ADN.
        var useVtx = isTsicAdn && method != "check";

        if (method == "check")
        {
            query = query.Where(x =>
                x.ra.Active == true
                && x.ra.Createdate != null
                && (start == null || x.ra.Createdate >= start)
                && (endEx == null || x.ra.Createdate < endEx)
                && (!isTsicAdn || !_context.VTxs.Any(v =>
                        v.TransactionId == x.ra.AdnTransactionId
                        && (v.TransactionStatus == "Declined" || v.TransactionStatus == "Voided"))));
        }
        else if (!useVtx)
        {
            query = query.Where(x =>
                x.ra.Active == true
                && x.ra.Createdate != null
                && (start == null || x.ra.Createdate >= start)
                && (endEx == null || x.ra.Createdate < endEx));
        }

        List<JobPaymentRecordDto> records;
        if (useVtx)
        {
            records = await (
                from x in query
                join v in _context.VTxs on x.ra.AdnTransactionId equals v.TransactionId
                where v.TransactionStatus != "Declined" && v.TransactionStatus != "Voided"
                    && v.SettlementTs != null
                    && (start == null || v.SettlementTs >= start)
                    && (endEx == null || v.SettlementTs < endEx)
                select new JobPaymentRecordDto
                {
                    JobName = x.j.JobName!,
                    Year = v.SettlementTs!.Value.Year,
                    Month = v.SettlementTs.Value.Month,
                    Registrant = x.r.ClubName == null
                        ? x.u.FirstName + " " + x.u.LastName
                        : x.u.FirstName + " " + x.u.LastName + " (" + x.r.ClubName + ")",
                    PaymentMethod = x.apm.PaymentMethod!,
                    PaymentDate = v.SettlementTs.Value,
                    PaymentAmount = v.SettlementAmount ?? 0m
                })
                .AsNoTracking()
                .ToListAsync(ct);
        }
        else
        {
            var displayAsCheck = method == "check";
            records = await query
                .Select(x => new JobPaymentRecordDto
                {
                    JobName = x.j.JobName!,
                    Year = x.ra.Createdate!.Value.Year,
                    Month = x.ra.Createdate.Value.Month,
                    Registrant = x.r.ClubName == null
                        ? x.u.FirstName + " " + x.u.LastName
                        : x.u.FirstName + " " + x.u.LastName + " (" + x.r.ClubName + ")",
                    PaymentMethod = displayAsCheck ? "Check" : x.apm.PaymentMethod!,
                    PaymentDate = x.ra.Createdate.Value,
                    PaymentAmount = x.ra.Payamt ?? 0m
                })
                .AsNoTracking()
                .ToListAsync(ct);
        }

        return records
            .OrderBy(r => r.JobName, StringComparer.Ordinal)
            .ThenBy(r => r.Year)
            .ThenBy(r => r.Month)
            .ThenBy(r => r.PaymentDate)
            .ToList();
    }

    // =====================================================================
    // TEAMS/PLAYERS TO CUSTOMER — the CLIENT's view of their own money.
    //
    // The Revenue Rollup is TSIC's view: what settles between TSIC and the client, netted
    // down by fees, with client-received checks explicitly cancelled out. This is the other
    // direction — what the client's own registrants owe THEM. Two different documents, not
    // two cuts of one figure, and they are not expected to tie.
    //
    // EVENT-GRAINED, not team-grained. A tournament team pays a deposit in one month and its
    // balance in another; a player on an ARB plan pays across six or ten months. A stored
    // paid_total collapses all of that into one undated number, so it cannot place any of it.
    // Instead every row here is a thing that HAPPENED, on the date it happened:
    //
    //   charge event   at teams.createdate / Registrations.RegistrationTS  -> Billed, Owed
    //   payment event  at Registration_Accounting.createdate               -> Collected
    //
    // That is also what rescues the team that never paid: being charged is itself an event
    // with a date, so an unpaid team is simply the case with one event instead of several,
    // rather than a special case needing its own handling.
    //
    // Money reaches a team by two routes, keyed on registration ROLE rather than job type,
    // so one query serves tournaments and player sites alike (Todd, 2026-08-31):
    //   1. role = Club Rep                                 -> teams.clubrep_registrationid
    //   2. else assigned_teamID != null AND fee_total <> 0  -> teams.teamID
    // The fee_total guard is load-bearing: SELF-ROSTERING DOES NOT CHARGE, and 98.4% of Top
    // Threat non-clubrep registrations are zero-fee self-roster rows. Use `<> 0` not `> 0`:
    // 59 registrations system-wide carry a NEGATIVE fee_total (credits).
    //
    // Payments route the same way with no risk of double counting: VERIFIED on Top Threat,
    // club-rep ledger rows always carry a teamID (13,389 rows / $11,077,132.82) and player
    // ledger rows never do (457 rows / $86,611.77). Disjoint sets, so
    // `ra.TeamId ?? r.AssignedTeamId` places every row exactly once.
    //
    // No payment-method filter: summing every active row reproduces the stored paid_total to
    // the cent, and that is the client actual receipts — credit cards, checks, online
    // corrections ($582K on Top Threat, material) and refunds as negatives.
    //
    // Team-level money only. The club-rep rollup drifts from the sum of its own teams by
    // ~$373K paid / ~$362K owed across 11% of Top Threat reps.
    //
    // Four PROJECTED, sequential queries assembled in memory. Registrations has NO index on
    // assigned_teamID (667K rows, heap-scanned), so the roster count MUST be one grouped
    // aggregate — as a correlated subquery it would be ~8,000 table scans.
    // =====================================================================
    public async Task<List<TeamBillingRecordDto>> GetTeamBillingAsync(
        Guid jobId, DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default)
    {
        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);
        var jobFilter = jobNames.ToList();
        // The date range does TWO jobs here, and neither is the rollup's (Todd, 2026-08-31):
        //
        //   1. It SELECTS THE EVENTS. A job qualifies when its ExpiryUsers falls in the range.
        //      This is what keeps the report about the events you asked about — a July 2026
        //      range takes Top Threat from 125 jobs down to 5.
        //   2. For those jobs it reports AS OF the end date — their whole financial history up
        //      to it, however far back that reaches. The start date does NOT clip transactions.
        //
        // Rule 2 is what makes Owed honest. Clipping transactions at the front dropped any team
        // charged before the window and took its balance with it: a July window on Fall Draw
        // 2026 reported $8,210.00 outstanding against a real receivable of $49,452.50, because
        // only 21 of its 258 teams happened to register that month. A balance has no July
        // version. Rule 1 is what stops rule 2 from dragging in the customer's entire history.
        //
        // Months still matter INSIDE the result — the pivot's year/month levels are what put a
        // deposit in its month, the balance in its, and each ARB draft in its own.
        //
        // Same end-date contract as the rollup: advanced a day, comparisons are < end.
        DateTime? endEx = endDate?.Date.AddDays(1);

        // --- Q1: team identity + the team own charge. NOT date-filtered: a team charged in
        //         January can still have a payment event inside a July window, and it has to
        //         be identifiable to carry that row labels. ---
        var teams = await (
            from t in _context.Teams
            join j in _context.Jobs on t.JobId equals j.JobId
            join a in _context.Agegroups on t.AgegroupId equals a.AgegroupId
            where customerIds.Contains(j.CustomerId)
                // The date range picks the EVENTS, not the transactions: a job qualifies when
                // its ExpiryUsers falls inside the range. Without this, as-of pulled in every
                // job the customer has ever run (125 on Top Threat) to report on a handful.
                && (startDate == null || j.ExpiryUsers >= startDate)
                && (endEx == null || j.ExpiryUsers < endEx)
                && t.Active == true
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            select new
            {
                t.TeamId,
                JobName = j.JobName!,
                t.Createdate,
                ClubName = t.ClubrepRegistration != null ? t.ClubrepRegistration.ClubName : null,
                AgegroupName = a.AgegroupName,
                t.TeamName,
                Billed = t.FeeTotal,
                Owed = t.OwedTotal,
                Discount = t.FeeDiscount
            })
            .AsNoTracking()
            .ToListAsync(ct);

        if (teams.Count == 0)
        {
            return [];
        }

        // --- Q2: roster headcount. Self-rostered players ARE counted — they are roster
        //         size, not revenue. Mirrors the CADT tree PlayerCount definition. ---
        var rosterCounts = await (
            from r in _context.Registrations
            join t in _context.Teams on r.AssignedTeamId equals t.TeamId
            join j in _context.Jobs on t.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                // The date range picks the EVENTS, not the transactions: a job qualifies when
                // its ExpiryUsers falls inside the range. Without this, as-of pulled in every
                // job the customer has ever run (125 on Top Threat) to report on a handful.
                && (startDate == null || j.ExpiryUsers >= startDate)
                && (endEx == null || j.ExpiryUsers < endEx)
                && t.Active == true
                && r.BActive == true
                && r.RoleId == RoleConstants.Player
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            group r by t.TeamId into g
            select new { TeamId = g.Key, Count = g.Count() })
            .AsNoTracking()
            .ToListAsync(ct);

        // --- Q3: player charge events, dated at the PLAYER own registration. ---
        var playerCharges = await (
            from r in _context.Registrations
            join t in _context.Teams on r.AssignedTeamId equals t.TeamId
            join j in _context.Jobs on t.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                // The date range picks the EVENTS, not the transactions: a job qualifies when
                // its ExpiryUsers falls inside the range. Without this, as-of pulled in every
                // job the customer has ever run (125 on Top Threat) to report on a handful.
                && (startDate == null || j.ExpiryUsers >= startDate)
                && (endEx == null || j.ExpiryUsers < endEx)
                && t.Active == true
                // `|| FeeDiscount != 0` is load-bearing, not defensive: a FULLY comped
                // registration is charged to zero, so a fee-only guard drops exactly the
                // registrations the Discounts column exists to report. System-wide that is
                // 4,022 registrations carrying $3,396,848.43 — 61% of all routed discount
                // money. Self-rostering still falls out, which is what the guard is for.
                && (r.FeeTotal != 0m || r.FeeDiscount != 0m)
                && (endEx == null || r.RegistrationTs < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            group new { r.FeeTotal, r.OwedTotal, r.FeeDiscount } by new
            {
                t.TeamId,
                Year = r.RegistrationTs.Year,
                Month = r.RegistrationTs.Month
            } into g
            select new
            {
                g.Key.TeamId,
                g.Key.Year,
                g.Key.Month,
                Billed = g.Sum(x => x.FeeTotal),
                Owed = g.Sum(x => x.OwedTotal),
                Discount = g.Sum(x => x.FeeDiscount)
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // --- Q4: payment events, dated at the LEDGER ROW. This is what splits a deposit
        //         from its balance, and an ARB plan into its individual drafts. ---
        var payments = await (
            from ra in _context.RegistrationAccounting
            join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
            join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
            join t in _context.Teams on (ra.TeamId ?? r.AssignedTeamId) equals t.TeamId
            join j in _context.Jobs on t.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                // The date range picks the EVENTS, not the transactions: a job qualifies when
                // its ExpiryUsers falls inside the range. Without this, as-of pulled in every
                // job the customer has ever run (125 on Top Threat) to report on a handful.
                && (startDate == null || j.ExpiryUsers >= startDate)
                && (endEx == null || j.ExpiryUsers < endEx)
                && t.Active == true
                && ra.Active == true
                && ra.Createdate != null
                && (endEx == null || ra.Createdate < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            // Corrections and Refunds are SUBSETS of Collected, broken out as memo columns —
            // they are not added to it. Corrections are summed NET, both signs (Todd,
            // 2026-08-31): the positives are money the director took outside the system, the
            // negatives are write-offs. On Top Threat that is +$603,383.36 against
            // -$21,639.50, so this column is overwhelmingly money IN, not comps.
            // Refunds (Credit Card Credit) are stored negative without exception — 4,109 of
            // 4,109 rows system-wide — so no sign correction is applied anywhere.
            group new
            {
                ra.Payamt,
                IsCorrection = apm.PaymentMethod == "Online Correction By Client"
                            || apm.PaymentMethod == "Online Correction By TSIC",
                IsRefund = apm.PaymentMethod == "Credit Card Credit"
            } by new
            {
                t.TeamId,
                Year = ra.Createdate!.Value.Year,
                Month = ra.Createdate.Value.Month
            } into g
            select new
            {
                g.Key.TeamId,
                g.Key.Year,
                g.Key.Month,
                Collected = g.Sum(x => x.Payamt ?? 0m),
                Corrections = g.Sum(x => x.IsCorrection ? (x.Payamt ?? 0m) : 0m),
                Refunds = g.Sum(x => x.IsRefund ? (x.Payamt ?? 0m) : 0m)
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // --- Assemble. Key is (team, year, month); a charge and a payment landing in the
        //     same month collapse onto one row, which is what a reader expects to see. ---
        var countByTeam = rosterCounts.ToDictionary(x => x.TeamId, x => x.Count);
        var identity = teams.ToDictionary(x => x.TeamId);
        var cells = new Dictionary<(Guid TeamId, int Year, int Month),
            (decimal Billed, decimal Collected, decimal Owed, decimal Discounts, decimal Corrections, decimal Refunds)>();

        // Defaulted so every call site names only the figures it actually contributes — a
        // charge says billed/discount, a payment says collected/corrections/refunds, and
        // neither carries a row of zeroes for the other's columns.
        void Accrue(Guid teamId, int year, int month,
            decimal billed = 0m, decimal collected = 0m, decimal owed = 0m,
            decimal discounts = 0m, decimal corrections = 0m, decimal refunds = 0m)
        {
            var key = (teamId, year, month);
            var cur = cells.TryGetValue(key, out var v)
                ? v
                : (Billed: 0m, Collected: 0m, Owed: 0m, Discounts: 0m, Corrections: 0m, Refunds: 0m);
            cells[key] = (
                cur.Billed + billed,
                cur.Collected + collected,
                cur.Owed + owed,
                cur.Discounts + discounts,
                cur.Corrections + corrections,
                cur.Refunds + refunds);
        }

        // Owed is COMPUTED as of the end date, not read from teams.owed_total, because the
        // stored column is the balance RIGHT NOW — it would contradict an as-of report whose
        // end date is in the past. Deriving it from this report's own Billed and Collected
        // makes the three columns self-consistent by construction.
        //
        // VERIFIED against the stored column on Fall Draw 2026, whole history: computed
        // $49,452.50 vs stored $49,452.50. The underlying identity fee_total - paid_total =
        // owed_total holds on 46,349 of 46,381 active teams system-wide (32 exceptions,
        // $3,574.24 total drift — pre-existing data, not introduced here).
        var owedAsOf = new Dictionary<Guid, decimal>();

        void Charge(Guid teamId, decimal amount)
            => owedAsOf[teamId] = owedAsOf.GetValueOrDefault(teamId) + amount;

        // Team charge events — the team fee, at the month it registered. A team charged after
        // the end date did not exist yet, so it is absent entirely rather than showing zeros.
        foreach (var t in teams)
        {
            if (endEx != null && t.Createdate >= endEx)
            {
                continue;
            }
            Accrue(t.TeamId, t.Createdate.Year, t.Createdate.Month,
                billed: t.Billed ?? 0m, discounts: t.Discount ?? 0m);
            Charge(t.TeamId, t.Billed ?? 0m);
        }

        foreach (var p in playerCharges)
        {
            Accrue(p.TeamId, p.Year, p.Month, billed: p.Billed, discounts: p.Discount);
            Charge(p.TeamId, p.Billed);
        }

        // Corrections and Refunds ride the SAME rows they are part of — they are a breakdown
        // of Collected, so they must never touch Charge() or they would be subtracted twice.
        foreach (var p in payments)
        {
            Accrue(p.TeamId, p.Year, p.Month,
                collected: p.Collected, corrections: p.Corrections, refunds: p.Refunds);
            Charge(p.TeamId, -p.Collected);
        }

        // Place each team's balance on the month it registered — the one row every team has.
        // Per TEAM, not per month: spreading it across months would make a payment-only month
        // read as negative Owed, which is an artifact, not a receivable. Summed up the pivot,
        // the job total is the true outstanding as of the end date.
        foreach (var t in teams)
        {
            if (endEx != null && t.Createdate >= endEx)
            {
                continue;
            }
            var balance = owedAsOf.GetValueOrDefault(t.TeamId);
            if (balance != 0m)
            {
                Accrue(t.TeamId, t.Createdate.Year, t.Createdate.Month, owed: balance);
            }
        }

        return cells
            .Select(kv =>
            {
                var t = identity[kv.Key.TeamId];
                var count = countByTeam.GetValueOrDefault(kv.Key.TeamId, 0);
                var teamName = string.IsNullOrWhiteSpace(t.TeamName) ? "(Unnamed team)" : t.TeamName;
                return new TeamBillingRecordDto
                {
                    JobName = t.JobName,
                    Year = kv.Key.Year,
                    Month = kv.Key.Month,
                    // Established fallback — same string the CADT tree uses.
                    ClubName = string.IsNullOrWhiteSpace(t.ClubName) ? "(No Club)" : t.ClubName,
                    // public-rosters label convention: "{agegroup}:{team} ({rosterCount})".
                    TeamLabel = $"{t.AgegroupName}:{teamName} ({count})",
                    Billed = Math.Round(kv.Value.Billed, 2, MidpointRounding.AwayFromZero),
                    Collected = Math.Round(kv.Value.Collected, 2, MidpointRounding.AwayFromZero),
                    Owed = Math.Round(kv.Value.Owed, 2, MidpointRounding.AwayFromZero),
                    Discounts = Math.Round(kv.Value.Discounts, 2, MidpointRounding.AwayFromZero),
                    Corrections = Math.Round(kv.Value.Corrections, 2, MidpointRounding.AwayFromZero),
                    Refunds = Math.Round(kv.Value.Refunds, 2, MidpointRounding.AwayFromZero)
                };
            })
            .OrderBy(r => r.JobName, StringComparer.Ordinal)
            .ThenBy(r => r.Year)
            .ThenBy(r => r.Month)
            .ThenBy(r => r.ClubName, StringComparer.Ordinal)
            .ThenBy(r => r.TeamLabel, StringComparer.Ordinal)
            .ToList();
    }

    public async Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default)
    {
        var record = await _context.MonthlyJobStats
            .FirstOrDefaultAsync(m => m.Aid == aid, ct)
            ?? throw new KeyNotFoundException($"MonthlyJobStats record with aid {aid} not found.");

        record.CountActivePlayersToDate = request.CountActivePlayersToDate;
        record.CountActivePlayersToDateLastMonth = request.CountActivePlayersToDateLastMonth;
        record.CountNewPlayersThisMonth = request.CountNewPlayersThisMonth;
        record.CountActiveTeamsToDate = request.CountActiveTeamsToDate;
        record.CountActiveTeamsToDateLastMonth = request.CountActiveTeamsToDateLastMonth;
        record.CountNewTeamsThisMonth = request.CountNewTeamsThisMonth;
        record.LebUserId = userId;
        record.Modified = DateTime.Now;

        await _context.SaveChangesAsync(ct);
    }

}
