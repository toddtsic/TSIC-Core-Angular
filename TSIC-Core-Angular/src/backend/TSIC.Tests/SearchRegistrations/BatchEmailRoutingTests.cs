using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.SearchRegistrations;

/// <summary>
/// BATCH EMAIL RECIPIENT ROUTING
///
/// Player's own User.Email is OPTIONAL (child accounts often have none).
/// Player rows route to the distinct union of:
///   - Families.MomEmail (if valued)
///   - Families.DadEmail (if valued)
///   - Registrations.User.Email (if valued)
/// Other roles (ClubRep, Staff, ...) keep User.Email as the single recipient.
/// </summary>
public class BatchEmailRoutingTests
{
    private static async Task<(RegistrationSearchService svc, SearchDataBuilder b,
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId,
        List<EmailMessageDto> sentMessages)>
        CreateServiceAsync()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        await b.SaveAsync();

        var registrationRepo = new RegistrationRepository(ctx);
        var accountingRepo = new RegistrationAccountingRepository(ctx);
        var familiesRepo = new FamiliesRepository(ctx);

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetConfirmationEmailInfoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobConfirmationEmailInfo { JobId = job.JobId, JobName = "Test Job", JobPath = "test" });

        var adnApi = new Mock<IAdnApiService>();
        var deviceRepo = new Mock<IDeviceRepository>();
        var arbRepo = new Mock<IArbSubscriptionRepository>();
        var logger = new Mock<ILogger<RegistrationSearchService>>();

        var textSub = new Mock<ITextSubstitutionService>();
        textSub.Setup(t => t.SubstituteAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .ReturnsAsync((string _, Guid _, Guid _, Guid? _, string _, string tpl, string? _, IReadOnlyDictionary<string, string>? _) => tpl);

        var sent = new List<EmailMessageDto>();
        var emailService = new Mock<IEmailService>();
        emailService.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessageDto, bool, CancellationToken>((dto, _, _) => sent.Add(dto))
            .ReturnsAsync(true);

        var feeAdjustment = new Mock<IRegistrationFeeAdjustmentService>().Object;

        var svc = new RegistrationSearchService(
            registrationRepo, accountingRepo, jobRepo.Object, familiesRepo, deviceRepo.Object,
            adnApi.Object, arbRepo.Object, textSub.Object, emailService.Object, feeAdjustment, logger.Object);

        return (svc, b, ctx, job.JobId, sent);
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
    public async Task Player_AllThreeEmails_SendsToDistinctUnion()
    {
        var (svc, b, ctx, jobId, sent) = await CreateServiceAsync();
        var reg = await AddPlayerUnderFamilyAsync(b, ctx, jobId,
            playerEmail: "kid@test.com",
            momEmail: "mom@test.com",
            dadEmail: "dad@test.com");

        var response = await svc.SendBatchEmailAsync(jobId, "admin", new BatchEmailRequest
        {
            RegistrationIds = new List<Guid> { reg.RegistrationId },
            Subject = "S",
            BodyTemplate = "B"
        });

        response.Sent.Should().Be(1);
        sent.Should().HaveCount(1);
        sent[0].ToAddresses.Should().BeEquivalentTo(new[] { "mom@test.com", "dad@test.com", "kid@test.com" });
    }

    [Fact(DisplayName = "Player with only mom email → ToAddresses = [mom]")]
    public async Task Player_MomOnly_SendsToMom()
    {
        var (svc, b, ctx, jobId, sent) = await CreateServiceAsync();
        var reg = await AddPlayerUnderFamilyAsync(b, ctx, jobId,
            playerEmail: "",
            momEmail: "mom@test.com",
            dadEmail: null);

        var response = await svc.SendBatchEmailAsync(jobId, "admin", new BatchEmailRequest
        {
            RegistrationIds = new List<Guid> { reg.RegistrationId },
            Subject = "S",
            BodyTemplate = "B"
        });

        response.Sent.Should().Be(1);
        sent[0].ToAddresses.Should().ContainSingle().Which.Should().Be("mom@test.com");
    }

    [Fact(DisplayName = "Player with mom == dad (same address) → ToAddresses deduped to one")]
    public async Task Player_MomEqualsDad_DedupedToOne()
    {
        var (svc, b, ctx, jobId, sent) = await CreateServiceAsync();
        var reg = await AddPlayerUnderFamilyAsync(b, ctx, jobId,
            playerEmail: "",
            momEmail: "shared@test.com",
            dadEmail: "SHARED@test.com");

        var response = await svc.SendBatchEmailAsync(jobId, "admin", new BatchEmailRequest
        {
            RegistrationIds = new List<Guid> { reg.RegistrationId },
            Subject = "S",
            BodyTemplate = "B"
        });

        response.Sent.Should().Be(1);
        sent[0].ToAddresses.Should().ContainSingle();
    }

    [Fact(DisplayName = "Player with no emails anywhere → marked failed, nothing sent")]
    public async Task Player_NoEmails_MarkedFailed()
    {
        var (svc, b, ctx, jobId, sent) = await CreateServiceAsync();
        var reg = await AddPlayerUnderFamilyAsync(b, ctx, jobId,
            playerEmail: "",
            momEmail: null,
            dadEmail: null);

        var response = await svc.SendBatchEmailAsync(jobId, "admin", new BatchEmailRequest
        {
            RegistrationIds = new List<Guid> { reg.RegistrationId },
            Subject = "S",
            BodyTemplate = "B"
        });

        response.Sent.Should().Be(0);
        response.Failed.Should().Be(1);
        sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "ClubRep → ToAddresses = [User.Email] only (no Families lookup)")]
    public async Task ClubRep_UsesUserEmailOnly()
    {
        var (svc, b, _, jobId, sent) = await CreateServiceAsync();

        var clubRepRole = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var clubRepUser = b.AddUser("Jane", "Rep", email: "clubrep@test.com");
        var reg = b.AddRegistration(jobId, clubRepUser.Id, clubRepRole.Id);
        // Non-Player roles carry FamilyUserId = their own UserId (legacy convention the
        // existing batch-send else-branch relies on via GetByJobAndFamilyWithUsersAsync).
        reg.FamilyUserId = clubRepUser.Id;
        await b.SaveAsync();

        var response = await svc.SendBatchEmailAsync(jobId, "admin", new BatchEmailRequest
        {
            RegistrationIds = new List<Guid> { reg.RegistrationId },
            Subject = "S",
            BodyTemplate = "B"
        });

        response.Sent.Should().Be(1);
        sent[0].ToAddresses.Should().ContainSingle().Which.Should().Be("clubrep@test.com");
    }
}
