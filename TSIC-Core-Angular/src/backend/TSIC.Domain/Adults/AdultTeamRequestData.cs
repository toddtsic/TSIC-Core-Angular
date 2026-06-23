using System.Text.Json;
using System.Text.Json.Serialization;

namespace TSIC.Domain.Adults;

/// <summary>Who put a team into a coach's record.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AdultTeamRequestSource>))]
public enum AdultTeamRequestSource
{
    /// <summary>The coach themselves requested it (registration pick).</summary>
    Self,
    /// <summary>A director added/granted it (Roster-Swapper grant, or the seed of pre-existing grants).</summary>
    Admin,
}

/// <summary>One team in a coach's append-only association record, tagged with its origin.</summary>
public sealed class AdultTeamRequest
{
    [JsonPropertyName("teamId")]
    public Guid TeamId { get; set; }

    [JsonPropertyName("src")]
    public AdultTeamRequestSource Src { get; set; }
}

/// <summary>
/// Structured codification of an UnassignedAdult (coach) registration's team association,
/// stored as JSON in <c>Registrations.SpecialRequests</c>.
///
/// The record is APPEND-ONLY and the source of truth for INTENT, never deleted or rewritten:
/// <list type="bullet">
/// <item>a coach's own registration picks append as <see cref="AdultTeamRequestSource.Self"/>;</item>
/// <item>a director granting a team appends it as <see cref="AdultTeamRequestSource.Admin"/>
///   (and the one-time seed codifies pre-existing grants the same way);</item>
/// <item>un-granting a team deletes the live Staff row but LEAVES the record entry.</item>
/// </list>
/// So the JSON answers "what was ever requested or granted"; the live Staff rows answer
/// "what is granted right now". The two are shown side by side and are allowed to diverge.
/// </summary>
public sealed class AdultTeamRequestData
{
    [JsonPropertyName("teams")]
    public List<AdultTeamRequest> Teams { get; set; } = new();

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>True when parsed from our JSON shape (new/legacy). False for free-text or empty.
    /// Not serialized — the seed (Build Rule) only fires when this is false.</summary>
    [JsonIgnore]
    public bool IsStructured { get; set; }

    /// <summary>Team ids the coach requested themselves (the intent signal). Back-compat accessor.</summary>
    [JsonIgnore]
    public IReadOnlyList<Guid> RequestedTeamIds =>
        Teams.Where(t => t.Src == AdultTeamRequestSource.Self).Select(t => t.TeamId).ToList();

    /// <summary>Every team in the record, regardless of origin.</summary>
    [JsonIgnore]
    public IReadOnlyList<Guid> AllTeamIds => Teams.Select(t => t.TeamId).ToList();

    /// <summary>
    /// Append a team to the record if not already present (dedup by team id; an existing
    /// <see cref="AdultTeamRequestSource.Self"/> entry is never downgraded to Admin). Returns
    /// true when the record actually changed.
    /// </summary>
    public bool AddTeam(Guid teamId, AdultTeamRequestSource src)
    {
        if (Teams.Any(t => t.TeamId == teamId)) return false;
        Teams.Add(new AdultTeamRequest { TeamId = teamId, Src = src });
        return true;
    }

    private static readonly JsonSerializerOptions _opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(AdultTeamRequestData data) =>
        JsonSerializer.Serialize(data, _opts);

    /// <summary>Tolerant parse of <c>Registrations.SpecialRequests</c>:
    /// <list type="bullet">
    /// <item>null/empty → empty record (not structured)</item>
    /// <item>JSON with <c>teams</c> → tagged record</item>
    /// <item>legacy JSON with <c>requestedTeamIds</c> → mapped to <see cref="AdultTeamRequestSource.Self"/></item>
    /// <item>any other text (legacy free-text) → kept as the note, not structured</item>
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
                var dto = JsonSerializer.Deserialize<ParseDto>(raw, _opts);
                if (dto != null)
                {
                    var teams = dto.Teams ?? new();
                    // Legacy shape: requestedTeamIds[] = the coach's own picks.
                    if (teams.Count == 0 && dto.RequestedTeamIds is { Count: > 0 })
                        teams = dto.RequestedTeamIds
                            .Select(id => new AdultTeamRequest { TeamId = id, Src = AdultTeamRequestSource.Self })
                            .ToList();

                    return new AdultTeamRequestData { Teams = teams, Note = dto.Note, IsStructured = true };
                }
            }
            catch (JsonException)
            {
                // Not our JSON after all — fall through to free-text handling.
            }
        }

        // Legacy free-text → preserve as the human note; no structured teams.
        return new AdultTeamRequestData { Note = raw };
    }

    /// <summary>Tolerant DTO accepting BOTH the v2 (<c>teams</c>) and legacy (<c>requestedTeamIds</c>) shapes.</summary>
    private sealed class ParseDto
    {
        [JsonPropertyName("teams")]
        public List<AdultTeamRequest>? Teams { get; set; }

        [JsonPropertyName("requestedTeamIds")]
        public List<Guid>? RequestedTeamIds { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }
}
