# CLUB REP

## Objective

**A club rep registers teams from a list they maintain, instead of typing in their teams for every event.**

For that to be true the rep needs two things:

- **Maintain the list.** Add a team, rename it, edit its grad year and level of play, archive it when it is no longer used, restore it, delete it if it never went anywhere.
- **Register from the list.** See which teams on the list are registered for this event and which are not, pick one, place it in an age group, pay.

## What the club rep has

A club rep has one **Club Team Library** per club: the club's list of teams, each with a name, grad year and level of play. The library belongs to the club, not to an event. Registering a team for an event means picking a library team and placing it in one of the event's age groups. The same library team can be registered for many events over the years, and the rep sees that history per team.

### Names: two places, one team

- Every library team has a **library name** (for example `2028 Blue`).
- Every event registration has its own **event name**. It starts as the library name.
- Renaming from the **Registered Teams** list changes the name **for this event only**. The library and other events keep theirs.
- Renaming from the **library** changes the library name. Existing event registrations keep the name they had.
- Nothing ever renames a team in other events. That is by design.

## Access Points: Standalone vs Integrated

The Club Team Library exists in two forms. Same library, same teams, same rules; the difference is where the rep is standing.

**Standalone CTL** is the Club Team Library page: its own page, not part of the registration wizard. The rep goes there to maintain the list: add, rename, edit, archive, restore, delete, and to see every team's status for this event and its other events. Registering from it is possible, but it is the list's home first.

**Integrated CTL** is the library fly-in inside Team Registration, on the Teams step. The rep is mid-registration; the fly-in shows the same library grouped by what fits this event, so the rep picks a team and registers it without leaving the wizard. Maintenance is there too (kebab on each row), but registering is what it is for.

### Reaching the Standalone CTL

- **Login / role select screen.** There is no library door here. A club rep picks an event row and reaches the library inside that event. The role list offers only available events (inside the director's user-expiry window); there is no past-events group.
- **Tournament job site, landing page rep card**: the **Club Team Library** door.
- **Tournament job site, header user menu**: **Club Team Library**, on every page.
- **From the Integrated CTL**: the fly-in's header link **Manage full library →**.

### Reaching the Integrated CTL

- **Tournament job site, landing page rep card**: **Team Registration** (reads **Register Teams** for a rep with no teams yet) opens the Teams step; **Register Another Team** there opens the fly-in.
- **Tournament job site, header user menu**: **Team Registration**, then the same button.
- **From the Standalone CTL**: the **Team Registration** link at the top of the page.

## The club rep's screens

### Screen 1 — Sign-in role picker

Each Club Rep row shows the event's dates and how many teams the rep has registered there. The role list offers only available events (inside the director's user-expiry window); there is no past-events group. The event the rep came from is tagged **this event**. With a few registrations the picker is a list of cards; with many it is a search box. Clicking a row opens that event's landing page. There is no Library button on a row and no library door on this screen: the library is reached inside the event.

### Screen 2 — Event landing page, rep card

The landing page shows a card for the rep: club name, number of registered teams, and a money line reading **Nothing due now**, **Balance due** or **Auto-pay scheduled**. It never says "paid in full". Doors: **Team Registration** (reads **Register Teams** when the rep has zero teams), **Pay Balance Due** (only when money is due), **Edit My Rosters**, **Club Team Library**, **Add Team RegSaver**. A rep with zero teams sees **No teams registered yet**. Some events have no registration panel on their landing page; there the rep uses the header user menu.

### Screen 3 — Team Registration › Teams step

The Registered Teams list for this event. Columns: team name with a pencil for this-event rename, age group, level of play, and **Fee Status**. Fee Status reads one of `Deposit $X due now`, `Deposit paid`, `$X balance due now`, `Paid in full`, `Auto-pay · $X`, or `No fee until placed` for a waitlisted team. The footer totals read **Paid · Due now · Later**, plus **Auto-pay** when one exists.

Under the list, when library teams that fit this event are not registered, a yellow strip names them with a **Register** button. The × dismisses it for the session.

The pencil is always shown. When the director has turned Allow Edit off it is greyed out with the tooltip **Editing closed by the director**.

### Screen 4 — Library fly-in

Opens from **Register Another Team** on the Teams step. A right-hand drawer with a pinned **Registered** strip at the top, then groups **Available for This Event** (library teams that fit an age group here), **Outside Event Age Groups**, **Archived** (collapsed) and **Dropped**. Each row has a kebab: **Rename team**, **Edit details**, **Archive team**, **Delete team**. Locked items show the reason on hover. The header link **Manage full library →** opens the Club Team Library page.

**Add a New Team** opens the **Add & Register a Team** modal (titled **Register Your First Team** when the library is empty): name, grad year, level of play, then age group. The primary button is **Register Team for this Event**. A secondary link **Save to library only — NOT registered for <event>** saves without registering and shows a warning toast. A team saved that way is tinted in the drawer with a **Not registered yet** pill. Closing the drawer while one exists shows an interstitial: **Register it now** or **Leave it**. The modal refuses a duplicate name. It shows a soft nudge when the name is only a year, such as `2028`; the nudge never blocks.

### Screen 5 — Club Team Library page

Reached from the header user menu **Club Team Library**, the rep card door, or the fly-in header link. One row per library team:

- **Team**: name, grad, LOP.
- **This event**: Registered with age group / Waitlisted / Dropped / a Register button / Closed.
- **Other events**: one chip per other event, marked `dropped` or `waitlist` where that applies. **First event** when there are none. More than three fold behind **+N more**.
- Kebab with two sections: **This event** (Register for this event / Remove from this event) and **Club Team Library** (Rename team / Edit details / Archive team / Delete team).

Archived teams sit in a collapsed **Archived** section with a **Restore** button. **Add to Library** adds a team without registering it. A strip at the top counts teams not registered for this event.

History is events only. There are no win-loss records anywhere in the library. That is by design.

### Correct behaviour, not bugs

- **One club rep per club per event.** A second rep from the same club is refused with a yellow toast naming the rep who already registered. Never a red "Something went wrong".
- An age group with no fee set up refuses with **Registration fees for this age group haven't been set up yet. Please contact the event organizer.**
- A waitlisted team has no fee until it is placed.
- A dropped team stays visible as Dropped. The rep cannot re-register it; the director handles it.
- A team that has been scheduled at any event cannot have its grad year or level of play edited. Its name can still be renamed.
- A registered team cannot be archived or deleted. A team with any event registration ever cannot be deleted, only archived.
- A team with a payment on it cannot be removed from the event by the rep.
- Typing a URL or opening a bookmark starts a fresh session and asks for login. F5 keeps the session.

## Club rep tests

### A. Sign-in role picker

- [ ] A1. Club Rep rows show event dates and a team count. There is no Past group, no Library button on any row, and no library door anywhere on the screen.
- [ ] A2. The event you arrived from is tagged **this event**.
- [ ] A3. Clicking a Club Rep row opens that event's landing page.
- [ ] A4. On an account with many registrations (search-box picker), the same holds.

### B. Event landing rep card (`primeaulacrosse`, lftc-fallshowcase-2026)

- [ ] B1. The card shows the club name and the number of registered teams. The count matches the Teams step.
- [ ] B2. The money line reads Nothing due now / Balance due / Auto-pay scheduled and agrees with the Teams step footer.
- [ ] B3. Doors work: Team Registration, Club Team Library, Edit My Rosters. Pay Balance Due appears only when a balance is due.
- [ ] B4. (`MGoins`, lftc-summer-2027) The card shows **No teams registered yet** and a **Register Teams** door. If this landing page has no registration panel at all, note that and use the header user menu.

### C. Teams step (`primeaulacrosse`)

- [ ] C1. Fee Status shows one of the listed statuses on every row.
- [ ] C2. Footer reads Paid · Due now · Later, and the sums match the rows.
- [ ] C3. If unregistered library teams fit this event, the yellow strip names them. **Register** opens the fly-in. The × dismisses it for the session and it returns after logout and login.
- [ ] C4. The pencil opens the rename dialog. Rename a team for this event only. The library page still shows the old library name; the Teams step shows the new event name.
- [ ] C5. Rename it back.

### D. Library fly-in (`primeaulacrosse`)

- [ ] D1. **Register Another Team** opens the drawer. The Registered strip lists the same teams as the Teams step.
- [ ] D2. Groups appear: Available for This Event, Outside Event Age Groups, Archived (collapsed), Dropped (if any).
- [ ] D3. **Add a New Team** › name `Zz Test Alpha`, grad year, level of play, age group › **Register Team for this Event**. Toast confirms. The team appears in the Registered strip and on the Teams step, and the count goes up by one.
- [ ] D4. **Add a New Team** › name `Zz Test Beta` › **Save to library only**. Warning toast says NOT registered. The row is tinted with **Not registered yet**.
- [ ] D5. Click **Done** with Zz Test Beta unregistered. The interstitial names the team. **Leave it** closes the drawer. Reopen: Zz Test Beta sits under Available for This Event with no tint.
- [ ] D6. Repeat D4 with `Zz Test Gamma`, click Done, choose **Register it now**. The register sheet opens for that team. Cancel it.
- [ ] D7. Type a name already in the library. The modal refuses it and the Register button stays disabled.
- [ ] D8. Type a name that is only a year, such as `2031`. A nudge appears and the Register button still works.
- [ ] D9. Kebab on a registered row: Archive team and Delete team are locked with a reason on hover. Rename team works.
- [ ] D10. Kebab on Zz Test Beta: Edit details opens, change the level of play, save. Archive moves it to Archived. Restore brings it back.
- [ ] D11. **Manage full library →** lands on the Club Team Library page.

### E. Club Team Library page (`primeaulacrosse`)

- [ ] E1. The strip counts unregistered teams and names the same rows that show a Register button.
- [ ] E2. Each registered team shows Registered with the right age group. A waitlisted team shows Waitlisted. A dropped team shows Dropped.
- [ ] E3. Other events: a team that played earlier events shows a chip per event; a new team shows **First event**. More than three fold behind **+N more**.
- [ ] E4. Every row's kebab shows two sections, This event and Club Team Library. Locked items show reasons.
- [ ] E5. On Zz Test Beta: **Register for this event** › pick an age group › register. The row turns Registered. The Teams step now lists it.
- [ ] E6. On Zz Test Beta (registered, unpaid): **Remove from this event** › confirm. The row returns to Register and the team stays in the library.
- [ ] E7. **Add to Library** adds `Zz Test Delta` without registering it. It appears with a Register button.
- [ ] E8. Delete Zz Test Delta (never registered): it disappears. On any team that has ever been registered, Delete is refused and only Archive is offered.
- [ ] E9. Press F5. You stay logged in and on the page.

### F. Refusals (`MGoins`, lftc-summer-2027)

- [ ] F1. Try to register any team. A **yellow** toast names `adamgoins` and says only one club representative may register. No red "Something went wrong".
- [ ] F2. The team is still in the library, not registered.

### G. Club rep clean-up

- [ ] G1. Remove `Zz Test Alpha` from the event, then archive it (delete is refused because it was registered). Delete `Zz Test Beta` and `Zz Test Gamma` if never registered, otherwise archive them.

---

# DIRECTOR

## What the director has

The director sees every club rep who signed in to the event, including reps who registered nothing, and can look inside any rep's library. The view is **read-only**. Directors never add, rename, archive or delete library teams. When a director renames a team in Search › Teams, that changes the event name only; the club's library name is untouched.

### Screen 1 — Search › Club Reps

One row per Club Rep registration on the event. A rep with no teams is a row. Columns:

- **Library** button, **Club**, **Rep**.
- **Active**, **Waitlisted**, **Dropped**: this event's team counts.
- **Owed**: this rep's balance on this event.
- **Library**: how many active teams are in the club's library.
- **Not Registered**: `fits / all`. `2/3` means 3 library teams are not registered here and 2 of them fit an age group at this event.
- **Email**, **Cell**, **Signed In** (the date the rep registered as a club rep here).

Four cards above the grid are filters: **club reps** (all), **no teams yet**, **library teams not registered** (that fit an age group), **owed**. Excel export is on the toolbar.

The **Library** button opens a right-hand panel: club name, rep name and email, counts, the event's oldest age group offered, then a table **Team | This event | Other events**. Registered and Waitlisted rows carry **Open in Search Teams**, which lands on Search › Teams with that team's detail panel open. Nothing in the panel edits anything.

Until the nav script is re-run there is no menu entry. The URL is `/<jobPath>/search/club-reps`, for example `/lftc-fallshowcase-2026/search/club-reps`.

### Screen 2 — Search › Teams, detail panel header

For a team that came from a library the header shows a **Library** tag (library name · grad year · level of play) and an **Other events** line with one chip per other event, or **none — first event for this team**. A team not linked to any library shows neither.

### Screen 3 — Configure › Job Settings › Teams › Club Rep Permissions

Under the Allow Edit / Allow Delete / Allow Add checkboxes is a list saying what each one gates:

- **Allow Edit**: the rep can change a registered team's age group and level of play for this event, and edit a library team's details until it has played. This-event rename is **not** gated by it.
- **Allow Delete**: the rep can unregister an unpaid team. A team with a payment is never removable by the rep.
- **Allow Add**: the rep can register teams here. Registering also needs Allow Team Registration and a club-rep fee row.

### Correct behaviour, not bugs

- There is no edit control of any kind in the Club Reps library panel.
- **Rename to New Team** is intentionally hidden on the team detail panel.
- The Club Reps grid counts registrations, so a club with two rep accounts shows two rows.

## Director tests

### H. Search › Club Reps (lftc-fallshowcase-2026)

- [ ] H1. `/lftc-fallshowcase-2026/search/club-reps` loads. The row count equals the Club Rep registrations on Search › Registrations filtered to the Club Rep role.
- [ ] H2. A rep with zero teams is present as a row with 0 Active.
- [ ] H3. `primeaulacrosse`'s row: Active equals their Teams step count. Library equals the number of active rows on their library page. Not Registered `fits / all` matches their library page's strip.
- [ ] H4. Click **no teams yet**: only zero-team reps remain. Click **owed**: only reps with an amount owed remain. **Show all** clears it.
- [ ] H5. Excel export downloads a file with the same rows as the grid.
- [ ] H6. **Library** on `primeaulacrosse`'s row opens the panel. The table shows every team on their library page with the same This event status and the same Other events chips.
- [ ] H7. The counts line at the top of the panel matches the table beneath it.
- [ ] H8. **Open in Search Teams** on a registered row lands on Search › Teams with that team's detail panel already open.
- [ ] H9. There is no button anywhere in the panel that renames, edits, archives or deletes a library team.
- [ ] H10. Escape, the ×, and a click on the backdrop each close the panel.

### I. Search › Teams detail panel

- [ ] I1. Open a team that came from a library. The header shows a **Library** tag with library name, grad year and level of play.
- [ ] I2. The **Other events** line lists the team's other events, or reads **none — first event for this team**.
- [ ] I3. A team not linked to any library (an old manually entered team) shows neither line.
- [ ] I4. Rename the team in the Details tab. As `primeaulacrosse`, the library page still shows the library name, and the This event column shows `as <new name>`.
- [ ] I5. Rename it back.

### J. Configure › Job Settings › Teams › Club Rep Permissions

- [ ] J1. The help list under the three checkboxes is present and reads correctly.
- [ ] J2. Turn **Allow Edit** off, save. As `primeaulacrosse`: the Teams step pencil is visible but greyed, tooltip **Editing closed by the director**. Fly-in kebab › Edit details and library page kebab › Edit details are locked with the same words.
- [ ] J3. With Allow Edit still off, library **Rename team** in the kebab still works. Expected: rename is not gated by Allow Edit.
- [ ] J4. Turn **Allow Delete** off, save. As the rep: **Remove from this event** is locked with **Removal closed by the director**.
- [ ] J5. Turn **Allow Add** off, save. As the rep: the Teams step strip is gone, the fly-in's Register buttons are gone, and the library page shows Closed on unregistered rows with **registration is closed**.
- [ ] J6. Turn all three back on, save. Everything reopens.

### K. Director clean-up

- [ ] K1. After the club rep clean-up (G1), Search › Club Reps counts for the club are back to what they were in H3.
