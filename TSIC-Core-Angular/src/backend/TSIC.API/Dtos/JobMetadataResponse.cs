namespace TSIC.API.DTOs;

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
}
