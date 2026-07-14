using System.Net;
using System.Text.RegularExpressions;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Adults;
using TSIC.API.Services.Metadata;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for managing job configuration with role-based field filtering.
/// SuperUser-only fields are nulled on read and silently ignored on write for non-super callers.
/// </summary>
public class JobConfigService : IJobConfigService
{
    private readonly IJobConfigRepository _repo;
    private readonly ITeamRegistrationService _teamRegService;
    private readonly IPlayerRegistrationService _playerRegService;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IProfileMetadataMigrationService _profileMigration;
    private readonly ILogger<JobConfigService> _logger;

    public JobConfigService(
        IJobConfigRepository repo,
        ITeamRegistrationService teamRegService,
        IPlayerRegistrationService playerRegService,
        IScheduleRepository scheduleRepo,
        IProfileMetadataMigrationService profileMigration,
        ILogger<JobConfigService> logger)
    {
        _repo = repo;
        _teamRegService = teamRegService;
        _playerRegService = playerRegService;
        _scheduleRepo = scheduleRepo;
        _profileMigration = profileMigration;
        _logger = logger;
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
            Scheduling = MapScheduling(job, gameClock, isSuperUser),
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
        job.JobTagline = req.JobTagline;
        job.Season = req.Season;
        job.Year = req.Year;
        job.ExpiryUsers = req.ExpiryUsers;
        job.SearchenginKeywords = req.SearchenginKeywords;
        job.SearchengineDescription = req.SearchengineDescription;
        // SuperUser-only
        if (isSuperUser)
        {
            // Job path rename. Blank or unchanged → no-op (guards non-super-shaped payloads
            // and prevents nulling the required identity slug). A real change is validated for
            // slug format and uniqueness, then persisted. Note: this invalidates existing JWTs
            // (jobPath claim) — the admin must log out and back in on the new path.
            if (!string.IsNullOrWhiteSpace(req.JobPath))
            {
                var newPath = req.JobPath.Trim();
                if (!newPath.Equals(job.JobPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!Regex.IsMatch(newPath, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
                        throw new ArgumentException(
                            "Job path must be a lowercase slug: letters, numbers, and single hyphens only (e.g. 'hhh-summer-2026').",
                            nameof(req));

                    if (await _repo.JobPathInUseByOtherAsync(newPath, jobId, ct))
                        throw new InvalidOperationException($"Job path '{newPath}' is already in use by another job.");

                    job.JobPath = newPath;
                }
            }

            job.JobDescription = req.JobDescription;
            job.JobNameQbp = req.JobNameQbp;
            if (req.ExpiryAdmin.HasValue) job.ExpiryAdmin = req.ExpiryAdmin.Value;
            if (req.JobTypeId.HasValue) job.JobTypeId = req.JobTypeId.Value;
            if (req.SportId.HasValue) job.SportId = req.SportId.Value;
            if (req.CustomerId.HasValue) job.CustomerId = req.CustomerId.Value;
            if (req.BillingTypeId.HasValue) job.BillingTypeId = req.BillingTypeId.Value;
            job.JobCode = req.JobCode;
        }

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdatePaymentAsync(Guid jobId, UpdateJobConfigPaymentRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Validate processing-fee ranges (defense in depth — frontend already enforces via min/max attrs)
        if (req.ProcessingFeePercent.HasValue
            && (req.ProcessingFeePercent.Value < FeeConstants.MinProcessingFeePercent
                || req.ProcessingFeePercent.Value > FeeConstants.MaxProcessingFeePercent))
        {
            throw new ArgumentException(
                $"ProcessingFeePercent must be between {FeeConstants.MinProcessingFeePercent} and {FeeConstants.MaxProcessingFeePercent}.",
                nameof(req));
        }

        if (req.EcprocessingFeePercent.HasValue
            && (req.EcprocessingFeePercent.Value < FeeConstants.MinEcprocessingFeePercent
                || req.EcprocessingFeePercent.Value > FeeConstants.MaxEcprocessingFeePercent))
        {
            throw new ArgumentException(
                $"EcprocessingFeePercent must be between {FeeConstants.MinEcprocessingFeePercent} and {FeeConstants.MaxEcprocessingFeePercent}.",
                nameof(req));
        }

        // Snapshot fee-affecting flags before update
        var prevFullPayReq = job.BTeamsFullPaymentRequired ?? false;
        var prevPlayersFullPayReq = job.BPlayersFullPaymentRequired;
        var prevAddProcessing = job.BAddProcessingFees;
        var prevApplyProcToDeposit = job.BApplyProcessingFeesToTeamDeposit ?? false;
        var prevProcessingRate = job.ProcessingFeePercent;

        // Admin-editable
        job.PaymentMethodsAllowedCode = req.PaymentMethodsAllowedCode;
        job.BAddProcessingFees = req.BAddProcessingFees;
        job.ProcessingFeePercent = req.ProcessingFeePercent;
        job.BEnableEcheck = req.BEnableEcheck;
        job.EcprocessingFeePercent = req.EcprocessingFeePercent;
        job.BApplyProcessingFeesToTeamDeposit = req.BApplyProcessingFeesToTeamDeposit;
        job.PayTo = req.PayTo;
        job.MailTo = req.MailTo;
        job.MailinPaymentWarning = req.MailinPaymentWarning;
        job.Balancedueaspercent = req.Balancedueaspercent;
        job.BTeamsFullPaymentRequired = req.BTeamsFullPaymentRequired;
        job.BPlayersFullPaymentRequired = req.BPlayersFullPaymentRequired;
        job.BIncludePlayerDonation = req.BIncludePlayerDonation;
        job.BIncludeTeamDonation = req.BIncludeTeamDonation;
        job.BAllowRefundsInPriorMonths = req.BAllowRefundsInPriorMonths;
        job.BAllowCreditAll = req.BAllowCreditAll;

        // SuperUser-only — per-unit charges + ARB
        if (isSuperUser)
        {
            job.PerPlayerCharge = req.PerPlayerCharge;
            job.PerTeamCharge = req.PerTeamCharge;
            job.PerMonthCharge = req.PerMonthCharge;
            job.AdnArb = req.AdnArb;
            job.AdnArbbillingOccurences = req.AdnArbBillingOccurrences;
            job.AdnArbintervalLength = req.AdnArbIntervalLength;
            job.AdnArbstartDate = req.AdnArbStartDate;
            job.AdnArbMinimunTotalCharge = req.AdnArbMinimumTotalCharge;
            job.AdnArbtrial = req.AdnArbTrial;
            job.AdnStartDateAfterTrial = req.AdnStartDateAfterTrial;
        }

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);

        // Auto-recalculate team fees if any team-fee-affecting flag changed
        var teamFeeConfigChanged =
            prevFullPayReq != req.BTeamsFullPaymentRequired ||
            prevAddProcessing != req.BAddProcessingFees ||
            prevApplyProcToDeposit != req.BApplyProcessingFeesToTeamDeposit ||
            prevProcessingRate != req.ProcessingFeePercent;

        if (teamFeeConfigChanged)
        {
            _logger.LogInformation(
                "Fee-affecting config changed for Job {JobId} — auto-recalculating team fees. " +
                "FullPayReq: {Prev}→{New}, AddProcessing: {PrevP}→{NewP}, ApplyToDeposit: {PrevD}→{NewD}",
                jobId,
                prevFullPayReq, req.BTeamsFullPaymentRequired,
                prevAddProcessing, req.BAddProcessingFees,
                prevApplyProcToDeposit, req.BApplyProcessingFeesToTeamDeposit);

            // SuperUserId — recalc stamps Teams.LebUserId, which FKs to AspNetUsers.
            // A synthetic "system:..." literal isn't a real user row and SQL rejects the UPDATE.
            var result = await _teamRegService.RecalculateTeamFeesAsync(
                new RecalculateTeamFeesRequest { JobId = jobId },
                TsicConstants.SuperUserId);

            _logger.LogInformation("Auto-recalculated {Count} team fees for Job {JobId}", result.UpdatedCount, jobId);
        }

        // Auto-recalculate player fees when the player phase flag flips. Processing-fee
        // changes also affect player FeeProcessing — same trigger conditions apply.
        var playerFeeConfigChanged =
            prevPlayersFullPayReq != req.BPlayersFullPaymentRequired ||
            prevAddProcessing != req.BAddProcessingFees ||
            prevProcessingRate != req.ProcessingFeePercent;

        if (playerFeeConfigChanged)
        {
            _logger.LogInformation(
                "Player-fee-affecting config changed for Job {JobId} — auto-recalculating player fees. " +
                "PlayersFullPayReq: {Prev}→{New}, AddProcessing: {PrevP}→{NewP}",
                jobId,
                prevPlayersFullPayReq, req.BPlayersFullPaymentRequired,
                prevAddProcessing, req.BAddProcessingFees);

            var playerCount = await _playerRegService.RecalculatePlayerFeesAsync(
                jobId, TsicConstants.SuperUserId, ct: ct);

            _logger.LogInformation("Auto-recalculated {Count} player fees for Job {JobId}", playerCount, jobId);
        }
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

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdatePlayerAsync(Guid jobId, UpdateJobConfigPlayerRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Admin-editable
        job.BRegistrationAllowPlayer = req.BRegistrationAllowPlayer;
        job.BplayerRegRequiresToken = req.BPlayerRegRequiresToken ?? false;
        job.RegformNamePlayer = req.RegformNamePlayer;
        job.PlayerRegConfirmationEmail = req.PlayerRegConfirmationEmail;
        job.PlayerRegConfirmationOnScreen = req.PlayerRegConfirmationOnScreen;
        job.PlayerRegRefundPolicy = req.PlayerRegRefundPolicy;
        job.PlayerRegReleaseOfLiability = req.PlayerRegReleaseOfLiability;
        job.PlayerRegCodeOfConduct = req.PlayerRegCodeOfConduct;
        job.PlayerRegCovid19Waiver = req.PlayerRegCovid19Waiver;
        job.UslaxNumberValidThroughDate = req.UslaxNumberValidThroughDate;

        // SuperUser-only
        if (isSuperUser)
        {
            // NOTE: PlayerReg_MultiPlayerDiscount_{Min,Percent} are deliberately NOT written here.
            // The setting was retired (CR-013): it saved, displayed and cloned but no charge path ever
            // read it, so a director could configure a sibling discount and watch it never get applied.
            // The columns remain on Jobs (untouched, no DDL) and the fee component FeeDiscountMp is a
            // live, always-subtracted term in FeeMath — so a real multi-player discount can be built
            // later without re-opening the fee formula. What must NOT come back is a config field that
            // nothing consumes.
            job.CoreRegformPlayer = req.CoreRegformPlayer;
            if (req.BOfferPlayerRegsaverInsurance.HasValue)
                job.BOfferPlayerRegsaverInsurance = req.BOfferPlayerRegsaverInsurance.Value;
            job.MomLabel = req.MomLabel;
            job.DadLabel = req.DadLabel;
            if (req.PlayerProfileMetadataJson is not null)
                job.PlayerProfileMetadataJson = req.PlayerProfileMetadataJson;
        }

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateTeamsAsync(Guid jobId, UpdateJobConfigTeamsRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Admin-editable
        job.BRegistrationAllowTeam = req.BRegistrationAllowTeam;
        job.BteamRegRequiresToken = req.BTeamRegRequiresToken ?? false;
        job.RegformNameTeam = req.RegformNameTeam;
        job.RegformNameClubRep = req.RegformNameClubRep;
        job.BClubRepAllowEdit = req.BClubRepAllowEdit;
        job.BClubRepAllowDelete = req.BClubRepAllowDelete;
        job.BClubRepAllowAdd = req.BClubRepAllowAdd;
        job.BRestrictPlayerTeamsToAgerange = req.BRestrictPlayerTeamsToAgerange;
        job.BTeamPushDirectors = req.BTeamPushDirectors;
        // BUseWaitlists is retired — waitlists are mandatory and the column is vestigial.
        // BShowTeamNameOnlyInSchedules lives on the Scheduling tab (it drives schedule rendering).
        job.BAllowRosterViewAdult = req.BAllowRosterViewAdult;
        job.BAllowRosterViewPlayer = req.BAllowRosterViewPlayer;

        // SuperUser-only
        if (isSuperUser)
        {
            if (req.BOfferTeamRegsaverInsurance.HasValue)
                job.BOfferTeamRegsaverInsurance = req.BOfferTeamRegsaverInsurance.Value;
        }

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateCoachesAsync(Guid jobId, UpdateJobConfigCoachesRequest req, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        job.BRegistrationAllowStaff = req.BRegistrationAllowStaff;
        job.BRegistrationAllowReferee = req.BRegistrationAllowReferee;
        job.BRegistrationAllowRecruiter = req.BRegistrationAllowRecruiter;
        // RegformNameCoach is NOT written here. It is the derived coach-form identity, owned by the
        // SuperUser-only coach-form-template swap (UpdateCoachFormTemplateAsync), which keeps it in sync
        // with the materialized AdultProfileMetadataJson. A plain coaches save must never desync them.
        job.AdultRegConfirmationEmail = req.AdultRegConfirmationEmail;
        job.AdultRegConfirmationOnScreen = req.AdultRegConfirmationOnScreen;
        job.AdultRegRefundPolicy = req.AdultRegRefundPolicy;
        job.AdultRegReleaseOfLiability = req.AdultRegReleaseOfLiability;
        job.AdultRegCodeOfConduct = req.AdultRegCodeOfConduct;
        job.RefereeRegConfirmationEmail = req.RefereeRegConfirmationEmail;
        job.RefereeRegConfirmationOnScreen = req.RefereeRegConfirmationOnScreen;
        job.RecruiterRegConfirmationEmail = req.RecruiterRegConfirmationEmail;
        job.RecruiterRegConfirmationOnScreen = req.RecruiterRegConfirmationOnScreen;

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);
    }

    /// <summary>
    /// SuperUser-only per-job coach-form swap. Re-materializes THIS job's coach form to the chosen
    /// canonical profile + USLax (preserving Referee/Recruiter) and re-syncs the legacy identity field
    /// (RegformName_Coach) so MapLegacy keeps agreeing with the stored blob. Overwrites any hand
    /// customization on the coach role — the UI gates this behind a confirm.
    /// </summary>
    public async Task UpdateCoachFormTemplateAsync(Guid jobId, UpdateCoachFormTemplateRequest req, CancellationToken ct = default)
    {
        var profile = AdultFormCatalog.Canonical(req.ProfileCode);
        if (!AdultFormCatalog.IsKnownProfile(profile))
            throw new ArgumentException($"Unknown coach form template '{req.ProfileCode}'.", nameof(req));
        if (req.RequiresUsLax && !AdultFormCatalog.CanRequireUsLax(profile))
            throw new ArgumentException($"Template '{profile}' cannot require a USA Lacrosse number.", nameof(req));

        var legacyName = AdultFormCatalog.ToLegacyRegformName(profile, req.RequiresUsLax)
            ?? throw new ArgumentException($"Unsupported template/USLax combination for '{profile}'.", nameof(req));

        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Rebuild the coach role (mutates job.JsonOptions for apparel seeding) and re-sync the identity.
        job.AdultProfileMetadataJson = _profileMigration.ComputeCoachFormSwap(job, profile, req.RequiresUsLax);
        job.RegformNameCoach = legacyName;
        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task UpdateSchedulingAsync(Guid jobId, UpdateJobConfigSchedulingRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        var prevShowTeamNameOnly = job.BShowTeamNameOnlyInSchedules;

        job.EventStartDate = req.EventStartDate;
        job.EventEndDate = req.EventEndDate;
        job.BScheduleAllowPublicAccess = req.BScheduleAllowPublicAccess;
        job.BRestrictPublicRosters = req.BRestrictPublicRosters;
        job.BShowTeamNameOnlyInSchedules = req.BShowTeamNameOnlyInSchedules;

        // SuperUser-only
        if (isSuperUser && req.BReseedTournament.HasValue)
            job.BReseedTournament = req.BReseedTournament.Value;

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
                    Modified = DateTime.Now,
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
                gcp.Modified = DateTime.Now;
            }
        }

        job.Modified = DateTime.Now;
        await _repo.SaveChangesAsync(ct);

        if (prevShowTeamNameOnly != req.BShowTeamNameOnlyInSchedules)
        {
            _logger.LogInformation(
                "BShowTeamNameOnlyInSchedules flipped on Job {JobId} ({Prev}→{New}) — recomposing schedule team names.",
                jobId, prevShowTeamNameOnly, req.BShowTeamNameOnlyInSchedules);
            await _scheduleRepo.SynchronizeAllScheduleNamesForJobAsync(jobId, ct);
        }
    }

    public async Task UpdateMobileStoreAsync(Guid jobId, UpdateJobConfigMobileStoreRequest req, bool isSuperUser, CancellationToken ct = default)
    {
        var job = await _repo.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // Admin-editable — Mobile
        if (req.BSuspendPublic.HasValue) job.BSuspendPublic = req.BSuspendPublic.Value;
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
            if (req.BAllowStoreWalkup.HasValue) job.BAllowStoreWalkup = req.BAllowStoreWalkup.Value;
            if (req.BenableStp.HasValue) job.BenableStp = req.BenableStp.Value;
            job.StoreContactEmail = req.StoreContactEmail;
            job.StoreRefundPolicy = req.StoreRefundPolicy;
            job.StorePickupDetails = req.StorePickupDetails;
            if (req.StoreSalesTax.HasValue) job.StoreSalesTax = req.StoreSalesTax.Value;
            job.StoreTsicrate = req.StoreTsicrate;
        }

        job.Modified = DateTime.Now;
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
            CreateDate = DateTime.Now,
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

        job.Modified = DateTime.Now;
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

        // Trim each line and drop ALL blank lines.
        // Drops the bare \r isolated by `<br />\r\n` patterns that survive Windows-line-ending splits,
        // plus any other blank middle lines from legacy data.
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

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
        JobTagline = job.JobTagline,
        Season = job.Season,
        Year = job.Year,
        ExpiryUsers = job.ExpiryUsers,
        SearchenginKeywords = job.SearchenginKeywords,
        SearchengineDescription = job.SearchengineDescription,
        // SuperUser-only
        JobDescription = isSuperUser ? job.JobDescription : null,
        AdnInvoicePrefix = isSuperUser ? $"{job.Customer?.CustomerAi}_{job.JobAi}" : null,
        JobNameQbp = isSuperUser ? job.JobNameQbp : null,
        ExpiryAdmin = isSuperUser ? job.ExpiryAdmin : null,
        JobTypeId = isSuperUser ? job.JobTypeId : null,
        SportId = isSuperUser ? job.SportId : null,
        CustomerId = isSuperUser ? job.CustomerId : null,
        BillingTypeId = isSuperUser ? job.BillingTypeId : null,
        JobCode = isSuperUser ? job.JobCode : null,
    };

    private static JobConfigPaymentDto MapPayment(Jobs job, bool isSuperUser, List<JobAdminCharges>? charges) => new()
    {
        PaymentMethodsAllowedCode = job.PaymentMethodsAllowedCode,
        BAddProcessingFees = job.BAddProcessingFees,
        ProcessingFeePercent = job.ProcessingFeePercent,
        MinProcessingFeePercent = FeeConstants.MinProcessingFeePercent,
        MaxProcessingFeePercent = FeeConstants.MaxProcessingFeePercent,
        BEnableEcheck = job.BEnableEcheck,
        EcprocessingFeePercent = job.EcprocessingFeePercent,
        MinEcprocessingFeePercent = FeeConstants.MinEcprocessingFeePercent,
        MaxEcprocessingFeePercent = FeeConstants.MaxEcprocessingFeePercent,
        BApplyProcessingFeesToTeamDeposit = job.BApplyProcessingFeesToTeamDeposit,
        PayTo = job.PayTo,
        MailTo = job.MailTo,
        MailinPaymentWarning = job.MailinPaymentWarning,
        Balancedueaspercent = job.Balancedueaspercent,
        BTeamsFullPaymentRequired = job.BTeamsFullPaymentRequired,
        BPlayersFullPaymentRequired = job.BPlayersFullPaymentRequired,
        BIncludePlayerDonation = job.BIncludePlayerDonation,
        BIncludeTeamDonation = job.BIncludeTeamDonation,
        BAllowRefundsInPriorMonths = job.BAllowRefundsInPriorMonths,
        BAllowCreditAll = job.BAllowCreditAll,
        // SuperUser-only — per-unit charges
        PerPlayerCharge = isSuperUser ? job.PerPlayerCharge : null,
        PerTeamCharge = isSuperUser ? job.PerTeamCharge : null,
        PerMonthCharge = isSuperUser ? job.PerMonthCharge : null,
        // SuperUser-only — ARB
        AdnArb = isSuperUser ? job.AdnArb : null,
        AdnArbBillingOccurrences = isSuperUser ? job.AdnArbbillingOccurences : null,
        AdnArbIntervalLength = isSuperUser ? job.AdnArbintervalLength : null,
        AdnArbStartDate = isSuperUser ? job.AdnArbstartDate : null,
        AdnArbMinimumTotalCharge = isSuperUser ? job.AdnArbMinimunTotalCharge : null,
        AdnArbTrial = isSuperUser ? job.AdnArbtrial : null,
        AdnStartDateAfterTrial = isSuperUser ? job.AdnStartDateAfterTrial : null,
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
        BPlayerRegRequiresToken = job.BplayerRegRequiresToken,
        RegformNamePlayer = job.RegformNamePlayer,
        PlayerRegConfirmationEmail = job.PlayerRegConfirmationEmail,
        PlayerRegConfirmationOnScreen = job.PlayerRegConfirmationOnScreen,
        PlayerRegRefundPolicy = job.PlayerRegRefundPolicy,
        PlayerRegReleaseOfLiability = job.PlayerRegReleaseOfLiability,
        PlayerRegCodeOfConduct = job.PlayerRegCodeOfConduct,
        PlayerRegCovid19Waiver = job.PlayerRegCovid19Waiver,
        UslaxNumberValidThroughDate = job.UslaxNumberValidThroughDate,
        // SuperUser-only
        CoreRegformPlayer = isSuperUser ? job.CoreRegformPlayer : null,
        BOfferPlayerRegsaverInsurance = isSuperUser ? job.BOfferPlayerRegsaverInsurance : null,
        MomLabel = isSuperUser ? job.MomLabel : null,
        DadLabel = isSuperUser ? job.DadLabel : null,
        PlayerProfileMetadataJson = isSuperUser ? job.PlayerProfileMetadataJson : null,
    };

    private static JobConfigTeamsDto MapTeams(Jobs job, bool isSuperUser) => new()
    {
        BRegistrationAllowTeam = job.BRegistrationAllowTeam,
        BTeamRegRequiresToken = job.BteamRegRequiresToken,
        RegformNameTeam = job.RegformNameTeam,
        RegformNameClubRep = job.RegformNameClubRep,
        BClubRepAllowEdit = job.BClubRepAllowEdit,
        BClubRepAllowDelete = job.BClubRepAllowDelete,
        BClubRepAllowAdd = job.BClubRepAllowAdd,
        BRestrictPlayerTeamsToAgerange = job.BRestrictPlayerTeamsToAgerange,
        BTeamPushDirectors = job.BTeamPushDirectors,
        BAllowRosterViewAdult = job.BAllowRosterViewAdult,
        BAllowRosterViewPlayer = job.BAllowRosterViewPlayer,
        // SuperUser-only
        BOfferTeamRegsaverInsurance = isSuperUser ? job.BOfferTeamRegsaverInsurance : null,
    };

    // The selectable coach-form templates, single-sourced from the catalog (no FE copy).
    private static readonly IReadOnlyList<AdultCoachProfileOptionDto> s_adultCoachProfileOptions =
        AdultFormCatalog.AllProfiles
            .Select(p => new AdultCoachProfileOptionDto
            {
                Code = p,
                Name = AdultFormCatalog.DisplayName(p),
                CanRequireUsLax = AdultFormCatalog.CanRequireUsLax(p)
            })
            .ToList();

    private static JobConfigCoachesDto MapCoaches(Jobs job)
    {
        // Derive the coach-form identity from the legacy string — the single source of truth.
        var (profile, requiresUsLax) = AdultFormCatalog.MapLegacy(job.RegformNameCoach);
        return new()
        {
            BRegistrationAllowStaff = job.BRegistrationAllowStaff,
            BRegistrationAllowReferee = job.BRegistrationAllowReferee,
            BRegistrationAllowRecruiter = job.BRegistrationAllowRecruiter,
            AdultCoachProfileCode = profile,
            AdultCoachProfileName = AdultFormCatalog.DisplayName(profile),
            AdultCoachRequiresUsLax = requiresUsLax,
            AvailableAdultCoachProfiles = s_adultCoachProfileOptions,
            AdultRegConfirmationEmail = job.AdultRegConfirmationEmail,
            AdultRegConfirmationOnScreen = job.AdultRegConfirmationOnScreen,
            AdultRegRefundPolicy = job.AdultRegRefundPolicy,
            AdultRegReleaseOfLiability = job.AdultRegReleaseOfLiability,
            AdultRegCodeOfConduct = job.AdultRegCodeOfConduct,
            RefereeRegConfirmationEmail = job.RefereeRegConfirmationEmail,
            RefereeRegConfirmationOnScreen = job.RefereeRegConfirmationOnScreen,
            RecruiterRegConfirmationEmail = job.RecruiterRegConfirmationEmail,
            RecruiterRegConfirmationOnScreen = job.RecruiterRegConfirmationOnScreen,
        };
    }

    private static JobConfigSchedulingDto MapScheduling(Jobs job, GameClockParams? gcp, bool isSuperUser) => new()
    {
        EventStartDate = job.EventStartDate,
        EventEndDate = job.EventEndDate,
        BScheduleAllowPublicAccess = job.BScheduleAllowPublicAccess,
        BRestrictPublicRosters = job.BRestrictPublicRosters,
        BShowTeamNameOnlyInSchedules = job.BShowTeamNameOnlyInSchedules,
        // SuperUser-only
        BReseedTournament = isSuperUser ? job.BReseedTournament : null,
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
        BSuspendPublic = job.BSuspendPublic,
        BEnableTsicteams = job.BEnableTsicteams,
        BEnableMobileRsvp = job.BEnableMobileRsvp,
        BEnableMobileTeamChat = job.BEnableMobileTeamChat,
        BAllowMobileLogin = job.BAllowMobileLogin,
        BAllowMobileRegn = job.BAllowMobileRegn,
        MobileScoreHoursPastGameEligible = job.MobileScoreHoursPastGameEligible,
        // SuperUser-only
        MobileJobName = isSuperUser ? job.MobileJobName : null,
        BEnableStore = isSuperUser ? job.BEnableStore : null,
        BAllowStoreWalkup = isSuperUser ? job.BAllowStoreWalkup : null,
        BenableStp = isSuperUser ? job.BenableStp : null,
        StoreContactEmail = isSuperUser ? job.StoreContactEmail : null,
        StoreRefundPolicy = isSuperUser ? job.StoreRefundPolicy : null,
        StorePickupDetails = isSuperUser ? job.StorePickupDetails : null,
        StoreSalesTax = isSuperUser ? job.StoreSalesTax : null,
        StoreTsicrate = isSuperUser ? job.StoreTsicrate : null,
    };
}
