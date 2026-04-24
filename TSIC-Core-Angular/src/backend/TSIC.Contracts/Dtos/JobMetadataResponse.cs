namespace TSIC.Contracts.Dtos;

public class JobMetadataResponse
{
    public required Guid JobId { get; set; }
    public required string JobName { get; set; }
    public required string JobPath { get; set; }
    public string? JobLogoPath { get; set; }
    public string? JobBannerPath { get; set; }
    public string? JobBannerText1 { get; set; }
    public string? JobBannerText2 { get; set; }
    public string? JobBannerBackgroundPath { get; set; }
    public bool? CoreRegformPlayer { get; set; }
    public DateTime? USLaxNumberValidThroughDate { get; set; }
    public DateTime? ExpiryUsers { get; set; }
    public string? PlayerProfileMetadataJson { get; set; }
    public string? JsonOptions { get; set; }
    public string? MomLabel { get; set; }
    public string? DadLabel { get; set; }
    // Waiver / registration policy HTML blocks
    public string? PlayerRegReleaseOfLiability { get; set; }
    public string? PlayerRegCodeOfConduct { get; set; }
    public string? PlayerRegCovid19Waiver { get; set; }
    public string? PlayerRegRefundPolicy { get; set; }
    public bool OfferPlayerRegsaverInsurance { get; set; }
    public bool OfferTeamRegsaverInsurance { get; set; }
    // Pay-In-Full gate: Jobs.CoreRegformPlayer contains "|ALLOWPIF".
    // When false, the checkout does not expose a Pay In Full option — the
    // player is locked into the configured phase (deposit or ARB schedule).
    public bool AllowPif { get; set; }
    // Payment schedule (ARB)
    public bool? AdnArb { get; set; }
    public int? AdnArbBillingOccurences { get; set; }
    public int? AdnArbIntervalLength { get; set; }
    public DateTime? AdnArbStartDate { get; set; }
    // Registration & availability flags
    public bool BRegistrationAllowPlayer { get; set; }
    public bool BRegistrationAllowTeam { get; set; }
    public bool BEnableStore { get; set; }
    public bool BScheduleAllowPublicAccess { get; set; }
    public bool BBannerIsCustom { get; set; }
    // Job type (e.g. "Players", "League", "Tournament")
    public string? JobTypeName { get; set; }
    /// <summary>Canonical job type discriminator. Matches TSIC.Domain.Constants.JobConstants.</summary>
    public required int JobTypeId { get; set; }
    /// <summary>Sport name from Jobs.Sports.SportName (e.g. "Lacrosse", "Basketball"). Used for icon mapping on the client.</summary>
    public string? SportName { get; set; }
    // Registration mode: "PP" (Player Profile, 1-to-1) or "CAC" (Camps & Clinics, 1-to-many)
    public string RegistrationMode { get; set; } = "PP";
    // Payment method restrictions (1=CC only, 2=CC or Check, 3=Check only)
    public int PaymentMethodsAllowedCode { get; set; }
    public bool BAddProcessingFees { get; set; }
    public string? PayTo { get; set; }
    public string? MailTo { get; set; }
    public string? MailinPaymentWarning { get; set; }
}
