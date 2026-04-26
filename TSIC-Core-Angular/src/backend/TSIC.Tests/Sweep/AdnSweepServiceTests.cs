using AuthorizeNet.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Configuration;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Sweep;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Sweep;

/// <summary>
/// Tests for the unified ADN reconciliation sweep (ARB import + eCheck returns).
///
/// Covers:
///   • ARB tx: settled → RA inserted + Reg balances bumped + sub-status synced
///   • ARB tx: declined / generalError → RA inserted with $0, no Reg mutation
///   • ARB tx: already imported → skipped
///   • eCheck return: refTransId matches Settlement → reversal lands + director emailed
///   • eCheck return: refTransId not in our Settlements → logged, skipped (not ours)
///   • eCheck return: already processed → skipped
///   • Mixed batch (ARB + eCheck return) → both paths run
///   • Empty batch list → SweepLog completed with zero counts
///   • Per-tx exception → other tx still processed
///   • Email failure → reversal not aborted
/// </summary>
public class AdnSweepServiceTests
{
    private static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid EcheckReturnMethodId = Guid.Parse("2FECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid TsicCustomerId = Guid.Parse("60660D3C-6C8C-DC11-8046-00137250256D");

    private readonly Mock<IEcheckSettlementRepository> _settleRepo = new();
    private readonly Mock<IRegistrationAccountingRepository> _accountingRepo = new();
    private readonly Mock<IRegistrationRepository> _regRepo = new();
    private readonly Mock<IArbSubscriptionRepository> _arbRepo = new();
    private readonly Mock<IAdnApiService> _adn = new();
    private readonly Mock<IRegistrationFeeAdjustmentService> _feeAdj = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<ILogger<AdnSweepService>> _logger = new();
    private readonly TsicSettings _tsicSettings = new() { DefaultCustomerId = TsicCustomerId };
    private readonly AdnSweepOptions _options = new() { Enabled = true, DaysPriorWindow = 2 };

    private AdnSweepService BuildSut() => new(
        _settleRepo.Object,
        _accountingRepo.Object,
        _regRepo.Object,
        _arbRepo.Object,
        _adn.Object,
        _feeAdj.Object,
        _email.Object,
        Options.Create(_tsicSettings),
        Options.Create(_options),
        _logger.Object);

    // ── Test data builders ─────────────────────────────────────────────

    private void StubSweepLogAndCreds()
    {
        _settleRepo.Setup(r => r.StartSweepLogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SweepLog { StartedAt = DateTime.UtcNow, TriggeredBy = "Test" });
        _adn.Setup(a => a.GetJobAdnCredentials_FromCustomerId(TsicCustomerId, true))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment(It.IsAny<bool>())).Returns(AuthorizeNet.Environment.PRODUCTION);
    }

    private void StubBatchListWith(params transactionSummaryType[] txs)
    {
        _adn.Setup(a => a.GetSettleBatchList_FromDateRange(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<bool>()))
            .Returns(new getSettledBatchListResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                batchList = [new batchDetailsType { batchId = "BATCH-1" }]
            });
        _adn.Setup(a => a.GetTransactionList_ByBatchId(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), "BATCH-1"))
            .Returns(new getTransactionListResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                transactions = txs
            });
    }

    private static transactionSummaryType ArbTxSummary(string status, decimal settleAmount, string subId, string txId, string invoiceNumber)
    {
        return new transactionSummaryType
        {
            transId = txId,
            transactionStatus = status,
            settleAmount = settleAmount,
            invoiceNumber = invoiceNumber,
            submitTimeLocal = DateTime.Now,
            subscription = new subscriptionPaymentType { id = int.Parse(subId) }
        };
    }

    private static transactionSummaryType EcheckReturnSummary(string txId)
    {
        return new transactionSummaryType
        {
            transId = txId,
            transactionStatus = "returnedItem",
            settleAmount = 0m,
            submitTimeLocal = DateTime.Now
        };
    }

    private void StubArbTxDetails(string txId, string subId, decimal settleAmount, string cc4 = "1234", string ccExp = "12-2030")
    {
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), txId))
            .Returns(new getTransactionDetailsResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                transaction = new transactionDetailsType
                {
                    transId = txId,
                    settleAmount = settleAmount,
                    payment = new paymentMaskedType
                    {
                        Item = new creditCardMaskedType { cardNumber = $"XXXX{cc4}", expirationDate = ccExp }
                    },
                    responseReasonDescription = "ok",
                    subscription = new subscriptionPaymentType { id = int.Parse(subId) }
                }
            });
    }

    private void StubEcheckReturnDetails(string returnTxId, string originalTxId, int reasonCode = 252, string reasonDesc = "NSF")
    {
        _adn.Setup(a => a.ADN_GetTransactionDetails(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), returnTxId))
            .Returns(new getTransactionDetailsResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                transaction = new transactionDetailsType
                {
                    transId = returnTxId,
                    refTransId = originalTxId,
                    transactionStatus = "returnedItem",
                    responseReasonCode = reasonCode,
                    responseReasonDescription = reasonDesc
                }
            });
    }

    private void StubSubStatus(string subId, ARBSubscriptionStatusEnum status = ARBSubscriptionStatusEnum.active)
    {
        _adn.Setup(a => a.GetSubscriptionStatus(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), subId))
            .Returns(new ARBGetSubscriptionStatusResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                status = status
            });
    }

    private static Registrations BuildReg(decimal paid = 0, decimal owed = 100m)
    {
        return new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            UserId = "user-1",
            FamilyUserId = "family-1",
            AdnSubscriptionId = "777",
            AdnSubscriptionStatus = "active",
            FeeBase = 100m,
            FeeProcessing = 0m,
            FeeDiscount = 0m,
            FeeDonation = 0m,
            FeeLatefee = 0m,
            FeeTotal = 100m,
            PaidTotal = paid,
            OwedTotal = owed
        };
    }

    private static Settlement BuildSettlement(string adnTxId, decimal payAmount = 100m)
    {
        var ra = new RegistrationAccounting
        {
            AId = 99,
            RegistrationId = Guid.NewGuid(),
            Payamt = payAmount,
            Registration = new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                JobId = Guid.NewGuid(),
                FeeBase = 100m,
                FeeProcessing = 0.40m,
                FeeTotal = 100.40m,
                PaidTotal = payAmount,
                OwedTotal = 0m
            }
        };
        ra.RegistrationId = ra.Registration.RegistrationId;
        return new Settlement
        {
            SettlementId = Guid.NewGuid(),
            RegistrationAccountingId = ra.AId,
            RegistrationAccounting = ra,
            AdnTransactionId = adnTxId,
            Status = "Pending",
            SubmittedAt = DateTime.UtcNow.AddDays(-3),
            NextCheckAt = DateTime.UtcNow.AddDays(-1)
        };
    }

    // ── Tests ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "ARB settled tx → RA inserted, PaidTotal/OwedTotal bumped, sub-status synced")]
    public async Task Arb_Settled_ImportsAndBumpsBalances()
    {
        var reg = BuildReg(paid: 0m, owed: 100m);
        StubSweepLogAndCreds();
        var tx = ArbTxSummary("settledSuccessfully", 50m, "777", "TX-1", "TSIC_123_456");
        StubBatchListWith(tx);
        StubArbTxDetails("TX-1", "777", 50m);
        StubSubStatus("777", ARBSubscriptionStatusEnum.active);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("TX-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("777", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);

        RegistrationAccounting? capturedRa = null;
        _accountingRepo.Setup(a => a.Add(It.IsAny<RegistrationAccounting>()))
            .Callback<RegistrationAccounting>(ra => capturedRa = ra);

        var result = await BuildSut().RunAsync("Test");

        result.ArbImported.Should().Be(1);
        result.Errored.Should().Be(0);

        capturedRa.Should().NotBeNull();
        capturedRa!.PaymentMethodId.Should().Be(CcPaymentMethodId);
        capturedRa.AdnTransactionId.Should().Be("TX-1");
        capturedRa.Payamt.Should().Be(50m);
        capturedRa.AdnInvoiceNo.Should().Be("TSIC_123_456");
        capturedRa.AdnCc4.Should().Be("1234");

        reg.PaidTotal.Should().Be(50m);
        reg.OwedTotal.Should().Be(50m);
    }

    [Fact(DisplayName = "ARB declined tx → RA inserted with Payamt=$0, no balance change")]
    public async Task Arb_Declined_InsertsZeroAmountRowOnly()
    {
        var reg = BuildReg(paid: 0m, owed: 100m);
        StubSweepLogAndCreds();
        var tx = ArbTxSummary("declined", 50m, "777", "TX-2", "TSIC_123_456");
        StubBatchListWith(tx);
        StubArbTxDetails("TX-2", "777", 50m);
        StubSubStatus("777");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("TX-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("777", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);

        RegistrationAccounting? capturedRa = null;
        _accountingRepo.Setup(a => a.Add(It.IsAny<RegistrationAccounting>()))
            .Callback<RegistrationAccounting>(ra => capturedRa = ra);

        var result = await BuildSut().RunAsync("Test");

        result.ArbImported.Should().Be(1);
        capturedRa!.Payamt.Should().Be(0m, "declined tx writes a $0 trail row");
        reg.PaidTotal.Should().Be(0m, "no balance change for declined");
    }

    [Fact(DisplayName = "ARB tx already imported → skipped, no Add call")]
    public async Task Arb_AlreadyImported_Skipped()
    {
        StubSweepLogAndCreds();
        var tx = ArbTxSummary("settledSuccessfully", 50m, "777", "TX-3", "TSIC_123_456");
        StubBatchListWith(tx);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("TX-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await BuildSut().RunAsync("Test");

        result.Checked.Should().Be(1, "tx is in batch");
        result.ArbImported.Should().Be(0, "but not imported (dup)");
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
    }

    [Fact(DisplayName = "ARB tx with sub-status drift → status synced before RA insert")]
    public async Task Arb_SubStatusDrift_SyncsBeforeImport()
    {
        var reg = BuildReg();
        reg.AdnSubscriptionStatus = "active";
        StubSweepLogAndCreds();
        var tx = ArbTxSummary("settledSuccessfully", 50m, "777", "TX-4", "TSIC_123_456");
        StubBatchListWith(tx);
        StubArbTxDetails("TX-4", "777", 50m);
        StubSubStatus("777", ARBSubscriptionStatusEnum.canceled);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("777", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);

        var result = await BuildSut().RunAsync("Test");

        _arbRepo.Verify(a => a.UpdateSubscriptionStatusAsync(
            reg.RegistrationId, "canceled", It.IsAny<CancellationToken>()), Times.Once);
        reg.AdnSubscriptionStatus.Should().Be("canceled");
    }

    [Fact(DisplayName = "eCheck return matches Settlement → reversal lands + director emailed")]
    public async Task EcheckReturn_MatchesSettlement_Reverses()
    {
        var settlement = BuildSettlement("ORIG-TX-100", payAmount: 100m);
        var reg = settlement.RegistrationAccounting.Registration!;
        StubSweepLogAndCreds();
        var tx = EcheckReturnSummary("RETURN-TX-200");
        StubBatchListWith(tx);
        StubEcheckReturnDetails("RETURN-TX-200", originalTxId: "ORIG-TX-100", reasonCode: 252, reasonDesc: "NSF");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("RETURN-TX-200", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.Is<IEnumerable<string>>(ids => ids.Contains("ORIG-TX-100")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);
        _regRepo.Setup(r => r.GetDirectorContactForJobAsync(reg.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorContactInfo { Email = "director@example.com", PaymentPlan = false });

        RegistrationAccounting? capturedReversal = null;
        _accountingRepo.Setup(a => a.Add(It.IsAny<RegistrationAccounting>()))
            .Callback<RegistrationAccounting>(ra => capturedReversal = ra);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckReturnsProcessed.Should().Be(1);
        settlement.Status.Should().Be("Returned");
        settlement.ReturnReasonCode.Should().Be("252");
        settlement.ReturnReasonText.Should().Be("NSF");

        capturedReversal.Should().NotBeNull();
        capturedReversal!.PaymentMethodId.Should().Be(EcheckReturnMethodId);
        capturedReversal.Payamt.Should().Be(-100m);
        capturedReversal.AdnTransactionId.Should().Be("RETURN-TX-200");

        _feeAdj.Verify(f => f.ReverseProcessingFeeForEcheckAsync(
            reg, 100m, reg.JobId, It.IsAny<string>()), Times.Once);

        _email.Verify(e => e.SendAsync(
            It.Is<EmailMessageDto>(m => m.ToAddresses!.Contains("director@example.com")
                                       && m.Subject!.Contains("NSF")),
            false, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact(DisplayName = "eCheck return with no matching Settlement → logged + skipped (not ours)")]
    public async Task EcheckReturn_NoSettlement_Skipped()
    {
        StubSweepLogAndCreds();
        var tx = EcheckReturnSummary("RETURN-TX-NOMATCH");
        StubBatchListWith(tx);
        StubEcheckReturnDetails("RETURN-TX-NOMATCH", originalTxId: "ORIG-WE-DONT-OWN");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("RETURN-TX-NOMATCH", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckReturnsProcessed.Should().Be(0);
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
        _feeAdj.Verify(f => f.ReverseProcessingFeeForEcheckAsync(
            It.IsAny<Registrations>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact(DisplayName = "eCheck return already processed → skipped on dup tx-id check")]
    public async Task EcheckReturn_AlreadyProcessed_Skipped()
    {
        StubSweepLogAndCreds();
        var tx = EcheckReturnSummary("RETURN-TX-DUP");
        StubBatchListWith(tx);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("RETURN-TX-DUP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckReturnsProcessed.Should().Be(0);
        _settleRepo.Verify(r => r.GetByAdnTransactionIdsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Mixed batch (1 ARB + 1 eCheck return) → both paths execute")]
    public async Task MixedBatch_BothPathsRun()
    {
        var reg = BuildReg(paid: 0m, owed: 100m);
        var settlement = BuildSettlement("ORIG-100", payAmount: 50m);

        StubSweepLogAndCreds();
        var arbTx = ArbTxSummary("settledSuccessfully", 25m, "777", "ARB-TX", "TSIC_1_2");
        var returnTx = EcheckReturnSummary("RETURN-TX");
        StubBatchListWith(arbTx, returnTx);
        StubArbTxDetails("ARB-TX", "777", 25m);
        StubSubStatus("777");
        StubEcheckReturnDetails("RETURN-TX", originalTxId: "ORIG-100");

        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("777", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);
        _regRepo.Setup(r => r.GetDirectorContactForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DirectorContactInfo?)null);

        var result = await BuildSut().RunAsync("Test");

        result.Checked.Should().Be(2);
        result.ArbImported.Should().Be(1);
        result.EcheckReturnsProcessed.Should().Be(1);
        result.Errored.Should().Be(0);
    }

    [Fact(DisplayName = "Empty batch list → SweepLog completed with all-zero counts")]
    public async Task EmptyBatch_LogsZeroCounts()
    {
        StubSweepLogAndCreds();
        _adn.Setup(a => a.GetSettleBatchList_FromDateRange(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<bool>()))
            .Returns(new getSettledBatchListResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                batchList = []
            });

        var result = await BuildSut().RunAsync("Test");

        result.Checked.Should().Be(0);
        result.ArbImported.Should().Be(0);
        result.EcheckReturnsProcessed.Should().Be(0);
        _settleRepo.Verify(r => r.CompleteSweepLogAsync(
            It.IsAny<SweepLog>(), 0, 0, 0, 0, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Per-tx exception → other tx in batch still processed, errored counted")]
    public async Task PerTxException_DoesNotAbortBatch()
    {
        var reg = BuildReg();
        StubSweepLogAndCreds();
        var bad = ArbTxSummary("settledSuccessfully", 25m, "777", "BAD-TX", "TSIC_1_2");
        var good = ArbTxSummary("settledSuccessfully", 25m, "888", "GOOD-TX", "TSIC_1_3");
        StubBatchListWith(bad, good);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("777", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB blew up"));
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("888", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);
        StubArbTxDetails("GOOD-TX", "888", 25m);
        StubSubStatus("888");

        var result = await BuildSut().RunAsync("Test");

        result.Errored.Should().Be(1);
        result.ArbImported.Should().Be(1, "GOOD-TX still imported despite BAD-TX failure");
    }

    [Fact(DisplayName = "Director email throws → reversal still completes")]
    public async Task EmailFailure_DoesNotAbortReversal()
    {
        var settlement = BuildSettlement("ORIG-X", payAmount: 75m);
        var reg = settlement.RegistrationAccounting.Registration!;
        StubSweepLogAndCreds();
        var tx = EcheckReturnSummary("RETURN-X");
        StubBatchListWith(tx);
        StubEcheckReturnDetails("RETURN-X", originalTxId: "ORIG-X");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("RETURN-X", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);
        _regRepo.Setup(r => r.GetDirectorContactForJobAsync(reg.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorContactInfo { Email = "director@example.com", PaymentPlan = false });
        // First email (director alert) throws; digest email succeeds.
        _email.SetupSequence(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("smtp down"))
            .ReturnsAsync(true);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckReturnsProcessed.Should().Be(1, "reversal must still complete despite email failure");
        result.Errored.Should().Be(0);
        settlement.Status.Should().Be("Returned");
    }
}
