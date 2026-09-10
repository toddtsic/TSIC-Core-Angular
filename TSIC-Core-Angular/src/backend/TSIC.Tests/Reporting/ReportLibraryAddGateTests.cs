using FluentAssertions;
using TSIC.API.Services.Reporting;
using TSIC.Domain.Constants;

namespace TSIC.Tests.Reporting;

/// <summary>
/// The Reports Library add-gate (ReportLibraryGate) — the security boundary between "a report
/// exists in reporting.ReportLibrary" and "this (job, role) shelf may hold it". Rulings
/// (Todd 2026-09-10): MinRoleId hierarchy Director &lt; SuperDirector &lt; Superuser, "Director"
/// = Director and above, "Superuser" = Superuser only, NULL = retired = nobody. Scope is a
/// fact and never gates. Owner customer and job-type applicability are the other two gates;
/// a Superuser bypasses applicability only.
///
/// Run: dotnet test --filter FullyQualifiedName~ReportLibraryAddGate
/// </summary>
public class ReportLibraryAddGateTests
{
    private static readonly Guid CustomerA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int Tournament = 1;
    private const int Camp = 4;

    // ── rank ──

    [Theory]
    [InlineData(RoleConstants.Director, ReportLibraryGate.RankDirector)]
    [InlineData(RoleConstants.SuperDirector, ReportLibraryGate.RankSuperDirector)]
    [InlineData(RoleConstants.Superuser, ReportLibraryGate.RankSuperuser)]
    [InlineData(null, ReportLibraryGate.RankRetired)]
    [InlineData("", ReportLibraryGate.RankRetired)]
    [InlineData(RoleConstants.ApiAuthorized, ReportLibraryGate.RankRetired)]
    [InlineData(RoleConstants.Player, ReportLibraryGate.RankRetired)]
    public void Rank_FollowsTheHierarchy_AndNullOrUnknownIsRetired(string? roleId, int expected)
        => ReportLibraryGate.Rank(roleId).Should().Be(expected);

    [Fact]
    public void Rank_IsCaseInsensitive_OnTheGuidString()
        => ReportLibraryGate.Rank(RoleConstants.Director.ToLowerInvariant()).Should().Be(ReportLibraryGate.RankDirector);

    // ── who may hold what ──

    [Theory]
    // Director-tier report: every admin role
    [InlineData(RoleConstants.Director, RoleConstants.Director, true)]
    [InlineData(RoleConstants.Director, RoleConstants.SuperDirector, true)]
    [InlineData(RoleConstants.Director, RoleConstants.Superuser, true)]
    // SuperDirector-tier: not the Director
    [InlineData(RoleConstants.SuperDirector, RoleConstants.Director, false)]
    [InlineData(RoleConstants.SuperDirector, RoleConstants.SuperDirector, true)]
    [InlineData(RoleConstants.SuperDirector, RoleConstants.Superuser, true)]
    // Superuser-tier: Superuser only
    [InlineData(RoleConstants.Superuser, RoleConstants.Director, false)]
    [InlineData(RoleConstants.Superuser, RoleConstants.SuperDirector, false)]
    [InlineData(RoleConstants.Superuser, RoleConstants.Superuser, true)]
    // Retired (NULL): nobody, not even the Superuser
    [InlineData(null, RoleConstants.Director, false)]
    [InlineData(null, RoleConstants.SuperDirector, false)]
    [InlineData(null, RoleConstants.Superuser, false)]
    public void RoleMayHold_MinimumRoleMeansThatRoleAndAbove_RetiredMeansNobody(
        string? minRoleId, string shelfRoleId, bool expected)
        => ReportLibraryGate.RoleMayHold(minRoleId, shelfRoleId).Should().Be(expected);

    [Theory]
    [InlineData(RoleConstants.ApiAuthorized)]
    [InlineData(RoleConstants.Player)]
    [InlineData(RoleConstants.ClubRep)]
    public void RoleMayHold_NonAdminShelfRole_NeverQualifies_EvenForDirectorTier(string shelfRoleId)
        => ReportLibraryGate.RoleMayHold(RoleConstants.Director, shelfRoleId).Should().BeFalse();

    [Fact]
    public void AllowedMinRoleIds_MirrorsRoleMayHold_ForTheBrowseQuery()
    {
        ReportLibraryGate.AllowedMinRoleIds(RoleConstants.Director)
            .Should().BeEquivalentTo(new[] { RoleConstants.Director });
        ReportLibraryGate.AllowedMinRoleIds(RoleConstants.SuperDirector)
            .Should().BeEquivalentTo(new[] { RoleConstants.Director, RoleConstants.SuperDirector });
        ReportLibraryGate.AllowedMinRoleIds(RoleConstants.Superuser)
            .Should().BeEquivalentTo(new[] { RoleConstants.Director, RoleConstants.SuperDirector, RoleConstants.Superuser });
        ReportLibraryGate.AllowedMinRoleIds(RoleConstants.Player).Should().BeEmpty();
    }

    // ── the full add-gate ──

    [Fact]
    public void Director_CanAdd_OpenDirectorTierReport_ForTheirOwnJobType()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Director, ownerCustomerId: null, applicableJobTypeIds: new[] { Tournament },
                RoleConstants.Director, callerIsSuperuser: false, CustomerA, Tournament)
            .Should().BeNull();

    [Fact]
    public void Director_CannotAdd_SuperuserOnlyReport()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Superuser, null, Array.Empty<int>(),
                RoleConstants.Director, false, CustomerA, Tournament)
            .Should().NotBeNull();

    [Fact]
    public void Director_CannotAdd_CrossJobReport_RuledSuperDirector()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.SuperDirector, null, Array.Empty<int>(),
                RoleConstants.Director, false, CustomerA, Tournament)
            .Should().NotBeNull();

    [Fact]
    public void SuperDirector_CanAdd_CrossJobReport()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.SuperDirector, null, Array.Empty<int>(),
                RoleConstants.SuperDirector, false, CustomerA, Tournament)
            .Should().BeNull();

    [Fact]
    public void DirectorOfCustomerA_CannotAdd_CustomerBsOwnedReport()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Director, ownerCustomerId: CustomerB, Array.Empty<int>(),
                RoleConstants.Director, false, CustomerA, Tournament)
            .Should().NotBeNull();

    [Fact]
    public void DirectorOfCustomerB_CanAdd_CustomerBsOwnedReport()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Director, ownerCustomerId: CustomerB, Array.Empty<int>(),
                RoleConstants.Director, false, CustomerB, Tournament)
            .Should().BeNull();

    [Fact]
    public void Superuser_IsNotExemptFromTheOwnerGate()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Director, ownerCustomerId: CustomerB, Array.Empty<int>(),
                RoleConstants.Superuser, callerIsSuperuser: true, CustomerA, Tournament)
            .Should().NotBeNull();

    [Fact]
    public void CampDirector_CannotAdd_TournamentOnlyReport()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Director, null, applicableJobTypeIds: new[] { Tournament },
                RoleConstants.Director, false, CustomerA, Camp)
            .Should().NotBeNull();

    [Fact]
    public void NoApplicabilityRows_MeansEveryJobType()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Director, null, Array.Empty<int>(),
                RoleConstants.Director, false, CustomerA, Camp)
            .Should().BeNull();

    [Fact]
    public void Superuser_BypassesApplicability_Only()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Director, null, applicableJobTypeIds: new[] { Tournament },
                RoleConstants.Superuser, callerIsSuperuser: true, CustomerA, Camp)
            .Should().BeNull();

    [Fact]
    public void Nobody_CanAdd_ARetiredReport_NotEvenSuperuser()
    {
        ReportLibraryGate.WhyNotAddable(null, null, Array.Empty<int>(),
                RoleConstants.Superuser, true, CustomerA, Tournament)
            .Should().Be("report is retired");
        ReportLibraryGate.WhyNotAddable(null, null, Array.Empty<int>(),
                RoleConstants.Director, false, CustomerA, Tournament)
            .Should().Be("report is retired");
    }

    [Fact]
    public void Superuser_PassesEveryGate_OnAnOpenReport()
        => ReportLibraryGate.WhyNotAddable(
                RoleConstants.Superuser, null, new[] { Tournament },
                RoleConstants.Superuser, true, CustomerA, Camp)
            .Should().BeNull();
}
