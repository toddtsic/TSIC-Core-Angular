using System.Text.Json;

namespace TSIC.API.Services;

/// <summary>
/// Handles metadata parsing and property mapping for player registrations.
/// Extracted from PlayerRegistrationService for reusability and testability.
/// </summary>
public interface IPlayerRegistrationMetadataService
{
    /// <summary>
    /// Determines registration mode (PP or CAC) from CoreRegformPlayer and JsonOptions.
    /// </summary>
    string GetRegistrationMode(string? coreRegformPlayer, string? jsonOptions);

    /// <summary>
    /// Builds a map from field names to database property names from job metadata JSON.
    /// Excludes hidden and adminOnly fields.
    /// </summary>
    Dictionary<string, string> BuildFieldNameToPropertyMap(string? metadataJson);

    /// <summary>
    /// Builds a map of writable properties on the Registrations entity.
    /// Filters out navigation properties, collections, and excluded fields.
    /// </summary>
    Dictionary<string, System.Reflection.PropertyInfo> BuildWritablePropertyMap();

    /// <summary>
    /// Case-insensitive property lookup on JsonElement objects.
    /// </summary>
    bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value);
}
