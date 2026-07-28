using FluentAssertions;
using TSIC.API.Services.Reporting;

namespace TSIC.Tests.Reporting;

/// <summary>
/// Locks the cross-job SuperUser allow-list that authorizes export-sp launches from the
/// SU Accounting nav and the X-Job Report Library (ReportingController.ExportStoredProcedureResults).
/// The set must stay in lockstep with the export-sp?bUseJobId=false rows in the nav manifest
/// (scripts/5) Re-Set Nav System.sql) and the 'sp' entries in x-job-report-catalog.ts. If a
/// report is added/removed there, this test is the reminder to mirror it here — and it guards
/// against an accidental deletion silently re-403-ing a live cross-job report.
/// </summary>
public class GlobalSuperuserReportsTests
{
    [Theory]
    // ── Accounting ──
    [InlineData("reporting.NewTsicJobsWithTxs")]              // "1) New Jobs Last Month (with txs)"
    [InlineData("adn.GetLastMonthsGrandTotals")]              // "4) Last Month's Grand Totals (Excel)"
    [InlineData("adn.ReconcileNuvei")]                        // "ADN-Nuvei Reconcile (Excel)"
    [InlineData("reporting.JobAdminFeesAll")]                 // "Job Admin Fees Summary"
    // ── Cross-job reports (X-Job Report Library) ──
    [InlineData("reporting.RegsaverRegistrants_ALL")]         // "Regsaver Purchases (Excel)"
    [InlineData("reporting.RegsaverPurchases_ALL_Rawdata")]   // "Regsaver Purchases — Raw Data (Excel)"
    [InlineData("utility.PlayerRegistrationBulletinsQA")]     // "Expired Player Reg Bulletins on Active Sites"
    [InlineData("utility.TeamRegistrationBulletinsQA")]       // "Expired Team Reg Bulletins on Active Sites"
    [InlineData("utility.GetSuspiciousArbs")]                 // "List of Suspicious ARBs"
    [InlineData("reporting.JobKeyAttributes-ALL")]            // "Job Key Attributes — ALL"
    [InlineData("reporting.ClubRepContacts-All")]             // "Club Rep Contacts — ALL"
    [InlineData("reporting.TournamentKeyAttributes-ALL")]     // "Tournament Keys — ALL"
    [InlineData("utility.ExpiringBulletins")]                 // "Expiring Bulletins (3 mos)"
    public void Contains_KnownGlobalSuperuserReport_ReturnsTrue(string spName)
    {
        GlobalSuperuserReports.Contains(spName).Should().BeTrue();
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        // SQL object names are case-insensitive; the launch surface could emit either casing.
        GlobalSuperuserReports.Contains("REPORTING.NEWTSICJOBSWITHTXS").Should().BeTrue();
    }

    [Fact]
    public void Contains_IsBracketInsensitive()
    {
        // The X-Job catalog emits bracketed names for hyphenated procs
        // (CommandType.StoredProcedure requires delimited identifiers there).
        GlobalSuperuserReports.Contains("[reporting].[JobKeyAttributes-ALL]").Should().BeTrue();
        GlobalSuperuserReports.Contains("[utility].[ExpiringBulletins]").Should().BeTrue();
    }

    [Theory]
    [InlineData("reporting.Get_JobPlayers_STEPS_Excel")]  // a real per-job library SP — must NOT be waved through
    [InlineData("adn.MonthyQBPExport_Automated")]         // a real SP, but not a nav-launched global report
    [InlineData("sys.sp_who")]                            // arbitrary proc — fail closed
    [InlineData("")]
    public void Contains_UnlistedSpName_ReturnsFalse(string spName)
    {
        GlobalSuperuserReports.Contains(spName).Should().BeFalse();
    }
}
