namespace TSIC.Contracts.Dtos;

public class JobMetadataResponse
{
    public required Guid JobId { get; set; }
    public required string JobName { get; set; }
    public required string JobPath { get; set; }
    public string? JobLogoPath { get; set; }
    public string? JobBannerPath { get; set; }
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
    // Payment schedule (ALLOWPIF removed; UI derives options from scenarios)
    public bool? AdnArb { get; set; }
    public int? AdnArbBillingOccurences { get; set; }
    public int? AdnArbIntervalLength { get; set; }
    public DateTime? AdnArbStartDate { get; set; }
}
