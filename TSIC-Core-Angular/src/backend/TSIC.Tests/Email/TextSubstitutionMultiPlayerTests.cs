using FluentAssertions;
using Moq;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Shared.Utilities;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Email;

/// <summary>
/// Render-win #3 correctness for MULTI-KID families. A batch sends one email per registration, so
/// each must render THAT registration's own per-recipient tokens (!PERSON, !EMAIL, ...).
///
/// The fast path (familyUserId="" → registrationId PK seek, taken when the template has no !F-
/// family-aggregation token) loads exactly the targeted registration, so simple tokens are correct
/// per recipient. The OLD family path (familyUserId supplied) builds simple tokens from
/// fixedFieldList[0] — the first sibling — so every kid in a 2-kid family rendered the FIRST kid's
/// !PERSON/!EMAIL. These tests pin the fix AND prove the family path is still intact for the !F-
/// tokens that genuinely need the whole family list.
/// </summary>
public class TextSubstitutionMultiPlayerTests
{
    private const string Body = "!PERSON <!EMAIL>";

    private static TextSubstitutionService BuildService(Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var repo = new TextSubstitutionRepository(ctx);
        return new TextSubstitutionService(
            repo, new Mock<IDiscountCodeEvaluator>().Object, new Mock<IFeeResolutionService>().Object);
    }

    private static async Task<(Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid aliceRegId, Guid bobRegId, string familyUserId)>
        SeedTwoKidFamilyAsync()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);

        var customerId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        ctx.Customers.Add(new Customers { CustomerId = customerId, CustomerName = "Acme Sports" });
        ctx.Sports.Add(new Sports { SportId = sportId, SportName = "Lacrosse" });

        var job = b.AddJob();
        job.CustomerId = customerId;
        job.SportId = sportId;
        job.JobName = "Spring Cup";
        job.Season = "Spring 2026";
        job.JobCode = "SC26";
        ctx.JobDisplayOptions.Add(new JobDisplayOptions { JobId = job.JobId, LogoHeader = "logo.png", LebUserId = "seed", Modified = DateTime.UtcNow });

        var role = b.AddRole(RoleConstants.Player, "Player");
        var familyUser = b.AddUser("Fam", "Account", email: "fam@x.com");

        // Two siblings in the same family, same job.
        var aliceUser = b.AddUser("Alice", "Smith", email: "alice@x.com");
        var bobUser = b.AddUser("Bob", "Smith", email: "bob@x.com");
        var aliceReg = b.AddRegistration(job.JobId, aliceUser.Id, role.Id, feeTotal: 150m, paidTotal: 0m);
        aliceReg.FamilyUserId = familyUser.Id;
        var bobReg = b.AddRegistration(job.JobId, bobUser.Id, role.Id, feeTotal: 150m, paidTotal: 0m);
        bobReg.FamilyUserId = familyUser.Id;

        await b.SaveAsync();
        return (ctx, job.JobId, aliceReg.RegistrationId, bobReg.RegistrationId, familyUser.Id);
    }

    [Fact(DisplayName = "Render-win #3: registrationId path renders EACH sibling's own !PERSON/!EMAIL")]
    public async Task RegistrationIdPath_RendersEachRecipientsOwnTokens()
    {
        var (ctx, jobId, aliceRegId, bobRegId, _) = await SeedTwoKidFamilyAsync();
        var svc = BuildService(ctx);
        var cc = Guid.NewGuid();
        var jobFields = await svc.LoadJobInvariantFieldsAsync(jobId);

        // Fast path: familyUserId="" routes to the registrationId PK seek (no !F- token in the body).
        var alice = await svc.SubstituteSubjectAndBodyAsync("seg", jobId, cc, aliceRegId, "", "S", Body, jobFields: jobFields);
        var bob = await svc.SubstituteSubjectAndBodyAsync("seg", jobId, cc, bobRegId, "", "S", Body, jobFields: jobFields);

        alice.Body.Should().Be("Alice Smith <alice@x.com>");
        bob.Body.Should().Be("Bob Smith <bob@x.com>", "each registration renders its OWN recipient, not the first sibling");
    }

    [Fact(DisplayName = "The family path is recipient-INSENSITIVE for simple tokens — the bug render-win #3 removes")]
    public async Task FamilyPath_IsRecipientInsensitive_DocumentsTheBug()
    {
        var (ctx, jobId, aliceRegId, bobRegId, familyUserId) = await SeedTwoKidFamilyAsync();
        var svc = BuildService(ctx);
        var cc = Guid.NewGuid();
        var jobFields = await svc.LoadJobInvariantFieldsAsync(jobId);

        // Old behavior: with familyUserId supplied, simple tokens come from fixedFieldList[0], so
        // BOTH targets render the SAME (first) sibling regardless of which registration is targeted.
        var asAlice = await svc.SubstituteSubjectAndBodyAsync("seg", jobId, cc, aliceRegId, familyUserId, "S", Body, jobFields: jobFields);
        var asBob = await svc.SubstituteSubjectAndBodyAsync("seg", jobId, cc, bobRegId, familyUserId, "S", Body, jobFields: jobFields);

        asBob.Body.Should().Be(asAlice.Body,
            "the family path keys simple tokens off list[0], ignoring the targeted registration — why the batch must use the registrationId path for non-!F- templates");
    }

    [Fact(DisplayName = "Family path with !F-PLAYERS still aggregates ALL siblings (family feature intact)")]
    public async Task FamilyPath_FPlayers_AggregatesAllSiblings()
    {
        var (ctx, jobId, _, _, familyUserId) = await SeedTwoKidFamilyAsync();
        var svc = BuildService(ctx);
        var cc = Guid.NewGuid();
        var jobFields = await svc.LoadJobInvariantFieldsAsync(jobId);

        // !F-PLAYERS needs the whole family list; the batch keeps the family path for !F- templates.
        var rendered = await svc.SubstituteSubjectAndBodyAsync(
            "seg", jobId, cc, null, familyUserId, "S", "Roster: !F-PLAYERS", jobFields: jobFields);

        rendered.Body.Should().Contain("Alice").And.Contain("Bob",
            "the family-aggregation path must still see every sibling after the render-win #2/#3 changes");
    }
}
