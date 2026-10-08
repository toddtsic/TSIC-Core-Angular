# AR Triage — ranked for Todd

**Snapshot: 2026-10-07.** Rebuilt from `After-Release-Punchlist.md` at that moment, replacing the
10-06 snapshot — seven items changed state in between.
⚠ **State changes constantly** — rulings land, items close, Ann verifies — so **rebuild this from the
punchlist rather than trusting a copy more than a few days old.**

**Ordering principle:** harm happening now → time-bound commitments → wrong money on customer-facing
screens → blocked users with no workaround → friction and polish.
**"Ready?"** is ✅ build as written vs ⚠️ needs Todd's ruling first.

**Scope:** the items waiting on **Todd**. What is waiting on Ann is listed at the end, so the two
queues are not confused.

---

## Read this first — what is waiting on a deploy, not on Todd

**Both items that led the queue on 10-06 are now BUILT and waiting on a deploy, not on you.**
**AR-144** (insurance never offered to club-rep team players) was fixed 10-07; **AR-142** (duplicate
club names) was built 10-06 and merged. 🎯 **So nothing in this queue is actively costing money or
blocking a registration today** — the first thing that needs your keyboard is a time-bound
commitment, not an emergency.

⚠ **Deploy is now the critical path for five items, not code:** AR-142, AR-144, AR-118, AR-120
(part 3), AR-132. **Ann cannot verify any of them until they land, and testing early shows old
behaviour.**

---

## Time-bound commitments

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 1 | **128** | USA Lacrosse team-level override not ported | ⚠️ spec | ⏰ **STEPS needs it soon.** The legacy control is the spec; *"suppress validation for one team"* |

## Money and figures

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 2 | **122** | Payment screen columns + figures | ✅ #1 #2 #4 · ⚠️ #3 | #3 is the owed-vs-LADT-fee design conflict — the only part that needs you |
| 3 | **125** | Search summary counts Corrections | ⚠️ ground 1 only | **Ground 2 closed 10-07** — a Correction is a payment by design. Ground 1, remove vs role-gate, is still yours |
| 4 | **124** | Overpayment not shown | ⚠️ policy | Research found **nobody overpaid** (136/136 paid what was billed). A policy answer, not a bug |

## Blocked users

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 5 | **135** | Family cannot correct name / DOB / gender | ⚠️ snapshot? | Blocks USA Lacrosse validation. ⭐ starred by Ann |
| 6 | **137** | Coach cannot edit own name | ⚠️ permission? | Same block, other side of the house. **Rule with 135** |
| 7 | **139** | No admin coach lookup / reconcile | ⚠️ find vs build | With 137 this is a dead end with nobody holding the fix |
| 8 | **152** | Club Rep Library unreachable with no role | ⚠️ routing | **Part 1.** A rep with no active registrations cannot reach or create their library — the preparation case AR-106 was closed on |
| 9 | **151** | Privilege-separation refusals | ⚠️ ruling + check | Four parts. The copy is three strings, but the **check fix must ship with it** or the new wording is false on a mixed account. See its own FOR TODD TO CONSIDER block |
| 10 | **133** | League/A/D/T tree undiscoverable | ⚠️ check first | 📣 **Ann flagged this important to users** |
| 11 | **134** | DOB not visible at date entry | ✅ spec given | Prevents the 135 mismatch in the first place |

## Testability and enablers

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 12 | **141** | Test card for SuperUser | ⚠️ guards | Makes 137 / 139 / 140 testable at all. Scope it server-side |
| 13 | **140** | Remove the one-time emailed code | ⚠️ ruling | Removes a verification step. One question decides it |
| 14 | **138** | Coach flow — edit on the first screen | ✅ | **Decides where 137 lands** — do them together |
| 15 | **136** | Edit Staff Account menu | ✅ copy AR-112 | The door; **137 is the room** |

## Presentation and copy

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 16 | **149** | Saved changes do not appear until you leave and come back | ⚠️ sweep | **Part 2 first** — a validator reading a stale USA Lacrosse number is a wrong RESULT, not a wrong picture. It blocks 134 |
| 17 | **148** | Email accepts anything; phone rejects its own format | ✅ likely | Check the order of formatting vs validation on the phone field first; that is probably the whole of it |
| 18 | **145** | USA Lacrosse `Email?` flag misses `Other issue?` | ⚠️ | 📎 two screenshots in `images/` |
| 19 | **147** | Concluded site collapses to one line | ⚠️ needs prod | Mechanism researched; the scope question needs prod |
| 20 | **152** | Drop "Currently in" from the library | ⚠️ ruling | **Part 2.** ⚠ This asks you to remove wording **you added deliberately on 09-27**, for the same reason Ann now gives — that the page belongs to no one event |
| 21 | **143** | Collapse Add-a-Team to a `+` and a popup | ⚠️ ruling | **REC 1 only.** Ann held the item open 10-07 and **ACCEPTED the library ruling**; she reviews REC 1 with Chelsea. The stale-listing defect claim is **withdrawn** |
| 22 | **130** | Chelsea navigation | ✅ mostly done | Ruled 10-02, two parts built |
| 23 | **111** | Reply-To on human-sent mail | ⏸ held open | A real inbox, not the Email Log. Ann emailed you the example |

## Reconsider

⚠ **These read as settled in the status column and are not.** Todd ruled, Ann pushed back, and they
have gone quiet rather than getting a second ruling.

| AR | Item | Contested since |
|:--|:--|:--|
| **129** | Event dates on Role selection — ⚠ **reopened on a PRINCIPLE, not a repeat**: the test is **pertinence, not accuracy**, and her point 2 of two has not arrived | 10-06 |
| **114** | LADT add flow — *"much easier before"* | 09-27 |
| **115** | Login job list — Brenda already uses the typeahead | 09-27 |

✅ **AR-118 came OFF this list on 10-07** — you built the free-text Search over team, club and club
rep name and verified it locally. **That was the contested half:** the capability existed in the API
and was simply missing from that screen.

⚠ **One collision worth deciding once rather than twice. Ann's 10-06 placement rule** — *"payment
figures on the payment screen, team counts on the teams screen, dates on the banners"* — **is the
same argument AR-129 makes, and AR-121 was closed against it on 10-07.** 🎯 **Her rule and that
closure cannot both stand on the role-selection screen.**

---

## Three things to say with it

- **Needs judgment, not keyboard:** **124, 140, 151, 152 part 2, 143 REC 1**, and the three
  contested. These will not move no matter how much time is spent coding.
- **Two coupling traps:** **137 before 138** means doing it twice; **151's copy without its check
  fix** ships a message that is FALSE on any account already holding two role types.
- **Deploy, not code, is the bottleneck this week.** Five built items are waiting on it, and Ann
  cannot verify any of them until they land.

---

## Not on this list — waiting on Ann, or on a deploy

| AR | Item | What is needed |
|:--|:--|:--|
| **142** | Duplicate club names | ✅ **BUILT 10-06, merged, NOT DEPLOYED.** 📎 Design + how-to-test doc: `AR-142-Club-Rep-Same-Name-Clubs.html`. ⚠ **Standing gate: no further club-rep items are considered until this response is fully vetted** |
| **144** | Insurance never offered to club-rep team players | ✅ **FIXED 10-07, NOT DEPLOYED** — Ann to verify |
| **118** | Free-text Search on Search / Teams | ✅ **BUILT + verified locally 10-07, NOT DEPLOYED** |
| **120** | Confirmation screen presentation | ✅ **DONE 10-07** — part 3 not deployed |
| **132** | Job Clone permissions | 🟡 Built, pushed, **NOT DEPLOYED** — cannot be exercised until the next **job clone**. ⚠ **Still diverges from Ann's ruling:** Edit + Add left ON where she ruled all three off |
| **108** | Team Breakdown moves with the team | 🟡 Fixed, **awaiting deploy** — testing early shows old behaviour |
| **103** | Club rep name in parentheses | ✅ **DONE** — on prod since 10-05, Ann to verify |
| **151** | Privilege-separation refusals | ❓ **Ann owes two answers:** login-vs-sign-up, and which reading her SuperUser example meant |
| **152** | Club Rep Library | ❓ **Ann owes:** whether the age-group badge goes with the "Currently in" wording |

⚠ **Two state cells in the punchlist's own table are STALE and read as open when the entry says
otherwise: AR-127** (heading says VERIFIED BY ANN 10-06 — CLOSED) **and AR-131** (heading says
WON'T DO, researched 10-02). ⚙ **AR-142's heading also still reads `UNRESEARCHED` although the body
carries the 10-06 build.** **Trust the entry, not the row, until they are reconciled.**

⚪ **Draft D-13 — duplicate team names in the library — was CLOSED 10-07 as NOT A PROBLEM.** The data
already carries **144 same-name groups in the library** and **703 across registered teams**, one row
per age cohort, separated by Grad Year. **Nothing to build.**

🗑 **AR-150 was REMOVED at Ann's request on 10-07** (admin Delete for a team with no accounting).
**The number is retired and must not be reassigned** — the gap between AR-149 and AR-151 is a
removal, not a renumbering.
