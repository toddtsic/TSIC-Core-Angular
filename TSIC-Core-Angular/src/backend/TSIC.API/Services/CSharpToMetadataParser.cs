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
    
    // Admin-only fields
    private static readonly HashSet<string> AdminOnlyFields = new()
    {
        "RegistrationId", "PlayerUserId", "AmtPaidToDate"
    };
    
    // Computed fields
    private static readonly HashSet<string> ComputedFields = new()
    {
        "Agerange"
    };
    
    public CSharpToMetadataParser(ILogger<CSharpToMetadataParser> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Parse profile and base class source code into metadata
    /// </summary>
    public async Task<ProfileMetadata> ParseProfileAsync(
        string profileSourceCode,
        string baseClassSourceCode,
        string profileType,
        string commitSha)
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
                MigratedBy = "ProfileMetadataMigrationService"
            }
        };
        
        var warnings = new List<string>();
        
        // First, parse base demographics fields
        var baseFields = await ParseBaseClassFieldsAsync(baseClassSourceCode, warnings);
        metadata.Fields.AddRange(baseFields);
        
        // Then, parse profile-specific fields
        var profileFields = await ParseProfileClassFieldsAsync(profileSourceCode, profileType, warnings);
        metadata.Fields.AddRange(profileFields);
        
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
            AdminOnly = AdminOnlyFields.Contains(propertyName),
            Computed = ComputedFields.Contains(propertyName)
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
        field.Validation = BuildValidation(attributes);
        
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
            // Boolean prefix convention (e.g., BWaiverSigned1)
            return "CHECKBOX";
        }
        
        if (propertyName.EndsWith("Email"))
            return "EMAIL";
        
        if (propertyName.EndsWith("Id") || propertyName == "TeamId" || 
            propertyName == "AgegroupId" || propertyName == "Gender" ||
            propertyName == "Position" || propertyName.EndsWith("Size") ||
            propertyName == "GradYear" || propertyName == "SchoolGrade" ||
            propertyName == "SkillLevel")
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
    
    private static FieldValidation? BuildValidation(List<AttributeSyntax> attributes)
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
                    validation.Min = min;
                    validation.Max = max;
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
