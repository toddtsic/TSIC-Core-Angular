using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Adults;

/// <summary>
/// The adult "legacy source" — the parallel to the player <see cref="Metadata.CSharpToMetadataParser"/>.
///
/// Collapses the 8 legacy <c>Jobs.RegformName_Coach</c> values into OUR OWN canonical nomenclature
/// (<see cref="AC1"/>/<see cref="AC2"/>) plus an orthogonal USA-Lacrosse capability, then materializes
/// the full three-role adult metadata set for a job.
///
/// <para><b>Nomenclature is ours, not the legacy strings.</b> The legacy values are only a migration
/// <i>source</i> (mirrored from <c>StartARegistrationController</c>'s staff-form switch). Going forward a
/// job's adult coach form is one of three substantive shapes:</para>
/// <list type="bullet">
///   <item><description><b>AC1 — Adult Coach (Standard)</b>: just Special Requests.</description></item>
///   <item><description><b>AC2 — Adult Coach (Apparel)</b>: jersey / shorts / waist / shoe sizes + Special Requests.</description></item>
///   <item><description><b>AC3 — Adult Coach (Shirt + Shoe)</b>: jersey + shoe sizes only + Special Requests
///   (the faithful legacy <c>StaffASL</c> subset — a strict subset of AC2, kept distinct so ASL jobs don't
///   over-collect shorts/waist).</description></item>
/// </list>
///
/// <para><b><c>sportAssnId</c>/USLax is a capability, NOT a profile.</b> Whether a coach form requires a
/// USA Lacrosse number is derived by <c>JobRepository.MetadataRequiresUsLax</c> purely from the presence of
/// a <i>required</i> <c>sportAssnId</c> field — so "with USLax" and "without USLax" are the same profile with
/// one extra field, never separate forms. <see cref="MapLegacy"/> returns the capability as a per-job bool;
/// <see cref="BuildRoleSet"/> prepends the required <c>sportAssnId</c> field iff it is on.</para>
/// </summary>
public static class AdultFormCatalog
{
    // ── Canonical profile codes (ours; analogous to the player PP##/CAC## profile-types) ──
    public const string AC1 = "AC1";
    public const string AC2 = "AC2";
    public const string AC3 = "AC3";

    public static readonly IReadOnlyList<string> AllProfiles = new[] { AC1, AC2, AC3 };

    public static bool IsKnownProfile(string? profile) =>
        !string.IsNullOrWhiteSpace(profile) &&
        AllProfiles.Any(p => string.Equals(p, profile, StringComparison.OrdinalIgnoreCase));

    public static string DisplayName(string profile) => Canonical(profile) switch
    {
        AC1 => "Adult Coach (Standard)",
        AC2 => "Adult Coach (Apparel)",
        AC3 => "Adult Coach (Shirt + Shoe)",
        _ => profile
    };

    /// <summary>Normalizes casing to the canonical code, or returns the input unchanged if unknown.</summary>
    public static string Canonical(string profile) =>
        AllProfiles.FirstOrDefault(p => string.Equals(p, profile, StringComparison.OrdinalIgnoreCase)) ?? profile;

    // ────────────────────────────────────────────────────────────────────────────────────
    // (a) Legacy → canonical mapping
    // ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a legacy <c>RegformName_Coach</c> value to (canonical profile, requiresUsLax). Mirrors the exact
    /// legacy routing in <c>StartARegistrationController</c>: only <c>StaffSTEPS</c>/<c>StaffLaxValidate</c>/
    /// <c>StaffLaxValidatePlus</c>/<c>StaffASL</c> are special-cased; everything else (incl. <c>CP-STEPS</c>,
    /// <c>RegAdult_WANTTOCOACH_RegForm</c>, the two "Default" spellings, and null/empty) falls to the base
    /// coach form (AC1).
    /// </summary>
    public static (string Profile, bool RequiresUsLax) MapLegacy(string? regformNameCoach)
    {
        var v = (regformNameCoach ?? string.Empty).Trim();

        if (Eq(v, "StaffSTEPS")) return (AC2, false);            // full apparel (jersey/shorts/waist/shoe)
        if (Eq(v, "StaffLaxValidatePlus")) return (AC2, true);   // full apparel + USLax
        if (Eq(v, "StaffLaxValidate")) return (AC1, true);       // base + USLax
        if (Eq(v, "StaffASL")) return (AC3, false);              // shirt + shoe ONLY (legacy StaffASL subset)

        // Default "Staff" base coach form: RegAdult_WANTTOCOACH_RegForm, Defalt_Form, Default_Form,
        // CP-STEPS, and anything unrecognized.
        return (AC1, false);
    }

    // ────────────────────────────────────────────────────────────────────────────────────
    // (b) Materialize the three-role set for a job
    // ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full role-keyed adult metadata (UnassignedAdult/Referee/Recruiter) for a job of the given
    /// profile. The coach (UnassignedAdult) block carries the profile's substantive fields, with a required
    /// <c>sportAssnId</c> prepended iff <paramref name="requiresUsLax"/>. Referee and Recruiter are uniform.
    /// Every field is fully formed (camelCase Name, PascalCase DbColumn, Order, Visibility, Validation, and —
    /// for apparel SELECTs — inline Options + a matching <c>ListSizes_*</c> DataSource).
    /// </summary>
    public static AdultRoleMetadataSet BuildRoleSet(string profile, bool requiresUsLax) => new()
    {
        UnassignedAdult = BuildCoach(Canonical(profile), requiresUsLax),
        Referee = BuildReferee(),
        Recruiter = BuildRecruiter()
    };

    private static ProfileMetadata BuildCoach(string profile, bool requiresUsLax)
    {
        var fields = new List<ProfileMetadataField>();
        var order = 1;

        // USLax capability (orthogonal to the profile): a required USA Lacrosse number. Its presence is what
        // drives MetadataRequiresUsLax server-side and the inline 2FA UI (frontend isUsLaxField: name==='sportassnid').
        if (requiresUsLax)
        {
            fields.Add(UsLaxField(order++));
        }

        // Apparel: sizes carried as legacy option lists (inline Options + per-job ListSizes_*).
        // AC2 = full apparel (jersey/shorts/waist/shoe); AC3 = shirt+shoe subset (legacy StaffASL).
        // Display names are the legacy DataAnnotation [Display(Name=...)] strings, verbatim from the staff
        // RegformFields view models (StaffSTEPS / StaffLaxValidatePlus / StaffASL all agree on these).
        if (Eq(profile, AC2))
        {
            fields.Add(Size("jerseySize", "JerseySize", "Men's Shirt Size", "ListSizes_Jersey", JerseySizes, order++));
            fields.Add(Size("shortsSize", "ShortsSize", "Men's or Women's Short Size", "ListSizes_Shorts", ShortsSizes, order++));
            // Waist is overloaded onto the legacy Sweatpants column (no dedicated waist column exists).
            fields.Add(Size("sweatpants", "Sweatpants", "Men's Waist Size", "ListSizes_Sweatpants", WaistSizes, order++));
            fields.Add(Size("shoes", "Shoes", "Shoe Size", "ListSizes_Shoes", ShoeSizes, order++));
        }
        else if (Eq(profile, AC3))
        {
            fields.Add(Size("jerseySize", "JerseySize", "Men's Shirt Size", "ListSizes_Jersey", JerseySizes, order++));
            fields.Add(Size("shoes", "Shoes", "Shoe Size", "ListSizes_Shoes", ShoeSizes, order++));
        }

        // Special Requests: matches the current UnassignedAdult fallback — an OPTIONAL free-text note (the
        // team-request multi-select is the primary team-preference mechanism in the new system).
        fields.Add(new ProfileMetadataField
        {
            Name = "specialRequests",
            DbColumn = "SpecialRequests",
            DisplayName = "Anything else the director should know?",
            InputType = "TEXTAREA",
            Order = order,
            Visibility = "public"
        });

        return new ProfileMetadata { Fields = fields };
    }

    // Referee: single uniform Special Requests (required), matching BuildFallbackFields(Referee).
    private static ProfileMetadata BuildReferee() => new()
    {
        Fields = new List<ProfileMetadataField>
        {
            new()
            {
                Name = "specialRequests",
                DbColumn = "SpecialRequests",
                DisplayName = "Special Requests",
                InputType = "TEXTAREA",
                Order = 1,
                Visibility = "public",
                Validation = new FieldValidation { Required = true }
            }
        }
    };

    // Recruiter: single uniform College/University (required TEXT), matching BuildFallbackFields(Recruiter).
    private static ProfileMetadata BuildRecruiter() => new()
    {
        Fields = new List<ProfileMetadataField>
        {
            new()
            {
                Name = "specialRequests",
                DbColumn = "SpecialRequests",
                DisplayName = "College / University",
                InputType = "TEXT",
                Order = 1,
                Visibility = "public",
                Validation = new FieldValidation { Required = true }
            }
        }
    };

    /// <summary>
    /// The apparel <c>ListSizes_*</c> option sets a migration should seed into <c>Jobs.JsonOptions</c> for AC2
    /// jobs (upsert-if-absent), so the sizes are admin-editable via the option-set editor. Values mirror the
    /// legacy <c>StaffSTEPSController.GetList*Sizes()</c>. Keys equal the fields' DataSource for exact-match
    /// runtime enrichment (<c>ProfileMetadataService.EnrichOptionsFromJson</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, List<ProfileFieldOption>> ApparelOptionSets { get; } =
        new Dictionary<string, List<ProfileFieldOption>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ListSizes_Jersey"] = JerseySizes,
            ["ListSizes_Shorts"] = ShortsSizes,
            ["ListSizes_Sweatpants"] = WaistSizes,
            ["ListSizes_Shoes"] = ShoeSizes
        };

    // ── legacy option lists (StaffSTEPSController.GetList*Sizes) ──
    private static List<ProfileFieldOption> JerseySizes => Opts("SM", "MD", "LG", "XL", "XXL", "XXXL");
    private static List<ProfileFieldOption> ShortsSizes => Opts("SM", "MD", "LG", "XL", "XXL", "XXXL");
    private static List<ProfileFieldOption> WaistSizes => Opts("28", "30", "32", "34", "36", "38", "40", "42", "44", "46");
    private static List<ProfileFieldOption> ShoeSizes => Opts(
        "5", "5.5", "6", "6.5", "7", "7.5", "8", "8.5", "9", "9.5", "10", "10.5",
        "11", "11.5", "12", "12.5", "13", "13.5", "14", "14.5", "15", "15.5", "16");

    /// <summary>The names that make up the USLax capability overlay on the coach form (capability-managed,
    /// never edited as substantive profile fields).</summary>
    public static readonly IReadOnlySet<string> UsLaxCapabilityFieldNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sportAssnId", "sportAssnIdexpDate" };

    /// <summary>The required USA Lacrosse number field prepended to a coach form when USLax is on.</summary>
    public static ProfileMetadataField UsLaxField(int order = 1) => new()
    {
        Name = "sportAssnId",
        DbColumn = "SportAssnId",
        DisplayName = "USA Lacrosse Number",
        InputType = "TEXT",
        Order = order,
        Visibility = "public",
        Validation = new FieldValidation { Required = true, MinLength = 7, MaxLength = 12 }
    };

    // ── helpers ──
    private static ProfileMetadataField Size(string name, string dbColumn, string display, string dataSource, List<ProfileFieldOption> options, int order) => new()
    {
        Name = name,
        DbColumn = dbColumn,
        DisplayName = display,
        InputType = "SELECT",
        DataSource = dataSource,
        Options = options,
        Order = order,
        Visibility = "public",
        Validation = new FieldValidation { Required = true }
    };

    private static List<ProfileFieldOption> Opts(params string[] values) =>
        values.Select(v => new ProfileFieldOption { Value = v, Label = v }).ToList();

    private static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
