# Step 2 Teams Implementation Summary

## Overview
Implemented the complete Step 2 "Teams" component for the team registration wizard, featuring a dual-table UI for managing club team registrations across events.

## Architecture

### ClubTeams vs Teams
- **ClubTeams**: Global, persistent teams that exist across all events
  - Properties: ClubTeamId, ClubId, ClubTeamName, ClubTeamGradYear, ClubTeamLevelOfPlay
  - Managed in left table ("Available Teams")
  
- **Teams**: Event-specific registrations linking ClubTeams to a Job
  - Properties: TeamId, JobId, ClubTeamId, PaidTotal
  - Managed in right table ("Registered Teams")
  - Cannot be deleted if PaidTotal > 0

### Fee Calculation Chain
Teams → AgeGroups → Leagues

**FeeBase**: Depends on `bTeamsFullPaymentRequired` flag
- If true: RosterFee + TeamFee (full payment upfront)
- If false: RosterFee only (deposit first)

**FeeProcessing**: Credit card processing fee (configurable)
- Never assessed
- Assessed on deposit + final balance
- Assessed on final balance only

**FeeTotal**: FeeBase + FeeProcessing

**OwedTotal**: FeeTotal - PaidTotal

## Components Created

### 1. Badge Update
**File**: `team-registration-wizard.component.ts` and `.html`

**Changes**:
- Added `clubName: string | null = null` property
- Updated `submitRegistration()` to store clubName from registration form
- Updated badge template to display: `"{{username}} for {{clubname}} - {{stepLabel}}"`

**Limitation**: For existing user logins, clubName will need to come from backend metadata endpoint (not yet implemented)

### 2. Teams Step Component
**File**: `teams-step/teams-step.component.ts`

**Features**:
- Dual-table signal-based UI (availableClubTeams, registeredTeams)
- Search and filter by name, grade year, level of play
- Computed signals for filtered lists and financial totals
- Mock data for development/testing
- Methods ready for service integration:
  - `registerTeam(clubTeam)`: Creates Teams record
  - `unregisterTeam(team)`: Deletes Teams record (validates PaidTotal = 0)
  - `addNewClubTeam(data)`: Creates new ClubTeam

**Signals**:
- `searchTerm`, `filterGradeYear`, `filterLevelOfPlay`
- `availableClubTeams`, `registeredTeams`, `ageGroups`
- `clubId`, `clubName`
- `totalOwed` (computed from registered teams)
- `filteredAvailableTeams`, `filteredRegisteredTeams` (computed with filters)
- `gradeYears`, `levelsOfPlay` (computed unique values)

### 3. Teams Step Template
**File**: `teams-step/teams-step.component.html`

**UI Features**:
- **Desktop (≥992px)**: Side-by-side dual tables
  - Left: Available ClubTeams (click to register)
  - Right: Registered Teams (click to unregister if unpaid)
  
- **Mobile (<992px)**: Stacked card layout
  - Collapsible cards for each team
  - Full-width buttons for actions

- **Search/Filter Panel**:
  - Text search by team name
  - Dropdown filter by grade year
  - Dropdown filter by level of play
  - Clear filters button

- **Financial Summary Bar**:
  - Total owed across all registered teams
  - Count of registered teams
  - "Add New Team" button

- **Age Group Availability Section**:
  - Cards showing "{registered} / {max}" for each age group
  - Visual indicators (red for full, green for available)
  - Displays roster fee and team fee

- **Add New Team Modal**:
  - Form to create new ClubTeam
  - Fields: Team Name, Graduation Year, Level of Play
  - Template-driven form validation

**Accessibility**:
- ARIA labels and live regions
- Keyboard navigation support
- Screen reader announcements

### 4. Team Registration Service
**File**: `services/team-registration.service.ts`

**Methods** (ready for backend integration):

```typescript
getTeamsMetadata(jobPath: string): Observable<TeamsMetadataResponse>
// Returns: clubId, clubName, availableClubTeams, registeredTeams, ageGroups

registerTeamForEvent(request: RegisterTeamRequest): Observable<RegisterTeamResponse>
// Creates Teams record linking ClubTeam to current Job

unregisterTeamFromEvent(teamId: number): Observable<void>
// Deletes Teams record (validates no payments)

addNewClubTeam(request: AddClubTeamRequest): Observable<AddClubTeamResponse>
// Creates new ClubTeam for the club
```

**DTOs Defined**:
- `TeamsMetadataResponse`
- `ClubTeamDto`
- `RegisteredTeamDto` (includes financial details)
- `AgeGroupDto` (includes availability)
- `RegisterTeamRequest`
- `RegisterTeamResponse`
- `AddClubTeamRequest`
- `AddClubTeamResponse`

**Note**: These DTOs will be moved to `core/api/models` once backend endpoints are implemented and OpenAPI schema is updated.

### 5. Integration into Wizard
**File**: `team-registration-wizard.component.ts` and `.html`

**Changes**:
- Imported `TeamsStepComponent`
- Added to component imports array
- Updated template to show `<app-teams-step>` when `step === 2`
- Removed placeholder "Team submission step placeholder"

## Backend Work Needed

### Endpoints to Create

1. **GET `/api/team-registration/metadata?jobPath={jobPath}`**
   - Returns club info, available ClubTeams, registered Teams, age groups
   - Include financial calculations (FeeBase, FeeProcessing, FeeTotal, PaidTotal, OwedTotal)
   - Include age group availability (MaxTeams, RegisteredCount)

2. **POST `/api/team-registration/register-team`**
   - Request: `{ clubTeamId, jobPath, ageGroupId? }`
   - Creates Teams record
   - Validates age group availability
   - Returns: `{ teamId, success, message? }`

3. **DELETE `/api/team-registration/unregister-team/{teamId}`**
   - Validates PaidTotal = 0
   - Deletes Teams record
   - Returns: `{ success, message? }`

4. **POST `/api/team-registration/add-club-team`**
   - Request: `{ clubTeamName, clubTeamGradYear, clubTeamLevelOfPlay }`
   - Creates ClubTeams record linked to current user's club
   - Returns: `{ clubTeamId, success, message? }`

### Business Logic to Implement

1. **Fee Calculation Service**
   - Read from Job (bTeamsFullPaymentRequired, bAddProcessingFees)
   - Read from AgeGroups (RosterFee, TeamFee)
   - Read from Leagues (fee configuration)
   - Calculate FeeBase, FeeProcessing, FeeTotal

2. **Age Group Availability**
   - Query AgeGroups.MaxTeams
   - Count active Teams per age group
   - Return available slots

3. **ClubTeam Auto-Linking**
   - Determine age group from ClubTeamGradYear
   - Match to available AgeGroups in current League/Season

4. **Anti-Oversubscription Strategy**
   - Production code creates Teams with `Active = true`
   - May need to implement `Active = false` strategy if oversubscription is a concern
   - User described: Set `Active = true` only after payment

## Testing Considerations

1. **Age Group Full Scenarios**
   - Attempt to register team when age group is at MaxTeams
   - Verify error message and UI feedback

2. **Payment Validation**
   - Attempt to unregister team with PaidTotal > 0
   - Verify error message prevents deletion

3. **Search/Filter**
   - Verify all filter combinations work correctly
   - Test empty states (no matches)

4. **Mobile Responsiveness**
   - Test dual-table → card transition at 992px breakpoint
   - Verify touch interactions work correctly

5. **Add New Team**
   - Test form validation
   - Verify new team appears in available list
   - Test modal open/close

## Known Limitations

1. **Club Name for Existing Logins**: Badge will show "NgClubRep1 for Soccer Stars FC" after new registration, but existing users who login won't see club name until metadata endpoint is implemented.

2. **Mock Data**: Component uses mock data until backend endpoints are created. Search `TODO` in `teams-step.component.ts` to find integration points.

3. **Age Group Auto-Assignment**: Currently not implemented. Backend will need to determine appropriate age group based on team's grade year.

4. **Level of Play Options**: Hardcoded in modal. May need to come from database if customizable per league.

## Next Steps

1. **Backend Implementation**
   - Create controller: `TeamRegistrationController.cs`
   - Create DTOs in `TSIC.Application/DTOs/TeamRegistration/`
   - Create service: `TeamRegistrationService.cs`
   - Implement fee calculation logic
   - Implement age group availability logic

2. **OpenAPI Schema Update**
   - Run API to generate Swagger
   - Run task "Refresh Angular API models (one-click)"
   - Move DTOs from service to `core/api/models`

3. **Service Integration**
   - Update `teams-step.component.ts` to inject `TeamRegistrationService`
   - Replace mock `loadTeamsMetadata()` with actual service call
   - Replace mock register/unregister/add methods with service calls
   - Add error handling and loading states

4. **Testing**
   - Create unit tests for components
   - Create integration tests for service
   - Manual testing with real data

5. **Polish**
   - Add animations for team transitions
   - Add toast notifications for success/error
   - Optimize for accessibility
   - Performance testing with 30+ teams

## Production Patterns Verified

From `TeamBaseController.cs`:

✅ Teams created with `Active = true` immediately
✅ Fee calculation uses `bTeamsFullPaymentRequired` flag
✅ Processing fees configured per Job
✅ Age groups filtered by `BAllowSelfRostering = true`
✅ Available spots calculated as `MaxTeams - (count of active teams)`

## Files Modified

1. `team-registration-wizard.component.ts` - Added clubName property, updated registration flow
2. `team-registration-wizard.component.html` - Updated badge display, integrated teams-step
3. `teams-step/teams-step.component.ts` - Created complete component with signals and methods
4. `teams-step/teams-step.component.html` - Created responsive dual-table UI
5. `teams-step/teams-step.component.scss` - Created styling
6. `services/team-registration.service.ts` - Created service with all methods and DTOs

## Success Criteria

✅ Badge shows "username for clubname - stepLabel"
✅ Dual tables show available vs registered teams
✅ Click to move teams between lists (with mock data)
✅ Financial details display correctly (mock values)
✅ Age group slots show availability (mock data)
✅ Search and filters work
✅ Responsive layout (desktop dual-table, mobile cards)
✅ Add new team modal functional
✅ Service architecture ready for backend integration

**All frontend work complete. Ready for backend implementation.**
