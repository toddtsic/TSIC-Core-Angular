# AR Triage — ranked for Todd

**Snapshot: 2026-10-06.** Built from `After-Release-Punchlist.md` at that moment.
⚠ **State changes constantly** — rulings land, items close, Ann verifies — so **rebuild this from the
punchlist rather than trusting a copy more than a few days old.**

**Ordering principle:** harm happening now → time-bound commitments → wrong money on customer-facing
screens → blocked users with no workaround → friction and polish.
**"Ready?"** is ✅ build as written vs ⚠️ needs Todd's ruling first.

**Scope:** the items waiting on **Todd**. What is waiting on Ann is listed at the end, so the two
queues are not confused.

---

## Band 1 — happening right now

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 1 | **144** | Insurance never offered to club-rep team players | ⚠️ pick a fix | **D2 is taking registrations today.** The offer is fetched once per flow — every registration that passes it is lost permanently. Pickleball already has the fix |
| 2 | **142** | Duplicate **club** names blocked | ⚠️ ruling | Support email already arriving (`SLS inquiry`). Blocks account creation, a 2nd registration, and team transfer |
| 3 | **143a** | Teams screen does not reflect other reps' actions | ✅ it is a defect | Waitlisted teams do not show. Counts refresh, the listing does not. Reps act on a false picture — and multi-rep is the **normal** case per AR-142 |

## Band 2 — time-bound

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 4 | **128** | USA Lacrosse team-level override not ported | ⚠️ spec | **STEPS needs it soon.** The legacy control is the spec; *"suppress validation"* needs its meaning pinned |

## Band 3 — wrong figures on customer-facing screens

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 5 | **119** | Paid total overstated by adjustments | ✅ ruled | Ann ruled: adjustments are never a Paid amount. **Guard:** removing them must not change Owed |
| 6 | **125** | Search summary counts Corrections | ✅ ground 2 · ⚠️ ground 1 | Same rule as 119. Ground 1 (remove vs role-gate) is Todd's |
| 7 | **122** | Payment screen columns + figures | ✅ #1 #2 #4 · ⚠️ #3 | #3 is the owed-vs-LADT-fee design conflict |
| 8 | **124** | Overpayment not shown | ⚠️ policy | Research found **nobody overpaid** — this is now a policy answer, not a bug |

## Band 4 — blocked users, no workaround

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 9 | **133** | League/A/D/T tree undiscoverable | ⚠️ check first | **Ann flagged this important to users.** Check whether **All** already fails to expand fully — may be a cheap bug fix before any redesign |
| 10 | **135** | Family cannot correct name / DOB / gender | ⚠️ snapshot? | Blocks USA Lacrosse validation. Key question: do registrations **copy** these fields or **read** the account? |
| 11 | **137** | Coach cannot edit own name | ⚠️ permission? | Same block, other side of the house. **Rule with 135** |
| 12 | **134** | DOB not visible at date entry | ✅ spec given | Prevents the mismatch in the first place |
| 13 | **139** | No admin coach lookup / reconcile | ⚠️ find vs build | With 137 this is a dead end with nobody holding a tool |

## Band 5 — unblocks the above, then friction

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 14 | **141** | `424242424242` for SuperUser | ⚠️ guards | Makes 137 / 139 / 140 testable at all. Server-side role check, mark the record, exclude from reconcile |
| 15 | **140** | Remove the one-time emailed code | ⚠️ ruling | Removes a verification step. One question decides it: **can the address be changed in-flow?** |
| 16 | **138** | Coach flow — edit on the first screen | ✅ | **Decides where 137 lands** — do them together or 137 moves twice |
| 17 | **136** | Edit Staff Account menu | ✅ copy AR-112 | The door; **137 is the room** |

## Band 6 — polish and decisions with no clock

| # | AR | Item | Ready? | Note |
|:--|:--|:--|:--|:--|
| 18 | **120** | Confirmation screen presentation | ✅ | Three changes; check whether email and online share a template |
| 19 | **130** | Chelsea navigation | ✅ mostly done | Ruled 10-02, two parts built |
| 20 | **103** | Club rep name in parentheses | ✅ | Small |
| 20b | **121** | Strip the badges and role list | ✅ answered 10-06 | Ann ruled: these are **links, not information surfaces** — a duplicated figure drifts even once fixed. ⚠ **Check 5 (fly-in count: KEEP and CORRECT) is still outstanding and still counts Waitlisted + Dropped** |
| 21 | **143b** | Collapse Add-a-Team to a popup | ⚠️ ruling | The design half of 143 — ⛔ **do not let it close the bug in 143a** |
| 22 | **143c** | **Remove the Club Teams Library** | ⚠️ big ruling | Retires draft **group D** (10 parked items) and moots **AR-131** and **AR-106**. Decide what happens to **existing library data** — hiding the surface is reversible, deleting is not |

## Last — ruled, then contested, and quiet since

⚠ **These read as settled in the status column and are not.** Todd ruled, Ann pushed back, and they
have gone quiet rather than getting a second ruling.

| AR | Item | Contested since |
|:--|:--|:--|
| **129** | Event dates on Role selection — ⚠ **reopened on a PRINCIPLE, not a repeat**: the test is **pertinence, not accuracy**. ⏳ Ann's point 2 of two still to come | 10-06 |
| **118** | Club filter exists in the API, missing from Search / Teams | 09-30 |
| **114** | LADT add flow — *"much easier before"* | 09-27 |
| **115** | Login job list — Brenda already uses the typeahead | 09-27 |
| **111** | Reply-To on human-sent mail — a real inbox, not the Email Log | 09-27 |

---

## Three things to say with it

- **Needs judgment, not keyboard:** **142, 124, 140, 143c**, and the four contested. These will not
  move no matter how much time is spent coding.
- **Two coupling traps:** **137 before 138** means doing it twice; building **143's popup** without
  fixing **143a** leaves the screen wrong for anyone who does not click it.
- **Urgency stops after band 3.** Everything from band 4 down is real, but nothing is on fire — if
  only bands 1–3 land this week, that is the right outcome.

---

## Not on this list — waiting on Ann

| AR | Item | What is needed |
|:--|:--|:--|
| **132** | Job Clone permissions | 🟡 **Acknowledged 10-06, NOT tested** — cannot be exercised until the next **job clone**, and still not deployed. ✅ **Design ACCEPTED 10-06** — Edit + Add ON, Delete OFF; her all-three-off ruling is superseded |
| **108** | Team Breakdown moves with the team | Fixed, **awaiting deploy** — testing early shows old behaviour |

✅ **AR-126 and AR-127 were VERIFIED AND CLOSED by Ann on 2026-10-06.** ⚠ **AR-127's close
deliberately ACCEPTS its divergence** — Height/Weight rejoin the recruiting envelope and there is
**no profile-editor strip**, which is the opposite shape to her 09-30 scope ruling. She was shown the
divergence twice and closed it anyway, so **the strip is not outstanding work.**

⚠ **One divergence from an Ann ruling is still open and sits in her queue: AR-132** — Edit + Add
left **ON**, where she ruled all three permissions off.
