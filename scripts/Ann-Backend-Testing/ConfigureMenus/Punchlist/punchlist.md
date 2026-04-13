# Configure Menus - Punch List

**Tester:** Ann
**Date Started:** 2026-04-09
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

- [ ] **Menu Editor** -- Adding, editing, removing, and reordering menu items
- [ ] **Menu Display** -- How menus appear to different roles and on different screens
- [ ] **Menu Permissions** -- Role-based visibility and access control for menu items
- [ ] **Navigation** -- Links work correctly, proper routing, no dead ends
- [ ] **Mobile Menus** -- Responsive behavior, hamburger menu, touch targets

---

## Punch List Items

### PL-020: Profile Editor placement — should Directors see it under Job Settings / Player Registration?
- **Area**: Menu Editor
- **What I did**: Looked at where Profile Editor currently lives
- **What I expected**: Easy access for Directors in the context where they'd use it
- **What happened**: Profile Editor is currently under Tools. Some of that info would be helpful to Directors under Job Settings > Player Registration subheader. Let's discuss placement and what Directors should see.
- **Severity**: Question
- **Status**: Open

### PL-019: "Configure Dropdown Options" — already a submenu under Job Configuration, remove from root level
- **Area**: Menu Editor
- **What I did**: Noticed "Configure Dropdown Options" appears at the root Configure level
- **What I expected**: It to only appear as a submenu under Job Configuration since it's already there
- **What happened**: It's in both places — don't think it's needed at the root level
- **Severity**: UX
- **Status**: Open

### PL-018: Timezone dropdown missing Arizona Time
- **Area**: Menu Editor
- **What I did**: Looked through the timezone dropdown options when editing a customer
- **What I expected**: Arizona Time to be available (Arizona doesn't observe DST, so it's a distinct timezone)
- **What happened**: No Arizona Time option in the dropdown
- **Severity**: Bug
- **Status**: Open

### PL-017: Edit Customer — Timezone popup shows "Afghanistan Time" but table shows "Eastern Time"
- **Area**: Menu Editor
- **What I did**: Edited a customer and looked at the Timezone field in the popup
- **What I expected**: Timezone in the popup to match what the table shows (Eastern Time)
- **What happened**: Popup shows "Afghanistan Time" but the table reads "Eastern Time" — something is wrong with the timezone field defaulting or display
- **Severity**: Bug
- **Status**: Open

### PL-016: Customers — split into "Active Customers" and "Inactive Customers" tables
- **Area**: Menu Display
- **What I did**: Looked at the Customers table
- **What I expected**: Easy way to distinguish active from inactive customers
- **What happened**: Customers with 0 Jobs are effectively inactive but mixed in with active ones — creates noise. Split into two tables: "Active Customers" at the top and "Inactive Customers" below to clean it up.
- **Severity**: UX
- **Status**: Open

### PL-015: Customers table — all show Eastern Time even for non-Eastern customers. Is time zone needed?
- **Area**: Menu Display
- **What I did**: Looked at the Customers table entries
- **What I expected**: Correct time zones per customer, or no time zone if not needed
- **What happened**: All customers show "Eastern Time" or "US Eastern Time" — even YJ Midwest. Is the time zone field needed at all?
- **Severity**: Question
- **Status**: Open

### PL-014: Configure Customers — reduce heading size and rename from "Customer Configure" to "Customers"
- **Area**: Menu Display
- **What I did**: Opened Configure Customers page
- **What I expected**: Heading sized consistently with other pages, labeled simply "Customers"
- **What happened**: Heading is too large (same issue as PL-007) and says "Customer Configure" — should just say "Customers"
- **Severity**: UX
- **Status**: Open

### PL-013: Customer Groups — Add and Delete buttons too far from customer names in right table
- **Area**: Menu Display
- **What I did**: Looked at the Add and Delete functions next to the customer names in the right table
- **What I expected**: Buttons positioned close to the customer names
- **What happened**: Too much space between the buttons and the customer names — move them much closer
- **Severity**: UX
- **Status**: Open

### PL-012: Customer Groups — "Members of [group name]" header needs visual emphasis
- **Area**: Menu Display
- **What I did**: Clicked into a Customer Group and saw the header "Members of 'STEPS Lacrosse LLC'"
- **What I expected**: The group name to stand out visually — highlighted, bold, or styled differently
- **What happened**: Header doesn't stand out enough — needs some kind of highlighting so the group name is clearly visible
- **Severity**: UX
- **Status**: Open

### PL-011: Customer Groups — remove total group count, add column heading like "Member Jobs" or "Jobs Included"
- **Area**: Menu Display
- **What I did**: Looked at the Customer Groups table
- **What I expected**: A meaningful column heading above the job count numbers
- **What happened**: Shows total number of groups (currently 5) which isn't useful. Remove it and instead add a heading above the numbers in each row, like "Member Jobs" or "Jobs Included", so it's clear what the numbers represent.
- **Severity**: UX
- **Status**: Open

### PL-010: All tables under Configure use too much space — compress to match Legacy sizing
- **Area**: Menu Display
- **What I did**: Looked at tables across all Configure menu pages
- **What I expected**: Compact tables like Legacy
- **What happened**: All tables under Configure have too much whitespace — rows, columns, and overall spacing should be much smaller. Legacy tables are a good reference for sizing.
- **Severity**: UX
- **Status**: Open

### PL-009: Customer Groups — change subtitle to "Organize customers into named groups for Customer/Job Revenue reporting"
- **Area**: Menu Display
- **What I did**: Read the Customer Groups subtitle under Configure
- **What I expected**: Description that specifies what the reporting is for
- **What happened**: Says "Organize customers into named groups for reporting" — should say "Organize customers into named groups for Customer/Job Revenue reporting"
- **Severity**: UX
- **Status**: Open

### PL-008: Age Ranges menu — hide it but keep the function available for future clients?
- **Area**: Menu Editor
- **What I did**: Noticed the Age Ranges menu item under Configure
- **What I expected**: Only menus relevant to current customers
- **What happened**: Age Ranges isn't used by any current customers. Should we hide this menu but keep the function available in case a new client in a new sports category needs it?
- **Severity**: Question
- **Status**: Open

### PL-007: Under Configure, all menu page headings should match "Customer Groups" heading size — many are too large
- **Area**: Menu Display
- **What I did**: Browsed through menu items under Configure
- **What I expected**: All page headings the same font size
- **What happened**: Many headings are too large — should all match the size used for "Customer Groups"
- **Severity**: UX
- **Status**: Open

### PL-006: Add Administrator — how does someone become eligible in the Username search? Dropdown options seem random.
- **Area**: Menu Editor
- **What I did**: Clicked "Add Administrator" and looked at the Username search dropdown
- **What I expected**: Clear list of eligible users, or understanding of how someone becomes eligible to be added
- **What happened**: Dropdown seems to have random options — not clear how a person registers or qualifies to appear in the Username search
- **Severity**: Question
- **Status**: Open

### PL-005: Star icons — move before names, clarify default contact, and confirm job clone behavior
- **Area**: Menu Display
- **What I did**: Looked at the star icons used to set primary contact in the Administrators table
- **What I expected**: Stars positioned before the name for easier scanning; clear understanding of default behavior
- **What happened**: Three items: (1) Consider moving star icons to the left of the name, (2) Who is the default contact if none is selected? (3) Will the selected Director carry forward when cloning a job?
- **Severity**: Question
- **Status**: Open

### PL-004: Administrators table — too much spacing, compress rows and columns
- **Area**: Menu Display
- **What I did**: Looked at the overall Administrators table layout
- **What I expected**: Compact table with items close together
- **What happened**: Table has too much whitespace — rows and columns can be much smaller and tighter overall
- **Severity**: UX
- **Status**: Open

### PL-003: Administrators table — use alternating row colors like Search Player table
- **Area**: Menu Display
- **What I did**: Looked at the Administrators table rows
- **What I expected**: Alternating row colors to make rows easy to distinguish, like the Search Player table
- **What happened**: No alternating colors — rows blend together
- **Severity**: UX
- **Status**: Open

### PL-002: "Administrators" heading too large — match "Search Registrations" header font/size
- **Area**: Menu Display
- **What I did**: Looked at the "Administrators" page heading
- **What I expected**: Same font and size as the "Search Registrations" header
- **What happened**: Heading is too large — should be consistent with other page headers
- **Severity**: UX
- **Status**: Open

### PL-001: Administrators table — match Search/Player table style and reorder columns
- **Area**: Menu Display
- **What I did**: Opened the Administrators menu and looked at the table
- **What I expected**: Consistent look with the Search/Player menu table
- **What happened**: Needs several changes: (1) Match column heading font and style to Search/Player table, (2) Change "Status" to "Active" and move it right after the Name column with "Yes" if active, (3) Column order should be: Name, Active, Role, Username, Registered
- **Severity**: UX
- **Status**: Open
