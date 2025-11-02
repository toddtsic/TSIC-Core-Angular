using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using TSIC.API.Dtos;

namespace TSIC.API.Services;

/// <summary>
/// Parses C# POCO classes using Roslyn and generates ProfileMetadata JSON
/// </summary>
public class CSharpToMetadataParser
{
    private readonly ILogger<CSharpToMetadataParser> _logger;

    // Fields to skip (UI state, internal)
    private static readonly HashSet<string> SkipFields = new()
    {
        "IsSelected", "ListTeamIds", "PlayerOffset", "IsTrue",
        "BasePP_Player_ViewModel", "BaseCAC_Player_ViewModel",
        "__RequestVerificationToken"
    };

    // Hidden fields (technical, never displayed)
    private static readonly HashSet<string> HiddenFields = new()
    {
        "RegistrationId"
    };

    // Admin-only fields (visible only to admins)
    private static readonly HashSet<string> AdminOnlyFields = new()
    {
        "PlayerUserId", "AmtPaidToDate"
    };

    // Computed fields
    private static readonly HashSet<string> ComputedFields = new()
    {
        "Agerange"
    };

    // Hidden fields detected from .cshtml view file (set dynamically per profile)
    private HashSet<string> _hiddenFieldsFromView = new();

    public CSharpToMetadataParser(ILogger<CSharpToMetadataParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parse hidden field names from .cshtml view file
    /// </summary>
    /// <param name="viewContent">Raw .cshtml file content</param>
    /// <returns>Set of field names marked as hidden in the view</returns>
    public HashSet<string> ParseHiddenFieldsFromView(string? viewContent)
    {
        var hiddenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(viewContent))
        {
            return hiddenFields;
        }

        // Regex to find: <input type="hidden" asp-for="FieldName" />
        // Handles variations like asp-for='...' and type='hidden'
        var regex = new Regex(
            @"<input[^>]*type\s*=\s*[""']hidden[""'][^>]*asp-for\s*=\s*[""']([^""']+)[""']|" +
            @"<input[^>]*asp-for\s*=\s*[""']([^""']+)[""'][^>]*type\s*=\s*[""']hidden[""']",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        foreach (Match match in regex.Matches(viewContent))
        {
            // Extract asp-for value (from either capture group)
            var aspForValue = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

            if (string.IsNullOrWhiteSpace(aspForValue))
                continue;

            // Extract the base field name, handling nested paths
            // Examples:
            //   "FamilyPlayers[i].BasePP_Player_ViewModel.RegistrationId" -> "RegistrationId"
            //   "RegistrationId" -> "RegistrationId"
            //   "FamilyPlayers[i].IsTrue" -> "IsTrue"
            var fieldName = ExtractBaseFieldName(aspForValue);

            if (!string.IsNullOrWhiteSpace(fieldName))
            {
                hiddenFields.Add(fieldName);
                _logger.LogDebug("Found hidden field in view: {FieldName} (from asp-for=\"{AspFor}\")",
                    fieldName, aspForValue);
            }
        }

        _logger.LogInformation("Parsed {Count} hidden fields from view file", hiddenFields.Count);

        return hiddenFields;
    }

    /// <summary>
    /// Extract base field name from asp-for path
    /// </summary>
    private static string ExtractBaseFieldName(string aspForPath)
    {
        // Remove array indices: "FamilyPlayers[i].FieldName" -> "FamilyPlayers.FieldName"
        var withoutIndices = Regex.Replace(aspForPath, @"\[\w+\]", "");

        // Get the last segment after splitting by '.'
        var segments = withoutIndices.Split('.');
        return segments[^1]; // Return last segment
    }

    /// <summary>
    /// Parse profile and base class source code into metadata
    /// </summary>
    public async Task<ProfileMetadata> ParseProfileAsync(
        string profileSourceCode,
        string baseClassSourceCode,
        string profileType,
        string commitSha,
        string? viewContent = null)
    {
        // Parse hidden fields from view file first (if provided)
        _hiddenFieldsFromView = ParseHiddenFieldsFromView(viewContent);

        var metadata = new ProfileMetadata
        {
            Fields = new List<ProfileMetadataField>(),
            Source = new ProfileMetadataSource
            {
                SourceFile = $"{profileType}ViewModel.cs",
                Repository = "toddtsic/TSIC-Unify-2024",
                CommitSha = commitSha,
                MigratedAt = DateTime.UtcNow,
                MigratedBy = "ProfileMetadataMigrationService"
            }
        };

        var warnings = new List<string>();

        // First, parse base demographics fields
        var baseFields = await ParseBaseClassFieldsAsync(baseClassSourceCode, warnings);

        // Then, parse profile-specific fields
        var profileFields = await ParseProfileClassFieldsAsync(profileSourceCode, profileType, warnings);

        // Deduplicate: profile fields override base fields with same name
        // Use a dictionary to track fields by name, profile fields overwrite base fields
        var fieldsByName = new Dictionary<string, ProfileMetadataField>(StringComparer.OrdinalIgnoreCase);

        // Add base fields first
        foreach (var field in baseFields)
        {
            fieldsByName[field.Name] = field;
        }

        // Profile fields override base fields
        foreach (var field in profileFields)
        {
            if (fieldsByName.ContainsKey(field.Name))
            {
                _logger.LogDebug("Property '{PropertyName}' redeclared in {ProfileType}, using derived version",
                    field.Name, profileType);
            }
            fieldsByName[field.Name] = field;
        }

        // Convert back to list
        metadata.Fields.AddRange(fieldsByName.Values);

        // Set order after combining all fields
        for (int i = 0; i < metadata.Fields.Count; i++)
        {
            metadata.Fields[i].Order = i + 1;
        }

        if (warnings.Count > 0)
        {
            _logger.LogWarning("Parsing {ProfileType} generated {Count} warnings: {Warnings}",
                profileType, warnings.Count, string.Join("; ", warnings));
        }

        return metadata;
    }

    private async Task<List<ProfileMetadataField>> ParseBaseClassFieldsAsync(
        string sourceCode, List<string> warnings)
    {
        var fields = new List<ProfileMetadataField>();

        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = await tree.GetRootAsync();

        // Find BasePP_Player_ViewModel class
        var baseClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == "BasePP_Player_ViewModel");

        if (baseClass == null)
        {
            warnings.Add("Could not find BasePP_Player_ViewModel class");
            return fields;
        }

        var order = 1; // Local order for this method
        foreach (var property in baseClass.Members.OfType<PropertyDeclarationSyntax>())
        {
            var propertyName = property.Identifier.Text;

            if (SkipFields.Contains(propertyName))
                continue;

            var field = ParseProperty(property, ref order, warnings);
            fields.Add(field);
        }

        return fields;
    }

    private async Task<List<ProfileMetadataField>> ParseProfileClassFieldsAsync(
        string sourceCode, string profileType, List<string> warnings)
    {
        var fields = new List<ProfileMetadataField>();

        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = await tree.GetRootAsync();

        // Find PP{XX}_Player_ViewModel or CAC{XX}_Player_ViewModel class
        var playerClass = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text.EndsWith("_Player_ViewModel") &&
                                  c.Identifier.Text.StartsWith(profileType));

        if (playerClass == null)
        {
            warnings.Add($"Could not find {profileType}_Player_ViewModel class");
            return fields;
        }

        var order = 1; // Local order for this method
        foreach (var property in playerClass.Members.OfType<PropertyDeclarationSyntax>())
        {
            var propertyName = property.Identifier.Text;

            if (SkipFields.Contains(propertyName))
                continue;

            var field = ParseProperty(property, ref order, warnings);
            fields.Add(field);
        }

        return fields;
    }

    private ProfileMetadataField ParseProperty(
        PropertyDeclarationSyntax property, ref int order, List<string> warnings)
    {
        var propertyName = property.Identifier.Text;
        var propertyType = property.Type.ToString();

        var field = new ProfileMetadataField
        {
            Name = ToCamelCase(propertyName),
            DbColumn = propertyName,
            DisplayName = propertyName, // Will be overridden by [Display] if present
            Order = order++,
            Computed = ComputedFields.Contains(propertyName),
            // Set visibility based on field classification
            Visibility = DetermineVisibility(propertyName),
#pragma warning disable CS0618 // Type or member is obsolete
            AdminOnly = AdminOnlyFields.Contains(propertyName) // Keep for backward compatibility
#pragma warning restore CS0618
        };

        // Extract attributes
        var attributes = property.AttributeLists
            .SelectMany(al => al.Attributes)
            .ToList();

        // Apply Display attribute
        var displayAttr = attributes.Find(a => a.Name.ToString() == "Display");
        if (displayAttr != null)
        {
            field.DisplayName = ExtractDisplayName(displayAttr) ?? PascalCaseToTitleCase(propertyName);
        }
        else
        {
            field.DisplayName = PascalCaseToTitleCase(propertyName);
        }

        // Infer input type
        field.InputType = InferInputType(propertyType, propertyName, attributes);

        // Infer data source for SELECT inputs
        if (field.InputType == "SELECT")
        {
            field.DataSource = InferDataSource(propertyName, warnings);
        }

        // Build validation
        field.Validation = BuildValidation(attributes, propertyType);

        // Add special validation rules based on field name
        ApplySpecialValidationRules(propertyName, field);

        // Check for HiddenInput attribute
        var hiddenAttr = attributes.Find(a => a.Name.ToString() == "HiddenInput");
        if (hiddenAttr != null)
        {
            field.InputType = "HIDDEN";
        }

        return field;
    }

    private static string InferInputType(string csharpType, string propertyName,
        List<AttributeSyntax> attributes)
    {
        // Check DataType attribute first
        var dataTypeAttr = attributes.Find(a => a.Name.ToString() == "DataType");
        if (dataTypeAttr != null)
        {
            var dataTypeValue = ExtractDataTypeValue(dataTypeAttr);
            switch (dataTypeValue)
            {
                case "Upload": return "FILE";
                case "Date": return "DATE";
                case "EmailAddress": return "EMAIL";
                case "Password": return "PASSWORD";
            }
        }

        // Check EmailAddress attribute
        if (attributes.Exists(a => a.Name.ToString() == "EmailAddress"))
            return "EMAIL";

        // Infer from C# type
        switch (csharpType)
        {
            case "bool":
                return "CHECKBOX";
            case "DateTime":
            case "DateTime?":
                return "DATE";
            case "decimal":
            case "decimal?":
            case "int":
            case "int?":
            case "double":
            case "double?":
                return "NUMBER";
            case "Guid?":
                // Guid typically used for foreign keys (teams, etc.)
                return "SELECT";
        }

        // Infer from property name
        if (propertyName.StartsWith('B') && char.IsUpper(propertyName[1]))
        {
            // Boolean prefix convention (e.g., BWaiverSigned1, BUploadedMedForm)
            return "CHECKBOX";
        }

        if (propertyName.StartsWith("Is") || propertyName.StartsWith("Has"))
        {
            // Boolean prefix patterns (e.g., IsActive, HasPermission)
            return "CHECKBOX";
        }

        if (propertyName.EndsWith("Email"))
            return "EMAIL";

        if (propertyName.EndsWith("Phone") || propertyName.Contains("Phone"))
            return "TEL";

        if (propertyName.Equals("Height", StringComparison.OrdinalIgnoreCase))
            return "NUMBER";

        if (propertyName.Equals("Weight", StringComparison.OrdinalIgnoreCase))
            return "NUMBER";

        if (propertyName.EndsWith("Id") || propertyName == "TeamId" ||
            propertyName == "AgegroupId" || propertyName == "Gender" ||
            propertyName == "Position" || propertyName.EndsWith("Size") ||
            propertyName == "GradYear" || propertyName == "SchoolGrade" ||
            propertyName == "SkillLevel" || propertyName == "State")
        {
            return "SELECT";
        }

        // Default to TEXT for strings
        return "TEXT";
    }

    private static string? InferDataSource(string propertyName, List<string> warnings)
    {
        var dataSource = propertyName switch
        {
            "TeamId" => "teams",
            "AgegroupId" => "agegroups",
            "Gender" => "genders",
            "Position" => "positions",
            "GradYear" => "gradYears",
            "SchoolGrade" => "schoolGrades",
            "SkillLevel" => "skillLevels",
            "State" => "states",
            "JerseySize" => "jerseySizes",
            "ShortsSize" => "shortsSizes",
            "TShirt" => "shirtSizes",
            "TShirtSize" => "shirtSizes",
            "Reversible" => "reversibleSizes",
            "Kilt" => "kiltSizes",
            "Sweatshirt" => "sweatshirtSizes",
            "StrongHand" => "handedness",
            _ => null
        };

        if (dataSource == null && propertyName.EndsWith("Size"))
        {
            dataSource = "sizes"; // Generic fallback
            warnings.Add($"Could not infer specific dataSource for {propertyName}, using generic 'sizes'");
        }

        return dataSource;
    }

    private static void ApplySpecialValidationRules(string propertyName, ProfileMetadataField field)
    {
        // Ensure validation object exists
        field.Validation ??= new FieldValidation();

        // Height: NUMBER input with min=36 (3 feet in inches), max=84 (7 feet in inches)
        if (propertyName.Equals("Height", StringComparison.OrdinalIgnoreCase))
        {
            field.Validation.Min = 36;  // 3 feet
            field.Validation.Max = 84;  // 7 feet
            if (string.IsNullOrEmpty(field.Validation.Message))
            {
                field.Validation.Message = "Height must be between 36 inches (3 ft) and 84 inches (7 ft)";
            }
        }

        // Weight: NUMBER input with min=30, max=250 (pounds)
        if (propertyName.Equals("Weight", StringComparison.OrdinalIgnoreCase))
        {
            field.Validation.Min = 30;   // minimum weight
            field.Validation.Max = 250;  // maximum weight
            if (string.IsNullOrEmpty(field.Validation.Message))
            {
                field.Validation.Message = "Weight must be between 30 and 250 pounds";
            }
        }

        // Phone: TEL input with digits-only pattern
        if (propertyName.EndsWith("Phone") || propertyName.Contains("Phone"))
        {
            field.Validation.Pattern = @"^\d{10}$";  // 10 digits only
            if (string.IsNullOrEmpty(field.Validation.Message))
            {
                field.Validation.Message = "Phone number must be 10 digits (numbers only)";
            }
        }

        // SportAssnID: Remote validation with USA Lacrosse API
        if (propertyName.Equals("SportAssnID", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Equals("USALacrosseID", StringComparison.OrdinalIgnoreCase))
        {
            // Placeholder for remote validation endpoint
            field.Validation.Remote = "/api/Validation/ValidateUSALacrosseID";
            if (string.IsNullOrEmpty(field.Validation.Message))
            {
                field.Validation.Message = "USA Lacrosse membership number will be validated";
            }
        }
    }

    private static FieldValidation? BuildValidation(List<AttributeSyntax> attributes, string propertyType)
    {
        var validation = new FieldValidation();
        var hasValidation = false;

        foreach (var attr in attributes)
        {
            var attrName = attr.Name.ToString();

            switch (attrName)
            {
                case "Required":
                    validation.Required = true;
                    validation.Message = ExtractErrorMessage(attr);
                    hasValidation = true;
                    break;

                case "EmailAddress":
                    validation.Email = true;
                    validation.Message = ExtractErrorMessage(attr);
                    hasValidation = true;
                    break;

                case "StringLength":
                    var (maxLen, minLen) = ExtractStringLengthValues(attr);
                    validation.MaxLength = maxLen;
                    validation.MinLength = minLen;
                    validation.Message = ExtractErrorMessage(attr);
                    hasValidation = true;
                    break;

                case "Range":
                    var (min, max) = ExtractRangeValues(attr);

                    // Check if this is a checkbox RequiredTrue pattern: [Range(typeof(bool), "true", "true")]
                    if (propertyType == "bool" && min.HasValue && max.HasValue &&
                        Math.Abs(min.Value - 1) < 0.001 && Math.Abs(max.Value - 1) < 0.001)
                    {
                        validation.RequiredTrue = true;
                    }
                    else
                    {
                        validation.Min = min;
                        validation.Max = max;
                    }

                    validation.Message = ExtractErrorMessage(attr);
                    hasValidation = true;
                    break;

                case "RegularExpression":
                    validation.Pattern = ExtractPattern(attr);
                    validation.Message = ExtractErrorMessage(attr);
                    hasValidation = true;
                    break;

                case "Compare":
                    validation.Compare = ExtractCompareProperty(attr);
                    validation.Message = ExtractErrorMessage(attr);
                    hasValidation = true;
                    break;

                case "Remote":
                    validation.Remote = ExtractRemoteEndpoint(attr);
                    hasValidation = true;
                    break;
            }
        }

        return hasValidation ? validation : null;
    }

    private static string? ExtractDisplayName(AttributeSyntax attr)
    {
        var nameArg = attr.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.ToString() == "Name");

        if (nameArg != null)
        {
            return ExtractStringLiteral(nameArg.Expression.ToString());
        }

        return null;
    }

    private static string? ExtractDataTypeValue(AttributeSyntax attr)
    {
        var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
        if (arg != null)
        {
            var value = arg.Expression.ToString();
            // Extract "Upload" from "DataType.Upload"
            return value.Split('.').LastOrDefault();
        }
        return null;
    }

    private static string? ExtractErrorMessage(AttributeSyntax attr)
    {
        var msgArg = attr.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.ToString() == "ErrorMessage");

        if (msgArg != null)
        {
            return ExtractStringLiteral(msgArg.Expression.ToString());
        }

        return null;
    }

    private static (int? maxLength, int? minLength) ExtractStringLengthValues(AttributeSyntax attr)
    {
        int? maxLength = null;
        int? minLength = null;

        var args = attr.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>();

        // First positional argument is max length
        var firstArg = args.FirstOrDefault(a => a.NameEquals == null);
        if (firstArg != null && int.TryParse(firstArg.Expression.ToString(), out var max))
        {
            maxLength = max;
        }

        // MinimumLength named argument
        var minArg = args.FirstOrDefault(a => a.NameEquals?.Name.ToString() == "MinimumLength");
        if (minArg != null && int.TryParse(minArg.Expression.ToString(), out var min))
        {
            minLength = min;
        }

        return (maxLength, minLength);
    }

    private static (double? min, double? max) ExtractRangeValues(AttributeSyntax attr)
    {
        double? min = null;
        double? max = null;

        var args = attr.ArgumentList?.Arguments ?? Enumerable.Empty<AttributeArgumentSyntax>();

        var minArg = args.FirstOrDefault(a => a.NameEquals?.Name.ToString() == "minimum");
        if (minArg != null && double.TryParse(minArg.Expression.ToString(), out var minVal))
        {
            min = minVal;
        }

        var maxArg = args.FirstOrDefault(a => a.NameEquals?.Name.ToString() == "maximum");
        if (maxArg != null && double.TryParse(maxArg.Expression.ToString(), out var maxVal))
        {
            max = maxVal;
        }

        return (min, max);
    }

    private static string? ExtractPattern(AttributeSyntax attr)
    {
        var patternArg = attr.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.ToString() == "pattern");

        if (patternArg != null)
        {
            return ExtractStringLiteral(patternArg.Expression.ToString());
        }

        return null;
    }

    private static string? ExtractCompareProperty(AttributeSyntax attr)
    {
        var firstArg = attr.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals == null);

        if (firstArg != null)
        {
            return ExtractStringLiteral(firstArg.Expression.ToString());
        }

        return null;
    }

    private static string? ExtractRemoteEndpoint(AttributeSyntax attr)
    {
        var actionArg = attr.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.ToString() == "action");
        var controllerArg = attr.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.ToString() == "controller");

        if (actionArg != null && controllerArg != null)
        {
            var action = ExtractStringLiteral(actionArg.Expression.ToString());
            var controller = ExtractStringLiteral(controllerArg.Expression.ToString());
            return $"/api/{controller}/{action}";
        }

        return null;
    }

    private static string? ExtractStringLiteral(string value)
    {
        // Remove quotes and @ symbol
        return value?.Trim('"', '@');
    }

    /// <summary>
    /// Determine field visibility based on field name classification
    /// </summary>
    private string DetermineVisibility(string propertyName)
    {
        // Check view-based hidden fields first (source of truth from .cshtml)
        if (_hiddenFieldsFromView.Contains(propertyName))
            return "hidden";

        // Fall back to hardcoded hidden fields
        if (HiddenFields.Contains(propertyName))
            return "hidden";

        if (AdminOnlyFields.Contains(propertyName))
            return "adminOnly";

        return "public";
    }

    private static string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        return char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];
    }

    private static string PascalCaseToTitleCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        // Insert space before uppercase letters (except first)
        var result = Regex.Replace(pascalCase, "([a-z])([A-Z])", "$1 $2");

        // Handle acronyms (e.g., "DOB" -> "DOB", not "D O B")
        result = Regex.Replace(result, "([A-Z]+)([A-Z][a-z])", "$1 $2");

        return result;
    }
}
