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

## Migration Strategy (Profile-Centric Approach)

### Concept

Instead of migrating job-by-job (which would fetch the same POCO from GitHub hundreds of times), we migrate **profile-by-profile** and apply the generated metadata to all jobs using that profile.

### Flow

```
1. ProfileMetadataMigrationService.MigrateProfileAsync("PP10")
   ↓
2. Fetch PP10ViewModel.cs from GitHub ONCE
   ↓
3. Fetch BaseRegForm_ViewModels.cs from GitHub ONCE (cached)
   ↓
4. Parse into metadata ONCE
   ↓
5. Find ALL jobs where CoreRegformPlayer starts with "PP10"
   ↓
6. Apply same metadata JSON to ALL matching jobs
   ↓
7. Save all updates in one transaction
   ↓
8. Return ProfileMigrationResult
   - ProfileType: "PP10"
   - JobsAffected: 45
   - FieldCount: 24
   - AffectedJobIds: [guid1, guid2, ...]
```

### Benefits

✅ **Efficiency**: GitHub fetch happens once per profile, not per job  
✅ **Speed**: Migrate 100 jobs in seconds instead of minutes  
✅ **Consistency**: All jobs using PP10 get identical metadata  
✅ **Simplicity**: User sees "6 profiles" not "156 jobs"  
✅ **Re-migration**: Easy to refresh a profile if GitHub POCO is updated  
✅ **Check for Updates**: Can compare current metadata SHA with GitHub to detect changes

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

✅ **COMPLETED** - Profile-centric migration system:

### Backend Implementation ✅

1. **New DTOs** (`ProfileMigrationDtos.cs`)
   - `ProfileSummary` - Shows profile types and their job counts
   - `ProfileMigrationResult` - Result of migrating one profile across all jobs
   - `ProfileBatchMigrationReport` - Report for batch profile migrations
   - `MigrateProfilesRequest` - Request for batch operations

2. **ProfileMetadataMigrationService** - New Methods
   - `GetProfileSummariesAsync()` - List all unique profiles with job counts
   - `PreviewProfileMigrationAsync(profileType)` - Dry run for single profile
   - `MigrateProfileAsync(profileType, dryRun)` - Migrate one profile → all jobs
   - `MigrateMultipleProfilesAsync(dryRun, filter)` - Batch migrate profiles
   - Legacy methods kept for backward compatibility

3. **ProfileMigrationController** - New Endpoints
   - `GET /api/admin/profile-migration/profiles` - Get profile summaries
   - `GET /api/admin/profile-migration/preview-profile/{type}` - Preview single profile
   - `POST /api/admin/profile-migration/migrate-profile/{type}` - Migrate single profile
   - `POST /api/admin/profile-migration/migrate-all-profiles` - Batch migrate
   - Legacy endpoints kept for backward compatibility

### API Usage Examples

**Get Profile Summary:**
```http
GET /api/admin/profile-migration/profiles
```
Returns:
```json
[
  {
    "profileType": "PP10",
    "jobCount": 45,
    "migratedJobCount": 45,
    "allJobsMigrated": true,
    "sampleJobNames": ["Summer Camp 2025", "Fall League", ...]
  },
  {
    "profileType": "PP17",
    "jobCount": 32,
    "migratedJobCount": 0,
    "allJobsMigrated": false,
    "sampleJobNames": ["Winter Clinic", ...]
  }
]
```

**Preview Profile Migration (Dry Run):**
```http
GET /api/admin/profile-migration/preview-profile/PP10
```
Returns metadata + list of affected jobs without committing.

**Migrate Single Profile:**
```http
POST /api/admin/profile-migration/migrate-profile/PP10
```
Fetches PP10 from GitHub, parses it, applies to all 45 jobs using PP10.

**Batch Migrate All Pending Profiles:**
```http
POST /api/admin/profile-migration/migrate-all-profiles
{
  "dryRun": false,
  "profileTypes": null  // null = all profiles
}
```

### Configuration

GitHub token in `appsettings.json` (optional but recommended):
```json
{
  "GitHub": {
    "RepoOwner": "toddtsic",
    "RepoName": "TSIC-Unify-2024",
    "Token": "github_pat_YOUR_TOKEN_HERE"
  }
}
```

### Design Decisions ✅

1. **No Migration Tracking**: Don't persist migration status in database - infer from `PlayerProfileMetadataJson` presence
2. **Re-migration Allowed**: Can re-run migration anytime to refresh metadata
3. **Check for Updates**: Compare `metadata.source.commitSha` with current GitHub SHA to detect POCO changes
4. **No Job Overrides**: Jobs never override profile metadata - always profile → jobs (one-way)

### Next Steps (Pending)

5. **Build Angular Migration UI**
   - Profile-based dashboard (not job-based)
   - Shows: Profile Type | Jobs Using | Status | Actions
   - Preview modal shows affected jobs + generated metadata
   - Batch migration with progress indicator
   - "Check for Updates" button to compare SHA with GitHub

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
