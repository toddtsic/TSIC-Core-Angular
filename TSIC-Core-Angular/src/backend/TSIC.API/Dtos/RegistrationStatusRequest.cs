namespace TSIC.API.DTOs;

public class RegistrationStatusRequest
{
    public required string JobPath { get; set; }
    public required string[] RegistrationTypes { get; set; }
}
