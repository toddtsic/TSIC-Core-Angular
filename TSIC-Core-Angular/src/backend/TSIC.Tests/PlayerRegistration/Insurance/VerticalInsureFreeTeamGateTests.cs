using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.Tests.PlayerRegistration.Insurance;

/// <summary>
/// VERTICAL INSURE — FREE-TEAM GATE
///
/// A free team has nothing to insure, so RegSaver must never be offered for a player on
/// one (e.g. a full team registered onto its $0 WAITLIST twin, or a genuinely free event).
/// The gate lives in <see cref="VerticalInsureService"/>'s product build and keys on the
/// team's CONFIGURED fee — the cascade full price (<see cref="ITeamLookupService.ResolveFullPriceAsync"/>)
/// OR the team's per-registrant fee — NOT the stamped FeeTotal (which a free team can still
/// carry from a donation).
///
/// These tests feed the eligible-registration list directly, bypassing the repository's
/// own <c>FeeTotal &gt; 0</c> pre-filter, to prove the gate stands on its own: a free team
/// is dropped even when its FeeTotal is non-zero, and a paid per-registrant-fee team (whose
/// cascade fee resolves to $0) is still offered.
/// </summary>
public class VerticalInsureFreeTeamGateTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private const string FamilyUserId = "fam-test";

    private static VerticalInsureService CreateService(
        List<EligibleInsuranceRegistration> regs,
        Dictionary<Guid, decimal> resolvedTeamFees)
    {
        var jobRepo = new Mock<IJobRepository>();
        jobRepo
            .Setup(j => j.GetInsuranceOfferInfoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsuranceOfferInfo
            {
                JobName = "Test Tournament",
                BOfferPlayerRegsaverInsurance = true,
                BOfferTeamRegsaverInsurance = false,
            });

        var regRepo = new Mock<IRegistrationRepository>();
        regRepo
            .Setup(r => r.GetEligibleInsuranceRegistrationsAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(regs);
        regRepo
            .Setup(r => r.GetDirectorContactForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorContactInfo { PaymentPlan = false });

        var familyRepo = new Mock<IFamilyRepository>();
        familyRepo
            .Setup(f => f.GetFamilyContactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FamilyContactInfo { FirstName = "Pat", LastName = "Parent", Email = "pat@example.com" });

        var teamLookup = new Mock<ITeamLookupService>();
        // The offer build keys the gate + insurable base on the cascade FULL price (not the
        // phase-dependent base), so the dictionary now stands in for ResolveFullPriceAsync.
        teamLookup
            .Setup(t => t.ResolveFullPriceAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((Guid teamId, string _) =>
                resolvedTeamFees.TryGetValue(teamId, out var f) ? f : 0m);

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Development");

        var paymentFeatures = new Mock<IJobPaymentFeaturesService>();
        paymentFeatures
            .Setup(p => p.UsesAmexAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return new VerticalInsureService(
            jobRepo.Object,
            regRepo.Object,
            familyRepo.Object,
            new Mock<ITeamRepository>().Object,
            new Mock<IUserRepository>().Object,
            env.Object,
            new Mock<ILogger<VerticalInsureService>>().Object,
            teamLookup.Object,
            Options.Create(new VerticalInsureSettings()),
            paymentFeatures.Object,
            httpClientFactory: null);
    }

    private static EligibleInsuranceRegistration Reg(
        Guid teamId, decimal feeTotal, decimal? perRegistrantFee = null) => new()
        {
            RegistrationId = Guid.NewGuid(),
            AssignedTeamId = teamId,
            FirstName = "Kid",
            LastName = "Player",
            PerRegistrantFee = perRegistrantFee,
            FeeTotal = feeTotal,
        };

    [Fact(DisplayName = "Free team (resolver $0, no per-registrant fee) → no offer, even with a non-zero FeeTotal")]
    public async Task FreeTeam_NotOffered()
    {
        var freeTeamId = Guid.NewGuid();
        // FeeTotal is deliberately > 0 (e.g. a donation rode along) to prove the gate keys on
        // the configured team fee, not the stamped total.
        var regs = new List<EligibleInsuranceRegistration> { Reg(freeTeamId, feeTotal: 25m) };
        var svc = CreateService(regs, new Dictionary<Guid, decimal> { [freeTeamId] = 0m });

        var offer = await svc.BuildOfferAsync(JobId, FamilyUserId);

        offer.Available.Should().BeFalse("a free team has nothing to insure — present no offer at all");
        offer.PlayerObject.Should().BeNull();
    }

    [Fact(DisplayName = "Mixed family: only the paid player is offered; the free (waitlist) player is dropped")]
    public async Task MixedPaidAndFree_OnlyPaidOffered()
    {
        var paidTeamId = Guid.NewGuid();
        var freeTeamId = Guid.NewGuid();
        var paidReg = Reg(paidTeamId, feeTotal: 100m);
        var freeReg = Reg(freeTeamId, feeTotal: 0m);
        var regs = new List<EligibleInsuranceRegistration> { paidReg, freeReg };
        var svc = CreateService(regs, new Dictionary<Guid, decimal>
        {
            [paidTeamId] = 100m,
            [freeTeamId] = 0m,
        });

        var offer = await svc.BuildOfferAsync(JobId, FamilyUserId);

        offer.Available.Should().BeTrue();
        var products = offer.PlayerObject!.ProductConfig.RegistrationCancellation;
        products.Should().HaveCount(1, "only the paid registration is insurable");
        products[0].Metadata.TsicRegistrationId.Should().Be(paidReg.RegistrationId);
    }

    [Fact(DisplayName = "Per-registrant-fee team (cascade resolves $0 but team charges per head) is still offered")]
    public async Task PerRegistrantFeeTeam_StillOffered()
    {
        // The cascade resolver returns $0 for a per-registrant-fee team (the fee lives on the
        // team, not in JobFees). The gate must NOT treat that as free.
        var teamId = Guid.NewGuid();
        var reg = Reg(teamId, feeTotal: 0m, perRegistrantFee: 50m);
        var svc = CreateService(
            new List<EligibleInsuranceRegistration> { reg },
            new Dictionary<Guid, decimal> { [teamId] = 0m });

        var offer = await svc.BuildOfferAsync(JobId, FamilyUserId);

        offer.Available.Should().BeTrue("a per-registrant fee is a real charge — insurable");
        offer.PlayerObject!.ProductConfig.RegistrationCancellation.Should().HaveCount(1);
    }
}
