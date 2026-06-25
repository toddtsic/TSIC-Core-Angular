using TSIC.API.Services.Shared.Email;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Service for the Rescheduler tool (009-6).
/// Cross-division grid with move/swap, weather adjustment, and bulk email.
/// </summary>
public sealed class ReschedulerService : IReschedulerService
{
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly IEmailBatchService _emailBatch;
    private readonly IJobRepository _jobRepo;
    private readonly IUserRepository _userRepo;
    private readonly ILogger<ReschedulerService> _logger;

    public ReschedulerService(
        IScheduleRepository scheduleRepo,
        IFieldRepository fieldRepo,
        IEmailBatchService emailBatch,
        IJobRepository jobRepo,
        IUserRepository userRepo,
        ILogger<ReschedulerService> logger)
    {
        _scheduleRepo = scheduleRepo;
        _fieldRepo = fieldRepo;
        _emailBatch = emailBatch;
        _jobRepo = jobRepo;
        _userRepo = userRepo;
        _logger = logger;
    }

    // ── Filter Options ──

    public async Task<ScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _scheduleRepo.GetScheduleFilterOptionsAsync(jobId, ct);
    }

    // ── Grid ──

    public async Task<ScheduleGridResponse> GetReschedulerGridAsync(
        Guid jobId, ReschedulerGridRequest request, CancellationToken ct = default)
    {
        return await _scheduleRepo.GetReschedulerGridAsync(jobId, request, ct);
    }

    // ── Move/Swap ──

    public async Task MoveGameAsync(string userId, MoveGameRequest request, CancellationToken ct = default)
    {
        await SchedulingGameMutationHelper.MoveOrSwapGameAsync(
            request, userId, _scheduleRepo, _fieldRepo, _logger, ct);
    }

    // ── Weather Adjustment ──

    public async Task<AffectedGameCountResponse> GetAffectedGameCountAsync(
        Guid jobId, DateTime preFirstGame, List<Guid> fieldIds, CancellationToken ct = default)
    {
        var count = await _scheduleRepo.GetAffectedGameCountAsync(jobId, preFirstGame, fieldIds, ct);
        return new AffectedGameCountResponse { Count = count };
    }

    public async Task<AdjustWeatherResponse> AdjustForWeatherAsync(
        Guid jobId, AdjustWeatherRequest request, CancellationToken ct = default)
    {
        var code = await _scheduleRepo.ExecuteWeatherAdjustmentAsync(jobId, request, ct);

        var response = new AdjustWeatherResponse
        {
            Success = code == 1,
            ResultCode = code,
            Message = code switch
            {
                1 => "Schedule adjusted successfully.",
                2 => "Cannot apply — adjustment would create overlapping games on one or more fields.",
                3 => "The 'before' interval doesn't match the actual game spacing. Verify the current first game time and interval.",
                4 => "The 'after' interval is invalid. Please enter a positive number of minutes.",
                5 => "All affected games must be within the same calendar year.",
                6 => "No games found for the selected date/time range and fields.",
                7 => "No changes — the before and after values are identical.",
                8 => "Some games in the range are not aligned to the specified interval. Manual adjustment required for off-interval games.",
                _ => $"Unexpected result code: {code}"
            }
        };

        _logger.LogInformation("WeatherAdjust: code={Code} success={Success} message={Message}",
            code, response.Success, response.Message);

        return response;
    }

    // ── Email ──

    public async Task<EmailRecipientCountResponse> GetEmailRecipientCountAsync(
        Guid jobId, DateTime firstGame, DateTime lastGame, List<Guid> fieldIds, CancellationToken ct = default)
    {
        var recipients = await _scheduleRepo.GetEmailRecipientsAsync(jobId, firstGame, lastGame, fieldIds, ct);
        return new EmailRecipientCountResponse { EstimatedCount = recipients.Count };
    }

    public async Task<EmailBatchHandle> StartParticipantsEmailAsync(
        Guid jobId, string userId, EmailParticipantsRequest request, CancellationToken ct = default)
    {
        // Sender identity → From address (preserves the legacy "send as the admin" behavior).
        var sender = await _userRepo.GetByIdAsync(userId, ct);
        var senderEmail = sender?.Email;
        if (string.IsNullOrWhiteSpace(senderEmail))
            throw new InvalidOperationException("Cannot identify the sender's email address.");

        var displayName = await _jobRepo.GetJobNameAsync(jobId, ct) ?? "TEAMSPORTSINFO.COM";

        // Each recipient already carries the registration it belongs to + that reg's opt-out flag
        // (league addon contacts carry null regId). The engine partitions opt-out, strips invalid
        // addresses, appends the per-reg unsubscribe footer, retries, rate-limits, and audits —
        // identical mechanics to every other batch path.
        var recipients = await _scheduleRepo.GetEmailRecipientsAsync(
            jobId, request.FirstGame, request.LastGame, request.FieldIds, ct);

        var subject = request.EmailSubject;
        var body = request.EmailBody;

        var plan = new EmailBatchPlan<ScheduleEmailRecipient>
        {
            SeedAsync = (_, _) => Task.FromResult(new EmailBatchSeed<ScheduleEmailRecipient> { Items = recipients }),
            IsOptedOut = r => r.OptedOut,
            DescribeItem = r => r.Email,
            RenderAsync = (r, _, _) =>
            {
                var toAddresses = BatchEmailRecipientFilter.BuildSendableSet(new[] { r.Email });
                if (toAddresses.Count == 0) return Task.FromResult<EmailBatchRendered?>(null);

                // Reschedule notices are admin-authored raw HTML — no token substitution.
                return Task.FromResult<EmailBatchRendered?>(new EmailBatchRendered
                {
                    Message = new EmailMessageDto
                    {
                        FromName = displayName,
                        FromAddress = senderEmail,
                        Subject = subject,
                        HtmlBody = body,
                        ToAddresses = toAddresses
                    },
                    UnsubscribeRegId = r.RegistrationId // null for league addon contacts → no footer
                });
            },
            // Engine writes the single EmailLogs audit row. No completion side-effects for this path.
            Audit = new EmailBatchAudit
            {
                JobId = jobId,
                SenderUserId = userId,
                Subject = subject,
                BodyTemplate = body,
                SendFrom = senderEmail
            }
        };

        var handle = await _emailBatch.StartAsync(plan, new EmailBatchOptions(), ct);

        _logger.LogInformation(
            "Rescheduler EmailParticipants: jobId={JobId} batchJobId={BatchJobId} recipients={Recipients}",
            jobId, handle.JobId, handle.TotalRecipients);

        return handle;
    }
}
