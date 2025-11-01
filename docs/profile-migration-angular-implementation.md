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

**Implementation Date:** 2024  
**Angular Version:** 18+  
**Bootstrap Version:** 5  
**Backend:** ASP.NET Core with Entity Framework Core
