using System.Text.Json;
using System.Text.Json.Serialization;
using TSIC.Contracts.Dtos.DdlOptions;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for managing per-job dropdown list options stored in Jobs.JsonOptions.
/// Handles serialization/deserialization between the legacy JSON format and the flat DTO.
/// </summary>
public class DdlOptionsService : IDdlOptionsService
{
    private readonly IDdlOptionsRepository _repository;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null, // Preserve exact property names for legacy compatibility
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public DdlOptionsService(IDdlOptionsRepository repository)
    {
        _repository = repository;
    }

    public async Task<JobDdlOptionsDto> GetOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        var json = await _repository.GetJsonOptionsAsync(jobId, ct);

        if (string.IsNullOrWhiteSpace(json))
            return EmptyDto();

        // Legacy data may contain \r\n — strip before deserializing
        json = json.Replace("\r\n", "");

        var options = JsonSerializer.Deserialize<JobJsonOptions>(json, SerializerOptions);
        if (options is null)
            return EmptyDto();

        return MapToDto(options);
    }

    public async Task SaveOptionsAsync(Guid jobId, JobDdlOptionsDto dto, CancellationToken ct = default)
    {
        var sanitized = SanitizeDto(dto);
        var options = MapFromDto(sanitized);
        var json = JsonSerializer.Serialize(options, SerializerOptions);
        await _repository.UpdateJsonOptionsAsync(jobId, json, ct);
    }

    // ═══════════════════════════════════════
    // Mapping: JobJsonOptions ↔ JobDdlOptionsDto
    // ═══════════════════════════════════════

    private static JobDdlOptionsDto MapToDto(JobJsonOptions options)
    {
        return new JobDdlOptionsDto
        {
            JerseySizes = ExtractValues(options.ListSizes_Jersey),
            ShortsSizes = ExtractValues(options.ListSizes_Shorts),
            ReversibleSizes = ExtractValues(options.ListSizes_Reversible),
            KiltSizes = ExtractValues(options.ListSizes_Kilt),
            TShirtSizes = ExtractValues(options.ListSizes_Tshirt),
            GlovesSizes = ExtractValues(options.ListSizes_Gloves),
            SweatshirtSizes = ExtractValues(options.ListSizes_Sweatshirt),
            ShoesSizes = ExtractValues(options.ListSizes_Shoes),
            YearsExperience = ExtractValues(options.List_YearsExperience),
            Positions = ExtractValues(options.List_Positions),
            GradYears = ExtractValues(options.List_GradYears),
            RecruitingGradYears = ExtractValues(options.List_RecruitingGradYears),
            SchoolGrades = ExtractValues(options.List_SchoolGrades),
            StrongHand = ExtractValues(options.List_StrongHand),
            WhoReferred = ExtractValues(options.List_WhoReferred),
            HeightInches = ExtractValues(options.List_HeightInches),
            SkillLevels = ExtractValues(options.List_SkillLevels),
            Lops = ExtractValues(options.List_Lops),
            ClubNames = ExtractValues(options.List_ClubNames),
            PriorSeasonYears = ExtractValues(options.List_PriorSeasonYears),
        };
    }

    private static JobJsonOptions MapFromDto(JobDdlOptionsDto dto)
    {
        return new JobJsonOptions
        {
            ListSizes_Jersey = ToJsonItems(dto.JerseySizes),
            ListSizes_Shorts = ToJsonItems(dto.ShortsSizes),
            ListSizes_Reversible = ToJsonItems(dto.ReversibleSizes),
            ListSizes_Kilt = ToJsonItems(dto.KiltSizes),
            ListSizes_Tshirt = ToJsonItems(dto.TShirtSizes),
            ListSizes_Gloves = ToJsonItems(dto.GlovesSizes),
            ListSizes_Sweatshirt = ToJsonItems(dto.SweatshirtSizes),
            ListSizes_Shoes = ToJsonItems(dto.ShoesSizes),
            List_YearsExperience = ToJsonItems(dto.YearsExperience),
            List_Positions = ToJsonItems(dto.Positions),
            List_GradYears = ToJsonItems(dto.GradYears),
            List_RecruitingGradYears = ToJsonItems(dto.RecruitingGradYears),
            List_SchoolGrades = ToJsonItems(dto.SchoolGrades),
            List_StrongHand = ToJsonItems(dto.StrongHand),
            List_WhoReferred = ToJsonItems(dto.WhoReferred),
            List_HeightInches = ToJsonItems(dto.HeightInches),
            List_SkillLevels = ToJsonItems(dto.SkillLevels),
            List_Lops = ToJsonItems(dto.Lops),
            List_ClubNames = ToJsonItems(dto.ClubNames),
            List_PriorSeasonYears = ToJsonItems(dto.PriorSeasonYears),
        };
    }

    // ═══════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════

    private static List<string> ExtractValues(List<JsonSelectListItem>? items)
    {
        if (items is null || items.Count == 0)
            return [];

        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.Value))
            .Select(i => i.Value!)
            .ToList();
    }

    private static List<JsonSelectListItem> ToJsonItems(List<string> values)
    {
        return values.Select(v => new JsonSelectListItem { Text = v, Value = v }).ToList();
    }

    private static JobDdlOptionsDto SanitizeDto(JobDdlOptionsDto dto)
    {
        return new JobDdlOptionsDto
        {
            JerseySizes = SanitizeList(dto.JerseySizes),
            ShortsSizes = SanitizeList(dto.ShortsSizes),
            ReversibleSizes = SanitizeList(dto.ReversibleSizes),
            KiltSizes = SanitizeList(dto.KiltSizes),
            TShirtSizes = SanitizeList(dto.TShirtSizes),
            GlovesSizes = SanitizeList(dto.GlovesSizes),
            SweatshirtSizes = SanitizeList(dto.SweatshirtSizes),
            ShoesSizes = SanitizeList(dto.ShoesSizes),
            YearsExperience = SanitizeList(dto.YearsExperience),
            Positions = SanitizeList(dto.Positions),
            GradYears = SanitizeList(dto.GradYears),
            RecruitingGradYears = SanitizeList(dto.RecruitingGradYears),
            SchoolGrades = SanitizeList(dto.SchoolGrades),
            StrongHand = SanitizeList(dto.StrongHand),
            WhoReferred = SanitizeList(dto.WhoReferred),
            HeightInches = SanitizeList(dto.HeightInches),
            SkillLevels = SanitizeList(dto.SkillLevels),
            Lops = SanitizeList(dto.Lops),
            ClubNames = SanitizeList(dto.ClubNames),
            PriorSeasonYears = SanitizeList(dto.PriorSeasonYears),
        };
    }

    /// <summary>
    /// Trim whitespace, remove blanks, remove case-insensitive duplicates (keep first occurrence).
    /// </summary>
    private static List<string> SanitizeList(List<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var raw in values)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            if (!seen.Add(trimmed)) continue;
            result.Add(trimmed);
        }

        return result;
    }

    private static JobDdlOptionsDto EmptyDto() => new()
    {
        JerseySizes = [],
        ShortsSizes = [],
        ReversibleSizes = [],
        KiltSizes = [],
        TShirtSizes = [],
        GlovesSizes = [],
        SweatshirtSizes = [],
        ShoesSizes = [],
        YearsExperience = [],
        Positions = [],
        GradYears = [],
        RecruitingGradYears = [],
        SchoolGrades = [],
        StrongHand = [],
        WhoReferred = [],
        HeightInches = [],
        SkillLevels = [],
        Lops = [],
        ClubNames = [],
        PriorSeasonYears = [],
    };

    // ═══════════════════════════════════════
    // Internal JSON model (legacy-compatible)
    // ═══════════════════════════════════════

    private sealed class JobJsonOptions
    {
        public List<JsonSelectListItem>? ListSizes_Jersey { get; set; }
        public List<JsonSelectListItem>? ListSizes_Shorts { get; set; }
        public List<JsonSelectListItem>? ListSizes_Reversible { get; set; }
        public List<JsonSelectListItem>? ListSizes_Kilt { get; set; }
        public List<JsonSelectListItem>? ListSizes_Tshirt { get; set; }
        public List<JsonSelectListItem>? ListSizes_Gloves { get; set; }
        public List<JsonSelectListItem>? ListSizes_Sweatshirt { get; set; }
        public List<JsonSelectListItem>? ListSizes_Shoes { get; set; }
        public List<JsonSelectListItem>? List_YearsExperience { get; set; }
        public List<JsonSelectListItem>? List_Positions { get; set; }
        public List<JsonSelectListItem>? List_GradYears { get; set; }
        public List<JsonSelectListItem>? List_RecruitingGradYears { get; set; }
        public List<JsonSelectListItem>? List_SchoolGrades { get; set; }
        public List<JsonSelectListItem>? List_StrongHand { get; set; }
        public List<JsonSelectListItem>? List_WhoReferred { get; set; }
        public List<JsonSelectListItem>? List_HeightInches { get; set; }
        public List<JsonSelectListItem>? List_ClubNames { get; set; }
        public List<JsonSelectListItem>? List_Lops { get; set; }
        public List<JsonSelectListItem>? List_PriorSeasonYears { get; set; }
        public List<JsonSelectListItem>? List_SkillLevels { get; set; }
    }

    private sealed class JsonSelectListItem
    {
        public string? Text { get; set; }
        public string? Value { get; set; }
    }
}
