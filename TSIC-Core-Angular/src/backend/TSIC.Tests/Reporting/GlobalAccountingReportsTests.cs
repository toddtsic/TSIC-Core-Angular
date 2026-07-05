using FluentAssertions;
using TSIC.API.Services.Reporting;

namespace TSIC.Tests.Reporting;

/// <summary>
/// Locks the cross-job accounting allow-list that authorizes the SuperUser Accounting
/// nav's export-sp launches (ReportingController.ExportStoredProcedureResults). The set
/// must stay in lockstep with the export-sp?bUseJobId=false rows in the nav manifest
/// (scripts/5) Re-Set Nav System.sql). If a report is added/removed there, this test is
/// the reminder to mirror it here — and it guards against an accidental deletion silently
/// re-403-ing a live accounting report.
/// </summary>
public class GlobalAccountingReportsTests
{
    [Theory]
    [InlineData("reporting.NewTsicJobsWithTxs")]   // "1) New Jobs Last Month (with txs)"
    [InlineData("adn.GetLastMonthsGrandTotals")]   // "4) Last Month's Grand Totals (Excel)"
    [InlineData("adn.ReconcileNuvei")]             // "ADN-Nuvei Reconcile (Excel)"
    [InlineData("reporting.JobAdminFeesAll")]      // "Job Admin Fees Summary"
    public void Contains_KnownGlobalAccountingReport_ReturnsTrue(string spName)
    {
        GlobalAccountingReports.Contains(spName).Should().BeTrue();
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        // SQL object names are case-insensitive; the nav could emit either casing.
        GlobalAccountingReports.Contains("REPORTING.NEWTSICJOBSWITHTXS").Should().BeTrue();
    }

    [Theory]
    [InlineData("reporting.Get_JobPlayers_STEPS_Excel")]  // a real per-job library SP — must NOT be waved through
    [InlineData("adn.MonthyQBPExport_Automated")]         // a real SP, but not a nav-launched global report
    [InlineData("sys.sp_who")]                            // arbitrary proc — fail closed
    [InlineData("")]
    public void Contains_UnlistedSpName_ReturnsFalse(string spName)
    {
        GlobalAccountingReports.Contains(spName).Should().BeFalse();
    }
}
