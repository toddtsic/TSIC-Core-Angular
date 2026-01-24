using System.Text.Json;
using TSIC.Contracts.Dtos.VerticalInsure;

namespace TSIC.Contracts.Dtos
{
    public class PreSubmitPlayerRegistrationRequestDto
    {
        public required string JobPath { get; set; } = string.Empty;
        public required List<PreSubmitTeamSelectionDto> TeamSelections { get; set; } = new();
    }

    public class PreSubmitTeamSelectionDto
    {
        public required string PlayerId { get; set; } = string.Empty;
        public required Guid TeamId { get; set; }
        // Optional: All form field values for this player (names should align to Registrations property names or metadata dbColumn)
        public Dictionary<string, JsonElement>? FormValues { get; set; }
    }

    public class PreSubmitPlayerRegistrationResponseDto
    {
        public required List<PreSubmitTeamResultDto> TeamResults { get; set; } = new();
        // Use List.Exists for style compliance (avoid LINQ Any in this simple predicate)
        public bool HasFullTeams => TeamResults.Exists(r => r.IsFull);
        public required string NextTab { get; set; } = ""; // "Team" or "Forms"
        // Optional: Insurance offer snapshot built post-creation of pending registrations
        public PreSubmitInsuranceDto? Insurance { get; set; }
        // Optional: Client-side forms validation errors echoed / enforced server-side
        // Each entry: playerId, field, message. Presence indicates Forms step should not advance.
        public List<PreSubmitValidationErrorDto>? ValidationErrors { get; set; }
    }

    public class PreSubmitTeamResultDto
    {
        public required string PlayerId { get; set; } = string.Empty;
        public required Guid TeamId { get; set; }
        public required bool IsFull { get; set; }
        public required string TeamName { get; set; } = string.Empty;
        public required string Message { get; set; } = string.Empty;
        public required bool RegistrationCreated { get; set; }
    }

    public class PreSubmitInsuranceDto
    {
        public required bool Available { get; set; }
        public VIPlayerObjectResponse? PlayerObject { get; set; }
        public string? Error { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public string? StateId { get; set; }
    }

    public class PreSubmitValidationErrorDto
    {
        public required string PlayerId { get; set; } = string.Empty;
        public required string Field { get; set; } = string.Empty;
        public required string Message { get; set; } = string.Empty;
    }
}
