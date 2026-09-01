using TSIC.Contracts.Dtos.CustomerJobRevenue;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Admin;

public class CustomerJobRevenueService : ICustomerJobRevenueService
{
    private readonly ICustomerJobRevenueRepository _repo;
    private readonly ICustomerRepository _customerRepo;

    // Legacy TSIC-owned ADN login IDs — used to pick the correct sproc variant
    private static readonly HashSet<string> TsicAdnLoginIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "4dE5m4WR9ey",
        "teamspt52"
    };

    public CustomerJobRevenueService(
        ICustomerJobRevenueRepository repo,
        ICustomerRepository customerRepo)
    {
        _repo = repo;
        _customerRepo = customerRepo;
    }

    public async Task<List<string>> GetAvailableJobNamesAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _repo.GetAvailableJobNamesAsync(jobId, ct);
    }

    public async Task<RevenueRollupResponseDto> GetRollupAsync(
        Guid jobId, DateTime? startDate, DateTime? endDate,
        List<string> jobNames, CancellationToken ct = default)
    {
        var isTsicAdn = await IsTsicAdnAsync(jobId, ct);
        return await _repo.GetRollupAsync(jobId, isTsicAdn, startDate, endDate, jobNames, ct);
    }

    public async Task<List<JobPaymentRecordDto>> GetPaymentDetailsAsync(
        Guid jobId, string method, DateTime? startDate, DateTime? endDate,
        List<string> jobNames, CancellationToken ct = default)
    {
        var isTsicAdn = await IsTsicAdnAsync(jobId, ct);
        return await _repo.GetPaymentDetailsAsync(jobId, isTsicAdn, method, startDate, endDate, jobNames, ct);
    }

    // Which sproc-era variant the queries emulate is a per-customer DATA classification (is
    // this a legacy TSIC-owned ADN merchant?), not an environment concern. Read the customer's
    // stored ADN login id straight from the DB so the classification is identical in every
    // environment — never route this through the env-gated ADN credential resolver (which
    // would return the shared sandbox login off-Production and misclassify).
    private async Task<bool> IsTsicAdnAsync(Guid jobId, CancellationToken ct)
    {
        var credentials = await _customerRepo.GetAdnCredentialsByJobIdAsync(jobId, ct);
        return credentials?.AdnLoginId != null && TsicAdnLoginIds.Contains(credentials.AdnLoginId);
    }

    /// <summary>
    /// SuperUser live QA: run the legacy sprocs and the EF port over the same scope and diff
    /// them with the golden-master normalization rules. Same-DbContext calls are sequential.
    /// </summary>
    public async Task<LegacyCompareResultDto> CompareWithLegacyAsync(
        Guid jobId, DateTime? startDate, DateTime? endDate,
        List<string> jobNames, CancellationToken ct = default)
    {
        var isTsicAdn = await IsTsicAdnAsync(jobId, ct);
        var jobsMode = jobNames.Count > 0;

        // Legacy sproc always needs dates; jobs mode approximates "complete history" with a
        // wide-open window (matches the verified golden-master methodology).
        var legacyStart = jobsMode ? new DateTime(2000, 1, 1) : startDate!.Value;
        var legacyEnd = jobsMode ? DateTime.Today.AddDays(1) : endDate!.Value;
        var listJobsString = jobsMode ? string.Join(",", jobNames) : string.Empty;

        var legacy = await _repo.GetLegacySprocDataAsync(jobId, legacyStart, legacyEnd, listJobsString, isTsicAdn, ct);
        var efStart = jobsMode ? (DateTime?)null : startDate;
        var efEnd = jobsMode ? (DateTime?)null : endDate;
        var efRollup = await _repo.GetRollupAsync(jobId, isTsicAdn, efStart, efEnd, jobNames, ct);
        var efCc = await _repo.GetPaymentDetailsAsync(jobId, isTsicAdn, "cc", efStart, efEnd, jobNames, ct);
        var efCheck = await _repo.GetPaymentDetailsAsync(jobId, isTsicAdn, "check", efStart, efEnd, jobNames, ct);
        var efEcheck = await _repo.GetPaymentDetailsAsync(jobId, isTsicAdn, "echeck", efStart, efEnd, jobNames, ct);

        var material = new List<string>();
        var pennies = new List<string>();

        // 1) Rollup cells: job|year|month|payMethod → summed amount
        static Dictionary<string, decimal> ToCellMap(IEnumerable<JobRevenueRecordDto> records)
        {
            var map = new Dictionary<string, decimal>();
            foreach (var r in records)
            {
                var key = $"{r.JobName}|{r.Year}|{r.Month}|{r.PayMethod}";
                map[key] = map.TryGetValue(key, out var v) ? v + r.PayAmount : r.PayAmount;
            }
            return map;
        }

        var oldMap = ToCellMap(legacy.RevenueRecords);
        var newMap = ToCellMap(efRollup.RevenueRecords);
        foreach (var key in oldMap.Keys.Union(newMap.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var inOld = oldMap.TryGetValue(key, out var ov);
            var inNew = newMap.TryGetValue(key, out var nv);
            if (!inNew) { material.Add($"ROLLUP missing in NEW: {key} = {ov:0.00}"); continue; }
            if (!inOld) { material.Add($"ROLLUP extra in NEW: {key} = {nv:0.00}"); continue; }
            var delta = Math.Abs(ov - nv);
            if (delta == 0m) { continue; }
            if (delta <= 0.01m) { pennies.Add($"ROLLUP penny-delta {key}: legacy={ov:0.00} new={nv:0.00}"); }
            else { material.Add($"ROLLUP MATERIAL {key}: legacy={ov:0.00} new={nv:0.00}"); }
        }

        // 2) Monthly counts by aid
        var oldCounts = legacy.MonthlyCounts.ToDictionary(c => c.Aid);
        foreach (var c in efRollup.MonthlyCounts)
        {
            if (!oldCounts.Remove(c.Aid, out var o)) { material.Add($"COUNTS extra in NEW: aid={c.Aid}"); continue; }
            if (o.CountActivePlayersToDate != c.CountActivePlayersToDate
                || o.CountActivePlayersToDateLastMonth != c.CountActivePlayersToDateLastMonth
                || o.CountNewPlayersThisMonth != c.CountNewPlayersThisMonth
                || o.CountActiveTeamsToDate != c.CountActiveTeamsToDate
                || o.CountActiveTeamsToDateLastMonth != c.CountActiveTeamsToDateLastMonth
                || o.CountNewTeamsThisMonth != c.CountNewTeamsThisMonth)
            {
                material.Add($"COUNTS differ: aid={c.Aid} ({c.JobName} {c.Year}/{c.Month})");
            }
        }
        material.AddRange(oldCounts.Keys.Select(aid => $"COUNTS missing in NEW: aid={aid}"));

        // 3) Admin fees + 4) detail sets: multiset compare on normalized keys
        //    (amount 2dp: sproc money is 4dp; date-only: sproc datetime rounds 1/300s)
        static void MultisetDiff<T>(string label, IEnumerable<T> oldRows, IEnumerable<T> newRows,
            Func<T, string> keyOf, List<string> sink)
        {
            var bag = new Dictionary<string, int>();
            foreach (var r in oldRows)
            {
                var k = keyOf(r);
                bag[k] = bag.TryGetValue(k, out var n) ? n + 1 : 1;
            }
            foreach (var r in newRows)
            {
                var k = keyOf(r);
                if (bag.TryGetValue(k, out var n) && n > 0) { bag[k] = n - 1; }
                else { sink.Add($"{label} extra in NEW: {k}"); }
            }
            foreach (var (k, n) in bag.Where(e => e.Value > 0))
            {
                sink.Add($"{label} missing in NEW (x{n}): {k}");
            }
        }

        MultisetDiff("ADMINFEES", legacy.AdminFees, efRollup.AdminFees,
            r => $"{r.JobName}|{r.Year}|{r.Month}|{r.ChargeType}|{r.ChargeAmount:0.00}|{r.Comment}", material);
        MultisetDiff("DETAIL/cc", legacy.CreditCardRecords, efCc, PaymentKey, material);
        MultisetDiff("DETAIL/check", legacy.CheckRecords, efCheck, PaymentKey, material);
        MultisetDiff("DETAIL/echeck", legacy.EcheckRecords, efEcheck, PaymentKey, material);

        static string PaymentKey(JobPaymentRecordDto r) =>
            $"{r.JobName}|{r.Year}|{r.Month}|{r.Registrant}|{r.PaymentMethod}|{r.PaymentDate:yyyy-MM-dd}|{Math.Round(r.PaymentAmount, 2):0.00}";

        return new LegacyCompareResultDto
        {
            Pass = material.Count == 0,
            RollupCellsLegacy = oldMap.Count,
            RollupCellsNew = newMap.Count,
            MaterialMismatches = material,
            PennyDeltas = pennies
        };
    }

    /// <summary>
    /// Team Billing tab. No isTsicAdn branch: these are balances off Leagues.teams, not
    /// settlement-vs-RA cash, so the merchant-account classification is irrelevant here.
    /// </summary>
    public async Task<List<TeamBillingRecordDto>> GetTeamBillingAsync(
        Guid jobId, DateTime? startDate, DateTime? endDate,
        List<string> jobNames, CancellationToken ct = default)
    {
        return await _repo.GetTeamBillingAsync(jobId, startDate, endDate, jobNames, ct);
    }

    public async Task<YoyRevenueResponseDto> GetYoyRevenueAsync(
        Guid jobId, DateTime startDate, DateTime endDate,
        CancellationToken ct = default)
    {
        return await _repo.GetYoyRevenueAsync(jobId, startDate, endDate, ct);
    }

    public async Task<List<AdjustmentRecordDto>> GetAdjustmentsAsync(
        Guid jobId, DateTime? startDate, DateTime? endDate,
        List<string> jobNames, CancellationToken ct = default)
    {
        return await _repo.GetAdjustmentsAsync(jobId, startDate, endDate, jobNames, ct);
    }

    public async Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default)
    {
        await _repo.UpdateMonthlyCountAsync(aid, request, userId, ct);
    }
}
