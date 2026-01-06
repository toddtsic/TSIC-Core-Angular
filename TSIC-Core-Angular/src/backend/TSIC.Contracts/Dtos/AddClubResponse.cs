namespace TSIC.Contracts.Dtos
{
    /// <summary>
    /// Response from adding an additional club to an existing ClubRep user
    /// </summary>
    public record AddClubResponse
    {
        public required bool Success { get; init; }
        public required string Message { get; init; }
        public int? ClubRepId { get; init; }
        public int? ClubId { get; init; }
        public List<ClubSearchResult>? SimilarClubs { get; init; }
    }
}
