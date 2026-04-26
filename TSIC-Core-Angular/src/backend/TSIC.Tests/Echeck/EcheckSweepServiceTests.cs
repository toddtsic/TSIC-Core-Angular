using AuthorizeNet.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Configuration;
using TSIC.API.Services.Echeck;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Echeck;

/// <summary>
/// Tests for the daily eCheck settlement sweep.
///
/// Behavior under test:
///   • Settled ADN tx → Settlement.Status = "Settled", no reversal
///   • Returned/voided/error → reversal RA row + fee credit reverse + director email
///   • capturedPendingSettlement → push NextCheckAt forward, no mutation
///   • Unrecognized status → leave Pending, increment errored
///   • SweepLog completed exactly once with correct counts
///
/// All collaborators are mocked — these tests are about orchestration, not persistence.
/// </summary>
public class EcheckSweepServiceTests
{
    private static readonly Guid EcheckReturnMethodId = Guid.Parse("2FECA575-A268-E111-9D56-F04DA202060D");
    private const string SystemUserId = "system-echeck-sweep";

    private readonly Mock<IEcheckSettlementRepository> _settleRepo = new();
    private readonly Mock<IRegistrationAccountingRepository> _accountingRepo = new();
    private readonly Mock<IRegistrationRepository> _regRepo = new();
    private readonly Mock<IAdnApiService> _adn = new();
    private readonly Mock<IRegistrationFeeAdjustmentService> _feeAdj = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<ILogger<EcheckSweepService>> _logger = new();
    private readonly EcheckSweepOptions _options = new() { Enabled = true, RetryDays = 1, InitialGraceDays = 2 };

    private EcheckSweepService BuildSut() => new(
        _settleRepo.Object,
        _accountingRepo.Object,
        _regRepo.Object,
        _adn.Object,
        _feeAdj.Object,
        _email.Object,
        Options.Create(_options),
        _logger.Object);

    // ── Test data builders ─────────────────────────────────────────────

    private static Settlement BuildPending(Guid jobId, decimal payAmount, string txId = "ADN-TX-1")
    {
        var ra = new RegistrationAccounting
        {
            AId = 42,
            RegistrationId = Guid.NewGuid(),
            TeamId = null,
            Payamt = payAmount,
            Registration = new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                JobId = jobId,
                FeeBase = 100m,
                FeeProcessing = 0.40m,
                FeeDiscount = 0,
                FeeDonation = 0,
                FeeLatefee = 0,
                FeeTotal = 100.40m,
                PaidTotal = 100.40m,
                OwedTotal = 0m
            }
        };
        ra.RegistrationId = ra.Registration.RegistrationId;

        return new Settlement
        {
            SettlementId = Guid.NewGuid(),
            RegistrationAccountingId = ra.AId,
            RegistrationAccounting = ra,
            AdnTransactionId = txId,
            Status = "Pending",
            SubmittedAt = DateTime.UtcNow.AddDays(-3),
            NextCheckAt = DateTime.UtcNow.AddMinutes(-1)
        };
    }

    private static getTransactionDetailsResponse BuildAdnResponse(string status, int reasonCode = 0, string reasonDesc = "")
    {
        return new getTransactionDetailsResponse
        {
            messages = new messagesType
            {
                resultCode = messageTypeEnum.Ok,
                message = new[] { new messagesTypeMessage { code = "I00001", text = "Successful." } }
            },
            transaction = new transactionDetailsType
            {
                transactionStatus = status,
                responseReasonCode = reasonCode,
                responseReasonDescription = reasonDesc
            }
        };
    }

    private void StubAdnSuccess(string status, int reasonCode = 0, string reasonDesc = "")
    {
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>()))
            .Returns(AuthorizeNet.Environment.PRODUCTION);
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(BuildAdnResponse(status, reasonCode, reasonDesc));
    }

    private void StubSweepLog()
    {
        _settleRepo.Setup(r => r.StartSweepLogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SweepLog { StartedAt = DateTime.UtcNow, TriggeredBy = "Test" });
    }

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "Settled ADN tx → Status=Settled, no reversal, counts.Settled=1")]
    public async Task Settled_MarksClearedNoReversal()
    {
        var jobId = Guid.NewGuid();
        var settlement = BuildPending(jobId, 100.40m);
        StubSweepLog();
        StubAdnSuccess("settledSuccessfully");
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settlement });

        var result = await BuildSut().RunAsync("Test");

        result.Checked.Should().Be(1);
        result.Settled.Should().Be(1);
        result.Returned.Should().Be(0);
        result.Errored.Should().Be(0);
        settlement.Status.Should().Be("Settled");
        settlement.SettledAt.Should().NotBeNull();
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never,
            "settled tx must not write a reversal row");
        _feeAdj.Verify(f => f.ReverseProcessingFeeForEcheckAsync(
            It.IsAny<Registrations>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never, "fee credit must not be reversed for a settled tx");
    }

    [Fact(DisplayName = "Returned ADN tx → reversal RA row, fee credit reversed, director emailed")]
    public async Task Returned_WritesReversalRow_ReversesFee_EmailsDirector()
    {
        var jobId = Guid.NewGuid();
        var settlement = BuildPending(jobId, 100.40m);
        var originalRegId = settlement.RegistrationAccounting.RegistrationId;
        StubSweepLog();
        StubAdnSuccess("returnedItem", reasonCode: 252, reasonDesc: "NSF");
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settlement });
        _regRepo.Setup(r => r.GetDirectorContactForJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorContactInfo { Email = "director@example.com", PaymentPlan = false });

        RegistrationAccounting? capturedReversalRow = null;
        _accountingRepo.Setup(a => a.Add(It.IsAny<RegistrationAccounting>()))
            .Callback<RegistrationAccounting>(ra => capturedReversalRow = ra);

        var result = await BuildSut().RunAsync("Test");

        result.Returned.Should().Be(1);
        result.Errored.Should().Be(0);

        settlement.Status.Should().Be("Returned");
        settlement.ReturnReasonCode.Should().Be("252");
        settlement.ReturnReasonText.Should().Be("NSF");

        // Reversal RA row written.
        capturedReversalRow.Should().NotBeNull();
        capturedReversalRow!.PaymentMethodId.Should().Be(EcheckReturnMethodId);
        capturedReversalRow.Payamt.Should().Be(-100.40m);
        capturedReversalRow.RegistrationId.Should().Be(originalRegId);
        capturedReversalRow.LebUserId.Should().Be(SystemUserId);

        // Fee credit reversed for the right reg + amount + jobId.
        _feeAdj.Verify(f => f.ReverseProcessingFeeForEcheckAsync(
            It.Is<Registrations>(r => r.RegistrationId == originalRegId),
            100.40m, jobId, SystemUserId), Times.Once);

        // Director got an email.
        _email.Verify(e => e.SendAsync(
            It.Is<EmailMessageDto>(m => m.ToAddresses!.Contains("director@example.com")),
            false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Returned tx with missing director email → still reverses, just skips email")]
    public async Task Returned_NoDirectorEmail_StillReverses()
    {
        var jobId = Guid.NewGuid();
        var settlement = BuildPending(jobId, 50m);
        StubSweepLog();
        StubAdnSuccess("returnedItem", reasonCode: 252, reasonDesc: "NSF");
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settlement });
        _regRepo.Setup(r => r.GetDirectorContactForJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DirectorContactInfo?)null);

        var result = await BuildSut().RunAsync("Test");

        result.Returned.Should().Be(1);
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Once,
            "reversal must still happen even without a director to notify");
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory(DisplayName = "All bounce statuses (settlementError, returnedItem, voided) trigger reversal")]
    [InlineData("settlementError")]
    [InlineData("returnedItem")]
    [InlineData("voided")]
    public async Task BounceStatuses_AllTriggerReversal(string adnStatus)
    {
        var jobId = Guid.NewGuid();
        var settlement = BuildPending(jobId, 75m);
        StubSweepLog();
        StubAdnSuccess(adnStatus, reasonCode: 99, reasonDesc: "test");
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settlement });
        _regRepo.Setup(r => r.GetDirectorContactForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DirectorContactInfo?)null);

        var result = await BuildSut().RunAsync("Test");

        result.Returned.Should().Be(1);
        settlement.Status.Should().Be("Returned");
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Once);
    }

    [Fact(DisplayName = "capturedPendingSettlement → no mutation, NextCheckAt pushed forward")]
    public async Task StillPending_PushesNextCheck()
    {
        var jobId = Guid.NewGuid();
        var settlement = BuildPending(jobId, 100m);
        var originalSubmitted = settlement.SubmittedAt;
        StubSweepLog();
        StubAdnSuccess("capturedPendingSettlement");
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settlement });

        var result = await BuildSut().RunAsync("Test");

        result.Checked.Should().Be(1);
        result.Settled.Should().Be(0);
        result.Returned.Should().Be(0);
        result.Errored.Should().Be(0);
        settlement.Status.Should().Be("Pending", "still in flight at the bank");
        settlement.NextCheckAt.Should().BeAfter(DateTime.UtcNow);
        settlement.SubmittedAt.Should().Be(originalSubmitted);
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
    }

    [Fact(DisplayName = "Unrecognized ADN status → counts.Errored++, leaves Pending")]
    public async Task UnknownStatus_CountsAsErrored()
    {
        var jobId = Guid.NewGuid();
        var settlement = BuildPending(jobId, 100m);
        StubSweepLog();
        StubAdnSuccess("authorizedPendingCapture");
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settlement });

        var result = await BuildSut().RunAsync("Test");

        result.Errored.Should().Be(1);
        settlement.Status.Should().Be("Pending");
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
    }

    [Fact(DisplayName = "ADN returns Error result → counts.Errored++, push next check")]
    public async Task AdnReturnsError_CountsAsErrored()
    {
        var jobId = Guid.NewGuid();
        var settlement = BuildPending(jobId, 100m);
        StubSweepLog();
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.PRODUCTION);
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new getTransactionDetailsResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Error }
            });
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settlement });

        var result = await BuildSut().RunAsync("Test");

        result.Errored.Should().Be(1);
        settlement.Status.Should().Be("Pending");
    }

    [Fact(DisplayName = "Empty pending list → SweepLog completed with all-zero counts, no work")]
    public async Task NoPending_LogsZeroCounts()
    {
        StubSweepLog();
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement>());

        var result = await BuildSut().RunAsync("Test");

        result.Checked.Should().Be(0);
        result.Settled.Should().Be(0);
        result.Returned.Should().Be(0);
        result.Errored.Should().Be(0);
        _settleRepo.Verify(r => r.CompleteSweepLogAsync(
            It.IsAny<SweepLog>(), 0, 0, 0, 0, null, It.IsAny<CancellationToken>()), Times.Once);
        _adn.Verify(a => a.ADN_GetTransactionDetails(
            It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact(DisplayName = "Mixed batch (1 settled, 1 returned, 1 still-pending) → counts add up correctly")]
    public async Task MixedBatch_CountsCorrectly()
    {
        var jobId = Guid.NewGuid();
        var settled = BuildPending(jobId, 100m, "TX-SETTLED");
        var returned = BuildPending(jobId, 50m, "TX-RETURNED");
        var stillPending = BuildPending(jobId, 25m, "TX-PENDING");

        StubSweepLog();
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.PRODUCTION);
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), "TX-SETTLED"))
            .Returns(BuildAdnResponse("settledSuccessfully"));
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), "TX-RETURNED"))
            .Returns(BuildAdnResponse("returnedItem", 252, "NSF"));
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), "TX-PENDING"))
            .Returns(BuildAdnResponse("capturedPendingSettlement"));
        _regRepo.Setup(r => r.GetDirectorContactForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DirectorContactInfo?)null);
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settled, returned, stillPending });

        var result = await BuildSut().RunAsync("Test");

        result.Checked.Should().Be(3);
        result.Settled.Should().Be(1);
        result.Returned.Should().Be(1);
        result.Errored.Should().Be(0);
        settled.Status.Should().Be("Settled");
        returned.Status.Should().Be("Returned");
        stillPending.Status.Should().Be("Pending");
    }

    [Fact(DisplayName = "Per-settlement exception → other rows still processed, errored counted")]
    public async Task PerRecordException_DoesNotAbortBatch()
    {
        var jobId = Guid.NewGuid();
        var bad = BuildPending(jobId, 100m, "TX-BAD");
        var good = BuildPending(jobId, 50m, "TX-GOOD");

        StubSweepLog();
        _adn.Setup(a => a.GetJobAdnCredentials_FromJobId(It.IsAny<Guid>(), It.IsAny<bool>()))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.PRODUCTION);
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), "TX-BAD"))
            .Throws(new InvalidOperationException("ADN unreachable"));
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), "TX-GOOD"))
            .Returns(BuildAdnResponse("settledSuccessfully"));
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { bad, good });

        var result = await BuildSut().RunAsync("Test");

        result.Errored.Should().Be(1);
        result.Settled.Should().Be(1);
        good.Status.Should().Be("Settled");
    }

    [Fact(DisplayName = "Director email throwing → reversal still completes (best-effort send)")]
    public async Task EmailFailure_DoesNotAbortReversal()
    {
        var jobId = Guid.NewGuid();
        var settlement = BuildPending(jobId, 100m);
        StubSweepLog();
        StubAdnSuccess("returnedItem", 252, "NSF");
        _settleRepo.Setup(r => r.GetPendingDueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Settlement> { settlement });
        _regRepo.Setup(r => r.GetDirectorContactForJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorContactInfo { Email = "director@example.com", PaymentPlan = false });
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("smtp down"));

        var result = await BuildSut().RunAsync("Test");

        result.Returned.Should().Be(1);
        result.Errored.Should().Be(0, "email is best-effort, must not bump errored");
        settlement.Status.Should().Be("Returned");
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Once);
    }
}
