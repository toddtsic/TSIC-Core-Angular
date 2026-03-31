namespace TSIC.Contracts.Dtos;

public record ChatMessageDto
{
    public required Guid MessageId { get; init; }
    public required string Message { get; init; }
    public required Guid TeamId { get; init; }
    public required string CreatorUserId { get; init; }
    public required DateTime Created { get; init; }
    public string? CreatedBy { get; init; }
    public bool MyMessage { get; init; }
}

public record GetChatMessagesRequest
{
    public required Guid TeamId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int RowsPerPage { get; init; } = 50;
}

public record GetChatMessagesResponse
{
    public required List<ChatMessageDto> Messages { get; init; }
    public required bool IncludesAll { get; init; }
}
