using TSIC.Domain.Constants;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// The Reports Library add-gate — pure, no I/O, unit-tested in ReportLibraryAddGateTests.
///
/// Two library columns, one gate (Todd 2026-09-10):
///   Scope     is a FACT about the query (JobOnly | CrossJob | CrossWebsite). It never gates;
///             it floors MinRoleId (a CrossJob report can never be set to Director).
///   MinRoleId is the POLICY: an AspNetRoles Id with hierarchy Director &lt; SuperDirector &lt;
///             Superuser. "Director" means Director and above; "Superuser" means Superuser only.
///             NULL means RETIRED and ranks ABOVE Superuser — nobody may add or hold it.
///
/// A shelf row (reporting.JobReports) is the run-time entitlement. This gate decides who may
/// CREATE one from the library; the same rank rule decides which rows the Superuser editor
/// sweeps when it raises MinRoleId.
/// </summary>
public static class ReportLibraryGate
{
    public const int RankDirector = 1;
    public const int RankSuperDirector = 2;
    public const int RankSuperuser = 3;

    /// <summary>NULL (retired) or any unknown role id: above every real role, so nothing qualifies.</summary>
    public const int RankRetired = 4;

    /// <summary>Rank of a role id in the admin hierarchy; unknown / null = retired (4).</summary>
    public static int Rank(string? roleId)
    {
        if (string.IsNullOrEmpty(roleId)) return RankRetired;
        if (string.Equals(roleId, RoleConstants.Director, StringComparison.OrdinalIgnoreCase)) return RankDirector;
        if (string.Equals(roleId, RoleConstants.SuperDirector, StringComparison.OrdinalIgnoreCase)) return RankSuperDirector;
        if (string.Equals(roleId, RoleConstants.Superuser, StringComparison.OrdinalIgnoreCase)) return RankSuperuser;
        return RankRetired;
    }

    /// <summary>True when a shelf for <paramref name="shelfRoleId"/> may hold a report whose minimum role is <paramref name="minRoleId"/>.</summary>
    public static bool RoleMayHold(string? minRoleId, string shelfRoleId)
    {
        var shelfRank = Rank(shelfRoleId);
        // A shelf role that is not one of the three admin roles never qualifies.
        if (shelfRank == RankRetired) return false;
        return shelfRank >= Rank(minRoleId);
    }

    /// <summary>
    /// The MinRoleIds a shelf of <paramref name="shelfRoleId"/> may hold — the SQL-side form of
    /// <see cref="RoleMayHold"/> for the browse query. Empty for a non-admin role.
    /// </summary>
    public static IReadOnlyCollection<string> AllowedMinRoleIds(string shelfRoleId)
    {
        var rank = Rank(shelfRoleId);
        var ids = new List<string>(3);
        if (rank >= RankDirector && rank != RankRetired) ids.Add(RoleConstants.Director);
        if (rank >= RankSuperDirector && rank != RankRetired) ids.Add(RoleConstants.SuperDirector);
        if (rank >= RankSuperuser && rank != RankRetired) ids.Add(RoleConstants.Superuser);
        return ids;
    }

    /// <summary>
    /// The full add-gate. All four checks must pass:
    ///   1. the shelf role ranks at or above MinRoleId (retired never passes);
    ///   2. OwnerCustomerId, when set, is the job's customer;
    ///   3. applicability: no job-type rows, or the job's type is listed — a Superuser bypasses
    ///      this one (advisory for browse, not a security boundary for SU);
    /// Returns the reason on failure so the controller can log it; null when allowed.
    /// </summary>
    public static string? WhyNotAddable(
        string? minRoleId,
        Guid? ownerCustomerId,
        IReadOnlyCollection<int> applicableJobTypeIds,
        string shelfRoleId,
        bool callerIsSuperuser,
        Guid jobCustomerId,
        int jobTypeId)
    {
        if (!RoleMayHold(minRoleId, shelfRoleId))
        {
            return minRoleId is null ? "report is retired" : "shelf role ranks below the report's minimum role";
        }

        if (ownerCustomerId.HasValue && ownerCustomerId.Value != jobCustomerId)
        {
            return "report is owned by another customer";
        }

        if (!callerIsSuperuser && applicableJobTypeIds.Count > 0 && !applicableJobTypeIds.Contains(jobTypeId))
        {
            return "report is not applicable to this job type";
        }

        return null;
    }
}
