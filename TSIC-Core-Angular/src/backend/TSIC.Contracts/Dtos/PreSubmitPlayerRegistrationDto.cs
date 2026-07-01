using System.Text.Json;
using TSIC.Contracts.Dtos.VerticalInsure;

namespace TSIC.Contracts.Dtos
{
    public record PreSubmitPlayerRegistrationRequestDto
    {
        public required string JobPath { get; init; } = string.Empty;
        public required List<PreSubmitTeamSelectionDto> TeamSelections { get; init; } = new();
    }

    public record PreSubmitTeamSelectionDto
    {
        public required string PlayerId { get; init; } = string.Empty;
        public required Guid TeamId { get; init; }
        // Optional: All form field values for this player (names should align to Registrations property names or metadata dbColumn)
        public Dictionary<string, JsonElement>? FormValues { get; init; }
    }

    public record PreSubmitPlayerRegistrationResponseDto
    {
        public required List<PreSubmitTeamResultDto> TeamResults { get; init; } = new();
        // Use List.Exists for style compliance (avoid LINQ Any in this simple predicate)
        public bool HasFullTeams => TeamResults.Exists(r => r.IsFull);
        public required string NextTab { get; init; } = ""; // "Team" or "Forms"
        // Optional: Insurance offer snapshot built post-creation of pending registrations
        public required PreSubmitInsuranceDto? Insurance { get; init; }
        // Optional: Client-side forms validation errors echoed / enforced server-side
        // Each entry: playerId, field, message. Presence indicates Forms step should not advance.
        public required List<PreSubmitValidationErrorDto>? ValidationErrors { get; init; }
        // Raw team snapshot (post-reconcile, current occupancy) so the wizard re-reflects the
        // agegroup options, team list, and defaults instead of running on stale init-time data.
        public required List<AvailableTeamDto>? RawTeams { get; init; }
        // Players auto-switched to a WAITLIST twin because their real team filled up. Surfaced
        // as a notice; empty when nothing moved.
        public required List<PaymentWaitlistedDto>? MovedToWaitlist { get; init; }
    }

    public record PreSubmitTeamResultDto
    {
        public required string PlayerId { get; init; } = string.Empty;
        public required Guid TeamId { get; init; }
        public required bool IsFull { get; init; }
        public required string TeamName { get; init; } = string.Empty;
        public required string Message { get; init; } = string.Empty;
        public required bool RegistrationCreated { get; init; }
    }

    public record PreSubmitInsuranceDto
    {
        public required bool Available { get; init; }
        public VIPlayerObjectResponse? PlayerObject { get; init; }
        public string? Error { get; init; }
        public DateTime? ExpiresUtc { get; init; }
        public string? StateId { get; init; }

        /// <summary>True iff this job's merchant account accepts AMEX (see IJobPaymentFeaturesService).
        /// Gates whether the VI-update payment step offers AMEX as a card type. Fail-closed false.</summary>
        public bool JobUsesAmex { get; init; }
    }

    public record PreSubmitValidationErrorDto
    {
        public required string PlayerId { get; init; } = string.Empty;
        public required string Field { get; init; } = string.Empty;
        public required string Message { get; init; } = string.Empty;
    }

}
