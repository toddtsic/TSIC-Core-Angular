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
    private readonly IEmailService _emailService;
    private readonly IJobRepository _jobRepo;
    private readonly IUserRepository _userRepo;
    private readonly ILogger<ReschedulerService> _logger;

    public ReschedulerService(
        IScheduleRepository scheduleRepo,
        IFieldRepository fieldRepo,
        IEmailService emailService,
        IJobRepository jobRepo,
        IUserRepository userRepo,
        ILogger<ReschedulerService> logger)
    {
        _scheduleRepo = scheduleRepo;
        _fieldRepo = fieldRepo;
        _emailService = emailService;
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

    public async Task<EmailParticipantsResponse> EmailParticipantsAsync(
        Guid jobId, string userId, EmailParticipantsRequest request, CancellationToken ct = default)
    {
        // 1. Get sender info
        var sender = await _userRepo.GetByIdAsync(userId, ct);
        var senderEmail = sender?.Email;

        if (string.IsNullOrWhiteSpace(senderEmail))
            throw new InvalidOperationException("Cannot identify the sender's email address.");

        // 2. Get job display name for From header
        var displayName = await _jobRepo.GetJobNameAsync(jobId, ct) ?? "TEAMSPORTSINFO.COM";

        // 3. Collect recipients
        var recipients = await _scheduleRepo.GetEmailRecipientsAsync(
            jobId, request.FirstGame, request.LastGame, request.FieldIds, ct);

        if (recipients.Count == 0)
        {
            return new EmailParticipantsResponse
            {
                RecipientCount = 0,
                FailedCount = 0,
                SentAt = DateTime.UtcNow
            };
        }

        // 4. Build and send emails via batch
        var messages = recipients.Select(recipientEmail => new EmailMessageDto
        {
            FromName = displayName,
            FromAddress = senderEmail,
            ToAddresses = new List<string> { recipientEmail },
            Subject = request.EmailSubject,
            HtmlBody = request.EmailBody
        });

        var batchResult = await _emailService.SendBatchAsync(messages, ct);

        // 5. Log results
        var sentAt = DateTime.UtcNow;
        var successfulAddresses = batchResult.AllAddresses
            .Except(batchResult.FailedAddresses)
            .ToList();

        _logger.LogInformation(
            "Rescheduler EmailParticipants: jobId={JobId} sent={Sent} failed={Failed}",
            jobId, successfulAddresses.Count, batchResult.FailedAddresses.Count);

        return new EmailParticipantsResponse
        {
            RecipientCount = successfulAddresses.Count,
            FailedCount = batchResult.FailedAddresses.Count,
            SentAt = sentAt
        };
    }
}
