using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for managing job configuration (SuperUser editor).
/// Maps UpdateJobConfigRequest fields to the Jobs entity.
/// </summary>
public class JobConfigService : IJobConfigService
{
    private readonly IJobRepository _jobRepo;

    public JobConfigService(IJobRepository jobRepo)
    {
        _jobRepo = jobRepo;
    }

    public async Task<JobConfigDto?> GetConfigAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _jobRepo.GetJobConfigAsync(jobId, ct);
    }

    public async Task<JobConfigLookupsDto> GetLookupsAsync(CancellationToken ct = default)
    {
        return await _jobRepo.GetJobConfigLookupsAsync(ct);
    }

    public async Task<JobConfigDto?> UpdateConfigAsync(
        Guid jobId, UpdateJobConfigRequest req, string userId, CancellationToken ct = default)
    {
        var success = await _jobRepo.UpdateJobConfigAsync(jobId, req.UpdatedOn, job =>
        {
            // General
            job.JobName = req.JobName;
            job.DisplayName = req.DisplayName;
            job.JobDescription = req.JobDescription;
            job.JobTagline = req.JobTagline;
            job.JobCode = req.JobCode;
            job.Year = req.Year;
            job.Season = req.Season;
            job.JobTypeId = req.JobTypeId;
            job.SportId = req.SportId;
            job.BillingTypeId = req.BillingTypeId;
            job.ExpiryAdmin = req.ExpiryAdmin;
            job.ExpiryUsers = req.ExpiryUsers;
            job.EventStartDate = req.EventStartDate;
            job.EventEndDate = req.EventEndDate;
            job.SearchenginKeywords = req.SearchenginKeywords;
            job.SearchengineDescription = req.SearchengineDescription;
            job.BBannerIsCustom = req.BBannerIsCustom;
            job.BannerFile = req.BannerFile;
            job.MobileJobName = req.MobileJobName;
            job.JobNameQbp = req.JobNameQbp;
            job.MomLabel = req.MomLabel;
            job.DadLabel = req.DadLabel;
            job.BSuspendPublic = req.BSuspendPublic;

            // Registration
            job.BRegistrationAllowPlayer = req.BRegistrationAllowPlayer;
            job.BRegistrationAllowTeam = req.BRegistrationAllowTeam;
            job.BAllowMobileRegn = req.BAllowMobileRegn;
            job.BUseWaitlists = req.BUseWaitlists;
            job.BRestrictPlayerTeamsToAgerange = req.BRestrictPlayerTeamsToAgerange;
            job.BOfferPlayerRegsaverInsurance = req.BOfferPlayerRegsaverInsurance;
            job.BOfferTeamRegsaverInsurance = req.BOfferTeamRegsaverInsurance;
            job.PlayerRegMultiPlayerDiscountMin = req.PlayerRegMultiPlayerDiscountMin;
            job.PlayerRegMultiPlayerDiscountPercent = req.PlayerRegMultiPlayerDiscountPercent;
            job.CoreRegformPlayer = req.CoreRegformPlayer;
            job.RegformNamePlayer = req.RegformNamePlayer;
            job.RegformNameTeam = req.RegformNameTeam;
            job.RegformNameCoach = req.RegformNameCoach;
            job.RegformNameClubRep = req.RegformNameClubRep;
            job.PlayerProfileMetadataJson = req.PlayerProfileMetadataJson;
            job.UslaxNumberValidThroughDate = req.UslaxNumberValidThroughDate;

            // Payment
            job.PaymentMethodsAllowedCode = req.PaymentMethodsAllowedCode;
            job.BAddProcessingFees = req.BAddProcessingFees;
            job.ProcessingFeePercent = req.ProcessingFeePercent;
            job.BApplyProcessingFeesToTeamDeposit = req.BApplyProcessingFeesToTeamDeposit;
            job.BTeamsFullPaymentRequired = req.BTeamsFullPaymentRequired;
            job.Balancedueaspercent = req.Balancedueaspercent;
            job.BAllowRefundsInPriorMonths = req.BAllowRefundsInPriorMonths;
            job.BAllowCreditAll = req.BAllowCreditAll;
            job.PayTo = req.PayTo;
            job.MailTo = req.MailTo;
            job.MailinPaymentWarning = req.MailinPaymentWarning;
            job.AdnArb = req.AdnArb;
            job.AdnArbbillingOccurences = req.AdnArbbillingOccurences;
            job.AdnArbintervalLength = req.AdnArbintervalLength;
            job.AdnArbstartDate = req.AdnArbstartDate;
            job.AdnArbMinimunTotalCharge = req.AdnArbMinimunTotalCharge;

            // Email & Templates
            job.RegFormFrom = req.RegFormFrom;
            job.RegFormCcs = req.RegFormCcs;
            job.RegFormBccs = req.RegFormBccs;
            job.Rescheduleemaillist = req.Rescheduleemaillist;
            job.Alwayscopyemaillist = req.Alwayscopyemaillist;
            job.BDisallowCcplayerConfirmations = req.BDisallowCcplayerConfirmations;
            job.PlayerRegConfirmationEmail = req.PlayerRegConfirmationEmail;
            job.PlayerRegConfirmationOnScreen = req.PlayerRegConfirmationOnScreen;
            job.PlayerRegRefundPolicy = req.PlayerRegRefundPolicy;
            job.PlayerRegReleaseOfLiability = req.PlayerRegReleaseOfLiability;
            job.PlayerRegCodeOfConduct = req.PlayerRegCodeOfConduct;
            job.PlayerRegCovid19Waiver = req.PlayerRegCovid19Waiver;
            job.AdultRegConfirmationEmail = req.AdultRegConfirmationEmail;
            job.AdultRegConfirmationOnScreen = req.AdultRegConfirmationOnScreen;
            job.AdultRegRefundPolicy = req.AdultRegRefundPolicy;
            job.AdultRegReleaseOfLiability = req.AdultRegReleaseOfLiability;
            job.AdultRegCodeOfConduct = req.AdultRegCodeOfConduct;
            job.RefereeRegConfirmationEmail = req.RefereeRegConfirmationEmail;
            job.RefereeRegConfirmationOnScreen = req.RefereeRegConfirmationOnScreen;
            job.RecruiterRegConfirmationEmail = req.RecruiterRegConfirmationEmail;
            job.RecruiterRegConfirmationOnScreen = req.RecruiterRegConfirmationOnScreen;

            // Features & Store
            job.BClubRepAllowEdit = req.BClubRepAllowEdit;
            job.BClubRepAllowDelete = req.BClubRepAllowDelete;
            job.BClubRepAllowAdd = req.BClubRepAllowAdd;
            job.BAllowMobileLogin = req.BAllowMobileLogin;
            job.BAllowRosterViewAdult = req.BAllowRosterViewAdult;
            job.BAllowRosterViewPlayer = req.BAllowRosterViewPlayer;
            job.BShowTeamNameOnlyInSchedules = req.BShowTeamNameOnlyInSchedules;
            job.BScheduleAllowPublicAccess = req.BScheduleAllowPublicAccess;
            job.BTeamPushDirectors = req.BTeamPushDirectors;
            job.BEnableTsicteams = req.BEnableTsicteams;
            job.BEnableMobileRsvp = req.BEnableMobileRsvp;
            job.BEnableMobileTeamChat = req.BEnableMobileTeamChat;
            job.BenableStp = req.BenableStp;
            job.BEnableStore = req.BEnableStore;
            job.BSignalRschedule = req.BSignalRschedule;
            job.MobileScoreHoursPastGameEligible = req.MobileScoreHoursPastGameEligible;
            job.StoreSalesTax = req.StoreSalesTax;
            job.StoreRefundPolicy = req.StoreRefundPolicy;
            job.StorePickupDetails = req.StorePickupDetails;
            job.StoreContactEmail = req.StoreContactEmail;
            job.StoreTsicrate = req.StoreTsicrate;

            // Audit
            job.LebUserId = userId;
        }, ct);

        if (!success)
            return null; // concurrency conflict

        // Re-read to get updated rowversion
        return await _jobRepo.GetJobConfigAsync(jobId, ct);
    }
}
