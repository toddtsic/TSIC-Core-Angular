using FluentAssertions;
using TSIC.API.Services.Shared.Email;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.SearchRegistrations;

/// <summary>
/// BATCH EMAIL RECIPIENT ROUTING (shared engine path)
///
/// Recipient resolution for every batch path now runs through the pure, shared
/// <see cref="BatchEmailRecipientFilter.ResolveRecipients"/>, fed by the two bulk address maps the
/// engine loads once per batch (GetRecipientEmailsByIdsAsync + GetByFamilyUserIdsAsync). These tests
/// drive that exact chain end-to-end from the DB — the same code the render worker calls per recipient.
///
/// Player's own User.Email is OPTIONAL (child accounts often have none). Player rows route to the
/// distinct union of:
///   - Families.MomEmail (if valued)
///   - Families.DadEmail (if valued)
///   - Registrations.User.Email (if valued)
/// Other roles (ClubRep, Staff, ...) keep User.Email as the single recipient.
/// </summary>
public class BatchEmailRoutingTests
{
    private static async Task<(SearchDataBuilder b, Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId)>
        CreateContextAsync()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        await b.SaveAsync();
        return (b, ctx, job.JobId);
    }

    /// <summary>
    /// Mirrors StartBatchEmailAsync's bulk address-map build, then resolves one registration's
    /// sendable To-addresses through the shared filter — the exact code the engine's render uses.
    /// </summary>
    private static async Task<List<string>> ResolveAsync(
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Registrations reg)
    {
        var registrationRepo = new RegistrationRepository(ctx);
        var familiesRepo = new FamiliesRepository(ctx);

        var emailByRegId = (await registrationRepo.GetRecipientEmailsByIdsAsync(new[] { reg.RegistrationId }))
            .GroupBy(r => r.RegistrationId)
            .ToDictionary(g => g.Key, g => g.First().Email);

        var familyIds = reg.RoleId == RoleConstants.Player && !string.IsNullOrWhiteSpace(reg.FamilyUserId)
            ? new List<string> { reg.FamilyUserId! }
            : new List<string>();
        var familyEmailsById = (await familiesRepo.GetByFamilyUserIdsAsync(familyIds))
            .GroupBy(f => f.FamilyUserId)
            .ToDictionary(g => g.Key, g => g.First());

        return BatchEmailRecipientFilter.ResolveRecipients(
            reg.RoleId, reg.FamilyUserId, reg.RegistrationId, emailByRegId, familyEmailsById);
    }

    /// <summary>Builds a Player registration under a family with configurable email state.</summary>
    /// <param name="playerEmail">Pass empty string for "no email" — null would trigger AddUser's default pattern.</param>
    private static async Task<Registrations> AddPlayerUnderFamilyAsync(
        SearchDataBuilder b,
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx,
        Guid jobId,
        string playerEmail,
        string? momEmail,
        string? dadEmail)
    {
        var familyUser = b.AddUser("Fam", $"Acc-{Guid.NewGuid():N}"[..8], email: $"family.{Guid.NewGuid():N}@test.com"[..40]);
        var playerRole = b.AddRole(RoleConstants.Player, "Player");
        var playerUser = b.AddUser("Kid", "Player", email: playerEmail);
        var reg = b.AddRegistration(jobId, playerUser.Id, playerRole.Id);
        reg.FamilyUserId = familyUser.Id;
        ctx.Families.Add(new Families
        {
            FamilyUserId = familyUser.Id,
            MomEmail = momEmail,
            DadEmail = dadEmail,
            Modified = DateTime.UtcNow
        });
        await b.SaveAsync();
        return reg;
    }

    [Fact(DisplayName = "Player with mom+dad+player emails → ToAddresses = distinct union of all three")]
    public async Task Player_AllThreeEmails_ResolvesToDistinctUnion()
    {
        var (b, ctx, jobId) = await CreateContextAsync();
        var reg = await AddPlayerUnderFamilyAsync(b, ctx, jobId,
            playerEmail: "kid@test.com",
            momEmail: "mom@test.com",
            dadEmail: "dad@test.com");

        var to = await ResolveAsync(ctx, reg);

        to.Should().BeEquivalentTo("mom@test.com", "dad@test.com", "kid@test.com");
    }

    [Fact(DisplayName = "Player with only mom email → ToAddresses = [mom]")]
    public async Task Player_MomOnly_ResolvesToMom()
    {
        var (b, ctx, jobId) = await CreateContextAsync();
        var reg = await AddPlayerUnderFamilyAsync(b, ctx, jobId,
            playerEmail: "",
            momEmail: "mom@test.com",
            dadEmail: null);

        var to = await ResolveAsync(ctx, reg);

        to.Should().ContainSingle().Which.Should().Be("mom@test.com");
    }

    [Fact(DisplayName = "Player with mom == dad (same address) → ToAddresses deduped to one")]
    public async Task Player_MomEqualsDad_DedupedToOne()
    {
        var (b, ctx, jobId) = await CreateContextAsync();
        var reg = await AddPlayerUnderFamilyAsync(b, ctx, jobId,
            playerEmail: "",
            momEmail: "shared@test.com",
            dadEmail: "SHARED@test.com");

        var to = await ResolveAsync(ctx, reg);

        to.Should().ContainSingle();
    }

    [Fact(DisplayName = "Player with no emails anywhere → empty (engine tallies as failed-no-email)")]
    public async Task Player_NoEmails_ResolvesEmpty()
    {
        var (b, ctx, jobId) = await CreateContextAsync();
        var reg = await AddPlayerUnderFamilyAsync(b, ctx, jobId,
            playerEmail: "",
            momEmail: null,
            dadEmail: null);

        var to = await ResolveAsync(ctx, reg);

        to.Should().BeEmpty();
    }

    [Fact(DisplayName = "ClubRep → ToAddresses = [User.Email] only (no Families lookup)")]
    public async Task ClubRep_UsesUserEmailOnly()
    {
        var (b, ctx, jobId) = await CreateContextAsync();

        var clubRepRole = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var clubRepUser = b.AddUser("Jane", "Rep", email: "clubrep@test.com");
        var reg = b.AddRegistration(jobId, clubRepUser.Id, clubRepRole.Id);
        // Real club-rep rows carry NO FamilyUserId (UserId is the login). The recipient resolver
        // never does a Families lookup for non-Player roles, so it routes to User.Email regardless.
        // (Do NOT assume FamilyUserId == UserId here — it's empty; that false belief broke the
        // club-rep invite link, see TextSubstitutionInviteLinkTests.)
        reg.FamilyUserId.Should().BeNull();
        await b.SaveAsync();

        var to = await ResolveAsync(ctx, reg);

        to.Should().ContainSingle().Which.Should().Be("clubrep@test.com");
    }
}
