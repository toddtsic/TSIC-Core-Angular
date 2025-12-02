using System.Globalization;
using System.Text.Json;
using TSIC.API.Dtos;

namespace TSIC.API.Services;

/// <summary>
/// Validates player form submission data against job metadata schema.
/// Extracted from RegistrationService for reusability across player and team registrations.
/// </summary>
public class PlayerFormValidationService : IPlayerFormValidationService
{
    public List<PreSubmitValidationErrorDto> ValidatePlayerFormValues(string? metadataJson, List<PreSubmitTeamSelectionDto> selections)
    {
        var errors = new List<PreSubmitValidationErrorDto>();
        if (string.IsNullOrWhiteSpace(metadataJson) || selections.Count == 0) return errors;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(metadataJson); }
        catch { return errors; }

        if (!doc.RootElement.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array)
            return errors;

        var schemas = BuildSchemas(fieldsEl);

        // Merge all FormValues per player across selections (case-insensitive keys, last write wins)
        var mergedValuesByPlayer = selections
            .Where(s => s.FormValues != null)
            .GroupBy(s => s.PlayerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var sel in g)
                    {
                        foreach (var kv in sel.FormValues!)
                        {
                            dict[kv.Key] = kv.Value; // last write wins
                        }
                    }
                    return dict;
                }
            );

        foreach (var kv in mergedValuesByPlayer)
        {
            var playerId = kv.Key;
            var formValues = kv.Value;
            ValidateSchemasForPlayer(schemas, playerId, formValues, errors);
        }
        return errors;
    }

    private static List<(string Name, bool Required, string Type, string? ConditionField, JsonElement? ConditionValue, string? ConditionOp, HashSet<string> Options)> BuildSchemas(JsonElement fieldsEl)
    {
        var list = new List<(string, bool, string, string?, JsonElement?, string?, HashSet<string>)>();
        foreach (var f in fieldsEl.EnumerateArray())
        {
            var name = f.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Skip hidden or admin-only fields
            if (f.TryGetProperty("visibility", out var visEl) && visEl.ValueKind == JsonValueKind.String &&
                (string.Equals(visEl.GetString(), "hidden", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(visEl.GetString(), "adminOnly", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Skip admin-only fields so they are not required/validated for player form submission.
            if (TryGetPropertyCI(f, "adminOnly", out var adminEl))
            {
                var adminFlag = adminEl.ValueKind == JsonValueKind.True ||
                                 (adminEl.ValueKind == JsonValueKind.String && bool.TryParse(adminEl.GetString(), out var b) && b);
                if (adminFlag) continue;
            }

            var required = f.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.True;
            if (!required && f.TryGetProperty("validation", out var valEl) && valEl.ValueKind == JsonValueKind.Object)
            {
                if (valEl.TryGetProperty("required", out var rEl) && rEl.ValueKind == JsonValueKind.True) required = true;
                if (valEl.TryGetProperty("requiredTrue", out var rtEl) && rtEl.ValueKind == JsonValueKind.True) required = true;
            }

            var type = f.TryGetProperty("type", out var tEl) ? (tEl.GetString() ?? "text") : "text";

            string? condField = null; JsonElement? condValue = null; string? condOp = null;
            if (f.TryGetProperty("condition", out var cEl) && cEl.ValueKind == JsonValueKind.Object)
            {
                condField = cEl.TryGetProperty("field", out var cfEl) ? cfEl.GetString() : null;
                if (cEl.TryGetProperty("value", out var cvEl)) condValue = cvEl;
                condOp = cEl.TryGetProperty("operator", out var coEl) ? coEl.GetString() : null;
            }

            var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (f.TryGetProperty("options", out var optEl) && optEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in optEl.EnumerateArray())
                {
                    if (o.ValueKind == JsonValueKind.String) options.Add(o.GetString()!);
                }
            }

            list.Add((name!, required, type!.ToLowerInvariant(), condField, condValue, condOp, options));
        }
        return list;
    }

    private static void ValidateSchemasForPlayer(
        List<(string Name, bool Required, string Type, string? ConditionField, JsonElement? ConditionValue, string? ConditionOp, HashSet<string> Options)> schemas,
        string playerId,
        Dictionary<string, JsonElement> formValues,
        List<PreSubmitValidationErrorDto> errors)
    {
        // Make field key lookup case-insensitive so client differences in casing (e.g., bWaiverSigned1 vs BWaiverSigned1) don't cause false "Required" errors.
        var ciFormValues = new Dictionary<string, JsonElement>(formValues, StringComparer.OrdinalIgnoreCase);

        foreach (var schema in schemas)
        {
            if (schema.ConditionField != null && schema.ConditionValue.HasValue)
            {
                ciFormValues.TryGetValue(schema.ConditionField, out var otherVal);
                var condOk = otherVal.ValueKind == schema.ConditionValue.Value.ValueKind && otherVal.ToString() == schema.ConditionValue.Value.ToString();
                if (!condOk) continue;
            }

            ciFormValues.TryGetValue(schema.Name, out var valEl);
            var present = valEl.ValueKind != JsonValueKind.Undefined && valEl.ValueKind != JsonValueKind.Null && valEl.ToString().Trim().Length > 0;
            var rawStr = valEl.ToString();

            switch (schema.Type)
            {
                case "number":
                    if (schema.Required && !present)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        break;
                    }
                    if (!present) break;
                    if (!double.TryParse(rawStr, out _))
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Must be a number" });
                    break;

                case "date":
                    if (schema.Required && !present)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        break;
                    }
                    if (!present) break;
                    if (!DateTime.TryParse(rawStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid date" });
                    break;

                case "select":
                    if (schema.Required && !present)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        break;
                    }
                    if (!present) break;
                    if (schema.Options.Count > 0 && !schema.Options.Contains(rawStr))
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid option" });
                    break;

                case "multiselect":
                    if (!present)
                    {
                        if (schema.Required) errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                        break;
                    }
                    if (valEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in valEl.EnumerateArray())
                        {
                            var s = item.ToString();
                            if (schema.Options.Count > 0 && !schema.Options.Contains(s))
                            {
                                errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Invalid option" });
                                break;
                            }
                        }
                    }
                    break;

                case "checkbox":
                    // For required checkboxes (e.g., waiver accepts): only evaluate if present; if missing, skip.
                    if (!present) break;

                    // Accept multiple representations of truthy values: true, "true", 1, "1", "yes", "on", "checked".
                    bool accepted;
                    if (valEl.ValueKind == JsonValueKind.True || valEl.ValueKind == JsonValueKind.False)
                    {
                        accepted = valEl.GetBoolean();
                    }
                    else if (valEl.ValueKind == JsonValueKind.Number)
                    {
                        try { accepted = valEl.GetInt32() != 0; }
                        catch { accepted = false; }
                    }
                    else
                    {
                        var s = rawStr.Trim();
                        accepted = string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "1", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "y", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "on", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(s, "checked", StringComparison.OrdinalIgnoreCase);
                    }
                    if (schema.Required && !accepted)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                    }
                    break;

                default:
                    if (schema.Required && !present)
                    {
                        errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = schema.Name, Message = "Required" });
                    }
                    break;
            }
        }
    }

    private static bool TryGetPropertyCI(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
