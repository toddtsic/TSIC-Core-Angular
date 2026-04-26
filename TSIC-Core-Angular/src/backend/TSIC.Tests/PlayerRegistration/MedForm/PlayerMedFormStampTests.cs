using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.VerticalInsure;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.PlayerRegistration.MedForm;

/// <summary>
/// MED-FORM STAMP TESTS
///
/// Validates that BUploadedMedForm on a newly-created Registrations row is
/// driven by the on-disk medical-form file (via IMedFormService.Exists),
/// NEVER by the client-asserted value in the form payload. This is the
/// security-critical guarantee: only the server gets to say whether a
/// medical form is on file.
/// </summary>
public class PlayerMedFormStampTests
{
    private static readonly Guid TestJobId = Guid.NewGuid();
    private static readonly string TestFamilyUserId = "family-user-test";
    private static readonly string TestPlayerId = "player-test-1";

    private static (
        PlayerRegistrationService svc,
        Mock<IRegistrationRepository> regRepo,
        Mock<ITeamRepository> teamRepo,
        Mock<IMedFormService> medForms)
        CreateService()
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

        validation
            .Setup(v => v.ValidatePlayerFormValues(It.IsAny<string?>(), It.IsAny<List<PreSubmitTeamSelectionDto>>()))
            .Returns(new List<PreSubmitValidationErrorDto>());

        verticalInsure
            .Setup(v => v.BuildOfferAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(new PreSubmitInsuranceDto { Available = false });

        jobRepo
            .Setup(j => j.GetPreSubmitMetadataAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPreSubmitMetadata
            {
                PlayerProfileMetadataJson = null,
                JsonOptions = null,
                CoreRegformPlayer = "PP10",
            });

        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations>());
        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersTrackedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations>());

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

        return (svc, regRepo, teamRepo, medForms);
    }

    private static Teams SetupTeamWithRoom(
        Mock<ITeamRepository> teamRepo,
        Mock<IRegistrationRepository> regRepo)
    {
        var team = RegistrationDataBuilder.BuildTeam(TestJobId, Guid.NewGuid(), maxCount: 10);

        teamRepo
            .Setup(t => t.GetTeamsForJobAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { team });

        regRepo
            .Setup(r => r.GetActiveTeamRosterCountsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { { team.TeamId, 0 } });

        return team;
    }

    private static ReserveTeamsRequestDto MakeRequest(Guid teamId) => new()
    {
        JobPath = "test-job",
        TeamSelections = new List<ReserveTeamSelectionDto>
        {
            new() { PlayerId = TestPlayerId, TeamId = teamId }
        }
    };

    [Fact(DisplayName = "Reserve: med-form file present → BUploadedMedForm stamped true on new row")]
    public async Task Reserve_MedFormOnDisk_StampsTrue()
    {
        var (svc, regRepo, teamRepo, medForms) = CreateService();
        var team = SetupTeamWithRoom(teamRepo, regRepo);

        medForms.Setup(m => m.Exists(TestPlayerId)).Returns(true);

        Registrations? captured = null;
        regRepo.Setup(r => r.Add(It.IsAny<Registrations>()))
            .Callback<Registrations>(reg => captured = reg);

        await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeRequest(team.TeamId), TestFamilyUserId);

        captured.Should().NotBeNull();
        captured!.BUploadedMedForm.Should().BeTrue("server stamps from on-disk file existence");
        medForms.Verify(m => m.Exists(TestPlayerId), Times.AtLeastOnce);
    }

    [Fact(DisplayName = "Reserve: no med-form file → BUploadedMedForm stamped false on new row")]
    public async Task Reserve_NoMedFormOnDisk_StampsFalse()
    {
        var (svc, regRepo, teamRepo, medForms) = CreateService();
        var team = SetupTeamWithRoom(teamRepo, regRepo);

        medForms.Setup(m => m.Exists(TestPlayerId)).Returns(false);

        Registrations? captured = null;
        regRepo.Setup(r => r.Add(It.IsAny<Registrations>()))
            .Callback<Registrations>(reg => captured = reg);

        await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeRequest(team.TeamId), TestFamilyUserId);

        captured.Should().NotBeNull();
        captured!.BUploadedMedForm.Should().BeFalse("no file on disk → flag is false");
    }
}
