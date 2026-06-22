using System.Text.Json;
using System.Text.Json.Serialization;

namespace TSIC.Domain.Adults;

/// <summary>
/// Structured codification of an UnassignedAdult (coach) registration's non-binding
/// team REQUESTS, stored as JSON in <c>Registrations.SpecialRequests</c>. The single
/// coach registration carries a 1-to-many list of requested team ids plus the coach's
/// free-text note. A director approves each requested team via the Roster Swapper,
/// which mints the per-team Staff row; approval status is DERIVED (a Staff row exists
/// for that team), never stored here.
/// </summary>
public sealed class AdultTeamRequestData
{
    [JsonPropertyName("requestedTeamIds")]
    public List<Guid> RequestedTeamIds { get; set; } = new();

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    private static readonly JsonSerializerOptions _opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(AdultTeamRequestData data) =>
        JsonSerializer.Serialize(data, _opts);

    /// <summary>
    /// Tolerant parse of <c>Registrations.SpecialRequests</c>:
    /// <list type="bullet">
    /// <item>null/empty → empty data</item>
    /// <item>a JSON object in our shape → structured requests + note</item>
    /// <item>any other value (legacy free-text, e.g. "Requested teams: …") → treated as
    ///   the note with no structured requests, so old rows render gracefully</item>
    /// </list>
    /// </summary>
    public static AdultTeamRequestData Parse(string? specialRequests)
    {
        var raw = specialRequests?.Trim();
        if (string.IsNullOrEmpty(raw)) return new AdultTeamRequestData();

        if (raw.StartsWith('{'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<AdultTeamRequestData>(raw, _opts);
                if (parsed != null)
                {
                    parsed.RequestedTeamIds ??= new();
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Not our JSON after all — fall through to legacy handling.
            }
        }

        // Legacy free-text → preserve as the human note; no structured requests.
        return new AdultTeamRequestData { Note = raw };
    }
}
