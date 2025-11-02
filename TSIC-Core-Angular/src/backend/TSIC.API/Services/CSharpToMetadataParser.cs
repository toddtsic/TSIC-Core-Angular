using System.Text.RegularExpressions;
using TSIC.API.Dtos;

namespace TSIC.API.Services;

public class CSharpToMetadataParser
{
    private readonly ILogger<CSharpToMetadataParser> _logger;

    public CSharpToMetadataParser(ILogger<CSharpToMetadataParser> logger)
    {
        _logger = logger;
    }

    public async Task<ProfileMetadata> ParseProfileAsync(
        string profileSourceCode,
        string baseClassSourceCode,
        string profileType,
        string commitSha,
        string? viewContent = null)
    {

        var metadata = new ProfileMetadata
        {
            Fields = new List<ProfileMetadataField>(),
            Source = new ProfileMetadataSource
            {
                SourceFile = $"{profileType}ViewModel.cs",
                Repository = "toddtsic/TSIC-Unify-2024",
                CommitSha = commitSha,
                MigratedAt = DateTime.UtcNow,
                MigratedBy = "VIEW-FIRST ALGORITHM v1.0"
            }
        };

        if (string.IsNullOrWhiteSpace(viewContent))
        {
            return metadata;
        }

        // Step 1: Parse .cshtml view file to extract fields in display order
        var viewFields = ParseViewFile(viewContent);

        // Step 2: Parse C# source code to extract property metadata (validation, display names, etc.)
        var propertyMetadata = ParseCSharpProperties(profileSourceCode, baseClassSourceCode);

        // Step 3: Build metadata from view fields, enriched with C# metadata
        int order = 1;
        foreach (var fieldInfo in viewFields)
        {
            var field = new ProfileMetadataField
            {
                Name = ToCamelCase(fieldInfo.Name),
                DbColumn = fieldInfo.Name,
                DisplayName = SplitPascalCase(fieldInfo.Name), // Default, will be overridden below
                Order = order++,
                Visibility = fieldInfo.Visibility,
                InputType = fieldInfo.InputType,
                Computed = false
            };

            // Enrich with C# property metadata if available
            if (propertyMetadata.TryGetValue(fieldInfo.Name, out var propMeta))
            {
                field.DisplayName = propMeta.DisplayName ?? field.DisplayName;
                field.Validation = propMeta.Validation;

                // Refine input type if C# provides more specific information
                if (!string.IsNullOrEmpty(propMeta.InputType))
                {
                    field.InputType = propMeta.InputType;
                }
                if (!string.IsNullOrEmpty(propMeta.DataSource))
                {
                    field.DataSource = propMeta.DataSource;
                }
            }

            metadata.Fields.Add(field);
        }

        return await Task.FromResult(metadata);
    }

    /// <summary>
    /// Parse .cshtml view file to extract fields in order (top-to-bottom)
    /// </summary>
    private List<ViewFieldInfo> ParseViewFile(string viewContent)
    {
        var fields = new List<ViewFieldInfo>();
        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pattern 1: <input asp-for="FieldName" type="hidden" />
        var hiddenInputPattern = @"<input[^>]*\btype\s*=\s*[""']hidden[""'][^>]*\basp-for\s*=\s*[""']([^""']+)[""'][^>]*>|<input[^>]*\basp-for\s*=\s*[""']([^""']+)[""'][^>]*\btype\s*=\s*[""']hidden[""'][^>]*>";
        var hiddenMatches = Regex.Matches(viewContent, hiddenInputPattern, RegexOptions.IgnoreCase);

        foreach (Match match in hiddenMatches)
        {
            var aspForValue = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var fieldName = ExtractFieldName(aspForValue);

            if (!string.IsNullOrEmpty(fieldName) && seenFields.Add(fieldName))
            {
                fields.Add(new ViewFieldInfo
                {
                    Name = fieldName,
                    Visibility = "hidden",
                    InputType = "HIDDEN"
                });
            }
        }

        // Pattern 2: @Html.HiddenFor(m => m.FieldName)
        var hiddenHelperPattern = @"@Html\.HiddenFor\(m\s*=>\s*m\.([^)]+)\)";
        foreach (Match match in Regex.Matches(viewContent, hiddenHelperPattern))
        {
            var fieldName = ExtractFieldName(match.Groups[1].Value);

            if (!string.IsNullOrEmpty(fieldName) && seenFields.Add(fieldName))
            {
                fields.Add(new ViewFieldInfo
                {
                    Name = fieldName,
                    Visibility = "hidden",
                    InputType = "HIDDEN"
                });
            }
        }

        // Pattern 3: <input asp-for="FieldName" /> (not hidden)
        var inputPattern = @"<input[^>]*\basp-for\s*=\s*[""']([^""']+)[""'][^>]*>";
        foreach (Match match in Regex.Matches(viewContent, inputPattern, RegexOptions.IgnoreCase))
        {
            var aspForValue = match.Groups[1].Value;
            var fieldName = ExtractFieldName(aspForValue);

            // Skip if already added as hidden or if it has type="hidden"
            if (!string.IsNullOrEmpty(fieldName) && seenFields.Add(fieldName))
            {
                fields.Add(new ViewFieldInfo
                {
                    Name = fieldName,
                    Visibility = "public",
                    InputType = "TEXT"
                });
            }
        }

        // Pattern 3b: <select asp-for="FieldName" ...>
        var selectPattern = @"<select[^>]*\basp-for\s*=\s*[""']([^""']+)[""'][^>]*>";
        foreach (Match match in Regex.Matches(viewContent, selectPattern, RegexOptions.IgnoreCase))
        {
            var aspForValue = match.Groups[1].Value;
            var fieldName = ExtractFieldName(aspForValue);

            if (!string.IsNullOrEmpty(fieldName) && seenFields.Add(fieldName))
            {
                fields.Add(new ViewFieldInfo
                {
                    Name = fieldName,
                    Visibility = "public",
                    InputType = "SELECT"
                });
            }
        }

        // Pattern 4: @Html.TextBoxFor / CheckBoxFor / etc
        var htmlHelperPattern = @"@Html\.(TextBoxFor|CheckBoxFor|TextAreaFor|DropDownListFor|EditorFor)\(m\s*=>\s*m\.([^),\s]+)";
        foreach (Match match in Regex.Matches(viewContent, htmlHelperPattern))
        {
            var helperType = match.Groups[1].Value;
            var fieldName = ExtractFieldName(match.Groups[2].Value);

            if (!string.IsNullOrEmpty(fieldName) && seenFields.Add(fieldName))
            {
                var inputType = helperType switch
                {
                    "CheckBoxFor" => "CHECKBOX",
                    "TextAreaFor" => "TEXTAREA",
                    "DropDownListFor" => "SELECT",
                    _ => "TEXT"
                };

                fields.Add(new ViewFieldInfo
                {
                    Name = fieldName,
                    Visibility = "public",
                    InputType = inputType
                });
            }
        }

        return fields;
    }

    /// <summary>
    /// Parse C# source code to extract property metadata (Display names, validation attributes, etc.)
    /// </summary>
    private Dictionary<string, PropertyMetadata> ParseCSharpProperties(string profileSource, string baseSource)
    {
        var metadata = new Dictionary<string, PropertyMetadata>(StringComparer.OrdinalIgnoreCase);

        // Parse base class properties first
        ParseClassProperties(baseSource, metadata);

        // Parse profile class properties (will override base if same property name)
        ParseClassProperties(profileSource, metadata);

        return metadata;
    }

    private void ParseClassProperties(string sourceCode, Dictionary<string, PropertyMetadata> metadata)
    {
        // POCOs have multiple classes: look for {Profile}_Player_ViewModel (registrant) and {Profile}_PlayerSearch_ViewModel (admin)
        // Parse registrant class first (has priority), then admin class (only for admin-only fields)

        // Find registrant class: {Profile}_Player_ViewModel pattern
        var registrantMatch = Regex.Match(sourceCode, @"^\s*public\s+class\s+(\w+_Player_ViewModel)\s", RegexOptions.Multiline);

        // Find admin/search class: {Profile}_PlayerSearch_ViewModel pattern
        var adminMatch = Regex.Match(sourceCode, @"^\s*public\s+class\s+(\w+_PlayerSearch_ViewModel)\s", RegexOptions.Multiline);

        if (registrantMatch.Success && adminMatch.Success)
        {
            _logger.LogInformation("Found registrant class '{Registrant}' and admin class '{Admin}'", registrantMatch.Groups[1].Value, adminMatch.Groups[1].Value);

            // Get the registrant class code (from match to admin match)
            var registrantStart = registrantMatch.Index;
            var registrantEnd = adminMatch.Index;
            var registrantCode = sourceCode.Substring(registrantStart, registrantEnd - registrantStart);

            // Parse registrant class properties (these take priority)
            var registrantMetadata = new Dictionary<string, PropertyMetadata>(StringComparer.OrdinalIgnoreCase);
            ParseSingleClassProperties(registrantCode, registrantMetadata);
            _logger.LogInformation("Parsed {Count} properties from registrant class", registrantMetadata.Count);

            // Get the admin class code (from admin match to end)
            var adminStart = adminMatch.Index;
            var adminCode = sourceCode.Substring(adminStart);

            // Parse admin class properties
            var adminMetadata = new Dictionary<string, PropertyMetadata>(StringComparer.OrdinalIgnoreCase);
            ParseSingleClassProperties(adminCode, adminMetadata);
            _logger.LogInformation("Parsed {Count} properties from admin class", adminMetadata.Count);

            // Add registrant properties first
            foreach (var kvp in registrantMetadata)
            {
                _logger.LogDebug("Adding registrant property: {Name} with Display='{Display}'", kvp.Key, kvp.Value.DisplayName);
                metadata[kvp.Key] = kvp.Value;
            }

            // Add admin properties ONLY if they don't exist in registrant (admin-only fields)
            foreach (var kvp in adminMetadata)
            {
                if (!metadata.ContainsKey(kvp.Key))
                {
                    _logger.LogDebug("Adding admin-only property: {Name} with Display='{Display}'", kvp.Key, kvp.Value.DisplayName);
                    metadata[kvp.Key] = kvp.Value;
                }
                else
                {
                    _logger.LogDebug("Skipping admin property {Name} (exists in registrant with Display='{Display}')", kvp.Key, metadata[kvp.Key].DisplayName);
                }
            }
        }
        else
        {
            // Single class file (base class or old format) - parse entire file
            ParseSingleClassProperties(sourceCode, metadata);
        }
    }

    private void ParseSingleClassProperties(string sourceCode, Dictionary<string, PropertyMetadata> metadata)
    {
        // Pattern to match ONLY the property declaration (not attributes)
        var propertyPattern = @"^\s*public\s+(\w+\??)\s+(\w+)\s*\{\s*get;\s*set;\s*\}";
        var matches = Regex.Matches(sourceCode, propertyPattern, RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            var propertyType = match.Groups[1].Value;
            var propertyName = match.Groups[2].Value;

            // Extract all attributes before this property
            var propMeta = new PropertyMetadata
            {
                PropertyType = propertyType
            };

            // Get text before the property declaration to find attributes
            var beforeProperty = sourceCode.Substring(0, match.Index);
            var lines = beforeProperty.Split('\n');

            // Look backwards for attributes (they appear right before the property)
            var attributeLines = new List<string>();
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.StartsWith('['))
                {
                    attributeLines.Insert(0, line);
                }
                else if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("//"))
                {
                    // Stop when we hit non-attribute, non-comment line
                    break;
                }
            }

            var attributesText = string.Join("\n", attributeLines);

            // Parse Display attribute
            var displayMatch = Regex.Match(attributesText, @"\[Display\s*\([^)]*Name\s*=\s*""([^""]+)""[^)]*\)\]");
            if (displayMatch.Success)
            {
                propMeta.DisplayName = displayMatch.Groups[1].Value;
            }

            // Parse validation attributes
            propMeta.Validation = ParseValidationAttributes(attributesText, propertyType);

            // Infer input type and data source
            propMeta.InputType = InferInputType(propertyName, propertyType);
            propMeta.DataSource = InferDataSource(propertyName);

            metadata[propertyName] = propMeta;
        }
    }

    private static FieldValidation? ParseValidationAttributes(string attributesText, string propertyType)
    {
        if (string.IsNullOrWhiteSpace(attributesText))
            return null;

        var validation = new FieldValidation();
        var hasValidation = false;

        // [Required] or [Required(ErrorMessage = "...")]
        if (Regex.IsMatch(attributesText, @"\[Required(?:\([^\)]*\))?\]"))
        {
            validation.Required = true;
            hasValidation = true;

            // For bool properties, [Required] means the checkbox must be checked
            if (propertyType == "bool")
            {
                validation.RequiredTrue = true;
            }
        }

        // [EmailAddress] or [EmailAddress(ErrorMessage = "...")]
        if (Regex.IsMatch(attributesText, @"\[EmailAddress(?:\([^\)]*\))?\]"))
        {
            validation.Email = true;
            hasValidation = true;
        }

        // [StringLength(50, MinimumLength = 2)]
        var stringLengthMatch = Regex.Match(attributesText, @"\[StringLength\s*\(\s*(\d+)\s*(?:,\s*MinimumLength\s*=\s*(\d+))?\s*\)\]");
        if (stringLengthMatch.Success)
        {
            validation.MaxLength = int.Parse(stringLengthMatch.Groups[1].Value);
            if (stringLengthMatch.Groups[2].Success)
            {
                validation.MinLength = int.Parse(stringLengthMatch.Groups[2].Value);
            }
            hasValidation = true;
        }

        // [Range(1, 100)] or [Range(typeof(bool), "true", "true")]
        var rangeMatch = Regex.Match(attributesText, @"\[Range\s*\(\s*(?:typeof\(bool\)|(\d+(?:\.\d+)?))\s*,\s*(?:""true""|(\d+(?:\.\d+)?))\s*(?:,\s*(?:""true""|(\d+(?:\.\d+)?)))?\s*\)\]");
        if (rangeMatch.Success)
        {
            // Check if it's the RequiredTrue pattern for checkboxes
            if (propertyType == "bool" && attributesText.Contains("typeof(bool)"))
            {
                validation.RequiredTrue = true;
            }
            else if (rangeMatch.Groups[1].Success)
            {
                validation.Min = double.Parse(rangeMatch.Groups[1].Value);
                if (rangeMatch.Groups[2].Success)
                {
                    validation.Max = double.Parse(rangeMatch.Groups[2].Value);
                }
            }
            hasValidation = true;
        }

        // [RegularExpression(@"pattern")]
        var regexMatch = Regex.Match(attributesText, @"\[RegularExpression\s*\(\s*@?""([^""]+)""\s*\)\]");
        if (regexMatch.Success)
        {
            validation.Pattern = regexMatch.Groups[1].Value;
            hasValidation = true;
        }

        // [Compare("IsTrue")] - for checkboxes that must be checked
        if (propertyType == "bool" && Regex.IsMatch(attributesText, @"\[Compare\s*\(\s*""IsTrue"""))
        {
            validation.RequiredTrue = true;
            hasValidation = true;
        }

        return hasValidation ? validation : null;
    }

    private static string? InferInputType(string propertyName, string propertyType)
    {
        // Infer based on property type
        if (propertyType == "bool")
            return "CHECKBOX";

        if (propertyType == "DateTime" || propertyType == "DateTime?")
            return "DATE";

        if (propertyType == "int" || propertyType == "int?" || propertyType == "decimal" || propertyType == "decimal?" || propertyType == "double" || propertyType == "double?")
            return "NUMBER";

        // Infer based on property name
        if (propertyName.EndsWith("Email", StringComparison.OrdinalIgnoreCase))
            return "EMAIL";

        if (propertyName.EndsWith("Phone", StringComparison.OrdinalIgnoreCase) || propertyName.Contains("Phone"))
            return "TEL";

        if (propertyName.EndsWith("Id") || propertyName == "Gender" || propertyName == "Position" || propertyName == "GradYear" || propertyName.EndsWith("Size") || propertyName == "State")
            return "SELECT";

        return null; // Use default from view
    }

    private static string? InferDataSource(string propertyName)
    {
        return propertyName switch
        {
            "State" => "List_States",
            "Gender" => "List_Genders",
            "Position" => "List_Positions",
            "GradYear" => "List_GradYears",
            "RecruitingGradYear" => "List_RecruitingGradYears",
            "SchoolGrade" => "List_SchoolGrades",
            "SkillLevel" => "List_SkillLevels",
            "SportYearsExp" => "List_YearsExperience",
            "StrongHand" => "List_StrongHand",
            "WhoReferred" => "List_WhoReferred",
            "HeightInches" => "List_HeightInches",
            "GlovesSize" => "ListSizes_Gloves",
            "JerseySize" => "ListSizes_Jersey",
            "KiltSize" => "ListSizes_Kilt",
            "ReversibleSize" => "ListSizes_Reversible",
            "ShoesSize" => "ListSizes_Shoes",
            "ShortsSize" => "ListSizes_Shorts",
            "SweatshirtSize" => "ListSizes_Sweatshirt",
            "TshirtSize" => "ListSizes_Tshirt",
            var name when name.EndsWith("Size") => $"ListSizes_{name.Replace("Size", "")}",
            _ => null
        };
    }

    /// <summary>
    /// Extract field name from asp-for path like "FamilyPlayers[i].BasePP_Player_ViewModel.FieldName"
    /// Returns just "FieldName"
    /// </summary>
    private static string ExtractFieldName(string aspForPath)
    {
        // Remove array indexers: FamilyPlayers[i] -> FamilyPlayers
        var cleaned = Regex.Replace(aspForPath, @"\[\w+\]", "");

        // Split by dots and take last segment
        var parts = cleaned.Split('.');
        var fieldName = parts[^1].Trim();

        // Skip navigation properties
        if (fieldName == "BasePP_Player_ViewModel" || fieldName == "BaseCAC_Player_ViewModel")
            return string.Empty;

        // Skip technical fields
        if (fieldName.StartsWith("__"))
            return string.Empty;

        return fieldName;
    }

    private static string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        return char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];
    }

    private static string SplitPascalCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        // Insert space before uppercase letters (except first)
        var result = Regex.Replace(pascalCase, "([a-z])([A-Z])", "$1 $2");

        // Handle acronyms: "DOB" stays "DOB", not "D O B"
        result = Regex.Replace(result, "([A-Z]+)([A-Z][a-z])", "$1 $2");

        return result;
    }

    /// <summary>
    /// Helper class to track field info from view
    /// </summary>
    private sealed class ViewFieldInfo
    {
        public required string Name { get; set; }
        public required string Visibility { get; set; }
        public required string InputType { get; set; }
    }

    /// <summary>
    /// Helper class to store property metadata extracted from C# source
    /// </summary>
    private sealed class PropertyMetadata
    {
        public string? PropertyType { get; set; }
        public string? DisplayName { get; set; }
        public FieldValidation? Validation { get; set; }
        public string? InputType { get; set; }
        public string? DataSource { get; set; }
    }
}