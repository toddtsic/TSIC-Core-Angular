namespace TSIC.API.Services.Reporting;

/// <summary>
/// The fixed set of cross-job accounting stored-proc exports launched from the
/// SuperUser <c>Accounting</c> nav menu (<c>scripts/5) Re-Set Nav System.sql</c>)
/// rather than the per-job reports-library. Every one runs with
/// <c>bUseJobId=false</c> — it aggregates across ALL jobs — so none has (or could
/// sensibly have) a per-job row in <c>reporting.JobReports</c>. They are authorized
/// in <see cref="ReportingController"/>'s <c>export-sp</c> endpoint by membership in
/// this list (SuperUser-gated), standing in for the per-job entitlement the generic
/// endpoint applies to library reports.
///
/// This list is the authorization counterpart of the nav manifest: when a new global
/// accounting report is added to the Accounting menu there, add its spName here in the
/// same change (the manifest edit already requires a repo change + nav reseed, so this
/// rides the exact same workflow). An spName NOT in this list falls through to the
/// normal per-job entitlement check and is denied — the endpoint fails closed.
/// </summary>
public static class GlobalAccountingReports
{
    // Names must match the spName the nav emits verbatim. OrdinalIgnoreCase because
    // SQL object names are case-insensitive under the default collation.
    private static readonly HashSet<string> SpNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "reporting.NewTsicJobsWithTxs",   // "1) New Jobs Last Month (with txs)"
        "adn.GetLastMonthsGrandTotals",   // "4) Last Month's Grand Totals (Excel)"
        "adn.ReconcileNuvei",             // "ADN-Nuvei Reconcile (Excel)"
        "reporting.JobAdminFeesAll",      // "Job Admin Fees Summary"
    };

    /// <summary>
    /// True if <paramref name="spName"/> is a known cross-job accounting export that a
    /// SuperUser may run without a per-job <c>reporting.JobReports</c> entitlement row.
    /// </summary>
    public static bool Contains(string spName) => SpNames.Contains(spName);
}
