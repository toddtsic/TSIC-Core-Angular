using TSIC.Contracts.Dtos;
using TSIC.Domain.Adults;

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

    // ── Adult apparel option-set keys (Jobs.JsonOptions) ──
    // Deliberately NAMESPACED away from the player apparel keys (ListSizes_Jersey/Shorts/Shoes). Player and
    // adult are independent forms whose size lists a director sets separately — mirroring the legacy split
    // where player sizes came from Jobs.JsonOptions but staff sizes were the hardcoded StaffSTEPSController
    // lists. Reusing the player keys made the coach form inherit the player's list; these Coach* keys fix that.
    public const string CoachJerseyKey = "ListSizes_CoachJersey";
    public const string CoachShortsKey = "ListSizes_CoachShorts";
    public const string CoachWaistKey = "ListSizes_CoachWaist";
    public const string CoachShoesKey = "ListSizes_CoachShoes";

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

    // ── USLax pipe tokens on RegformName_Coach ──
    // The capability is encoded as a suffix token — "StaffSTEPS|USLAX-R" — mirroring the pipe encoding
    // Jobs.CoreRegformPlayer already uses for the player side ("PP27|BYGRADYEAR"). varchar(50) has ample
    // headroom: the longest form name is 28 chars and the longest token adds 9.
    public const string UsLaxRequiredToken = "USLAX-R";
    public const string UsLaxOptionalToken = "USLAX-NR";

    /// <summary>
    /// Maps a <c>RegformName_Coach</c> value to (canonical profile, USLax mode).
    ///
    /// <para>The value is <c>formName[|TOKEN…]</c>. The form name is matched exactly as the legacy routing
    /// in <c>StartARegistrationController</c> does: only <c>StaffSTEPS</c>/<c>StaffLaxValidate</c>/
    /// <c>StaffLaxValidatePlus</c>/<c>StaffASL</c> are special-cased; everything else (incl.
    /// <c>CP-STEPS</c>, <c>RegAdult_WANTTOCOACH_RegForm</c>, the two "Default" spellings, and null/empty)
    /// falls to the base coach form (AC1).</para>
    ///
    /// <para><b>Precedence:</b> an explicit USLax token always wins. With no token the form name supplies
    /// the default, so the legacy USLax names keep meaning what they always meant.</para>
    /// </summary>
    public static (string Profile, AdultUsLaxMode UsLax) MapLegacy(string? regformNameCoach)
    {
        var raw = (regformNameCoach ?? string.Empty).Trim();

        // Split the identity from its capability tokens. Must happen BEFORE the name match — an
        // un-split piped value would miss every case below and silently downgrade the job to AC1.
        var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var formName = parts.Length > 0 ? parts[0] : string.Empty;

        AdultUsLaxMode? explicitMode = null;
        for (var i = 1; i < parts.Length; i++)
        {
            if (Eq(parts[i], UsLaxRequiredToken)) explicitMode = AdultUsLaxMode.Required;
            else if (Eq(parts[i], UsLaxOptionalToken)) explicitMode = AdultUsLaxMode.Optional;
        }

        var (profile, nameDefault) = MapFormName(formName);
        return (profile, explicitMode ?? nameDefault);
    }

    /// <summary>The bare form name's (profile, default USLax mode), ignoring any tokens.</summary>
    private static (string Profile, AdultUsLaxMode UsLax) MapFormName(string formName)
    {
        if (Eq(formName, "StaffSTEPS")) return (AC2, AdultUsLaxMode.None);                    // full apparel
        if (Eq(formName, "StaffLaxValidatePlus")) return (AC2, AdultUsLaxMode.Required);      // full apparel + USLax
        if (Eq(formName, "StaffLaxValidate")) return (AC1, AdultUsLaxMode.Required);          // base + USLax
        if (Eq(formName, "StaffASL")) return (AC3, AdultUsLaxMode.None);                      // shirt + shoe subset

        // Default "Staff" base coach form: RegAdult_WANTTOCOACH_RegForm, Defalt_Form, Default_Form,
        // CP-STEPS, and anything unrecognized.
        return (AC1, AdultUsLaxMode.None);
    }

    /// <summary>
    /// Whether a profile supports the USA-Lacrosse capability overlay. Now true for every profile: the
    /// capability rides on a pipe token rather than on the choice of form name, so AC3 + USLax — previously
    /// unrepresentable, since no legacy name encoded that pair — is expressible like any other combination.
    /// </summary>
    public static bool CanRequireUsLax(string profile) => IsKnownProfile(profile);

    /// <summary>
    /// Reverse of <see cref="MapLegacy"/>: the <c>RegformName_Coach</c> string for a (profile, USLax mode)
    /// pair, chosen so <c>MapLegacy</c> round-trips back to the same pair. Every combination is now
    /// representable, so this returns <c>null</c> only for an unknown profile.
    ///
    /// <para>The mode is always written as an explicit token — never left implicit in a legacy name — so
    /// there is exactly ONE representation of each state and no ambiguity between
    /// <c>StaffLaxValidate</c> and <c>Default_Form|USLAX-R</c>.</para>
    /// </summary>
    public static string? ToLegacyRegformName(string profile, AdultUsLaxMode usLax)
    {
        var baseName = Canonical(profile) switch
        {
            AC1 => "Default_Form",
            AC2 => "StaffSTEPS",
            AC3 => "StaffASL",
            _ => null
        };
        if (baseName is null) return null;

        return usLax switch
        {
            AdultUsLaxMode.Required => $"{baseName}|{UsLaxRequiredToken}",
            AdultUsLaxMode.Optional => $"{baseName}|{UsLaxOptionalToken}",
            _ => baseName
        };
    }

    // ────────────────────────────────────────────────────────────────────────────────────
    // (b) Materialize the three-role set for a job
    // ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full role-keyed adult metadata (UnassignedAdult/Referee/Recruiter) for a job of the given
    /// profile. The coach (UnassignedAdult) block carries the profile's substantive fields, with a
    /// <c>sportAssnId</c> prepended when <paramref name="usLax"/> is not <see cref="AdultUsLaxMode.None"/>.
    /// Referee and Recruiter are uniform. Every field is fully formed (camelCase Name, PascalCase DbColumn,
    /// Order, Visibility, Validation, and — for apparel SELECTs — inline Options + a matching
    /// <c>ListSizes_*</c> DataSource).
    /// </summary>
    public static AdultRoleMetadataSet BuildRoleSet(string profile, AdultUsLaxMode usLax) => new()
    {
        UnassignedAdult = BuildCoach(Canonical(profile), usLax),
        Referee = BuildReferee(),
        Recruiter = BuildRecruiter()
    };

    private static ProfileMetadata BuildCoach(string profile, AdultUsLaxMode usLax)
    {
        var fields = new List<ProfileMetadataField>();
        var order = 1;

        // USLax capability (orthogonal to the profile). Presence drives the inline 2FA UI (frontend
        // isUsLaxField: name==='sportassnid'); REQUIREDNESS is what UsLaxMetadataPolicy.RequiresUsLax reads,
        // so Optional shows the field and hard-validates a supplied number without ever blocking a coach
        // who has none.
        if (usLax != AdultUsLaxMode.None)
        {
            fields.Add(UsLaxField(order++, required: usLax == AdultUsLaxMode.Required));
        }

        // Apparel: sizes carried as legacy option lists (inline Options + per-job ListSizes_*).
        // AC2 = full apparel (jersey/shorts/waist/shoe); AC3 = shirt+shoe subset (legacy StaffASL).
        // Display names are the legacy DataAnnotation [Display(Name=...)] strings, verbatim from the staff
        // RegformFields view models (StaffSTEPS / StaffLaxValidatePlus / StaffASL all agree on these).
        if (Eq(profile, AC2))
        {
            fields.Add(Size("jerseySize", "JerseySize", "Men's Shirt Size", CoachJerseyKey, JerseySizes, order++));
            fields.Add(Size("shortsSize", "ShortsSize", "Men's or Women's Short Size", CoachShortsKey, ShortsSizes, order++));
            // Waist is overloaded onto the legacy Sweatpants column (no dedicated waist column exists).
            fields.Add(Size("sweatpants", "Sweatpants", "Men's Waist Size", CoachWaistKey, WaistSizes, order++));
            fields.Add(Size("shoes", "Shoes", "Shoe Size", CoachShoesKey, ShoeSizes, order++));
        }
        else if (Eq(profile, AC3))
        {
            fields.Add(Size("jerseySize", "JerseySize", "Men's Shirt Size", CoachJerseyKey, JerseySizes, order++));
            fields.Add(Size("shoes", "Shoes", "Shoe Size", CoachShoesKey, ShoeSizes, order++));
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

    // Referee: optional certification credentials + Special Requests (required). Kept in sync with
    // BuildFallbackFields(Referee).
    //
    // certificationNumber / certificationExpiry map to the SAME Registrations columns the referee
    // CSV/xlsx import writes (SportAssnId / SportAssnIdexpDate), so a self-registered referee and an
    // imported one carry identical credential data and the assignment dropdown shows a cert for either.
    // The field NAME is deliberately NOT "sportAssnId": the wizard treats that name as the coach
    // USA-Lacrosse field and renders a membership-verification widget. Field→column mapping is by
    // DbColumn, so a distinct Name keeps the column target while rendering a plain input. For a referee
    // the USLax stamp path no-ops, so the value is stored raw (a generic certification number, unverified).
    private static ProfileMetadata BuildReferee() => new()
    {
        Fields = new List<ProfileMetadataField>
        {
            new()
            {
                Name = "certificationNumber",
                DbColumn = "SportAssnId",
                DisplayName = "Certification Number",
                InputType = "TEXT",
                Order = 1,
                Visibility = "public",
                Validation = new FieldValidation { Required = false }
            },
            new()
            {
                Name = "certificationExpiry",
                DbColumn = "SportAssnIdexpDate",
                DisplayName = "Certification Expiry",
                InputType = "DATE",
                Order = 2,
                Visibility = "public",
                Validation = new FieldValidation { Required = false }
            },
            new()
            {
                Name = "specialRequests",
                DbColumn = "SpecialRequests",
                DisplayName = "Special Requests",
                InputType = "TEXTAREA",
                Order = 3,
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
            [CoachJerseyKey] = JerseySizes,
            [CoachShortsKey] = ShortsSizes,
            [CoachWaistKey] = WaistSizes,
            [CoachShoesKey] = ShoeSizes
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
    public static ProfileMetadataField UsLaxField(int order = 1, bool required = true) => new()
    {
        Name = "sportAssnId",
        DbColumn = "SportAssnId",
        DisplayName = "USA Lacrosse Number",
        InputType = "TEXT",
        Order = order,
        Visibility = "public",
        Validation = new FieldValidation { Required = required, MinLength = 7, MaxLength = 12 }
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
