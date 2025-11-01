# POCO Class Structure Analysis

## Overview
Analysis of TSIC-Unify-2024 Player Profile POCO classes to inform the metadata migration parser design.

## Repository Information
- **Repository**: toddtsic/TSIC-Unify-2024
- **PP Profiles Path**: `TSIC-Unify-Models/ViewModels/RegPlayersSingle_ViewModels/`
- **CAC Profiles Path**: `TSIC-Unify-Models/ViewModels/RegPlayersMulti_ViewModels/`
- **Base Classes Path**: `TSIC-Unify-Models/ViewModels/RegForm_ViewModels/`

## Class Hierarchy

### Player Profile (PP) Classes

```
PP_ViewModel (abstract)
  └─ BaseModel: Reg_JobData_BaseModel
  
PP{XX}_ViewModel : PP_ViewModel
  └─ FamilyPlayers: List<PP{XX}_Player_ViewModel>
  └─ ListModelDistinctFields: List<string> (manual field list - to be eliminated)
  
PP{XX}_Player_ViewModel
  └─ BasePP_Player_ViewModel: BasePP_Player_ViewModel (demographics)
  └─ TeamId: Guid?
  └─ [Profile-Specific Fields with DataAnnotations]
```

### Example: PP10

```csharp
// Container class
public class PP10_ViewModel : PP_ViewModel
{
    public List<PP10_Player_ViewModel> FamilyPlayers { get; set; }
    public List<string> ListModelDistinctFields { get; set; } // Manual - to eliminate
}

// Actual form fields
public class PP10_Player_ViewModel
{
    public BasePP_Player_ViewModel BasePP_Player_ViewModel { get; set; }
    
    [Display(Name = "Select a Team")]
    [Required(ErrorMessage = "you must SELECT A TEAM")]
    public Guid? TeamId { get; set; }
    
    [Display(Name = "School Name")]
    [Required(ErrorMessage = "SCHOOL NAME is required")]
    public string SchoolName { get; set; }
    
    [Display(Name = "HIGH SCHOOL Grad Year")]
    [Required(ErrorMessage = "HIGH SCHOOL GRAD YEAR is required")]
    public string GradYear { get; set; }
    
    [Display(Name = "Position")]
    [Required(ErrorMessage = "POSITION is required")]
    public string Position { get; set; }
    
    [Display(Name = "Club Team Name")]
    public string ClubTeamName { get; set; }
    
    [Display(Name = "Club Coach")]
    public string ClubCoach { get; set; }
    
    [Display(Name = "Club Coach Email")]
    [EmailAddress(ErrorMessage = "CLUB COACH EMAIL is not a valid email address")]
    public string ClubCoachEmail { get; set; }
    
    [Display(Name = "Roommate Preference")]
    public string RoommatePref { get; set; }
    
    [Display(Name = "Waiver 1 Signed")]
    [Required(ErrorMessage = "Waiver 1 must be signed")]
    public bool BWaiverSigned1 { get; set; }
    
    [Display(Name = "Waiver 3 Signed")]
    public bool BWaiverSigned3 { get; set; }
    
    [Display(Name = "Medical Form Uploaded")]
    [DataType(DataType.Upload)]
    public bool BUploadedMedForm { get; set; }
}
```

## Base Demographics Class

**BasePP_Player_ViewModel** (from BaseRegForm_ViewModels.cs):

```csharp
public class BasePP_Player_ViewModel
{
    public bool IsSelected { get; set; }           // UI state - ignore
    public List<Guid?> ListTeamIds { get; set; }   // UI state - ignore
    public int PlayerOffset { get; set; }          // UI state - ignore
    
    public string PlayerUserId { get; set; }       // HIDDEN - internal
    public string FirstName { get; set; }          // TEXT - required
    public string LastName { get; set; }           // TEXT - required
    public DateTime Dob { get; set; }              // DATE - required
    public string Gender { get; set; }             // SELECT - dataSource: genders
    public Guid? RegistrationId { get; set; }      // HIDDEN - internal
    public decimal? AmtPaidToDate { get; set; }    // HIDDEN - admin-only
    public string Agerange { get; set; }           // COMPUTED - derived from DOB
    public string HeadShotPath { get; set; }       // FILE - optional
}
```

## CAC Profile Classes

**BaseCAC_Player_ViewModel** (from RegistrationPlayerBase_ViewModels.cs):

```csharp
public class BaseCAC_Player_ViewModel
{
    public bool IsSelected { get; set; }           // UI state - ignore
    public string FirstName { get; set; }          // TEXT
    public string LastName { get; set; }           // TEXT
    public string PlayerUserId { get; set; }       // HIDDEN
    public List<Guid?> ListTeamIds { get; set; }   // Multi-select teams
    public int PlayerOffset { get; set; }          // UI state - ignore
}
```

Note: CAC profiles simpler - coaches rostering multiple players at once.

## DataAnnotations Inventory

### Display Attributes
```csharp
[Display(Name = "Display Label")]               // Sets displayName
[Display(Name = "Field", Description = "...")]  // Optional description
```

### Validation Attributes
```csharp
[Required(ErrorMessage = "...")]                 // validation.required = true
[EmailAddress(ErrorMessage = "...")]             // inputType = EMAIL
[Compare("PropertyName", ErrorMessage = "...")] // validation.compare
[Range(minimum: 0, maximum: 5.0, ErrorMessage = "...")] // validation.range
[StringLength(12, MinimumLength = 7, ErrorMessage = "...")] // validation.length
[RegularExpression(pattern: "...", ErrorMessage = "...")] // validation.pattern
[Remote(action: "...", controller: "...")] // validation.remote (server-side)
```

### Data Type Attributes
```csharp
[DataType(DataType.Upload)]                      // inputType = FILE
[DataType(DataType.Date)]                        // inputType = DATE
[DataType(DataType.EmailAddress)]                // inputType = EMAIL
[DataType(DataType.Password)]                    // inputType = PASSWORD
```

### Hidden Fields
```csharp
[HiddenInput(DisplayValue = true)]              // inputType = HIDDEN
```

## Field Type Inference Rules

### By Property Type
| C# Type | JSON inputType |
|---------|----------------|
| `bool` | `CHECKBOX` |
| `bool B...` (prefix) | `CHECKBOX` (e.g., `BWaiverSigned1`) |
| `DateTime` | `DATE` |
| `Guid?` | `SELECT` (if property name contains "Team" or "Id") |
| `decimal?` | `NUMBER` |
| `int` | `NUMBER` |
| `string` | `TEXT` (default) |

### By DataAnnotations
| Annotation | JSON inputType |
|------------|----------------|
| `[EmailAddress]` | `EMAIL` |
| `[DataType(DataType.Upload)]` | `FILE` |
| `[DataType(DataType.Date)]` | `DATE` |
| `[DataType(DataType.Password)]` | `PASSWORD` |
| `[HiddenInput]` | `HIDDEN` |

### By Naming Conventions
| Property Pattern | Inference |
|------------------|-----------|
| `*Email` | `EMAIL` if not annotated |
| `TeamId` | `SELECT`, dataSource: "teams" |
| `SchoolGrade` | `SELECT`, dataSource: "grades" |
| `Position` | `SELECT`, dataSource: "positions" |
| `GradYear` | `SELECT`, dataSource: "gradYears" |
| `JerseySize` | `SELECT`, dataSource: "jerseySizes" |
| `ShortsSize` | `SELECT`, dataSource: "shortsSizes" |
| `TShirt*` | `SELECT`, dataSource: "shirtSizes" |
| `SchoolName` | `TEXT` (but could be `AUTOCOMPLETE` later) |
| `Gender` | `SELECT`, dataSource: "genders" |

## Parser Requirements

### Phase 1: Extract All Properties

For a given profile (e.g., PP10):

1. **Parse Base Demographics**
   - Read `BasePP_Player_ViewModel` from `BaseRegForm_ViewModels.cs`
   - Extract all properties except UI state fields (IsSelected, ListTeamIds, PlayerOffset)
   - Apply metadata inference for each field

2. **Parse Profile-Specific Fields**
   - Read `PP10_Player_ViewModel` from `PP10ViewModel.cs`
   - Skip `BasePP_Player_ViewModel` property (already parsed)
   - Extract remaining properties
   - Apply metadata inference

### Phase 2: Build Metadata JSON

For each property:

```json
{
  "name": "property.Name (camelCase)",
  "dbColumn": "property.Name (original case)",
  "displayName": "from [Display(Name)] or PascalCase → Title Case",
  "inputType": "inferred from type + annotations + naming",
  "dataSource": "inferred from property name (if SELECT)",
  "validation": {
    "required": "from [Required]",
    "email": "from [EmailAddress]",
    "minLength": "from [StringLength(MinimumLength)]",
    "maxLength": "from [StringLength] or [StringLength(MaximumLength)]",
    "pattern": "from [RegularExpression]",
    "range": "from [Range]",
    "compare": "from [Compare]"
  },
  "order": "sequential based on source order",
  "adminOnly": "true for AmtPaidToDate, RegistrationId, etc."
}
```

### Phase 3: Roslyn Syntax Tree Traversal

```csharp
// Parse the file
var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
var root = syntaxTree.GetRoot();

// Find the _Player_ViewModel class
var playerClass = root.DescendantNodes()
    .OfType<ClassDeclarationSyntax>()
    .FirstOrDefault(c => c.Identifier.Text.EndsWith("_Player_ViewModel"));

// For each property
foreach (var property in playerClass.Members.OfType<PropertyDeclarationSyntax>())
{
    var propertyName = property.Identifier.Text;
    var propertyType = property.Type.ToString();
    
    // Skip BasePP_Player_ViewModel property
    if (propertyType == "BasePP_Player_ViewModel") continue;
    
    // Extract attributes
    var attributes = property.AttributeLists
        .SelectMany(al => al.Attributes)
        .ToList();
    
    var displayAttr = attributes.FirstOrDefault(a => a.Name.ToString() == "Display");
    var requiredAttr = attributes.FirstOrDefault(a => a.Name.ToString() == "Required");
    var emailAttr = attributes.FirstOrDefault(a => a.Name.ToString() == "EmailAddress");
    // ... etc
    
    // Build metadata field
    var field = new ProfileMetadataField
    {
        Name = ToCamelCase(propertyName),
        DbColumn = propertyName,
        DisplayName = GetDisplayName(displayAttr, propertyName),
        InputType = InferInputType(propertyType, attributes, propertyName),
        DataSource = InferDataSource(propertyName, propertyType),
        Validation = BuildValidation(attributes),
        Order = order++,
        AdminOnly = IsAdminOnly(propertyName)
    };
}
```

## Migration Service Flow

```
1. ProfileMetadataMigrationService.MigrateJobsAsync()
   ↓
2. Get all active Jobs from database
   ↓
3. For each Job:
   a. Identify ProfileType (PP10, PP17, CAC05, etc.)
   b. GitHubProfileFetcher.FetchProfileAsync(profileType)
      - Fetch PP{XX}ViewModel.cs from GitHub
      - Fetch BaseRegForm_ViewModels.cs (for BasePP_Player_ViewModel)
   c. CSharpToMetadataParser.ParseProfileAsync(sourceCode)
      - Parse base demographics
      - Parse profile-specific fields
      - Merge into single metadata array
      - Apply inference rules
   d. Update Job.PlayerProfileMetadataJson
   e. Save to database
   ↓
4. Return MigrationReport
   - SuccessCount
   - FailureCount
   - Warnings (e.g., "Could not infer dataSource for field X")
   - Details per job
```

## Example Output

**PP10 PlayerProfileMetadataJson** (partial):

```json
{
  "fields": [
    {
      "name": "firstName",
      "dbColumn": "FirstName",
      "displayName": "First Name",
      "inputType": "TEXT",
      "validation": { "required": true },
      "order": 1
    },
    {
      "name": "lastName",
      "dbColumn": "LastName",
      "displayName": "Last Name",
      "inputType": "TEXT",
      "validation": { "required": true },
      "order": 2
    },
    {
      "name": "dob",
      "dbColumn": "Dob",
      "displayName": "Date of Birth",
      "inputType": "DATE",
      "validation": { "required": true },
      "order": 3
    },
    {
      "name": "gender",
      "dbColumn": "Gender",
      "displayName": "Gender",
      "inputType": "SELECT",
      "dataSource": "genders",
      "validation": { "required": true },
      "order": 4
    },
    {
      "name": "teamId",
      "dbColumn": "TeamId",
      "displayName": "Select a Team",
      "inputType": "SELECT",
      "dataSource": "teams",
      "validation": { "required": true },
      "order": 5
    },
    {
      "name": "schoolName",
      "dbColumn": "SchoolName",
      "displayName": "School Name",
      "inputType": "TEXT",
      "validation": { "required": true },
      "order": 6
    },
    {
      "name": "gradYear",
      "dbColumn": "GradYear",
      "displayName": "HIGH SCHOOL Grad Year",
      "inputType": "SELECT",
      "dataSource": "gradYears",
      "validation": { "required": true },
      "order": 7
    },
    {
      "name": "position",
      "dbColumn": "Position",
      "displayName": "Position",
      "inputType": "SELECT",
      "dataSource": "positions",
      "validation": { "required": true },
      "order": 8
    },
    {
      "name": "clubCoachEmail",
      "dbColumn": "ClubCoachEmail",
      "displayName": "Club Coach Email",
      "inputType": "EMAIL",
      "validation": { 
        "email": true,
        "message": "CLUB COACH EMAIL is not a valid email address"
      },
      "order": 11
    },
    {
      "name": "bWaiverSigned1",
      "dbColumn": "BWaiverSigned1",
      "displayName": "Waiver 1 Signed",
      "inputType": "CHECKBOX",
      "validation": { "required": true },
      "order": 13
    },
    {
      "name": "bUploadedMedForm",
      "dbColumn": "BUploadedMedForm",
      "displayName": "Medical Form Uploaded",
      "inputType": "FILE",
      "order": 15
    }
  ]
}
```

## Next Steps

1. **Implement GitHubProfileFetcher**
   - Use HttpClient to fetch from GitHub Contents API
   - Handle base64 decoding
   - Cache BaseRegForm_ViewModels.cs (same for all PP profiles)

2. **Implement CSharpToMetadataParser**
   - Add Microsoft.CodeAnalysis.CSharp NuGet package
   - Implement Roslyn traversal
   - Build inference engine for inputType, dataSource, validation

3. **Implement ProfileMetadataMigrationService**
   - Orchestrate fetch + parse + update
   - Transaction handling (rollback on error)
   - Generate detailed migration report

4. **Create ProfileMigrationController**
   - `GET /api/admin/profile-migration/preview/{jobId}` - Preview single job
   - `POST /api/admin/profile-migration/migrate-all` - Run full migration
   - `GET /api/admin/profile-migration/report` - Get last migration report

5. **Build Angular Migration UI**
   - Component with "Preview" and "Migrate All" buttons
   - Progress indicator during migration
   - Report display with success/failure/warning counts
   - Detailed log view

## Open Questions

1. **DataSource Resolution**: How to map field names to actual API endpoints?
   - `"teams"` → `/api/teams/{jobId}`
   - `"positions"` → `/api/lookups/positions?sport={jobSport}`
   - `"gradYears"` → client-side computed (current year + 10 years)

2. **Complex Validations**: Remote validation with `[Remote]` attribute?
   - Parse action/controller names
   - Map to API endpoint
   - Include in validation.remote

3. **Conditional Fields**: Some profiles have conditional logic (e.g., show college field only if BCollegeCommit = true)
   - Add `conditionalOn` property to metadata?
   - `{ "conditionalOn": { "field": "bCollegeCommit", "value": true } }`

4. **Field Order**: Preserve logical grouping?
   - Demographics first
   - Team/Registration second
   - Apparel third
   - Waivers last
   - Or strict source order?

5. **Version Control**: What if POCO changes after migration?
   - Include `migratedFrom` metadata: `{ "source": "PP10ViewModel.cs", "version": "sha256hash", "migratedAt": "2025-11-01T..." }`
   - Allow re-migration with comparison/merge?

## Warnings/Edge Cases

- **ListModelDistinctFields**: Manual list - ignore during parsing, will be eliminated
- **UI State Fields**: IsSelected, ListTeamIds, PlayerOffset - skip these
- **Computed Fields**: Agerange derived from DOB - mark as computed, not editable
- **Internal IDs**: RegistrationId, PlayerUserId - HIDDEN, adminOnly
- **Multiple Profiles**: Same job could have different profiles for different age groups
  - Need array? `playerProfileMetadata: ProfileMetadata[]`?
  - Or single profile per job? (Current assumption)

---

**Document Status**: Complete  
**Last Updated**: November 1, 2025  
**Next Action**: Begin implementation of GitHubProfileFetcher service
