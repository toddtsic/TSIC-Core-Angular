using System.Text.Json;
using TSIC.Domain.Entities;

namespace TSIC.API.Services;

public class PlayerRegistrationMetadataService : IPlayerRegistrationMetadataService
{
    public string GetRegistrationMode(string? coreRegformPlayer, string? jsonOptions)
    {
        // 1) Prefer explicit CoreRegformPlayer if present (e.g., "CAC09|..." or "PP10|...")
        if (!string.IsNullOrWhiteSpace(coreRegformPlayer) && coreRegformPlayer != "0" && coreRegformPlayer != "1")
        {
            var firstPart = coreRegformPlayer!.Split('|')[0].Trim();
            if (firstPart.StartsWith("CAC", StringComparison.OrdinalIgnoreCase) ||
                firstPart.Equals("CAC", StringComparison.OrdinalIgnoreCase))
            {
                return "CAC";
            }
            if (firstPart.StartsWith("PP", StringComparison.OrdinalIgnoreCase) ||
                firstPart.Equals("PP", StringComparison.OrdinalIgnoreCase))
            {
                return "PP";
            }
        }

        // 2) Fallback to JsonOptions keys if provided
        if (!string.IsNullOrWhiteSpace(jsonOptions))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonOptions);
                var root = doc.RootElement;
                var keys = new[] { "registrationMode", "profileMode", "regProfileType", "registrationType" };
                foreach (var k in keys)
                {
                    if (!root.TryGetProperty(k, out var el)) continue;
                    var s = el.GetString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    s = s.Trim();
                    if (s.Equals("CAC", StringComparison.OrdinalIgnoreCase)) return "CAC";
                    if (s.Equals("PP", StringComparison.OrdinalIgnoreCase)) return "PP";
                }
            }
            catch (Exception)
            {
                // Ignore malformed jsonOptions; default to PP below
            }
        }

        // 3) Default to PP to maintain backward compatibility
        return "PP";
    }

    public Dictionary<string, string> BuildFieldNameToPropertyMap(string? metadataJson)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(metadataJson)) return map;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fieldsEl.EnumerateArray())
                {
                    var name = f.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                    var dbCol = f.TryGetProperty("dbColumn", out var dEl) ? dEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    // Exclude fields marked hidden or adminOnly via visibility
                    if (f.TryGetProperty("visibility", out var visEl) && visEl.ValueKind == JsonValueKind.String)
                    {
                        var vis = visEl.GetString();
                        if (string.Equals(vis, "hidden", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(vis, "adminOnly", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    // Do not include admin-only fields in the writable map
                    if (TryGetPropertyCI(f, "adminOnly", out var adminEl))
                    {
                        var adminFlag = adminEl.ValueKind == JsonValueKind.True ||
                                         (adminEl.ValueKind == JsonValueKind.String && bool.TryParse(adminEl.GetString(), out var b) && b);
                        if (adminFlag) continue;
                    }
                    map[name!] = !string.IsNullOrWhiteSpace(dbCol) ? dbCol! : name!;
                }
            }
        }
        catch (Exception)
        {
            // Ignore malformed metadata and return empty map
        }
        return map;
    }

    public bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value)
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

    public Dictionary<string, System.Reflection.PropertyInfo> BuildWritablePropertyMap()
    {
        var props = typeof(Registrations).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var dict = new Dictionary<string, System.Reflection.PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in props)
        {
            if (!p.CanWrite) continue;
            if (!ShouldIncludeProperty(p)) continue;
            if (string.Equals(p.Name, nameof(Registrations.AssignedTeamId), StringComparison.OrdinalIgnoreCase)) continue;
            dict[p.Name] = p;
        }
        return dict;
    }

    private static bool ShouldIncludeProperty(System.Reflection.PropertyInfo p)
    {
        if (!p.CanRead || p.GetIndexParameters().Length > 0) return false;
        var name = p.Name;
        var type = p.PropertyType;

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            return false;
        }

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
        // Allow waiver acceptance booleans to be written (client injects them). Still exclude uploaded flags.
        if (name.StartsWith("BUploaded", StringComparison.Ordinal))
        {
            return false;
        }
        if (name.StartsWith("Adn", StringComparison.Ordinal) || name.StartsWith("Regsaver", StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }
}
