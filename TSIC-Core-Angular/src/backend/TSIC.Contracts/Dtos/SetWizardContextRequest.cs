namespace TSIC.Contracts.Dtos;

/// <summary>
/// Request to set job context for player registration wizard (Phase 1 → job-scoped token upgrade)
/// </summary>
public record SetWizardContextRequest
{
    public required string JobPath { get; init; }
}
