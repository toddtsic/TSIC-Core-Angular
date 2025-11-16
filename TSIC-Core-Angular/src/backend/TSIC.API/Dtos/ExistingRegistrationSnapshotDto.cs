using System.Text.Json.Serialization;

namespace TSIC.API.DTOs;

public sealed class ExistingRegistrationSnapshotDto
{
    [JsonPropertyName("teams")]
    public Dictionary<string, object> Teams { get; init; } = new();

    [JsonPropertyName("values")]
    public Dictionary<string, Dictionary<string, object?>> Values { get; init; } = new();
}
