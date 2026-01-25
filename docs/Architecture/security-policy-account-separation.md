# Security Policy: Account Privilege Separation

**Last Updated:** December 14, 2025  
**Status:** Active Policy  
**Scope:** All user registrations and authentication

---

## Overview

This security policy establishes strict account separation rules to protect minor personally identifiable information (PII) in the TSIC multi-tenant youth sports platform. The policy prevents unauthorized access to children's data through credential sharing while maintaining usability for legitimate use cases.

---

## Multi-Tenant Architecture

### Tenant Model
- **Tenant Definition:** Each Job (sporting event/season) is a unique tenant
- **Tenant Identification:** `jobId` (primary key) and `jobPath` (unique index)
- **Tenant Isolation:** Strict - users cannot access data across tenants
- **Route-Based Selection:** Angular routes contain job segment (`jobPath`)
- **Server Resolution:** `GetJobIdFromJobPath()` converts route to tenant identifier

### Authentication Flow

#### Stage 1: Initial Authentication
1. User provides username/password credentials
2. Server validates and returns JWT with **user identity only**
3. JWT claims: `{ sub: userId, username: username }`
4. Token expiration: 60 minutes
5. Refresh token: 7 days

#### Stage 2: Job/Role Selection
1. Client uses Stage 1 JWT to request available job/role combinations
2. User presented with available registrations (job + role pairs)
3. User selects specific job/role combination
4. Server returns **new JWT** with full claims:
   - `sub`: userId
   - `username`: username
   - `jobPath`: tenant identifier
   - `role`: selected role
   - `registrationId`: specific registration (links to team assignment)
5. All subsequent API calls use Stage 2 JWT

#### Stage 3: Role Switching (Optional)
- Users can switch job/role combinations **without re-entering credentials**
- `/api/auth/switch-role` endpoint exchanges current JWT for new JWT
- Validates user has access to requested job/role combination
- Returns new JWT with updated claims

### JWT Claims Structure

**Stage 1 JWT (Minimal):**
```json
{
  "sub": "user-id-guid",
  "username": "john_player",
  "jti": "token-id",
  "iat": 1234567890
}
```

**Stage 2 JWT (Full Authorization):**
```json
{
  "sub": "user-id-guid",
  "username": "john_player",
  "email": "john@example.com",
  "jobPath": "summer-baseball-2024",
  "role": "Player",
  "registrationId": "reg-id-guid"
}
```

### Authorization Scope

The `registrationId` claim is the **primary security boundary**:
- 1:1 relationship with `assignedTeamId`
- Server resolves all data access from `registrationId`
- API endpoints filter queries by team derived from `registrationId`
- Team-scoped access: users only see data for their assigned team

---

## Privilege Hierarchy

The platform enforces a six-level privilege hierarchy:

```
Player (Lowest)
  ↓
Staff (Coach/Volunteer)
  ↓
Club Rep (Club Team Organizer)
  ↓
Director (League Administrator)
  ↓
Superdirector (Multi-League Administrator)
  ↓
Superuser (Platform Administrator - Highest)
```

### Privilege Level Characteristics

#### Player
- **Purpose:** View own child's team information
- **Data Access:** Own child's team roster, schedule, announcements (via `registrationId`)
- **PII Visibility:** Contact info for teammates on same team only
- **Credential Sharing:** Safe to share with child (team-scoped access)
- **Registration:** Parent registers child as player

#### Staff (Coach/Volunteer)
- **Purpose:** Manage assigned team
- **Data Access:** Assigned team roster, schedules, attendance, communications
- **PII Visibility:** Full contact information for all players on assigned team
- **Credential Sharing:** **NEVER** - accesses other families' children's data
- **Registration:** Individual registers as coach/volunteer for specific team

#### Club Rep (Club Team Organizer)
- **Purpose:** Register and manage all club teams for an event/tournament
- **Data Access:** All teams in their club for the event, player rosters for those teams
- **PII Visibility:** Contact information for all players who self-roster onto club teams
- **Credential Sharing:** **NEVER** - accesses multiple teams and families' data
- **Registration:** One club rep per club should register all club teams for a specific event
- **Scope:** Broader than Staff (multiple teams) but limited to their club within an event

#### Director
- **Purpose:** League/division administration
- **Data Access:** All teams within assigned league/division
- **PII Visibility:** Contact information for all families in league
- **Credential Sharing:** **NEVER** - administrative access
- **Registration:** Organization appoints league administrators

#### Superdirector
- **Purpose:** Multi-league organization management
- **Data Access:** Multiple leagues/divisions within organization
- **PII Visibility:** Extensive organizational contact data
- **Credential Sharing:** **NEVER** - senior administrative access

#### Superuser
- **Purpose:** Platform administration and support
- **Data Access:** Cross-tenant access for support and maintenance
- **PII Visibility:** All data across all tenants
- **Credential Sharing:** **NEVER** - platform-level access

---

## Account Privilege Separation Policy

### Core Policy Rule

**Each account (username) is permanently locked to ONE privilege level upon first registration.**

Once an account is used for a registration at a specific privilege level, that account **cannot** be used for registrations at any other privilege level (higher or lower).

### Policy Rationale

**Legal and Ethical Requirements:**
1. **Minor PII Protection:** Children under 18 have regulated personal information (COPPA, state privacy laws)
2. **Credential Sharing Prevention:** Family credential sharing is common - must not expose other children's data
3. **Liability Protection:** Clear audit trail of who accessed what data
4. **Parental Control:** Parents consciously control whether accounts are shared
5. **Defensible Security Posture:** Technical prevention vs. reliance on user behavior

**Example Scenarios (Why Policy Exists):**

*Scenario 1 - Coach:*
- Parent coaches Team A (Staff registration with `registrationId: 456`)
- Parent's child plays on Team B (Player registration with `registrationId: 123`)
- Parent shares Player account password with child (acceptable - team-scoped)
- **Without policy:** Child could select Staff role and access Team A roster (other children's PII) ❌
- **With policy:** Staff registration requires separate account - child cannot access ✅

*Scenario 2 - Club Rep:*
- Parent is Club Rep for ABC Soccer Club (manages 8 teams in tournament)
- Parent's child plays on one of those teams (Player registration)
- Parent shares Player account password with child
- **Without policy:** Child could select Club Rep role and access rosters for all 8 teams (hundreds of children's PII) ❌
- **With policy:** Club Rep registration requires separate account - child cannot access ✅

### Multiple Accounts Model

**Allowed:**
- Same person can have multiple usernames in `AspNetUsers`
- Same email address can be associated with multiple accounts
- Each account locked to its privilege level

**Example:**
```
Parent: John Smith (john@email.com)
├─ Account 1: "jsmith_player" → Locked to Player privilege
│  └─ Can register children as Players ✅
│  └─ Cannot register as Staff ❌
└─ Account 2: "jsmith_coach" → Locked to Staff privilege
   └─ Can register as Staff/Coach ✅
   └─ Cannot register as Player ❌
```

### Policy Enforcement

**When Enforced:**
- During registration flow when username/password is requested
- Before creating new registration record

**Validation Logic:**
1. User provides username (new or existing)
2. If username exists in `AspNetUsers`:
   - Determine locked privilege level from existing registrations
   - Compare to current registration privilege level
   - **Block registration if privilege levels don't match**
3. If username is new:
   - Allow registration
   - First registration locks account to that privilege level

**Privilege Level Determination:**
- Not stored directly in `AspNetUsers`
- **Derived** from existing registrations associated with user
- Query user's registrations and determine highest/locked privilege level
- Cached for performance during registration validation

---

## User Experience and Friction Mitigation

### User Friction Points

**Primary Friction:**
- Parent-coach needs separate accounts (different usernames/passwords)
- Confusion about "why can't I use same login?"
- Perceived complexity for volunteer coaches

**Acceptance Criteria:**
- Friction is acceptable when protecting children's PII
- Legal compliance requirements outweigh convenience
- Clear communication reduces support burden

### Mitigation Strategies

#### 1. Clear Communication During Registration

**Player Registration:**
- Prominent info popup at account creation step
- Message: "This Player account is for viewing YOUR CHILD'S TEAM ONLY and can be safely shared with your child. If you plan to coach or volunteer, you'll need a separate Coach account to protect other families' privacy."

**Staff Registration:**
- Warning alert at beginning of wizard
- Message: "IMPORTANT: Coach/Staff accounts access other families' children's information and should NOT be shared. If you have a Player account, you must create a separate username for coaching."

#### 2. Username Convention Suggestions

**Guidance:**
- Recommend naming pattern: `yourname_player` and `yourname_coach`
- Makes accounts easy to remember and distinguish
- Info popup at username field suggests convention

#### 3. Email Linking and Account Discovery

**Implementation:**
- Allow same email for multiple accounts
- "Forgot username?" lookup by email shows all associated usernames
- Login page: "Have multiple accounts? Use the correct username for your role."

#### 4. Visual Differentiation

**UI Indicators:**
- Color-coded account type badges (blue=Player, green=Staff, etc.)
- Role selector shows clear distinction between account types
- Dashboard header displays current account type

#### 5. Registration Flow Auto-Detection

**Smart Messaging:**
- If email exists with Player account during Staff registration:
  - "We see you have a Player account (jsmith_player). For coaching, create a separate Coach username to protect player privacy."
  - Pre-fill contact info from existing account
  - Only require new username/password

#### 6. Documentation and FAQ

**Support Resources:**
- "Why do I need two accounts?" FAQ
- Emphasize child protection and privacy
- Highlight that this is industry best practice
- Step-by-step guide for creating second account

---

## Information Popup Implementation Guide

### Player Registration Wizard

#### Step: Player Information
- **Location:** "Player Name" field
- **Type:** Info icon (ℹ️)
- **Message:** "Enter the name of the child who will be playing. This account can be safely shared with your child to view their team information."

#### Step: Guardian Information
- **Location:** "Primary Guardian" section header
- **Type:** Info icon (ℹ️)
- **Message:** "Guardian contact information is shared only with coaches and league administrators for team communication. It is never shared with other families."

#### Step: Account Creation ⭐ CRITICAL
- **Location 1:** "Create Account" section header
- **Type:** Alert banner (prominent)
- **Message:** "This Player account is for viewing YOUR CHILD'S TEAM ONLY and can be safely shared with your child. If you plan to coach or volunteer, you'll need a separate Coach account to protect other families' privacy."

- **Location 2:** "Username" field
- **Type:** Info icon (ℹ️)
- **Message:** "Choose a username that indicates this is for your player (example: jsmith_player). If you already have a Coach account, you'll need a different username for this Player account."

- **Location 3:** "Password" field
- **Type:** Info icon (ℹ️)
- **Message:** "This password can be shared with your child since Player accounts only show their own team information."

#### Step: Emergency Contact
- **Location:** "Emergency Contact" section header
- **Type:** Info icon (ℹ️)
- **Message:** "Emergency contacts are only accessible to coaches and league staff during practices and games."

#### Step: Review & Submit
- **Location:** Above submit button
- **Type:** Info box (blue background)
- **Message:** "Remember: This Player account shows only your child's team. Safe to share with your child. Need to coach? Create a separate Coach account."

### Club Rep Registration Wizard

#### Step: Personal Information ⭐ CRITICAL
- **Location:** Page header/intro
- **Type:** Danger alert (red background)
- **Message:** "⚠️ IMPORTANT: Club Rep Account Security - Club Rep accounts access ALL teams in your club and player rosters for an entire event. This includes contact information for potentially hundreds of children and families. This account should NEVER be shared. If you have a Player or Coach account, you must create a separate username for Club Rep registration."

#### Step: Account Setup ⭐ CRITICAL
- **Location 1:** "Username" field
- **Type:** Info icon (ℹ️)
- **Message:** "Your Club Rep username must be different from any Player or Coach account username. We recommend: yourname_clubrep. This account should NEVER be shared as it accesses multiple teams and contact information for many families."

- **Location 2:** "Password" field
- **Type:** Info icon (ℹ️)
- **Message:** "Keep this password secure and do NOT share it. Club Rep accounts access private information about multiple teams and many families' children."

#### Step: Role Selection
- **Location:** "Select Role" dropdown
- **Type:** Info icon (ℹ️)
- **Message:** "Your role determines what team information you can access. Coaches see their assigned team's roster and contact information."

#### Step: Review & Submit
- **Location:** Above submit button
- **Type:** Danger box (red background)
- **Message:** "⚠️ This Club Rep account accesses ALL club teams and player rosters for this event. This may include hundreds of children's contact information. Keep your password secure and NEVER share this account."

---

## Technical Implementation Requirements

### Backend Validation Service

**Service:** `IUserPrivilegeLevelService`

**Methods:**
```csharp
// Determine locked privilege level from existing registrations
Task<PrivilegeLevel?> GetLockedPrivilegeLevelAsync(string userId);

// Validate registration is allowed for user
Task<bool> CanRegisterAtPrivilegeLevelAsync(string userId, PrivilegeLevel requestedLevel);

// Get user's existing accounts by email
Task<List<string>> GetUsernamesByEmailAsync(string email);
```

**Implementation:**
- Query all registrations for userId
- Determine privilege level of each registration (Player/Staff/Director/etc.)
- Return first/locked privilege level (all should match if policy enforced)
- Cache results for performance

### Registration Endpoint Validation

**Player Registration (`POST /api/registration/player`):**
```csharp
// Before creating registration
var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
var canRegister = await _privilegeLevelService.CanRegisterAtPrivilegeLevelAsync(
    userId, 
    PrivilegeLevel.Player
);

if (!canRegister)
{
    return BadRequest(new { 
        Error = "This account is locked to a different privilege level. Please create a separate account for Player registrations." 
    });
}
```

**Staff Registration (`POST /api/registration/club-rep`):**
```csharp
// Before creating registration
var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
var canRegister = await _privilegeLevelService.CanRegisterAtPrivilegeLevelAsync(
    userId, 
    PrivilegeLevel.Staff
);

if (!canRegister)
{
    return BadRequest(new { 
        Error = "This account is locked to a different privilege level. Please create a separate account for Coach/Staff registrations." 
    });
}
```

### Frontend Validation

**Account Creation Step:**
- Check if email exists (lookup endpoint)
- If exists, display existing usernames and their privilege levels
- Show guidance for creating compatible username
- Pre-fill contact information from existing account

**Error Handling:**
- Clear error messages when privilege validation fails
- Guidance to create appropriate account type
- Link to support documentation

---

## Audit and Compliance

### Logging Requirements

**Authentication Events:**
- Login attempts (success/failure) with username and timestamp
- Role selection with jobPath, role, and registrationId
- Role switching events

**Data Access Events:**
- API calls with userId, jobPath, role, and accessed resources
- Focus on PII access (roster views, contact information queries)

**Policy Violation Attempts:**
- Failed privilege level validations during registration
- Include userId, attempted privilege level, locked privilege level

### Audit Trail Benefits

1. **Compliance Demonstration:** Prove policy enforcement to regulators
2. **Incident Response:** Investigate unauthorized access claims
3. **Policy Refinement:** Identify friction points and support needs
4. **User Behavior Analysis:** Understand multi-account usage patterns

### Privacy Considerations

**Data Minimization:**
- Store only registrationId in JWT (derive everything else server-side)
- Limit PII in logs (hash email addresses, redact sensitive fields)
- Automatic log retention limits

**Right to Access:**
- Users can request audit logs of their data access
- Parents can request logs of who accessed their child's information

---

## Future Considerations

### Potential Enhancements

1. **Role-Specific MFA:** Require additional verification for Staff+ roles
2. **Session Management:** Active session tracking and forced logout
3. **Suspicious Activity Detection:** Alert on unusual access patterns
4. **Consent Management:** Explicit parent consent for data sharing
5. **Bulk Account Creation:** Streamlined process for parent-coaches

### Policy Review Schedule

- **Annual Review:** Evaluate policy effectiveness and user friction
- **Regulatory Updates:** Adjust for new privacy laws and requirements
- **Incident-Triggered:** Review after any security incident or breach
- **User Feedback:** Incorporate support ticket trends and user complaints

---

## Summary

This account privilege separation policy establishes a security-first approach to protecting minor PII in the TSIC platform while maintaining usability for legitimate multi-role users. The policy:

✅ **Prevents credential sharing exploits** that expose children's data  
✅ **Complies with legal requirements** for minor data protection  
✅ **Provides clear audit trails** for regulatory compliance  
✅ **Balances security with usability** through friction mitigation  
✅ **Supports multi-tenant architecture** with strict isolation  

**The policy is non-negotiable for legal and ethical reasons, with user friction mitigated through clear communication and thoughtful UX design.**

---

**Document Owner:** Development Team  
**Review Cycle:** Annually or as needed  
**Related Documents:**
- [Authorization Policies](authorization-policies.md)
- [Clean Architecture Implementation](clean-architecture-implementation.md)
- [Player Registration Architecture](player-registration-architecture.md)
