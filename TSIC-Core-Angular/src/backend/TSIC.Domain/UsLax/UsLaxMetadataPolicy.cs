using System.Text.Json;

namespace TSIC.Domain.UsLax;

/// <summary>
/// Single source of truth for "does this profile form REQUIRE a USA Lacrosse membership number."
///
/// A field counts as the USLax field when its name is <c>sportAssnId</c>/<c>uslax</c>
/// (case-insensitive) OR its label mentions "lacrosse"; it is required when the field is directly
/// <c>required</c> or its <c>validation</c> block asserts <c>required</c>/<c>requiredTrue</c>.
/// Malformed/empty metadata → <c>false</c> (fail safe to not-required).
///
/// Lives in Domain so both the Infrastructure repositories (which compute
/// <c>Jobs.PlayerRegRequiresUsLax</c> on the job pulse) and the API registration services (which
/// decide whether to stamp <c>SportAssnIdexpDate</c> at submit) resolve the same predicate without
/// duplicating the JSON parse.
/// </summary>
public static class UsLaxMetadataPolicy
{
    public static bool RequiresUsLax(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var f in fields.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                var name = GetStringCI(f, "name") ?? string.Empty;
                var label = GetStringCI(f, "label") ?? GetStringCI(f, "displayName") ?? GetStringCI(f, "display") ?? string.Empty;
                var isUsLax = name.Equals("sportAssnId", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("uslax", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("lacrosse", StringComparison.OrdinalIgnoreCase);
                if (isUsLax && IsFieldRequired(f)) return true;
            }
        }
        catch (JsonException)
        {
            // Malformed metadata → no USLax requirement asserted.
        }
        return false;
    }

    private static bool IsFieldRequired(JsonElement f)
    {
        if (ReadBoolCI(f, "required")) return true;
        if (TryGetPropertyCI(f, "validation", out var val) && val.ValueKind == JsonValueKind.Object)
        {
            if (ReadBoolCI(val, "required")) return true;
            if (ReadBoolCI(val, "requiredTrue")) return true;
        }
        return false;
    }

    private static bool ReadBoolCI(JsonElement obj, string name)
    {
        if (!TryGetPropertyCI(obj, name, out var el)) return false;
        return el.ValueKind == JsonValueKind.True
            || (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b);
    }

    private static string? GetStringCI(JsonElement obj, string name)
        => TryGetPropertyCI(obj, name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        return false;
    }
}
