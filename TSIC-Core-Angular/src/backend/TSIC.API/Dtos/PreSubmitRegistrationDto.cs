using System.Text.Json;
using TSIC.API.Dtos.VerticalInsure;

namespace TSIC.API.Dtos
{
    public class PreSubmitRegistrationRequestDto
    {
        public string JobPath { get; set; } = string.Empty;
        public string FamilyUserId { get; set; } = string.Empty;
        public List<PreSubmitTeamSelectionDto> TeamSelections { get; set; } = new();
    }

    public class PreSubmitTeamSelectionDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public Guid TeamId { get; set; }
        // Optional: All form field values for this player (names should align to Registrations property names or metadata dbColumn)
        public Dictionary<string, JsonElement>? FormValues { get; set; }
    }

    public class PreSubmitRegistrationResponseDto
    {
        public List<PreSubmitTeamResultDto> TeamResults { get; set; } = new();
        // Use List.Exists for style compliance (avoid LINQ Any in this simple predicate)
        public bool HasFullTeams => TeamResults.Exists(r => r.IsFull);
        public string NextTab { get; set; } = ""; // "Team" or "Forms"
        // Optional: Insurance offer snapshot built post-creation of pending registrations
        public PreSubmitInsuranceDto? Insurance { get; set; }
        // Optional: Client-side forms validation errors echoed / enforced server-side
        // Each entry: playerId, field, message. Presence indicates Forms step should not advance.
        public List<PreSubmitValidationErrorDto>? ValidationErrors { get; set; }
    }

    public class PreSubmitTeamResultDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public Guid TeamId { get; set; }
        public bool IsFull { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool RegistrationCreated { get; set; }
    }

    public class PreSubmitInsuranceDto
    {
        public bool Available { get; set; }
        public VIPlayerObjectResponse? PlayerObject { get; set; }
        public string? Error { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public string? StateId { get; set; }
    }

    public class PreSubmitValidationErrorDto
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
