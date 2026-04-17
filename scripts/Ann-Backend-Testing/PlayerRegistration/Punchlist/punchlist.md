# Player Registration - Punch List

**Tester:** Ann
**Date Started:** 2026-04-04
**Status:** In Progress

---

## How to Read Severity

| Label | Meaning |
|-------|---------|
| Bug | Something is broken or produces wrong results |
| UX | It works but is confusing, ugly, or hard to use |
| Question | Not sure if this is right -- need to ask Todd |

## How to Read Status

| Label | Meaning |
|-------|---------|
| Open | Not yet looked at |
| Fixed | Todd/Claude fixed it |
| Won't Fix | Intentional behavior, not changing |

---

## Test Areas

Use these as a guide for what to walk through. You don't have to go in order.

- [ ] **Family Account Setup** -- Create and manage family accounts before player registration
- [ ] **Registration Process Review** -- Walk through the player registration flow end to end
- [ ] **Login & Family Check** -- Log in with your family account, verify it finds your players
- [ ] **Player Selection** -- Select which players to register, try adding a new player
- [ ] **Eligibility** -- Pick age group / grad year / club for each player
- [ ] **Team Assignment** -- Assign players to teams, check capacity and waitlist
- [ ] **Player Forms** -- Fill out required fields, check dropdowns and date pickers work
- [ ] **Waivers** -- Read and accept waivers, make sure required ones block you if unchecked
- [ ] **Review Summary** -- Verify all your selections look correct, fees are right
- [ ] **Payment** -- Try a payment, test discount codes, check insurance offer
- [ ] **Confirmation** -- Verify confirmation page shows, email arrives, receipt looks right

---

## Punch List Items

### PL-001: Login button — remove Palette, change to "Login" label, and offer new family account option
- **Area**: Family Account Setup
- **What I did**: Looked at top-right login area as a new parent would
- **What I expected**: A clear "Login" button (not a people icon dropdown), no Palette option visible, and an option to create a new family account for first-time parents
- **What happened**: Shows a people icon with dropdown and Palette option — not intuitive for new parents who don't have an account yet
- **Severity**: UX
- **Status**: Future — header bar chrome, not player registration specific

### PL-002: Customer/job icon at top should navigate to job home screen
- **Area**: Family Account Setup
- **What I did**: Clicked the customer:job icon at the top of the page
- **What I expected**: Navigate to the home screen for that job
- **What happened**: Doesn't bring me to the job home screen
- **Severity**: UX
- **Status**: Future — header bar chrome, not player registration specific

### PL-003: Navigation for new families — bulletins/text need work
- **Area**: Family Account Setup
- **What I did**: Navigated the site as a new family would
- **What I expected**: Clear, helpful bulletins and text guiding new families
- **What happened**: Bulletins and text content need work — more details to follow
- **Severity**: UX
- **Status**: Future — bulletin content/nav, not player registration specific

### PL-004: "Family Account" header should say "Create Family Account" for new registrations
- **Area**: Family Account Setup
- **What I did**: Clicked "New Family Account" from the registration flow
- **What I expected**: Header to say "Create Family Account" to match the action
- **What happened**: Header just says "Family Account" — should be clearer for new parents that they're creating one
- **Severity**: UX
- **Status**: Fixed — credentials step heading changed to "Create Family Account"

### PL-005: Family Account card is a DEAD END — no Previous or Next button
- **Area**: Family Account Setup
- **What I did**: Arrived at the Family Account card after choosing New Family Account
- **What I expected**: A "Next" button to proceed and a "Previous" button to go back (e.g., "I already have an account")
- **What happened**: No way to proceed or go back — complete dead end
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Addressed by Registration Wizards v2 rewrite — WizardShellComponent provides Back/Continue navigation on all steps.

### PL-006: Player Registration card — highlight Family Account more prominently
- **Area**: Registration Process Review
- **What I did**: Looked at the top card in the Player Registration flow
- **What I expected**: Family Account info to be prominent since it's key context for the registration
- **What happened**: Family Account not highlighted enough — consider making it more visible
- **Severity**: UX
- **Status**: Fixed — family-check step now shows a prominent "Let's Register Your Players!" hero with embedded "Family Account Sign In" login and Create/Update CTAs.

### PL-007: "Choose Your Players" screen — add Previous/Next buttons at bottom
- **Area**: Registration Process Review
- **What I did**: Arrived at the "Choose Your Players" screen
- **What I expected**: Previous and Next buttons at the bottom to navigate between wizard screens
- **What happened**: No navigation buttons at the bottom of the screen
- **Severity**: UX
- **Status**: Fixed — WizardShellComponent provides Back/Continue on all steps

### PL-008: "Already registered? Locked in" — can this be removed?
- **Area**: Registration Process Review
- **What I did**: Saw "Already registered? Locked in" message on Choose Your Players screen
- **What I expected**: Cleaner screen without unnecessary messaging
- **What happened**: Not clear if this message is needed — consider removing it
- **Severity**: Question
- **Status**: Fixed — "Already registered? Locked in" removed from hint row

### PL-009: Change "Edit details anytime" to "Edit player details"
- **Area**: Registration Process Review
- **What I did**: Saw "Edit details anytime" link/button
- **What I expected**: Clearer label specifying what details
- **What happened**: Label is vague — should say "Edit player details" to be specific
- **Severity**: UX
- **Status**: Fixed — changed to "Edit player details"

### PL-010: No Next button after selecting two players
- **Area**: Registration Process Review
- **What I did**: Checked two players on the Choose Your Players screen
- **What I expected**: A Next button to proceed to the next step
- **What happened**: No Next button appears after selecting players — can't advance
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Added bottom action bar (Back/Continue) to all wizard steps via WizardShellComponent. Both player and team registration wizards now have navigation at top and bottom of each step.

### PL-011: Bottom Continue button needs visual separation from the card above
- **Area**: Registration Process Review
- **What I did**: Looked at the new Continue button at the bottom of the Choose Your Players screen
- **What I expected**: Clear spacing or divider between the card content and the bottom button
- **What happened**: Continue button sits too close to the card above — needs separation
- **Severity**: UX
- **Status**: Fixed — already addressed

### PL-012: Remove arrow icon from Continue buttons
- **Area**: Registration Process Review
- **What I did**: Noticed arrow icons in both the top and bottom Continue buttons
- **What I expected**: Clean button with just the text "Continue"
- **What happened**: Both Continue buttons have an arrow that isn't needed
- **Severity**: UX
- **Status**: Fixed — bottom action bar now uses Bootstrap chevron icons matching the top bar

### PL-013: Standardize "Next" vs "Continue" wording across all registration processes
- **Area**: Registration Process Review
- **What I did**: Noticed the button says "Continue" — but other screens may say "Next"
- **What I expected**: Consistent wording throughout all registration flows
- **What happened**: Need to confirm whether we're using "Next" or "Continue" everywhere and standardize
- **Severity**: Question
- **Status**: Fixed — "Continue" is the standard. WizardShellComponent defaults to "Continue" across all wizards.

### PL-014: Add Back/Previous button on Choose Your Players screen
- **Area**: Registration Process Review
- **What I did**: Arrived at the Choose Your Players screen
- **What I expected**: A Back or Previous button to return to the prior step
- **What happened**: No way to go back — only a Continue button forward
- **Severity**: UX
- **Status**: Fixed — WizardShellComponent provides Back on all steps

### PL-015: Add trash can icon next to pencil icon to delete players on Choose Your Players card
- **Area**: Registration Process Review
- **What I did**: Looked at player rows on the Choose Your Players card
- **What I expected**: A delete (trash can) icon next to the edit (pencil) icon for each player
- **What happened**: Only a pencil icon is available — no way to delete a player from the list
- **Severity**: UX
- **Status**: Fixed — trash icon already present next to pencil on each player row

### PL-016: Set Player Graduation Year — Back and Continue buttons need stronger hover/selected contrast
- **Area**: Registration Process Review
- **What I did**: Hovered over and clicked the Back and Continue buttons on the Set Player Graduation Year screen
- **What I expected**: Clear visual feedback with strong color contrast on hover and selected states
- **What happened**: Buttons don't change enough visually when hovered or selected — hard to tell they're interactive
- **Severity**: UX
- **Status**: Fixed — already addressed

### PL-017: Assign Teams — remove "Capacity shown in dropdown" text
- **Area**: Registration Process Review
- **What I did**: Arrived at the Assign Teams screen
- **What I expected**: Clean screen without unnecessary instructional text
- **What happened**: Text says "Capacity shown in dropdown" — this is obvious from the dropdown itself and should be removed
- **Severity**: UX
- **Status**: Won't Fix — capacity info is important for parents choosing teams

### PL-018: Assign Teams — use same white background card style as Graduation Year screen
- **Area**: Registration Process Review
- **What I did**: Compared the Assign Teams screen to the Set Player Graduation Year screen
- **What I expected**: Consistent white card background for the team assignment area, matching the grad year selection style
- **What happened**: Assign Teams section doesn't have the same white background treatment — looks inconsistent with the previous screen
- **Severity**: UX
- **Status**: Fixed — team-selection-step now uses the same `card shadow border-0 card-rounded` shell as eligibility-step.

### PL-019: Consider merging Graduation Year and Assign Teams into one screen
- **Area**: Registration Process Review
- **What I did**: Went through Set Player Graduation Year and then Assign Teams as separate steps
- **What I expected**: Possibly a single screen since both are short player setup tasks
- **What happened**: Two separate screens for related info — could these be combined into one step to reduce clicks?
- **Severity**: Question
- **Status**: Won't Fix — Ann chose to keep them as two separate steps.

### PL-020: Choose Your Players — change "Edit Account" to "Edit Family Contact Info"
- **Area**: Registration Process Review
- **What I did**: Saw "Edit Account" link on the Choose Your Players screen
- **What I expected**: Label that clearly describes what you're editing
- **What happened**: "Edit Account" is vague — should say "Edit Family Contact Info" to be specific
- **Severity**: UX
- **Status**: Fixed — button relabeled to "Edit Family Contact Info".

### PL-021: USA Lacrosse Number validation — wrap phone number on one line in failed entry popup
- **Area**: Registration Process Review
- **What I did**: Entered an invalid USA Lacrosse Number and got the validation failure popup
- **What I expected**: Phone number displayed fully on one line
- **What happened**: Phone number wraps awkwardly across two lines — needs to stay on a single line
- **Severity**: UX
- **Status**: Fixed — phone number wrapped in `<span style="white-space:nowrap">` so it stays on one line.

### PL-022: Player form — move Weight next to Height, make Height optional, move Shorts Size next to T-shirt Size
- **Area**: Registration Process Review
- **What I did**: Filled out the player form for The Players Series: Girls Summer Showcase 2026
- **What I expected**: Related fields grouped together — Height/Weight side by side, Shorts Size/T-shirt Size side by side; Height should be optional
- **What happened**: Weight is not next to Height, Shorts Size is not next to T-shirt Size, and Height is required when it shouldn't be
- **Severity**: UX
- **Status**: Fixed — field order is profile-driven. Todd edited the profile and the field moves appropriately. Height-required and adjacency concerns are handled per-profile going forward.

### PL-023: Player Details form — increase font size of Team Selected next to Player Name
- **Area**: Registration Process Review
- **What I did**: Looked at the Player Details form heading
- **What I expected**: Team Selected to be prominent and easy to read next to the Player Name
- **What happened**: Team Selected text is too small — needs a bigger font so it stands out
- **Severity**: UX
- **Status**: Fixed — `.team-pill` bumped from 10px to `--font-size-sm` (14px) and semibold.

### PL-024: Player Details — white data entry fields with tinted surrounding background for consistency
- **Area**: Registration Process Review
- **What I did**: Looked at the Player Details form styling
- **What I expected**: White input fields on a tinted/shaded background, matching the look of other registration screens
- **What happened**: Fields and background don't have enough contrast between them — make input fields white and the surrounding card background tinted for visual consistency across all registration screens
- **Severity**: UX
- **Status**: Fixed — `.field-grid` given a faint primary-tinted background; inputs/selects inside forced to `--neutral-0` (white) for contrast.

### PL-025: Review and Accept Waivers — larger player names in a list with individual checkboxes
- **Area**: Registration Process Review
- **What I did**: Arrived at the Review and Accept Waivers screen
- **What I expected**: Player names displayed prominently in a list with a checkbox next to each name for the parent to actively confirm
- **What happened**: Player names are too small and not listed clearly — consider having the parent check a box next to each player's name to acknowledge the waiver for each child individually
- **Severity**: UX
- **Status**: Won't Fix — current blanket model (one checkbox per waiver applies to all selected players) is correct by design. Acceptance text already states the waiver applies to selected players; forcing a per-player tick doesn't change the consent.

### PL-026: Review and Accept Waivers — make "ALL" capitalized and increase font size of intro text
- **Area**: Registration Process Review
- **What I did**: Read the intro text on the waivers screen ("These waivers apply to all selected players...")
- **What I expected**: Prominent, easy-to-read text with emphasis on "ALL"
- **What happened**: Text is too small and "all" is not capitalized — change to "ALL" (caps) and make the entire intro line larger so parents don't miss it
- **Severity**: UX
- **Status**: Fixed — "all" → "ALL" and intro callout bumped to `--font-size-base`.

### PL-027: "Almost There!" screen — Team selection text needs larger font
- **Area**: Registration Process Review
- **What I did**: Arrived at the "Almost There!" review screen
- **What I expected**: Team selection info displayed prominently
- **What happened**: Team selection text is too small — needs a larger font so it stands out
- **Severity**: UX
- **Status**: Fixed — `.review-team-pill` bumped from 11px to `--font-size-sm` (14px) and semibold.

### PL-028: "Almost There!" screen — change "F" to "Female" (spell out gender)
- **Area**: Registration Process Review
- **What I did**: Saw gender displayed as "F" on the Almost There review screen
- **What I expected**: Full word "Female" (and presumably "Male" instead of "M")
- **What happened**: Gender shows as a single letter abbreviation — should be spelled out
- **Severity**: UX
- **Status**: Fixed — `genderLabel()` helper maps F/M → Female/Male on the review screen.

### PL-029: "Almost There!" screen — "Review your details" notes slightly too small
- **Area**: Registration Process Review
- **What I did**: Read the "Review your details" section on the Almost There screen
- **What I expected**: Text large enough to read comfortably without being oversized
- **What happened**: All the detail notes are a bit too small — bump up the font size slightly (not too much, just enough to improve readability)
- **Severity**: UX
- **Status**: Fixed — `.review-field-value` bumped to `--font-size-base`; `.review-field-label` bumped from 10px to `--font-size-xs`.

### PL-030: "Almost There!" screen — player names as section headers need bolder font weight
- **Area**: Registration Process Review
- **What I did**: Looked at player name headers on the Almost There review screen
- **What I expected**: Player names to stand out clearly as section headers
- **What happened**: Names aren't bold enough — need heavier font weight so they read as headers
- **Severity**: UX
- **Status**: Fixed — `.review-player-name` bumped to `--font-size-base` and bold weight.

### PL-031: "Almost There!" screen — should accounting/fee summary be shown here?
- **Area**: Registration Process Review
- **What I did**: Reviewed the Almost There screen before proceeding to payment
- **What I expected**: Possibly a fee summary or accounting breakdown before the parent commits
- **What happened**: No accounting or fee information shown — should parents see what they owe before continuing?
- **Severity**: Question
- **Status**: Fixed

### PL-032: Complete Payment — what is "Pay in Full" button for before Add Refund Protection?
- **Area**: Registration Process Review
- **What I did**: Arrived at the Complete Payment screen and saw a "Pay in Full" button appearing before the Add Refund Protection option
- **What I expected**: Clear understanding of what "Pay in Full" does at that point in the flow
- **What happened**: Not clear why "Pay in Full" appears before the refund protection option — shouldn't the parent decide on refund protection first?
- **Severity**: Question
- **Status**: Fixed
- **Note**: Removed badge-styled "Pay In Full" (looked like a clickable button). Now renders as plain muted text flush-right on the "Credit Card Information" heading line — clearly a status label, not an action.

### PL-033: Complete Payment — can't change Refund Protection choice after declining
- **Area**: Registration Process Review
- **What I did**: Declined refund protection coverage, then went Back and clicked Continue to return to the payment screen
- **What I expected**: Ability to change my mind and add coverage before paying
- **What happened**: My decline choice is locked in — no way to reset or change the refund protection selection on the payment screen
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: By design — once refund protection is declined, the choice is locked for that session.

### PL-034: Discount Code section needs better visual emphasis and white input field
- **Area**: Registration Process Review
- **What I did**: Looked at the Discount Code section on the payment screen
- **What I expected**: Discount Code title to stand out (e.g., red or highlighted) and the "Enter Code" input field to be white so it's clearly a data entry field
- **What happened**: Title doesn't stand out enough and the input field blends in with the background — needs a highlighted title (maybe red) and a white input field
- **Severity**: UX
- **Status**: Fixed — label styled red/bold/uppercase; input forced to white (`--neutral-0`). Also replaced banned Bootstrap `form-control` with `field-input`.

### PL-035: Confirm Registration Payment + Insurance popup — standardize font size and replace icons with bullets
- **Area**: Registration Process Review
- **What I did**: Opened the Confirm Registration Payment + Insurance popup
- **What I expected**: All text items in the same (larger) font size, with simple bullet points instead of icons
- **What happened**: Mixed font sizes and icons used instead of bullets — needs uniform larger font and plain bullet list
- **Severity**: UX
- **Status**: Fixed — emoji icons (🧾📧💵💳) replaced with plain `•` bullets; all list items + footer normalized to `--font-size-base`.

### PL-036: Most Recent Transaction(s) only shows one payment after paying for two players
- **Area**: Registration Process Review
- **What I did**: Paid for two players in a family account
- **What I expected**: Both payments to appear under Most Recent Transaction(s) on the Family Players Table
- **What happened**: Family Players Table shows both players, but Most Recent Transaction(s) only shows one payment
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Root cause: `!F-ACCOUNTING` token only queried transactions for the first registration in the family. Fixed `BuildAccountingTableHtmlAsync` to query all family registration IDs. Added `GetAccountingTransactionsAsync(List<Guid>)` overload to `ITextSubstitutionRepository`. Same fix applies to on-screen confirmation and email confirmation.

### PL-037: Registration Complete Confirmation page needs cleaner layout with distinct card areas
- **Area**: Registration Process Review
- **What I did**: Completed registration and viewed the confirmation page
- **What I expected**: Crisp, well-organized layout — 3 tables should stand out clearly, organization info in its own card, waiver info in its own card
- **What happened**: Page feels cluttered — tables don't stand out, organization information and waiver content are not separated into their own distinct card areas
- **Severity**: UX
- **Status**: Won't Fix — table styling refactor (dual-mode CSS classes, tsic-grid, warning/waiver/choices blocks) landed on 2026-04-12 (commits `897e17b2`, `67bebfd1`). Tables, waiver/insurance block, and contacts section now read as visually distinct on-screen. Card-wrapping every section would add chrome for marginal gain.

### PL-038: Waiver area on confirmation shows "BY CLICKING NEXT BELOW, I AGREE..." — confuses parents
- **Area**: Registration Process Review
- **What I did**: Completed registration and saw the waiver section on the confirmation page
- **What I expected**: Clear indication that the waiver was already accepted during registration — no action needed
- **What happened**: Text says "BY CLICKING NEXT BELOW, I AGREE WITH THE ABOVE RELEASE OF LIABILITY" which makes parents think they need to do something else. Either remove this text or add a clarifying note outside the waiver card (e.g., "Waiver accepted during registration")
- **Severity**: UX
- **Status**: Fixed — `BuildWaiverHtmlAsync` in `TextSubstitutionService` now strips `BY CLICKING...` sentences (and any empty `<p>` wrappers they leave behind) before rendering. Applies to every job's confirmation and email waiver tokens, no per-job data edit needed.

### PL-039: After finishing registration and logging back in — no menus or info to review/edit
- **Area**: Registration Process Review
- **What I did**: Finished registration (got logged out), logged back in, and selected one of my registration options
- **What I expected**: Menus and information available to review or edit my registration details
- **What happened**: After selecting a registration, there are no menus or information shown — nothing to review or edit
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: By design — the bulletin provides Begin/Edit registration links. That's the intended entry point for reviewing or editing registration details.

### PL-040: Player Details form missing Academic Honors and Athletic Honors/Awards fields
- **Area**: Registration Process Review
- **What I did**: Registered for The Players Series: Girls Summer Showcase 2026 and looked at the Player Details form
- **What I expected**: Academic Honors and Athletic Honors/Awards text entry fields to appear, as they do in the Legacy system
- **What happened**: Both fields are missing from the new Player Details form — they exist in Legacy but aren't showing up here
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Profile migration parser (CSharpToMetadataParser) had no regex pattern for `<textarea asp-for="...">` — only `<input>`, `<select>`, and `@Html.*For()` helpers. Added Pattern 3c for textarea tag helpers. This also fixes other textarea fields across PP03, PP09, PP43, PP45, CAC14 profiles. Re-run migration for affected profiles to pick up the missing fields.

### PL-041: Standardize how optional fields are indicated across all forms
- **Area**: Registration Process Review
- **What I did**: Looked at optional fields across registration forms
- **What I expected**: Consistent treatment — either "(OPTIONAL)" after the label or placeholder text like "Leave blank if unknown" inside the field
- **What happened**: No consistent pattern for marking optional fields — need to pick one approach and apply it everywhere
- **Severity**: UX
- **Status**: Fixed — standardized on `<span class="tip">(optional)</span>` (italic muted text). Migrated player-form-modal and family-edit-modal off the ad-hoc Bootstrap classes. Children-step and dynamic profile fields already match.

### PL-042: "Click Here to Begin" bulletin only goes to Adult Registration — consider splitting Player and Coach paths
- **Area**: Registration Process Review
- **What I did**: Clicked "Click Here to Begin a Player or Coach registration and waiver" on the Player Self-Rostering page
- **What I expected**: Option to choose between Player registration and Coach registration
- **What happened**: Only brings me to Adult Registration — no way to go to Player registration. Does it make sense to split the bulletin into separate links for Player and Coach paths?
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Backend regex patterns were matching combined player+staff URLs and replacing with a single route before the frontend pipe could split them. Added negative lookahead to skip combined URLs — frontend pipe now correctly splits into two links (Player + Coach).

### PL-043: Public Rosters "Click Here to view currently rostered players" leads to 404 error
- **Area**: Registration Process Review
- **What I did**: Clicked "Click Here to view currently rostered players" on the Public Rosters page
- **What I expected**: A page showing the currently rostered players
- **What happened**: Screen shows a 404 error
- **Severity**: Bug
- **Status**: Fixed

### PL-044: Assign Teams dropdown on Players ARB site no longer shows cost per option
- **Area**: Registration Process Review
- **What I did**: Opened the Assign Teams dropdown on the Players ARB site
- **What I expected**: Cost of each team option shown in parentheses next to the name, like it was in Legacy
- **What happened**: Cost is no longer displayed in the dropdown — should we add it back?
- **Severity**: Question
- **Status**: Fixed — `AvailableTeamDto.EffectiveFee` now layers active modifiers (early-bird, late fee) on top of the base fee at list time. Dropdown label becomes `Team · Division ($120)` when fee > 0. Payment screen still recomputes final totals fresh.

### PL-045: ARB site Complete Payment — subscription and recurring payments charged separately per player in family
- **Area**: Registration Process Review
- **What I did**: Registered multiple players on the ARB site and reached the Complete Payment screen
- **What I expected**: Unclear — should subscription setup and recurring payments be combined for the family or separate per player?
- **What happened**: Subscription setup and recurring payments are charged separately for each player in the family — need to confirm if this is intended or should be consolidated
- **Severity**: Question
- **Status**: Won't Fix — matches legacy behavior by design. Legacy `PlayerBaseController.cs` iterates family players and calls `ADN_ARB_CreateMonthlySubscription` per player; each Registrations row carries its own `AdnSubscriptionId`. Consolidation is not in scope — new system MUST match legacy.

### PL-046: Add "eye" icon to Password and Confirm Password fields to toggle visibility
- **Area**: Family Account Creation
- **What I did**: Looked at the Password and Confirm Password fields on the Create Family Account screen
- **What I expected**: An eye icon at the end of each password field to let users see what they typed
- **What happened**: No visibility toggle — should an eye icon be added? Could also be useful on other password fields across the site
- **Severity**: Question
- **Status**: Fixed — swept across all password inputs in registration/auth flows. Login and reset-password already had it; added to family credentials (2 fields), adult account (2 fields), team club-rep (2 fields), and self-roster update modal (1 field). Shared `.password-toggle` styling lives in `_forms.scss`.

### PL-065: "Update My Family Account Data and/or Players" button goes to same place as "Create NEW Family Account"
- **Area**: Family Account Creation
- **What I did**: Clicked the "Update My Family Account Data and/or Players" button on the Player Registration card
- **What I expected**: A different screen for updating an existing account
- **What happened**: Goes to the same place as "Create NEW Family Account" — these should lead to different flows
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: By design — both paths use the same wizard flow, which detects existing accounts automatically.

### PL-064: Add "Don't have a family account yet?" above the Create New Family Account button
- **Area**: Family Account Creation
- **What I did**: Looked at the Player Registration card login area
- **What I expected**: Helpful prompt text for new parents above the create account button
- **What happened**: No introductory text above the "Create New Family Account" button — add "Don't have a family account yet?" to guide first-time parents
- **Severity**: UX
- **Status**: Fixed — prompt added above the Create NEW Family Account button.

### PL-063: Premier Lacrosse 2026 (CAC site) behaves like a single player option site
- **Area**: Registration Process Review
- **What I did**: Tested registration on Premier Lacrosse 2026, which is a CAC (Club/Affiliate/Camp) site
- **What I expected**: Multi-team selection behavior appropriate for a CAC site
- **What happened**: Site behaves like a single player option site, not a CAC site — needs to be updated to support CAC registration flow
- **Severity**: Bug
- **Status**: Fixed

### PL-062: Where is headshot uploaded when adding a player?
- **Area**: Family Account Creation
- **What I did**: Added a new player
- **What I expected**: An option to upload a player headshot somewhere in the flow
- **What happened**: No headshot upload visible — where should this happen?
- **Severity**: Question
- **Status**: Won't Fix — headshots are no longer collected. No active job uses them.

### PL-061: 404 error after signing in following new account creation
- **Area**: Family Account Creation
- **What I did**: Created a new family account, then signed in to proceed to registration, accepted Terms of Service
- **What I expected**: To land on the registration flow after signing in
- **What happened**: Got a 404 error screen after signing in. Note: Legacy went directly to registration without requiring a separate sign-in, but the sign-in step seems like a good idea. Terms of Service appearing here also makes sense. The 404 is the problem.
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Root cause: navigation used old route paths (`register-player`, `register-team`) but actual routes are `registration/player` and `registration/team`. Fixed in family.component.ts, review-step.component.ts, and job-home.component.ts.

### PL-060: Add a review/summary screen after finishing player entry — like Legacy had
- **Area**: Family Account Creation
- **What I did**: Finished adding all players
- **What I expected**: A review screen showing all the data entered (contacts, address, players) before proceeding
- **What happened**: No review screen — Legacy provided one and it was helpful for parents to verify everything before continuing
- **Severity**: UX
- **Status**: Fixed — wizard has a Review & Save step (after Players, before ToS) that summarizes Parent 1, Parent 2, Address, and the players table. Confirmed covers the Legacy review-screen intent.

### PL-059: "Add Child" button should read "Add Player"
- **Area**: Family Account Creation
- **What I did**: Looked at the button to add a player
- **What I expected**: Button text to say "Add Player"
- **What happened**: Button says "Add Child" — should say "Add Player"
- **Severity**: UX
- **Status**: Fixed — submit button label changed from "Add child" to "Add Player".

### PL-058: Cell phone display should show hyphens (e.g., 555-123-4567)
- **Area**: Family Account Creation
- **What I did**: Entered a cell phone number — input accepts it with or without hyphens, which is fine
- **What I expected**: Display to always show the number formatted with hyphens
- **What happened**: Number displays without hyphens. See the player data output after adding a new player as an example of where this shows up.
- **Severity**: UX
- **Status**: Fixed — `formatPhone()` helper renders 10/11-digit phone as NNN-NNN-NNNN in the player list.

### PL-057: Player date of birth format should be MM/DD/YYYY not YYYY-MM-DD
- **Area**: Family Account Creation
- **What I did**: Looked at the date format in the player fields
- **What I expected**: US date format MM/DD/YYYY (e.g., 01/01/2015)
- **What happened**: Shows as 2015-01-01 — should display as 01/01/2015
- **Severity**: UX
- **Status**: Fixed — `formatDob()` helper renders ISO yyyy-mm-dd as MM/DD/YYYY in the player list.

### PL-056: After adding a player, header should say "Player 1 added"
- **Area**: Family Account Creation
- **What I did**: Added a player in the Add Children section
- **What I expected**: Header to confirm the player was added, e.g., "Player 1 added"
- **What happened**: No confirmation header showing the player was successfully added
- **Severity**: UX
- **Status**: Fixed — added players list header now reads "Player 1 added" (or "N players added" for >1).

### PL-055: "Add Children" section — rename to "Add Player", update wording
- **Area**: Family Account Creation
- **What I did**: Looked at the Add Children section
- **What I expected**: Player-focused wording
- **What happened**: Multiple wording changes needed: (1) Change "Add Children" button to "Add Player", (2) Change step 4 at top to "Players", (3) Change instruction to "Add at least one player to continue", (4) Remove the line "Add each child who will be registered as a player"
- **Severity**: UX
- **Status**: Fixed — all four wording changes applied (section heading, step bar label, empty-state alert, removed instructional line).

### PL-054: Continue button doesn't activate until you click outside the last required field
- **Area**: Family Account Creation
- **What I did**: Filled in the last required field but stayed focused in it
- **What I expected**: Continue button to activate as soon as the last required field has valid input
- **What happened**: Button stays inactive until you click outside the field — requires an extra click. Can it activate immediately after entering the last item?
- **Severity**: UX
- **Status**: Fixed
- **Note**: Added `(input)="syncToState()"` alongside `(blur)` on all form fields in contacts-step and address-step (credentials-step already had it). State now updates in real-time so Continue activates instantly.

### PL-053: Change address instruction to "Enter your player's/family's mailing address"
- **Area**: Family Account Creation
- **What I did**: Read the address section instruction text
- **What I expected**: Wording that covers both player and family
- **What happened**: Says "Enter your family's mailing address" — should say "Enter your player's/family's mailing address"
- **Severity**: UX
- **Status**: Fixed.

### PL-052: Add "Select Cell Phone Provider" field for text messaging — all registration types
- **Area**: Family Account Creation
- **What I did**: Compared Parent Details to Legacy
- **What I expected**: A "Select Cell Phone Provider" optional field for text messaging, like Legacy has
- **What happened**: Field is missing. Legacy has it as "SELECT CELL PHONE PROVIDER (optional: for text messaging)." Should it be added here and anywhere else a cell phone is collected (Director, Club Rep, Staff, etc.)?
- **Severity**: Question
- **Status**: Won't Fix — carrier email-to-SMS gateways (`@vtext.com`, etc.) are no longer reliable. US carriers have progressively deprecated or blackholed those gateways. The field is obsolete; real SMS would need a proper provider (Twilio, etc.) keyed on phone number alone.

### PL-051: Remove "Both parent/guardian contacts are required" line from Family Contacts
- **Area**: Family Account Creation
- **What I did**: Looked at the top of the Family Contacts section
- **What I expected**: No unnecessary instructional text
- **What happened**: Line says "Both parent/guardian contacts are required" — should be removed
- **Severity**: UX
- **Status**: Fixed — wizard-tip line removed.

### PL-050: Change Family Contacts headers to "Parent/Contact 1 Details" and "Parent/Contact 2 Details"
- **Area**: Family Account Creation
- **What I did**: Looked at the Family Contacts section headers
- **What I expected**: Headers that clearly label each contact as "Parent/Contact 1 Details" and "Parent/Contact 2 Details"
- **What happened**: Current headers don't use that wording — should be renamed for clarity
- **Severity**: UX
- **Status**: Fixed — headers now read "Parent/Contact 1 Details" and "Parent/Contact 2 Details" (replaces dynamic Mom/Dad labels for these section headings).

### PL-049: Terms of Service acceptance screen missing after entering username/password
- **Area**: Family Account Creation
- **What I did**: Entered a username and password and clicked Continue on the new account creation screen
- **What I expected**: A Terms of Service acceptance screen to appear, like it does in Legacy
- **What happened**: No Terms of Service screen — goes straight through. Should it be added here?
- **Severity**: Question
- **Status**: Fixed — ToS step added to family wizard (create mode) after Review. Auto-logs in and persists acceptance to AspNetUsers on accept. Commits `b26b896a` (2026-04-08) and `aa709b7a` (2026-04-10).

### PL-048: Legacy collected Email for Family Account in addition to contact emails — still needed?
- **Area**: Family Account Creation
- **What I did**: Compared the new Family Account creation form to the Legacy system
- **What I expected**: Same fields collected, or a clear reason why some were dropped
- **What happened**: Legacy collected an Email field for the Family Account itself, separate from contact emails — the new system doesn't. Need to confirm if this is still needed or intentionally removed.
- **Severity**: Question
- **Status**: Won't Fix — intentionally simplified. `AspNetUsers.Email` is now derived from Contact 1's email in `FamilyService.RegisterAsync`, so password reset still works. Functional match with legacy; one fewer redundant input for parents.

### PL-047: Rewrite account creation text and add Back button for existing users
- **Area**: Family Account Creation
- **What I did**: Read the text on the account creation screen
- **What I expected**: Clear instructions for new users, and a way for existing users to go back to login
- **What happened**: Text says "New here? Choose a username and password. Already have an account? Enter your existing credentials." — this is confusing. Should say "Choose a username and password for your NEW account" and on a new line "Already have an account? Select 'Back' below to login." Also need to add a Back button on this screen.
- **Severity**: UX
- **Status**: Fixed — wizard-tip rewritten to Ann's wording. Back button is provided by WizardShell on every step.

---

## Second Pass Items

*Started 2026-04-14. Numbered independently (SP-001, SP-002, ...).*

### SP-001: Create Family Account — add Back button at bottom of Username/Password screen
- **Area**: Family Account Creation
- **What I did**: Reached the Create Family Account screen where Username and Password are chosen
- **What I expected**: A Back button at the bottom of the screen, consistent with the instruction text "Already have an account? Select Back below to login."
- **What happened**: No Back button at the bottom — the instruction text references a button that isn't there
- **Severity**: UX
- **Status**: Fixed — replaced "Select Back below to login" with inline "click here" link routing to `registration/player` (credentials-step.component.ts)

### SP-002: "Let's Register Your Players!" — remove "Update My Family Account Data and/or Players" button
- **Area**: Registration Process Review
- **What I did**: Looked at the "Let's Register Your Players!" screen and clicked the "Update My Family Account Data and/or Players" button
- **What I expected**: Either no button (since the first post-login screen already provides these functions), or for the button to navigate to the correct place
- **What happened**: Button is redundant with post-login functionality AND doesn't lead to the correct place — remove it
- **Severity**: Bug
- **Status**: Fixed — removed the redundant button (family-check-step.component.ts)

### SP-003: "Let's Register Your Players!" — reword sign-in instruction
- **Area**: Registration Process Review
- **What I did**: Read the instruction text on the "Let's Register Your Players!" screen
- **What I expected**: Wording that covers both registering and updating an existing family account
- **What happened**: Text reads "Sign in with your family account to get started." — change to "Sign in to get started or to update your family account details."
- **Severity**: UX
- **Status**: Won't change — current wording is acceptable

### SP-004: Move "Add Player" button to right side of entry card for clarity
- **Area**: Family Account Creation
- **What I did**: Looked at the Add Players screen and the placement of the "Add Player" button relative to the Wizard "Continue" button
- **What I expected**: "Add Player" button positioned on the right side of the card where player data is entered, so it's visually clear it must be clicked before Continue
- **What happened**: Current placement isn't clear enough — users may click Continue before adding the player they just typed in. Move "Add Player" to the right side of the entry card.
- **Severity**: UX
- **Status**: Fixed — Add Player button moved flush right (children-step.component.ts)

### SP-005: Review screen — move Username to its own section at top, relabel "Family Account Username"
- **Area**: Family Account Creation
- **What I did**: Looked at the Save and Review screen layout
- **What I expected**: The account username displayed in a distinct section at the top of the data card, labeled "Family Account Username"
- **What happened**: Username currently sits at the bottom of Parent/Contact 1 info and is labeled only "Username" — promote it to its own top-of-card section and relabel it "Family Account Username"
- **Severity**: UX
- **Status**: Completed

### SP-006: Edit Player — make "Save changes" more prominent and move to right with Cancel
- **Area**: Family Account Creation
- **What I did**: Clicked the edit (pencil) icon on a player, made changes, and tried to save. Missed the Save changes button more than a few times.
- **What I expected**: A prominent, easy-to-spot "Save changes" button — ideally positioned on the right side of the edit card alongside Cancel so the action is obvious
- **What happened**: Save changes button is not visually prominent and its placement makes it easy to miss — move it to the right side next to Cancel and give it stronger visual emphasis
- **Severity**: UX
- **Status**: Completed

### SP-007: Edit Family Account/Players — flow drops into Players step with no forward path to Contacts/Address; Back loses changes
- **Area**: Family Account Creation
- **What I did**: Logged in with an existing Family Account and selected "Edit Family Account/Players". Added two players. Then tried to edit Contacts and Address info.
- **What I expected**: Edit flow to walk through Contacts → Address → Players → Review → Register, carrying changes forward through each step — same logical order as the create flow
- **What happened**: (1) Only way to reach Contacts/Address was to click the Back arrow, which isn't intuitive as an "edit" path. (2) Going back one step further jumped to the Create New Family Account screen and my player additions were lost — had to start over.
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Two fixes: (1) "Edit Family Account/Players" now deep-links to `contacts` step instead of `children`, so the edit flow starts at Contacts and walks forward through Address → Players → Review. (2) Family wizard step indicators are now clickable (wired `goToStep` like player/team wizards), so users can jump to any completed step directly.

### SP-008: Processing fees not carried forward from Legacy; re-entry loses "Registered" status and forces re-entry of Grad Year/Team
- **Area**: Registration Process Review
- **What I did**: Registered two players on The Players Series: Summer Showcase 2026. No processing fees appeared on the payment screen. Went back into admin and added the processing fees, then returned to the payment screen.
- **What I expected**: (1) Processing fees configured in Legacy to carry forward automatically — no manual re-entry needed. (2) After adding fees, they should apply to all selected players. (3) On return to the registration flow, already-registered players should retain their "Registered" status and the previously collected Grad Year and Team data.
- **What happened**: (1) Processing fees were missing on the payment screen for both players. (2) After adding fees manually, they only applied to one of the two players. (3) On return, the players no longer showed as "Registered" and I was forced to re-enter Grad Year and Team — data the system already had.
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Review step now shows `feeBase` (labeled "Registration Fee") instead of `feeTotal` — processing fees are payment-method-dependent and belong on the payment step, not review. All players display consistently regardless of preSubmit timing.

### SP-009: Processing fees configured in Job Configuration/Payment don't apply to fresh registrations
- **Area**: Registration Process Review
- **What I did**: After re-adding Processing Fees in Job Configuration/Payment, created a brand new family account and walked through registration from scratch on The Players Series: Summer Showcase 2026.
- **What I expected**: Processing fees configured in Job Configuration/Payment to apply to the amount owed on the payment screen
- **What happened**: Payment screen did not charge the processing fees, even though they were properly configured in Job Configuration/Payment for this job. Suggests the payment screen isn't reading the fee configuration correctly for new registrations.
- **Severity**: Bug
- **Status**: Fixed
- **Note**: See SP-008 note. Review step corrected to show base registration fee; processing fees calculated and displayed on payment step after preSubmit.

### SP-010: No Back option on Choose Your Players screen (possible regression of PL-014)
- **Area**: Registration Process Review
- **What I did**: Arrived at the Choose Your Players screen during a second-pass walkthrough
- **What I expected**: A Back button to return to the prior step — PL-014 was previously marked Fixed (WizardShellComponent was supposed to provide Back on all steps)
- **What happened**: No Back option visible on the Choose Your Players screen — appears to be a regression of PL-014
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: Players is the first real step after family-check (login). When authenticated, family-check is skipped and the user lands directly on players. Back would go to the "Signed in as xxx" screen — no useful action there.

### SP-011: Trash can icons missing on Choose Your Players card (reopens PL-015)
- **Area**: Registration Process Review
- **What I did**: Looked at player rows on the Choose Your Players card during second-pass review
- **What I expected**: A trash can icon next to the pencil icon on each player row — PL-015 was marked Fixed with note "trash icon already present next to pencil on each player row"
- **What happened**: Trash can icons are NOT present next to the pencil icons on the player rows. PL-015 needs to be reopened — the icon either was never added or has regressed.
- **Severity**: Bug
- **Status**: Won't Fix
- **Note**: This screen selects which players to register — it doesn't manage the player roster. Removing players from the family account is done via "Edit Family Account/Players" which opens the family wizard.

### SP-012: Revisit capacity info in Assign Teams dropdown (reopens PL-017)
- **Area**: Registration Process Review
- **What I did**: Reconsidered the "Capacity shown in dropdown" behavior that was previously marked Won't Fix on PL-017
- **What I expected**: Feature to be evaluated against director preferences and legacy behavior before committing to it
- **What happened**: Concerns: (1) directors may not want capacity visible to parents — especially when a high max default is in place, (2) Legacy did not expose this, (3) showing capacity may cause more issues than it solves. Recommend making this optional (director toggle) or removing entirely to match Legacy.
- **Severity**: Question
- **Status**: Open

### SP-013: Profile migration from Legacy must preserve exact field order (follow-up to PL-022)
- **Area**: Registration Process Review
- **What I did**: Looked at the player form for The Players Series: Girls Summer Showcase 2026 during second-pass review. PL-022 was marked Fixed ("field order is profile-driven") but the migrated profile didn't match Legacy's field order.
- **What I expected**: When profiles are migrated from Legacy into the new system, field order should be imported exactly as presented in Legacy — clients expect their forms to look the same
- **What happened**: This profile came over with fields in a different order than Legacy. I manually edited the new version and the profile is now correct. But this is a broader migration-quality concern: the profile importer (CSharpToMetadataParser or similar) needs to preserve Legacy's original field ordering so directors aren't required to re-sequence every migrated profile by hand.
- **Severity**: Bug
- **Status**: Open
- **Note**: Individual profile corrected manually. Underlying migration-order issue still needs to be addressed to prevent recurrence on other profiles.

### SP-014: Review and Accept Waivers — visibly list players the waiver applies to (follow-up to PL-025)
- **Area**: Registration Process Review
- **What I did**: Revisited the Review and Accept Waivers screen. PL-025 was closed Won't Fix because per-player interactive checkboxes don't change the consent, which is reasonable.
- **What I expected**: A clearer visual signal of which players the waiver covers, without requiring the parent to tick each player individually
- **What happened**: Compromise suggestion — keep the single acceptance checkbox per waiver, but render the selected players as a visible list directly on the screen. Options in order of preference: (1) bullet list with a pre-checked check-mark icon next to each player name, (2) bullet list with names only, (3) plain list with names called out prominently. Goal: make it obvious at a glance who the waiver applies to.
- **Severity**: UX
- **Status**: Open

### SP-015: "Almost There!" screen — player name section headers still need more visual distinction (follow-up to PL-030)
- **Area**: Registration Process Review
- **What I did**: Looked at the Almost There review screen after PL-030 fix (player names bumped to base size + bold weight)
- **What I expected**: Player names to read clearly as section headers separating each player's review block
- **What happened**: Even with the bold + larger font, the names still don't feel distinct enough as table/section headers. Suggest adding color (e.g., blue/primary) or another treatment beyond weight alone so the eye jumps to them as clear section dividers.
- **Severity**: UX
- **Status**: Open

### SP-016: Refund Protection — can a parent re-opt-in after declining? (follow-up to PL-033)
- **Area**: Registration Process Review
- **What I did**: Re-read PL-033, which documented that the Refund Protection decline is locked for the session by design
- **What I expected**: Clarity on whether a parent has any path to add coverage later — e.g., if they log back in after declining, do they see the offer again, or is there a dedicated "second chance" link/bulletin somewhere?
- **What happened**: Unknown — need confirmation of the intended post-decline experience. Options to consider: (1) offer re-appears on next login until a window closes, (2) a bulletin/link in the Family Account area lets them opt in after the fact, (3) truly one-shot with no recovery. Need to decide + document.
- **Severity**: Question
- **Status**: Open

### SP-017: Post-registration login — Player role lands with no actions (no Edit, no Pay Balance, etc.)
- **Area**: Registration Process Review
- **What I did**: With a player already registered, used Family Account Login (upper-right) and selected the Player's role on the role-selection screen
- **What I expected**: A landing page offering post-registration actions — at minimum: edit registration details, view/pay balance due, view receipt, view teams/schedule
- **What happened**: After selecting the Player role, there are no options to do anything — dead end for returning registered players. Consider: (1) a "My Registrations" dashboard with Edit / Pay Balance / View Receipt actions, (2) surface any outstanding balance prominently, (3) allow profile / form field updates where the window hasn't closed.
- **Severity**: Bug
- **Status**: Open

### SP-018: Discount code "Girls100" zeroes out payment but registration never persists — silent failure
- **Area**: Registration Process Review
- **What I did**: Registered a player on The Players Series: Summer Showcase 2026 from TestFamily007. Entered Discount Code "Girls100" (absolute discount amount: $100). Payment screen showed $0 owed and I completed the flow.
- **What I expected**: Either (a) registration records successfully at $0 owed and shows up in Search/Registrations, or (b) a clear error preventing the flow from finishing
- **What happened**: Payment screen returned "nothing owed" and appeared to complete successfully, but the registration never persisted — the player does not appear in Search/Registrations. Silent failure with no error surfaced to the parent. Registration is effectively lost.
- **Severity**: Bug
- **Status**: Fixed
- **Repro notes**: TestFamily007, The Players Series: Summer Showcase 2026, discount code "Girls100" ($100 absolute). Bring-fee-to-zero discount path appears to short-circuit the registration/persist step.

