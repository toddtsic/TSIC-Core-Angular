using System.Text.Json;

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
}
