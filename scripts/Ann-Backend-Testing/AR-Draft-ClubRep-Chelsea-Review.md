# DRAFT — Club Rep Registration review with Chelsea (2026-09-30)

**Working file, not the punchlist.** Source: `AR Items for Team Reg Review with Chelsea.docx`.
Draft IDs `D-01…` are temporary and carry no order — **AR numbers get assigned when these are
committed into `After-Release-Punchlist.md`**, so we can reorder freely until then.

**None of these are researched.** Every entry is the report as given, grouped and tightened — no
code opened, no cause proposed, no feasibility judged.

**How to use this:** walk it one item at a time. Edit the wording in place, delete what should not
be filed, and say when an item is ready. Items marked ❓ need a decision before they can be filed
as written.

**~30 notes → 32 items.** What was merged is named under each item, so nothing is silently lost.

---

## Where each group stands — as of 2026-09-30

| Group | Draft<br>items | AR items<br>filed | Which AR numbers | Status |
|:--|--:|--:|:--|:--|
| **A** · Confirmation screens | 4 | 2 | AR-119, AR-120 | ✅ done |
| **B** · Registration Links and badges | 3 | 0 | *folded into AR-121* | ✅ done |
| **C** · Team counts | 1 | 1 | AR-121 *(covers B too)* | ✅ done |
| **D** · Club Team Library | 10 | 0 | — | 🅿 parked — UX replaced |
| **E** · Registered Teams | 5 | 0 | — | 🅿 parked — UX replaced |
| **F** · No role selected | 1 | 0 | — | ⏳ on hold |
| **G** · Payment screen | 2 | 3 | AR-122, AR-123, AR-124 | ✅ done |
| **H** · Search | 2 | 2 | AR-118, AR-125 | ✅ done |
| **I** · Bugs and permissions | 4 | 0 | — | ⏳ to do |
| **TOTAL** | **32** | **8** | **AR-118 … AR-125** | |

⚠ **Two groups do not map one-to-one, by decision:** **B and C collapsed into a single item** (seven
draft items → AR-121 with five checks, because removing the badge content is what makes the
bulletin labels and the counts go with it), and **G produced more items than it started with** (the
overpayment question came out of D-25 as its own item).

<div style="page-break-after: always;"></div>

## Every item and where it landed

| D | Item | Status |
|:--|:--|:--|
| | **A · Confirmation screens** | |
| D-01 | Team name = Age Group + Club + Team Name, both tables | ✅ **AR-120** (part 1) |
| D-02 | Summary table — totals row only for Owed | ✅ **AR-120** (part 2) |
| D-03 | Most Recent Transactions — show the time | ✅ **AR-120** (part 3) |
| D-04 | Paid total wrong with a Discount or Correction | ✅ **AR-119** |
| | **B · Registration Links and badges** | |
| D-05 | Badges back to previous content | ✅ **AR-121** (check 1) |
| D-06 | Badge size stays one third | ✅ **AR-121** (check 2) |
| D-07 | Bulletin labels wrong — remove | ✅ **AR-121** (check 3) |
| | **C · Team counts** | |
| D-08 | Roles list count removed; fly-in count **corrected** | ✅ **AR-121** (checks 4–5) |
| | **D · Club Team Library** | 🅿 **not filed — UX replaced** |
| D-09 | Remove "Currently in" | 🅿 |
| D-10 | Library reachable with no active role | 🅿 *recheck — capability* |
| D-11 | Grad Year / LOP should not display | 🅿 |
| D-12 | Archive model | 🅿 |
| D-13 | Duplicate team names allowed | 🅿 *recheck — capability* |
| D-14 | Age group for teams that have none | 🅿 *recheck — capability* |
| D-15 | Colour circles on Registered Teams only | 🅿 |
| D-16 | Register card — register fields only | 🅿 |
| D-17 | Required-field treatment on Age Group / LOP | 🅿 |
| D-18 | Dropped teams in the Library | 🅿 |
| | **E · Registered Teams** | 🅿 **not filed — UX replaced** |
| D-19 | Undo → Delete Registration | 🅿 |
| D-20 | Renaming does not move age group | 🅿 |
| D-21 | Cancel / Save Changes; "Level of Play" | 🅿 |
| D-22 | Empty state needs the customer name | 🅿 |
| D-23 | Column order and wrapping mirror the Library | 🅿 |
| | **F · No role selected** | |
| D-24 | Account menu available with no role | ⏳ **on hold** |
| | **G · Payment screen** | |
| D-25 | Team table shows at $0, payment card does not | ✅ **AR-123** |
| D-26 | Columns and accounting figures | ✅ **AR-122** |
| — | Overpayment / negative amount due | ✅ **AR-124** *(split out of D-25)* |
| | **H · Search** | |
| D-27 | Remove summary amounts (Registrations + Teams) | ✅ **AR-125** |
| D-28 | Inactive teams vanish with the Club filter | ✅ **AR-118** |
| | **I · Bugs and permissions** | |
| D-29 | Dropped team returns to Library, re-registers | ⏳ *check against new UX first* |
| D-30 | 60-minute delete window after Delete is off | ⏳ |
| D-31 | Closed functions disabled with a reason | ⏳ |
| D-32 | SuperUser can create a club rep registration | ⏳ *needs defect-or-policy* |

⚠ **These two tables are maintained by hand — they are only as current as the last item filed.**

<div style="page-break-after: always;"></div>

---

## A · Confirmation screens (email and online)

### D-01 · ✅ FILED IN AR-120 (09-30, part 1) · Team name format on both confirmation tables
Team name renders as **Age Group + Team Name**; it needs **Age Group + Club + Team Name**. Without
the club, teams read as `2030 2030` or `2030 2030 Blue` instead of `2030 Test 2030 Blue`. Occurs
in **both tables**.

### D-02 · ✅ FILED IN AR-120 (09-30, part 2) · Registered Teams Summary — drop all totals except Owed
Remove the totals row for every column **except Owed**. Total Owed can move to the second column,
which also frees the width needed for the longer team name in D-01.

### D-03 · ✅ FILED IN AR-120 (09-30, part 3) · Most Recent Transactions — show the time
The Date column needs to show **time as well as date**.

### D-04 · ✅ FILED AS AR-119 (09-30) · Paid total is wrong when a Discount or Correction is applied
The confirmation screen's **Paid total does not incorporate the fee adjustment**, so the figure
shown is incorrect. A separate Fee-Adjust column is **not** wanted here — the total itself needs
to be right.

---

## B · Registration Links and badges (logged in as Club Rep)

### D-05 · ✅ FILED AS AR-121 (09-30, with D-06, D-07, D-08) · Badges return to their previous content
Only **"Team Registration"** and **"Balance Due"** belong in the badges. Remove: the number of
teams registered; the sub-labels under Team Registration (*"Register more, rename, review fees"*);
and the amount due under Pay Balance Due — which is also variable once processing fees are added.

### D-06 · ✅ FILED IN AR-121 (09-30, check 2) · Badge size stays static
Badges must stay the size they were **before logging in** — one third size — rather than growing.

### D-07 · ✅ FILED IN AR-121 (09-30, check 3) · Registration Links bulletin labels are wrong
The bulletin's labels misstate the **number of teams** and the **amount owed**. Remove them.

---

## C · Team counts appear in three places and are wrong in all of them

### D-08 · ✅ FILED IN AR-121 (09-30, checks 4–5) · Counts: removed from the Roles list, CORRECTED in the fly-in
Counts appear in the **Registration Links bulletin**, the **Club Rep Roles list**, and the
**Club Rep teams fly-in**. The fly-in number is **wrong when teams have been dropped** — it counts
Waitlisted and Dropped teams. In all three places the count is misleading and does not belong
there.
*(Merged from three separate notes. Overlaps D-05 and D-07 — one of them should be the parent and
the others cross-referenced.)*

---

## D · Club Team Library — 🅿 NOT FILED (Ann, 09-30)

> **Todd has completely changed the UX for this area, so these ten reports are against a screen that
> no longer exists.** Left in the draft as a record of what was seen, not carried into the punchlist.
> **Re-check them against the new UX before discarding** — three of them below are capability asks
> rather than layout, and a redesign does not automatically deliver those.

### D-09 · Remove "Currently in" from the Library
This is the **library only** — the registration context does not belong on it.

### D-10 · The Library must be reachable without an active role ❓
A club rep with **no role selected** — or with no active registrations, because they expired —
cannot reach the Library at all. Club reps need to **create an account and build their library
outside an event**; without that they are at a significant disadvantage when registration opens.
*(Related to D-24, the same lockout on the account menu. File as one item with two parts, or as
two linked items.)*

### D-11 · Grad Year and Level of Play should not display in the Library
They have **no value once a team is in Registered Teams** and no relationship to it — they matter
**before** registration only.

### D-12 · Archive model for the Library
One coherent change, assembled from four notes:
- Move the **pencil icon before the team name**.
- Add an **archive** action (trash-can icon) with a **"Delete team?"** confirm.
- **Archive is restorable.** Archived teams show **Level of Play** and keep the **edit pencil**, so
  a team can be edited while archived and then restored.
- **Permanent delete happens from the Archive only** — past registrations no longer matter there.
- The **Archive is collapsed** by default; only available teams show in the Library.

❓ One line reads *"Can't archive a team if it has a trash can"* — the rule it states is unclear.

### D-13 · Duplicate team names must be allowed in the Library
Clubs **reuse one team name across different age groups** routinely. Creating a team with a name
that already exists needs to work.

### D-14 · Age group for teams that don't have one ❓
**High school teams have no single age group**, so there is nothing correct to select in the
Library. Options raised: an **N/A** choice, or making the field **optional**.
*(Split from D-13 — a different question, likely ruled on separately.)*

### D-15 · Colour circles belong on Registered Teams, not the Library
The small colour circle should appear on the **Registered Teams table only**. On the Library table
it **pulls attention and distracts**.

### D-16 · Register card shows only the register fields
On the register card, **remove the arrow**, and remove the **pencil** and **archive/trash** icons —
only the fields needed to register should be there.

### D-17 · Age Group and Level of Play need the required-field treatment on registration
*"Select the LOP and Age Group for this team"* needs the **yellow border and yellow icon**, **even
when it autofills**. When it does autofill, a **down arrow** must show that other options exist.
Yellow outlines apply to autofilled values too.

### D-18 · Dropped teams in the Library ❓
A heading with nothing under it — *"Dropped Teams handling in library…"*. It sits beside D-29, the
dropped-team bug. Needs content, or it folds into D-29.

---

## E · Registered Teams (event side) — 🅿 NOT FILED (Ann, 09-30)

> **Superseded by Todd's UX change, same as group D.** Kept as a record; not filed.

### D-19 · "Undo Registration" becomes "Delete Registration"
Rename to **Delete Registration**, as a **red Delete button**, permitted **only when the team has
no accounting activity**. The same wording applies to the **age-group toast**, which currently says
*"Undo this registration and register it again in the correct age group"* — that hyperlink should
read **Delete**.

### D-20 · Renaming does not move a team between age groups — confirm the wording
Editing a registered team does **not** change its age group; renaming does not move it. The toast
that explains this is the one being reworded in D-19.
❓ The note opens as a question — *"Did edit of a registered team allow change in agegroups?"* If
the **behaviour** should change rather than the wording, that is a separate item.

### D-21 · Edit buttons should read Cancel and Save Changes
Not *"Rename…"*. The note asks directly: **why not Save? what is the difference?** Also
**"Level of Play"** in any edit position — not **"Level"**.

### D-22 · Empty-state text needs the customer name
*"Press Register on a library team to register it for [Job Name]"* — add the **Customer Name
before the Job Name**.

### D-23 · Column order and wrapping should mirror the Library
**Team name must not wrap**; column order (Team, Age Group, …) should match the Library.
❓ Open question from the notes: *"Should Registered Teams in the Library be collapsed to avoid
confusion?"*

---

## F · Access when no role is selected

### D-24 · Account-maintenance menu items must be available with no role selected
**Whatever the account type**, the menu items to update an account should be reachable when no role
has been selected. An account with no active registrations is either a **new registrant** or one
whose registrations have **expired** — neither should be locked out.
*(Pairs with D-10.)*

---

## G · Payment screen

### D-25 · ✅ FILED AS AR-123 (09-30) · Payment screen when nothing is due
The team table is **critical for review and must show for every registrant**, including when the
amount owed is **$0** — but the **Credit Card Information card and payment fields should not**
appear for this subset of registrants.

✅ **Split into its own item — AR-124 (09-30):** overpayment is
not shown and should appear as a **negative amount due**, plus the open question **how are overpaid
amounts handled at the payment level?**

### D-26 · ✅ FILED AS AR-122 (09-30) · Payment screen columns and accounting figures
- Club reps in the **Deposit phase**: add a column where **Balance Due** sits in the Final Balance
  phase, labelled **Additional Fees**.
- Move **Reg Date to the last column** — it is lost behind the horizontal scroll, and moving it
  lets longer team names show.
- **Accounting table**: **Deposit Due** and **Balance Due** should each show the **fee base from
  LADT**, and **Processing Fees** should show the amount owed **for the payment phase in play**.
- **Paid column total** renders blue; it should be **green**.

---

## H · Search

### D-27 · ✅ FILED AS AR-125 (09-30) · Search / Registrations + Teams — remove the summary amounts
Many head directors **do not want subdirectors seeing these figures** and are asking for removal.
**Customer Job Revenue exists for this purpose and is SuperDirector-only.** Also, the **sum of paid
amounts includes Correction records** — adjustments, not revenue — **so the figure is misleading.**
*(Scope widened 09-30: Teams as well as Registrations.)*

### D-28 · ✅ FILED AS AR-118 (09-30) · BUG · Inactive teams disappear depending on how the filter is set
Search / Teams: selecting **Club**, then **Inactive/Active** or just **Inactive**, the inactive
(dropped) teams **do not show**.

---

## I · Bugs and permissions

### D-29 · BUG · A dropped team returns to the Library and can be re-registered repeatedly ❓
When a team is **dropped from an event it reappears in the Club Teams Library**, and can be used to
register again, any number of times.
❓ Two open questions from the notes: **is this correct behaviour?** and **do Team IDs change with
each registration?**

### D-30 · Club Rep Permissions — Delete stays available for 60 minutes after it is switched off
With **Delete off**, a newly created team is **still deletable** — the 60-minute delete window.
Three asks:
- It must be **discussed explicitly with the Shoulbergs** — this was not what she wanted, so she
  should be able to **turn it off and on** if persuaded it makes sense.
- The **description on the Teams permission must state** that the window exists.
- Also observed: **Add and Delete were both ticked, and delete returned on a team after an hour** —
  is **Add coupled to Delete**?

### D-31 · Closed functions should be disabled with a reason, not removed
When the director has closed a function, the control should **stay visible and disabled with an
explanation** rather than vanish:
- Trash can → **"Delete closed by the director"**
- Add → icons **faded** so they read as unavailable, with **"Add team closed by the director"**
- The **Register badge greys out** rather than disappearing.

### D-32 · SuperUser can create a club rep registration ❓
Recorded from the note *"can use Superuser to make a club rep registration"*, marked as something
that should **not** be possible. Needs a steer on whether this is filed as a **defect** or a
**policy question**.

---

## Needs a decision before filing

| | What the note says | What is needed |
|:--|:--|:--|
| **❓ 1** | *"Make a bank of teams"* | Unclear whether this is the Library by another name or something separate |
| **❓ 2** | *"Future testing: competition for spots, Add/Drop/Delete"* | Reads as a note-to-self about what to test next, not an item — confirm and it stays out |
| **❓ 3** | *"THE SCREEEN:"* | A heading with nothing under it |

## Redundancies collapsed — worth a check

| Merged into | From |
|:--|:--|
| **D-05** | badge content + the "labels incorrect" line |
| **D-08** | all three team-count locations, from three separate notes |
| **D-12** | four separate notes on pencil / archive / restore / collapse |
| **D-13** | the duplicate-names note + *"Create team and add a team name already there"* |
| **D-15** | *"Registered Teams needs to mirror Club Team Library. Small circle with colour"* + the later "colours distract" line |
| **D-19** | the Undo→Delete note + the Undo wording inside the age-group note |
| **D-30** | the permissions note + the Add/Delete observation an hour later |
