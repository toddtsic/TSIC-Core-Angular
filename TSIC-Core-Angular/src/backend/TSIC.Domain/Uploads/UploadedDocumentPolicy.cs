using System.Text.Json;

namespace TSIC.Domain.Uploads;

/// <summary>
/// Single source of truth for "does this event's registration profile COLLECT a given uploaded
/// document." An event that never asked for a document has no business displaying or streaming one.
///
/// Why this exists: med-form PDFs are stored per PERSON (<c>MedForms\{userId}.pdf</c>, a legacy
/// convention carried forward for file compatibility), NOT per event. A file uploaded for one event
/// is therefore on disk for every event that person ever registers in. The admin med-form control
/// used to render off disk-existence alone, which put a "Medical form on file / View" button in
/// front of directors at 757 jobs across 52 unrelated customers who never collected one — a
/// cross-customer PHI exposure. Gating on the job's own profile re-establishes the event boundary
/// that the storage layer does not carry.
///
/// A field counts as collected when its <c>name</c> OR <c>dbColumn</c> matches, case-insensitively.
/// Missing or malformed metadata yields <c>false</c> — fail closed: no metadata, no access.
///
/// Lives in Domain so the API authorization path (<c>MedFormController</c>) and the registration
/// write path (<c>PlayerRegistrationService</c>, which stamps <c>Registrations.BUploadedMedForm</c>)
/// resolve the same predicate without duplicating the JSON parse. Mirrors
/// <see cref="UsLax.UsLaxMetadataPolicy"/>.
/// </summary>
public static class UploadedDocumentPolicy
{
    /// <summary>The medical-form upload field, as it appears in <c>Jobs.PlayerProfileMetadataJson</c>.</summary>
    public const string MedFormField = "bUploadedMedForm";

    /// <summary>
    /// The vaccine-card upload field. No live job collects it and this stack has no reader for it
    /// (upload, view, and storage are all legacy-only). The constant exists so that if a reader is
    /// ever built it inherits this gate by construction instead of repeating the med-form mistake.
    /// </summary>
    public const string VaccineCardField = "bUploadedVaccineCard";

    /// <summary>True iff the job's player profile collects a medical form.</summary>
    public static bool CollectsMedForm(string? metadataJson) => Collects(metadataJson, MedFormField);

    /// <summary>True iff the job's player profile contains <paramref name="fieldName"/>.</summary>
    public static bool Collects(string? metadataJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson) || string.IsNullOrWhiteSpace(fieldName)) return false;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var f in fields.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                if (MatchesCI(f, "name", fieldName) || MatchesCI(f, "dbColumn", fieldName)) return true;
            }
        }
        catch (JsonException)
        {
            // Malformed metadata asserts nothing. Fail closed.
        }

        return false;
    }

    private static bool MatchesCI(JsonElement obj, string property, string expected)
    {
        var value = GetStringCI(obj, property);
        return value is not null && string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
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
