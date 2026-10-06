# AR-142: Several club reps for one club — Current Design as of 10/06/2026

**For:** Ann, and anyone testing club rep registration.

---

## Goal

**To support multiple club reps with the same club name.**

## Scenarios it covers

| Scenario | What happens |
|---|---|
| **A new person takes over a club.** The old rep left, and the new one has never used TSIC. | They sign up with the club's name, answer **"Is this your club?"**, and start with the club's saved teams. Nobody has to hand anything over. |
| **Two people register for one club in the same event**, e.g. one handles the boys' teams and one the girls'. | Both register. Each has their own teams and their own bill, and each sees the other's teams, read-only. |
| **The second person goes to add a team the first may already have entered.** | They're warned and asked first: **Don't add** or **Yes, add it**. |
| **Two unrelated clubs happen to share a name.** | The newcomer picks **None of these**, which gives them their own new club. |
| **The director needs to spot possible double entries.** | The club shows **"⚠ N reps"** in the **C-A-D-T tree filter** (Club → Agegroup → Division → Team) of the director's **Search Registrations** and **Search Teams**. |

## How it works

**The Club Team Library is now per rep: C-CR-TL, Club → Club Rep → Team Library** (the C-A-D-T tree filter in Search Registrations and Search Teams is unchanged: Club → Agegroup → Division → Team). A club name can have several reps. Each rep has their own team list, and nobody else's changes touch it.

The design touches **two separate places**, at two different times:

| | **Create NEW Club Rep Account** | **Team Registration Wizard** |
|---|---|---|
| **When** | **Once**, when the person becomes a club rep | **Every event** the rep registers teams for |
| **What it decides** | **Which club** this rep is, and **what's in their library** | **Which teams** go into this event |
| **What changed** | Asks "Is your club one of these?", and copies the picked club's teams into the rep's own library | Shows other reps of the same club name, and warns before a possible duplicate team |
| **Can it block?** | **No.** A club name never refuses an account. | **No.** The warning asks, and the rep decides. |

Teams are copied only at Create NEW Club Rep Account. Nothing in the wizard copies teams or changes another rep's library.

---

## The rules

1. **Every rep has their own club and their own bill.** A new rep never shares another rep's club, team list, or balance.
2. **A similar name only helps a new rep find their club at Create NEW Club Rep Account.** After that, the **exact** club name is what counts.
3. **One rep's teams are never another rep's to manage.** Each rep sees the other reps' teams in the event, but can't edit, drop or pay for them.
4. **Warn, never block.** Each warning ends in a choice the person can make, never in a dead end.

---

## What each person sees

### 1. Create NEW Club Rep Account (once, when the person becomes a club rep)

- The whole form shows from the start. A line at the top reads *"Already have a club rep account? Sign in or reset your password."*
- As the rep types the club name, clubs with that name that are already on TSIC appear under **"Is your club one of these?"** (or "Is this your club?" when there is one). Each row shows the club's state, number of active teams and when it last registered. Rep names and emails are never shown.
- The rep **must answer** before **Create Account** works:
  - **Pick a club:** the Club Name box **locks** to that club's exact name.
  - **None of these, we're a new club:** the box unlocks, the rep's own typing comes back, and they start with an empty library.
- A name nobody else uses gets no question at all.
- **The copy.** When the account is created, the picked club's **active** teams are copied into the new rep's own library: name, grad year and level of play.
  - Archived teams and teams with no name aren't copied.
  - A team the rep already has (same name and grad year) isn't copied twice.
  - It happens **once**. After that the two lists are separate: renaming, archiving or adding teams in one never changes the other.

### 2. Team Registration Wizard, Teams step (every event)

- **Add a Team** offers the rep's own library, including any teams copied at Create NEW Club Rep Account, so there's nothing to retype.
- **"Registering as {club}"** bar at the top:
  - **Rename** is offered while the rep has **no teams in this event**. It renames the club for this event and in the rep's Club Team Library; events where the rep already has teams keep their name.
  - Once the rep has a team in this event, the bar instead reads *"To rename your club for this event, ask the event director."*
  - Teams in **other** events don't lock it.
- **Duplicate warning:** when the rep picks an age group where another rep of the **same club name** already has teams in this event, an amber warning shows:
  - *"{Rep} has already registered the {teams} team(s) in the {age group} age group."*
  - *"Do you really want to add the {team} team?"*
  - *"Adding it creates a separate entry and a separate fee."*
  - **Don't add** clears the row. **Yes, add it** adds the team. **+ Add** and **✕** stay disabled until the rep answers.
- **Registered Teams** is grouped by rep when other reps of the same club name are in the event:
  - **"You ({name})"** first, in green, with every action.
  - Each other rep below, in grey and read-only: *"Registered and paid for by {rep} — not on your bill."*
  - The team count and the payment step cover the rep's **own** teams only.

### 3. Director screens (Search Teams, Search Registrations, schedule screens)

- **"⚠ N reps" badge** on any club in the **C-A-D-T tree filter** (Club → Agegroup → Division → Team) of **Search Registrations** and **Search Teams** that has more than one rep's teams in the event. The text is always visible, not hover-only. Its tooltip reads *"N club reps entered teams under {club} — check for a team entered twice."*
- Only directors (and SuperDirector/Superuser) of that event see the badge. Public schedule pages never do.
- **Rename Club for this event** (registration detail panel, Details tab):
  - Allowed once the rep has at least one team in the event.
  - Allowed even when another rep in the event already uses that name.
  - It renames this event only: the registration, its teams' names, and the schedule.
  - Refused when the name belongs to a **different** club that isn't one of the rep's own.
- **Move a team to another club rep** works as before.

---

## AR-142, point by point

| Ann's point | Where it stands |
|---|---|
| **Block 1: creating a new account** | **Fixed.** A club name never refuses Create NEW Club Rep Account (section 1). |
| **Block 2: a second rep in an event** | **Fixed.** The rule "Only one club representative can register teams per event" is removed. Legacy never had it; it was added in January 2026. It was also over-broad: someone repping two clubs could block the other club's reps. |
| **Block 3: transferring teams to them** | **Unblocked.** The director's move had no club-name check. The new rep was stuck only because they couldn't sign up to receive the teams. |
| **Recommendation 1: warn, but let them proceed** | **Done, at two points:** the "Is your club one of these?" question at Create NEW Club Rep Account (section 1), and the age-group warning in the Team Registration Wizard (section 2). |
| **Recommendation 2: a flag for duplicates in Search** | **Done as the "⚠ N reps" badge** on the club in the **C-A-D-T tree filter** of the director's **Search Registrations** and **Search Teams** (section 3). It's visible text rather than hover-only, which covers the touch and keyboard concern. Todd ruled this sufficient. |
| **The "4 minute window"** | **Closed.** No 4-minute limit exists in TSIC, in today's code, its history or Legacy. The only timers a rep meets are the team undo window (60 minutes) and a username check that never blocks. The cost Ann described, a rep losing minutes and then being stopped without knowing why, came from the blocks above. All three are removed, so nothing stops a rep now. |

**One correction to the report:** a second rep was never blocked at **login**. They got in and were refused on their first **Add**. That's now fixed.

---

## Known limits (accepted)

- **Matching is by exact club name.** "Lax Plus" and "Lax Plus Club" count as different clubs, so they get no warning and no badge.
- **The C-A-D-T tree filter (Search Registrations, Search Teams) groups by club name.** Two unrelated clubs that share a name show as one line with the badge. The badge is the prompt to look.
- **Older clubs with two reps on one club record:** in currently open events, exactly **one** club has two such reps who both have teams (South Jersey Select, Top Threat Five Star 2026). Create NEW Club Rep Account never creates this. Those reps now register alongside each other like any same-name reps.
- **A renamed club** shows as two clubs in the year-over-year team-retention report, which matches clubs across years by name.

---

## How to test

Test locally. Use made-up accounts and an event you own. Each step says what you should see.

### A. Create NEW Club Rep Account: an existing club name
1. Start **Create NEW Club Rep Account** and type the name of a club already on TSIC.
   → **"Is your club one of these?"** lists it, with its state, team count and last registered date. **Create Account** is disabled.
2. Pick the club.
   → The Club Name box locks to that club's exact name.
3. Pick **None of these**, then pick the club again.
   → None unlocks the box and brings your typing back; picking again locks it.
4. Pick the club, finish and create the account. Then open the Team Registration Wizard for an event and go to the Teams step.
   → Your library already has that club's teams.
5. Repeat with a new rep, choosing **None of these**.
   → The new rep gets an empty club.
6. Type a name nobody uses.
   → No question appears.

### B. Team Registration Wizard: two reps of the same club in one event
1. Rep A registers a team in age group X.
2. Rep B, with the same exact club name, opens the Teams step in that event.
   → Registered Teams shows "You (B)" in green and rep A's group in grey: "Registered and paid for by A — not on your bill."
3. Rep B starts a team in age group X.
   → The amber warning names rep A's team. **+ Add** and **✕** are disabled.
4. Click **Don't add**.
   → The row clears and nothing is added.
5. Repeat, and click **Yes, add it**.
   → The team is added. B's count and payment include only B's teams.
6. Rep B starts a team in an age group where rep A has none.
   → No warning.

### C. Team Registration Wizard: renaming the club on the Teams step
1. As a rep with no teams in the event:
   → The bar shows **Rename**. Rename the club, and the page reloads under the new name.
2. Rename to another rep's exact club name in this event.
   → The warning and the grey groups appear.
3. Rename back.
   → They disappear.
4. Add one team.
   → **Rename** is replaced by "ask the event director".

### D. Director screens
1. Open **Search Teams** for the event.
   → The club has "⚠ 2 reps", and hovering or focusing it shows the tooltip.
2. Open the rep's registration → **Details** → **Rename Club** → a new name.
   → It saves. The assignment and team names follow.
3. Rename to the name another rep in this event uses.
   → It saves. In Search Teams, that club shows one more team and one more rep.
4. Rename to a **different** club's name that isn't the rep's own.
   → It's refused: "… is the name of a different club."
5. View the event's public schedule while signed out.
   → No rep badge.

**Note:** the Search Registrations tree loads once when the page opens. After a rename, reopen the page to see the tree update. Search Teams is current.
