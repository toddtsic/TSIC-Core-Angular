using System.Net;
using System.Text.RegularExpressions;
using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for managing job configuration with role-based field filtering.
/// SuperUser-only fields are nulled on read and silently ignored on write for non-super callers.
/// </summary>
public class JobConfigService : IJobConfigService
{
    private readonly IJobConfigRepository _repo;

    public JobConfigService(IJobConfigRepository repo)
    {
        _repo = repo;
    }

    // ══════════════════════════════════════════════════════════
    // GET — Single load, all 8 categories
    // ══════════════════════════════════════════════════════════

    public async Task<JobConfigFullDto> GetFullConfigAsync(Guid jobId, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobByIdAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        var gameClock = await _repo.GetGameClockParamsAsync(jobId, ct);
        var adminCharges = isSuperUser ? await _repo.GetAdminChargesAsync(jobId, ct) : null;
        var displayOptions = await _repo.GetDisplayOptionsByJobIdAsync(jobId, ct);

        return new JobConfigFullDto
        {
            General = MapGeneral(job, isSuperUser),
            Branding = MapBranding(job, displayOptions),
            Payment = MapPayment(job, isSuperUser, adminCharges),
            Communications = MapCommunications(job),
            Player = MapPlayer(job, isSuperUser),
            Teams = MapTeams(job, isSuperUser),
            Coaches = MapCoaches(job),
            Scheduling = MapScheduling(job, gameClock),
            MobileStore = MapMobileStore(job, isSuperUser),
        };
    }

    // ══════════════════════════════════════════════════════════
    // PUT — Per-category updates
    // ══════════════════════════════════════════════════════════

    public async Task UpdateGeneralAsync(Guid jobId, UpdateJobConfigGeneralRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Admin-editable
        job.JobName = req.JobName;
        job.JobDescription = req.JobDescription;
        job.JobTagline = req.JobTagline;
        job.Season = req.Season;
        job.Year = req.Year;
        job.ExpiryUsers = req.ExpiryUsers;
        job.DisplayName = req.DisplayName;
        job.SearchenginKeywords = req.SearchenginKeywords;
        job.SearchengineDescription = req.SearchengineDescription;
        // SuperUser-only
        if (isSuperUser)
        {
            job.JobNameQbp = req.JobNameQbp;
            if (req.ExpiryAdmin.HasValue) job.ExpiryAdmin = req.ExpiryAdmin.Value;
            if (req.JobTypeId.HasValue) job.JobTypeId = req.JobTypeId.Value;
            if (req.SportId.HasValue) job.SportId = req.SportId.Value;
            if (req.CustomerId.HasValue) job.CustomerId = req.CustomerId.Value;
            if (req.BillingTypeId.HasValue) job.BillingTypeId = req.BillingTypeId.Value;
            if (req.BSuspendPublic.HasValue) job.BSuspendPublic = req.BSuspendPublic.Value;
            job.JobCode = req.JobCode;
        }

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdatePaymentAsync(Guid jobId, UpdateJobConfigPaymentRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Admin-editable
        job.PaymentMethodsAllowedCode = req.PaymentMethodsAllowedCode;
        job.BAddProcessingFees = req.BAddProcessingFees;
        job.ProcessingFeePercent = req.ProcessingFeePercent;
        job.BApplyProcessingFeesToTeamDeposit = req.BApplyProcessingFeesToTeamDeposit;
        job.PerPlayerCharge = req.PerPlayerCharge;
        job.PerTeamCharge = req.PerTeamCharge;
        job.PerMonthCharge = req.PerMonthCharge;
        job.PayTo = req.PayTo;
        job.MailTo = req.MailTo;
        job.MailinPaymentWarning = req.MailinPaymentWarning;
        job.Balancedueaspercent = req.Balancedueaspercent;
        job.BTeamsFullPaymentRequired = req.BTeamsFullPaymentRequired;
        job.BAllowRefundsInPriorMonths = req.BAllowRefundsInPriorMonths;
        job.BAllowCreditAll = req.BAllowCreditAll;

        // SuperUser-only — ARB
        if (isSuperUser)
        {
            job.AdnArb = req.AdnArb;
            job.AdnArbbillingOccurences = req.AdnArbBillingOccurrences;
            job.AdnArbintervalLength = req.AdnArbIntervalLength;
            job.AdnArbstartDate = req.AdnArbStartDate;
            job.AdnArbMinimunTotalCharge = req.AdnArbMinimumTotalCharge;
        }

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateCommunicationsAsync(Guid jobId, UpdateJobConfigCommunicationsRequest req, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        job.DisplayName = req.DisplayName;
        job.RegFormFrom = req.RegFormFrom;
        job.RegFormCcs = req.RegFormCcs;
        job.RegFormBccs = req.RegFormBccs;
        job.Rescheduleemaillist = req.Rescheduleemaillist;
        job.Alwayscopyemaillist = req.Alwayscopyemaillist;
        job.BDisallowCcplayerConfirmations = req.BDisallowCcplayerConfirmations;

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdatePlayerAsync(Guid jobId, UpdateJobConfigPlayerRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Admin-editable
        job.BRegistrationAllowPlayer = req.BRegistrationAllowPlayer;
        job.RegformNamePlayer = req.RegformNamePlayer;
        job.CoreRegformPlayer = req.CoreRegformPlayer;
        job.PlayerRegConfirmationEmail = req.PlayerRegConfirmationEmail;
        job.PlayerRegConfirmationOnScreen = req.PlayerRegConfirmationOnScreen;
        job.PlayerRegRefundPolicy = req.PlayerRegRefundPolicy;
        job.PlayerRegReleaseOfLiability = req.PlayerRegReleaseOfLiability;
        job.PlayerRegCodeOfConduct = req.PlayerRegCodeOfConduct;
        job.PlayerRegCovid19Waiver = req.PlayerRegCovid19Waiver;
        job.PlayerRegMultiPlayerDiscountMin = req.PlayerRegMultiPlayerDiscountMin;
        job.PlayerRegMultiPlayerDiscountPercent = req.PlayerRegMultiPlayerDiscountPercent;

        // SuperUser-only
        if (isSuperUser)
        {
            if (req.BOfferPlayerRegsaverInsurance.HasValue)
                job.BOfferPlayerRegsaverInsurance = req.BOfferPlayerRegsaverInsurance.Value;
            job.MomLabel = req.MomLabel;
            job.DadLabel = req.DadLabel;
            job.PlayerProfileMetadataJson = req.PlayerProfileMetadataJson;
        }

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateTeamsAsync(Guid jobId, UpdateJobConfigTeamsRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Admin-editable
        job.BRegistrationAllowTeam = req.BRegistrationAllowTeam;
        job.RegformNameTeam = req.RegformNameTeam;
        job.RegformNameClubRep = req.RegformNameClubRep;
        job.BClubRepAllowEdit = req.BClubRepAllowEdit;
        job.BClubRepAllowDelete = req.BClubRepAllowDelete;
        job.BClubRepAllowAdd = req.BClubRepAllowAdd;
        job.BRestrictPlayerTeamsToAgerange = req.BRestrictPlayerTeamsToAgerange;
        job.BTeamPushDirectors = req.BTeamPushDirectors;
        job.BUseWaitlists = req.BUseWaitlists;
        job.BShowTeamNameOnlyInSchedules = req.BShowTeamNameOnlyInSchedules;

        // SuperUser-only
        if (isSuperUser)
        {
            if (req.BOfferTeamRegsaverInsurance.HasValue)
                job.BOfferTeamRegsaverInsurance = req.BOfferTeamRegsaverInsurance.Value;
        }

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateCoachesAsync(Guid jobId, UpdateJobConfigCoachesRequest req, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        job.RegformNameCoach = req.RegformNameCoach;
        job.AdultRegConfirmationEmail = req.AdultRegConfirmationEmail;
        job.AdultRegConfirmationOnScreen = req.AdultRegConfirmationOnScreen;
        job.AdultRegRefundPolicy = req.AdultRegRefundPolicy;
        job.AdultRegReleaseOfLiability = req.AdultRegReleaseOfLiability;
        job.AdultRegCodeOfConduct = req.AdultRegCodeOfConduct;
        job.RefereeRegConfirmationEmail = req.RefereeRegConfirmationEmail;
        job.RefereeRegConfirmationOnScreen = req.RefereeRegConfirmationOnScreen;
        job.RecruiterRegConfirmationEmail = req.RecruiterRegConfirmationEmail;
        job.RecruiterRegConfirmationOnScreen = req.RecruiterRegConfirmationOnScreen;
        job.BAllowRosterViewAdult = req.BAllowRosterViewAdult;
        job.BAllowRosterViewPlayer = req.BAllowRosterViewPlayer;

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateSchedulingAsync(Guid jobId, UpdateJobConfigSchedulingRequest req, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        job.EventStartDate = req.EventStartDate;
        job.EventEndDate = req.EventEndDate;
        job.BScheduleAllowPublicAccess = req.BScheduleAllowPublicAccess;

        // GameClockParams — upsert pattern
        if (req.GameClock is not null)
        {
            var gcp = await _repo.GetGameClockParamsTrackedAsync(jobId, ct);
            if (gcp is null)
            {
                // Insert new
                gcp = new GameClockParams
                {
                    JobId = jobId,
                    HalfMinutes = req.GameClock.HalfMinutes,
                    HalfTimeMinutes = req.GameClock.HalfTimeMinutes,
                    TransitionMinutes = req.GameClock.TransitionMinutes,
                    PlayoffMinutes = req.GameClock.PlayoffMinutes,
                    PlayoffHalfMinutes = req.GameClock.PlayoffHalfMinutes,
                    PlayoffHalfTimeMinutes = req.GameClock.PlayoffHalfTimeMinutes,
                    QuarterMinutes = req.GameClock.QuarterMinutes,
                    QuarterTimeMinutes = req.GameClock.QuarterTimeMinutes,
                    UtcoffsetHours = req.GameClock.UtcOffsetHours,
                    Modified = DateTime.UtcNow,
                };
                _repo.AddGameClockParams(gcp);
            }
            else
            {
                // Update existing
                gcp.HalfMinutes = req.GameClock.HalfMinutes;
                gcp.HalfTimeMinutes = req.GameClock.HalfTimeMinutes;
                gcp.TransitionMinutes = req.GameClock.TransitionMinutes;
                gcp.PlayoffMinutes = req.GameClock.PlayoffMinutes;
                gcp.PlayoffHalfMinutes = req.GameClock.PlayoffHalfMinutes;
                gcp.PlayoffHalfTimeMinutes = req.GameClock.PlayoffHalfTimeMinutes;
                gcp.QuarterMinutes = req.GameClock.QuarterMinutes;
                gcp.QuarterTimeMinutes = req.GameClock.QuarterTimeMinutes;
                gcp.UtcoffsetHours = req.GameClock.UtcOffsetHours;
                gcp.Modified = DateTime.UtcNow;
            }
        }

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateMobileStoreAsync(Guid jobId, UpdateJobConfigMobileStoreRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Admin-editable — Mobile
        job.BEnableTsicteams = req.BEnableTsicteams;
        job.BEnableMobileRsvp = req.BEnableMobileRsvp;
        job.BEnableMobileTeamChat = req.BEnableMobileTeamChat;
        job.BAllowMobileLogin = req.BAllowMobileLogin;
        job.BAllowMobileRegn = req.BAllowMobileRegn;
        job.MobileScoreHoursPastGameEligible = req.MobileScoreHoursPastGameEligible;

        // SuperUser-only
        if (isSuperUser)
        {
            job.MobileJobName = req.MobileJobName;
            if (req.BEnableStore.HasValue) job.BEnableStore = req.BEnableStore.Value;
            if (req.BenableStp.HasValue) job.BenableStp = req.BenableStp.Value;
            job.StoreContactEmail = req.StoreContactEmail;
            job.StoreRefundPolicy = req.StoreRefundPolicy;
            job.StorePickupDetails = req.StorePickupDetails;
            if (req.StoreSalesTax.HasValue) job.StoreSalesTax = req.StoreSalesTax.Value;
            job.StoreTsicrate = req.StoreTsicrate;
        }

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    // ══════════════════════════════════════════════════════════
    // Reference Data
    // ══════════════════════════════════════════════════════════

    public async Task<JobConfigReferenceDataDto> GetReferenceDataAsync(CancellationToken ct = default)
    {
        var jobTypes = await _repo.GetJobTypesAsync(ct);
        var sports = await _repo.GetSportsAsync(ct);
        var customers = await _repo.GetCustomersAsync(ct);
        var billingTypes = await _repo.GetBillingTypesAsync(ct);
        var chargeTypes = await _repo.GetChargeTypesAsync(ct);

        return new JobConfigReferenceDataDto
        {
            JobTypes = jobTypes,
            Sports = sports,
            Customers = customers,
            BillingTypes = billingTypes,
            ChargeTypes = chargeTypes,
        };
    }

    // ══════════════════════════════════════════════════════════
    // Admin Charges CRUD
    // ══════════════════════════════════════════════════════════

    public async Task<JobAdminChargeDto> AddAdminChargeAsync(Guid jobId, CreateAdminChargeRequest req, CancellationToken ct = default)
    {
        var charge = new JobAdminCharges
        {
            JobId = jobId,
            ChargeTypeId = req.ChargeTypeId,
            ChargeAmount = req.ChargeAmount,
            Comment = req.Comment,
            Year = req.Year,
            Month = req.Month,
            CreateDate = DateTime.UtcNow,
        };

        _repo.AddAdminCharge(charge);
        await _repo.SaveChangesAsync(ct);

        return new JobAdminChargeDto
        {
            Id = charge.Id,
            ChargeTypeId = charge.ChargeTypeId,
            ChargeTypeName = null, // caller can refresh full config to get names
            ChargeAmount = charge.ChargeAmount,
            Comment = charge.Comment,
            Year = charge.Year,
            Month = charge.Month,
        };
    }

    public async Task DeleteAdminChargeAsync(Guid jobId, int chargeId, CancellationToken ct = default)
    {
        var charge = await _repo.GetAdminChargeByIdAsync(chargeId, jobId, ct)
            ?? throw new KeyNotFoundException($"Admin charge {chargeId} not found for job {jobId}.");

        _repo.RemoveAdminCharge(charge);
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateBrandingAsync(Guid jobId, UpdateJobConfigBrandingRequest req, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Upsert JobDisplayOptions for text fields + banner toggle
        var jdo = await _repo.GetDisplayOptionsTrackedAsync(jobId, ct);
        if (jdo is null)
        {
            jdo = new JobDisplayOptions
            {
                JobId = jobId,
                ParallaxSlideCount = req.BBannerIsCustom ? 1 : 0,
                ParallaxSlide1Text1 = NewlineToBr(req.BannerOverlayText1),
                ParallaxSlide1Text2 = NewlineToBr(req.BannerOverlayText2),
            };
            _repo.AddDisplayOptions(jdo);
        }
        else
        {
            jdo.ParallaxSlideCount = req.BBannerIsCustom ? 1 : 0;
            jdo.ParallaxSlide1Text1 = NewlineToBr(req.BannerOverlayText1);
            jdo.ParallaxSlide1Text2 = NewlineToBr(req.BannerOverlayText2);
        }

        job.Modified = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateBrandingImageFieldAsync(Guid jobId, string conventionName, string? fileName, CancellationToken ct = default)
    {
        // Upsert JobDisplayOptions — set the correct field based on convention name
        var jdo = await _repo.GetDisplayOptionsTrackedAsync(jobId, ct);
        if (jdo is null)
        {
            jdo = new JobDisplayOptions { JobId = jobId };
            _repo.AddDisplayOptions(jdo);
        }

        switch (conventionName)
        {
            case BrandingImageConventions.BannerBackground:
                jdo.ParallaxBackgroundImage = fileName;
                break;
            case BrandingImageConventions.BannerOverlay:
                jdo.ParallaxSlide1Image = fileName;
                break;
            case BrandingImageConventions.LogoHeader:
                jdo.LogoHeader = fileName;
                break;
            default:
                throw new ArgumentException($"Unknown convention name: {conventionName}", nameof(conventionName));
        }

        await _repo.SaveChangesAsync(ct);
    }

    // ══════════════════════════════════════════════════════════
    // Private Mapping Helpers
    // ══════════════════════════════════════════════════════════

    private static JobConfigBrandingDto MapBranding(Jobs job, JobDisplayOptions? jdo) => new()
    {
        BBannerIsCustom = jdo?.ParallaxSlideCount > 0,
        BannerBackgroundImage = jdo?.ParallaxBackgroundImage,
        BannerOverlayImage = jdo?.ParallaxSlide1Image,
        BannerOverlayText1 = SanitizeOverlayText(jdo?.ParallaxSlide1Text1),
        BannerOverlayText2 = SanitizeOverlayText(jdo?.ParallaxSlide1Text2),
        LogoHeader = jdo?.LogoHeader,
    };

    /// <summary>
    /// Converts legacy HTML-encoded/rich-text overlay values to plain text.
    /// Decodes HTML entities, converts &lt;br&gt; to newlines, strips remaining tags.
    /// </summary>
    private static string? SanitizeOverlayText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Decode HTML entities (legacy data is HTML-encoded)
        var text = WebUtility.HtmlDecode(raw);

        // Convert <br> variants to newline
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Strip all remaining HTML tags
        text = Regex.Replace(text, @"<[^>]+>", "");

        // Collapse &nbsp; remnants
        text = text.Replace("\u00A0", " ");

        // Trim each line, remove blank-only lines at start/end
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .ToList();

        // Remove leading/trailing empty lines
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        var result = string.Join("\n", lines);
        return result.Length > 0 ? result : null;
    }

    /// <summary>
    /// Converts newlines from textarea input to &lt;br&gt; for DB storage.
    /// Returns null for empty/whitespace-only input.
    /// </summary>
    private static string? NewlineToBr(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Trim().Replace("\r\n", "<br>").Replace("\n", "<br>");
    }

    private static JobConfigGeneralDto MapGeneral(Jobs job, bool isSuperUser) => new()
    {
        JobId = job.JobId,
        JobPath = job.JobPath,
        JobName = job.JobName,
        JobDescription = job.JobDescription,
        JobTagline = job.JobTagline,
        Season = job.Season,
        Year = job.Year,
        ExpiryUsers = job.ExpiryUsers,
        DisplayName = job.DisplayName,
        SearchenginKeywords = job.SearchenginKeywords,
        SearchengineDescription = job.SearchengineDescription,
        // SuperUser-only
        JobNameQbp = isSuperUser ? job.JobNameQbp : null,
        ExpiryAdmin = isSuperUser ? job.ExpiryAdmin : null,
        JobTypeId = isSuperUser ? job.JobTypeId : null,
        SportId = isSuperUser ? job.SportId : null,
        CustomerId = isSuperUser ? job.CustomerId : null,
        BillingTypeId = isSuperUser ? job.BillingTypeId : null,
        BSuspendPublic = isSuperUser ? job.BSuspendPublic : null,
        JobCode = isSuperUser ? job.JobCode : null,
    };

    private static JobConfigPaymentDto MapPayment(Jobs job, bool isSuperUser, List<JobAdminCharges>? charges) => new()
    {
        PaymentMethodsAllowedCode = job.PaymentMethodsAllowedCode,
        BAddProcessingFees = job.BAddProcessingFees,
        ProcessingFeePercent = job.ProcessingFeePercent,
        BApplyProcessingFeesToTeamDeposit = job.BApplyProcessingFeesToTeamDeposit,
        PerPlayerCharge = job.PerPlayerCharge,
        PerTeamCharge = job.PerTeamCharge,
        PerMonthCharge = job.PerMonthCharge,
        PayTo = job.PayTo,
        MailTo = job.MailTo,
        MailinPaymentWarning = job.MailinPaymentWarning,
        Balancedueaspercent = job.Balancedueaspercent,
        BTeamsFullPaymentRequired = job.BTeamsFullPaymentRequired,
        BAllowRefundsInPriorMonths = job.BAllowRefundsInPriorMonths,
        BAllowCreditAll = job.BAllowCreditAll,
        // SuperUser-only — ARB
        AdnArb = isSuperUser ? job.AdnArb : null,
        AdnArbBillingOccurrences = isSuperUser ? job.AdnArbbillingOccurences : null,
        AdnArbIntervalLength = isSuperUser ? job.AdnArbintervalLength : null,
        AdnArbStartDate = isSuperUser ? job.AdnArbstartDate : null,
        AdnArbMinimumTotalCharge = isSuperUser ? job.AdnArbMinimunTotalCharge : null,
        AdminCharges = charges?.Select(c => new JobAdminChargeDto
        {
            Id = c.Id,
            ChargeTypeId = c.ChargeTypeId,
            ChargeTypeName = c.ChargeType?.Name,
            ChargeAmount = c.ChargeAmount,
            Comment = c.Comment,
            Year = c.Year,
            Month = c.Month,
        }).ToList(),
    };

    private static JobConfigCommunicationsDto MapCommunications(Jobs job) => new()
    {
        DisplayName = job.DisplayName,
        RegFormFrom = job.RegFormFrom,
        RegFormCcs = job.RegFormCcs,
        RegFormBccs = job.RegFormBccs,
        Rescheduleemaillist = job.Rescheduleemaillist,
        Alwayscopyemaillist = job.Alwayscopyemaillist,
        BDisallowCcplayerConfirmations = job.BDisallowCcplayerConfirmations,
    };

    private static JobConfigPlayerDto MapPlayer(Jobs job, bool isSuperUser) => new()
    {
        BRegistrationAllowPlayer = job.BRegistrationAllowPlayer,
        RegformNamePlayer = job.RegformNamePlayer,
        CoreRegformPlayer = job.CoreRegformPlayer,
        PlayerRegConfirmationEmail = job.PlayerRegConfirmationEmail,
        PlayerRegConfirmationOnScreen = job.PlayerRegConfirmationOnScreen,
        PlayerRegRefundPolicy = job.PlayerRegRefundPolicy,
        PlayerRegReleaseOfLiability = job.PlayerRegReleaseOfLiability,
        PlayerRegCodeOfConduct = job.PlayerRegCodeOfConduct,
        PlayerRegCovid19Waiver = job.PlayerRegCovid19Waiver,
        PlayerRegMultiPlayerDiscountMin = job.PlayerRegMultiPlayerDiscountMin,
        PlayerRegMultiPlayerDiscountPercent = job.PlayerRegMultiPlayerDiscountPercent,
        // SuperUser-only
        BOfferPlayerRegsaverInsurance = isSuperUser ? job.BOfferPlayerRegsaverInsurance : null,
        MomLabel = isSuperUser ? job.MomLabel : null,
        DadLabel = isSuperUser ? job.DadLabel : null,
        PlayerProfileMetadataJson = isSuperUser ? job.PlayerProfileMetadataJson : null,
    };

    private static JobConfigTeamsDto MapTeams(Jobs job, bool isSuperUser) => new()
    {
        BRegistrationAllowTeam = job.BRegistrationAllowTeam,
        RegformNameTeam = job.RegformNameTeam,
        RegformNameClubRep = job.RegformNameClubRep,
        BClubRepAllowEdit = job.BClubRepAllowEdit,
        BClubRepAllowDelete = job.BClubRepAllowDelete,
        BClubRepAllowAdd = job.BClubRepAllowAdd,
        BRestrictPlayerTeamsToAgerange = job.BRestrictPlayerTeamsToAgerange,
        BTeamPushDirectors = job.BTeamPushDirectors,
        BUseWaitlists = job.BUseWaitlists,
        BShowTeamNameOnlyInSchedules = job.BShowTeamNameOnlyInSchedules,
        // SuperUser-only
        BOfferTeamRegsaverInsurance = isSuperUser ? job.BOfferTeamRegsaverInsurance : null,
    };

    private static JobConfigCoachesDto MapCoaches(Jobs job) => new()
    {
        RegformNameCoach = job.RegformNameCoach,
        AdultRegConfirmationEmail = job.AdultRegConfirmationEmail,
        AdultRegConfirmationOnScreen = job.AdultRegConfirmationOnScreen,
        AdultRegRefundPolicy = job.AdultRegRefundPolicy,
        AdultRegReleaseOfLiability = job.AdultRegReleaseOfLiability,
        AdultRegCodeOfConduct = job.AdultRegCodeOfConduct,
        RefereeRegConfirmationEmail = job.RefereeRegConfirmationEmail,
        RefereeRegConfirmationOnScreen = job.RefereeRegConfirmationOnScreen,
        RecruiterRegConfirmationEmail = job.RecruiterRegConfirmationEmail,
        RecruiterRegConfirmationOnScreen = job.RecruiterRegConfirmationOnScreen,
        BAllowRosterViewAdult = job.BAllowRosterViewAdult,
        BAllowRosterViewPlayer = job.BAllowRosterViewPlayer,
    };

    private static JobConfigSchedulingDto MapScheduling(Jobs job, GameClockParams? gcp) => new()
    {
        EventStartDate = job.EventStartDate,
        EventEndDate = job.EventEndDate,
        BScheduleAllowPublicAccess = job.BScheduleAllowPublicAccess,
        GameClock = gcp is null ? null : new GameClockParamsDto
        {
            Id = gcp.Id,
            HalfMinutes = gcp.HalfMinutes,
            HalfTimeMinutes = gcp.HalfTimeMinutes,
            TransitionMinutes = gcp.TransitionMinutes,
            PlayoffMinutes = gcp.PlayoffMinutes,
            PlayoffHalfMinutes = gcp.PlayoffHalfMinutes,
            PlayoffHalfTimeMinutes = gcp.PlayoffHalfTimeMinutes,
            QuarterMinutes = gcp.QuarterMinutes,
            QuarterTimeMinutes = gcp.QuarterTimeMinutes,
            UtcOffsetHours = gcp.UtcoffsetHours,
        },
    };

    private static JobConfigMobileStoreDto MapMobileStore(Jobs job, bool isSuperUser) => new()
    {
        BEnableTsicteams = job.BEnableTsicteams,
        BEnableMobileRsvp = job.BEnableMobileRsvp,
        BEnableMobileTeamChat = job.BEnableMobileTeamChat,
        BAllowMobileLogin = job.BAllowMobileLogin,
        BAllowMobileRegn = job.BAllowMobileRegn,
        MobileScoreHoursPastGameEligible = job.MobileScoreHoursPastGameEligible,
        // SuperUser-only
        MobileJobName = isSuperUser ? job.MobileJobName : null,
        BEnableStore = isSuperUser ? job.BEnableStore : null,
        BenableStp = isSuperUser ? job.BenableStp : null,
        StoreContactEmail = isSuperUser ? job.StoreContactEmail : null,
        StoreRefundPolicy = isSuperUser ? job.StoreRefundPolicy : null,
        StorePickupDetails = isSuperUser ? job.StorePickupDetails : null,
        StoreSalesTax = isSuperUser ? job.StoreSalesTax : null,
        StoreTsicrate = isSuperUser ? job.StoreTsicrate : null,
    };
}
