namespace TSIC.API.Services.Reporting;

/// <summary>
/// The fixed set of cross-job stored-proc exports a SuperUser may run without a
/// per-job <c>reporting.JobReports</c> entitlement row. Every one runs with
/// <c>bUseJobId=false</c> — it aggregates across ALL jobs — so none has (or could
/// sensibly have) a per-job catalogue row. They are authorized in
/// <see cref="ReportingController"/>'s <c>export-sp</c> endpoint by membership in
/// this list (SuperUser-gated), standing in for the per-job entitlement the generic
/// endpoint applies to library reports.
///
/// Launch surfaces: the SuperUser Accounting nav (<c>scripts/5) Re-Set Nav System.sql</c>)
/// and the X-Job Report Library (<c>x-job-report-catalog.ts</c>). This list is the
/// authorization counterpart of both: when a new cross-job SP export is added to either
/// surface, add its spName here in the same change (both edits already require a repo
/// change, so this rides the exact same workflow). An spName NOT in this list falls
/// through to the normal per-job entitlement check and is denied — the endpoint fails
/// closed.
/// </summary>
public static class GlobalSuperuserReports
{
    // Canonical (unbracketed) names; must match the spName the launch surface emits
    // after bracket-stripping — callers may send either `schema.Name` or
    // `[schema].[Name]` (bracketed is required at execution time for names containing
    // hyphens, since CommandType.StoredProcedure needs delimited identifiers).
    // OrdinalIgnoreCase because SQL object names are case-insensitive under the
    // default collation.
    private static readonly HashSet<string> SpNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Accounting (SU Accounting nav + X-Job library mirror) ──
        "reporting.NewTsicJobsWithTxs",             // "1) New Jobs Last Month (with txs)"
        "adn.GetLastMonthsGrandTotals",             // "4) Last Month's Grand Totals (Excel)"
        "adn.ReconcileNuvei",                       // "ADN-Nuvei Reconcile (Excel)"
        "reporting.JobAdminFeesAll",                // "Job Admin Fees Summary"

        // ── Cross-job reports (X-Job Report Library) ──
        "reporting.RegsaverRegistrants_ALL",        // "Regsaver Purchases (Excel)"
        "reporting.RegsaverPurchases_ALL_Rawdata",  // "Regsaver Purchases — Raw Data (Excel)"
        "utility.PlayerRegistrationBulletinsQA",    // "Expired Player Reg Bulletins on Active Sites"
        "utility.TeamRegistrationBulletinsQA",      // "Expired Team Reg Bulletins on Active Sites"
        "utility.GetSuspiciousArbs",                // "List of Suspicious ARBs"
        "reporting.JobKeyAttributes-ALL",           // "Job Key Attributes — ALL"
        "reporting.ClubRepContacts-All",            // "Club Rep Contacts — ALL"
        "reporting.TournamentKeyAttributes-ALL",    // "Tournament Keys — ALL"
        "utility.ExpiringBulletins",                // "Expiring Bulletins (3 mos)"
    };

    /// <summary>
    /// True if <paramref name="spName"/> is a known cross-job report that a SuperUser
    /// may run without a per-job <c>reporting.JobReports</c> entitlement row.
    /// Bracket-insensitive: <c>[utility].[ExpiringBulletins]</c> matches
    /// <c>utility.ExpiringBulletins</c>.
    /// </summary>
    public static bool Contains(string spName) =>
        SpNames.Contains(spName.Replace("[", "").Replace("]", ""));
}
