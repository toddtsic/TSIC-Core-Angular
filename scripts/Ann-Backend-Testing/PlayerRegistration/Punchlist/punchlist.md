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
- **Status**: Re-review — significant updates since this was logged (header now says "Family Account Sign In", wizard-tip styling, etc.). May already be addressed.

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
- **Status**: Open

### PL-019: Consider merging Graduation Year and Assign Teams into one screen
- **Area**: Registration Process Review
- **What I did**: Went through Set Player Graduation Year and then Assign Teams as separate steps
- **What I expected**: Possibly a single screen since both are short player setup tasks
- **What happened**: Two separate screens for related info — could these be combined into one step to reduce clicks?
- **Severity**: Question
- **Status**: Open

### PL-020: Choose Your Players — change "Edit Account" to "Edit Family Contact Info"
- **Area**: Registration Process Review
- **What I did**: Saw "Edit Account" link on the Choose Your Players screen
- **What I expected**: Label that clearly describes what you're editing
- **What happened**: "Edit Account" is vague — should say "Edit Family Contact Info" to be specific
- **Severity**: UX
- **Status**: Open

### PL-021: USA Lacrosse Number validation — wrap phone number on one line in failed entry popup
- **Area**: Registration Process Review
- **What I did**: Entered an invalid USA Lacrosse Number and got the validation failure popup
- **What I expected**: Phone number displayed fully on one line
- **What happened**: Phone number wraps awkwardly across two lines — needs to stay on a single line
- **Severity**: UX
- **Status**: Open

### PL-022: Player form — move Weight next to Height, make Height optional, move Shorts Size next to T-shirt Size
- **Area**: Registration Process Review
- **What I did**: Filled out the player form for The Players Series: Girls Summer Showcase 2026
- **What I expected**: Related fields grouped together — Height/Weight side by side, Shorts Size/T-shirt Size side by side; Height should be optional
- **What happened**: Weight is not next to Height, Shorts Size is not next to T-shirt Size, and Height is required when it shouldn't be
- **Severity**: UX
- **Status**: Open

### PL-023: Player Details form — increase font size of Team Selected next to Player Name
- **Area**: Registration Process Review
- **What I did**: Looked at the Player Details form heading
- **What I expected**: Team Selected to be prominent and easy to read next to the Player Name
- **What happened**: Team Selected text is too small — needs a bigger font so it stands out
- **Severity**: UX
- **Status**: Open

### PL-024: Player Details — white data entry fields with tinted surrounding background for consistency
- **Area**: Registration Process Review
- **What I did**: Looked at the Player Details form styling
- **What I expected**: White input fields on a tinted/shaded background, matching the look of other registration screens
- **What happened**: Fields and background don't have enough contrast between them — make input fields white and the surrounding card background tinted for visual consistency across all registration screens
- **Severity**: UX
- **Status**: Open

### PL-025: Review and Accept Waivers — larger player names in a list with individual checkboxes
- **Area**: Registration Process Review
- **What I did**: Arrived at the Review and Accept Waivers screen
- **What I expected**: Player names displayed prominently in a list with a checkbox next to each name for the parent to actively confirm
- **What happened**: Player names are too small and not listed clearly — consider having the parent check a box next to each player's name to acknowledge the waiver for each child individually
- **Severity**: UX
- **Status**: Open

### PL-026: Review and Accept Waivers — make "ALL" capitalized and increase font size of intro text
- **Area**: Registration Process Review
- **What I did**: Read the intro text on the waivers screen ("These waivers apply to all selected players...")
- **What I expected**: Prominent, easy-to-read text with emphasis on "ALL"
- **What happened**: Text is too small and "all" is not capitalized — change to "ALL" (caps) and make the entire intro line larger so parents don't miss it
- **Severity**: UX
- **Status**: Open

### PL-027: "Almost There!" screen — Team selection text needs larger font
- **Area**: Registration Process Review
- **What I did**: Arrived at the "Almost There!" review screen
- **What I expected**: Team selection info displayed prominently
- **What happened**: Team selection text is too small — needs a larger font so it stands out
- **Severity**: UX
- **Status**: Open

### PL-028: "Almost There!" screen — change "F" to "Female" (spell out gender)
- **Area**: Registration Process Review
- **What I did**: Saw gender displayed as "F" on the Almost There review screen
- **What I expected**: Full word "Female" (and presumably "Male" instead of "M")
- **What happened**: Gender shows as a single letter abbreviation — should be spelled out
- **Severity**: UX
- **Status**: Open

### PL-029: "Almost There!" screen — "Review your details" notes slightly too small
- **Area**: Registration Process Review
- **What I did**: Read the "Review your details" section on the Almost There screen
- **What I expected**: Text large enough to read comfortably without being oversized
- **What happened**: All the detail notes are a bit too small — bump up the font size slightly (not too much, just enough to improve readability)
- **Severity**: UX
- **Status**: Open

### PL-030: "Almost There!" screen — player names as section headers need bolder font weight
- **Area**: Registration Process Review
- **What I did**: Looked at player name headers on the Almost There review screen
- **What I expected**: Player names to stand out clearly as section headers
- **What happened**: Names aren't bold enough — need heavier font weight so they read as headers
- **Severity**: UX
- **Status**: Open

### PL-031: "Almost There!" screen — should accounting/fee summary be shown here?
- **Area**: Registration Process Review
- **What I did**: Reviewed the Almost There screen before proceeding to payment
- **What I expected**: Possibly a fee summary or accounting breakdown before the parent commits
- **What happened**: No accounting or fee information shown — should parents see what they owe before continuing?
- **Severity**: Question
- **Status**: Open

### PL-032: Complete Payment — what is "Pay in Full" button for before Add Refund Protection?
- **Area**: Registration Process Review
- **What I did**: Arrived at the Complete Payment screen and saw a "Pay in Full" button appearing before the Add Refund Protection option
- **What I expected**: Clear understanding of what "Pay in Full" does at that point in the flow
- **What happened**: Not clear why "Pay in Full" appears before the refund protection option — shouldn't the parent decide on refund protection first?
- **Severity**: Question
- **Status**: Open

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
- **Status**: Open

### PL-035: Confirm Registration Payment + Insurance popup — standardize font size and replace icons with bullets
- **Area**: Registration Process Review
- **What I did**: Opened the Confirm Registration Payment + Insurance popup
- **What I expected**: All text items in the same (larger) font size, with simple bullet points instead of icons
- **What happened**: Mixed font sizes and icons used instead of bullets — needs uniform larger font and plain bullet list
- **Severity**: UX
- **Status**: Open

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
- **Status**: Open

### PL-038: Waiver area on confirmation shows "BY CLICKING NEXT BELOW, I AGREE..." — confuses parents
- **Area**: Registration Process Review
- **What I did**: Completed registration and saw the waiver section on the confirmation page
- **What I expected**: Clear indication that the waiver was already accepted during registration — no action needed
- **What happened**: Text says "BY CLICKING NEXT BELOW, I AGREE WITH THE ABOVE RELEASE OF LIABILITY" which makes parents think they need to do something else. Either remove this text or add a clarifying note outside the waiver card (e.g., "Waiver accepted during registration")
- **Severity**: UX
- **Status**: Open

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
- **Status**: Open

### PL-042: "Click Here to Begin" bulletin only goes to Adult Registration — consider splitting Player and Coach paths
- **Area**: Registration Process Review
- **What I did**: Clicked "Click Here to Begin a Player or Coach registration and waiver" on the Player Self-Rostering page
- **What I expected**: Option to choose between Player registration and Coach registration
- **What happened**: Only brings me to Adult Registration — no way to go to Player registration. Does it make sense to split the bulletin into separate links for Player and Coach paths?
- **Severity**: Question
- **Status**: Open

### PL-043: Public Rosters "Click Here to view currently rostered players" leads to 404 error
- **Area**: Registration Process Review
- **What I did**: Clicked "Click Here to view currently rostered players" on the Public Rosters page
- **What I expected**: A page showing the currently rostered players
- **What happened**: Screen shows a 404 error
- **Severity**: Bug
- **Status**: Open

### PL-044: Assign Teams dropdown on Players ARB site no longer shows cost per option
- **Area**: Registration Process Review
- **What I did**: Opened the Assign Teams dropdown on the Players ARB site
- **What I expected**: Cost of each team option shown in parentheses next to the name, like it was in Legacy
- **What happened**: Cost is no longer displayed in the dropdown — should we add it back?
- **Severity**: Question
- **Status**: Open

### PL-045: ARB site Complete Payment — subscription and recurring payments charged separately per player in family
- **Area**: Registration Process Review
- **What I did**: Registered multiple players on the ARB site and reached the Complete Payment screen
- **What I expected**: Unclear — should subscription setup and recurring payments be combined for the family or separate per player?
- **What happened**: Subscription setup and recurring payments are charged separately for each player in the family — need to confirm if this is intended or should be consolidated
- **Severity**: Question
- **Status**: Open

### PL-046: Add "eye" icon to Password and Confirm Password fields to toggle visibility
- **Area**: Family Account Creation
- **What I did**: Looked at the Password and Confirm Password fields on the Create Family Account screen
- **What I expected**: An eye icon at the end of each password field to let users see what they typed
- **What happened**: No visibility toggle — should an eye icon be added? Could also be useful on other password fields across the site
- **Severity**: Question
- **Status**: Open

### PL-065: "Update My Family Account Data and/or Players" button goes to same place as "Create NEW Family Account"
- **Area**: Family Account Creation
- **What I did**: Clicked the "Update My Family Account Data and/or Players" button on the Player Registration card
- **What I expected**: A different screen for updating an existing account
- **What happened**: Goes to the same place as "Create NEW Family Account" — these should lead to different flows
- **Severity**: Bug
- **Status**: Open

### PL-064: Add "Don't have a family account yet?" above the Create New Family Account button
- **Area**: Family Account Creation
- **What I did**: Looked at the Player Registration card login area
- **What I expected**: Helpful prompt text for new parents above the create account button
- **What happened**: No introductory text above the "Create New Family Account" button — add "Don't have a family account yet?" to guide first-time parents
- **Severity**: UX
- **Status**: Open

### PL-063: Premier Lacrosse 2026 (CAC site) behaves like a single player option site
- **Area**: Registration Process Review
- **What I did**: Tested registration on Premier Lacrosse 2026, which is a CAC (Club/Affiliate/Camp) site
- **What I expected**: Multi-team selection behavior appropriate for a CAC site
- **What happened**: Site behaves like a single player option site, not a CAC site — needs to be updated to support CAC registration flow
- **Severity**: Bug
- **Status**: Open

### PL-062: Where is headshot uploaded when adding a player?
- **Area**: Family Account Creation
- **What I did**: Added a new player
- **What I expected**: An option to upload a player headshot somewhere in the flow
- **What happened**: No headshot upload visible — where should this happen?
- **Severity**: Question
- **Status**: Open

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
- **Status**: Open

### PL-059: "Add Child" button should read "Add Player"
- **Area**: Family Account Creation
- **What I did**: Looked at the button to add a player
- **What I expected**: Button text to say "Add Player"
- **What happened**: Button says "Add Child" — should say "Add Player"
- **Severity**: UX
- **Status**: Open

### PL-058: Cell phone display should show hyphens (e.g., 555-123-4567)
- **Area**: Family Account Creation
- **What I did**: Entered a cell phone number — input accepts it with or without hyphens, which is fine
- **What I expected**: Display to always show the number formatted with hyphens
- **What happened**: Number displays without hyphens. See the player data output after adding a new player as an example of where this shows up.
- **Severity**: UX
- **Status**: Open

### PL-057: Player date of birth format should be MM/DD/YYYY not YYYY-MM-DD
- **Area**: Family Account Creation
- **What I did**: Looked at the date format in the player fields
- **What I expected**: US date format MM/DD/YYYY (e.g., 01/01/2015)
- **What happened**: Shows as 2015-01-01 — should display as 01/01/2015
- **Severity**: UX
- **Status**: Open

### PL-056: After adding a player, header should say "Player 1 added"
- **Area**: Family Account Creation
- **What I did**: Added a player in the Add Children section
- **What I expected**: Header to confirm the player was added, e.g., "Player 1 added"
- **What happened**: No confirmation header showing the player was successfully added
- **Severity**: UX
- **Status**: Open

### PL-055: "Add Children" section — rename to "Add Player", update wording
- **Area**: Family Account Creation
- **What I did**: Looked at the Add Children section
- **What I expected**: Player-focused wording
- **What happened**: Multiple wording changes needed: (1) Change "Add Children" button to "Add Player", (2) Change step 4 at top to "Players", (3) Change instruction to "Add at least one player to continue", (4) Remove the line "Add each child who will be registered as a player"
- **Severity**: UX
- **Status**: Open

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
- **Status**: Open

### PL-052: Add "Select Cell Phone Provider" field for text messaging — all registration types
- **Area**: Family Account Creation
- **What I did**: Compared Parent Details to Legacy
- **What I expected**: A "Select Cell Phone Provider" optional field for text messaging, like Legacy has
- **What happened**: Field is missing. Legacy has it as "SELECT CELL PHONE PROVIDER (optional: for text messaging)." Should it be added here and anywhere else a cell phone is collected (Director, Club Rep, Staff, etc.)?
- **Severity**: Question
- **Status**: Open

### PL-051: Remove "Both parent/guardian contacts are required" line from Family Contacts
- **Area**: Family Account Creation
- **What I did**: Looked at the top of the Family Contacts section
- **What I expected**: No unnecessary instructional text
- **What happened**: Line says "Both parent/guardian contacts are required" — should be removed
- **Severity**: UX
- **Status**: Open

### PL-050: Change Family Contacts headers to "Parent/Contact 1 Details" and "Parent/Contact 2 Details"
- **Area**: Family Account Creation
- **What I did**: Looked at the Family Contacts section headers
- **What I expected**: Headers that clearly label each contact as "Parent/Contact 1 Details" and "Parent/Contact 2 Details"
- **What happened**: Current headers don't use that wording — should be renamed for clarity
- **Severity**: UX
- **Status**: Open

### PL-049: Terms of Service acceptance screen missing after entering username/password
- **Area**: Family Account Creation
- **What I did**: Entered a username and password and clicked Continue on the new account creation screen
- **What I expected**: A Terms of Service acceptance screen to appear, like it does in Legacy
- **What happened**: No Terms of Service screen — goes straight through. Should it be added here?
- **Severity**: Question
- **Status**: Open

### PL-048: Legacy collected Email for Family Account in addition to contact emails — still needed?
- **Area**: Family Account Creation
- **What I did**: Compared the new Family Account creation form to the Legacy system
- **What I expected**: Same fields collected, or a clear reason why some were dropped
- **What happened**: Legacy collected an Email field for the Family Account itself, separate from contact emails — the new system doesn't. Need to confirm if this is still needed or intentionally removed.
- **Severity**: Question
- **Status**: Open

### PL-047: Rewrite account creation text and add Back button for existing users
- **Area**: Family Account Creation
- **What I did**: Read the text on the account creation screen
- **What I expected**: Clear instructions for new users, and a way for existing users to go back to login
- **What happened**: Text says "New here? Choose a username and password. Already have an account? Enter your existing credentials." — this is confusing. Should say "Choose a username and password for your NEW account" and on a new line "Already have an account? Select 'Back' below to login." Also need to add a Back button on this screen.
- **Severity**: UX
- **Status**: Open
