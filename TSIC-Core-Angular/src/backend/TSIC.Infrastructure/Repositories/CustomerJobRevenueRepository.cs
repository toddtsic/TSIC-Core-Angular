using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.CustomerJobRevenue;
using TSIC.Contracts.Payments;
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
        //   1. It SELECTS THE EVENTS. A job qualifies when it is still LIVE as the range
        //      opens — ExpiryUsers at or after the start date, no upper bound. At 8/1/2026
        //      that is 18 of Top Threat's 125 jobs.
        //      This was "ExpiryUsers falls INSIDE the range" until 2026-09-01. An expiry is
        //      a DEADLINE, so it lands in any given month only by coincidence: an August
        //      2026 window matched ZERO jobs, and an empty receivable report reads as
        //      "nothing is owed" rather than "no event closed in August" (Todd's call).
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
                // it is still LIVE as the range opens. NOT "expiry lands inside the range" —
                // an expiry is a DEADLINE, so it falls in any given month only by coincidence:
                // an 8/1-8/31/2026 window matched 0 of Top Threat's 18 live jobs, because Fall
                // Draw 2026 expires 11/30/2026, and the tab reported an empty receivable that
                // reads as "nothing is owed". Without ANY filter, as-of pulled in every job the
                // customer has ever run (125 on Top Threat) to report on a handful.
                && (startDate == null || j.ExpiryUsers >= startDate)
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
                // Both discount buckets and the late fee — the three charge-side terms of
                // FeeAdj. TotalDiscount() is FeeDiscount + FeeDiscountMp everywhere else in
                // the system; it is spelled out here because EF cannot translate the extension.
                Discount = t.FeeDiscount,
                DiscountMp = t.FeeDiscountMp,
                LateFee = t.FeeLatefee
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
                // it is still LIVE as the range opens. NOT "expiry lands inside the range" —
                // an expiry is a DEADLINE, so it falls in any given month only by coincidence:
                // an 8/1-8/31/2026 window matched 0 of Top Threat's 18 live jobs, because Fall
                // Draw 2026 expires 11/30/2026, and the tab reported an empty receivable that
                // reads as "nothing is owed". Without ANY filter, as-of pulled in every job the
                // customer has ever run (125 on Top Threat) to report on a handful.
                && (startDate == null || j.ExpiryUsers >= startDate)
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
                // it is still LIVE as the range opens. NOT "expiry lands inside the range" —
                // an expiry is a DEADLINE, so it falls in any given month only by coincidence:
                // an 8/1-8/31/2026 window matched 0 of Top Threat's 18 live jobs, because Fall
                // Draw 2026 expires 11/30/2026, and the tab reported an empty receivable that
                // reads as "nothing is owed". Without ANY filter, as-of pulled in every job the
                // customer has ever run (125 on Top Threat) to report on a handful.
                && (startDate == null || j.ExpiryUsers >= startDate)
                && t.Active == true
                // `|| FeeDiscount != 0` is load-bearing, not defensive: a FULLY comped
                // registration is charged to zero, so a fee-only guard drops exactly the
                // registrations the Discounts column exists to report. System-wide that is
                // 4,022 registrations carrying $3,396,848.43 — 61% of all routed discount
                // money. Self-rostering still falls out, which is what the guard is for.
                && (r.FeeTotal != 0m || r.FeeDiscount != 0m)
                && (endEx == null || r.RegistrationTs < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            group new { r.FeeTotal, r.OwedTotal, r.FeeDiscount, r.FeeDiscountMp, r.FeeLatefee } by new
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
                Discount = g.Sum(x => x.FeeDiscount),
                DiscountMp = g.Sum(x => x.FeeDiscountMp),
                LateFee = g.Sum(x => x.FeeLatefee)
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // --- Q4: payment events, dated at the LEDGER ROW. This is what splits a deposit
        //         from its balance, and an ARB plan into its individual drafts. ---
        //
        // Classified on the METHOD ID, never the display name. PaymentMethodIds exists for
        // exactly this reason — its own header warns that a text test drifts across variants
        // ("Credit Card Payment" vs "…PIF", "Correction" vs "Online Correction By Client"),
        // and it is the single classifier the payment resolver sums on, so a second opinion
        // here is how two reads of the same ledger come to disagree.
        var correctionMethodIds = PaymentMethodIds.Correction.ToArray();
        var creditCardCreditId = PaymentMethodIds.CreditCardCredit;

        var payments = await (
            from ra in _context.RegistrationAccounting
            join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
            join t in _context.Teams on (ra.TeamId ?? r.AssignedTeamId) equals t.TeamId
            join j in _context.Jobs on t.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                // The date range picks the EVENTS, not the transactions: a job qualifies when
                // it is still LIVE as the range opens. NOT "expiry lands inside the range" —
                // an expiry is a DEADLINE, so it falls in any given month only by coincidence:
                // an 8/1-8/31/2026 window matched 0 of Top Threat's 18 live jobs, because Fall
                // Draw 2026 expires 11/30/2026, and the tab reported an empty receivable that
                // reads as "nothing is owed". Without ANY filter, as-of pulled in every job the
                // customer has ever run (125 on Top Threat) to report on a handful.
                && (startDate == null || j.ExpiryUsers >= startDate)
                && t.Active == true
                && ra.Active == true
                && ra.Createdate != null
                && (endEx == null || ra.Createdate < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            // Corrections and Refunds are both SUBSETS of Collected — memos, never added to
            // it. Corrections are summed NET across both signs (Todd, 2026-08-31): the
            // positives are money the director took outside the system, the negatives are
            // write-offs. On Top Threat that is +$581,743.86 net across 1,019 rows, so this
            // is overwhelmingly money IN, not comps — which is why it feeds Adj by SUBTRACTION
            // (a credit correction lowers what is owed).
            // Refunds (Credit Card Credit) are stored negative without exception — all 165
            // rows on Top Threat, min -$2,259.50, max -$77.85 — so no sign correction anywhere.
            group new
            {
                ra.Payamt,
                IsCorrection = correctionMethodIds.Contains(ra.PaymentMethodId),
                IsRefund = ra.PaymentMethodId == creditCardCreditId
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
            (decimal Billed, decimal Collected, decimal Owed, decimal Adj, decimal Refunds)>();

        // Defaulted so every call site names only the figures it actually contributes — a
        // charge says billed/adj, a payment says collected/adj/refunds, and neither carries a
        // row of zeroes for the other's columns.
        //
        // Adj is the ONE signed column that replaced separate Discounts and Corrections
        // (Todd, 2026-09-01): lateFee - discount - correction, matching
        // PaymentState.FeeAdjustment, which the player and club-rep grids already display as
        // "Fee-Adj". It accumulates from BOTH sides of the ledger, which is why it is a memo
        // and adds to nothing: the charge-side terms are already inside Billed, the correction
        // term already inside Collected.
        void Accrue(Guid teamId, int year, int month,
            decimal billed = 0m, decimal collected = 0m, decimal owed = 0m,
            decimal adj = 0m, decimal refunds = 0m)
        {
            var key = (teamId, year, month);
            var cur = cells.TryGetValue(key, out var v)
                ? v
                : (Billed: 0m, Collected: 0m, Owed: 0m, Adj: 0m, Refunds: 0m);
            cells[key] = (
                cur.Billed + billed,
                cur.Collected + collected,
                cur.Owed + owed,
                cur.Adj + adj,
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
                billed: t.Billed ?? 0m,
                // Charge-side half of FeeAdj: a late fee makes the team owe MORE (positive),
                // a discount less (negative). A NEGATIVE fee_discount is a surcharge and
                // flips this positive — 4 teams system-wide carry one, so the sign is real,
                // not defensive.
                adj: (t.LateFee ?? 0m) - ((t.Discount ?? 0m) + (t.DiscountMp ?? 0m)));
            Charge(t.TeamId, t.Billed ?? 0m);
        }

        foreach (var p in playerCharges)
        {
            Accrue(p.TeamId, p.Year, p.Month, billed: p.Billed,
                adj: p.LateFee - (p.Discount + p.DiscountMp));
            Charge(p.TeamId, p.Billed);
        }

        // Corrections and Refunds ride the SAME rows they are part of — they are a breakdown
        // of Collected, so they must never touch Charge() or they would be subtracted twice.
        // The correction term enters Adj NEGATED, per FeeAdjustment's `- correction`: a credit
        // correction is money credited against the balance, so it lowers what is owed.
        foreach (var p in payments)
        {
            Accrue(p.TeamId, p.Year, p.Month,
                collected: p.Collected, adj: -p.Corrections, refunds: p.Refunds);
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
                    Adj = Math.Round(kv.Value.Adj, 2, MidpointRounding.AwayFromZero),
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

    // =====================================================================
    // YEAR-OVER-YEAR REVIEW — same basis as Teams/Players to Customer
    //
    // The question this answers is "how are we doing versus the same point last year", and
    // every design choice below follows from the words SAME POINT.
    //
    // THE AS-OF PIN. Each year column aggregates from the beginning of its jobs' history to a
    // cutoff, and that cutoff is the date asked shifted back a whole number of years. Ask on
    // 8/31/2026 with a 2026 anchor and the 2025 column is measured at 8/31/2025 — deliberately
    // NOT at what 2025 finished with. Comparing a season still selling against a prior season's
    // FINAL figure manufactures a collapse that is only the calendar not having caught up.
    //
    // The pin travels with the date asked, so historical columns are not frozen: run the report
    // a month later and last season's column advances a month too, converging on its final
    // number about a year after the money stopped moving.
    //
    // SCOPE (Todd, 2026-09-01). Active jobs are those still LIVE when the range opens —
    // ExpiryUsers at or after the start date, with no upper bound. Their group keys then reach
    // into the ENTIRE customer history with no date bound at all. That unboundedness is
    // deliberate: a prior season that was collected well has no recent transactions, so any
    // activity-based inclusion test drops exactly the seasons worth comparing against and
    // keeps the ones that never got paid.
    //
    // GROUPING IS BY NAME, ARITHMETIC IS BY jobId (Todd, 2026-09-01). Money is aggregated
    // strictly per jobId and only then attributed to a (group, year) cell, so name handling can
    // never disturb a figure. A cell may legitimately hold more than one job — a season that
    // split into North and South — and because the jobs stay separable the composing names ride
    // out to the client on every column. That is the safety rail: name grouping is a heuristic
    // whose failure mode is a confident chart against a wrong baseline, and a reader spots a bad
    // pairing instantly where no parser will.
    //
    // NO TEAM GRAIN. The Teams/Players tab computes Owed per team and sums; summation is linear,
    // so at job level Owed is simply Billed less Collected over the same populations. The team
    // join is still made — it is what enforces the routing rules and the active-team filter, so
    // both reports read exactly the same population.
    // =====================================================================
    public async Task<YoyRevenueResponseDto> GetYoyRevenueAsync(
        Guid jobId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        // Chart readability, not a data bound. Deeper history stays reachable by scrolling.
        const int MaxYearColumns = 6;

        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);
        var asOf = endDate.Date;
        var activeFrom = startDate.Date;
        // A job is LIVE when its registration window has not closed by the time the range
        // opens. NOT "expiry falls inside the range": an expiry is a deadline, so it lands in
        // any given month only by coincidence — on Top Threat an 8/1-8/31/2026 window caught 0
        // of 18 live jobs, because Fall Draw 2026 expires 11/30/2026. There is deliberately no
        // upper bound: a season selling now that closes in 2027 is exactly what the director
        // wants paced against its prior years.

        // Whole customer group — 125 rows on Top Threat, and the historic side needs all of it.
        var allJobs = await _context.Jobs
            .AsNoTracking()
            .Where(j => customerIds.Contains(j.CustomerId) && j.JobName != null)
            .Select(j => new { j.JobId, JobName = j.JobName!, j.Year, j.ExpiryUsers })
            .ToListAsync(ct);

        // Jobs.year is varchar — parsed in memory, same as GetAvailableJobNamesAsync does.
        var jobs = new List<YoyJobRef>();
        var ungrouped = new List<string>();
        foreach (var j in allJobs)
        {
            var year = ParseJobYear(j.Year);
            if (year == null)
            {
                // Only worth reporting if it would otherwise have been on the chart. A dead
                // 2014 job with a blank year is noise; a LIVE job that cannot be placed is a
                // hole in the report the reader would never catch unaided.
                if (j.ExpiryUsers >= activeFrom)
                {
                    ungrouped.Add(j.JobName);
                }
                continue;
            }
            jobs.Add(new YoyJobRef(
                j.JobId, j.JobName, j.ExpiryUsers, year.Value,
                BuildGroupKey(j.JobName, year.Value)));
        }

        // --- The active set picks which lineages appear at all; its newest season anchors the
        //     pin. Max rather than first: a customer can have two seasons open at once. ---
        var anchorByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var labelByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in jobs)
        {
            if (a.ExpiryUsers < activeFrom)
            {
                continue;
            }
            if (!anchorByKey.TryGetValue(a.GroupKey, out var cur) || a.Year > cur)
            {
                anchorByKey[a.GroupKey] = a.Year;
                // Label follows the newest live season, so casing and spacing match the job
                // the director is actually running right now.
                labelByKey[a.GroupKey] = a.GroupKey;
            }
        }

        if (anchorByKey.Count == 0)
        {
            return new YoyRevenueResponseDto
            {
                AsOfDate = asOf,
                Groups = [],
                UngroupedJobNames = ungrouped
            };
        }

        // --- Every job in a live lineage, no date bound. Seasons NEWER than the anchor are
        //     dropped: shifting the pin forward would read a future season against a cutoff it
        //     has not reached, which is not a comparison. ---
        var membersByKey = new Dictionary<string, List<YoyJobRef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var j in jobs)
        {
            if (!anchorByKey.TryGetValue(j.GroupKey, out var anchor) || j.Year > anchor)
            {
                continue;
            }
            if (!membersByKey.TryGetValue(j.GroupKey, out var list))
            {
                list = [];
                membersByKey[j.GroupKey] = list;
            }
            list.Add(j);
        }

        // --- Resolve every (group, year) cell to its cutoff, and every job to the cell it
        //     belongs to. One job, one cell, one cutoff. ---
        var cellJobs = new Dictionary<(string Key, int Year), List<YoyJobRef>>();
        var pinByCell = new Dictionary<(string Key, int Year), DateTime>();
        var pinExByJob = new Dictionary<Guid, DateTime>();

        foreach (var (key, members) in membersByKey)
        {
            var anchor = anchorByKey[key];
            var years = members
                .Select(m => m.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .Take(MaxYearColumns)
                .ToHashSet();

            foreach (var m in members)
            {
                if (!years.Contains(m.Year))
                {
                    continue;
                }
                var cell = (key, m.Year);
                if (!cellJobs.TryGetValue(cell, out var list))
                {
                    list = [];
                    cellJobs[cell] = list;
                    // AddYears is calendar-safe — a Feb 29 ask lands on Feb 28 in a common year.
                    pinByCell[cell] = asOf.AddYears(m.Year - anchor);
                }
                list.Add(m);
                pinExByJob[m.JobId] = pinByCell[cell].AddDays(1);
            }
        }

        // --- Aggregate. Batched by distinct cutoff so each query carries ONE date comparison:
        //     jobs of the same season share a pin, so this is a handful of batches, not one
        //     pass per job. Sequential awaits throughout — shared scoped DbContext. ---
        var billedByJob = new Dictionary<Guid, decimal>();
        var collectedByJob = new Dictionary<Guid, decimal>();
        var refundsByJob = new Dictionary<Guid, decimal>();

        // One signed adjustment per job — lateFee - discount - correction, matching
        // PaymentState.FeeAdjustment. It accumulates from both the charge queries and the
        // payment query, which is precisely why it is a memo that adds to nothing.
        var adjByJob = new Dictionary<Guid, decimal>();

        // Entity counts as of the pin, kept apart BY ROUTE: a charged team and a charged player
        // are different things, and the chart names the one it is actually drawing rather than
        // summing them into a "registrations" figure that is teams on every tournament event.
        // Owing stays combined — it is a state of whatever population is there, not a route.
        // Paid is the difference; see the queries for why it is derived rather than counted,
        // and why neither can come from owed_total.
        var playerCountByJob = new Dictionary<Guid, int>();
        var teamCountByJob = new Dictionary<Guid, int>();
        var owingCountByJob = new Dictionary<Guid, int>();

        // Classified on the METHOD ID, never the display name — PaymentMethodIds is the single
        // classifier the payment resolver sums on, and its own header warns that a text test
        // drifts across method-name variants.
        var correctionMethodIds = PaymentMethodIds.Correction.ToArray();
        var creditCardCreditId = PaymentMethodIds.CreditCardCredit;

        foreach (var batch in pinExByJob.GroupBy(kv => kv.Value))
        {
            var pinEx = batch.Key;
            var batchIds = batch.Select(kv => kv.Key).ToList();

            // Team fees — the club-rep route, dated at teams.createdate.
            var teamCharges = await (
                from t in _context.Teams
                where batchIds.Contains(t.JobId)
                    && t.Active == true
                    && t.Createdate < pinEx
                group new { t.FeeTotal, t.FeeDiscount, t.FeeDiscountMp, t.FeeLatefee } by t.JobId into g
                select new
                {
                    JobId = g.Key,
                    Billed = g.Sum(x => x.FeeTotal),
                    Discount = g.Sum(x => x.FeeDiscount),
                    DiscountMp = g.Sum(x => x.FeeDiscountMp),
                    LateFee = g.Sum(x => x.FeeLatefee)
                })
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var c in teamCharges)
            {
                billedByJob[c.JobId] = billedByJob.GetValueOrDefault(c.JobId) + (c.Billed ?? 0m);
                adjByJob[c.JobId] = adjByJob.GetValueOrDefault(c.JobId)
                    + (c.LateFee ?? 0m) - ((c.Discount ?? 0m) + (c.DiscountMp ?? 0m));
            }

            // Player fees — the assigned-team route, dated at the registration. The
            // `|| FeeDiscount != 0` half of the guard is load-bearing: a fully comped
            // registration is charged to zero, and a fee-only guard drops 4,022 registrations
            // carrying $3,396,848.43 of routed discount money. Self-rostering still falls out.
            var playerCharges = await (
                from r in _context.Registrations
                join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                where batchIds.Contains(t.JobId)
                    && t.Active == true
                    && (r.FeeTotal != 0m || r.FeeDiscount != 0m)
                    && r.RegistrationTs < pinEx
                group new { r.FeeTotal, r.FeeDiscount, r.FeeDiscountMp, r.FeeLatefee } by t.JobId into g
                select new
                {
                    JobId = g.Key,
                    Billed = g.Sum(x => x.FeeTotal),
                    Discount = g.Sum(x => x.FeeDiscount),
                    DiscountMp = g.Sum(x => x.FeeDiscountMp),
                    LateFee = g.Sum(x => x.FeeLatefee)
                })
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var c in playerCharges)
            {
                billedByJob[c.JobId] = billedByJob.GetValueOrDefault(c.JobId) + c.Billed;
                adjByJob[c.JobId] = adjByJob.GetValueOrDefault(c.JobId)
                    + c.LateFee - (c.Discount + c.DiscountMp);
            }

            // --- Entity COUNTS, classified at the SAME cutoff as the money.
            //
            //     These filter r.BActive; the MONEY queries above deliberately do not. That
            //     asymmetry is the ruling, not an oversight (Todd, 2026-09-02): bActive says
            //     whether a registration still counts as a registration, and it has no bearing
            //     on money that actually moved. LI Yellow Jackets:Players 2027 carries a
            //     deactivated registration whose fee was zeroed on drop but which took $875 on
            //     a card and gave $850 back — both real transactions, both listed on the CC
            //     Records tab. Filtering receipts on bActive would delete them and put this tab
            //     at odds with the one showing the transactions themselves.
            //
            //     Filtering the counts also puts them in step with the roster headcount Q2 in
            //     GetTeamBillingAsync, which has always filtered BActive — before this, the same
            //     job reported 696 registrations here and 685 there.
            //
            //     Deliberately NOT read from owed_total. That column is the balance as it stands
            //     TODAY, and this report is as-of: on Girls Elite Players 2025-2026 all 184 of 184
            //     registrations read owed_total = 0 while the bar for that season is mostly red,
            //     because Owed here is Billed - Collected at the pin. Labelling the segments from
            //     the stored balance would print "184 paid / 0 owing" on a bar that is mostly
            //     owing — a contradiction the reader can see.
            //
            //     Shaped as POPULATION + STILL-OWING, with paid derived by subtraction, rather
            //     than as a left join to a payments-per-entity subquery. The join form reads
            //     better and does not translate: EF cannot build a GROUP BY over the transparent
            //     identifier a GroupJoin/DefaultIfEmpty produces, and throws at runtime. A
            //     correlated SUM in the WHERE is plain SQL, and the counts still sum to the
            //     population by construction — paid is whatever is not owing.
            var playerPopulation = await (
                from r in _context.Registrations
                join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                where batchIds.Contains(t.JobId)
                    && t.Active == true
                    && r.BActive == true
                    && (r.FeeTotal != 0m || r.FeeDiscount != 0m)
                    && r.RegistrationTs < pinEx
                group r by t.JobId into g
                select new { JobId = g.Key, Count = g.Count() })
                .AsNoTracking()
                .ToListAsync(ct);

            var playerOwing = await (
                from r in _context.Registrations
                join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                where batchIds.Contains(t.JobId)
                    && t.Active == true
                    && r.BActive == true
                    && (r.FeeTotal != 0m || r.FeeDiscount != 0m)
                    && r.RegistrationTs < pinEx
                    // Player ledger rows never carry a TeamId — that is the route discriminator
                    // the Adjustments tab established, verified disjoint on Top Threat.
                    && r.FeeTotal > _context.RegistrationAccounting
                        .Where(ra => ra.RegistrationId == r.RegistrationId
                            && ra.TeamId == null
                            && ra.Active == true
                            && ra.Createdate != null
                            && ra.Createdate < pinEx)
                        .Sum(ra => ra.Payamt ?? 0m)
                group r by t.JobId into g
                select new { JobId = g.Key, Count = g.Count() })
                .AsNoTracking()
                .ToListAsync(ct);

            // The club-rep route counts the TEAM, because the team is what was charged — the
            // players on it carry no fee of their own. Teams charged nothing are excluded: they
            // are roster containers, not money, and counting them would inflate the population
            // on jobs like STEPS where every team carries a zero fee.
            var teamPopulation = await (
                from t in _context.Teams
                where batchIds.Contains(t.JobId)
                    && t.Active == true
                    && t.Createdate < pinEx
                    && (t.FeeTotal ?? 0m) != 0m
                group t by t.JobId into g
                select new { JobId = g.Key, Count = g.Count() })
                .AsNoTracking()
                .ToListAsync(ct);

            var teamOwing = await (
                from t in _context.Teams
                where batchIds.Contains(t.JobId)
                    && t.Active == true
                    && t.Createdate < pinEx
                    && (t.FeeTotal ?? 0m) != 0m
                    && (t.FeeTotal ?? 0m) > _context.RegistrationAccounting
                        .Where(ra => ra.TeamId == t.TeamId
                            && ra.Active == true
                            && ra.Createdate != null
                            && ra.Createdate < pinEx)
                        .Sum(ra => ra.Payamt ?? 0m)
                group t by t.JobId into g
                select new { JobId = g.Key, Count = g.Count() })
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var p in playerPopulation)
            {
                playerCountByJob[p.JobId] = playerCountByJob.GetValueOrDefault(p.JobId) + p.Count;
            }
            foreach (var p in teamPopulation)
            {
                teamCountByJob[p.JobId] = teamCountByJob.GetValueOrDefault(p.JobId) + p.Count;
            }
            foreach (var p in playerOwing)
            {
                owingCountByJob[p.JobId] = owingCountByJob.GetValueOrDefault(p.JobId) + p.Count;
            }
            foreach (var p in teamOwing)
            {
                owingCountByJob[p.JobId] = owingCountByJob.GetValueOrDefault(p.JobId) + p.Count;
            }

            // Receipts. No payment-method filter — summing every active row is what reproduces
            // the stored paid_total to the cent. Corrections and Refunds are broken out as
            // SUBSETS of Collected, never added to it.
            var payments = await (
                from ra in _context.RegistrationAccounting
                join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                join t in _context.Teams on (ra.TeamId ?? r.AssignedTeamId) equals t.TeamId
                where batchIds.Contains(t.JobId)
                    && t.Active == true
                    && ra.Active == true
                    && ra.Createdate != null
                    && ra.Createdate < pinEx
                group new
                {
                    ra.Payamt,
                    IsCorrection = correctionMethodIds.Contains(ra.PaymentMethodId),
                    IsRefund = ra.PaymentMethodId == creditCardCreditId
                } by t.JobId into g
                select new
                {
                    JobId = g.Key,
                    Collected = g.Sum(x => x.Payamt ?? 0m),
                    Corrections = g.Sum(x => x.IsCorrection ? (x.Payamt ?? 0m) : 0m),
                    Refunds = g.Sum(x => x.IsRefund ? (x.Payamt ?? 0m) : 0m)
                })
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var p in payments)
            {
                collectedByJob[p.JobId] = collectedByJob.GetValueOrDefault(p.JobId) + p.Collected;
                // Negated, per FeeAdjustment's `- correction`: a credit correction is money
                // credited against the balance, so it lowers what is owed.
                adjByJob[p.JobId] = adjByJob.GetValueOrDefault(p.JobId) - p.Corrections;
                refundsByJob[p.JobId] = refundsByJob.GetValueOrDefault(p.JobId) + p.Refunds;
            }
        }

        // --- Assemble. Per-job figures are attributed to their cell here and nowhere earlier. ---
        var groups = new List<YoyEventGroupDto>();

        foreach (var (key, _) in membersByKey)
        {
            var anchor = anchorByKey[key];
            var columns = new List<YoyYearColumnDto>();

            foreach (var cell in cellJobs)
            {
                if (!string.Equals(cell.Key.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pin = pinByCell[cell.Key];
                var pinEx = pin.AddDays(1);
                decimal billed = 0m, collected = 0m, adj = 0m, refunds = 0m;
                var playerCount = 0;
                var teamCount = 0;
                var owingCount = 0;

                foreach (var m in cell.Value)
                {
                    billed += billedByJob.GetValueOrDefault(m.JobId);
                    collected += collectedByJob.GetValueOrDefault(m.JobId);
                    adj += adjByJob.GetValueOrDefault(m.JobId);
                    refunds += refundsByJob.GetValueOrDefault(m.JobId);
                    playerCount += playerCountByJob.GetValueOrDefault(m.JobId);
                    teamCount += teamCountByJob.GetValueOrDefault(m.JobId);
                    owingCount += owingCountByJob.GetValueOrDefault(m.JobId);
                }

                columns.Add(new YoyYearColumnDto
                {
                    Year = cell.Key.Year,
                    AsOf = pin,
                    // Still selling AT ITS OWN CUTOFF — so a concluded 2024 is not marked
                    // in-flight merely because today is later than its pin.
                    IsActive = cell.Value.Exists(m => m.ExpiryUsers >= pinEx),
                    JobNames = cell.Value
                        .Select(m => m.JobName)
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToList(),
                    Billed = Math.Round(billed, 2, MidpointRounding.AwayFromZero),
                    Collected = Math.Round(collected, 2, MidpointRounding.AwayFromZero),
                    Adj = Math.Round(adj, 2, MidpointRounding.AwayFromZero),
                    Refunds = Math.Round(refunds, 2, MidpointRounding.AwayFromZero),
                    // Computed, never read from teams.owed_total — that column is the balance
                    // right now and would contradict a column measured in the past.
                    Owed = Math.Round(billed - collected, 2, MidpointRounding.AwayFromZero),
                    TeamCount = teamCount,
                    PlayerCount = playerCount,
                    PaidCount = Math.Max(0, playerCount + teamCount - owingCount),
                    OwingCount = owingCount
                });
            }

            if (columns.Count == 0)
            {
                continue;
            }

            groups.Add(new YoyEventGroupDto
            {
                GroupLabel = labelByKey.GetValueOrDefault(key, key),
                AnchorYear = anchor,
                Years = columns.OrderBy(c => c.Year).ToList()
            });
        }

        return new YoyRevenueResponseDto
        {
            AsOfDate = asOf,
            Groups = groups.OrderBy(g => g.GroupLabel, StringComparer.Ordinal).ToList(),
            UngroupedJobNames = ungrouped.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList()
        };
    }

    /// <summary>One job's identity for YoY placement. Money never travels on this record.</summary>
    private sealed record YoyJobRef(
        Guid JobId, string JobName, DateTime ExpiryUsers, int Year, string GroupKey);

    /// <summary>
    /// <c>Jobs.year</c> is varchar and not validated on write. Anything that is not a plausible
    /// 4-digit season is refused rather than coerced — a job that cannot be placed in a column
    /// is reported to the reader, not guessed at.
    /// </summary>
    private static int? ParseJobYear(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var s = raw.Trim();
        if (s.Length != 4 || !int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }
        return year is >= 1990 and <= 2100 ? year : null;
    }

    /// <summary>
    /// The lineage key: the job name with its SEASON DESIGNATOR removed, so every year of one
    /// event collapses to a single group.
    /// </summary>
    /// <remarks>
    /// This used to strip only the job's OWN <c>Jobs.year</c> where it stood alone, and that was
    /// wrong twice over on real data (found on STEPS Lacrosse California, 2026-09-02):
    ///
    ///   JobName                          Jobs.year
    ///   Girls Elite Players 2020-2021    2021
    ///   Girls Elite Players 2026-2027    2027
    ///
    /// The name carries a SPAN, and Jobs.year is the SECOND year of it. "2021" inside "2020-2021"
    /// is not standalone — a digit precedes it — so nothing was stripped and every single season
    /// became its own lineage. Twenty-two "events" for what are really a handful, no history under
    /// any of them, and worse: a lineage whose only member had expired was no longer live at the
    /// range start, so it was dropped from the report altogether. That is why seasons through
    /// 2024 were missing rather than merely uncompared.
    ///
    /// So the rule is now about the SHAPE of a season designator, not about one job's stored year:
    /// a standalone four-digit year, or a span of two joined by a dash or slash, with the second
    /// half written either way ("2024-2025", "2024-25"). Jobs.year is still what places a job in
    /// its column — it is only no longer trusted to describe the name.
    ///
    /// Deliberately NOT a regex: this runs over every job in a customer group on every request,
    /// and the boundary rules (no digit or letter either side) are the whole correctness argument
    /// — worth reading as code rather than hiding in a pattern.
    /// </remarks>
    private static string BuildGroupKey(string jobName, int year)
    {
        // `year` is no longer used to choose what to strip — see the remarks. It stays in the
        // signature because the caller has it and a future rule may want it.
        _ = year;

        var stripped = new StringBuilder(jobName.Length);

        var i = 0;
        while (i < jobName.Length)
        {
            var len = SeasonTokenLength(jobName, i);
            if (len > 0)
            {
                i += len;
                continue;
            }
            stripped.Append(jobName[i]);
            i++;
        }

        // Collapse the whitespace the removal left behind, so "Fall  Draw" cannot fork a group.
        var collapsed = new StringBuilder(stripped.Length);
        var lastWasSpace = false;
        foreach (var ch in stripped.ToString())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    collapsed.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }
            collapsed.Append(ch);
            lastWasSpace = false;
        }

        var key = collapsed.ToString().Trim(' ', '-', '–', '—', ',', ':', '/');
        // A name that was NOTHING but its season leaves nothing to group on — keep the original
        // rather than emit an empty key that would swallow every other such job.
        return key.Length == 0 ? jobName.Trim() : key;
    }

    /// <summary>
    /// Length of the season designator starting at <paramref name="start"/>, or 0 if there is
    /// none. Matches "2026" and "2026-2027" / "2026-27" / "2026/27" and the dash variants.
    /// </summary>
    private static int SeasonTokenLength(string s, int start)
    {
        // A designator never begins mid-token: a preceding digit means we are inside a longer
        // number, a preceding letter means it is part of a word ("U2026" is not a season).
        if (start > 0 && (char.IsDigit(s[start - 1]) || char.IsLetter(s[start - 1])))
        {
            return 0;
        }

        if (!IsYearAt(s, start))
        {
            return 0;
        }

        var end = start + 4;

        // Optional second half: a separator, then two OR four digits. Spaces are allowed around
        // the separator because "2026 - 2027" is written that way often enough to matter.
        var probe = end;
        while (probe < s.Length && s[probe] == ' ')
        {
            probe++;
        }
        if (probe < s.Length && (s[probe] == '-' || s[probe] == '/' || s[probe] == '–' || s[probe] == '—'))
        {
            probe++;
            while (probe < s.Length && s[probe] == ' ')
            {
                probe++;
            }
            if (IsYearAt(s, probe))
            {
                end = probe + 4;
            }
            else if (probe + 2 <= s.Length && char.IsDigit(s[probe]) && char.IsDigit(s[probe + 1])
                && (probe + 2 == s.Length || !char.IsDigit(s[probe + 2])))
            {
                end = probe + 2;
            }
        }

        // And it never ends mid-token either.
        if (end < s.Length && (char.IsDigit(s[end]) || char.IsLetter(s[end])))
        {
            return 0;
        }

        return end - start;
    }

    /// <summary>
    /// Four digits at <paramref name="at"/> forming a plausible season year. Bounded so a street
    /// number or a jersey number cannot be mistaken for one.
    /// </summary>
    private static bool IsYearAt(string s, int at)
    {
        if (at + 4 > s.Length)
        {
            return false;
        }
        for (var k = at; k < at + 4; k++)
        {
            if (!char.IsDigit(s[k]))
            {
                return false;
            }
        }
        if (at + 4 < s.Length && char.IsDigit(s[at + 4]))
        {
            return false;
        }
        var value = ((s[at] - '0') * 1000) + ((s[at + 1] - '0') * 100) + ((s[at + 2] - '0') * 10) + (s[at + 3] - '0');
        return value is >= 1990 and <= 2100;
    }

    // =====================================================================
    // ADJUSTMENTS — the entity-level detail behind the Adj column
    //
    // UNDATED, and that is the honest shape (Todd, 2026-09-01). Every other detail tab buckets
    // by Year/Month because its rows are dated ledger events. Two of the three adjustment terms
    // are not events at all: fee_discount and fee_latefee are stamped columns on the entity with
    // no timestamp, no author and no reason. There is no adjustment-history table anywhere —
    // fees.FeeModifiers holds the CONFIGURATION (3 rows system-wide, all LateFee), never an
    // application. Inventing a date for a stamped balance would be a fiction, so this tab
    // reports a rollup and says so.
    //
    // Still AS OF the end date, through the inclusion rule rather than a date column: an entity
    // is in scope when it was CHARGED by the cutoff, and its correction rows — which genuinely
    // are dated — are cut at the same cutoff.
    //
    // THE ENTITY IS THE MONEY-BEARING ONE, which depends on role: a club rep's money lives on
    // Leagues.teams, everyone else's on their own registration. VERIFIED 2026-09-01: all 8
    // club-rep registrations carrying a non-zero fee_discount carry EXACTLY their own teams'
    // total ($4,132.25 both sides, row for row), so reading the team rather than the rep drops
    // nothing and is what prevents double counting.
    //
    // Components are deliberately NOT broken out. fee_discount is a blended column — early bird
    // is stamped from the cascade and discount codes += onto it afterwards — so a typed split
    // was never recoverable from the data, and a tab that showed one would be inventing it.
    //
    // Rows whose net adjustment is zero are omitted: this tab exists to show the entities that
    // HAVE an adjustment, and on Top Threat that is a few thousand rows out of a hundred
    // thousand registrations.
    // =====================================================================
    public async Task<List<AdjustmentRecordDto>> GetAdjustmentsAsync(
        Guid jobId, DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default)
    {
        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);
        var jobFilter = jobNames.ToList();
        DateTime? endEx = endDate?.Date.AddDays(1);

        // Classified on the METHOD ID, never the display name — same single classifier the
        // payment resolver sums on.
        var correctionMethodIds = PaymentMethodIds.Correction.ToArray();

        // --- Q1: the CLUB-REP route. Team money is native on Leagues.teams. ---
        //
        // No roster count in the label, unlike the Teams/Players tab: that count costs a grouped
        // scan of Registrations (667K rows, no index on assigned_teamID) and this tab is about
        // money adjustments, not roster size.
        var teams = await (
            from t in _context.Teams
            join j in _context.Jobs on t.JobId equals j.JobId
            join a in _context.Agegroups on t.AgegroupId equals a.AgegroupId
            where customerIds.Contains(j.CustomerId)
                && (startDate == null || j.ExpiryUsers >= startDate)
                && t.Active == true
                && (endEx == null || t.Createdate < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            select new
            {
                t.TeamId,
                JobName = j.JobName!,
                ClubName = t.ClubrepRegistration != null ? t.ClubrepRegistration.ClubName : null,
                a.AgegroupName,
                t.TeamName,
                Discount = t.FeeDiscount,
                DiscountMp = t.FeeDiscountMp,
                LateFee = t.FeeLatefee
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // --- Q2: the REGISTRATION route. Tests the FIELD, not a role list, so it cannot go
        //         stale when another role starts carrying money. The fee_latefee arm of the
        //         guard is not decoration: the first late-fee window in the system opens
        //         2026-09-14, and without it a registration whose ONLY adjustment is a late fee
        //         would be absent from the tab that exists to show it. ---
        var regs = await (
            from r in _context.Registrations
            join t in _context.Teams on r.AssignedTeamId equals t.TeamId
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join j in _context.Jobs on t.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (startDate == null || j.ExpiryUsers >= startDate)
                && t.Active == true
                && (r.FeeTotal != 0m || r.FeeDiscount != 0m || r.FeeLatefee != 0m)
                && (endEx == null || r.RegistrationTs < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            select new
            {
                r.RegistrationId,
                JobName = j.JobName!,
                r.ClubName,
                u.FirstName,
                u.LastName,
                Discount = r.FeeDiscount,
                DiscountMp = r.FeeDiscountMp,
                LateFee = r.FeeLatefee
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // --- Q3: correction rows, cut at the same cutoff. Grouped so a correction lands on the
        //         entity that owns it: club-rep ledger rows always carry a TeamId and player
        //         rows never do (verified on Top Threat: 13,389 vs 457 rows, disjoint), so the
        //         presence of TeamId IS the route discriminator. ---
        var corrections = await (
            from ra in _context.RegistrationAccounting
            join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
            join t in _context.Teams on (ra.TeamId ?? r.AssignedTeamId) equals t.TeamId
            join j in _context.Jobs on t.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (startDate == null || j.ExpiryUsers >= startDate)
                && t.Active == true
                && ra.Active == true
                && ra.Createdate != null
                && (endEx == null || ra.Createdate < endEx)
                && correctionMethodIds.Contains(ra.PaymentMethodId)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            group ra.Payamt by new { ra.TeamId, ra.RegistrationId } into g
            select new
            {
                g.Key.TeamId,
                g.Key.RegistrationId,
                Amount = g.Sum(x => x ?? 0m)
            })
            .AsNoTracking()
            .ToListAsync(ct);

        var correctionByTeam = new Dictionary<Guid, decimal>();
        var correctionByReg = new Dictionary<Guid, decimal>();
        foreach (var c in corrections)
        {
            if (c.TeamId != null)
            {
                correctionByTeam[c.TeamId.Value] =
                    correctionByTeam.GetValueOrDefault(c.TeamId.Value) + c.Amount;
            }
            else if (c.RegistrationId != null)
            {
                correctionByReg[c.RegistrationId.Value] =
                    correctionByReg.GetValueOrDefault(c.RegistrationId.Value) + c.Amount;
            }
        }

        var records = new List<AdjustmentRecordDto>();

        // FeeAdj = lateFee - discount - correction, the same signed figure
        // PaymentState.FeeAdjustment produces for the player and club-rep grids. Positive means
        // the entity owes MORE.
        foreach (var t in teams)
        {
            var adj = (t.LateFee ?? 0m)
                - ((t.Discount ?? 0m) + (t.DiscountMp ?? 0m))
                - correctionByTeam.GetValueOrDefault(t.TeamId);
            if (adj == 0m)
            {
                continue;
            }
            var teamName = string.IsNullOrWhiteSpace(t.TeamName) ? "(Unnamed team)" : t.TeamName;
            records.Add(new AdjustmentRecordDto
            {
                JobName = t.JobName,
                ClubName = string.IsNullOrWhiteSpace(t.ClubName) ? "(No Club)" : t.ClubName,
                EntityType = "Team",
                EntityLabel = $"{t.AgegroupName}:{teamName}",
                Adj = Math.Round(adj, 2, MidpointRounding.AwayFromZero)
            });
        }

        foreach (var r in regs)
        {
            var adj = r.LateFee
                - (r.Discount + r.DiscountMp)
                - correctionByReg.GetValueOrDefault(r.RegistrationId);
            if (adj == 0m)
            {
                continue;
            }
            var name = $"{r.FirstName} {r.LastName}".Trim();
            records.Add(new AdjustmentRecordDto
            {
                JobName = r.JobName,
                ClubName = string.IsNullOrWhiteSpace(r.ClubName) ? "(No Club)" : r.ClubName,
                EntityType = "Registrant",
                EntityLabel = string.IsNullOrWhiteSpace(name) ? "(Unnamed)" : name,
                Adj = Math.Round(adj, 2, MidpointRounding.AwayFromZero)
            });
        }

        return records
            .OrderBy(r => r.JobName, StringComparer.Ordinal)
            .ThenBy(r => r.ClubName, StringComparer.Ordinal)
            .ThenBy(r => r.EntityType, StringComparer.Ordinal)
            .ThenBy(r => r.EntityLabel, StringComparer.Ordinal)
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
