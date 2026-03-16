using FluentAssertions;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.NavEditor;

/// <summary>
/// Integration tests for NavRepository.GetMergedNavAsync.
///
/// Each test gets a fresh InMemory database (DbContextFactory.Create) so
/// there is zero state leakage between tests.
///
/// Naming convention: {MethodName}_{Scenario}_{ExpectedOutcome}
/// </summary>
public class NavRepositoryTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private const string Role = "Director";

    // ─── Test 1 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMergedNavAsync_NoOverride_ReturnsPlatformDefaults()
    {
        // Arrange — seed a platform default nav with two L1s, each with two children
        var context = DbContextFactory.Create();
        var b = new NavDataBuilder(context);

        var defaultNav = b.AddPlatformDefaultNav(Role);
        var search     = b.AddL1Item(defaultNav.NavId, "Search");
        /*    */          b.AddL2Item(defaultNav.NavId, search.NavItemId, "Players");
        /*    */          b.AddL2Item(defaultNav.NavId, search.NavItemId, "Teams");
        var reports    = b.AddL1Item(defaultNav.NavId, "Reports");
        /*    */          b.AddL2Item(defaultNav.NavId, reports.NavItemId, "Summary");

        await b.SaveAsync();

        // Act — no override nav exists for this job
        var repo   = new NavRepository(context);
        var result = await repo.GetMergedNavAsync(Role, JobId);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2, "both L1 sections should be present");

        var searchItem = result.Items.Single(i => i.Text == "Search");
        searchItem.Children.Should().HaveCount(2, "Search has two children");
        searchItem.Children.Select(c => c.Text).Should().BeEquivalentTo("Players", "Teams");

        var reportsItem = result.Items.Single(i => i.Text == "Reports");
        reportsItem.Children.Should().HaveCount(1, "Reports has one child");
        reportsItem.Children.Single().Text.Should().Be("Summary");
    }

    // ─── Test 2 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMergedNavAsync_HiddenL2Item_ExcludedFromResult()
    {
        // Arrange — hide "Teams" for this job; "Players" and "Search" should still appear
        var context = DbContextFactory.Create();
        var b = new NavDataBuilder(context);

        var defaultNav = b.AddPlatformDefaultNav(Role);
        var search     = b.AddL1Item(defaultNav.NavId, "Search");
        /*    */          b.AddL2Item(defaultNav.NavId, search.NavItemId, "Players");
        var teams      = b.AddL2Item(defaultNav.NavId, search.NavItemId, "Teams");

        var overrideNav = b.AddJobOverrideNav(JobId, Role);
        b.AddHideRow(overrideNav.NavId, teams.NavItemId);

        await b.SaveAsync();

        // Act
        var repo   = new NavRepository(context);
        var result = await repo.GetMergedNavAsync(Role, JobId);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1, "Search L1 should still be present");

        var searchItem = result.Items.Single();
        searchItem.Text.Should().Be("Search");
        searchItem.Children.Should().HaveCount(1, "only Players remains; Teams is hidden");
        searchItem.Children.Single().Text.Should().Be("Players");
    }

    // ─── Test 3 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMergedNavAsync_HiddenL1Item_CascadesSuppressesAllChildren()
    {
        // Arrange — hide "LADT" (L1); its 3 children should also disappear.
        //           "Search" (sibling L1) must remain visible.
        var context = DbContextFactory.Create();
        var b = new NavDataBuilder(context);

        var defaultNav = b.AddPlatformDefaultNav(Role);

        var ladt = b.AddL1Item(defaultNav.NavId, "LADT");
        b.AddL2Item(defaultNav.NavId, ladt.NavItemId, "Pools");
        b.AddL2Item(defaultNav.NavId, ladt.NavItemId, "Roster");
        b.AddL2Item(defaultNav.NavId, ladt.NavItemId, "Standings");

        b.AddL1Item(defaultNav.NavId, "Search");  // must survive

        var overrideNav = b.AddJobOverrideNav(JobId, Role);
        b.AddHideRow(overrideNav.NavId, ladt.NavItemId); // hide the L1

        await b.SaveAsync();

        // Act
        var repo   = new NavRepository(context);
        var result = await repo.GetMergedNavAsync(Role, JobId);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1, "LADT section should be fully suppressed");

        var remaining = result.Items.Single();
        remaining.Text.Should().Be("Search", "Search is the only surviving L1");
        remaining.Children.Should().BeEmpty("Search had no children seeded");

        result.Items.Should().NotContain(i => i.Text == "LADT", "L1 hide row suppresses the root");
        result.Items.Should().NotContain(i => i.Text == "Pools" || i.Text == "Roster" || i.Text == "Standings",
            "cascade suppression removes all children of hidden L1");
    }
}
