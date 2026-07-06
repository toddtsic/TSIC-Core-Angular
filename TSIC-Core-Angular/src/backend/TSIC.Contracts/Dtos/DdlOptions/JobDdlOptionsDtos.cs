namespace TSIC.Contracts.Dtos.DdlOptions;

/// <summary>
/// All dropdown categories for player/team registration forms and camp roster admin.
/// Each category is a simple string list (Text always equals Value in the legacy JSON).
/// </summary>
public record JobDdlOptionsDto
{
    // ── Clothing sizes (player) ──
    public required List<string> JerseySizes { get; init; }
    public required List<string> ShortsSizes { get; init; }
    public required List<string> ReversibleSizes { get; init; }
    public required List<string> KiltSizes { get; init; }
    public required List<string> TShirtSizes { get; init; }
    public required List<string> GlovesSizes { get; init; }
    public required List<string> SweatshirtSizes { get; init; }
    public required List<string> ShoesSizes { get; init; }

    // ── Clothing sizes (adult / coach) ──
    // Namespaced apart from the player sizes above (ListSizes_Coach* in Jobs.JsonOptions) so the coach
    // registration form's size dropdowns are edited independently of the player form's. See AdultFormCatalog.
    public required List<string> CoachJerseySizes { get; init; }
    public required List<string> CoachShortsSizes { get; init; }
    public required List<string> CoachWaistSizes { get; init; }
    public required List<string> CoachShoesSizes { get; init; }

    // ── Player data ──
    public required List<string> YearsExperience { get; init; }
    public required List<string> Positions { get; init; }
    public required List<string> GradYears { get; init; }
    public required List<string> RecruitingGradYears { get; init; }
    public required List<string> SchoolGrades { get; init; }
    public required List<string> StrongHand { get; init; }
    public required List<string> WhoReferred { get; init; }
    public required List<string> HeightInches { get; init; }
    public required List<string> SkillLevels { get; init; }

    // ── Team & context ──
    public required List<string> Lops { get; init; }
    public required List<string> ClubNames { get; init; }
    public required List<string> PriorSeasonYears { get; init; }

    // ── Camp & context ──
    public required List<string> DayGroups { get; init; }
    public required List<string> NightGroups { get; init; }
}
