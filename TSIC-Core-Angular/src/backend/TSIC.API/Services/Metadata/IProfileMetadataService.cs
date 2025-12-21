using System.Text.Json;
using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Metadata;

public interface IProfileMetadataService
{
    ParsedProfileMetadata Parse(string? metadataJson, string? jsonOptions);
    string? ResolveConstraintType(string? coreRegformPlayer);
    JobRegFormDto BuildJobRegForm(Guid jobId, ParsedProfileMetadata parsed, string? coreRegformPlayer, string? metadataJson, string? jsonOptions);
}

public sealed class ParsedProfileMetadata
{
    public List<(string Name, string DbColumn)> MappedFields { get; set; } = new();
    public List<ProfileMetadataField> TypedFields { get; set; } = new();
    public List<string> WaiverFieldNames { get; set; } = new();
    public HashSet<string> VisibleFieldNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
