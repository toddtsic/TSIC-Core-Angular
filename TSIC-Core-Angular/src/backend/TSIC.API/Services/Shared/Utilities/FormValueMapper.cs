using System.Globalization;
using System.Reflection;
using System.Text.Json;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Shared.Utilities;

/// <summary>
/// Shared static utilities for mapping Dictionary&lt;string, JsonElement&gt; form values
/// to/from Registrations entity columns via reflection.
/// Used by both player and adult registration services.
/// </summary>
public static class FormValueMapper
{
    // ─── Write: Apply form values to a Registrations entity ──────────────

    /// <summary>
    /// Writes form values (from frontend) onto a Registrations entity using reflection.
    /// </summary>
    public static void ApplyFormValues(
        Registrations reg,
        Dictionary<string, JsonElement>? formValues,
        Dictionary<string, string> nameToProperty,
        Dictionary<string, PropertyInfo> writableProps)
    {
        if (formValues == null || formValues.Count == 0) return;
        foreach (var kvp in formValues)
        {
            var targetName = ResolveTargetPropertyName(kvp.Key, nameToProperty, writableProps);
            if (targetName == null) continue;
            if (!writableProps.TryGetValue(targetName, out var prop)) continue;
            if (TryConvertAndAssign(kvp.Value, prop.PropertyType, out var converted))
            {
                prop.SetValue(reg, converted);
            }
        }
    }

    // ─── Read: Extract form values from a Registrations entity ───────────

    /// <summary>
    /// Reads form values from a Registrations entity back into a dictionary (for sending to frontend).
    /// </summary>
    public static IReadOnlyDictionary<string, JsonElement> BuildFormValuesDictionary(
        Registrations reg,
        List<(string Name, string DbColumn)> mapped)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (mapped.Count == 0) return dict;

        var regType = typeof(Registrations);
        var excluded = new HashSet<string>(
        [
            nameof(Registrations.RegistrationId), nameof(Registrations.FamilyUserId),
            nameof(Registrations.UserId), nameof(Registrations.AssignedTeamId),
            nameof(Registrations.LebUserId),
            nameof(Registrations.FeeBase), nameof(Registrations.FeeProcessing),
            nameof(Registrations.FeeDiscount), nameof(Registrations.FeeDonation),
            nameof(Registrations.FeeLatefee), nameof(Registrations.FeeTotal),
            nameof(Registrations.OwedTotal), nameof(Registrations.PaidTotal),
            nameof(Registrations.Modified),
            "JsonOptions", "JsonFormValues"
        ], StringComparer.OrdinalIgnoreCase);

        foreach (var (name, dbCol) in mapped)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dbCol)) continue;
            var prop = regType.GetProperty(dbCol, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null || excluded.Contains(prop.Name)) continue;

            object? value = prop.GetValue(reg);
            if (value == null) continue;

            JsonElement cloned = value switch
            {
                DateTime dt => JsonDocument.Parse(JsonSerializer.Serialize(
                    dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd") : dt.ToString("O")
                )).RootElement.Clone(),
                DateTimeOffset dto => JsonDocument.Parse(JsonSerializer.Serialize(dto.ToString("O"))).RootElement.Clone(),
                _ => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone()
            };
            dict[name] = cloned;
        }
        return dict;
    }

    // ─── Metadata parsing: Build name→property maps ──────────────────────

    /// <summary>
    /// Parses flat metadata JSON ({"fields":[...]}) and builds a map of field name → DB column name.
    /// </summary>
    public static Dictionary<string, string> BuildFieldNameToPropertyMap(string? metadataJson)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(metadataJson)) return map;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
            {
                ExtractFieldMappings(fieldsEl, map);
            }
        }
        catch (Exception)
        {
            // Ignore malformed metadata and return empty map
        }
        return map;
    }

    /// <summary>
    /// Parses role-keyed metadata JSON ({"RoleName":{"fields":[...]}}) for a specific role.
    /// </summary>
    public static Dictionary<string, string> BuildFieldNameToPropertyMapForRole(string? roleKeyedMetadataJson, string roleKey)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(roleKeyedMetadataJson) || string.IsNullOrWhiteSpace(roleKey)) return map;
        try
        {
            using var doc = JsonDocument.Parse(roleKeyedMetadataJson);
            if (doc.RootElement.TryGetProperty(roleKey, out var roleEl) &&
                roleEl.TryGetProperty("fields", out var fieldsEl) &&
                fieldsEl.ValueKind == JsonValueKind.Array)
            {
                ExtractFieldMappings(fieldsEl, map);
            }
        }
        catch (Exception)
        {
            // Ignore malformed metadata and return empty map
        }
        return map;
    }

    /// <summary>
    /// Builds a map of writable Registrations property names → PropertyInfo, excluding system/fee/ID fields.
    /// </summary>
    public static Dictionary<string, PropertyInfo> BuildWritablePropertyMap()
    {
        var props = typeof(Registrations).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var dict = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            if (!p.CanWrite) continue;
            if (!ShouldIncludeProperty(p)) continue;
            if (string.Equals(p.Name, nameof(Registrations.AssignedTeamId), StringComparison.OrdinalIgnoreCase)) continue;
            dict[p.Name] = p;
        }
        return dict;
    }

    // ─── Private helpers ─────────────────────────────────────────────────

    private static void ExtractFieldMappings(JsonElement fieldsEl, Dictionary<string, string> map)
    {
        foreach (var f in fieldsEl.EnumerateArray())
        {
            if (TryExtractFieldMapping(f, out var name, out var dbCol))
            {
                map[name!] = dbCol!;
            }
        }
    }

    private static string? ResolveTargetPropertyName(
        string incoming,
        Dictionary<string, string> nameToProperty,
        Dictionary<string, PropertyInfo> writable)
    {
        if (nameToProperty.TryGetValue(incoming, out var target) && writable.ContainsKey(target))
            return target;
        if (writable.ContainsKey(incoming)) return incoming;
        return null;
    }

    private static bool TryExtractFieldMapping(JsonElement f, out string? name, out string? dbCol)
    {
        name = f.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
        dbCol = f.TryGetProperty("dbColumn", out var dEl) ? dEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (IsFieldExcludedByVisibility(f))
            return false;
        if (IsAdminOnlyField(f))
            return false;

        dbCol = !string.IsNullOrWhiteSpace(dbCol) ? dbCol : name;
        return true;
    }

    private static bool IsFieldExcludedByVisibility(JsonElement f)
    {
        if (f.TryGetProperty("visibility", out var visEl) && visEl.ValueKind == JsonValueKind.String)
        {
            var vis = visEl.GetString();
            if (string.Equals(vis, "hidden", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(vis, "adminOnly", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsAdminOnlyField(JsonElement f)
    {
        if (TryGetPropertyCI(f, "adminOnly", out var adminEl))
        {
            return adminEl.ValueKind == JsonValueKind.True ||
                   (adminEl.ValueKind == JsonValueKind.String && bool.TryParse(adminEl.GetString(), out var b) && b);
        }
        return false;
    }

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

    private static bool ShouldIncludeProperty(PropertyInfo p)
    {
        if (!p.CanRead || p.GetIndexParameters().Length > 0) return false;
        var name = p.Name;
        var type = p.PropertyType;

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
            return false;

        static bool IsSimple(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal) || u == typeof(DateTime) || u == typeof(Guid);
        }
        if (!IsSimple(type)) return false;

        if (name is nameof(Registrations.RegistrationAi)
            or nameof(Registrations.RegistrationId)
            or nameof(Registrations.RegistrationTs)
            or nameof(Registrations.RoleId)
            or nameof(Registrations.UserId)
            or nameof(Registrations.FamilyUserId)
            or nameof(Registrations.BActive)
            or nameof(Registrations.BConfirmationSent)
            or nameof(Registrations.JobId)
            or nameof(Registrations.LebUserId)
            or nameof(Registrations.Modified)
            or nameof(Registrations.RegistrationFormName)
            or nameof(Registrations.PaymentMethodChosen)
            or nameof(Registrations.FeeProcessing)
            or nameof(Registrations.FeeBase)
            or nameof(Registrations.FeeDiscount)
            or nameof(Registrations.FeeDiscountMp)
            or nameof(Registrations.FeeDonation)
            or nameof(Registrations.FeeLatefee)
            or nameof(Registrations.FeeTotal)
            or nameof(Registrations.OwedTotal)
            or nameof(Registrations.PaidTotal)
            or nameof(Registrations.CustomerId)
            or nameof(Registrations.DiscountCodeId)
            or nameof(Registrations.AssignedTeamId)
            or nameof(Registrations.AssignedAgegroupId)
            or nameof(Registrations.AssignedCustomerId)
            or nameof(Registrations.AssignedDivId)
            or nameof(Registrations.AssignedLeagueId)
            or nameof(Registrations.RegformId)
            or nameof(Registrations.AccountingApplyToSummaries))
        {
            return false;
        }
        if (!string.Equals(name, nameof(Registrations.SportAssnId), StringComparison.Ordinal) &&
            (name.EndsWith("Id", StringComparison.Ordinal) || name.EndsWith("ID", StringComparison.Ordinal)))
        {
            return false;
        }
        if (name.StartsWith("BUploaded", StringComparison.Ordinal))
            return false;
        if (name.StartsWith("Adn", StringComparison.Ordinal) || name.StartsWith("Regsaver", StringComparison.Ordinal))
            return false;

        return true;
    }

    // ─── Type conversion helpers ─────────────────────────────────────────

    private static bool TryConvertAndAssign(JsonElement json, Type targetType, out object? boxed)
    {
        boxed = null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            if (t == typeof(string)) return TryConvertToString(json, out boxed);
            if (t == typeof(int)) return TryConvertToInt(json, out boxed);
            if (t == typeof(long)) return TryConvertToLong(json, out boxed);
            if (t == typeof(decimal)) return TryConvertToDecimal(json, out boxed);
            if (t == typeof(double)) return TryConvertToDouble(json, out boxed);
            if (t == typeof(bool)) return TryConvertToBool(json, out boxed);
            if (t == typeof(DateTime)) return TryConvertToDateTime(json, out boxed);
            if (t == typeof(Guid)) return TryConvertToGuid(json, out boxed);
        }
        catch { return false; }
        return false;
    }

    private static bool TryConvertToString(JsonElement json, out object? boxed)
    {
        boxed = json.ValueKind == JsonValueKind.Null ? null : json.ToString();
        return true;
    }

    private static bool TryConvertToInt(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), out var iv)) { boxed = iv; return true; }
        if (json.TryGetInt32(out var i)) { boxed = i; return true; }
        return false;
    }

    private static bool TryConvertToLong(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && long.TryParse(json.GetString(), out var lv)) { boxed = lv; return true; }
        if (json.TryGetInt64(out var l)) { boxed = l; return true; }
        return false;
    }

    private static bool TryConvertToDecimal(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && decimal.TryParse(json.GetString(), out var dv)) { boxed = dv; return true; }
        if (json.TryGetDecimal(out var d)) { boxed = d; return true; }
        return false;
    }

    private static bool TryConvertToDouble(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && double.TryParse(json.GetString(), out var xv)) { boxed = xv; return true; }
        if (json.TryGetDouble(out var x)) { boxed = x; return true; }
        return false;
    }

    private static bool TryConvertToBool(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && bool.TryParse(json.GetString(), out var bv)) { boxed = bv; return true; }
        if (json.ValueKind == JsonValueKind.Number) { boxed = json.GetInt32() != 0; return true; }
        if (json.ValueKind == JsonValueKind.True || json.ValueKind == JsonValueKind.False) { boxed = json.GetBoolean(); return true; }
        return false;
    }

    private static bool TryConvertToDateTime(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && DateTime.TryParse(json.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            boxed = dt;
            return true;
        }
        return false;
    }

    private static bool TryConvertToGuid(JsonElement json, out object? boxed)
    {
        boxed = null;
        if (json.ValueKind == JsonValueKind.String && Guid.TryParse(json.GetString(), out var g))
        {
            boxed = g;
            return true;
        }
        return false;
    }
}
