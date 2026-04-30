using AuthorizeNet.Api.Contracts.V1;
using TSIC.API.Services.Shared.Adn;
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
    private readonly IEmailLogRepository _emailLogRepo;
    private readonly IAdnApiService _adnApi;
    private readonly IEmailService _emailService;
    private readonly ILogger<ArbDefensiveService> _logger;

    public ArbDefensiveService(
        IArbSubscriptionRepository arbRepo,
        IEmailLogRepository emailLogRepo,
        IAdnApiService adnApi,
        IEmailService emailService,
        ILogger<ArbDefensiveService> logger)
    {
        _arbRepo = arbRepo;
        _emailLogRepo = emailLogRepo;
        _adnApi = adnApi;
        _emailService = emailService;
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
        var env = _adnApi.GetADNEnvironment(bProdOnly: true);
        var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId, bProdOnly: true);

        var response = _adnApi.ARBGetSubscriptionListRequest(
            env, creds.AdnLoginId!, creds.AdnTransactionKey!,
            ARBGetSubscriptionListSearchTypeEnum.cardExpiringThisMonth);

        if (response?.messages?.resultCode != messageTypeEnum.Ok
            || response.subscriptionDetails == null)
            return [];

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
        var regs = await _arbRepo.GetActiveSubscriptionsForJobAsync(jobId, ct);

        // Calculate fees owed and filter
        var flagged = new List<(ArbRegistrationProjection Reg, decimal Owes)>();
        foreach (var reg in regs)
        {
            if (reg.SubscriptionStartDate == null
                || reg.BillingOccurrences == null
                || reg.IntervalLength == null
                || reg.AmountPerOccurrence == null)
                continue;

            var occurrences = GetOccurrencesAsOfNow(
                reg.BillingOccurrences.Value,
                reg.SubscriptionStartDate.Value,
                reg.IntervalLength.Value);

            var owes = CalculateFeesOwed(reg, occurrences);
            if (owes <= 0) continue;

            // 48-hour grace: skip if most recent scheduled payment was < 48 hrs ago
            if (occurrences > 0)
            {
                var mostRecentPayment = reg.SubscriptionStartDate.Value
                    .AddMonths(reg.IntervalLength.Value * (occurrences - 1));
                if (Math.Abs((DateTime.UtcNow - mostRecentPayment).TotalHours) < GraceHours)
                    continue;
            }

            flagged.Add((reg, owes));
        }

        // Refresh subscription status from Authorize.Net for flagged registrations
        if (flagged.Count > 0)
        {
            var env = _adnApi.GetADNEnvironment(bProdOnly: true);
            var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId, bProdOnly: true);

            var result = new List<ArbFlaggedRegistrantDto>();

            foreach (var (reg, owes) in flagged)
            {
                if (string.IsNullOrEmpty(reg.SubscriptionId)) continue;

                try
                {
                    var statusResponse = _adnApi.GetSubscriptionStatus(
                        env, creds.AdnLoginId!, creds.AdnTransactionKey!,
                        reg.SubscriptionId);

                    var liveStatus = statusResponse.status.ToString();

                    // Update stale status in DB
                    if (reg.SubscriptionStatus != liveStatus)
                        await _arbRepo.UpdateSubscriptionStatusAsync(reg.RegistrationId, liveStatus, ct);

                    // Skip canceled subscriptions
                    if (statusResponse.status == ARBSubscriptionStatusEnum.canceled)
                        continue;

                    // If not active/terminated/suspended, they owe everything
                    var finalOwes = owes;
                    if (statusResponse.status != ARBSubscriptionStatusEnum.active
                        && statusResponse.status != ARBSubscriptionStatusEnum.terminated
                        && statusResponse.status != ARBSubscriptionStatusEnum.suspended)
                    {
                        finalOwes = reg.FeeTotal - reg.PaidTotal;
                    }

                    if (finalOwes <= 0) continue;

                    var dto = MapToDto(reg, ArbFlagType.BehindInPayment, finalOwes);
                    // Override status with live value
                    dto = dto with { SubscriptionStatus = liveStatus };

                    result.Add(dto);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to get subscription status for {SubscriptionId}",
                        reg.SubscriptionId);
                    // Still include with DB status
                    result.Add(MapToDto(reg, ArbFlagType.BehindInPayment, owes));
                }
            }

            return result.OrderBy(r => r.NextPaymentDate).ToList();
        }

        return [];
    }

    // ── SendDefensiveEmailsAsync ────────────────────────────────────────

    public async Task<ArbEmailResultDto> SendDefensiveEmailsAsync(
        ArbSendEmailsRequest request, CancellationToken ct = default)
    {
        var senderInfo = await _arbRepo.GetSenderInfoAsync(request.SenderUserId, ct);
        if (senderInfo == null)
            return new ArbEmailResultDto { EmailsSent = 0, EmailsFailed = 0 };

        // Load the flagged registrations to get contact info for token replacement
        var allFlagged = await GetFlaggedSubscriptionsAsync(
            request.JobId, request.FlagType, ct);

        var selectedIds = request.RegistrationIds.ToHashSet();
        var selected = allFlagged.Where(r => selectedIds.Contains(r.RegistrationId)).ToList();

        if (selected.Count == 0)
            return new ArbEmailResultDto { EmailsSent = 0, EmailsFailed = 0 };

        // Build one EmailMessageDto per registrant
        var messages = selected
            .Select(reg =>
            {
                var toAddresses = CollectValidEmails(reg.MomEmail, reg.DadEmail, reg.RegistrantEmail);
                if (toAddresses.Count == 0) return null;

                return new EmailMessageDto
                {
                    FromName = senderInfo.Value.DisplayName,
                    FromAddress = senderInfo.Value.Email,
                    ToAddresses = toAddresses,
                    Subject = request.EmailSubject,
                    HtmlBody = ReplaceArbTokens(request.EmailBody, reg)
                };
            })
            .Where(m => m != null)
            .Cast<EmailMessageDto>()
            .ToList();

        var batchResult = await _emailService.SendBatchAsync(messages, ct);

        // Log the batch
        var distinctSent = batchResult.AllAddresses.Except(batchResult.FailedAddresses).Distinct().ToList();
        await _emailLogRepo.LogAsync(new EmailLogs
        {
            JobId = request.JobId,
            Count = distinctSent.Count,
            Msg = request.EmailBody,
            SendFrom = senderInfo.Value.Email,
            SendTo = string.Join(";", batchResult.AllAddresses.Distinct()),
            Subject = request.EmailSubject,
            SenderUserId = request.SenderUserId,
            SendTs = DateTime.Now
        }, ct);

        // Confirmation email to sender
        var confirmBody = $@"Batch Email Successful
            <br /><strong>Type:</strong> ARB Defensive ({request.FlagType})
            <br /><strong>#Emails Attempted:</strong> {batchResult.AllAddresses.Distinct().Count()}
            <br /><strong>#Emails Sent:</strong> {distinctSent.Count}
            <br /><strong>Failed:</strong> {string.Join(";", batchResult.FailedAddresses.Distinct())}
            <hr />{request.EmailSubject}";

        await _emailService.SendAsync(new EmailMessageDto
        {
            FromName = "TEAMSPORTSINFO.COM",
            FromAddress = senderInfo.Value.Email,
            ToAddresses = [senderInfo.Value.Email],
            Subject = $"ARB Defensive Email Batch Complete — {distinctSent.Count} sent",
            HtmlBody = confirmBody
        }, cancellationToken: ct);

        // Director notification
        if (request.NotifyDirectors)
        {
            var directors = await _arbRepo.GetDirectorsForJobsAsync([request.JobId], ct);

            foreach (var director in directors)
            {
                if (string.IsNullOrEmpty(director.Email)) continue;

                var names = selected.Select(r => $"<li>{r.RegistrantName}</li>");
                var dirBody = $@"<h2>ARB Defensive Emails Sent ({request.FlagType})</h2>
                    <p>{selected.Count} registrant(s) were notified.</p>
                    <h3>Registrants:</h3><ul>{string.Join("", names)}</ul>
                    <p>No action required from you at this time.</p>";

                await _emailService.SendAsync(new EmailMessageDto
                {
                    FromName = senderInfo.Value.DisplayName,
                    FromAddress = senderInfo.Value.Email,
                    ToAddresses = [director.Email],
                    Subject = $"ARB {request.FlagType} Notifications Sent",
                    HtmlBody = dirBody
                }, cancellationToken: ct);
            }
        }

        return new ArbEmailResultDto
        {
            EmailsSent = distinctSent.Count,
            EmailsFailed = batchResult.FailedAddresses.Distinct().Count(),
            FailedAddresses = batchResult.FailedAddresses.Distinct().ToList()
        };
    }

    // ── GetSubscriptionInfoAsync ────────────────────────────────────────

    public async Task<ArbSubscriptionInfoDto?> GetSubscriptionInfoAsync(
        Guid registrationId, CancellationToken ct = default)
    {
        var detail = await _arbRepo.GetRegistrationArbDetailAsync(registrationId, ct);
        if (detail == null || string.IsNullOrEmpty(detail.SubscriptionId))
            return null;

        var balanceDue = 0m;
        if (detail.SubscriptionStartDate != null
            && detail.BillingOccurrences != null
            && detail.IntervalLength != null
            && detail.AmountPerOccurrence != null)
        {
            var occurrences = GetOccurrencesAsOfNow(
                detail.BillingOccurrences.Value,
                detail.SubscriptionStartDate.Value,
                detail.IntervalLength.Value);

            var sumArbFeesAsOfNow = detail.AmountPerOccurrence.Value * occurrences;
            var sumAllArbFees = detail.AmountPerOccurrence.Value * detail.BillingOccurrences.Value;
            var nonArbFees = detail.FeeTotal - sumAllArbFees;

            balanceDue = occurrences <= 1 ? 0 :
                Math.Max(0, (sumArbFeesAsOfNow + nonArbFees) - detail.PaidTotal);

            // 48-hour grace deduction
            if (occurrences > 0)
            {
                var mostRecent = detail.SubscriptionStartDate.Value
                    .AddMonths(detail.IntervalLength.Value * (occurrences - 1));
                if (Math.Abs((DateTime.UtcNow - mostRecent).TotalHours) < GraceHours)
                    balanceDue -= detail.AmountPerOccurrence.Value;
            }
            balanceDue = Math.Max(0, balanceDue);
        }

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

        var env = _adnApi.GetADNEnvironment(bProdOnly: true);
        var creds = await _adnApi.GetJobAdnCredentials_FromJobId(detail.JobId, bProdOnly: true);

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

        // 3. Charge balance if > 0
        var balanceCharged = false;
        decimal amountCharged = 0;
        string? transactionId = null;

        if (request.BalanceDue > 0)
        {
            var chargeExpiry = $"{request.ExpirationMonth}/{request.ExpirationYear}";
            var chargeResponse = _adnApi.ADN_Charge(new AdnChargeRequest
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
                Amount = request.BalanceDue,
                InvoiceNumber = detail.FirstInvoiceNumber ?? string.Empty,
                Description = "Autocharge of previously failed ARB transactions"
            });

            if (chargeResponse?.messages?.resultCode == messageTypeEnum.Ok
                && chargeResponse.transactionResponse?.messages != null)
            {
                balanceCharged = true;
                amountCharged = request.BalanceDue;
                transactionId = chargeResponse.transactionResponse.transId;

                await _arbRepo.RecordPaymentAsync(new RegistrationAccounting
                {
                    Active = true,
                    AdnCc4 = request.CardNumber[^4..],
                    AdnCcexpDate = chargeExpiry,
                    AdnInvoiceNo = detail.FirstInvoiceNumber,
                    AdnTransactionId = transactionId,
                    RegistrationId = request.RegistrationId,
                    Createdate = DateTime.Now,
                    Dueamt = request.BalanceDue,
                    Payamt = request.BalanceDue,
                    PaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D"),
                    Comment = "Autocharge of previously failed ARB transactions",
                    Paymeth = "Autocharge of previously failed ARB transactions",
                    LebUserId = userId,
                    Modified = DateTime.Now
                }, request.BalanceDue, userId, ct);

                message += $" Card charged {request.BalanceDue:C} for failed ARB payments.";
            }
            else
            {
                var chargeErr = chargeResponse?.transactionResponse?.errors?.FirstOrDefault()?.errorText
                    ?? chargeResponse?.messages?.message?.FirstOrDefault()?.text
                    ?? "Charge failed.";
                message += $" Balance charge failed: {chargeErr}";
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

    private static decimal CalculateFeesOwed(ArbRegistrationProjection reg, int occurrences)
    {
        if (occurrences <= 1) return 0;

        var sumArbFeesAsOfNow = (reg.AmountPerOccurrence ?? 0) * occurrences;
        var sumAllArbFees = (reg.AmountPerOccurrence ?? 0) * (reg.BillingOccurrences ?? 0);
        var nonArbFees = reg.FeeTotal - sumAllArbFees;

        var owed = sumArbFeesAsOfNow + nonArbFees - reg.PaidTotal;
        return owed > 0 ? owed : 0;
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
            JobPath = reg.JobPath
        };
    }

    private static string ReplaceArbTokens(string template, ArbFlaggedRegistrantDto reg)
    {
        return template
            .Replace("!PLAYER", $"<strong>{reg.RegistrantName}</strong>")
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

    private static List<string> CollectValidEmails(params string?[] emails)
    {
        var validator = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();
        return emails
            .Where(e => !string.IsNullOrWhiteSpace(e) && validator.IsValid(e))
            .Distinct()
            .ToList()!;
    }
}
