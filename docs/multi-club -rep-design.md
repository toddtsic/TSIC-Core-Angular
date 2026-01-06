GitHub Copilot: # Multi-Club Rep Management - Complete Design Document

**Date:** January 4, 2026  
**Context:** Design session to support club reps managing multiple clubs

---

## Problem Statement

### Current State Issues
1. **ClubReps table seeded from historical registrations** → Many clubs have 2-4 reps (this is FINE)
2. **Some individuals manage multiple DIFFERENT clubs** → Current system can't support this well
3. **Mega clubs (parent organizations with multiple locations)** → Want ONE rep account to manage multiple club branches
4. **Fuzzy logic problems:**
   - Uses state in matching (wrong for mega clubs)
   - Blocks registration when similar club found (wrong - should allow multiple reps)
   - Should prevent duplicate clubs but NOT block multiple reps per club

### Key Examples
- "3 Point Lacrosse - VA" and "3 Point Lacrosse - NC" are DIFFERENT clubs (different states)
- One person can legitimately be rep for both
- They should login once, pick which club, and operate in that context

---

## Data Model (KEEP AS IS)

### ClubReps Table Structure
```
ClubRepId (PK) | ClubRepUserId | ClubId (FK) | ClubName | LebUserId (audit) | Address fields...
1              | johndoe123    | 5           | 3Pt - VA | system            | ...
2              | johndoe123    | 8           | 3Pt - NC | system            | ...
3              | sarah456      | 5           | 3Pt - VA | system            | ...
```

**KEY INSIGHT:** 
- **ClubRepUserId** = User's username/account identifier
- **ClubId** = Which club they represent (KEEP THIS COLUMN - IT'S ESSENTIAL)
- **One ClubRepUserId → Multiple ClubRep records → Multiple Clubs**
- **One ClubId → Multiple ClubRep records → Multiple reps per club**
- **NO junction table needed** - ClubReps table IS the association table

---

## Fuzzy Logic Changes

### Current Problems (ClubService.cs)
**Line 80:** `var similarClubs = await SearchClubsAsync(request.ClubName, request.State);`
- Passes state parameter (wrong)

**Lines 83-94:** Blocks registration when 90%+ match found
- Returns `Success = false`
- Message: "similar club already exists"
- This is WRONG behavior

**Line 166:** `var clubs = await _clubRepo.GetSearchCandidatesAsync(state);`
- Filters by state before fuzzy matching
- Means "3 Point - VA" won't find "3 Point - VA" typos in other states

### Required Changes

#### 1. Remove State Filtering (ClubService.cs line ~166)
**FROM:**
```csharp
var clubs = await _clubRepo.GetSearchCandidatesAsync(state);
```

**TO:**
```csharp
// Search ALL states to find duplicate club names
var clubs = await _clubRepo.GetSearchCandidatesAsync();
```

#### 2. Remove Blocking Behavior (ClubService.cs lines ~79-94)
**FROM:**
```csharp
// Check for similar existing clubs (fuzzy match)
var similarClubs = await SearchClubsAsync(request.ClubName, request.State);

// If exact match found (90%+ similarity), warn user
var exactMatch = similarClubs.FirstOrDefault(c => c.MatchScore >= 90);
if (exactMatch != null)
{
    return new ClubRepRegistrationResponse
    {
        Success = false,
        ClubId = null,
        UserId = null,
        Message = $"A club with a very similar name to '{exactMatch.ClubName}' already exists.",
        SimilarClubs = similarClubs
    };
}
```

**TO:**
```csharp
// Check for similar existing clubs (fuzzy match) - search ALL states to find duplicates
var similarClubs = await SearchClubsAsync(request.ClubName, null);

// Return similar clubs for user to review - DON'T block registration
// Frontend will present matches and ask: "Is this your club?"
// User can choose to: 
//   A) Attach to existing club (create ClubRep record with existing ClubId)
//   B) Create new club (proceed with new Clubs + ClubRep records)
```

#### 3. Remove State Warning in Registration Form
- Currently warns if registrant's state differs from club state
- **Remove this warning** - it's valid for mega clubs to operate in multiple states

### Fuzzy Logic Goals
✅ **DO:** Find similar club names across ALL states (prevent "3 Point Lacrosse" and "3Point Lax" duplicates)  
✅ **DO:** Return matches to frontend for user decision  
✅ **DON'T:** Block registration  
✅ **DON'T:** Filter by state  
✅ **DON'T:** Prevent multiple reps per club  

---

## Registration Flow (Option 4 - Progressive)

### Why Option 4?
- **Optimizes for common case:** Most reps manage ONE club
- **Lower barrier:** Simple initial registration
- **Natural moment:** Ask about additional clubs after success
- **Not blocking:** Can add more clubs later from dashboard

### User Experience

#### Initial Registration (Existing Flow)
```
1. Fill out club rep registration form
2. Enter club name: "3 Point Lacrosse - VA"
3. Fuzzy search returns similar clubs (if any)
4. Frontend shows matches: "Found similar clubs - is this yours?"
   → [Yes, use existing club] [No, create new]
5. Creates:
   - AspNetUser (if new)
   - Clubs record (if new club chosen)
   - ClubReps record (ClubRepUserId + ClubId)
```

#### After Registration Success
```
✓ Registration Complete for 3 Point Lacrosse - VA

┌─────────────────────────────────────────────────────┐
│ Do you represent additional clubs?                  │
│                                                      │
│ [Manage Additional Clubs →]  [No, I'm done]        │
└─────────────────────────────────────────────────────┘
```

#### Add Additional Clubs Flow (NEW)
```
Add Another Club
─────────────────
Contact info (pre-filled from first registration)
✓ Same address  ☐ Different address

Club Name: [________________]
State: [__]

[Search for similar clubs...]

Similar clubs found:
☐ 3 Point Lacrosse - NC (5 teams)
☐ None of these - create new

[Cancel]  [Add This Club]

→ Creates new ClubReps record with:
   - Same ClubRepUserId
   - Different ClubId (existing or new)
```

---

## Login & Club Selection Flow

### Authentication Query
```csharp
// Get all clubs for this user
var clubReps = await _context.ClubReps
    .Where(cr => cr.ClubRepUserId == username)
    .Include(cr => cr.Club)
    .ToListAsync();
```

### Single Club (existing behavior)
- 1 ClubRep record found → proceed directly to dashboard
- Store ClubRepId and ClubId in session/JWT

### Multiple Clubs (NEW behavior)
```
Welcome back, John!

Select which club you're managing today:

┌─────────────────────────────────────┐
│ ○ 3 Point Lacrosse - VA            │
│   (12 teams registered)             │
│                                     │
│ ○ 3 Point Lacrosse - NC            │
│   (8 teams registered)              │
└─────────────────────────────────────┘

[Continue →]
```

### Session Management
- Store in JWT/session:
  - `ClubRepId` (which ClubRep record they're using)
  - `ClubId` (which club they're operating as)
  - `ClubRepUserId` (their username)
- All operations scoped to selected ClubId
- "Switch Club" button in header → logout/re-authenticate

---

## Implementation Checklist

### Backend Changes

#### 1. ClubService.cs
- [ ] Line ~80: Change `SearchClubsAsync(request.ClubName, request.State)` to `SearchClubsAsync(request.ClubName, null)`
- [ ] Lines ~79-94: Remove blocking `if (exactMatch != null)` code block
- [ ] Add comment explaining fuzzy match returns clubs for frontend decision
- [ ] Line ~166: Change `GetSearchCandidatesAsync(state)` to `GetSearchCandidatesAsync()`

#### 2. New Endpoint: Add Additional Club
```csharp
[HttpPost("club-rep/add-club")]
[Authorize(Policy = "ClubRepOnly")] // Or similar
public async Task<IActionResult> AddAdditionalClub([FromBody] AddClubRequest request)
{
    // Get current user's ClubRepUserId from JWT
    var username = User.Identity.Name;
    
    // Fuzzy search for similar clubs
    var similarClubs = await _clubService.SearchClubsAsync(request.ClubName, null);
    
    // If user confirmed they want existing club
    if (request.UseExistingClubId.HasValue)
    {
        // Create ClubRep record with existing ClubId
        var clubRep = new ClubReps 
        {
            ClubRepUserId = username,
            ClubId = request.UseExistingClubId.Value,
            // Copy other fields as needed
        };
        await _clubRepRepo.AddAsync(clubRep);
        return Ok(new { clubRepId = clubRep.ClubRepId });
    }
    
    // Otherwise create new club + club rep record
    // (similar to existing registration logic)
}
```

#### 3. Auth/Login Changes
- [ ] After authentication, query all ClubReps for user
- [ ] If count > 1, return club list instead of proceeding
- [ ] Frontend shows club picker
- [ ] Store selected ClubRepId + ClubId in JWT/session

#### 4. Remove State Warnings
- [ ] Find and remove any validation that warns about registrant state != club state

### Frontend Changes

#### 1. Registration Success Page
- [ ] Add "Manage Additional Clubs" section after success
- [ ] Button/link to add-club flow
- [ ] "No thanks, I'm done" option

#### 2. Add Club Flow (New Component)
```typescript
// add-club.component.ts
- Pre-fill contact info from existing user
- Club name input with fuzzy search
- Display similar clubs with "Is this your club?" UI
- Checkbox list of matching clubs OR "Create new"
- Submit creates new ClubReps record
```

#### 3. Login Club Picker (New Component)
```typescript
// club-picker.component.ts  
- Triggered when auth service detects multiple ClubRep records
- Radio button list of clubs
- Show club name + team count
- Store selected ClubRepId in auth state
- Redirect to dashboard
```

#### 4. Header Component
- [ ] Add "Switch Club" button (visible when user has multiple clubs)
- [ ] Shows current club name
- [ ] Clicking triggers re-authentication with club picker

#### 5. Registration Form
- [ ] Remove state validation warning
- [ ] Update similar clubs UI to not treat it as error
- [ ] Add "Is this your club?" decision flow

---

## Testing Scenarios

### Scenario 1: Single Club Rep (existing behavior)
1. Register as rep for "Charlotte Fury - NC"
2. Login → goes directly to dashboard
3. ✅ No changes to existing simple flow

### Scenario 2: Multi-Club Rep (new behavior)
1. Register as rep for "3 Point Lacrosse - VA"
2. Success page shows "Add additional clubs"
3. Click to add "3 Point Lacrosse - NC"
4. System finds similar club (different state) - doesn't block
5. Choose "Create new club"
6. Logout
7. Login → Shows club picker with both VA and NC
8. Select VA → Dashboard shows VA context
9. ✅ Can manage both clubs with one account

### Scenario 3: Multiple Reps Same Club (existing behavior)
1. John registers as rep for "Charlotte Fury - NC"
2. Sarah registers as rep for "Charlotte Fury - NC"
3. System finds 90%+ match
4. Sarah confirms "Yes, this is my club"
5. Creates ClubReps record with existing ClubId
6. ✅ Both have access to same club

### Scenario 4: Prevent Duplicate Clubs (improved)
1. John registers "3 Point Lacrosse"
2. Later, Sarah tries "3Point Lax" (typo/variant)
3. System finds 90%+ match (same state)
4. Shows: "Found: 3 Point Lacrosse - is this your club?"
5. If yes → use existing ClubId
6. If no → create new (but should be rare)
7. ✅ Prevents duplicate clubs via fuzzy match

---

## Key Design Decisions Summary

1. **Keep ClubId in ClubReps** - it's the association column
2. **One ClubRepUserId → Many ClubRep records** - that's how multi-club works
3. **Remove state from fuzzy logic** - mega clubs operate across states
4. **Don't block on similar match** - return options to user
5. **Progressive registration** - simple first, add more later
6. **Club picker on login** - when multiple clubs detected
7. **Allow multiple reps per club** - that's the whole point

---

## Files That Need Changes

### Backend
- ClubService.cs (fuzzy logic changes)
- `TSIC.API/Controllers/ClubRepController.cs` (new add-club endpoint)
- `TSIC.API/Services/Auth/AuthService.cs` (multi-club login handling)

### Frontend  
- `src/app/views/club-rep/registration-success/` (add "manage clubs" UI)
- `src/app/views/club-rep/add-club/` (NEW - add additional club flow)
- `src/app/views/auth/club-picker/` (NEW - club selection dialog)
- `src/app/components/header/` (add "switch club" button)
- `src/app/services/auth.service.ts` (multi-club detection)

---