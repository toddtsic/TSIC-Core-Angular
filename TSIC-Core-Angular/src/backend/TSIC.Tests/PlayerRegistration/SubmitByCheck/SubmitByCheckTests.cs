using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.PlayerRegistration.SubmitByCheck;

/// <summary>
/// PAY-BY-CHECK SUBMIT TESTS
///
/// Covers the new <see cref="PlayerRegistrationService.SubmitByCheckAsync"/>
/// intake endpoint. The endpoint stamps PaymentMethodChosen=3 (Check) and
/// BActive=true on registrations the parent owns so the roster spot is held
/// while the check is in transit.
///
/// Invariants under test:
///   - Success path stamps the three audit-relevant fields.
///   - Idempotency: re-submitting an already-stamped row is a no-op success.
///   - Family scope: rows owned by another family are rejected.
///   - Job scope: rows from another job are rejected.
///   - Lock-to-check: rows already committed to a non-check method (e.g., CC) are rejected.
///   - Unknown registration IDs are rejected.
///   - Empty input returns a structured failure without saving.
/// </summary>
public class SubmitByCheckTests
{
    private const int CheckMethodCode = 3;
    private const int CcMethodCode = 1;

    private static readonly Guid TestJobId = Guid.NewGuid();
    private const string TestFamilyUserId = "family-user-test";
    private const string OtherFamilyUserId = "family-user-other";

    private static (PlayerRegistrationService svc, Mock<IRegistrationRepository> regRepo) CreateService()
    {
        var logger = new Mock<ILogger<PlayerRegistrationService>>();
        var feeService = new Mock<IFeeResolutionService>();
        var verticalInsure = new Mock<IVerticalInsureService>();
        var teamLookup = new Mock<ITeamLookupService>();
        var validation = new Mock<IPlayerFormValidationService>();
        var regRepo = new Mock<IRegistrationRepository>();
        var teamRepo = new Mock<ITeamRepository>();
        var jobRepo = new Mock<IJobRepository>();
        var placement = new Mock<ITeamPlacementService>();
        var medForms = new Mock<IMedFormService>();

        regRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var svc = new PlayerRegistrationService(
            logger.Object,
            feeService.Object,
            verticalInsure.Object,
            teamLookup.Object,
            validation.Object,
            regRepo.Object,
            teamRepo.Object,
            jobRepo.Object,
            placement.Object,
            medForms.Object);

        return (svc, regRepo);
    }

    private static Registrations BuildPendingReg(
        Guid? id = null,
        string familyUserId = TestFamilyUserId,
        Guid? jobId = null,
        bool? bActive = false,
        int? paymentMethodChosen = null)
    {
        return new Registrations
        {
            RegistrationId = id ?? Guid.NewGuid(),
            JobId = jobId ?? TestJobId,
            FamilyUserId = familyUserId,
            UserId = "player-" + Guid.NewGuid().ToString("N")[..8],
            BActive = bActive,
            PaymentMethodChosen = paymentMethodChosen,
            Modified = DateTime.UtcNow.AddDays(-1),
        };
    }

    private static SubmitByCheckRequestDto MakeRequest(params Guid[] ids) =>
        new()
        {
            JobPath = "test-job",
            RegistrationIds = ids.ToList(),
        };

    [Fact(DisplayName = "Submit: stamps PaymentMethodChosen=3, BActive=true, Modified, LebUserId")]
    public async Task Submit_StampsAuditFields()
    {
        var (svc, regRepo) = CreateService();
        var reg = BuildPendingReg();
        regRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { reg });

        var before = DateTime.UtcNow;
        var result = await svc.SubmitByCheckAsync(
            TestJobId, TestFamilyUserId, MakeRequest(reg.RegistrationId), TestFamilyUserId);
        var after = DateTime.UtcNow;

        result.Success.Should().BeTrue();
        result.UpdatedRegistrationIds.Should().ContainSingle().Which.Should().Be(reg.RegistrationId);
        result.Rejections.Should().BeEmpty();

        reg.PaymentMethodChosen.Should().Be(CheckMethodCode, "lock-to-check stamps method=3");
        reg.BActive.Should().BeTrue("BActive flips so roster spot is held");
        reg.LebUserId.Should().Be(TestFamilyUserId, "audit trail stamps caller");
        reg.Modified.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);

        regRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Submit: idempotent — re-submit on already-stamped row is no-op success")]
    public async Task Submit_Idempotent()
    {
        var (svc, regRepo) = CreateService();
        var reg = BuildPendingReg(bActive: true, paymentMethodChosen: CheckMethodCode);
        var modifiedBefore = reg.Modified;
        regRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { reg });

        var result = await svc.SubmitByCheckAsync(
            TestJobId, TestFamilyUserId, MakeRequest(reg.RegistrationId), TestFamilyUserId);

        result.Success.Should().BeTrue();
        result.UpdatedRegistrationIds.Should().ContainSingle().Which.Should().Be(reg.RegistrationId);
        result.Rejections.Should().BeEmpty();

        reg.Modified.Should().Be(modifiedBefore, "idempotent path must not re-stamp Modified");
        regRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no-op path must not save when nothing changed");
    }

    [Fact(DisplayName = "Submit: row owned by another family is rejected, not stamped")]
    public async Task Submit_RejectsOtherFamilyRow()
    {
        var (svc, regRepo) = CreateService();
        var foreignReg = BuildPendingReg(familyUserId: OtherFamilyUserId);
        regRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { foreignReg });

        var result = await svc.SubmitByCheckAsync(
            TestJobId, TestFamilyUserId, MakeRequest(foreignReg.RegistrationId), TestFamilyUserId);

        result.Success.Should().BeFalse();
        result.UpdatedRegistrationIds.Should().BeEmpty();
        result.Rejections.Should().ContainSingle()
            .Which.Reason.Should().Contain("not owned");

        foreignReg.PaymentMethodChosen.Should().BeNull();
        foreignReg.BActive.Should().Be(false, "foreign-family row must not be touched");
    }

    [Fact(DisplayName = "Submit: row from a different job is rejected, not stamped")]
    public async Task Submit_RejectsDifferentJobRow()
    {
        var (svc, regRepo) = CreateService();
        var otherJobId = Guid.NewGuid();
        var foreignReg = BuildPendingReg(jobId: otherJobId);
        regRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { foreignReg });

        var result = await svc.SubmitByCheckAsync(
            TestJobId, TestFamilyUserId, MakeRequest(foreignReg.RegistrationId), TestFamilyUserId);

        result.Success.Should().BeFalse();
        result.Rejections.Should().ContainSingle();
        foreignReg.PaymentMethodChosen.Should().BeNull("cross-job rows must not be touched");
    }

    [Fact(DisplayName = "Submit: row already committed to CC is rejected (lock-to-check invariant)")]
    public async Task Submit_RejectsCcCommittedRow()
    {
        var (svc, regRepo) = CreateService();
        var ccReg = BuildPendingReg(paymentMethodChosen: CcMethodCode, bActive: true);
        regRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { ccReg });

        var result = await svc.SubmitByCheckAsync(
            TestJobId, TestFamilyUserId, MakeRequest(ccReg.RegistrationId), TestFamilyUserId);

        result.Success.Should().BeFalse();
        result.Rejections.Should().ContainSingle()
            .Which.Reason.Should().Contain("already committed");

        ccReg.PaymentMethodChosen.Should().Be(CcMethodCode, "CC commitment must not be overwritten");
    }

    [Fact(DisplayName = "Submit: unknown registration ID returns rejection with 'not found'")]
    public async Task Submit_RejectsUnknownId()
    {
        var (svc, regRepo) = CreateService();
        var unknownId = Guid.NewGuid();
        regRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations>());

        var result = await svc.SubmitByCheckAsync(
            TestJobId, TestFamilyUserId, MakeRequest(unknownId), TestFamilyUserId);

        result.Success.Should().BeFalse();
        result.Rejections.Should().ContainSingle()
            .Which.Reason.Should().Contain("not found");
    }

    [Fact(DisplayName = "Submit: empty registration list returns failure without saving")]
    public async Task Submit_EmptyListFails()
    {
        var (svc, regRepo) = CreateService();
        var result = await svc.SubmitByCheckAsync(
            TestJobId, TestFamilyUserId, MakeRequest(), TestFamilyUserId);

        result.Success.Should().BeFalse();
        result.UpdatedRegistrationIds.Should().BeEmpty();
        regRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Submit: mixed batch — owned rows stamped, foreign row rejected, partial save")]
    public async Task Submit_MixedBatch_PartialResult()
    {
        var (svc, regRepo) = CreateService();
        var ownedReg = BuildPendingReg();
        var foreignReg = BuildPendingReg(familyUserId: OtherFamilyUserId);
        regRepo
            .Setup(r => r.GetByIdsAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { ownedReg, foreignReg });

        var result = await svc.SubmitByCheckAsync(
            TestJobId, TestFamilyUserId,
            MakeRequest(ownedReg.RegistrationId, foreignReg.RegistrationId),
            TestFamilyUserId);

        result.Success.Should().BeFalse("partial-failure surfaces as Success=false");
        result.UpdatedRegistrationIds.Should().ContainSingle().Which.Should().Be(ownedReg.RegistrationId);
        result.Rejections.Should().ContainSingle();

        ownedReg.BActive.Should().BeTrue();
        foreignReg.BActive.Should().Be(false);
        regRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once,
            "save runs once for the owned row");
    }
}
