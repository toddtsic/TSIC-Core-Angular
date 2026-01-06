namespace TSIC.Contracts.Dtos
{
    /// <summary>
    /// Request to add an additional club to an existing ClubRep user
    /// </summary>
    public record AddClubRequest
    {
        public required string ClubName { get; init; }
        public string? State { get; init; }  // Optional - used for fuzzy matching if provided
        public int? UseExistingClubId { get; init; }  // If user confirms they want to use existing club
    }
}
