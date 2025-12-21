namespace TSIC.Contracts.Dtos;

public class RegistrationStatusResponse
{
    public required string RegistrationType { get; set; }
    public required bool IsAvailable { get; set; }
    public string? Message { get; set; }
    public string? RegistrationUrl { get; set; }
}
