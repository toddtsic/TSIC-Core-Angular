# CLUB REP

## Objective

**A club rep registers teams from a list they maintain, instead of typing in their teams for every event.**

For that to be true the rep needs two things:

- **Maintain the list.** Add a team, edit its name, grad year and level of play, archive it when it is no longer used, restore it, delete it if it never went anywhere.
- **Register from the list.** See which teams on the list are registered for this event and which are not, pick one, place it in an age group, pay.

## What the club rep has

A club rep has one **Club Team Library** per club: the club's list of teams, each with a name, grad year and level of play. The library belongs to the club, not to an event. Registering a team for an event means picking a library team and placing it in one of the event's age groups. The same library team can be registered for many events over the years, and the rep sees that history per team.

### Names: two places, one team

- Every library team has a **library name** (for example `2028 Blue`).
- Every event registration has its own **event name**. It starts as the library name.
- Renaming from the **Registered Teams** list changes the name **for this event only**. The library and other events keep theirs.
- Editing a team in the **library** (name, grad year, level of play) changes the library only. Existing event registrations keep the name, grad year and level of play they had. There is no separate Rename in the library.
- Nothing ever renames a team in other events. That is by design.

## Access Points: the library page and the library fly-in

The Club Team Library appears in two places. Same library, same teams, same rules; the difference is where the rep is standing. Both are inside an event. There is no library outside an event: the rep reaches it by opening one of their event registrations.

**The library page** is the Club Team Library page: its own page inside the event, not part of the registration wizard. It is the list's home: add, edit, archive, restore, delete, and see every event each team has been registered for. Nothing on it registers a team. That happens in Team Registration.

**The library fly-in** is the same library inside Team Registration, on the Teams step. The rep is mid-registration; the fly-in shows the library grouped by what fits this event, so the rep picks a team and registers it without leaving the wizard. Maintenance is there too (kebab on each row), but registering is what it is for.

### Reaching the library page

- **Tournament job site, header user menu**: **Club Team Library**, on every page.
- **From the library fly-in**: the fly-in's header link **Manage full library →**.

### Reaching the library fly-in

**Starting from the job home page** (not signed in, or not yet a club rep in this event):

- **Register Team** in the Registration Links panel. It shows only while team registration is open. Sign in or create an account, name the club, and the wizard opens on the Teams step.
- On the Teams step, **Register Your First Team for this Event** opens the fly-in. When the library is empty, the fly-in's add form is titled **Register Your First Team**.

**Signed in as the event's club rep:**

- Landing page rep card: **Team Registration** (reads **Register Teams** when the rep has no teams yet) opens the Teams step.
- Header user menu: **Team Registration** opens the Teams step.
- On the Teams step, three things open the fly-in: **Register Another Team** in the footer, **Register** on the yellow strip of unregistered library teams, and **Register Your First Team for this Event** when nothing is registered yet.

## Correct behaviour, not bugs

- **One club rep per club per event.** A second rep from the same club is refused with a yellow toast naming the rep who already registered. Never a red "Something went wrong".
- An age group with no fee set up refuses with **Registration fees for this age group haven't been set up yet. Please contact the event organizer.**
- A waitlisted team has no fee until it is placed.
- A dropped team stays visible as Dropped. The rep cannot re-register it; the director handles it.
- A library team's name, grad year and level of play can always be edited by the rep, played or not. Every event registration keeps its own copy, so a library edit changes nothing in any event's schedule. The director's Allow Edit governs the event copy only.
- A registered team cannot be archived or deleted. A team with any event registration ever cannot be deleted, only archived.
- A team with a payment on it cannot be removed from the event by the rep.
- Typing a URL or opening a bookmark starts a fresh session and asks for login. F5 keeps the session.

## Club rep tests

### A. Event landing rep card (`primeaulacrosse`, lftc-fallshowcase-2026)

- [ ] A1. The card shows the club name and the number of registered teams. The count matches the Teams step.
- [ ] A2. Money on the card agrees with the Teams step footer. When something is owed now, the **Pay Balance Due** door carries the amount with its phase word (`$X due now`, `$X deposit due now · $Y due later`, `$X balance due now`). When nothing is owed now the status line reads **Nothing due now**, **Auto-pay scheduled**, or **$X deposit paid · $Y due later**. The phase is never named on its own.
- [ ] A3. Doors work: Team Registration, Edit My Rosters. There is no Club Team Library door on the card; the library is in the header user menu. Pay Balance Due appears only when a balance is due.
- [ ] A4. (`MGoins`, lftc-summer-2027) The card shows **No teams registered yet** and a **Register Teams** door. If this landing page has no registration panel at all, note that and use the header user menu.

### B. Teams step (`primeaulacrosse`)

- [ ] B1. Fee Status on every row reads one of `Deposit $X due now`, `Deposit paid`, `$X balance due now`, `Paid in full`, `Auto-pay · $X`, or `No fee until placed` (a waitlisted team).
- [ ] B2. Footer reads Paid · Due now · Later, and the sums match the rows.
- [ ] B3. If unregistered library teams fit this event, the yellow strip names them. **Register** opens the fly-in. The × dismisses it for the session and it returns after logout and login.
- [ ] B4. The pencil opens the rename dialog. Rename a team for this event only. The library page still shows the old library name; the Teams step shows the new event name.
- [ ] B5. Rename it back.

### C. Library fly-in (`primeaulacrosse`)

- [ ] C1. **Register Another Team** opens the drawer. The Registered strip lists the same teams as the Teams step.
- [ ] C2. Groups appear: Available for This Event, Outside Event Age Groups, Archived (collapsed), Dropped (if any).
- [ ] C3. **Add a New Team** › name `Zz Test Alpha`, grad year, level of play, age group › **Register Team for this Event**. Toast confirms. The team appears in the Registered strip and on the Teams step, and the count goes up by one.
- [ ] C4. **Add a New Team** › name `Zz Test Beta` › **Save to library only**. Warning toast says NOT registered. The row is tinted with **Not registered yet**.
- [ ] C5. Click **Done** with Zz Test Beta unregistered. The interstitial names the team. **Leave it** closes the drawer. Reopen: Zz Test Beta sits under Available for This Event with no tint.
- [ ] C6. Repeat C4 with `Zz Test Gamma`, click Done, choose **Register it now**. The register sheet opens for that team. Cancel it.
- [ ] C7. Type a name already in the library. The modal refuses it and the Register button stays disabled.
- [ ] C8. Type a name that is only a year, such as `2031`. A nudge appears and the Register button still works.
- [ ] C9. Kebab on a registered row: Archive team and Delete team are locked with a reason on hover. Edit team works and its dialog says it changes the library only.
- [ ] C10. Kebab on Zz Test Beta: Edit details opens, change the level of play, save. Archive moves it to Archived. Restore brings it back.
- [ ] C11. **Manage full library →** lands on the Club Team Library page.

### D. Club Team Library page (`primeaulacrosse`)

- [ ] D1. The page says it is the club's list of teams and that you are not registering here. The columns read **Library team** and **Registered for**. There is no Register button, no Team Registration button and no strip anywhere on the page.
- [ ] D2. Registered for: one chip per event the team has been registered for. This event's chip comes first, highlighted with a pin. A waitlisted or dropped registration carries a **waitlist** or **dropped** tag on its chip. A team never registered anywhere reads **Not registered yet**. More than three fold behind **+N more**. When two organizers ran events with the same short name, the chips show the organizer in front.
- [ ] D3. Every row has an Actions column with Edit, Archive and Delete, and nothing about this event. A locked action is greyed and struck through; hovering shows the reason and clicking it shows the reason as a toast. A team registered here locks Archive with **Registered for the** event name.
- [ ] D4. On Zz Test Beta: **Edit** › the dialog opens with a callout saying it changes the library only and that registered teams keep their name, grad year and level of play › change the name › save. The row shows the new name. Change it back.
- [ ] D5. **Add to Library** adds `Zz Test Delta` without registering it. It appears reading **Not registered yet**.
- [ ] D6. Delete Zz Test Delta (never registered): it disappears. On any team that has ever been registered, Delete is refused and only Archive is offered.
- [ ] D7. Press F5. You stay logged in and on the page.

### E. Refusals (`MGoins`, lftc-summer-2027)

- [ ] E1. Try to register any team. A **yellow** toast names `adamgoins` and says only one club representative may register. No red "Something went wrong".
- [ ] E2. The team is still in the library, not registered.

### F. Club rep clean-up

- [ ] F1. Remove `Zz Test Alpha` from the event, then archive it (delete is refused because it was registered). Delete `Zz Test Beta` and `Zz Test Gamma` if never registered, otherwise archive them.

---

# DIRECTOR

## What the director has

The director sees every club rep who signed in to the event, including reps who registered nothing, and can look inside any rep's library. The view is **read-only**. Directors never add, rename, archive or delete library teams. When a director renames a team in Search › Teams, that changes the event name only; the club's library name is untouched.

## Correct behaviour, not bugs

- There is no edit control of any kind in the Club Reps library panel.
- **Rename to New Team** is intentionally hidden on the team detail panel.
- The Club Reps grid counts registrations, so a club with two rep accounts shows two rows.

## Director tests

### G. Search › Club Reps (lftc-fallshowcase-2026)

- [ ] G1. `/lftc-fallshowcase-2026/search/club-reps` loads. Until the nav script is re-run there is no menu entry; type the URL. The row count equals the Club Rep registrations on Search › Registrations filtered to the Club Rep role.
- [ ] G2. A rep with zero teams is present as a row with 0 Active.
- [ ] G3. `primeaulacrosse`'s row: Active equals their Teams step count. Library equals the number of active rows on their library page. That column reads `fits / all`: `2/3` means 3 library teams are not registered here and 2 of them fit an age group at this event.
- [ ] G4. Click **no teams yet**: only zero-team reps remain. Click **owed**: only reps with an amount owed remain. **Show all** clears it.
- [ ] G5. Excel export downloads a file with the same rows as the grid.
- [ ] G6. **Library** on `primeaulacrosse`'s row opens the panel. The table shows every team on their library page. Its This event column matches the highlighted chip on the rep's page, and its Played at chips match the rest.
- [ ] G7. The counts line at the top of the panel matches the table beneath it.
- [ ] G8. **Open in Search Teams** on a registered row lands on Search › Teams with that team's detail panel already open.
- [ ] G9. There is no button anywhere in the panel that renames, edits, archives or deletes a library team.
- [ ] G10. Escape, the ×, and a click on the backdrop each close the panel.

### H. Search › Teams detail panel

- [ ] H1. Open a team that came from a library. The header shows a **Library** tag with library name, grad year and level of play.
- [ ] H2. The **Other events** line lists the team's other events, or reads **none — first event for this team**.
- [ ] H3. A team not linked to any library (an old manually entered team) shows neither line.
- [ ] H4. Rename the team in the Details tab. As `primeaulacrosse`, the library page still shows the library name, and this event's chip on it shows `as <new name>`.
- [ ] H5. Rename it back.

### I. Configure › Job Settings › Teams › Club Rep Permissions

- [ ] I1. The help list under the three checkboxes is present and reads correctly.
- [ ] I2. Turn **Allow Edit** off, save. As `primeaulacrosse`: the Teams step pencil is visible but greyed, tooltip **Editing closed by the director**. Fly-in kebab › Edit details and the library page's Edit button stay open: Allow Edit governs the event copy only, never the library.
- [ ] I3. With Allow Edit still off, library **Edit** still works on the library page and in the fly-in kebab. Expected: the library is never gated by Allow Edit.
- [ ] I4. Turn **Allow Delete** off, save. As the rep: **Remove from this event** is locked with **Removal closed by the director**.
- [ ] I5. Turn **Allow Add** off, save. As the rep: the Teams step strip is gone, the fly-in's Register buttons are gone, and the library page is unchanged: it never offered registration.
- [ ] I6. Turn all three back on, save. Everything reopens.

### J. Director clean-up

- [ ] J1. After the club rep clean-up (F1), Search › Club Reps counts for the club are back to what they were in G3.
