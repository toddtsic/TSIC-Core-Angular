using TSIC.Domain.Entities;

namespace TSIC.Domain.Helpers;

/// <summary>
/// Single source of truth for a field's display address. Used by the schedule read paths
/// (games grid, team results, consolation) and by filter-options so the string can never drift.
/// </summary>
public static class FieldAddressFormatter
{
    /// <summary>Concatenates a field's address parts into a Google Maps-friendly string, or null.</summary>
    public static string? Build(Fields? field)
    {
        if (field == null) return null;
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(field.Address)) parts.Add(field.Address.Trim());
        if (!string.IsNullOrWhiteSpace(field.City)) parts.Add(field.City.Trim());
        var stateZip = string.Join(" ",
            new[] { field.State?.Trim(), field.Zip?.Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (stateZip.Length > 0) parts.Add(stateZip);
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}
