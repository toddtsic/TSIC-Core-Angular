using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Metadata;

public sealed class ProfileMetadataService : IProfileMetadataService
{
    public ParsedProfileMetadata Parse(string? metadataJson, string? jsonOptions)
    {
        var result = new ParsedProfileMetadata();
        if (string.IsNullOrWhiteSpace(metadataJson)) return result;

        using var doc = JsonDocument.Parse(metadataJson);
        if (!doc.RootElement.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array)
            return result;

        // 1) Parse fields to typed/mapped/waiver lists
        var (typed, mapped, waiver) = ParseFields(fieldsEl);

        // 2) Enrich with option sets if provided
        if (!string.IsNullOrWhiteSpace(jsonOptions) && typed.Count > 0)
        {
            EnrichOptionsFromJson(jsonOptions!, typed);
        }

        // 3) Compute visible field names
        var visible = ComputeVisible(typed);

        result.MappedFields = mapped;
        result.TypedFields = typed;
        result.WaiverFieldNames = waiver.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        result.VisibleFieldNames = visible;
        return result;
    }

    public string? ResolveConstraintType(string? coreRegformPlayer)
    {
        if (string.IsNullOrWhiteSpace(coreRegformPlayer)) return null;
        foreach (var p in coreRegformPlayer.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var up = p.ToUpperInvariant();
            if (up is "BYGRADYEAR" or "BYAGEGROUP" or "BYAGERANGE" or "BYCLUBNAME")
                return up;
        }
        return null;
    }

    public JobRegFormDto BuildJobRegForm(Guid jobId, ParsedProfileMetadata parsed, string? coreRegformPlayer, string? metadataJson, string? jsonOptions)
    {
        var constraint = ResolveConstraintType(coreRegformPlayer);
        var seed = $"{jobId}-{metadataJson?.Length ?? 0}-{jsonOptions?.Length ?? 0}-{parsed.TypedFields.Count}";
        var version = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).Substring(0, 16);

        var fields = parsed.TypedFields.Select(tf => new JobRegFieldDto
        {
            Name = tf.Name,
            DbColumn = tf.DbColumn,
            DisplayName = string.IsNullOrWhiteSpace(tf.DisplayName) ? tf.Name : tf.DisplayName,
            InputType = string.IsNullOrWhiteSpace(tf.InputType) ? "TEXT" : tf.InputType,
            DataSource = tf.DataSource,
            Options = tf.Options,
            Validation = tf.Validation,
            Order = tf.Order,
            Visibility = string.IsNullOrWhiteSpace(tf.Visibility) ? "public" : tf.Visibility,
            Computed = tf.Computed,
            ConditionalOn = tf.ConditionalOn
        }).ToList();

        return new JobRegFormDto { Version = version, CoreProfileName = coreRegformPlayer, Fields = fields, WaiverFieldNames = parsed.WaiverFieldNames, ConstraintType = constraint };
    }

    private static (List<ProfileMetadataField> typed, List<(string Name, string DbColumn)> mapped, List<string> waiver) ParseFields(JsonElement fieldsEl)
    {
        var typed = new List<ProfileMetadataField>();
        var mapped = new List<(string Name, string DbColumn)>();
        var waiver = new List<string>();

        foreach (var f in fieldsEl.EnumerateArray())
        {
            var field = BuildField(f);

            if (!string.IsNullOrWhiteSpace(field.Name) && !string.IsNullOrWhiteSpace(field.DbColumn))
                mapped.Add((field.Name, field.DbColumn));

            if (!string.IsNullOrWhiteSpace(field.Name))
            {
                typed.Add(field);
                if (IsWaiverField(field)) waiver.Add(field.Name);
            }
        }

        return (typed, mapped, waiver);
    }

    private static ProfileMetadataField BuildField(JsonElement f)
    {
        var name = f.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty;
        var dbCol = f.TryGetProperty("dbColumn", out var dEl) ? (dEl.GetString() ?? string.Empty) : string.Empty;
        var field = new ProfileMetadataField
        {
            Name = name,
            DbColumn = dbCol,
            DisplayName = f.TryGetProperty("displayName", out var dnEl) ? (dnEl.GetString() ?? name) : name,
            InputType = f.TryGetProperty("inputType", out var itEl) ? (itEl.GetString() ?? "TEXT") : "TEXT",
            DataSource = f.TryGetProperty("dataSource", out var dsEl) ? dsEl.GetString() : null,
            Order = f.TryGetProperty("order", out var ordEl) && ordEl.ValueKind == JsonValueKind.Number ? ordEl.GetInt32() : 0,
            Visibility = f.TryGetProperty("visibility", out var visEl) ? (visEl.GetString() ?? "public") : "public",
            Computed = f.TryGetProperty("computed", out var compEl) && compEl.ValueKind == JsonValueKind.True,
        };

        ApplyAdminOnlyFlag(f, field);
        ApplyOptions(f, field);
        ApplyValidation(f, field);
        ApplyConditional(f, field);

        return field;
    }

    private static void ApplyAdminOnlyFlag(JsonElement f, ProfileMetadataField field)
    {
        if (f.TryGetProperty("adminOnly", out var aoEl) && aoEl.ValueKind == JsonValueKind.True)
            field.Visibility = "adminOnly";
    }

    private static void ApplyOptions(JsonElement f, ProfileMetadataField field)
    {
        if (f.TryGetProperty("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array)
            field.Options = MapOptions(optsEl);
    }

    private static void ApplyValidation(JsonElement f, ProfileMetadataField field)
    {
        if (!(f.TryGetProperty("validation", out var valEl) && valEl.ValueKind == JsonValueKind.Object)) return;

        field.Validation = new FieldValidation
        {
            Required = GetBool(valEl, "required"),
            Email = GetBool(valEl, "email"),
            RequiredTrue = GetBool(valEl, "requiredTrue"),
            MinLength = GetInt(valEl, "minLength"),
            MaxLength = GetInt(valEl, "maxLength"),
            Pattern = GetString(valEl, "pattern"),
            Min = GetDouble(valEl, "min"),
            Max = GetDouble(valEl, "max"),
            Compare = GetString(valEl, "compare"),
            Remote = GetString(valEl, "remote"),
            Message = GetString(valEl, "message"),
        };
    }

    private static void ApplyConditional(JsonElement f, ProfileMetadataField field)
    {
        if (!(f.TryGetProperty("conditionalOn", out var condEl) && condEl.ValueKind == JsonValueKind.Object)) return;

        field.ConditionalOn = new FieldCondition
        {
            Field = condEl.TryGetProperty("field", out var cf) ? (cf.GetString() ?? string.Empty) : string.Empty,
            Operator = condEl.TryGetProperty("operator", out var op) ? (op.GetString() ?? "equals") : "equals",
            Value = condEl.TryGetProperty("value", out var cv) ? JsonSerializer.Deserialize<object>(cv.GetRawText()) : null
        };
    }

    private static bool IsWaiverField(ProfileMetadataField field)
    {
        var lname = (field.Name ?? string.Empty).ToLowerInvariant();
        var ldisp = (field.DisplayName ?? field.Name ?? string.Empty).ToLowerInvariant();
        var isCheckbox = (field.InputType ?? string.Empty).ToLowerInvariant().Contains("checkbox");
        var looksLikeWaiver = isCheckbox && (
            ldisp.StartsWith("i agree") ||
            ldisp.Contains("waiver") || ldisp.Contains("release") ||
            (ldisp.Contains("code") && ldisp.Contains("conduct")) ||
            ldisp.Contains("refund") || (ldisp.Contains("terms") && ldisp.Contains("conditions"))
        );
        return looksLikeWaiver || lname.Contains("waiver") || lname.Contains("codeofconduct") || lname.Contains("refund");
    }

    private static void EnrichOptionsFromJson(string jsonOptions, List<ProfileMetadataField> typed)
    {
        using var optsDoc = JsonDocument.Parse(jsonOptions);
        if (optsDoc.RootElement.ValueKind != JsonValueKind.Object) return;

        var sets = optsDoc.RootElement
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var tf in typed)
        {
            if (string.IsNullOrWhiteSpace(tf.DataSource)) continue;
            if (!sets.TryGetValue(tf.DataSource!, out var setEl)) continue;

            var mappedOpts = MapOptions(setEl);
            if (mappedOpts.Count > 0) tf.Options = mappedOpts;
        }
    }

    private static HashSet<string> ComputeVisible(List<ProfileMetadataField> typed)
    {
        return new HashSet<string>(
            typed.Where(tf => !string.Equals(tf.Visibility, "hidden", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(tf.Visibility, "adminOnly", StringComparison.OrdinalIgnoreCase))
                 .Select(tf => tf.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<ProfileFieldOption> MapOptions(JsonElement set)
    {
        var list = new List<ProfileFieldOption>();
        if (set.ValueKind != JsonValueKind.Array) return list;

        foreach (var el in set.EnumerateArray())
        {
            var opt = MapOptionElement(el);
            if (opt != null) list.Add(opt);
        }
        return list;
    }

    private static ProfileFieldOption? MapOptionElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var val = ExtractOptionValue(el);
                var label = ExtractOptionLabel(el, val);
                return new ProfileFieldOption { Value = val, Label = label };
            case JsonValueKind.String:
                var v = el.GetString() ?? string.Empty; return new ProfileFieldOption { Value = v, Label = v };
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                var raw = el.GetRawText(); return new ProfileFieldOption { Value = raw, Label = raw };
            default:
                return null;
        }
    }

    private static string ExtractOptionValue(JsonElement obj)
    {
        return FirstNonEmpty(
            () => TryGetPropertyString(obj, "value"),
            () => TryGetPropertyString(obj, "Value"),
            () => TryGetPropertyString(obj, "id"),
            () => TryGetPropertyString(obj, "Id"),
            () => TryGetPropertyString(obj, "code"),
            () => TryGetPropertyString(obj, "Code"),
            () => TryGetPropertyString(obj, "year"),
            () => TryGetPropertyString(obj, "Year")
        ) ?? string.Empty;
    }

    private static string ExtractOptionLabel(JsonElement obj, string fallback)
    {
        return FirstNonEmpty(
            () => TryGetPropertyString(obj, "label"),
            () => TryGetPropertyString(obj, "Label"),
            () => TryGetPropertyString(obj, "text"),
            () => TryGetPropertyString(obj, "Text")
        ) ?? fallback;
    }

    private static string? TryGetPropertyString(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out var el) ? (el.GetString() ?? string.Empty) : null;
    }

    private static string? FirstNonEmpty(params Func<string?>[] candidates)
    {
        foreach (var c in candidates)
        {
            var v = c();
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }

    private static bool GetBool(JsonElement obj, string prop)
    {
        return obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.True;
    }

    private static int? GetInt(JsonElement obj, string prop)
    {
        return obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : (int?)null;
    }

    private static double? GetDouble(JsonElement obj, string prop)
    {
        return obj.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : (double?)null;
    }

    private static string? GetString(JsonElement obj, string prop)
    {
        return obj.TryGetProperty(prop, out var el) ? el.GetString() : null;
    }
}
