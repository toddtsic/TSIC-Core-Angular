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

        // Step 2: Build metadata from view fields
        int order = 1;
        foreach (var fieldInfo in viewFields)
        {
            var field = new ProfileMetadataField
            {
                Name = ToCamelCase(fieldInfo.Name),
                DbColumn = fieldInfo.Name,
                DisplayName = SplitPascalCase(fieldInfo.Name),
                Order = order++,
                Visibility = fieldInfo.Visibility,
                InputType = fieldInfo.InputType,
                Computed = false
            };

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
    /// Extract field name from asp-for path like "FamilyPlayers[i].BasePP_Player_ViewModel.FieldName"
    /// Returns just "FieldName"
    /// </summary>
    private string ExtractFieldName(string aspForPath)
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
    private class ViewFieldInfo
    {
        public required string Name { get; set; }
        public required string Visibility { get; set; }
        public required string InputType { get; set; }
    }
}