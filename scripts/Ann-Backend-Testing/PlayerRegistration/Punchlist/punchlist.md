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

- [ ] **LADT Setup** -- Leagues, age groups, divisions, and teams are configured correctly before registration
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

### PL-001: LADT Editor menu click should collapse expanded LADT tree
- **Area**: LADT Setup
- **What I did**: Opened LADT, expanded the tree, then clicked LADT/Editor menu again
- **What I expected**: LADT tree should collapse back to its original display
- **What happened**: Tree stayed expanded instead of reverting
- **Severity**: UX
- **Status**: Won't Fix
- **Note**: It does collapse — to the age group level, which is by design. Fully collapsing to root would just add an extra click to get anywhere useful.

### PL-002: LADT tree items spaced too far apart
- **Area**: LADT Setup
- **What I did**: Expanded the LADT tree
- **What I expected**: Compact spacing so more items are visible on one screen
- **What happened**: Too much space between items, can't see enough of the tree at once
- **Severity**: UX
- **Status**: Fixed
- **Note**: Font size dropped to match bullet items, tree spacing compressed.

### PL-003: "Sync Division Names" function unclear
- **Area**: LADT Setup
- **What I did**: Saw the "Sync Division Names" function in the LADT toolbar
- **What I expected**: Clear understanding of what it does
- **What happened**: Not clear what this function is for or when to use it
- **Severity**: Question
- **Status**: Fixed
- **Note**: Renamed to "Standardize Division Names" and added explanatory subtitle in the dialog: "Pick a naming pattern below. It will rename divisions in every age group to match, in alphabetical order." Also fixed a bug where divisions beyond the template count were force-renamed to a fallback — now they're left untouched.

### PL-004: "Sync Division Names" placement — only item under Settings
- **Area**: LADT Setup
- **What I did**: Looked at Settings area in LADT toolbar
- **What I expected**: Settings to have multiple items, or Sync Division Names to be elsewhere
- **What happened**: Sync Division Names is the only item under Settings — feels like it doesn't belong there
- **Severity**: Question
- **Status**: Open

### PL-005: League-level "+" circle — is it needed?
- **Area**: LADT Setup
- **What I did**: Hovered over the "+" circle at the League level in the LADT tree
- **What I expected**: Clear purpose or no button if unnecessary
- **What happened**: Not sure what this adds or if it's needed at the League level
- **Severity**: Question
- **Status**: Open

### PL-006: Remove Team popup references "Dropped Teams" — not applicable to player sites
- **Area**: LADT Setup
- **What I did**: Triggered the Remove Team popup
- **What I expected**: Text relevant to player registration sites
- **What happened**: Popup says "Otherwise it will be moved to Dropped Teams and deactivated" — player sites don't have Dropped Teams or Inactive Teams
- **Severity**: Bug
- **Status**: Open

### PL-007: League edit — Sport dropdown needs cleanup
- **Area**: LADT Setup
- **What I did**: Edited a League and opened the Sport dropdown
- **What I expected**: A clean, relevant list of sports
- **What happened**: Dropdown list needs cleanup (stale/irrelevant entries)
- **Severity**: UX
- **Status**: Open

### PL-008: League edit — do Hide Contacts and Hide Standings belong here?
- **Area**: LADT Setup
- **What I did**: Edited a League and saw Hide Contacts and Hide Standings radio buttons
- **What I expected**: Only league-relevant settings
- **What happened**: Not clear if Hide Contacts and Hide Standings belong at the League level
- **Severity**: Question
- **Status**: Open

### PL-009: League edit Advanced — does Reschedule Emails belong here?
- **Area**: LADT Setup
- **What I did**: Opened Advanced section under League edit
- **What I expected**: Only league-relevant advanced settings
- **What happened**: Reschedule Emails option is there — not clear it belongs at the League level
- **Severity**: Question
- **Status**: Open

### PL-010: League edit Legacy — missing options: Coach Score, TM-See-Schedule, SortProfile, Player Fee Override
- **Area**: LADT Setup
- **What I did**: Checked Legacy section under League edit
- **What I expected**: Options for Coach Score, TM-See-Schedule, SortProfile, Player Fee Override
- **What happened**: These options are no longer there — need to know if they're still needed or moved elsewhere
- **Severity**: Question
- **Status**: Open

### PL-011: LADT tree — add hover text showing the level name (League / AgeGroup / Division / Team)
- **Area**: LADT Setup
- **What I did**: Hovered over items in the LADT tree (nice that no data table shows on first load)
- **What I expected**: Hover tooltip indicating which level each item is (League, AgeGroup, Division, Team)
- **What happened**: No level indicator on hover — would help orient users
- **Severity**: UX
- **Status**: Open

### PL-036: "LADT Hierarchy" — consider renaming to "LADT Tree" and spell out the acronym somewhere
- **Area**: LADT Setup
- **What I did**: Looked at the LADT section heading
- **What I expected**: "Tree" is more intuitive than "Hierarchy"; full names (Leagues, Age Groups, Divisions, Teams) spelled out somewhere
- **What happened**: Says "LADT Hierarchy" — "Tree" would be clearer, and the acronym should be expanded at least once for new users
- **Severity**: UX
- **Status**: Open

### PL-046: No Next button after selecting two players
- **Area**: Registration Process Review
- **What I did**: Checked two players on the Choose Your Players screen
- **What I expected**: A Next button to proceed to the next step
- **What happened**: No Next button appears after selecting players — can't advance
- **Severity**: Bug
- **Status**: Fixed
- **Note**: Added bottom action bar (Back/Continue) to all wizard steps via WizardShellComponent. Both player and team registration wizards now have navigation at top and bottom of each step.

### PL-047: Bottom Continue button needs visual separation from the card above
- **Area**: Registration Process Review
- **What I did**: Looked at the new Continue button at the bottom of the Choose Your Players screen
- **What I expected**: Clear spacing or divider between the card content and the bottom button
- **What happened**: Continue button sits too close to the card above — needs separation
- **Severity**: UX
- **Status**: Open

### PL-048: Remove arrow icon from Continue buttons
- **Area**: Registration Process Review
- **What I did**: Noticed arrow icons in both the top and bottom Continue buttons
- **What I expected**: Clean button with just the text "Continue"
- **What happened**: Both Continue buttons have an arrow that isn't needed
- **Severity**: UX
- **Status**: Open

### PL-049: Standardize "Next" vs "Continue" wording across all registration processes
- **Area**: Registration Process Review
- **What I did**: Noticed the button says "Continue" — but other screens may say "Next"
- **What I expected**: Consistent wording throughout all registration flows
- **What happened**: Need to confirm whether we're using "Next" or "Continue" everywhere and standardize
- **Severity**: Question
- **Status**: Open

### PL-050: Add Back/Previous button on Choose Your Players screen
- **Area**: Registration Process Review
- **What I did**: Arrived at the Choose Your Players screen
- **What I expected**: A Back or Previous button to return to the prior step
- **What happened**: No way to go back — only a Continue button forward
- **Severity**: UX
- **Status**: Open

### PL-051: Add trash can icon next to pencil icon to delete players on Choose Your Players card
- **Area**: Registration Process Review
- **What I did**: Looked at player rows on the Choose Your Players card
- **What I expected**: A delete (trash can) icon next to the edit (pencil) icon for each player
- **What happened**: Only a pencil icon is available — no way to delete a player from the list
- **Severity**: UX
- **Status**: Open

### PL-052: Set Player Graduation Year — Back and Continue buttons need stronger hover/selected contrast
- **Area**: Registration Process Review
- **What I did**: Hovered over and clicked the Back and Continue buttons on the Set Player Graduation Year screen
- **What I expected**: Clear visual feedback with strong color contrast on hover and selected states
- **What happened**: Buttons don't change enough visually when hovered or selected — hard to tell they're interactive
- **Severity**: UX
- **Status**: Open

### PL-070: Discount Code section needs better visual emphasis and white input field
- **Area**: Registration Process Review
- **What I did**: Looked at the Discount Code section on the payment screen
- **What I expected**: Discount Code title to stand out (e.g., red or highlighted) and the "Enter Code" input field to be white so it's clearly a data entry field
- **What happened**: Title doesn't stand out enough and the input field blends in with the background — needs a highlighted title (maybe red) and a white input field
- **Severity**: UX
- **Status**: Open

### PL-069: Complete Payment — can't change Refund Protection choice after declining
- **Area**: Registration Process Review
- **What I did**: Declined refund protection coverage, then went Back and clicked Continue to return to the payment screen
- **What I expected**: Ability to change my mind and add coverage before paying
- **What happened**: My decline choice is locked in — no way to reset or change the refund protection selection on the payment screen
- **Severity**: Bug
- **Status**: Open

### PL-068: Complete Payment — what is "Pay in Full" button for before Add Refund Protection?
- **Area**: Registration Process Review
- **What I did**: Arrived at the Complete Payment screen and saw a "Pay in Full" button appearing before the Add Refund Protection option
- **What I expected**: Clear understanding of what "Pay in Full" does at that point in the flow
- **What happened**: Not clear why "Pay in Full" appears before the refund protection option — shouldn't the parent decide on refund protection first?
- **Severity**: Question
- **Status**: Open

### PL-067: "Almost There!" screen — should accounting/fee summary be shown here?
- **Area**: Registration Process Review
- **What I did**: Reviewed the Almost There screen before proceeding to payment
- **What I expected**: Possibly a fee summary or accounting breakdown before the parent commits
- **What happened**: No accounting or fee information shown — should parents see what they owe before continuing?
- **Severity**: Question
- **Status**: Open

### PL-066: "Almost There!" screen — player names as section headers need bolder font weight
- **Area**: Registration Process Review
- **What I did**: Looked at player name headers on the Almost There review screen
- **What I expected**: Player names to stand out clearly as section headers
- **What happened**: Names aren't bold enough — need heavier font weight so they read as headers
- **Severity**: UX
- **Status**: Open

### PL-065: "Almost There!" screen — "Review your details" notes slightly too small
- **Area**: Registration Process Review
- **What I did**: Read the "Review your details" section on the Almost There screen
- **What I expected**: Text large enough to read comfortably without being oversized
- **What happened**: All the detail notes are a bit too small — bump up the font size slightly (not too much, just enough to improve readability)
- **Severity**: UX
- **Status**: Open

### PL-064: "Almost There!" screen — change "F" to "Female" (spell out gender)
- **Area**: Registration Process Review
- **What I did**: Saw gender displayed as "F" on the Almost There review screen
- **What I expected**: Full word "Female" (and presumably "Male" instead of "M")
- **What happened**: Gender shows as a single letter abbreviation — should be spelled out
- **Severity**: UX
- **Status**: Open

### PL-063: "Almost There!" screen — Team selection text needs larger font
- **Area**: Registration Process Review
- **What I did**: Arrived at the "Almost There!" review screen
- **What I expected**: Team selection info displayed prominently
- **What happened**: Team selection text is too small — needs a larger font so it stands out
- **Severity**: UX
- **Status**: Open

### PL-062: Review and Accept Waivers — make "ALL" capitalized and increase font size of intro text
- **Area**: Registration Process Review
- **What I did**: Read the intro text on the waivers screen ("These waivers apply to all selected players...")
- **What I expected**: Prominent, easy-to-read text with emphasis on "ALL"
- **What happened**: Text is too small and "all" is not capitalized — change to "ALL" (caps) and make the entire intro line larger so parents don't miss it
- **Severity**: UX
- **Status**: Open

### PL-061: Review and Accept Waivers — larger player names in a list with individual checkboxes
- **Area**: Registration Process Review
- **What I did**: Arrived at the Review and Accept Waivers screen
- **What I expected**: Player names displayed prominently in a list with a checkbox next to each name for the parent to actively confirm
- **What happened**: Player names are too small and not listed clearly — consider having the parent check a box next to each player's name to acknowledge the waiver for each child individually
- **Severity**: UX
- **Status**: Open

### PL-060: Player Details — white data entry fields with tinted surrounding background for consistency
- **Area**: Registration Process Review
- **What I did**: Looked at the Player Details form styling
- **What I expected**: White input fields on a tinted/shaded background, matching the look of other registration screens
- **What happened**: Fields and background don't have enough contrast between them — make input fields white and the surrounding card background tinted for visual consistency across all registration screens
- **Severity**: UX
- **Status**: Open

### PL-059: Player Details form — increase font size of Team Selected next to Player Name
- **Area**: Registration Process Review
- **What I did**: Looked at the Player Details form heading
- **What I expected**: Team Selected to be prominent and easy to read next to the Player Name
- **What happened**: Team Selected text is too small — needs a bigger font so it stands out
- **Severity**: UX
- **Status**: Open

### PL-058: Player form — move Weight next to Height, make Height optional, move Shorts Size next to T-shirt Size
- **Area**: Registration Process Review
- **What I did**: Filled out the player form for The Players Series: Girls Summer Showcase 2026
- **What I expected**: Related fields grouped together — Height/Weight side by side, Shorts Size/T-shirt Size side by side; Height should be optional
- **What happened**: Weight is not next to Height, Shorts Size is not next to T-shirt Size, and Height is required when it shouldn't be
- **Severity**: UX
- **Status**: Open

### PL-057: USA Lacrosse Number validation — wrap phone number on one line in failed entry popup
- **Area**: Registration Process Review
- **What I did**: Entered an invalid USA Lacrosse Number and got the validation failure popup
- **What I expected**: Phone number displayed fully on one line
- **What happened**: Phone number wraps awkwardly across two lines — needs to stay on a single line
- **Severity**: UX
- **Status**: Open

### PL-056: Choose Your Players — change "Edit Account" to "Edit Family Contact Info"
- **Area**: Registration Process Review
- **What I did**: Saw "Edit Account" link on the Choose Your Players screen
- **What I expected**: Label that clearly describes what you're editing
- **What happened**: "Edit Account" is vague — should say "Edit Family Contact Info" to be specific
- **Severity**: UX
- **Status**: Open

### PL-055: Consider merging Graduation Year and Assign Teams into one screen
- **Area**: Registration Process Review
- **What I did**: Went through Set Player Graduation Year and then Assign Teams as separate steps
- **What I expected**: Possibly a single screen since both are short player setup tasks
- **What happened**: Two separate screens for related info — could these be combined into one step to reduce clicks?
- **Severity**: Question
- **Status**: Open

### PL-054: Assign Teams — use same white background card style as Graduation Year screen
- **Area**: Registration Process Review
- **What I did**: Compared the Assign Teams screen to the Set Player Graduation Year screen
- **What I expected**: Consistent white card background for the team assignment area, matching the grad year selection style
- **What happened**: Assign Teams section doesn't have the same white background treatment — looks inconsistent with the previous screen
- **Severity**: UX
- **Status**: Open

### PL-053: Assign Teams — remove "Capacity shown in dropdown" text
- **Area**: Registration Process Review
- **What I did**: Arrived at the Assign Teams screen
- **What I expected**: Clean screen without unnecessary instructional text
- **What happened**: Text says "Capacity shown in dropdown" — this is obvious from the dropdown itself and should be removed
- **Severity**: UX
- **Status**: Open

### PL-045: Change "Edit details anytime" to "Edit player details"
- **Area**: Registration Process Review
- **What I did**: Saw "Edit details anytime" link/button
- **What I expected**: Clearer label specifying what details
- **What happened**: Label is vague — should say "Edit player details" to be specific
- **Severity**: UX
- **Status**: Open

### PL-044: "Already registered? Locked in" — can this be removed?
- **Area**: Registration Process Review
- **What I did**: Saw "Already registered? Locked in" message on Choose Your Players screen
- **What I expected**: Cleaner screen without unnecessary messaging
- **What happened**: Not clear if this message is needed — consider removing it
- **Severity**: Question
- **Status**: Open

### PL-043: "Choose Your Players" screen — add Previous/Next buttons at bottom
- **Area**: Registration Process Review
- **What I did**: Arrived at the "Choose Your Players" screen
- **What I expected**: Previous and Next buttons at the bottom to navigate between wizard screens
- **What happened**: No navigation buttons at the bottom of the screen
- **Severity**: UX
- **Status**: Open

### PL-042: Player Registration card — highlight Family Account more prominently
- **Area**: Registration Process Review
- **What I did**: Looked at the top card in the Player Registration flow
- **What I expected**: Family Account info to be prominent since it's key context for the registration
- **What happened**: Family Account not highlighted enough — consider making it more visible
- **Severity**: UX
- **Status**: Open

### PL-041: Family Account card is a DEAD END — no Previous or Next button
- **Area**: Family Account Setup
- **What I did**: Arrived at the Family Account card after choosing New Family Account
- **What I expected**: A "Next" button to proceed and a "Previous" button to go back (e.g., "I already have an account")
- **What happened**: No way to proceed or go back — complete dead end
- **Severity**: Bug
- **Status**: Open

### PL-040: "Family Account" header should say "Create Family Account" for new registrations
- **Area**: Family Account Setup
- **What I did**: Clicked "New Family Account" from the registration flow
- **What I expected**: Header to say "Create Family Account" to match the action
- **What happened**: Header just says "Family Account" — should be clearer for new parents that they're creating one
- **Severity**: UX
- **Status**: Open

### PL-039: Navigation for new families — bulletins/text need work
- **Area**: Family Account Setup
- **What I did**: Navigated the site as a new family would
- **What I expected**: Clear, helpful bulletins and text guiding new families
- **What happened**: Bulletins and text content need work — more details to follow
- **Severity**: UX
- **Status**: Open

### PL-038: Customer/job icon at top should navigate to job home screen
- **Area**: Family Account Setup
- **What I did**: Clicked the customer:job icon at the top of the page
- **What I expected**: Navigate to the home screen for that job
- **What happened**: Doesn't bring me to the job home screen
- **Severity**: UX
- **Status**: Open

### PL-037: Login button — remove Palette, change to "Login" label, and offer new family account option
- **Area**: Family Account Setup
- **What I did**: Looked at top-right login area as a new parent would
- **What I expected**: A clear "Login" button (not a people icon dropdown), no Palette option visible, and an option to create a new family account for first-time parents
- **What happened**: Shows a people icon with dropdown and Palette option — not intuitive for new parents who don't have an account yet
- **Severity**: UX
- **Status**: Open

### PL-035: Keyword Pairs — add explanatory note so users understand what this is for
- **Area**: LADT Setup
- **What I did**: Saw "Keyword Pairs" in Team Details
- **What I expected**: A brief explanation or tooltip describing what Keyword Pairs are used for
- **What happened**: No context — not clear what this feature does or when to use it
- **Severity**: UX
- **Status**: Open

### PL-034: Eligibility section — Level of Play isn't set here, is it?
- **Area**: LADT Setup
- **What I did**: Looked at Eligibility settings in Team Details
- **What I expected**: Level of Play to be configured here or clear indication of where it's set
- **What happened**: Not clear if Level of Play is actually set in this section — need to confirm
- **Severity**: Question
- **Status**: Open

### PL-033: Review Override cards — is "Club Rep Fee Override" really "Team Fee Override"?
- **Area**: LADT Setup
- **What I did**: Reviewed the Override cards in Team Details
- **What I expected**: Clear, accurate labels for each override
- **What happened**: "Club Rep Fee Override" may be mislabeled — should it be "Team Fee Override"? Need to review all override cards for clarity
- **Severity**: Question
- **Status**: Open

### PL-032: Team Details — should Dates section come before Overrides since dates are always used?
- **Area**: LADT Setup
- **What I did**: Looked at the order of sections in Team Details
- **What I expected**: Most frequently used sections first
- **What happened**: Dates section appears after Overrides — since dates are always used, they should come first
- **Severity**: UX
- **Status**: Open

### PL-031: Review Self Rostering and Hide Roster radio buttons — which job types do they apply to?
- **Area**: LADT Setup
- **What I did**: Noticed Self Rostering and Hide Roster radio buttons in Team Details
- **What I expected**: Clear understanding of which job types these settings apply to
- **What happened**: Need to review whether these are relevant for all job types or only specific ones
- **Severity**: Question
- **Status**: Open

### PL-030: Team Details — "More Actions" button is empty, is it needed?
- **Area**: LADT Setup
- **What I did**: Clicked "More Actions" button on Team Details
- **What I expected**: A dropdown with additional options
- **What happened**: No options appear — button seems unnecessary if nothing is under it
- **Severity**: Question
- **Status**: Open

### PL-029: Team Details — Max Rostered should be right next to # Players Registered
- **Area**: LADT Setup
- **What I did**: Looked at Team Details table columns
- **What I expected**: Max Rostered column adjacent to # Players Registered for easy comparison
- **What happened**: These columns are separated — should be next to each other
- **Severity**: UX
- **Status**: Open

### PL-028: Team Details table — reorder and narrow columns so important data is visible on first screen
- **Area**: LADT Setup
- **What I did**: Opened the Team Details table
- **What I expected**: Most important columns visible without scrolling
- **What happened**: Too many wide columns push important data off-screen to the right — need to reorder columns by priority and narrow widths
- **Severity**: UX
- **Status**: Open

### PL-027: Trash can icons — do they appear at higher levels (Division, etc.) when empty?
- **Area**: LADT Setup
- **What I did**: Noticed trash can icons at the Team level next to pencil icon
- **What I expected**: Trash cans also available at higher levels (e.g., Division) when no items underneath
- **What happened**: Not sure if delete is available at higher levels when they have no children — need to verify
- **Severity**: Question
- **Status**: Open

### PL-026: Teams table — change "in" to "under" and improve L/A/D hover text
- **Area**: LADT Setup
- **What I did**: Looked at Teams table header and the L, A, D navigation links
- **What I expected**: "Teams under [name]" wording; hover text like "Navigate to League: [name]"
- **What happened**: Says "in" instead of "under"; L/A/D hover text doesn't explain what they navigate to
- **Severity**: UX
- **Status**: Open

### PL-025: LADT tree — add "Teams" and "Players" column headers with numbers centered below
- **Area**: LADT Setup
- **What I did**: Looked at the LADT tree counts
- **What I expected**: Clear column headers labeling what the numbers represent
- **What happened**: Team and player counts show in the tree but no headers — add "Teams" and "Players" headers with numbers centered below them
- **Severity**: UX
- **Status**: Open

### PL-024: "Add New Division" button — wrong placement and adds a Team instead
- **Area**: LADT Setup
- **What I did**: Clicked "Add New Division" button in the Divisions table
- **What I expected**: A new Division to be added
- **What happened**: Button needs a better location, and functionally it adds a Team prompt in the tree under the last Division listed instead of adding a Division
- **Severity**: Bug
- **Status**: Open

### PL-023: Divisions table — move up/down buttons under Division column and remove Fees column
- **Area**: LADT Setup
- **What I did**: Looked at the Divisions table layout
- **What I expected**: Up/down buttons grouped with Division name; only relevant columns shown
- **What happened**: Up/down buttons are separate from Division column, and Fees column is present but may not be needed here
- **Severity**: UX
- **Status**: Open

### PL-022: League table — add down-arrow button to navigate to Age Groups for consistency
- **Area**: LADT Setup
- **What I did**: Looked at the League table
- **What I expected**: A down-arrow button to navigate to the Age Groups level, consistent with other table navigation
- **What happened**: No down-arrow navigation button at the League table level
- **Severity**: UX
- **Status**: Open

### PL-021: Confirm Sort Age is no longer needed for any functional reasons
- **Area**: LADT Setup
- **What I did**: Noticed Sort Age field in Age Group settings
- **What I expected**: Understanding of whether this field still serves a purpose
- **What happened**: Need Todd to confirm if Sort Age is still used anywhere or can be removed
- **Severity**: Question
- **Status**: Open

### PL-020: Reminder — test Early Bird and other accounting functions in future
- **Area**: LADT Setup
- **What I did**: Noticed accounting-related settings (Early Bird, etc.) in Age Group Details
- **What I expected**: N/A — future testing reminder
- **What happened**: N/A — need to circle back and test these accounting functions later
- **Severity**: Question
- **Status**: Open

### PL-019: Age Group Details fly-in — Early Bird dropdown truncates option labels
- **Area**: LADT Setup
- **What I did**: Opened Age Group Details fly-in and clicked the Early Bird dropdown
- **What I expected**: Full option labels visible in the dropdown
- **What happened**: Option labels are cut off — dropdown needs to be wide enough to show entire text
- **Severity**: UX
- **Status**: Open

### PL-018: AG SET button redundant — same as Edit icon
- **Area**: LADT Setup
- **What I did**: Clicked the AG SET button on an Age Group row
- **What I expected**: Different functionality from the Edit icon
- **What happened**: Takes you to the same place as the Edit icon — may not be needed
- **Severity**: Question
- **Status**: Open

### PL-017: Up/down nav buttons confusing next to edit buttons; simplify Age Group table
- **Area**: LADT Setup
- **What I did**: Looked at the Age Group table row actions
- **What I expected**: Clear separation of actions; no redundant data
- **What happened**: Navigate up/down buttons next to edit buttons is confusing — maybe move them under the Age Group column. Also consider removing team and player number columns since they already appear in the tree on the left
- **Severity**: UX
- **Status**: Open

### PL-016: Consider new placement for "Add New Age Group" button
- **Area**: LADT Setup
- **What I did**: Used the "Add New Age Group" button
- **What I expected**: Button in a more intuitive location
- **What happened**: Current placement could be improved for better discoverability / workflow
- **Severity**: UX
- **Status**: Open

### PL-015: "Add New Age Group" button adds a Division instead
- **Area**: LADT Setup
- **What I did**: Clicked "Add New Age Group" button in the Age Groups table
- **What I expected**: A new Age Group to be added in the tree
- **What happened**: A new Division was added in the tree instead
- **Severity**: Bug
- **Status**: Open

### PL-014: Table header — change "AgeGroups in [League]" to "AgeGroups under [League]"
- **Area**: LADT Setup
- **What I did**: Opened an Age Group table and read the header
- **What I expected**: Wording that reinforces the tree hierarchy
- **What happened**: Header says "AgeGroups in [League name]" — should say "under" instead of "in" to reinforce the tree concept
- **Severity**: UX
- **Status**: Open

### PL-013: Add labeled bar above data table to show which level you're editing
- **Area**: LADT Setup
- **What I did**: Clicked on a tree item to open its data table
- **What I expected**: A bar above the table with a centered label ("League", "Age Group", etc.) so I know what level I'm looking at
- **What happened**: No label bar — easy to lose track of which level the table is showing
- **Severity**: UX
- **Status**: Open

### PL-012: Age Group table — columns too wide, right-hand columns not visible
- **Area**: LADT Setup
- **What I did**: Opened an Age Group table
- **What I expected**: All columns visible without horizontal scrolling, or scroll bar directly below table
- **What happened**: Columns are too wide so right-hand columns are cut off; if a scroll bar is needed, it should be placed directly below the table
- **Severity**: UX
- **Status**: Open

### PL-071: Confirm Registration Payment + Insurance popup — standardize font size and replace icons with bullets
- **Area**: Registration Process Review
- **What I did**: Opened the Confirm Registration Payment + Insurance popup
- **What I expected**: All text items in the same (larger) font size, with simple bullet points instead of icons
- **What happened**: Mixed font sizes and icons used instead of bullets — needs uniform larger font and plain bullet list
- **Severity**: UX
- **Status**: Open

### PL-072: Most Recent Transaction(s) only shows one payment after paying for two players
- **Area**: Registration Process Review
- **What I did**: Paid for two players in a family account
- **What I expected**: Both payments to appear under Most Recent Transaction(s) on the Family Players Table
- **What happened**: Family Players Table shows both players, but Most Recent Transaction(s) only shows one payment
- **Severity**: Bug
- **Status**: Open

### PL-073: Registration Complete Confirmation page needs cleaner layout with distinct card areas
- **Area**: Registration Process Review
- **What I did**: Completed registration and viewed the confirmation page
- **What I expected**: Crisp, well-organized layout — 3 tables should stand out clearly, organization info in its own card, waiver info in its own card
- **What happened**: Page feels cluttered — tables don't stand out, organization information and waiver content are not separated into their own distinct card areas
- **Severity**: UX
- **Status**: Open

### PL-074: Waiver area on confirmation shows "BY CLICKING NEXT BELOW, I AGREE..." — confuses parents
- **Area**: Registration Process Review
- **What I did**: Completed registration and saw the waiver section on the confirmation page
- **What I expected**: Clear indication that the waiver was already accepted during registration — no action needed
- **What happened**: Text says "BY CLICKING NEXT BELOW, I AGREE WITH THE ABOVE RELEASE OF LIABILITY" which makes parents think they need to do something else. Either remove this text or add a clarifying note outside the waiver card (e.g., "Waiver accepted during registration")
- **Severity**: UX
- **Status**: Open

### PL-075: After finishing registration and logging back in — no menus or info to review/edit
- **Area**: Registration Process Review
- **What I did**: Finished registration (got logged out), logged back in, and selected one of my registration options
- **What I expected**: Menus and information available to review or edit my registration details
- **What happened**: After selecting a registration, there are no menus or information shown — nothing to review or edit
- **Severity**: Bug
- **Status**: Open

### PL-076: Player Details form missing Academic Honors and Athletic Honors/Awards fields
- **Area**: Registration Process Review
- **What I did**: Registered for The Players Series: Girls Summer Showcase 2026 and looked at the Player Details form
- **What I expected**: Academic Honors and Athletic Honors/Awards text entry fields to appear, as they do in the Legacy system
- **What happened**: Both fields are missing from the new Player Details form — they exist in Legacy but aren't showing up here
- **Severity**: Bug
- **Status**: Open

### PL-077: Standardize how optional fields are indicated across all forms
- **Area**: Registration Process Review
- **What I did**: Looked at optional fields across registration forms
- **What I expected**: Consistent treatment — either "(OPTIONAL)" after the label or placeholder text like "Leave blank if unknown" inside the field
- **What happened**: No consistent pattern for marking optional fields — need to pick one approach and apply it everywhere
- **Severity**: UX
- **Status**: Open

### PL-078: "Click Here to Begin" bulletin only goes to Adult Registration — consider splitting Player and Coach paths
- **Area**: Registration Process Review
- **What I did**: Clicked "Click Here to Begin a Player or Coach registration and waiver" on the Player Self-Rostering page
- **What I expected**: Option to choose between Player registration and Coach registration
- **What happened**: Only brings me to Adult Registration — no way to go to Player registration. Does it make sense to split the bulletin into separate links for Player and Coach paths?
- **Severity**: Question
- **Status**: Open

### PL-079: Public Rosters "Click Here to view currently rostered players" leads to 404 error
- **Area**: Registration Process Review
- **What I did**: Clicked "Click Here to view currently rostered players" on the Public Rosters page
- **What I expected**: A page showing the currently rostered players
- **What happened**: Screen shows a 404 error
- **Severity**: Bug
- **Status**: Open

### PL-080: Assign Teams dropdown on Players ARB site no longer shows cost per option
- **Area**: Registration Process Review
- **What I did**: Opened the Assign Teams dropdown on the Players ARB site
- **What I expected**: Cost of each team option shown in parentheses next to the name, like it was in Legacy
- **What happened**: Cost is no longer displayed in the dropdown — should we add it back?
- **Severity**: Question
- **Status**: Open

