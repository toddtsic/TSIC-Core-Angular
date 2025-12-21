namespace TSIC.Contracts.Dtos;

public class RegistrationStatusRequest
{
    public required string JobPath { get; set; }
    public required string[] RegistrationTypes { get; set; }
}
