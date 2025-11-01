namespace TSIC.API.Dtos;

/// <summary>
/// Represents the complete metadata structure for a player profile
/// </summary>
public class ProfileMetadata
{
    public List<ProfileMetadataField> Fields { get; set; } = new();

    /// <summary>
    /// Source information for tracking migration
    /// </summary>
    public ProfileMetadataSource? Source { get; set; }
}

/// <summary>
/// Individual field metadata for dynamic form generation
/// </summary>
public class ProfileMetadataField
{
    /// <summary>
    /// Property name in camelCase (e.g., "firstName")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Database column name in original case (e.g., "FirstName")
    /// </summary>
    public string DbColumn { get; set; } = string.Empty;

    /// <summary>
    /// Display label for the field
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Input type: TEXT, EMAIL, DATE, NUMBER, SELECT, CHECKBOX, FILE, HIDDEN, etc.
    /// </summary>
    public string InputType { get; set; } = "TEXT";

    /// <summary>
    /// For SELECT inputs: teams, positions, gradYears, genders, etc.
    /// </summary>
    public string? DataSource { get; set; }

    /// <summary>
    /// Validation rules
    /// </summary>
    public FieldValidation? Validation { get; set; }

    /// <summary>
    /// Display order (1-based)
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// If true, only admin can see/edit this field
    /// </summary>
    public bool AdminOnly { get; set; }

    /// <summary>
    /// If true, field is computed and not editable
    /// </summary>
    public bool Computed { get; set; }

    /// <summary>
    /// Conditional display rules
    /// </summary>
    public FieldCondition? ConditionalOn { get; set; }
}

/// <summary>
/// Validation rules for a field
/// </summary>
public class FieldValidation
{
    public bool Required { get; set; }
    public bool Email { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string? Pattern { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string? Compare { get; set; }
    public string? Remote { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Conditional display rules
/// </summary>
public class FieldCondition
{
    public string Field { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string Operator { get; set; } = "equals"; // equals, notEquals, greaterThan, etc.
}

/// <summary>
/// Source tracking for migrations
/// </summary>
public class ProfileMetadataSource
{
    public string SourceFile { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public DateTime MigratedAt { get; set; }
    public string MigratedBy { get; set; } = string.Empty;
}
