using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.Email;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Arb;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

public class ArbDefensiveService : IArbDefensiveService
{
    private const int GraceHours = 48;

    private readonly IArbSubscriptionRepository _arbRepo;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly IAdnApiService _adnApi;
    private readonly IEmailBatchService _emailBatch;
    private readonly IEmailTestSendService _testSend;
    private readonly ILogger<ArbDefensiveService> _logger;

    public ArbDefensiveService(
        IArbSubscriptionRepository arbRepo,
        IRegistrationAccountingRepository accountingRepo,
        IAdnApiService adnApi,
        IEmailBatchService emailBatch,
        IEmailTestSendService testSend,
        ILogger<ArbDefensiveService> logger)
    {
        _arbRepo = arbRepo;
        _accountingRepo = accountingRepo;
        _adnApi = adnApi;
        _emailBatch = emailBatch;
        _testSend = testSend;
        _logger = logger;
    }

    // ── GetFlaggedSubscriptionsAsync ────────────────────────────────────

    public async Task<List<ArbFlaggedRegistrantDto>> GetFlaggedSubscriptionsAsync(
        Guid jobId, ArbFlagType flagType, CancellationToken ct = default)
    {
        return flagType switch
        {
            ArbFlagType.ExpiringCard => await GetExpiringCardFlagsAsync(jobId, ct),
            ArbFlagType.BehindInPayment => await GetBehindInPaymentFlagsAsync(jobId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(flagType))
        };
    }

    private async Task<List<ArbFlaggedRegistrantDto>> GetExpiringCardFlagsAsync(
        Guid jobId, CancellationToken ct)
    {
        // FORCED PRODUCTION — same read-only exception as RefreshSubscriptionStatusesAsync:
        // real subscriptions live only on the production account, so the sandbox list is
        // always empty off-Production. ARBGetSubscriptionList cannot charge/modify/cancel.
        var env = AuthorizeNet.Environment.PRODUCTION;
        var creds = await _adnApi.GetJobAdnProductionCredentials_FromJobId(jobId);

        var response = _adnApi.ARBGetSubscriptionListRequest(
            env, creds.AdnLoginId!, creds.AdnTransactionKey!,
            ARBGetSubscriptionListSearchTypeEnum.cardExpiringThisMonth);

        // An ADN error is NOT a month with no expiring cards. Collapsing both into an empty list is
        // what let a broken call read as a clean all-clear; the automated 2nd/15th send would then
        // mail nobody and report success. Ok-with-no-details IS genuinely empty and returns [].
        if (response?.messages?.resultCode != messageTypeEnum.Ok)
        {
            var detail = response?.messages?.message?.FirstOrDefault();
            throw new InvalidOperationException(
                $"ARBGetSubscriptionList (cardExpiringThisMonth) failed for job {jobId}: "
                + $"{detail?.code} {detail?.text}".Trim());
        }

        if (response.subscriptionDetails == null) return [];

        var invoices = response.subscriptionDetails
            .Where(s => !string.IsNullOrEmpty(s.invoice))
            .Select(s => s.invoice)
            .ToList();

        if (invoices.Count == 0) return [];

        var regs = await _arbRepo.GetRegistrationsByInvoiceNumbersAsync(invoices, jobId, ct);

        return regs.Select(r => MapToDto(r, ArbFlagType.ExpiringCard, currentlyOwes: 0)).ToList();
    }

    private async Task<List<ArbFlaggedRegistrantDto>> GetBehindInPaymentFlagsAsync(
        Guid jobId, CancellationToken ct)
    {
        // Pure DB read — stored status is maintained by RefreshSubscriptionStatusesAsync
        // (the director-clicked chokepoint) and the month-end sweep's charge-time check.
        var regs = await _arbRepo.GetActiveSubscriptionsForJobAsync(jobId, ct);

        var result = new List<ArbFlaggedRegistrantDto>();
        foreach (var reg in regs)
        {
            if (reg.SubscriptionStartDate == null
                || reg.BillingOccurrences == null
                || reg.IntervalLength == null
                || reg.AmountPerOccurrence == null)
                continue;

            // Skip canceled subscriptions
            if (string.Equals(reg.SubscriptionStatus, "canceled", StringComparison.OrdinalIgnoreCase))
                continue;

            // ONE balance rule, shared with the family-facing CC-update page. The dead-subscription
            // override and the 48-hour grace both live inside CalculateOwedNow now; they used to be
            // open-coded here and separately re-implemented there, and the two copies drifted.
            var finalOwes = CalculateOwedNow(ArbBalanceInputs.From(reg));
            if (finalOwes <= 0) continue;

            result.Add(MapToDto(reg, ArbFlagType.BehindInPayment, finalOwes));
        }

        return result.OrderBy(r => r.NextPaymentDate).ToList();
    }

    // ── RefreshSubscriptionStatusesAsync ────────────────────────────────

    public async Task<ArbRefreshStatusesResultDto> RefreshSubscriptionStatusesAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var targets = await _arbRepo.GetStatusRefreshTargetsForJobAsync(jobId, ct);
        if (targets.Count == 0)
            return new ArbRefreshStatusesResultDto { Checked = 0, Updated = 0, Failed = 0 };

        // FORCED PRODUCTION — deliberate exception to the env-bound rule, for this action
        // precisely: the stored subscription IDs exist only on the production ADN account,
        // so a sandbox lookup always fails and the refresh would be useless off-Production.
        // Safe because ARBGetSubscriptionStatus is READ-ONLY at ADN — it cannot charge,
        // modify, or cancel. Do NOT copy this pattern into any charging/mutating path.
        var env = AuthorizeNet.Environment.PRODUCTION;
        var creds = await _adnApi.GetJobAdnProductionCredentials_FromJobId(jobId);

        var updates = new Dictionary<Guid, string>();
        var failed = 0;

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var response = _adnApi.GetSubscriptionStatus(
                    env, creds.AdnLoginId!, creds.AdnTransactionKey!,
                    target.SubscriptionId);

                if (response?.messages?.resultCode != messageTypeEnum.Ok)
                {
                    failed++;
                    _logger.LogWarning(
                        "ARB status refresh: non-Ok response for {SubscriptionId} ({Code})",
                        target.SubscriptionId, response?.messages?.resultCode);
                    continue;
                }

                var liveStatus = response.status.ToString();
                if (!string.Equals(target.SubscriptionStatus, liveStatus, StringComparison.Ordinal))
                    updates[target.RegistrationId] = liveStatus;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "ARB status refresh failed for {SubscriptionId}", target.SubscriptionId);
            }
        }

        await _arbRepo.UpdateSubscriptionStatusesAsync(updates, ct);

        _logger.LogInformation(
            "ARB status refresh for job {JobId}: {Checked} checked, {Updated} updated, {Failed} failed",
            jobId, targets.Count, updates.Count, failed);

        return new ArbRefreshStatusesResultDto
        {
            Checked = targets.Count,
            Updated = updates.Count,
            Failed = failed
        };
    }

    // ── SendDefensiveEmailsAsync ────────────────────────────────────────

    public async Task<EmailBatchHandle> StartDefensiveEmailsAsync(
        ArbSendEmailsRequest request, CancellationToken ct = default)
    {
        var senderInfo = await _arbRepo.GetSenderInfoAsync(request.SenderUserId, ct);

        // Load flagged registrations + narrow to the selected subset. The ADN calls happen HERE,
        // before fan-out — same up-front cost the synchronous version paid, then sends go background.
        var allFlagged = await GetFlaggedSubscriptionsAsync(request.JobId, request.FlagType, ct);
        var selectedIds = request.RegistrationIds.ToHashSet();
        var selected = allFlagged.Where(r => selectedIds.Contains(r.RegistrationId)).ToList();

        // Capture ONLY plain data for the engine closures — this request scope (and its DbContext /
        // _arbRepo) is disposed the instant we return the handle. The completion hook resolves every
        // service it needs from the fresh scope the engine hands it.
        var senderName = senderInfo?.DisplayName ?? "TEAMSPORTSINFO.COM";
        var senderEmail = senderInfo?.Email;
        var subject = request.EmailSubject;
        var bodyTemplate = request.EmailBody;
        var flagType = request.FlagType;
        var jobId = request.JobId;
        var notifyDirectors = request.NotifyDirectors;
        // Names of those actually emailable (post opt-out) for the director-notify list.
        var notifiedNames = selected.Where(r => !r.BemailOptOut).Select(r => r.RegistrantName).ToList();

        var plan = new EmailBatchPlan<ArbFlaggedRegistrantDto>
        {
            SeedAsync = (_, _) => Task.FromResult(new EmailBatchSeed<ArbFlaggedRegistrantDto> { Items = selected }),
            IsOptedOut = r => r.BemailOptOut,
            DescribeItem = r => $"(no email for {r.RegistrantName})",
            RenderAsync = (reg, _, _) =>
            {
                // Shared recipient rule (drops blanks, the not@given.com sentinel, dupes) — same as
                // every other batch path now, replacing ARB's bespoke validator.
                var toAddresses = BatchEmailRecipientFilter.BuildSendableSet(
                    new[] { reg.MomEmail, reg.DadEmail, reg.RegistrantEmail });
                if (toAddresses.Count == 0) return Task.FromResult<EmailBatchRendered?>(null);

                return Task.FromResult<EmailBatchRendered?>(new EmailBatchRendered
                {
                    Message = new EmailMessageDto
                    {
                        FromName = senderName,
                        ReplyToName = senderName,
                        ReplyToAddress = senderEmail,
                        ToAddresses = toAddresses,
                        Subject = subject,
                        HtmlBody = ReplaceArbTokens(bodyTemplate, reg)
                    },
                    UnsubscribeRegId = reg.RegistrationId // engine appends the unsubscribe footer
                });
            },
            Audit = new EmailBatchAudit
            {
                JobId = jobId,
                SenderUserId = request.SenderUserId,
                Subject = subject,
                BodyTemplate = bodyTemplate,
                SendFrom = senderEmail
            },
            // Path-specific completion side-effects (sender summary + optional director-notify), now
            // fired by the engine when the background batch drains. Resolves services from the scope.
            OnCompleteAsync = async (status, sp, token) =>
            {
                var email = sp.GetRequiredService<IEmailService>();

                // Sender completion summary (automatic for ARB — unlike Search Reg's opt-in summary).
                if (!string.IsNullOrWhiteSpace(senderEmail))
                {
                    var confirmBody = $@"Batch Email Complete
                        <br /><strong>Type:</strong> ARB Defensive ({flagType})
                        <br /><strong>#Sent:</strong> {status.Sent}
                        <br /><strong>#Failed:</strong> {status.Failed}
                        <br /><strong>#Opted out:</strong> {status.OptedOut}"
                        + (status.FailedAddresses.Count > 0
                            ? $"<br /><strong>Failed:</strong> {string.Join(";", status.FailedAddresses)}"
                            : "")
                        + $"<hr />{subject}";

                    await email.SendAsync(new EmailMessageDto
                    {
                        FromName = "TEAMSPORTSINFO.COM",
                        ToAddresses = new List<string> { senderEmail },
                        Subject = $"ARB Defensive Email Batch Complete — {status.Sent} sent",
                        HtmlBody = confirmBody
                    }, cancellationToken: token);
                }

                // Director notification.
                if (notifyDirectors)
                {
                    var arbRepo = sp.GetRequiredService<IArbSubscriptionRepository>();
                    var directors = await arbRepo.GetDirectorsForJobsAsync(new List<Guid> { jobId }, token);
                    foreach (var director in directors)
                    {
                        if (string.IsNullOrEmpty(director.Email)) continue;

                        var names = notifiedNames.Select(n => $"<li>{System.Net.WebUtility.HtmlEncode(n)}</li>");
                        var dirBody = $@"<h2>ARB Defensive Emails Sent ({flagType})</h2>
                            <p>{status.Sent} registrant(s) were notified.</p>
                            <h3>Registrants:</h3><ul>{string.Join("", names)}</ul>
                            <p>No action required from you at this time.</p>";

                        await email.SendAsync(new EmailMessageDto
                        {
                            FromName = senderName,
                            ReplyToName = senderName,
                            ReplyToAddress = senderEmail,
                            ToAddresses = new List<string> { director.Email },
                            Subject = $"ARB {flagType} Notifications Sent",
                            HtmlBody = dirBody
                        }, cancellationToken: token);
                    }
                }
            }
        };

        return await _emailBatch.StartAsync(plan, new EmailBatchOptions(), ct);
    }

    // ── SendTestEmailAsync ──────────────────────────────────────────────

    public async Task<EmailTestSendResponse> SendTestEmailAsync(
        ArbTestSendRequest request, CancellationToken ct = default)
    {
        // Render against the same flagged snapshot the real send would use, so token values
        // (owed-now, status, progress) match what the registrant would actually receive.
        var flagged = await GetFlaggedSubscriptionsAsync(request.JobId, request.FlagType, ct);
        var reg = flagged.FirstOrDefault(r => r.RegistrationId == request.RegistrationId)
                  ?? flagged.FirstOrDefault();
        if (reg == null)
        {
            return new EmailTestSendResponse
            {
                Sent = false,
                RenderedFor = string.Empty,
                Recipient = string.Empty,
                Message = "No flagged registrant available to render the test email."
            };
        }

        return await _testSend.SendRenderedAsync(
            request.EmailSubject,
            ReplaceArbTokens(request.EmailBody, reg),
            reg.RegistrantName,
            request.TestRecipient,
            ct);
    }

    // ── GetSubscriptionInfoAsync ────────────────────────────────────────

    public async Task<ArbSubscriptionInfoDto?> GetSubscriptionInfoAsync(
        Guid registrationId, CancellationToken ct = default)
    {
        var detail = await _arbRepo.GetRegistrationArbDetailAsync(registrationId, ct);
        if (detail == null || string.IsNullOrEmpty(detail.SubscriptionId))
            return null;

        var balanceDue = CalculateOwedNow(ArbBalanceInputs.From(detail));

        return new ArbSubscriptionInfoDto
        {
            SubscriptionId = detail.SubscriptionId,
            SubscriptionStatus = detail.SubscriptionStatus ?? "unknown",
            ChargePerOccurrence = detail.AmountPerOccurrence ?? 0,
            BalanceDue = balanceDue,
            RegistrantName = detail.RegistrantName,
            JobName = detail.JobName,
            StartDate = detail.SubscriptionStartDate ?? DateTime.MinValue,
            TotalOccurrences = detail.BillingOccurrences ?? 0,
            IntervalMonths = detail.IntervalLength ?? 0
        };
    }

    // ── UpdateSubscriptionCreditCardAsync ────────────────────────────────

    public async Task<ArbUpdateCcResultDto> UpdateSubscriptionCreditCardAsync(
        ArbUpdateCcRequest request, string userId, CancellationToken ct = default)
    {
        var detail = await _arbRepo.GetRegistrationArbDetailAsync(request.RegistrationId, ct);
        if (detail == null || detail.SubscriptionId != request.SubscriptionId)
            return new ArbUpdateCcResultDto
            {
                SubscriptionUpdated = false,
                BalanceCharged = false,
                Message = "Subscription not found or ID mismatch."
            };

        // Env-bound: this validates the card, updates the subscription, and charges the balance —
        // all against the create-time account. Off-Production that is sandbox, so this destructive
        // flow can never touch a real customer's card from a preview host.
        var env = _adnApi.GetADNEnvironment();
        var creds = await _adnApi.GetJobAdnCredentials_FromJobId(detail.JobId);

        var expiry = $"{request.ExpirationMonth}{request.ExpirationYear[^2..]}";

        // 1. Validate card via penny-auth + void
        var verifyResult = _adnApi.ADN_VerifyCardWithPennyAuth(new AdnAuthorizeRequest
        {
            Env = env,
            LoginId = creds.AdnLoginId!,
            TransactionKey = creds.AdnTransactionKey!,
            CardNumber = request.CardNumber,
            CardCode = request.CardCode,
            Expiry = expiry,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Address = request.Address,
            Zip = request.Zip,
            Amount = 0.01m
        });

        if (!verifyResult.Success)
        {
            return new ArbUpdateCcResultDto
            {
                SubscriptionUpdated = false,
                BalanceCharged = false,
                Message = $"Card validation failed: {verifyResult.ErrorMessage}"
            };
        }

        // 2. Update subscription
        var updateResponse = _adnApi.ADN_UpdateSubscription(new AdnArbUpdateRequest
        {
            Env = env,
            LoginId = creds.AdnLoginId!,
            TransactionKey = creds.AdnTransactionKey!,
            SubscriptionId = request.SubscriptionId,
            ChargePerOccurrence = detail.AmountPerOccurrence ?? 0,
            CardNumber = request.CardNumber,
            ExpirationDate = expiry,
            CardCode = request.CardCode,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Address = request.Address,
            Zip = request.Zip,
            Email = request.Email
        });

        var subscriptionUpdated = updateResponse?.messages?.resultCode == messageTypeEnum.Ok;
        var message = subscriptionUpdated
            ? "Subscription credit card updated successfully."
            : $"Subscription update failed: {updateResponse?.messages?.message?.FirstOrDefault()?.text}";

        var balanceCharged = false;
        decimal amountCharged = 0;
        string? transactionId = null;

        // 3. Charge the balance the SERVER computes - never the one the browser sent.
        //
        // ArbUpdateCcRequest no longer carries an amount. It used to, and this method charged that
        // number verbatim: a self-service, family-reachable endpoint took a dollar figure from the
        // client and ran it through the card. That was masked while the page always rendered $0.00;
        // now that it renders a real arrears figure the amount has to be re-derived here, from the
        // same rule the page displayed, against the registration the server just loaded.
        var balanceDue = CalculateOwedNow(ArbBalanceInputs.From(detail));

        if (balanceDue > 0)
        {
            var chargeExpiry = $"{request.ExpirationMonth}/{request.ExpirationYear}";
            var chargeResult = _adnApi.ADN_Charge_Result(new AdnChargeRequest
            {
                Env = env,
                LoginId = creds.AdnLoginId!,
                TransactionKey = creds.AdnTransactionKey!,
                CardNumber = request.CardNumber,
                CardCode = request.CardCode,
                Expiry = chargeExpiry,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Address = request.Address,
                Zip = request.Zip,
                Email = request.Email,
                Phone = string.Empty,
                Amount = balanceDue,
                InvoiceNumber = detail.FirstInvoiceNumber ?? string.Empty,
                Description = "Autocharge of previously failed ARB transactions"
            });

            if (chargeResult.Success)
            {
                balanceCharged = true;
                amountCharged = balanceDue;
                transactionId = chargeResult.TransactionId;

                await _accountingRepo.RecordPaymentAndRecomputeAsync(new RegistrationAccounting
                {
                    Active = true,
                    AdnCc4 = request.CardNumber[^4..],
                    AdnCcexpDate = chargeExpiry,
                    AdnInvoiceNo = detail.FirstInvoiceNumber,
                    AdnTransactionId = transactionId,
                    RegistrationId = request.RegistrationId,
                    Createdate = DateTime.Now,
                    Dueamt = balanceDue,
                    Payamt = balanceDue,
                    PaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D"),
                    Comment = "Autocharge of previously failed ARB transactions",
                    Paymeth = "Autocharge of previously failed ARB transactions",
                    LebUserId = userId,
                    Modified = DateTime.Now
                }, userId, ct);

                message += $" Card charged {balanceDue:C} for failed ARB payments.";
            }
            else
            {
                message += $" Balance charge failed: {chargeResult.MessageForUser}";
            }
        }

        return new ArbUpdateCcResultDto
        {
            SubscriptionUpdated = subscriptionUpdated,
            BalanceCharged = balanceCharged,
            AmountCharged = amountCharged,
            TransactionId = transactionId,
            Message = message
        };
    }

    // ── ARB Schedule Math (ported from legacy AdnTSICService) ───────────

    private static int GetOccurrencesAsOfNow(int totalOccurrences, DateTime startDate, int intervalMonths)
    {
        var count = 0;
        for (var i = 0; i < totalOccurrences; i++)
        {
            if (startDate.AddMonths(i * intervalMonths).Date <= DateTime.Now.Date)
                count++;
            else
                break;
        }
        return count;
    }

    /// <summary>
    /// Inputs the ARB balance rule needs, lifted off either projection so the rule itself has ONE
    /// body. The director-facing flag list reads <see cref="ArbRegistrationProjection"/>; the
    /// family-facing CC-update page reads <see cref="ArbRegistrationDetail"/>. Before this record
    /// each surface carried its own transcription of the arithmetic, and they drifted.
    /// </summary>
    private readonly record struct ArbBalanceInputs(
        string? SubscriptionStatus,
        DateTime? StartDate,
        int? IntervalMonths,
        int? BillingOccurrences,
        decimal? AmountPerOccurrence,
        decimal FeeTotal,
        decimal PaidTotal,
        DateTime? LastFailedDraftDate,
        DateTime? LastArbDraftDate)
    {
        public static ArbBalanceInputs From(ArbRegistrationProjection r) => new(
            r.SubscriptionStatus, r.SubscriptionStartDate, r.IntervalLength, r.BillingOccurrences,
            r.AmountPerOccurrence, r.FeeTotal, r.PaidTotal, r.LastFailedDraftDate, r.LastArbDraftDate);

        public static ArbBalanceInputs From(ArbRegistrationDetail d) => new(
            d.SubscriptionStatus, d.SubscriptionStartDate, d.IntervalLength, d.BillingOccurrences,
            d.AmountPerOccurrence, d.FeeTotal, d.PaidTotal, d.LastFailedDraftDate, d.LastArbDraftDate);
    }

    /// <summary>
    /// What this subscriber owes RIGHT NOW. Single source of truth for every surface that quotes the
    /// figure to a human: the director-facing Behind-In-Payment list, and the family-facing
    /// self-service CC-update page (which also charges it).
    /// </summary>
    /// <remarks>
    /// Rules, in the order they apply:
    ///
    /// 1. A DEAD subscription owes everything left, not merely the installments due to date. Todd,
    ///    2026-08-26: updating the card on a dead plan pays the entire remaining balance. "Dead" is
    ///    the predicate this file already used for the flag list - a known status that is not
    ///    active / terminated / suspended (in practice: expired, canceled). A BLANK status is not
    ///    evidence of death and keeps schedule math rather than inflating the ask.
    ///
    /// 2. Otherwise schedule math: installments due to date, plus any non-ARB fees, less paid.
    ///
    /// 3. The 48-hour grace, and the matching first-installment suppression, apply ONLY while the
    ///    outcome is unknown. Both exist for one reason: an installment whose date has arrived may
    ///    still be settling at ADN and PaidTotal will not show it yet, so quoting it as arrears would
    ///    double-bill. That is a proxy for "we do not know yet". A booked failed draft for that same
    ///    installment IS knowing, and overrules the proxy.
    ///
    ///    Without this override the two windows overlapped almost exactly: ADN drafts on the due
    ///    date, the card declines, the sweep emails the family the arrears figure hours later - and
    ///    the page that email sends them to deducted the very installment that had just failed,
    ///    quoting $0.00 and charging nothing until the grace aged out about a day and a half later.
    ///    Reg F802868B / sub 73796805, 2026-08-26: sweep said $875.00, page said $0.00, same morning.
    /// </remarks>
    private static decimal CalculateOwedNow(ArbBalanceInputs input)
    {
        // Dead plan: everything still outstanding, regardless of where the schedule sits.
        if (IsDeadSubscription(input.SubscriptionStatus))
            return Math.Max(0, input.FeeTotal - input.PaidTotal);

        if (input.StartDate == null
            || input.IntervalMonths == null
            || input.BillingOccurrences == null
            || input.AmountPerOccurrence == null)
            return 0;

        var amount = input.AmountPerOccurrence.Value;
        var occurrences = GetOccurrencesAsOfNow(
            input.BillingOccurrences.Value, input.StartDate.Value, input.IntervalMonths.Value);

        if (occurrences <= 0) return 0;

        // The installment most recently scheduled - the one the grace covers, and the one a booked
        // failure must belong to before it counts as knowledge about THIS installment. An older
        // failed draft that has since been made good must not hold the grace open forever.
        var mostRecent = input.StartDate.Value
            .AddMonths(input.IntervalMonths.Value * (occurrences - 1));
        var knownFailed = input.LastFailedDraftDate != null
            && input.LastFailedDraftDate.Value.Date >= mostRecent.Date;

        // Weaker signal, same principle: ANY booked draft for this installment - settled or not -
        // means its outcome is known and already reflected in PaidTotal, so the grace has nothing
        // left to protect. Without this the grace still over-deducted in the mirror-image case: a
        // family carrying an older arrears whose NEXT installment then settled normally read $0.00
        // for 48 hours after every successful draft, because the grace subtracted an installment
        // the ledger had already credited.
        var knownOutcome = input.LastArbDraftDate != null
            && input.LastArbDraftDate.Value.Date >= mostRecent.Date;

        // First installment still inside its settle window: nothing is behind yet - unless it failed.
        if (occurrences <= 1 && !knownFailed) return 0;

        var sumArbFeesAsOfNow = amount * occurrences;
        var sumAllArbFees = amount * input.BillingOccurrences.Value;
        var nonArbFees = input.FeeTotal - sumAllArbFees;

        var owed = sumArbFeesAsOfNow + nonArbFees - input.PaidTotal;

        var withinGrace = Math.Abs((DateTime.Now - mostRecent).TotalHours) < GraceHours;
        if (withinGrace && !knownOutcome)
            owed -= amount;

        return Math.Max(0, owed);
    }

    /// <summary>
    /// A subscription ADN reports as neither live nor merely paused. Blank is unknown, not dead.
    /// Its own predicate so the flag list and the CC-update page cannot classify a status differently.
    /// </summary>
    private static bool IsDeadSubscription(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return !string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "terminated", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "suspended", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? CalculateNextPaymentDate(DateTime startDate, int intervalMonths, int totalOccurrences)
    {
        var occurrences = GetOccurrencesAsOfNow(totalOccurrences, startDate, intervalMonths);
        if (occurrences >= totalOccurrences) return null;
        return startDate.AddMonths(occurrences * intervalMonths);
    }

    // ── Mapping & Helpers ───────────────────────────────────────────────

    private static ArbFlaggedRegistrantDto MapToDto(
        ArbRegistrationProjection reg, ArbFlagType flagType, decimal currentlyOwes)
    {
        DateTime? nextPayment = null;
        string? progress = null;

        if (reg.SubscriptionStartDate != null
            && reg.IntervalLength != null
            && reg.BillingOccurrences != null)
        {
            nextPayment = CalculateNextPaymentDate(
                reg.SubscriptionStartDate.Value,
                reg.IntervalLength.Value,
                reg.BillingOccurrences.Value);

            var occ = GetOccurrencesAsOfNow(
                reg.BillingOccurrences.Value,
                reg.SubscriptionStartDate.Value,
                reg.IntervalLength.Value);
            progress = $"{occ} of {reg.BillingOccurrences.Value}";
        }

        return new ArbFlaggedRegistrantDto
        {
            RegistrationId = reg.RegistrationId,
            SubscriptionId = reg.SubscriptionId,
            SubscriptionStatus = reg.SubscriptionStatus ?? "unknown",
            FlagType = flagType,
            RegistrantName = reg.RegistrantName,
            FirstName = reg.FirstName,
            LastName = reg.LastName,
            Assignment = reg.Assignment,
            FamilyUsername = reg.FamilyUsername,
            Role = reg.Role,
            RegistrantEmail = reg.RegistrantEmail,
            MomName = reg.MomName,
            MomEmail = reg.MomEmail,
            MomPhone = reg.MomPhone,
            DadName = reg.DadName,
            DadEmail = reg.DadEmail,
            DadPhone = reg.DadPhone,
            FeeTotal = reg.FeeTotal,
            PaidTotal = reg.PaidTotal,
            CurrentlyOwes = currentlyOwes,
            OwedTotal = reg.OwedTotal,
            NextPaymentDate = nextPayment,
            PaymentProgress = progress,
            JobName = reg.JobName,
            JobPath = reg.JobPath,
            BemailOptOut = reg.BemailOptOut
        };
    }

    /// <summary>
    /// Natural-order name for PROSE. <see cref="ArbFlaggedRegistrantDto.RegistrantName"/> is the grid's
    /// sort-order format ("Regan, Peyton"), which read backwards inside an email sentence. First/Last
    /// come off the projection so this never has to parse a comma out of a name. Falls back to the
    /// sort-order string when either part is missing.
    /// </summary>
    private static string ProseName(ArbFlaggedRegistrantDto reg)
    {
        var natural = $"{reg.FirstName} {reg.LastName}".Trim();
        return string.IsNullOrWhiteSpace(natural) ? reg.RegistrantName : natural;
    }

    private static string ReplaceArbTokens(string template, ArbFlaggedRegistrantDto reg)
    {
        var person = $"<strong>{ProseName(reg)}</strong>";
        return template
            .Replace("!PLAYER", person)
            // !PERSON is the canonical engine's name token; ARB directors reach for it out of habit
            // from the Search/Registrations composer. Alias it rather than let it pass through raw.
            .Replace("!PERSON", person)
            .Replace("!SUBSCRIPTIONID", $"<strong>{reg.SubscriptionId}</strong>")
            .Replace("!SUBSCRIPTIONSTATUS", $"<strong>{reg.SubscriptionStatus}</strong>")
            .Replace("!FEETOTAL", $"<strong>{reg.FeeTotal:C}</strong>")
            .Replace("!PAIDTOTAL", $"<strong>{reg.PaidTotal:C}</strong>")
            .Replace("!OWEDNOW", $"<strong>{reg.CurrentlyOwes:C}</strong>")
            .Replace("!OWEDTOTAL", $"<strong>{reg.OwedTotal:C}</strong>")
            .Replace("!FAMILYUSERNAME", $"<strong>{reg.FamilyUsername}</strong>")
            .Replace("!JOBLINK", $"<a href='https://www.teamsportsinfo.com/{reg.JobPath}' target='_blank'>{System.Net.WebUtility.HtmlEncode(reg.JobName ?? string.Empty)}</a>")
            .Replace("!JOBNAME", $"<strong>{reg.JobName}</strong>");
    }
}
