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
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Sweep;

/// <summary>
/// Tests for the unified ADN reconciliation sweep (ARB import + eCheck returns).
///
/// Covers:
///   • ARB tx: settled → RA inserted + Reg balances bumped + sub-status synced
///   • ARB tx: declined / generalError → RA inserted with $0, no Reg mutation
///   • ARB tx: already imported → skipped
///   • eCheck return: refTransId matches Settlement → reversal lands (digest-only reporting)
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
    private static readonly Guid FailedEcheckPaymentMethodId = Guid.Parse("2FECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid TsicCustomerId = Guid.Parse("60660D3C-6C8C-DC11-8046-00137250256D");

    private readonly Mock<IEcheckSettlementRepository> _settleRepo = new();
    private readonly Mock<IRegistrationAccountingRepository> _accountingRepo = new();
    private readonly Mock<IRegistrationRepository> _regRepo = new();
    private readonly Mock<ITeamRepository> _teamRepo = new();
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
        _teamRepo.Object,
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
        // Watchdog + integrity-net steps run on every sweep; default to "nothing stale, nothing untracked".
        _settleRepo.Setup(r => r.GetStalePendingAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _settleRepo.Setup(r => r.GetUntrackedEcheckAccountingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _adn.Setup(a => a.GetJobAdnCredentials_FromCustomerId(TsicCustomerId))
            .ReturnsAsync(new AdnCredentialsViewModel { AdnLoginId = "login", AdnTransactionKey = "key" });
        _adn.Setup(a => a.GetADNEnvironment()).Returns(AuthorizeNet.Environment.PRODUCTION);
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

    /// <summary>
    /// Settled eCheck tx — no subscription / invoiceNumber so it won't match the ARB filter.
    /// </summary>
    private static transactionSummaryType EcheckSettledSummary(string txId, decimal settleAmount = 100m)
    {
        return new transactionSummaryType
        {
            transId = txId,
            transactionStatus = "settledSuccessfully",
            settleAmount = settleAmount,
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

    private static Registrations BuildReg(decimal paid = 0, decimal owed = 100m, string jobName = "Test Tournament")
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
            OwedTotal = owed,
            Job = new Jobs { JobId = Guid.NewGuid(), JobName = jobName, DisplayName = jobName }
        };
    }

    private static Settlement BuildSettlement(string adnTxId, decimal payAmount = 100m, string jobName = "Bounce Job")
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
                UserId = "bouncer-user",
                FeeBase = 100m,
                FeeProcessing = 0.40m,
                FeeTotal = 100.40m,
                PaidTotal = payAmount,
                OwedTotal = 0m,
                Job = new Jobs { JobName = jobName, DisplayName = jobName }
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

    [Fact(DisplayName = "ARB settled tx → recorded via the ledger chokepoint, stamped SystemUser (not the registrant)")]
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

        // A settled installment goes through the ledger chokepoint, which writes the row AND
        // re-derives the registration's PaidTotal/OwedTotal in one transaction (the recompute
        // itself is proven in RecordPaymentAndRecomputeTests). Here we assert the sweep's half:
        // it hands the chokepoint the right row, stamped with the system superuser — the sweep
        // is the actor, NOT the registrant (FamilyUserId would be a mis-attribution).
        RegistrationAccounting? capturedRa = null;
        string? capturedUserId = null;
        _accountingRepo.Setup(a => a.RecordPaymentAndRecomputeAsync(
                It.IsAny<RegistrationAccounting>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<RegistrationAccounting, string, CancellationToken>((ra, uid, _) =>
            {
                capturedRa = ra;
                capturedUserId = uid;
            })
            .Returns(Task.CompletedTask);

        var result = await BuildSut().RunAsync("Test");

        result.ArbImported.Should().Be(1);
        result.Errored.Should().Be(0);

        capturedRa.Should().NotBeNull();
        capturedRa!.PaymentMethodId.Should().Be(CcPaymentMethodId);
        capturedRa.AdnTransactionId.Should().Be("TX-1");
        capturedRa.Payamt.Should().Be(50m);
        capturedRa.AdnInvoiceNo.Should().Be("TSIC_123_456");
        capturedRa.AdnCc4.Should().Be("1234");

        capturedUserId.Should().Be(TsicConstants.SuperUserId,
            "the sweep is the actor — the registration is stamped with the system superuser, not the registrant");
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

        await BuildSut().RunAsync("Test");

        _arbRepo.Verify(a => a.UpdateSubscriptionStatusAsync(
            reg.RegistrationId, "canceled", It.IsAny<CancellationToken>()), Times.Once);
        reg.AdnSubscriptionStatus.Should().Be("canceled");
    }

    [Fact(DisplayName = "eCheck return matches Settlement → reversal lands, digest is the only email")]
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

        // The reversal lands through the ledger chokepoint (writes the -amount row AND re-derives
        // the registration's totals in one txn; the recompute is proven in
        // RecordPaymentAndRecomputeTests). Assert the sweep's half: the right reversal row,
        // keyed to the registration (no TeamId), stamped SystemUser.
        RegistrationAccounting? capturedReversal = null;
        string? capturedUserId = null;
        _accountingRepo.Setup(a => a.RecordPaymentAndRecomputeAsync(
                It.IsAny<RegistrationAccounting>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<RegistrationAccounting, string, CancellationToken>((ra, uid, _) =>
            {
                capturedReversal = ra;
                capturedUserId = uid;
            })
            .Returns(Task.CompletedTask);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckReturnsProcessed.Should().Be(1);
        settlement.Status.Should().Be("Returned");
        settlement.ReturnReasonCode.Should().Be("252");
        settlement.ReturnReasonText.Should().Be("NSF");

        capturedReversal.Should().NotBeNull();
        capturedReversal!.PaymentMethodId.Should().Be(FailedEcheckPaymentMethodId);
        capturedReversal.Payamt.Should().Be(-100m);
        capturedReversal.AdnTransactionId.Should().Be("RETURN-TX-200");
        capturedReversal.TeamId.Should().BeNull("player eCheck return reverses on the registration, not a team");
        capturedUserId.Should().Be(TsicConstants.SuperUserId,
            "the NSF sweep is the actor — stamped with the system superuser");

        _feeAdj.Verify(f => f.ReverseProcessingFeeForEcheckAsync(
            reg, 100m, reg.JobId, It.IsAny<string>()), Times.Once);

        // No per-return director alert — returns route to the support digest only.
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once, "the run's digest is the only email sent");
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

    [Fact(DisplayName = "Digest email throws → reversal still completes, run result intact")]
    public async Task EmailFailure_DoesNotAbortReversal()
    {
        var settlement = BuildSettlement("ORIG-X", payAmount: 75m);
        StubSweepLogAndCreds();
        var tx = EcheckReturnSummary("RETURN-X");
        StubBatchListWith(tx);
        StubEcheckReturnDetails("RETURN-X", originalTxId: "ORIG-X");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("RETURN-X", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);
        // The digest (the run's only email) throws — booked money must not depend on SMTP.
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("smtp down"));

        var result = await BuildSut().RunAsync("Test");

        result.EcheckReturnsProcessed.Should().Be(1, "reversal must still complete despite email failure");
        result.Errored.Should().Be(0);
        settlement.Status.Should().Be("Returned");
    }

    [Fact(DisplayName = "Digest email body has BOTH ARB rows AND eCheck Returns rows when both happened")]
    public async Task DigestEmail_IncludesBothArbAndEcheckTables()
    {
        var reg = BuildReg(jobName: "Spring Tournament");
        var settlement = BuildSettlement("ORIG-99", payAmount: 60m, jobName: "Bounce Job");

        StubSweepLogAndCreds();
        var arbTx = ArbTxSummary("settledSuccessfully", 25m, "777", "ARB-DIGEST", "TSIC_1_2");
        var ecTx = EcheckReturnSummary("RETURN-DIGEST");
        StubBatchListWith(arbTx, ecTx);
        StubArbTxDetails("ARB-DIGEST", "777", 25m);
        StubSubStatus("777");
        StubEcheckReturnDetails("RETURN-DIGEST", originalTxId: "ORIG-99", reasonCode: 252, reasonDesc: "NSF");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("777", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);

        // Capture the digest email ("AdnSweep" subject) — the run's only email.
        var sentEmails = new List<EmailMessageDto>();
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessageDto, bool, CancellationToken>((m, _, _) => sentEmails.Add(m))
            .ReturnsAsync(true);

        await BuildSut().RunAsync("Test");

        var digest = sentEmails.LastOrDefault(m => m.Subject!.StartsWith("AdnSweep"));
        digest.Should().NotBeNull("a digest email should always be sent");

        var html = digest!.HtmlBody!;

        // ARB section present and populated.
        html.Should().Contain("ARB Activity");
        html.Should().Contain("ARB-DIGEST", "the ARB tx id should appear in the ARB table");
        html.Should().Contain("Spring Tournament", "JobName should be populated for ARB rows (was '' before fix)");

        // eCheck Returns section present and populated.
        html.Should().Contain("eCheck Returns");
        html.Should().Contain("RETURN-DIGEST", "the return tx id should appear in the eCheck Returns table");
        html.Should().Contain("ORIG-99", "the original tx id should appear in the eCheck Returns table");
        html.Should().Contain("NSF", "the return reason should appear in the eCheck Returns table");
        html.Should().Contain("Bounce Job", "JobName should be populated for eCheck rows");
        html.Should().Contain("returned before settlement recorded",
            "the Type column classifies the return (this settlement was still Pending when the return arrived)");
    }

    // ── eCheck Pending → Settled ──────────────────────────────────────

    [Fact(DisplayName = "eCheck settled tx matches Pending Settlement → Status flips to Settled, no money movement")]
    public async Task EcheckSettled_MatchesPending_FlipsStatus()
    {
        var settlement = BuildSettlement("EC-SETTLED-1", payAmount: 100m);
        var ra = settlement.RegistrationAccounting;
        var reg = ra.Registration!;
        var origPaid = reg.PaidTotal;
        var origOwed = reg.OwedTotal;

        StubSweepLogAndCreds();
        StubBatchListWith(EcheckSettledSummary("EC-SETTLED-1", settleAmount: 100m));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.Is<IEnumerable<string>>(ids => ids.Contains("EC-SETTLED-1")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckSettled.Should().Be(1);
        result.Errored.Should().Be(0);
        settlement.Status.Should().Be("Settled");
        settlement.SettledAt.Should().NotBeNull();
        settlement.LastCheckedAt.Should().NotBeNull();
        // No money movement — already credited at submission time.
        reg.PaidTotal.Should().Be(origPaid);
        reg.OwedTotal.Should().Be(origOwed);
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
        _feeAdj.Verify(f => f.ReverseProcessingFeeForEcheckAsync(
            It.IsAny<Registrations>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact(DisplayName = "eCheck settled tx with no matching Settlement → no-op (not ours)")]
    public async Task EcheckSettled_NoMatch_NoOp()
    {
        StubSweepLogAndCreds();
        StubBatchListWith(EcheckSettledSummary("EC-NOT-OURS"));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckSettled.Should().Be(0);
        result.Errored.Should().Be(0);
    }

    [Fact(DisplayName = "Settlement already in Settled status → skipped (idempotent)")]
    public async Task EcheckSettled_AlreadySettled_Skipped()
    {
        var settlement = BuildSettlement("EC-DUP", payAmount: 100m);
        settlement.Status = "Settled";
        var origSettledAt = DateTime.UtcNow.AddDays(-2);
        settlement.SettledAt = origSettledAt;

        StubSweepLogAndCreds();
        StubBatchListWith(EcheckSettledSummary("EC-DUP"));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckSettled.Should().Be(0, "already-Settled rows are filtered out before the loop");
        settlement.Status.Should().Be("Settled");
        settlement.SettledAt.Should().Be(origSettledAt, "existing SettledAt must not be overwritten");
    }

    [Fact(DisplayName = "Returned settlement seen as settled in batch → not re-flipped to Settled")]
    public async Task EcheckSettled_ReturnedSettlement_NotOverwritten()
    {
        // Edge case: a Settlement was previously marked "Returned" but its original tx id still
        // appears in the batch list as settledSuccessfully. The status filter must protect us.
        var settlement = BuildSettlement("EC-RETURNED", payAmount: 100m);
        settlement.Status = "Returned";
        settlement.ReturnReasonText = "NSF";

        StubSweepLogAndCreds();
        StubBatchListWith(EcheckSettledSummary("EC-RETURNED"));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckSettled.Should().Be(0);
        settlement.Status.Should().Be("Returned", "Returned status must not be flipped back to Settled");
    }

    [Fact(DisplayName = "SweepLog records the eCheck settled count, not the ARB count")]
    public async Task SweepLog_RecordsEcheckSettledCount()
    {
        var settlement = BuildSettlement("EC-LOG", payAmount: 50m);
        StubSweepLogAndCreds();
        StubBatchListWith(EcheckSettledSummary("EC-LOG"));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);

        await BuildSut().RunAsync("Test");

        _settleRepo.Verify(r => r.CompleteSweepLogAsync(
            It.IsAny<SweepLog>(),
            1,    // recordsChecked
            1,    // recordsSettled — eCheck Pending → Settled count
            0,    // recordsReturned
            0,    // recordsErrored
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Digest email includes 'eCheck Settled' section with row data when transitions occurred")]
    public async Task DigestEmail_IncludesSettledSection()
    {
        var settlement = BuildSettlement("EC-DIGEST", payAmount: 75m, jobName: "Spring League");
        settlement.AccountLast4 = "1234";
        StubSweepLogAndCreds();
        StubBatchListWith(EcheckSettledSummary("EC-DIGEST"));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);

        var sentEmails = new List<EmailMessageDto>();
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessageDto, bool, CancellationToken>((m, _, _) => sentEmails.Add(m))
            .ReturnsAsync(true);

        await BuildSut().RunAsync("Test");

        var digest = sentEmails.LastOrDefault(m => m.Subject!.StartsWith("AdnSweep"));
        digest.Should().NotBeNull();
        var html = digest!.HtmlBody!;
        html.Should().Contain("eCheck Settled");
        html.Should().Contain("EC-DIGEST");
        html.Should().Contain("Spring League");
        html.Should().Contain("****1234");
    }

    [Fact(DisplayName = "Sub-status sync skipped when ADN response is non-Ok")]
    public async Task SubStatus_NonOkResponse_DoesNotMutateLocal()
    {
        var reg = BuildReg();
        reg.AdnSubscriptionStatus = "active";
        StubSweepLogAndCreds();
        var tx = ArbTxSummary("settledSuccessfully", 25m, "777", "TX-NONOK", "TSIC_1_2");
        StubBatchListWith(tx);
        StubArbTxDetails("TX-NONOK", "777", 25m);
        // Sub-status returns Error result, not Ok.
        _adn.Setup(a => a.GetSubscriptionStatus(
                It.IsAny<AuthorizeNet.Environment>(), It.IsAny<string>(), It.IsAny<string>(), "777"))
            .Returns(new ARBGetSubscriptionStatusResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Error },
                status = ARBSubscriptionStatusEnum.canceled
            });
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("777", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);

        await BuildSut().RunAsync("Test");

        reg.AdnSubscriptionStatus.Should().Be("active", "non-Ok ADN response must not overwrite local status");
        _arbRepo.Verify(a => a.UpdateSubscriptionStatusAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Team-side ARB-Trial subscription handling ─────────────────────

    private static Teams BuildTeam(decimal owed = 510m, string subId = "888", string subStatus = "active", string jobName = "Trial Tournament")
    {
        var jobId = Guid.NewGuid();
        return new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            AgegroupId = Guid.NewGuid(),
            ClubrepRegistrationid = Guid.NewGuid(),
            ClubrepId = "club-rep-user",
            TeamName = "U10 Red",
            AdnSubscriptionId = subId,
            AdnSubscriptionStatus = subStatus,
            AdnSubscriptionStartDate = DateTime.Now.Date.AddDays(-1),
            AdnSubscriptionIntervalLength = 30,
            AdnSubscriptionBillingOccurences = 2,
            AdnSubscriptionAmountPerOccurence = 310.50m,
            FeeBase = 500m,
            FeeProcessing = 10.50m,
            FeeTotal = 510.50m,
            OwedTotal = owed,
            PaidTotal = 0m,
            Active = true,
            TeamAi = 1,
            Modified = DateTime.UtcNow,
            Job = new Jobs { JobId = jobId, JobName = jobName, DisplayName = jobName }
        };
    }

    [Fact(DisplayName = "ARB tx for team-side sub → team financials updated, rep aggregate synced")]
    public async Task Arb_TeamSide_Settled_UpdatesTeamAndSyncsRep()
    {
        // Consistent seed: FeeBase 500 + FeeProcessing 10.50 = FeeTotal 510.50, PaidTotal 0 -> owed 510.50.
        var team = BuildTeam(owed: 510.50m, subId: "888");
        StubSweepLogAndCreds();
        var tx = ArbTxSummary("settledSuccessfully", 200m, "888", "TX-TEAM-DEPOSIT", "TSIC_100_1");
        StubBatchListWith(tx);
        StubArbTxDetails("TX-TEAM-DEPOSIT", "888", 200m);
        StubSubStatus("888", ARBSubscriptionStatusEnum.active);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("TX-TEAM-DEPOSIT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // Registration lookup MISSES (sub belongs to a team)
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("888", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Registrations?)null);
        _teamRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("888", It.IsAny<CancellationToken>()))
            .ReturnsAsync(team);

        // A settled team installment goes through the ledger chokepoint, which writes the row
        // AND re-derives the team's PaidTotal/OwedTotal in one transaction (the recompute itself
        // is proven in RecordPaymentAndRecomputeTests). Assert the sweep's half here.
        RegistrationAccounting? capturedRa = null;
        string? capturedUserId = null;
        _accountingRepo.Setup(a => a.RecordPaymentAndRecomputeAsync(
                It.IsAny<RegistrationAccounting>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<RegistrationAccounting, string, CancellationToken>((ra, uid, _) =>
            {
                capturedRa = ra;
                capturedUserId = uid;
            })
            .Returns(Task.CompletedTask);

        var result = await BuildSut().RunAsync("Test");

        result.ArbImported.Should().Be(1);
        result.Errored.Should().Be(0);
        // RA tagged with both rep RegistrationId AND TeamId so refunds/audits resolve cleanly,
        // keyed to the team so the chokepoint recomputes the team, stamped SystemUser.
        capturedRa.Should().NotBeNull();
        capturedRa!.RegistrationId.Should().Be(team.ClubrepRegistrationid);
        capturedRa.TeamId.Should().Be(team.TeamId);
        capturedRa.Payamt.Should().Be(200m);
        capturedRa.AdnTransactionId.Should().Be("TX-TEAM-DEPOSIT");
        capturedUserId.Should().Be(TsicConstants.SuperUserId,
            "the sweep is the actor — stamped with the system superuser, not the registrant");
        // Rep aggregate sync fires AFTER the chokepoint so it reads the new team totals.
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(
            team.ClubrepRegistrationid!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "ARB tx subId belongs to neither registration nor team → logged + skipped")]
    public async Task Arb_OrphanSub_NoRegOrTeam_Skipped()
    {
        StubSweepLogAndCreds();
        var tx = ArbTxSummary("settledSuccessfully", 100m, "999", "TX-ORPHAN", "TSIC_100_1");
        StubBatchListWith(tx);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("TX-ORPHAN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Registrations?)null);
        _teamRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Teams?)null);

        var result = await BuildSut().RunAsync("Test");

        result.ArbImported.Should().Be(0);
        result.Errored.Should().Be(0);
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "ARB tx for team-side: registration lookup tried first, team lookup is fallback")]
    public async Task Arb_TeamSide_RegistrationCheckedFirst()
    {
        // Consistent seed: FeeBase 500 + FeeProcessing 10.50 = FeeTotal 510.50, PaidTotal 0 -> owed 510.50.
        var team = BuildTeam(owed: 510.50m, subId: "888");
        StubSweepLogAndCreds();
        var tx = ArbTxSummary("settledSuccessfully", 200m, "888", "TX-LOOKUP", "TSIC_100_1");
        StubBatchListWith(tx);
        StubArbTxDetails("TX-LOOKUP", "888", 200m);
        StubSubStatus("888");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("888", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Registrations?)null);
        _teamRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("888", It.IsAny<CancellationToken>()))
            .ReturnsAsync(team);

        await BuildSut().RunAsync("Test");

        _regRepo.Verify(r => r.GetByAdnSubscriptionIdAsync("888", It.IsAny<CancellationToken>()), Times.Once);
        _teamRepo.Verify(r => r.GetByAdnSubscriptionIdAsync("888", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "eCheck NSF on RA with TeamId → team financials reversed + rep aggregate synced")]
    public async Task EcheckReturn_TeamSide_ReversesOnTeamAndSyncsRep()
    {
        var team = BuildTeam(owed: 0m, subId: "888"); // optimistic credit applied at submit
        team.PaidTotal = 510.50m;
        team.OwedTotal = 0m;

        // Build a Settlement whose RA carries a TeamId — this is the team eCheck path.
        var ra = new RegistrationAccounting
        {
            AId = 42,
            RegistrationId = team.ClubrepRegistrationid,
            TeamId = team.TeamId,
            Payamt = 510.50m,
            Registration = new Registrations
            {
                RegistrationId = team.ClubrepRegistrationid!.Value,
                JobId = team.JobId,
                UserId = "club-rep-user",
                FeeBase = 500m,
                FeeProcessing = 10.50m,
                FeeTotal = 510.50m,
                PaidTotal = 510.50m,
                OwedTotal = 0m,
                Job = new Jobs { JobName = "Trial Tournament", DisplayName = "Trial Tournament" }
            }
        };
        var settlement = new Settlement
        {
            SettlementId = Guid.NewGuid(),
            RegistrationAccountingId = 42,
            RegistrationAccounting = ra,
            AdnTransactionId = "ORIG-TEAM-100",
            Status = "Pending",
            SubmittedAt = DateTime.UtcNow.AddDays(-3)
        };

        StubSweepLogAndCreds();
        var tx = EcheckReturnSummary("RETURN-TEAM-200");
        StubBatchListWith(tx);
        StubEcheckReturnDetails("RETURN-TEAM-200", originalTxId: "ORIG-TEAM-100", reasonCode: 252, reasonDesc: "NSF");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("RETURN-TEAM-200", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.Is<IEnumerable<string>>(ids => ids.Contains("ORIG-TEAM-100")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);
        _teamRepo.Setup(r => r.GetTeamFromTeamId(team.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team);

        // The reversal is recorded through the ledger chokepoint, keyed to the team (TeamId set),
        // which re-derives the team's PaidTotal/OwedTotal (proven in RecordPaymentAndRecomputeTests).
        RegistrationAccounting? capturedReversal = null;
        string? capturedUserId = null;
        _accountingRepo.Setup(a => a.RecordPaymentAndRecomputeAsync(
                It.IsAny<RegistrationAccounting>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<RegistrationAccounting, string, CancellationToken>((ra, uid, _) =>
            {
                capturedReversal = ra;
                capturedUserId = uid;
            })
            .Returns(Task.CompletedTask);

        var result = await BuildSut().RunAsync("Test");

        result.EcheckReturnsProcessed.Should().Be(1);
        settlement.Status.Should().Be("Returned");

        // Reversal is recorded against the TEAM (TeamId set), not the rep registration directly.
        capturedReversal.Should().NotBeNull();
        capturedReversal!.TeamId.Should().Be(team.TeamId, "team eCheck return reverses on the team");
        capturedReversal.Payamt.Should().Be(-510.50m);
        capturedReversal.PaymentMethodId.Should().Be(FailedEcheckPaymentMethodId);
        capturedUserId.Should().Be(TsicConstants.SuperUserId);
        _feeAdj.Verify(f => f.ReverseTeamProcessingFeeForEcheckAsync(
            team, 510.50m, team.JobId, It.IsAny<string>()), Times.Once);
        // Player-side reversal must NOT run for team-side NSFs.
        _feeAdj.Verify(f => f.ReverseProcessingFeeForEcheckAsync(
            It.IsAny<Registrations>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        // Rep aggregate is rolled up after the team reversal.
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(
            team.ClubrepRegistrationid.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "eCheck NSF on player RA (no TeamId) → existing player path runs, no team sync")]
    public async Task EcheckReturn_PlayerSide_RegressionGuard()
    {
        // Use the existing BuildSettlement helper (no TeamId) to confirm we didn't break the
        // long-standing player path while adding the team branch.
        var settlement = BuildSettlement("ORIG-PLAYER", payAmount: 75m);
        var reg = settlement.RegistrationAccounting.Registration!;
        StubSweepLogAndCreds();
        var tx = EcheckReturnSummary("RETURN-PLAYER");
        StubBatchListWith(tx);
        StubEcheckReturnDetails("RETURN-PLAYER", originalTxId: "ORIG-PLAYER");
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("RETURN-PLAYER", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([settlement]);

        await BuildSut().RunAsync("Test");

        // Player-side reversal runs.
        _feeAdj.Verify(f => f.ReverseProcessingFeeForEcheckAsync(
            reg, 75m, reg.JobId, It.IsAny<string>()), Times.Once);
        // Team-side reversal does NOT run.
        _feeAdj.Verify(f => f.ReverseTeamProcessingFeeForEcheckAsync(
            It.IsAny<Teams>(), It.IsAny<decimal>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        // No rep aggregate sync for the player path.
        _regRepo.Verify(r => r.SynchronizeClubRepFinancialsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Orphan ADN charge detection (report-only) ─────────────────────

    /// <summary>
    /// A settled ONE-TIME charge (no subscription) carrying our invoice format — i.e. a
    /// candidate for the orphan ("charged at ADN, never booked locally") path.
    /// </summary>
    private static transactionSummaryType OrphanTxSummary(string txId, string invoiceNumber, decimal settleAmount = 75m)
    {
        return new transactionSummaryType
        {
            transId = txId,
            transactionStatus = "settledSuccessfully",
            settleAmount = settleAmount,
            invoiceNumber = invoiceNumber,
            submitTimeLocal = DateTime.Now
            // subscription left null → one-time charge, not ARB
        };
    }

    [Fact(DisplayName = "Orphan: settled charge with no accounting row → reported, NOT booked")]
    public async Task Orphan_SettledNotBooked_ReportedNotBooked()
    {
        var reg = BuildReg(paid: 0m, owed: 100m, jobName: "Fall Classic");
        StubSweepLogAndCreds();
        StubBatchListWith(OrphanTxSummary("ORPH-1", "1_2_3", settleAmount: 75m));
        // Settled tx also passes through the eCheck-settled path (step 3); no pending eCheck matches it.
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("ORPH-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // no local accounting row → orphan
        _regRepo.Setup(r => r.GetByInvoiceAisAsync(1, 2, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);

        var result = await BuildSut().RunAsync("Test");

        result.OrphansFound.Should().Be(1);
        result.Errored.Should().Be(0);
        // REPORT-ONLY: nothing is written to accounting and no balances move.
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
        _accountingRepo.Verify(a => a.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        reg.PaidTotal.Should().Be(0m, "report-only must not touch the registration balance");
        reg.OwedTotal.Should().Be(100m);
    }

    [Fact(DisplayName = "Orphan digest: resolved orphan row appears with the report-only warning")]
    public async Task Orphan_Digest_ShowsRowAndReportOnlyWarning()
    {
        var reg = BuildReg(jobName: "Fall Classic");
        StubSweepLogAndCreds();
        StubBatchListWith(OrphanTxSummary("ORPH-DIGEST", "1_2_3", settleAmount: 75m));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("ORPH-DIGEST", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByInvoiceAisAsync(1, 2, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);

        var sentEmails = new List<EmailMessageDto>();
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessageDto, bool, CancellationToken>((m, _, _) => sentEmails.Add(m))
            .ReturnsAsync(true);

        await BuildSut().RunAsync("Test");

        var digest = sentEmails.LastOrDefault(m => m.Subject!.StartsWith("AdnSweep"));
        digest.Should().NotBeNull("a digest email should always be sent");
        var html = digest!.HtmlBody!;
        html.Should().Contain("Orphan ADN Charges");
        html.Should().Contain("ORPH-DIGEST", "the orphan tx id should appear in the table");
        html.Should().Contain("1_2_3", "the invoice number should appear");
        html.Should().Contain("REPORT ONLY", "the digest must state that nothing was booked");
        html.Should().Contain("user-1", "the resolved registrant should appear");
    }

    [Fact(DisplayName = "Orphan: settled charge already booked → NOT flagged")]
    public async Task Orphan_AlreadyBooked_NotFlagged()
    {
        StubSweepLogAndCreds();
        StubBatchListWith(OrphanTxSummary("BOOKED-1", "1_2_3"));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("BOOKED-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // already has an accounting row

        var result = await BuildSut().RunAsync("Test");

        result.OrphansFound.Should().Be(0, "a booked charge is not an orphan");
        _regRepo.Verify(r => r.GetByInvoiceAisAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
    }

    [Fact(DisplayName = "Orphan: settled charge that maps to no registration → still reported (unresolved), not booked")]
    public async Task Orphan_Unresolvable_ReportedNotBooked()
    {
        StubSweepLogAndCreds();
        StubBatchListWith(OrphanTxSummary("ORPH-NOREG", "9_9_9"));
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("ORPH-NOREG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByInvoiceAisAsync(9, 9, 9, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Registrations?)null);

        var result = await BuildSut().RunAsync("Test");

        result.OrphansFound.Should().Be(1, "an unattributable settled-not-booked charge still needs human eyes");
        result.Errored.Should().Be(0);
        _accountingRepo.Verify(a => a.Add(It.IsAny<RegistrationAccounting>()), Times.Never);
    }

    [Fact(DisplayName = "ARB settled tx → handled by ARB path, NOT double-counted as an orphan")]
    public async Task Orphan_ArbTx_NotTreatedAsOrphan()
    {
        var reg = BuildReg();
        StubSweepLogAndCreds();
        StubBatchListWith(ArbTxSummary("settledSuccessfully", 50m, "777", "ARB-NOTORPH", "TSIC_1_2"));
        StubArbTxDetails("ARB-NOTORPH", "777", 50m);
        StubSubStatus("777");
        _settleRepo.Setup(r => r.GetByAdnTransactionIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _accountingRepo.Setup(a => a.AnyByAdnTransactionIdAsync("ARB-NOTORPH", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _regRepo.Setup(r => r.GetByAdnSubscriptionIdAsync("777", It.IsAny<CancellationToken>()))
            .ReturnsAsync(reg);

        var result = await BuildSut().RunAsync("Test");

        result.ArbImported.Should().Be(1, "ARB tx still imported by the ARB path");
        result.OrphansFound.Should().Be(0, "a subscription tx is never an orphan candidate");
        _regRepo.Verify(r => r.GetByInvoiceAisAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
