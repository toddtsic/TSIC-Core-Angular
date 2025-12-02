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
    private const string RequiredErrorMessage = "Required";
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
            var schema = TryBuildSchemaFromField(f);
            if (schema.HasValue)
                list.Add(schema.Value);
        }
        return list;
    }

    private static (string Name, bool Required, string Type, string? ConditionField, JsonElement? ConditionValue, string? ConditionOp, HashSet<string> Options)? TryBuildSchemaFromField(JsonElement f)
    {
        var name = f.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (ShouldSkipField(f)) return null;

        var required = DetermineIfRequired(f);
        var type = f.TryGetProperty("type", out var tEl) ? (tEl.GetString() ?? "text") : "text";
        var (condField, condValue, condOp) = ExtractCondition(f);
        var options = ExtractOptions(f);

        return (name!, required, type!.ToLowerInvariant(), condField, condValue, condOp, options);
    }

    private static bool ShouldSkipField(JsonElement f)
    {
        if (f.TryGetProperty("visibility", out var visEl) && visEl.ValueKind == JsonValueKind.String)
        {
            var vis = visEl.GetString();
            if (string.Equals(vis, "hidden", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(vis, "adminOnly", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (TryGetPropertyCI(f, "adminOnly", out var adminEl))
        {
            var adminFlag = adminEl.ValueKind == JsonValueKind.True ||
                           (adminEl.ValueKind == JsonValueKind.String && bool.TryParse(adminEl.GetString(), out var b) && b);
            if (adminFlag) return true;
        }

        return false;
    }

    private static bool DetermineIfRequired(JsonElement f)
    {
        if (f.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.True)
            return true;

        if (f.TryGetProperty("validation", out var valEl) && valEl.ValueKind == JsonValueKind.Object)
        {
            if (valEl.TryGetProperty("required", out var rEl) && rEl.ValueKind == JsonValueKind.True) 
                return true;
            if (valEl.TryGetProperty("requiredTrue", out var rtEl) && rtEl.ValueKind == JsonValueKind.True) 
                return true;
        }

        return false;
    }

    private static (string? Field, JsonElement? Value, string? Op) ExtractCondition(JsonElement f)
    {
        if (!f.TryGetProperty("condition", out var cEl) || cEl.ValueKind != JsonValueKind.Object)
            return (null, null, null);

        var condField = cEl.TryGetProperty("field", out var cfEl) ? cfEl.GetString() : null;
        var condValue = cEl.TryGetProperty("value", out var cvEl) ? (JsonElement?)cvEl : null;
        var condOp = cEl.TryGetProperty("operator", out var coEl) ? coEl.GetString() : null;

        return (condField, condValue, condOp);
    }

    private static HashSet<string> ExtractOptions(JsonElement f)
    {
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (f.TryGetProperty("options", out var optEl) && optEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var o in optEl.EnumerateArray())
            {
                if (o.ValueKind == JsonValueKind.String) 
                    options.Add(o.GetString()!);
            }
        }
        return options;
    }

    private static void ValidateSchemasForPlayer(
        List<(string Name, bool Required, string Type, string? ConditionField, JsonElement? ConditionValue, string? ConditionOp, HashSet<string> Options)> schemas,
        string playerId,
        Dictionary<string, JsonElement> formValues,
        List<PreSubmitValidationErrorDto> errors)
    {
        var ciFormValues = new Dictionary<string, JsonElement>(formValues, StringComparer.OrdinalIgnoreCase);

        foreach (var schema in schemas)
        {
            if (!IsConditionSatisfied(schema.ConditionField, schema.ConditionValue, ciFormValues))
                continue;

            ciFormValues.TryGetValue(schema.Name, out var valEl);
            var present = IsValuePresent(valEl);
            var rawStr = valEl.ToString();

            ValidateFieldByType(schema, playerId, present, rawStr, valEl, errors);
        }
    }

    private static bool IsConditionSatisfied(string? conditionField, JsonElement? conditionValue, Dictionary<string, JsonElement> formValues)
    {
        if (conditionField == null || !conditionValue.HasValue)
            return true;

        formValues.TryGetValue(conditionField, out var otherVal);
        return otherVal.ValueKind == conditionValue.Value.ValueKind && 
               otherVal.ToString() == conditionValue.Value.ToString();
    }

    private static bool IsValuePresent(JsonElement valEl)
    {
        return valEl.ValueKind != JsonValueKind.Undefined && 
               valEl.ValueKind != JsonValueKind.Null && 
               valEl.ToString().Trim().Length > 0;
    }

    private static void ValidateFieldByType(
        (string Name, bool Required, string Type, string? ConditionField, JsonElement? ConditionValue, string? ConditionOp, HashSet<string> Options) schema,
        string playerId,
        bool present,
        string rawStr,
        JsonElement valEl,
        List<PreSubmitValidationErrorDto> errors)
    {
        switch (schema.Type)
        {
            case "number":
                ValidateNumberField(schema.Name, schema.Required, playerId, present, rawStr, errors);
                break;
            case "phone":
                ValidateDateField(schema.Name, schema.Required, playerId, present, rawStr, errors);
                break;
            case "select":
                ValidateSelectField(schema.Name, schema.Required, schema.Options, playerId, present, rawStr, errors);
                break;
            case "multiselect":
                ValidateMultiSelectField(schema.Name, schema.Required, schema.Options, playerId, present, valEl, errors);
                break;
            case "checkbox":
                ValidateCheckboxField(schema.Name, schema.Required, playerId, present, rawStr, errors);
                break;
            case "text":
            case "textarea":
                ValidateTextField(schema.Name, schema.Required, playerId, rawStr, errors);
                break;
            default:
                ValidateDefaultField(schema.Name, schema.Required, playerId, present, errors);
                break;
        }
    }

    private static void ValidateNumberField(string fieldName, bool required, string playerId, bool present, string rawStr, List<PreSubmitValidationErrorDto> errors)
    {
        if (required && !present)
        {
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = RequiredErrorMessage });
            return;
        }
        if (!present) return;
        if (!double.TryParse(rawStr, out _))
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = "Must be a number" });
    }

    private static void ValidateDateField(string fieldName, bool required, string playerId, bool present, string rawStr, List<PreSubmitValidationErrorDto> errors)
    {
        if (required && !present)
        {
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = RequiredErrorMessage });
            return;
        }
        if (!present) return;
        if (!DateTime.TryParse(rawStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = "Invalid date" });
    }

    private static void ValidateSelectField(string fieldName, bool required, HashSet<string> options, string playerId, bool present, string rawStr, List<PreSubmitValidationErrorDto> errors)
    {
        if (required && !present)
        {
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = RequiredErrorMessage });
            return;
        }
        if (!present) return;
        if (options.Count > 0 && !options.Contains(rawStr))
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = "Invalid option" });
    }

    private static void ValidateMultiSelectField(string fieldName, bool required, HashSet<string> options, string playerId, bool present, JsonElement valEl, List<PreSubmitValidationErrorDto> errors)
    {
        if (!present)
        {
            if (required) 
                errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = RequiredErrorMessage });
            return;
        }
        
        if (valEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in valEl.EnumerateArray())
            {
                var s = item.ToString();
                if (options.Count > 0 && !options.Contains(s))
                {
                    errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = "Invalid option" });
                    break;
                }
            }
        }
    }

    private static void ValidateCheckboxField(string fieldName, bool required, string playerId, bool present, string rawStr, List<PreSubmitValidationErrorDto> errors)
    {
        if (!present) return;

        var accepted = string.Equals(rawStr, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(rawStr, "yes", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(rawStr, "1", StringComparison.Ordinal) ||
                       string.Equals(rawStr, "y", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(rawStr, "on", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(rawStr, "checked", StringComparison.OrdinalIgnoreCase);

        if (required && !accepted)
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = RequiredErrorMessage });
    }

    private static void ValidateTextField(string fieldName, bool required, string playerId, string rawStr, List<PreSubmitValidationErrorDto> errors)
    {
        if (required && string.IsNullOrWhiteSpace(rawStr))
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = RequiredErrorMessage });
    }

    private static void ValidateDefaultField(string fieldName, bool required, string playerId, bool present, List<PreSubmitValidationErrorDto> errors)
    {
        if (required && !present)
            errors.Add(new PreSubmitValidationErrorDto { PlayerId = playerId, Field = fieldName, Message = RequiredErrorMessage });
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
