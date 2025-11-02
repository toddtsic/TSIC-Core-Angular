# Profile Metadata Migration System - Angular Implementation

## Overview
This document describes the Angular front-end implementation for the Profile Metadata Migration system, consisting of two admin-only components accessible at `/tsic/admin`.

## Components Created

### 1. Profile Migration Dashboard (`/tsic/admin/profile-migration`)

**Purpose:** Allows SuperUsers to migrate player profile metadata from GitHub POCO classes to the database.

**Location:** `src/app/admin/profile-migration/`

**Files:**
- `profile-migration.component.ts` - Component logic with Angular signals
- `profile-migration.component.html` - Bootstrap 5 UI template
- `profile-migration.component.scss` - Component styling

**Features:**
- **Profile List:** Displays all available profile types with migration status
  - Shows job count per profile
  - Shows migrated vs pending count
  - Color-coded badges (success/warning/info)
- **Batch Migration:** "Migrate All Pending" button to migrate multiple profiles
- **Individual Actions:**
  - Preview: Shows generated metadata without saving
  - Migrate: Performs actual migration and updates database
- **Summary Statistics:**
  - Total jobs
  - Migrated jobs
  - Pending/Completed profile counts
- **Migration Report:** Displays detailed results after batch migration
- **Error/Success Messages:** Alert-based feedback system

**State Management:**
Uses Angular 18+ signals for reactive state:
- `isLoading`, `isMigrating` - Loading states
- `errorMessage`, `successMessage` - User feedback
- `profiles` - List of profile summaries
- `migrationReport` - Batch migration results
- `selectedProfile`, `previewResult` - Preview modal state

**Computed Values:**
- `totalJobs` - Sum of all job counts
- `migratedJobs` - Sum of migrated jobs
- `pendingProfiles` - Profiles not fully migrated
- `migratedProfiles` - Fully migrated profiles

### 2. Profile Editor (`/tsic/admin/profile-editor`)

**Purpose:** Allows SuperUsers to view and edit profile metadata, test validation rules, and manage fields.

**Location:** `src/app/admin/profile-editor/`

**Files:**
- `profile-editor.component.ts` - Component logic with Angular signals
- `profile-editor.component.html` - Bootstrap 5 UI template
- `profile-editor.component.scss` - Component styling

**Features:**
- **Profile Selector:** Dropdown to choose profile type (Player, Parent, Coach)
  - **"CREATE NEW" Option:** First item in dropdown for creating new profiles
- **Create New Profile Modal:** Clone existing profiles with auto-naming
  - "Clone From:" dropdown to select source profile
  - Auto-generated name preview (e.g., PlayerProfile2, CoachProfile3)
  - Incremental versioning based on existing profiles
- **Field List:** Table displaying all fields with:
  - Order, Field Name, Display Name
  - Input Type (badge display)
  - Required/Admin Only status icons
  - Action buttons (Edit, Test, Remove)
- **Field Editor Modal:** Full field editing capabilities:
  - Basic properties (name, dbColumn, displayName, inputType)
  - Order, adminOnly, computed flags
  - Data source, placeholder, help text
  - Validation rules (required, email, min/max length, pattern, remote validation)
- **Add New Field:** Creates fields with default values
- **Remove Field:** Deletes fields with confirmation
- **Test Validation Modal:** Live validation testing without saving:
  - Input test value
  - Run validation
  - Display pass/fail with messages
- **Save Confirmation:** Shows number of jobs affected

**Field Types Supported (locked list):**
- TEXT, TEXTAREA, EMAIL, NUMBER, TEL
- DATE, DATETIME, CHECKBOX, SELECT, RADIO

**Field Visibility System:**
Each field has a `visibility` property that controls how it appears in forms:

- **`public`** (default): Visible to both registrants and admins
  - Displayed with a green "Public" badge
  - Rendered in registration forms for end users
  - Editable by both registrants and admins

- **`adminOnly`**: Visible only to administrators
  - Displayed with a yellow "Admin Only" badge
  - Fields like `AmtPaidToDate`, `PlayerUserId`
  - Useful for internal tracking without exposing to registrants
  - Editable only by admins in admin dashboard

- **`hidden`**: Technical fields never displayed in UI
  - Displayed with a gray "Hidden" badge
  - Auto-detected from .cshtml view files via `<input type="hidden" />`
  - Fields like `RegistrationId`, `IsTrue`, `Dob`, `Gender`, `Agerange`
  - Used for form state, computed values, or system-managed data
  - Not editable in forms but may be populated by backend logic
  - **Note on `IsTrue`**: Legacy DataAnnotations workaround for checkbox validation - stays hidden, but checkboxes now get clean `requiredTrue` validation metadata instead

**Visibility Detection:**
The migration system automatically determines field visibility:
1. **Primary source**: Parses corresponding .cshtml view file from GitHub
   - Looks for `<input type="hidden" asp-for="FieldName" />`
   - Marks those fields as `visibility="hidden"`
2. **Fallback**: Hardcoded classification in `CSharpToMetadataParser`
   - `AdminOnlyFields` HashSet → `visibility="adminOnly"`
   - `HiddenFields` HashSet → `visibility="hidden"`
3. **Default**: All other fields → `visibility="public"`

**Validation Testing Feature:**
Per user request: "if you could do this it would be wonderful"
- Tests validation rules against sample values
- Displays validation messages
- Does not modify database
- Useful for verifying regex patterns, remote validators, etc.

## Service Layer

### ProfileMigrationService
**Location:** `src/app/core/services/profile-migration.service.ts`

**Methods:**
```typescript
// Migration Dashboard APIs
getProfileSummaries(): Observable<ProfileSummary[]>
previewProfile(profileType: string): Observable<ProfileMigrationResult>
migrateProfile(profileType: string, dryRun: boolean): Observable<ProfileMigrationResult>
migrateAllProfiles(dryRun: boolean, filter?: string[]): Observable<ProfileBatchMigrationReport>

// Profile Editor APIs
getProfileMetadata(profileType: string): Observable<ProfileMetadata>
updateProfileMetadata(profileType: string, metadata: ProfileMetadata): Observable<ProfileMigrationResult>
testValidation(field: ProfileMetadataField, testValue: string): Observable<ValidationTestResult>

// Profile Creation APIs
cloneProfile(sourceProfileType: string): Observable<CloneProfileResult>
```

**TypeScript Interfaces:**
- `ProfileSummary` - Profile type with job counts
- `ProfileMigrationResult` - Single profile migration result
- `ProfileBatchMigrationReport` - Batch migration results
- `ProfileMetadata` - Complete metadata structure
- `ProfileMetadataField` - Individual field definition
- `FieldValidation` - Validation rules
- `ValidationTestResult` - Validation test response
- `CloneProfileRequest` - Request to clone profile
- `CloneProfileResult` - Result with new profile name and metadata

## Security & Routing

### SuperUser Guard
**Location:** `src/app/core/guards/auth.guard.ts`

**Function:** `superUserGuard: CanActivateFn`

**Logic:**
```typescript
// Checks if user is SuperUser and jobPath === 'tsic'
if (authService.isSuperuser() && user?.jobPath === 'tsic') {
  return true;
}
// Redirects to /tsic/home if not authorized
return router.createUrlTree(['/tsic/home']);
```

### Routes Configuration
**Location:** `src/app/app.routes.ts`

**Admin Routes:**
```typescript
{
  path: 'admin',
  component: LayoutComponent,
  canActivate: [superUserGuard],
  children: [
    {
      path: 'profile-migration',
      loadComponent: () => import('./admin/profile-migration/profile-migration.component')
    },
    {
      path: 'profile-editor',
      loadComponent: () => import('./admin/profile-editor/profile-editor.component')
    }
  ]
}
```

**Access URLs:**
- Migration Dashboard: `http://localhost:4200/tsic/admin/profile-migration`
- Profile Editor: `http://localhost:4200/tsic/admin/profile-editor`

**Security Requirements:**
1. User must be authenticated
2. User role must be "SuperUser" (case-insensitive)
3. User must have selected jobPath "tsic"
4. All three conditions must be met, else redirect to `/tsic/home`

## API Integration

### Backend Endpoints Used

**Profile Migration:**
- `GET /api/admin/profile-migration/profiles` - Get profile summaries
- `GET /api/admin/profile-migration/preview-profile/{type}` - Preview migration
- `POST /api/admin/profile-migration/migrate-profile/{type}?dryRun={bool}` - Migrate profile
- `POST /api/admin/profile-migration/migrate-all-profiles` - Batch migrate

**Profile Editing:**
- `GET /api/admin/profiles/{profileType}/metadata` - Get current metadata
- `PUT /api/admin/profiles/{profileType}/metadata` - Update metadata
- `POST /api/admin/profile-migration/test-validation` - Test field validation

**Profile Creation:**
- `POST /api/admin/profile-migration/clone-profile` - Clone profile with auto-naming

**Base URL:** Configured in `environment.ts` as `apiUrl`

## Create New Profile Feature

### Overview
Allows SuperUsers to create new profile types by cloning existing ones with automatic version incrementing.

### User Workflow
1. Open Profile Editor at `/tsic/admin/profile-editor`
2. Select "➕ CREATE NEW" from profile dropdown (first option)
3. Modal opens: "Create New Profile"
4. Select source profile from "Clone From:" dropdown
5. System displays: "Will prepare: [NewProfileName]"
6. Click "Create Profile" button
7. Backend clones metadata and generates new profile name
8. New profile added to available profiles list
9. Modal closes and new profile automatically loads for editing

### Auto-Naming Logic

**Algorithm:**
1. Extract base name from source profile (remove trailing numbers)
2. Find all existing profiles with same base name
3. Extract version numbers from matching profiles
4. Calculate max version + 1
5. Return base name + new version

**Examples:**
- Clone `PlayerProfile` → `PlayerProfile2`
- Clone `PlayerProfile2` → `PlayerProfile3`
- Clone `CoachProfile` → `CoachProfile2`
- Clone `CoachProfile5` → `CoachProfile6`
- Clone `ParentProfile` → `ParentProfile2`

### Backend Implementation

**Service Method:** `ProfileMetadataMigrationService.CloneProfileAsync()`
- Retrieves source profile metadata
- Calls `GenerateNewProfileNameAsync()` for auto-naming
- Deep clones metadata structure via JSON serialization
- Returns `CloneProfileResult` with new profile name and field count

**Helper Method:** `GenerateNewProfileNameAsync()`
- Queries database for all profiles with matching base name
- Uses regex to extract version numbers: `^{baseName}(\d+)$`
- Finds maximum version across all matches
- Returns incremented version name

**DTOs:**
```csharp
public class CloneProfileRequest {
    public string SourceProfileType { get; set; }
}

public class CloneProfileResult {
    public bool Success { get; set; }
    public string NewProfileType { get; set; }
    public string SourceProfileType { get; set; }
    public int FieldCount { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### Frontend Implementation

**Component State (Profile Editor):**
- `isCreateModalOpen` - Controls modal visibility
- `selectedCloneSource` - Selected source profile
- `newProfileName` - Auto-generated preview name
- `isCloning` - Loading state during API call

**Methods:**
- `openCreateModal()` - Opens modal, resets state
- `closeCreateModal()` - Closes modal
- `onCloneSourceSelected(source)` - Triggers name preview
- `generateNewProfileName(source)` - Client-side preview (mirrors backend logic)
- `createNewProfile()` - Calls API, updates profile list, auto-loads new profile

**Service Method:**
```typescript
cloneProfile(sourceProfileType: string): Observable<CloneProfileResult>
```

**Modal UI:**
- Bootstrap modal with dropdown selector
- Info alert showing preview: "Will prepare: [name]"
- Disabled state during cloning operation
- Success message after creation
- Auto-refresh of profile dropdown

## Design Patterns & Best Practices

### Angular Patterns
1. **Standalone Components:** No NgModule required, imports array in component decorator
2. **Signals:** Reactive state management (Angular 18+)
   - Use `signal()` for writable state
   - Use `computed()` for derived values
   - Access with `mySignal()`, update with `mySignal.set(value)`
3. **Dependency Injection:** `inject()` function in component constructor
4. **Lazy Loading:** Components loaded on-demand via route configuration
5. **FormsModule:** Two-way binding with `[(ngModel)]`

### Code Structure
- **TypeScript:** Strict typing, interfaces imported from service
- **HTML:** Control flow syntax (`@if`, `@for`) - Angular 17+ feature
- **SCSS:** Scoped component styles, Bootstrap 5 classes
- **Error Handling:** Try/catch with user-friendly error messages

### User Experience
- **Loading States:** Spinners during async operations
- **Disabled States:** Buttons disabled during save/load
- **Alert Messages:** Dismissible success/error alerts
- **Modals:** Bootstrap modal patterns for editing and preview
- **Confirmation Dialogs:** JavaScript `confirm()` for destructive actions
- **Badges & Icons:** Visual indicators for status (Bootstrap Icons)

## User Requirements Met

### Requirement Checklist
✅ **No hiding after migration:** Migration dashboard always accessible  
✅ **SuperUser only:** Protected by `superUserGuard`  
✅ **jobName='tsic' required:** Guard checks `user.jobPath === 'tsic'`  
✅ **Current state only:** No versioning, single metadata per profile  
✅ **Locked field types:** Predefined list in component (10 types)  
✅ **Validation testing:** Profile Editor includes test modal feature  
✅ **Create new profiles:** Clone existing profiles with auto-incremented names  

### Field Type Inference (Backend)
The backend `CSharpToMetadataParser` infers input types from C# properties:
- **State fields** → SELECT with dataSource: "states"
- **Phone fields** (*Phone) → TEL with pattern: ^\d{10}$
- **Height** → NUMBER with min: 36 (3ft), max: 84 (7ft)
- **Weight** → NUMBER with min: 30, max: 250
- **Is*/Has* fields** → CHECKBOX (IsActive, HasPermission, etc.)
- **SportAssnID** → Remote validation: `/api/Validation/ValidateUSALacrosseID`
- **Email** → EMAIL type with email validation
- **DateTime** → DATETIME type

### Validation Rule Detection

The parser automatically detects and converts DataAnnotations validation attributes:

**Standard Validations:**
- `[Required]` → `validation.required = true`
- `[EmailAddress]` → `validation.email = true`
- `[StringLength(max, MinimumLength = min)]` → `validation.minLength`, `validation.maxLength`
- `[Range(min, max)]` → `validation.min`, `validation.max`
- `[RegularExpression("pattern")]` → `validation.pattern`
- `[Compare("OtherField")]` → `validation.compare`
- `[Remote("action", "controller")]` → `validation.remote`

**Checkbox Required Pattern (RequiredTrue):**
- Detects: `[Range(typeof(bool), "true", "true")]` on `bool` properties
- Converts to: `validation.requiredTrue = true`
- This is the DataAnnotations workaround for "checkbox must be checked"
- Maps to Angular's `Validators.requiredTrue` for dynamic forms
- **Note:** The `IsTrue` hidden field pattern is legacy - it stays hidden but `requiredTrue` is the clean metadata property

**Example Conversion:**
```csharp
// C# POCO with DataAnnotations hack
[Required]
[Range(typeof(bool), "true", "true", ErrorMessage = "You must agree to terms")]
public bool AgreeToTerms { get; set; }

public bool IsTrue => true;  // Hidden field hack
```

Becomes:
```json
{
  "name": "agreeToTerms",
  "inputType": "CHECKBOX",
  "validation": {
    "requiredTrue": true
  }
},
{
  "name": "isTrue",
  "inputType": "CHECKBOX",
  "visibility": "hidden"  // Stays hidden - legacy implementation detail
}
```

**Frontend Display:**
- Preview shows: **Validators:** Must be checked
- Editor allows toggling the "Required True (Checkbox Must Be Checked)" option
- Validation testing supports checkbox true/false values

## Future Enhancements

### Potential Improvements
1. **Navigation Links:** Add menu items to main layout for easy access
2. **Undo/Redo:** Track changes and allow reversal
3. **Field Reordering:** Drag-and-drop to change order
4. **Bulk Field Operations:** Select multiple fields, bulk edit
5. **Export/Import:** JSON export for backup/restore
6. **Audit Log:** Track who changed what and when
7. **Field Templates:** Pre-configured field sets for common patterns
8. **Conditional Logic Builder:** UI for creating field conditions
9. **Preview Changes:** Show before/after comparison
10. **Rollback:** Revert to previous metadata version (would require versioning)

### Testing Recommendations
1. Unit tests for components (Jasmine/Karma)
2. Integration tests for service calls
3. E2E tests for user workflows (Cypress/Playwright)
4. Accessibility testing (ARIA labels, keyboard navigation)
5. Cross-browser testing

## Troubleshooting

### Common Issues

**Cannot access admin routes**
- Check user is logged in as SuperUser
- Verify jobPath is 'tsic'
- Check browser console for guard redirects

**Fields not saving**
- Check browser console for API errors
- Verify backend ProfileMigrationController is running
- Check database connection
- Ensure field structure matches backend DTOs

**Validation testing fails**
- Verify backend ValidationController is running
- Check remote validation URL is correct
- Ensure USA Lacrosse API is available (if implemented)

**Compilation errors**
- Run `npm install` to ensure dependencies
- Check Angular version (18+)
- Verify TypeScript version compatibility

**Create new profile not working**
- Check backend CloneProfileAsync method is implemented
- Verify profile name generation logic finds existing profiles
- Check API endpoint `/api/admin/profile-migration/clone-profile`
- Ensure new profile is added to dropdown after creation

## Related Documentation

- [profile-metadata-editor-design.md](./profile-metadata-editor-design.md) - Original design spec
- [angular-signal-patterns.md](./angular-signal-patterns.md) - Signal usage patterns
- [poco-class-structure-analysis.md](./poco-class-structure-analysis.md) - Backend POCO analysis
- Backend services in `TSIC-Core-Angular/src/backend/TSIC.Admin/Services/`
- Backend DTOs in `TSIC-Core-Angular/src/backend/TSIC.Admin/DTOs/`

---

## Session Notes

**October 26, 2025 - Initial Implementation:**
- Created migration architecture
- Implemented ProfileMigrationService with HTTP calls
- Created ProfileMigrationComponent with full UI
- Integrated with admin routing
- Added error handling and batch migration support

**November 1, 2025 - GitHub Authentication & Form Preview:**
- Fixed GitHub API 404 errors by adding Bearer token authentication
- Implemented field deduplication in CSharpToMetadataParser (derived class overrides base)
- Successfully migrated CAC04 profile (28 jobs, 22 fields after deduplication)
- Created ProfileFormPreviewComponent with dynamic form rendering
- Created FormFieldDataService with comprehensive dropdown data
- Integrated form preview into migration modal with toggle (form/JSON views)
- Fixed TypeScript strict null checking errors for HTML attribute bindings
- Replaced browser confirm() with Bootstrap modals throughout application
- **GitHub Token:** Stored in `appsettings.Development.json` (excluded from git)
- **Security:** Token is local-only, not committed to repository

**November 1, 2025 - CRITICAL: Job-Specific Dropdown Options Architecture:**

### The Problem
When migrating profile metadata to `PlayerProfileMetadataJson`, the original implementation:
1. Fetched profile metadata from GitHub **once**
2. Serialized it to JSON **once**
3. Applied the **same JSON to all jobs** using that profile

This created a critical flaw: all jobs would show identical dropdown options, but each job's `Job.JsonOptions` contains **unique, job-specific values** (specific team names, jersey sizes, position options, etc.).

### The Solution: Job-Specific Metadata Injection

**Backend Architecture Changes:**

1. **Added `ProfileFieldOption` class** (`ProfileMetadata.cs`)
   ```csharp
   public class ProfileFieldOption {
       public string Value { get; set; } = string.Empty;
       public string Label { get; set; } = string.Empty;
   }
   ```

2. **Added `Options` property to `ProfileMetadataField`**
   - Stores job-specific dropdown options populated from `Job.JsonOptions`
   - Embeds dropdown data directly into the metadata for each job

3. **Modified `MigrateProfileAsync` method** (`ProfileMetadataMigrationService.cs`)
   ```csharp
   // OLD: Same metadata for all jobs
   var metadataJson = JsonSerializer.Serialize(metadata, options);
   foreach (var job in jobs) {
       job.PlayerProfileMetadataJson = metadataJson;
   }
   
   // NEW: Job-specific metadata for each job
   foreach (var job in jobs) {
       var jobSpecificMetadata = CloneMetadata(metadata);
       InjectJobOptionsIntoMetadata(jobSpecificMetadata, job.JsonOptions);
       var metadataJson = JsonSerializer.Serialize(jobSpecificMetadata, options);
       job.PlayerProfileMetadataJson = metadataJson;
   }
   ```

4. **Created helper methods for JsonOptions parsing:**
   - `InjectJobOptionsIntoMetadata(metadata, jsonOptionsString)` - Main injection orchestrator
     - Parses `Job.JsonOptions` JSON string
     - Iterates SELECT fields in metadata
     - Maps field `dataSource` to JsonOptions keys
     - Injects parsed options into `field.Options`
   
   - `FindJsonOptionsKey(jsonOptions, dataSource)` - Smart key matching
     - Maps `"positions"` → `"List_Positions"`
     - Maps `"jerseySize"` → `"ListSizes_Jersey"`
     - Handles exact match, prefix match, and partial match
   
   - `ParseJsonOptionsArray(jsonElement)` - Array parser
     - Converts: `[{"Text":"Attack","Value":"attack"}]`
     - To: `List<ProfileFieldOption>`
     - Handles both object and string array formats
   
   - `GetPropertyString(element, propertyName)` - Safe property extraction

**Frontend Architecture Changes:**

1. **Added `ProfileFieldOption` interface** (`profile-migration.service.ts`)
   ```typescript
   export interface ProfileFieldOption {
       value: string;
       label: string;
   }
   ```

2. **Updated `ProfileMetadataField` interface**
   ```typescript
   export interface ProfileMetadataField {
       // ... existing properties
       options?: ProfileFieldOption[];  // NEW: Job-specific options
   }
   ```

3. **Updated `getDropdownOptions` priority** (`ProfileFormPreviewComponent`)
   
   **Reactive Signals Approach:**
   The component uses Angular Signals for proper reactivity when job-specific options change.
   
   ```typescript
   getDropdownOptions(field: ProfileMetadataField): Array<{ value: string; label: string }> {
       // Call _jobOptions() to establish reactive dependency
       // When _jobOptions signal changes, template automatically re-renders
       const jobOptions = this._jobOptions();
       return this.computeDropdownOptions(field, jobOptions);
   }
   
   private computeDropdownOptions(
       field: ProfileMetadataField, 
       jobOptions: Record<string, unknown> | null
   ): Array<{ value: string; label: string }> {
       // PRIORITY 1: Use field.options (populated during migration)
       if (field.options && field.options.length > 0) {
           return field.options;
       }
       
       // PRIORITY 2: Use jobOptions (from job selector in preview)
       if (jobOptions) {
           // Find matching key: "positions" -> "List_Positions"
           const dataSourceLower = field.dataSource.toLowerCase();
           const optionsKey = Object.keys(jobOptions).find(key => {
               const keyLower = key.toLowerCase();
               return keyLower.includes(dataSourceLower);
           });
           
           if (optionsKey && Array.isArray(jobOptions[optionsKey])) {
               return jobOptions[optionsKey].map((opt: any) => ({
                   value: opt.Value || opt.value || '',
                   label: opt.Text || opt.text || opt.label || ''
               }));
           }
       }
       
       // PRIORITY 3: Fallback to mock data (unmigrated profiles)
       return this.fieldDataService.getOptionsForDataSource(field.dataSource);
   }
   ```
   
   **Why This Works (Angular Signals Reactivity):**
   - When job is selected, `jobSpecificOptions.set(newOptions)` updates the signal
   - Signal is passed to child component via `[jobOptions]="jobSpecificOptions()"`
   - Child component stores it in `_jobOptions` signal
   - Template calls `getDropdownOptions(field)` in `@for` loop
   - `getDropdownOptions` calls `this._jobOptions()` - establishing reactive dependency
   - Angular detects signal change and automatically re-runs the `@for` loop
   - Dropdowns populate with new job-specific options
   
   **Key Insight:** Calling `_jobOptions()` inside the method (not just reading a property) 
   is what establishes the reactive dependency. When the signal changes, Angular knows 
   this method depends on it and triggers re-evaluation.

### Impact & Benefits

✅ **Each job gets unique `PlayerProfileMetadataJson`** with job-specific dropdown options embedded  
✅ **Forms automatically display correct options** for each job without runtime lookups  
✅ **No additional API calls needed** - options are pre-embedded in metadata  
✅ **Preview functionality enhanced** - job selector still works for pre-migration testing  
✅ **Three-tier fallback system** ensures options always available:
   1. Migrated data (job-specific, accurate)
   2. Preview mode (dynamic, for testing)
   3. Mock data (development, fallback)

### Data Flow Example

**Job 1 (CAC04 - Attack/Defense positions):**
```json
{
  "name": "position",
  "inputType": "SELECT",
  "dataSource": "positions",
  "options": [
    {"value": "attack", "label": "Attack"},
    {"value": "defense", "label": "Defense"},
    {"value": "midfield", "label": "Midfield"}
  ]
}
```

**Job 2 (PP10 - Forward/Goalie positions):**
```json
{
  "name": "position",
  "inputType": "SELECT",
  "dataSource": "positions",
  "options": [
    {"value": "forward", "label": "Forward"},
    {"value": "goalie", "label": "Goalie"},
    {"value": "defense", "label": "Defense"}
  ]
}
```

### Migration Workflow

1. **Fetch profile structure from GitHub** (once per profile type)
2. **Parse into base metadata** (field structure without options)
3. **For each job using this profile:**
   - Clone base metadata
   - Parse job's `JsonOptions` string
   - Find SELECT fields
   - Map `dataSource` to JsonOptions keys
   - Inject options into metadata
   - Serialize job-specific metadata
   - Save to `Job.PlayerProfileMetadataJson`

**Result:** Each of 28 CAC04 jobs now has identical field structure but unique dropdown options matching their specific `JsonOptions` data.

### Year Filtering for Preview Dropdown (Current Year ± 1)

**Challenge:** Job names always include a year, and the dropdown could contain jobs from many years, making it unwieldy for preview purposes.

**Solution:** Filter preview dropdown to show only jobs from current year ± 1, while migration still affects all jobs.

**Implementation:**

1. **Backend Enhancement:**
   - Added `AffectedJobYears` property to `ProfileMigrationResult` DTO
   - Service populates years alongside job names: `result.AffectedJobYears = jobs.Select(j => j.Year ?? "").ToList()`
   - Uses `Job.Year` property directly (no string parsing needed)

2. **Frontend Filtering:**
   - Added `affectedJobYears: string[]` to TypeScript interface
   - Created `sortedAffectedJobs` computed signal with year filtering:
     ```typescript
     const currentYear = new Date().getFullYear();
     const minYear = currentYear - 1;
     const maxYear = currentYear + 1;
     
     const filteredJobsWithIndex = jobs
         .map((name, index) => ({ name, year: years[index], index }))
         .filter(item => {
             if (!item.year) return true;
             const year = Number.parseInt(item.year, 10);
             return year >= minYear && year <= maxYear;
         });
     ```
   - Returns objects with `{ name, originalIndex }` for proper ID lookup
   - Created `getJobIdFromFilteredIndex` method to map filtered index to original job ID
   - Created `allAffectedJobs` computed signal for unfiltered full list

3. **UI Updates:**
   - Job selector label: "Preview with Job-Specific Options (Current Year ± 1):"
   - Shows only jobs from 2024, 2025, 2026 (when current year is 2025)
   - "All Affected Jobs" dropdown shows complete unfiltered list for reference

**Benefits:**
- ✅ Migration affects **all jobs** regardless of year
- ✅ Preview dropdown only shows **recent/relevant jobs** (current year ± 1)
- ✅ Admin can still see all affected jobs in expanded list
- ✅ No performance impact (filtering done client-side on already-loaded data)

---

## Field Visibility System & .cshtml Parsing (November 2025)

### Challenge: Reliably Detecting Hidden Fields

**Problem:** Need to distinguish between:
- **Public fields** - visible to registrants and admins
- **Admin-only fields** - visible only to admins (e.g., `AmtPaidToDate`, `PlayerUserId`)
- **Hidden fields** - technical fields never shown in UI (e.g., `RegistrationId`, `IsTrue`, `Dob`)

**Previous Approach:** Hardcoded HashSets in `CSharpToMetadataParser`
- ❌ Error-prone (easy to miss fields)
- ❌ Requires manual maintenance
- ❌ No source of truth linkage

**New Approach:** Parse .cshtml view files from GitHub to extract definitive hidden field list

### Solution: .cshtml Parsing for Hidden Field Detection

**Architecture:**

1. **GitHubProfileFetcher Enhancement**
   - Added `FetchViewFileAsync(profileType)` method
   - Fetches corresponding .cshtml file: `Views/PlayerRegistrationForms/PlayerSingle/{profileType}.cshtml` (PP profiles)
   - Fetches corresponding .cshtml file: `Views/PlayerRegistrationForms/PlayerMulti/{profileType}.cshtml` (CAC profiles)
   - Returns null if view file doesn't exist (graceful fallback)

2. **CSharpToMetadataParser Enhancement**
   - Added `ParseHiddenFieldsFromView(viewContent)` method
   - Uses regex to find: `<input type="hidden" asp-for="FieldName" />`
   - Regex pattern handles both attribute orders:
     ```csharp
     @"<input[^>]*type\s*=\s*[""']hidden[""'][^>]*asp-for\s*=\s*[""']([^""']+)[""']|" +
     @"<input[^>]*asp-for\s*=\s*[""']([^""']+)[""'][^>]*type\s*=\s*[""']hidden[""']"
     ```
   - Extracts field names from `asp-for` attribute values
   - Handles nested paths: `"FamilyPlayers[i].BasePP_Player_ViewModel.RegistrationId"` → `"RegistrationId"`
   - Returns `HashSet<string>` of hidden field names

3. **Field Name Extraction**
   - `ExtractBaseFieldName(aspForPath)` method processes asp-for values:
     - Removes array indices: `"FamilyPlayers[i].FieldName"` → `"FamilyPlayers.FieldName"`
     - Splits by `.` and takes last segment: `"FieldName"`
   - Handles complex nested ViewModels correctly

4. **Visibility Determination**
   - Updated `ParseProfileAsync` to accept optional `viewContent` parameter
   - Calls `ParseHiddenFieldsFromView` first if view content provided
   - `DetermineVisibility(propertyName)` checks in priority order:
     1. View-based hidden fields (source of truth from .cshtml)
     2. Hardcoded HiddenFields HashSet (fallback)
     3. AdminOnlyFields HashSet → `"adminOnly"`
     4. Default → `"public"`

5. **ProfileMetadataField DTO**
   - Added `Visibility` property: `string` with values `"public"` | `"adminOnly"` | `"hidden"`
   - Deprecated `AdminOnly` boolean (kept for backward compatibility with `[Obsolete]` attribute)
   - Default value: `"public"`

**Migration Flow:**

```csharp
// ProfileMetadataMigrationService.cs
var (profileSource, profileSha) = await _githubFetcher.FetchProfileSourceAsync(profileType);
var (baseSource, _) = await _githubFetcher.FetchBaseClassSourceAsync();
var viewContent = await _githubFetcher.FetchViewFileAsync(profileType); // NEW

var metadata = await _parser.ParseProfileAsync(
    profileSource, 
    baseSource, 
    profileType, 
    profileSha, 
    viewContent  // NEW parameter
);
```

**Example: PP10.cshtml Hidden Fields**

View file contains:
```html
<input type="hidden" asp-for="FamilyPlayers[i].IsTrue" />
<input type="hidden" asp-for="FamilyPlayers[i].BasePP_Player_ViewModel.AmtPaidToDate" />
<input type="hidden" asp-for="FamilyPlayers[i].BasePP_Player_ViewModel.PlayerUserId" />
<input type="hidden" asp-for="FamilyPlayers[i].BasePP_Player_ViewModel.FirstName" />
<input type="hidden" asp-for="FamilyPlayers[i].BasePP_Player_ViewModel.LastName" />
<input type="hidden" asp-for="FamilyPlayers[i].BasePP_Player_ViewModel.Dob" />
<input type="hidden" asp-for="FamilyPlayers[i].BasePP_Player_ViewModel.Gender" />
<input type="hidden" asp-for="FamilyPlayers[i].BasePP_Player_ViewModel.RegistrationId" />
<input type="hidden" asp-for="FamilyPlayers[i].BasePP_Player_ViewModel.Agerange" />
```

Parser extracts: `IsTrue`, `AmtPaidToDate`, `PlayerUserId`, `FirstName`, `LastName`, `Dob`, `Gender`, `RegistrationId`, `Agerange`

All marked with `visibility="hidden"` in metadata.

### Frontend Visibility Badge Display

**Profile Form Preview Component:**
- Added `getVisibilityBadge(field)` method
- Returns badge configuration based on visibility:
  ```typescript
  if (field.visibility === 'hidden') 
      return { class: 'bg-secondary', text: 'Hidden' };
  if (field.visibility === 'adminOnly') 
      return { class: 'bg-warning text-dark', text: 'Admin Only' };
  return { class: 'bg-success', text: 'Public' };
  ```
- Template displays badges alongside field labels
- Hidden fields still shown in preview but clearly marked

**Profile Editor Component:**
- Updated table header: "Admin Only" → "Visibility"
- Changed table cell to show visibility badge instead of lock icon
- Added visibility dropdown in edit modal:
  ```html
  <select class="form-select" [(ngModel)]="field.visibility">
      <option value="public">Public</option>
      <option value="adminOnly">Admin Only</option>
      <option value="hidden">Hidden</option>
  </select>
  ```
- Updated `addNewField()` to initialize `visibility: 'public'` for new fields

### Benefits

✅ **Source of truth:** .cshtml files are definitive (they define actual UI rendering)  
✅ **Automatic detection:** No manual HashSet maintenance required  
✅ **Accurate:** Catches all hidden fields, including computed/technical fields  
✅ **Graceful fallback:** If .cshtml not found, uses hardcoded classification  
✅ **Clear UI indication:** Badges show field visibility at a glance  
✅ **Editor support:** Admins can manually adjust visibility if needed  
✅ **Three visibility levels:** Public, Admin Only, Hidden (comprehensive categorization)

### Field Name vs Display Name Handling

**Three distinct properties:**

1. **`name`** (camelCase): JSON property name for frontend
   - Set by: `ToCamelCase(propertyName)`
   - Example: `"firstName"`
   - Used in: TypeScript interfaces, Angular forms, API requests

2. **`dbColumn`** (PascalCase): Database column name / C# property name
   - Set by: `propertyName` (original from POCO)
   - Example: `"FirstName"`
   - Used in: Database queries, C# backend code

3. **`displayName`** (Title Case): Human-readable label
   - Set by: `[Display(Name = "...")]` attribute or `PascalCaseToTitleCase(propertyName)`
   - Example: `"First Name"` or custom like `"Parent/Guardian First Name"`
   - Used in: Form labels, column headers, user-facing UI

**Extraction logic:**
```csharp
var field = new ProfileMetadataField
{
    Name = ToCamelCase(propertyName),           // firstName
    DbColumn = propertyName,                    // FirstName
    DisplayName = propertyName,                 // Temporary, overridden below
    // ...
};

// Extract [Display(Name = "...")] attribute
var displayAttr = attributes.Find(a => a.Name.ToString() == "Display");
if (displayAttr != null)
{
    field.DisplayName = ExtractDisplayName(displayAttr) ?? PascalCaseToTitleCase(propertyName);
}
else
{
    field.DisplayName = PascalCaseToTitleCase(propertyName);  // FirstName → First Name
}
```

**Result:** All three naming conventions properly captured and available in metadata.

---
- ✅ Preview dropdown is **manageable** (shows ~3-10 jobs instead of 50+)
- ✅ Clean implementation using `Job.Year` property
- ✅ Full job list still available in separate dropdown for reference

**Implementation Time:** ~8 hours total  
**Status:** ✅ Complete, form preview ready (requires dev server restart)

---

## Recent Updates (November 1, 2025)

### Field Ordering by Visibility During Migration

**Change:** Field sorting now happens during migration in the backend, not during display in the frontend.

**Backend Implementation (`CSharpToMetadataParser.cs`):**
```csharp
// After combining base and profile fields, sort by visibility
var hiddenFields = metadata.Fields.Where(f => f.Visibility == "hidden" || f.InputType == "HIDDEN").ToList();
var publicFields = metadata.Fields.Where(f => f.Visibility != "hidden" && f.InputType != "HIDDEN" && f.Visibility != "adminOnly").ToList();
var adminOnlyFields = metadata.Fields.Where(f => f.Visibility == "adminOnly").ToList();

// Rebuild fields list in visibility order
metadata.Fields.Clear();
metadata.Fields.AddRange(hiddenFields);
metadata.Fields.AddRange(publicFields);
metadata.Fields.AddRange(adminOnlyFields);

// Assign order numbers to sorted list
for (int i = 0; i < metadata.Fields.Count; i++)
{
    metadata.Fields[i].Order = i + 1;
}
```

**Frontend Simplification (`ProfileFormPreviewComponent`):**
```typescript
// Simply sort by order (sorting already done during migration)
sortedFields = computed(() => {
    const meta = this._metadata();
    if (!meta) return [];
    return [...meta.fields].sort((a, b) => a.order - b.order);
});
```

**Display Order:**
1. **Hidden fields** (at top) - Technical fields like `RegistrationId`, `PlayerUserId`
2. **Public fields** (in middle) - User-visible fields like `FirstName`, `Email`
3. **Admin-only fields** (at bottom) - Admin fields like `AmtPaidToDate`

**Benefits:**
- ✅ Sorting persisted in database (consistent across all views)
- ✅ No client-side computation needed (better performance)
- ✅ Single source of truth for field order
- ✅ Easier to debug and maintain

**Note:** Existing migrated profiles need to be **re-migrated** to apply the new field ordering.

### Hidden Fields Display in Preview

**Change:** Profile preview now shows **all** fields including hidden ones, with visibility badges.

**Previous Behavior:**
- Hidden fields were filtered out: `@if (!isHiddenField(field))`
- Users couldn't see what fields existed in the metadata

**New Behavior:**
- All fields displayed with visibility badges
- Hidden fields show gray "Hidden" badge
- Admin-only fields show yellow "Admin Only" badge
- Public fields show no badge (default)

**Template Change:**
```html
<!-- OLD: Skip hidden fields -->
@if (!isHiddenField(field)) {
    <div class="form-field-preview">...</div>
}

<!-- NEW: Show all fields with badges -->
<div class="form-field-preview">
    <label>
        {{ field.displayName }}
        @if (getVisibilityBadge(field); as badge) {
        <span class="badge" [ngClass]="badge.class">{{ badge.label }}</span>
        }
    </label>
</div>
```

**Benefits:**
- ✅ Complete visibility into profile structure
- ✅ Clear indication of field visibility levels
- ✅ Helpful for debugging and validation
- ✅ Admin can see technical fields without accessing database

### Refresh Token Fix for Page Reload

**Problem:** Pressing Ctrl+Shift+R (hard refresh) would redirect users to login page even though they had a valid refresh token.

**Root Cause:**
- Auth guard checked `isAuthenticated()` synchronously
- If access token was expired, guard returned `false` immediately
- Redirect happened before refresh token could be used
- Background refresh attempt in `initializeFromToken()` created race condition

**Solution:**

1. **Updated `authGuard` to wait for refresh:**
```typescript
export const authGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const isAuth = authService.isAuthenticated();
    if (isAuth) {
        return true;
    }

    // Try to refresh if we have a refresh token
    const refreshToken = authService.getRefreshToken();
    if (refreshToken) {
        return authService.refreshAccessToken().pipe(
            map(() => true),
            catchError(() => {
                return [router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } })];
            })
        );
    }

    return router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } });
};
```

2. **Updated `roleGuard` similarly:**
```typescript
export const roleGuard: CanActivateFn = (route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    const user = authService.getCurrentUser();
    if (user?.regId && user?.jobPath) {
        return true;
    }

    // Try refresh if not authenticated but have refresh token
    const isAuth = authService.isAuthenticated();
    if (!isAuth && authService.getRefreshToken()) {
        return authService.refreshAccessToken().pipe(
            map(() => {
                const refreshedUser = authService.getCurrentUser();
                if (refreshedUser?.regId && refreshedUser?.jobPath) {
                    return true;
                }
                return router.createUrlTree(['/tsic/role-selection']);
            }),
            catchError(() => {
                return [router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } })];
            })
        );
    }

    if (isAuth) {
        return router.createUrlTree(['/tsic/role-selection']);
    }

    return router.createUrlTree(['/tsic/login'], { queryParams: { returnUrl: state.url } });
};
```

3. **Simplified `initializeFromToken`:**
```typescript
private initializeFromToken(): void {
    const token = this.getToken();
    if (!token) {
        this.currentUser.set(null);
        return;
    }

    try {
        const payload = this.decodeToken(token);

        // Check if token is expired
        if (payload.exp) {
            const expirationDate = new Date(payload.exp * 1000);
            const now = new Date();

            if (expirationDate <= now) {
                console.warn('Access token expired. Guards will handle refresh on next navigation.');
                // Don't set user to null - let guards attempt refresh
                return;
            }
        }

        const user: AuthenticatedUser = { /* ... */ };
        this.currentUser.set(user);
    } catch (error) {
        console.error('Failed to decode token:', error);
        this.currentUser.set(null);
    }
}
```

**Benefits:**
- ✅ Page refresh (Ctrl+R, Ctrl+Shift+R, F5) no longer logs users out
- ✅ Guards wait for token refresh to complete before making routing decisions
- ✅ No race conditions between initialization and guard checks
- ✅ Clean separation: guards handle refresh, service handles token decoding
- ✅ Better user experience (stays logged in across refreshes)

**Flow:**
1. User refreshes page
2. App initializes, token is expired
3. Guard detects expired token but sees refresh token exists
4. Guard calls `refreshAccessToken()` and waits for result
5. If refresh succeeds → allow navigation
6. If refresh fails → redirect to login
7. No premature redirects or race conditions

---

**Implementation Date:** 2024-2025  
**Angular Version:** 18+  
**Bootstrap Version:** 5  
**Backend:** ASP.NET Core with Entity Framework Core

