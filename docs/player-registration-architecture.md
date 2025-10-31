# Player Registration Architecture - Modernization Proposal

**Date**: October 31, 2025  
**Status**: Draft for Discussion

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

**Steps**:

1. **Family Selection** (if not already authenticated)
   - Display family members from user account
   - Allow selection of which children to register
   - For CAC: Show camp selection matrix

2. **Constraint Selection** (if applicable)
   - Show dropdown for grad year, age group, etc.
   - Based on `CoreRegformPlayer` part 2
   - Skip if constraint type is empty

3. **Team Selection**
   - Display filtered teams based on constraint
   - Show team details (name, description, fee, deposit option)
   - For PP: Select one team per player
   - For CAC: Already selected via camp matrix

4. **Camp Selection** (CAC only)
   - Grid: Camps (rows) × Players (columns)
   - Checkboxes for which children attend which camps
   - Display camp descriptions, dates, fees

5. **Per-Player Forms**
   - Dynamic form per child
   - Fields from profile metadata
   - Base fields + profile-specific fields
   - Validation from metadata

6. **Review & Submit**
   - Summary of all registrations
   - Total cost calculation
   - Payment option selection (deposit vs PIF)
   - Final submit button

**Progress Indicator**:
```html
<div class="progress mb-4">
  <div class="progress-bar" [style.width]="progressPercent() + '%'">
    Step {{ currentStep() }} of {{ totalSteps() }}
  </div>
</div>
```

**Navigation**:
- Previous/Next buttons
- Disable Next until current step valid
- Allow Previous to go back and edit

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
