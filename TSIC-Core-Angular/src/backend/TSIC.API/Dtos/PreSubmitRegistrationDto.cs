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
    }

    public class PreSubmitRegistrationResponseDto
    {
        public List<PreSubmitTeamResultDto> TeamResults { get; set; } = new();
        public bool HasFullTeams => TeamResults.Any(r => r.IsFull);
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
