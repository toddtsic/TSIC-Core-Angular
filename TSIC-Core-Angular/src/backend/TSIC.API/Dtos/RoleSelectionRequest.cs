namespace TSIC.API.Dtos
{
    /// <summary>
    /// Phase 2: User selects a registration - username comes from JWT token claims
    /// </summary>
    public record RoleSelectionRequest(string RegId);
}
