namespace TSIC.Contracts.Dtos.DdlOptions;

/// <summary>
/// All 20 dropdown categories for player/team registration forms.
/// Each category is a simple string list (Text always equals Value in the legacy JSON).
/// </summary>
public record JobDdlOptionsDto
{
    // ── Clothing sizes ──
    public required List<string> JerseySizes { get; init; }
    public required List<string> ShortsSizes { get; init; }
    public required List<string> ReversibleSizes { get; init; }
    public required List<string> KiltSizes { get; init; }
    public required List<string> TShirtSizes { get; init; }
    public required List<string> GlovesSizes { get; init; }
    public required List<string> SweatshirtSizes { get; init; }
    public required List<string> ShoesSizes { get; init; }

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
}
