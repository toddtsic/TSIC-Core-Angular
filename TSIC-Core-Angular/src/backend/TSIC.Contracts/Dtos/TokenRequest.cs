namespace TSIC.Contracts.Dtos
{
    public record TokenRequest
    {
        public required string Username { get; init; }
        public required string RegId { get; init; }
    }
}
