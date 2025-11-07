# Player Registration Architecture - Modernization Proposal

**Date**: October 31, 2025  
**Status**: Draft for Discussion

---

## Implementation Status Update — November 7, 2025

Recent work aligned the login, routing, and wizard orchestration to enable a clean, reliable return to the Player Registration Wizard after Family login. Highlights:

- Deep-linking to the wizard now works consistently using `returnUrl`.
  - Family Check step builds a normalized `returnUrl` such as `/{jobPath}/register-player?step=start` (or `/register-player` when no `jobPath`).
  - The login page is themed via `theme=family&header=...&subHeader=...` and includes `intent=player-register` and the `returnUrl`.
- Authentication/guards behavior:
  - The wizard intentionally calls `auth.logoutLocal()` on init to ensure a clean slate, then expects a bounce through the login page.
  - `redirectAuthenticatedGuard` now honors an internal `returnUrl` when the user is already authenticated, navigating directly to the wizard instead of role selection.
  - `LoginComponent` normalizes malformed `returnUrl` values (decodes, strips leading double slashes) and navigates to it without auto-selecting any registration context.
- Stability fixes:
  - Prevented repeated `/registrations` fetches via a one-shot guard in `AuthService`; reset occurs on `logoutLocal()`.
  - Normalized `returnUrl` construction in Family Check to avoid `//register-player` and to correctly infer `jobPath` when missing.
- UX improvement:
  - The wizard header shows a badge with the active Family User (when selected) or falls back to the Family Account username (from `last_username` in local storage).

What’s next (high-level):

- Implement a wizard step to select the Family User, then determine whether there is an existing player registration for the job or a new one should be created.
- Add/align backend endpoints for: family users list, job-specific registration summary, form schema, and create-registration; enrich the token after the wizard decides on a registration id.
- Prefill mapping (PP/CAC) and edge cases (roster full, waitlist, partial forms).

Key file touchpoints (frontend):

- `app/registration-wizards/player-registration-wizard/player-registration-wizard.component.ts`
- `app/registration-wizards/player-registration-wizard/player-registration-wizard.component.html`
- `app/registration-wizards/player-registration-wizard/registration-wizard.service.ts`
- `app/registration-wizards/player-registration-wizard/steps/family-check.component.ts`
- `app/login/login.component.ts`
- `app/core/services/auth.service.ts`
- `app/core/guards/auth.guard.ts` (redirect behavior)

Deep-linking contract examples:

- Start from job context: `/{jobPath}/register-player?step=start`
- Start without job context: `/register-player?step=start`
- Family login URL (example): `/tsic/login?theme=family&header=Family%20Account%20Login&subHeader=Sign%20in%20to%20continue&intent=player-register&returnUrl=/{jobPath}/register-player%3Fstep%3Dstart`

Notes:

- We purposefully keep login minimal (Phase 1 token). Token enrichment (adding `regId`, `jobPath`) happens after the wizard establishes context.
- Return URL parsing uses router `parseUrl` to safely handle internal paths.

---

## Executive Summary

This document proposes a modernized architecture for the TSIC player registration system, migrating from hardcoded C# view models and Razor forms to a dynamic, API-driven approach suitable for Angular frontend integration.

### Current State Problems
1. **Hardcoded HTML Forms**: Forms manually built based on tribal knowledge of which fields are dropdowns, textareas, etc.
2. **Redundant View Models**: Separate classes for player view vs admin view (e.g., uniform_number only shown to admin)
3. **Manual Field Lists**: `ListModelDistinctFields` arrays manually maintained for reporting
4. **No Input Type Metadata**: Profile classes only define properties without specifying input types or constraints
5. **Not Portable**: Cannot auto-generate Angular forms from existing metadata

### Proposed Solution
Transform the registration system into a **metadata-driven architecture** where:
- Profile definitions include complete field metadata (input type, validation, data source)
- Backend API serves comprehensive JSON metadata to Angular
- Forms are dynamically generated from metadata
- Single source of truth eliminates redundancy

---

## Background Context

### Data Model Overview (From ER Diagram)

**Complete Registration Architecture**:

```
AspNetUsers (Parent Account)
    ↓
Family_Members (Junction Table)
    ↓ (ParentUserId → ChildUserId)
AspNetUsers (Child/Player Accounts)
    ↓ (UserId)
Registrations (One per child per team/camp)
    ↓ (jobID)
Jobs (Configuration & Metadata)
```

**Key Tables & Relationships**:

1. **AspNetUsers** (Identity + Demographics - 38 fields)
   - Core: Id, UserName, Email, PasswordHash (ASP.NET Identity)
   - Demographics: FirstName, LastName, dob, gender, phone, cellphone, address fields
   - Created once per person (parent or child)
   - Pre-filled when user is authenticated

2. **Family_Members** (Family Structure - 4 fields)
   - Links parent AspNetUsers to child AspNetUsers
   - `ParentUserId` → AspNetUsers.Id
   - `ChildUserId` → AspNetUsers.Id
   - `IsDefault`: Flags primary family relationship
   - Enables multi-child registration flow

3. **Registrations** (Registration Data - 112 fields)
   - One record per player per team/camp registration
   - `UserId` → AspNetUsers.Id (the player)
   - `Family_UserId` → AspNetUsers.Id (the parent who registered them)
   - `jobID` → Jobs.JobId
   - `assigned_teamID` → Teams.TeamId (nullable, set after registration)
   - Contains all profile-specific fields (jersey_size, school_name, waivers, etc.)
   - Profile defines WHICH fields to show and make required

4. **Jobs** (Job Configuration)
   - `JobId`: Primary key
   - `CoreRegformPlayer`: Profile configuration string (e.g., "PP47|BYGRADYEAR|ALLOWPIF")
   - `JsonOptions`: Job-specific dropdown options (sizes, positions, grad years)
   - `PlayerProfileMetadataJson`: Dynamic form metadata (field definitions, validation, order)
   - `USLaxNumberValidThroughDate`: Validation date for USLax numbers
   - `ExpiryUsers`: Registration deadline

**Key Architectural Insights**:
- ✅ Demographics (name, dob, address) come from AspNetUsers - NOT asked in registration forms
- ✅ Registration forms only collect Registrations table fields defined by profile
- ✅ Family structure maintained via Family_Members junction table
- ✅ Each child has own AspNetUsers record with demographics
- ✅ Parent can register multiple children in single transaction
- ✅ Profile (PP47, CAC12) defines subset of 112 Registrations fields to show
- ✅ **NEW**: `Jobs.PlayerProfileMetadataJson` stores complete form metadata (separate from dropdown options)
- ✅ **NEW**: `Jobs.JsonOptions` stores dropdown data only (maintains backward compatibility)

---

### Registration Types

**PP (PlayerProfile)**: Single registration per player per job
- Example: Seasonal league registration
- One team selection, one form submission per player

**CAC (CampsAndClinics)**: Multiple registrations per player per job
- Example: Summer camp series
- Parent selects which children attend which camps
- One form per player (not per camp)

### CoreRegformPlayer Structure

Format: `"PP47|BYGRADYEAR|ALLOWPIF"` (pipe-delimited, 3 parts)

**Part 1 - Profile Name**: `PP47`, `PP50`, `CAC12`, etc.
- References C# class in TSIC-Unify-Models project
- Located in `ViewModels/RegPlayersSingle_ViewModels` (PP) or `RegPlayersMulti_ViewModels` (CAC)

**Part 2 - Team Constraint Strategy**:
- `BYGRADYEAR`: Filter teams by graduation year
- `BYAGEGROUP`: Filter teams by age group
- `BYAGERANGE`: Filter teams by age range
- `BYCLUBNAME`: Filter teams by club name
- User selects constraint value first, then sees filtered team list

**Part 3 - Payment Options**:
- `ALLOWPIF`: Pay In Full option enabled for deposit-based registrations

### Job.JsonOptions Property

Each Job entity contains a JSON string with dropdown options specific to that job:

```json
{
  "ListSizes_Jersey": [
    {"Text": "adult s", "Value": "adult s"},
    {"Text": "adult m", "Value": "adult m"}
  ],
  "List_Positions": [
    {"Text": "attack", "Value": "attack"},
    {"Text": "defense", "Value": "defense"}
  ],
  "List_GradYears": [
    {"Text": "2027", "Value": "2027"},
    {"Text": "2028", "Value": "2028"}
  ]
}
```

**Key Insight**: Profile defines **which** fields to show; JsonOptions defines **values** for those fields.

### Jobs.PlayerProfileMetadataJson Storage Strategy

**Implemented Solution**: Separate database column for profile metadata

**Schema**:
- `Jobs.JsonOptions` (existing): Dropdown options only - maintains backward compatibility
- `Jobs.PlayerProfileMetadataJson` (new): Complete form metadata - independent versioning

**Structure of PlayerProfileMetadataJson**:
```json
{
  "profileName": "PP47",
  "registrationType": "PP",
  "teamConstraint": "BYGRADYEAR",
  "allowPayInFull": true,
  "fields": [
    {
      "name": "jersey_size",
      "dbColumn": "jersey_size",
      "displayName": "Jersey Size",
      "inputType": "select",
      "dataSource": "ListSizes_Jersey",
      "required": true,
      "order": 10,
      "adminOnly": false,
      "validation": {
        "required": true
      }
    },
    {
      "name": "sportAssnID",
      "dbColumn": "sportAssnID",
      "displayName": "US Lacrosse Number",
      "inputType": "text",
      "required": true,
      "order": 5,
      "adminOnly": false,
      "validation": {
        "required": true,
        "externalApi": {
          "endpoint": "/api/registration/validate-uslax",
          "validThroughDateField": "USLaxNumberValidThroughDate"
        }
      }
    }
  ]
}
```

**Benefits**:
- ✅ Zero breaking changes to existing software
- ✅ Clear separation of concerns (dropdown data vs form structure)
- ✅ Can populate PlayerProfileMetadataJson gradually per job
- ✅ Independent versioning of profile metadata
- ✅ Nullable column - old jobs work fine without it

**API Response** (GET /api/jobs/{jobPath}):
```json
{
  "jobId": "guid",
  "jobName": "Summer League 2025",
  "jsonOptions": "{\"ListSizes_Jersey\":[...]}",
  "playerProfileMetadataJson": "{\"profileName\":\"PP47\",\"fields\":[...]}"
}
```

Angular receives both and can:
1. Parse `playerProfileMetadataJson` for form structure
2. Parse `jsonOptions` for dropdown values
3. Match field `dataSource` property to `jsonOptions` keys

### Profile Inheritance

- **Base Class**: Common demographics across all registrations (name, DOB, address, parent contact)
- **Profile Class**: Job-specific fields extending base (jersey size, school, waivers, special requests)

Example:
```csharp
public class PP50_ViewModel : PP_ViewModel
{
    // PP_ViewModel contains base demographics
    // PP50_ViewModel adds job-specific fields
}
```

### Family Registration Flow

**Database Structure** (from ER diagram):

**Family_Members Table**:
- `Id`: Primary key (uniqueidentifier)
- `ParentUserId`: FK → AspNetUsers.Id (the parent account)
- `ChildUserId`: FK → AspNetUsers.Id (the child/player account)
- `IsDefault`: Indicates default/primary family relationship

**Data Model**:
- **Parent**: AspNetUsers record (authenticated account)
- **Children**: Separate AspNetUsers records (each child has own demographics)
- **Family Link**: Family_Members junction table connects parent to children
- **Registrations**: Each child can have multiple Registrations (one per job/team)

**Query Pattern**:
```sql
-- Load family children for registration
SELECT child.* 
FROM Family_Members fm
JOIN AspNetUsers child ON fm.ChildUserId = child.Id
WHERE fm.ParentUserId = @ParentUserId AND fm.IsDefault = 1
```

**Registration Flow**:
- Parent authenticates (has AspNetUsers.Id)
- System loads children via Family_Members table
- Parent selects which children to register for job
- For CAC: Grid of camps × players with checkboxes
- Forms generated per player (demographics pre-filled from AspNetUsers)
- Profile fields collected per child (stored in Registrations table)
- Submit all at once (transaction scope includes all children's registrations)

**Key Foreign Keys** (from ER diagram):
- `Registrations.UserId` → AspNetUsers.Id (the player being registered)
- `Registrations.Family_UserId` → AspNetUsers.Id (the parent who registered them)
- `Registrations.jobID` → Jobs.JobId
- `Registrations.assigned_teamID` → Teams.TeamId (nullable, assigned after registration)

### Special Field: USLax Number Validation

**Field**: `USLaxNumberIsValid`
- **Validation**: External API call to USA Lacrosse
- **Two Requirements**:
  1. Number must exist and be valid (API check)
  2. Must be valid through `Job.USLaxNumberValidThroughDate`
- **Blocking**: Player cannot register without valid USLax number
- **Implementation**: You have USA Lacrosse API credentials and parsing code

---

## Proposed Architecture

### Phase 1: Profile Metadata System (API-Driven)

#### Backend Components

**1. ProfileMetadataService** (new service)

```csharp
public interface IProfileMetadataService
{
    Task<ProfileMetadata> GetProfileMetadataAsync(string jobPath);
    Task<List<TeamOption>> GetFilteredTeamsAsync(Guid jobId, string constraintType, string value);
}
```

**Responsibilities**:
- Parse `CoreRegformPlayer` string from Job entity
- Resolve profile name (PP47, CAC12, etc.)
- Load field definitions (via reflection OR new metadata format)
- Merge base fields + profile-specific fields
- Combine with `Job.JsonOptions` for dropdown data
- Return comprehensive metadata to Angular

**Implementation Options**:

**Option A: Reflection on Existing C# Classes** (recommended for Phase 1)
- Scan TSIC-Unify-Models project classes
- Extract properties and DataAnnotations
- Enhance with custom attributes for missing metadata:

```csharp
[FieldMetadata(InputType = "select", DataSource = "ListSizes_Jersey", Order = 10)]
[AdminOnly]
public string JerseySize { get; set; }

[FieldMetadata(InputType = "textarea", MaxLength = 500, Order = 20)]
public string SpecialRequests { get; set; }

[FieldMetadata(InputType = "checkbox", MustBeTrue = true, Order = 100)]
[Display(Name = "I agree with the Waiver Terms and Conditions")]
[Required]
public bool BWaiverSigned1 { get; set; }

[FieldMetadata(InputType = "text", Order = 5)]
[ExternalValidation(Endpoint = "/api/registration/validate-uslax")]
public string UslaxNumber { get; set; }
```

**Option B: Database-Driven Profiles** (future enhancement)
- Create tables: `RegistrationProfiles`, `ProfileFields`, `FieldValidations`
- Enable admin UI for creating/editing profiles without code deployment
- Keep C# classes as seed data or migration path

**2. Custom Attributes** (new)

```csharp
[AttributeUsage(AttributeTargets.Property)]
public class FieldMetadataAttribute : Attribute
{
    public string InputType { get; set; } // text, textarea, select, checkbox, date, number, email, tel
    public string? DataSource { get; set; } // References Job.JsonOptions key
    public int Order { get; set; } // Display order in form
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public bool MustBeTrue { get; set; } // For checkbox agreements
}

[AttributeUsage(AttributeTargets.Property)]
public class AdminOnlyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class ExternalValidationAttribute : Attribute
{
    public string Endpoint { get; set; }
}
```

**3. API Endpoint**: `GET /api/registration/profile/{jobPath}`

**Response Structure**:

```json
{
  "profileName": "PP47",
  "registrationType": "PlayerProfile",
  "teamConstraint": "BYGRADYEAR",
  "allowPayInFull": true,
  "baseFields": [
    {
      "name": "firstName",
      "displayName": "First Name",
      "inputType": "text",
      "required": true,
      "order": 1,
      "adminOnly": false,
      "validation": {
        "required": true,
        "maxLength": 50
      }
    },
    {
      "name": "dateOfBirth",
      "displayName": "Date of Birth",
      "inputType": "date",
      "required": true,
      "order": 5,
      "adminOnly": false,
      "validation": {
        "required": true,
        "maxDate": "today"
      }
    }
  ],
  "profileFields": [
    {
      "name": "jerseySize",
      "displayName": "Jersey Size",
      "inputType": "select",
      "dataSource": "ListSizes_Jersey",
      "required": true,
      "order": 10,
      "adminOnly": false,
      "validation": {
        "required": true
      }
    },
    {
      "name": "uslaxNumber",
      "displayName": "US Lacrosse Number",
      "inputType": "text",
      "required": true,
      "order": 5,
      "adminOnly": false,
      "validation": {
        "required": true,
        "externalApi": {
          "endpoint": "/api/registration/validate-uslax",
          "validThroughDate": "2025-12-31"
        }
      }
    },
    {
      "name": "specialRequests",
      "displayName": "Special Requests",
      "inputType": "textarea",
      "required": false,
      "order": 20,
      "adminOnly": false,
      "validation": {
        "maxLength": 500
      }
    },
    {
      "name": "bWaiverSigned1",
      "displayName": "I agree with the Waiver Terms and Conditions",
      "inputType": "checkbox",
      "required": true,
      "order": 100,
      "adminOnly": false,
      "validation": {
        "required": true,
        "mustBeTrue": true
      }
    },
    {
      "name": "uniformNumber",
      "displayName": "Uniform Number",
      "inputType": "number",
      "required": false,
      "order": 200,
      "adminOnly": true,
      "validation": {
        "min": 0,
        "max": 99
      }
    }
  ],
  "options": {
    "ListSizes_Jersey": [
      {"text": "Adult S", "value": "adult s"},
      {"text": "Adult M", "value": "adult m"},
      {"text": "Adult L", "value": "adult l"}
    ],
    "List_Positions": [
      {"text": "Attack", "value": "attack"},
      {"text": "Defense", "value": "defense"},
      {"text": "Goalie", "value": "goalie"},
      {"text": "Midfield", "value": "midfield"}
    ],
    "List_GradYears": [
      {"text": "2027", "value": "2027"},
      {"text": "2028", "value": "2028"},
      {"text": "2029", "value": "2029"}
    ]
  }
}
```

---

### Phase 2: Dynamic Team Filtering

**TeamFilterService** (new service)

```csharp
public interface ITeamFilterService
{
    Task<List<TeamOption>> GetFilteredTeamsAsync(Guid jobId, string constraintType, string value);
}
```

**API Endpoint**: `GET /api/registration/teams?jobId={guid}&constraintType=BYGRADYEAR&value=2027`

**Response**:
```json
{
  "teams": [
    {
      "teamId": "guid-1",
      "teamName": "Boys 2027 Blue",
      "description": "Competitive team for 2027 graduates",
      "registrationFee": 850.00,
      "allowDeposit": true,
      "depositAmount": 200.00
    },
    {
      "teamId": "guid-2",
      "teamName": "Boys 2027 White",
      "description": "Developmental team for 2027 graduates",
      "registrationFee": 650.00,
      "allowDeposit": false
    }
  ]
}
```

**Logic**:
1. Parse `constraintType` from `CoreRegformPlayer` part 2
2. Query Teams table WHERE `JobId = jobId` AND team name/property matches constraint
3. Filter examples:
   - `BYGRADYEAR`: `TeamName LIKE '%2027%'`
   - `BYAGEGROUP`: `AgeGroup = value`
   - `BYAGERANGE`: `MinAge <= value AND MaxAge >= value`
   - `BYCLUBNAME`: `ClubName = value`

**Angular Flow**:
1. User selects grad year (2027) from dropdown
2. Angular calls `/api/registration/teams?...&value=2027`
3. Team dropdown populates with filtered results
4. User selects team and continues registration

---

### Phase 3: USLax Integration

**USLaxValidationService** (new service)

```csharp
public interface IUSLaxValidationService
{
    Task<USLaxValidationResult> ValidateNumberAsync(string uslaxNumber, Guid jobId);
}

public class USLaxValidationResult
{
    public bool IsValid { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Message { get; set; }
}
```

**API Endpoint**: `POST /api/registration/validate-uslax`

**Request**:
```json
{
  "uslaxNumber": "123456",
  "jobId": "guid"
}
```

**Response**:
```json
{
  "isValid": true,
  "expiryDate": "2026-06-30",
  "message": "Valid through June 30, 2026"
}
```

**OR** (invalid):
```json
{
  "isValid": false,
  "expiryDate": "2024-12-31",
  "message": "Membership expired on December 31, 2024. Please renew at uslacrosse.org"
}
```

**Implementation**:
1. Call USA Lacrosse API with your credentials
2. Parse response using your existing code
3. Compare expiry date with `Job.USLaxNumberValidThroughDate`
4. Return validation result

**Angular Integration**:
- Use as async validator on form field
- Show loading spinner during validation
- Display validation result (green checkmark or red error)
- Block form submission if invalid
- Cache result to avoid repeated API calls

---

### Phase 4: Angular Dynamic Forms

#### RegistrationFormComponent (Smart Component)

**Responsibilities**:
1. Fetch profile metadata on init
2. Build reactive FormGroup dynamically from field definitions
3. Render inputs based on `inputType`
4. Populate select options from `options` object
5. Apply validation rules from metadata
6. Handle async validators (USLax)
7. Show/hide admin-only fields based on user role
8. Handle conditional field logic (future enhancement)

**Example Template Pattern**:

```html
<form [formGroup]="registrationForm">
  @for (field of fields(); track field.name) {
    <div class="mb-3">
      @switch (field.inputType) {
        @case ('text') {
          <label [for]="field.name" class="form-label">
            {{ field.displayName }}
            @if (field.required) {<span class="text-danger">*</span>}
          </label>
          <input 
            [id]="field.name"
            [formControlName]="field.name"
            type="text"
            class="form-control"
            [class.is-invalid]="isFieldInvalid(field.name)" />
          <div class="invalid-feedback">
            {{ getFieldError(field.name) }}
          </div>
        }
        @case ('select') {
          <label [for]="field.name" class="form-label">
            {{ field.displayName }}
            @if (field.required) {<span class="text-danger">*</span>}
          </label>
          <select 
            [id]="field.name"
            [formControlName]="field.name"
            class="form-select"
            [class.is-invalid]="isFieldInvalid(field.name)">
            <option value="">-- Select --</option>
            @for (option of getOptions(field.dataSource); track option.value) {
              <option [value]="option.value">{{ option.text }}</option>
            }
          </select>
          <div class="invalid-feedback">
            {{ getFieldError(field.name) }}
          </div>
        }
        @case ('textarea') {
          <label [for]="field.name" class="form-label">
            {{ field.displayName }}
            @if (field.required) {<span class="text-danger">*</span>}
          </label>
          <textarea 
            [id]="field.name"
            [formControlName]="field.name"
            class="form-control"
            rows="4"
            [maxlength]="field.validation?.maxLength"
            [class.is-invalid]="isFieldInvalid(field.name)"></textarea>
          <div class="invalid-feedback">
            {{ getFieldError(field.name) }}
          </div>
        }
        @case ('checkbox') {
          <div class="form-check">
            <input 
              [id]="field.name"
              [formControlName]="field.name"
              type="checkbox"
              class="form-check-input"
              [class.is-invalid]="isFieldInvalid(field.name)" />
            <label [for]="field.name" class="form-check-label">
              {{ field.displayName }}
              @if (field.required) {<span class="text-danger">*</span>}
            </label>
            <div class="invalid-feedback">
              {{ getFieldError(field.name) }}
            </div>
          </div>
        }
      }
    </div>
  }
</form>
```

#### Multi-Step Registration Wizard

The registration process uses a multi-step wizard pattern with dynamic steps based on job configuration. This approach provides clear user guidance while maintaining flexibility for different registration types (PP vs CAC).

**Enhanced Flow (6-7 Steps)**:

1. **Select Players**
   - Display family members from authenticated user account
   - Allow selection of which children to register
   - Option to add new player (creates AspNetUsers + Family_Members record)
   - Multi-select for batch registration

2. **Select Constraint** (Conditional - only if job has `teamConstraint`)
   - Show constraint selection per player (grad year, age group, etc.)
   - Based on `CoreRegformPlayer` part 2 (BYGRADYEAR, BYAGEGROUP, etc.)
   - Skip this step if constraint type is empty
   - Used to filter available teams in next step

3. **Select Teams/Camps**
   - **PP Type**: Display filtered teams based on constraint
     - One team per player
     - Show team cards with name, description, fee, deposit option
   - **CAC Type**: Grid of camps × players with checkboxes
     - Multiple camps per player
     - Show camp dates, descriptions, fees
     - Visual total calculation

4. **Player Forms** (Dynamic from Metadata)
   - One form per selected player
   - Form fields generated from `PlayerProfileMetadataJson`
   - Navigation between players:
     - **Option A**: Tabs for quick switching
     - **Option B**: Sequential with Next/Previous
     - **Option C**: Accordion (expand/collapse)
   - Real-time validation from metadata
   - External validation (e.g., USLax number)
   - Visual indicators for completion status

5. **Review**
   - Summary of all registrations before payment
   - Show player name, team, key details, fee
   - Edit links to go back and modify
   - Total cost calculation
   - Critical for multi-player registrations to catch errors

6. **Payment**
   - Select payment option (Pay in Full vs Deposit)
   - Only show deposit option if `allowPayInFull` is true
   - Payment processing integration
   - Secure card entry

7. **Confirmation**
   - Success message with confirmation number
   - Summary of what was registered
   - Receipt download/email options
   - Navigation to dashboard or home

**Progress Indicator**:
```html
<div class="progress mb-4">
  <div class="progress-bar" [style.width]="progressPercent() + '%'">
    Step {{ currentStep() }} of {{ totalSteps() }}
  </div>
</div>
```

**Wizard State Management**:
```typescript
export class RegistrationWizardComponent {
  // Wizard state
  currentStep = signal(1);
  totalSteps = computed(() => {
    // Dynamic step count based on job configuration
    let steps = 5; // Base: Players, Teams, Forms, Payment, Confirmation
    if (this.hasConstraint()) steps++; // Add constraint step
    return steps;
  });
  
  // Registration data (accumulates across steps)
  selectedPlayers = signal<Player[]>([]);
  playerConstraints = signal<Map<string, string>>(new Map()); // playerId -> gradYear
  playerTeams = signal<Map<string, Team[]>>(new Map()); // playerId -> teams
  playerForms = signal<Map<string, FormData>>(new Map()); // playerId -> form data
  paymentOption = signal<'PIF' | 'Deposit'>('PIF');
  
  // Navigation
  canGoNext = computed(() => {
    switch (this.currentStep()) {
      case 1: return this.selectedPlayers().length > 0;
      case 2: return this.allPlayersHaveConstraints();
      case 3: return this.allPlayersHaveTeams();
      case 4: return this.allFormsValid();
      case 5: return true; // Review always allows next
      default: return false;
    }
  });
  
  next() {
    if (this.canGoNext()) {
      this.currentStep.update(step => step + 1);
    }
  }
  
  back() {
    this.currentStep.update(step => Math.max(1, step - 1));
  }
}
```

**Component Structure**:
```
registration-wizard/
├── registration-wizard.component.ts        # Main wizard container
├── registration-wizard.component.html      
├── steps/
│   ├── player-selection.component.ts      # Step 1
│   ├── constraint-selection.component.ts  # Step 2 (conditional)
│   ├── team-selection.component.ts        # Step 3
│   ├── player-forms.component.ts          # Step 4 (uses metadata)
│   ├── review.component.ts                # Step 5
│   ├── payment.component.ts               # Step 6
│   └── confirmation.component.ts          # Step 7
└── services/
    └── registration-wizard.service.ts     # Shared state
```

**Navigation Rules**:
- Previous button enabled on all steps except first
- Next button enabled when current step is valid
- Can go back to edit previous steps
- Data persists when navigating back/forward
- Confirmation step is final (no back button)

---

### Phase 5: Family/Multi-Player Data Structure

#### Registration Submission DTOs

**Family Registration Request**:

```csharp
public class FamilyRegistrationRequest
{
    public Guid JobId { get; set; }
    public Guid ParentUserId { get; set; }
    public string RegistrationType { get; set; } // "PP" or "CAC"
    public List<PlayerRegistration> Players { get; set; }
}

public class PlayerRegistration
{
    public Guid? PlayerId { get; set; } // Null for new players
    public Dictionary<string, object> BaseFields { get; set; } // firstName, DOB, etc.
    public Dictionary<string, object> ProfileFields { get; set; } // jerseySize, etc.
    public List<TeamRegistration> Teams { get; set; } // For CAC: multiple teams
}

public class TeamRegistration
{
    public Guid TeamId { get; set; }
    public string PaymentOption { get; set; } // "PIF" or "Deposit"
    public decimal Amount { get; set; }
}
```

**Example Request** (PP - Single Team):
```json
{
  "jobId": "guid",
  "parentUserId": "guid",
  "registrationType": "PP",
  "players": [
    {
      "playerId": "guid-1",
      "baseFields": {
        "firstName": "John",
        "lastName": "Doe",
        "dateOfBirth": "2012-05-15"
      },
      "profileFields": {
        "jerseySize": "youth m",
        "uslaxNumber": "123456",
        "bWaiverSigned1": true
      },
      "teams": [
        {
          "teamId": "team-guid",
          "paymentOption": "Deposit",
          "amount": 200.00
        }
      ]
    }
  ]
}
```

**Example Request** (CAC - Multiple Camps):
```json
{
  "jobId": "guid",
  "parentUserId": "guid",
  "registrationType": "CAC",
  "players": [
    {
      "playerId": "guid-1",
      "baseFields": { ... },
      "profileFields": { ... },
      "teams": [
        {
          "teamId": "camp1-guid",
          "paymentOption": "PIF",
          "amount": 150.00
        },
        {
          "teamId": "camp2-guid",
          "paymentOption": "PIF",
          "amount": 175.00
        }
      ]
    },
    {
      "playerId": "guid-2",
      "baseFields": { ... },
      "profileFields": { ... },
      "teams": [
        {
          "teamId": "camp1-guid",
          "paymentOption": "PIF",
          "amount": 150.00
        }
      ]
    }
  ]
}
```

#### Backend Processing: Metadata-Driven Mapping

**Challenge**: How does the server map dynamic `Dictionary<string, object>` to strongly-typed `Registration` entity?

**Solution**: Use metadata's `dbColumn` property to map field names to database columns.

**RegistrationMapper Service**:

```csharp
public class RegistrationMapper
{
    private readonly IProfileMetadataService _metadataService;
    
    public async Task<Registration> MapToRegistration(
        Guid jobId, 
        Dictionary<string, object> formData)
    {
        // Load metadata for this job (EF Core likely caches the query)
        var metadata = await _metadataService.GetProfileMetadataAsync(jobId);
        
        var registration = new Registration 
        { 
            JobId = jobId,
            // ... other fixed fields set by caller
        };
        
        // Map each dynamic field using metadata
        foreach (var field in metadata.Fields)
        {
            if (formData.TryGetValue(field.Name, out var value))
            {
                SetProperty(registration, field.DbColumn, value);
            }
        }
        
        return registration;
    }
    
    private void SetProperty(Registration registration, string propertyName, object value)
    {
        var property = typeof(Registration).GetProperty(
            propertyName, 
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
        );
        
        if (property != null && property.CanWrite)
        {
            var convertedValue = ConvertValue(value, property.PropertyType);
            property.SetValue(registration, convertedValue);
        }
    }
    
    private object ConvertValue(object value, Type targetType)
    {
        if (value == null) return null;
        
        // Handle JSON deserialization type conversions
        if (targetType == typeof(int) || targetType == typeof(int?))
            return Convert.ToInt32(value);
        
        if (targetType == typeof(decimal) || targetType == typeof(decimal?))
            return Convert.ToDecimal(value);
        
        if (targetType == typeof(bool) || targetType == typeof(bool?))
            return Convert.ToBoolean(value);
        
        if (targetType == typeof(DateTime) || targetType == typeof(DateTime?))
            return DateTime.Parse(value.ToString());
        
        // String or already correct type
        return value;
    }
}
```

**Registration Controller Usage**:

```csharp
[HttpPost("submit")]
public async Task<IActionResult> SubmitRegistration(
    [FromBody] FamilyRegistrationRequest request)
{
    foreach (var player in request.Players)
    {
        // Map dynamic fields to Registration entity
        var registration = await _mapper.MapToRegistration(
            request.JobId, 
            player.ProfileFields
        );
        
        // Set fixed fields
        registration.UserId = player.PlayerId.Value;
        registration.Family_UserId = request.ParentUserId;
        registration.TeamId = player.Teams.First().TeamId;
        
        // Save to database
        await _registrationRepository.AddAsync(registration);
    }
    
    await _unitOfWork.SaveChangesAsync();
    
    return Ok(new { success = true });
}
```

**Why No Caching?**

For the primary use case (individual users submitting registrations one at a time):
- Performance difference is negligible: ~10-50ms per request
- Metadata queries are likely cached by EF Core anyway
- Simpler code: No cache invalidation complexity
- Always uses fresh metadata if admin updates it

**Note**: If you later build bulk import features or high-traffic APIs, consider adding `IMemoryCache` with sliding expiration keyed by `JobId`.

---

## Migration Path

### Phase 1: Immediate (Keep Existing C# Classes)

**Approach**: Enhance existing TSIC-Unify-Models classes with custom attributes

**Steps**:
1. Create custom attributes: `[FieldMetadata]`, `[AdminOnly]`, `[ExternalValidation]`
2. Annotate existing PP/CAC view model classes
3. Build `ProfileMetadataService` using reflection
4. Create `/api/registration/profile/{jobPath}` endpoint
5. Test metadata generation from annotated classes
6. Build Angular dynamic form component proof-of-concept
7. Validate single profile (e.g., PP47) end-to-end

**Timeline**: 2-3 weeks

**Advantages**:
- No database schema changes
- Leverage existing profile definitions
- Incremental migration (one profile at a time)

**Disadvantages**:
- Still requires code deployment to add/modify profiles
- Reflection overhead (can be mitigated with caching)

### Phase 2: Future Enhancement (Database-Driven Profiles)

**Approach**: Move profile definitions to database tables

**Schema Design**:

```sql
CREATE TABLE RegistrationProfiles (
    ProfileId UNIQUEIDENTIFIER PRIMARY KEY,
    ProfileName NVARCHAR(50) NOT NULL, -- PP47, CAC12, etc.
    RegistrationType NVARCHAR(10) NOT NULL, -- PP or CAC
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedDate DATETIME NOT NULL,
    ModifiedDate DATETIME NOT NULL
);

CREATE TABLE ProfileFields (
    FieldId UNIQUEIDENTIFIER PRIMARY KEY,
    ProfileId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES RegistrationProfiles(ProfileId),
    FieldName NVARCHAR(100) NOT NULL,
    DisplayName NVARCHAR(200) NOT NULL,
    InputType NVARCHAR(50) NOT NULL, -- text, select, checkbox, etc.
    DataSource NVARCHAR(100) NULL, -- References Job.JsonOptions key
    IsRequired BIT NOT NULL DEFAULT 0,
    IsAdminOnly BIT NOT NULL DEFAULT 0,
    DisplayOrder INT NOT NULL,
    ValidationJson NVARCHAR(MAX) NULL -- JSON with validation rules
);

CREATE TABLE BaseFields (
    FieldId UNIQUEIDENTIFIER PRIMARY KEY,
    FieldName NVARCHAR(100) NOT NULL,
    DisplayName NVARCHAR(200) NOT NULL,
    InputType NVARCHAR(50) NOT NULL,
    IsRequired BIT NOT NULL DEFAULT 0,
    DisplayOrder INT NOT NULL,
    ValidationJson NVARCHAR(MAX) NULL
);
```

**Migration Strategy**:
1. Seed database with existing PP/CAC class definitions
2. Update `ProfileMetadataService` to query database instead of reflection
3. Build admin UI for profile management (CRUD operations)
4. Migrate one profile at a time, test thoroughly
5. Keep C# classes as fallback for complex validation logic

**Timeline**: 4-6 weeks (after Phase 1 complete)

**Advantages**:
- No code deployment for profile changes
- Admin UI empowers business users
- Easier to version and audit profile changes

**Disadvantages**:
- More complex initial setup
- Need robust validation to prevent bad data
- Migration effort from C# classes to database

---

## Key Decisions Required

### 1. Custom Attributes Strategy

**Question**: Should we create `[FieldMetadata]`, `[AdminOnly]`, and `[ExternalValidation]` attributes to annotate existing PP/CAC classes?

**Option A**: Yes, annotate existing classes (Recommended for Phase 1)
- Pros: Minimal disruption, leverage existing code, faster implementation
- Cons: Still coupled to C# classes, requires code deployment

**Option B**: Skip attributes, define metadata in separate JSON files
- Pros: Decoupled from C# classes, easier to modify without compilation
- Cons: Duplicate maintenance (class + JSON), harder to keep in sync

**Option C**: Go directly to database-driven (Phase 2)
- Pros: Ultimate flexibility, admin UI
- Cons: Longer timeline, higher complexity, more risky

**Recommendation**: Start with Option A, migrate to database-driven in Phase 2

---

### 2. Profile Location

**Question**: Keep profiles in TSIC-Unify-Models repo or migrate to TSIC-Core-Angular solution?

**Option A**: Keep in TSIC-Unify-Models, reference as NuGet package
- Pros: Maintains separation of concerns, existing organization
- Cons: NuGet package deployment overhead, versioning complexity

**Option B**: Copy to TSIC-Core-Angular solution
- Pros: Single solution, simpler builds, easier development
- Cons: Code duplication if TSIC-Unify still uses them

**Option C**: Migrate gradually (new profiles in Core-Angular, old in Unify)
- Pros: Best of both worlds during transition
- Cons: Temporary complexity with dual locations

**Recommendation**: Clarify if TSIC-Unify solution is still actively used. If yes → Option A. If no → Option B.

---

### 3. Validation Strategy

**Question**: Use DataAnnotations + custom attributes, or define new JSON schema for validation rules?

**Option A**: Enhance DataAnnotations with custom attributes
- Pros: Familiar .NET pattern, compile-time checking, IntelliSense support
- Cons: Tied to C# language features

**Option B**: Pure JSON validation schema (like JSON Schema spec)
- Pros: Language-agnostic, Angular can use same schema, portable
- Cons: No compile-time checking, custom validation engine needed

**Option C**: Hybrid (DataAnnotations for C# validation, generate JSON for API)
- Pros: Best of both (backend type safety + frontend flexibility)
- Cons: Mapping layer complexity

**Recommendation**: Option C - use DataAnnotations in C# classes, serialize to JSON for API consumption

---

### 4. Multi-Step UX Flow

**Question**: Should family selection happen first, or integrate with existing user authentication?

**Assumptions to Clarify**:
- Are users authenticated before starting registration?
- Do authenticated users already have family/player data in system?
- Can anonymous users register (create account during registration)?
- Should we support "save draft" for partial completion?

**Proposed Flow** (authenticated users):
1. User lands on job page, clicks "Start Registration"
2. System checks authentication → already logged in
3. Load family/player data from user account
4. Show player selection (checkboxes for which children to register)
5. Proceed with constraint selection → team selection → forms

**Proposed Flow** (anonymous users):
1. User lands on job page, clicks "Start Registration"
2. System detects no authentication
3. Redirect to login/register page
4. After successful auth, return to registration flow
5. Proceed as authenticated user flow

**Alternative**: Allow anonymous registration, create account at end
- More friction-free initial experience
- Risk of incomplete registrations
- Harder to manage family data without account

**Recommendation**: Require authentication before registration starts (cleaner data model)

---

## Implementation Roadmap

### Sprint 1: Foundation (Week 1-2)

**Backend**:
- [ ] Create custom attributes (`FieldMetadataAttribute`, `AdminOnlyAttribute`, `ExternalValidationAttribute`)
- [ ] Annotate 1-2 sample PP classes (PP47, PP50)
- [ ] Build `ProfileMetadataService` with reflection logic
- [ ] Create `GET /api/registration/profile/{jobPath}` endpoint
- [ ] Add `CoreRegformPlayer` parsing logic
- [ ] Test metadata generation and API response

**Frontend**:
- [ ] Create `RegistrationService` to fetch profile metadata
- [ ] Build basic `DynamicFormComponent` proof-of-concept
- [ ] Test rendering text, select, checkbox fields from metadata
- [ ] Implement basic validation from metadata

**Deliverable**: Working proof-of-concept with one profile (PP47) rendering dynamically in Angular

---

### Sprint 2: Team Selection (Week 3-4)

**Backend**:
- [ ] Build `TeamFilterService` for constraint-based filtering
- [ ] Create `GET /api/registration/teams` endpoint
- [ ] Implement filtering logic (BYGRADYEAR, BYAGEGROUP, etc.)
- [ ] Test with sample teams in database

**Frontend**:
- [ ] Add constraint selection step to wizard
- [ ] Integrate team filtering API
- [ ] Display team cards with descriptions and fees
- [ ] Handle team selection (single vs multiple)

**Deliverable**: Complete flow from constraint selection → filtered teams → team selection

---

### Sprint 3: USLax Validation (Week 5)

**Backend**:
- [ ] Build `USLaxValidationService`
- [ ] Integrate USA Lacrosse API (using your credentials)
- [ ] Create `POST /api/registration/validate-uslax` endpoint
- [ ] Test validation with real USLax numbers

**Frontend**:
- [ ] Create async validator for USLax field
- [ ] Add loading spinner during validation
- [ ] Display validation results (valid/invalid)
- [ ] Block form submission if invalid

**Deliverable**: USLax number validation working end-to-end

---

### Sprint 4: Full Wizard (Week 6-7)

**Frontend**:
- [ ] Build multi-step wizard component
- [ ] Implement progress indicator
- [ ] Add navigation (Previous/Next buttons)
- [ ] Create review & submit step
- [ ] Handle form state persistence across steps

**Backend**:
- [ ] Design registration submission DTOs
- [ ] Create `POST /api/registration/submit` endpoint
- [ ] Implement family registration logic
- [ ] Save to database (Registrations, Players, Teams)

**Deliverable**: Complete registration flow for PP type

---

### Sprint 5: CAC Support (Week 8-9)

**Frontend**:
- [ ] Build camp selection matrix component
- [ ] Handle multiple registrations per player
- [ ] Update wizard steps for CAC flow
- [ ] Test multi-camp registration

**Backend**:
- [ ] Extend submission endpoint for CAC type
- [ ] Handle multiple team assignments per player
- [ ] Calculate total fees correctly

**Deliverable**: CAC registration type fully functional

---

### Sprint 6: Polish & Testing (Week 10)

- [ ] Error handling and user feedback
- [ ] Accessibility improvements (ARIA labels, keyboard navigation)
- [ ] Mobile responsiveness testing
- [ ] Performance optimization (caching, lazy loading)
- [ ] Integration testing across all registration types
- [ ] User acceptance testing with real jobs

**Deliverable**: Production-ready registration system

---

## Open Questions & Next Steps

1. **Profile Location**: Should profiles stay in TSIC-Unify-Models or move to TSIC-Core-Angular?

2. **Authentication Flow**: Confirm user must be authenticated before registration starts?

3. **Payment Integration**: Is payment processing in scope for this phase, or separate?

4. **Admin Interface**: When should we build admin UI for managing profiles (if database-driven)?

5. **Existing Data**: How to handle users with existing registrations from old system?

6. **Testing Strategy**: Unit tests, integration tests, E2E tests - what's the priority?

7. **Deployment**: Blue-green deployment, feature flags, or direct production deployment?

8. **Monitoring**: What metrics/logging should we track for registration flow analytics?

---

## Appendix

### Sample Profile Class (Before Enhancement)

```csharp
public class PP50_Player_ViewModel
{
    [Display(Name = "Registration Option")]
    [Required(ErrorMessage = "you must SELECT A REGISTRATION OPTION")]
    public Guid? TeamId { get; set; }

    [Display(Name = "Jersey Size")]
    [Required(ErrorMessage = "JERSEY SIZE is required")]
    public string JerseySize { get; set; }

    [Display(Name = "Special Requests")]
    public string SpecialRequests { get; set; }
}
```

### Sample Profile Class (After Enhancement)

```csharp
public class PP50_Player_ViewModel
{
    [Display(Name = "Registration Option")]
    [Required(ErrorMessage = "you must SELECT A REGISTRATION OPTION")]
    [FieldMetadata(InputType = "select", DataSource = "Teams_Filtered", Order = 1)]
    public Guid? TeamId { get; set; }

    [Display(Name = "Jersey Size")]
    [Required(ErrorMessage = "JERSEY SIZE is required")]
    [FieldMetadata(InputType = "select", DataSource = "ListSizes_Jersey", Order = 10)]
    public string JerseySize { get; set; }

    [Display(Name = "Special Requests")]
    [FieldMetadata(InputType = "textarea", MaxLength = 500, Order = 20)]
    public string SpecialRequests { get; set; }
}
```

---

**End of Document**

*Ready for review and refinement. Please provide feedback on key decisions and open questions.*
