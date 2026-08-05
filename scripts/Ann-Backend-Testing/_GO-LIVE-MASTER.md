# 🚩 Last-Pass Master List — everything still needing action

**Compiled 2026-08-05 (Ann + Claude), last pass before go-live.** Consolidated from a **full-status read** of both punchlists (`Admin-Menus-Punchlist.md` = AM-, `Payment-Test-Punchlist.md` = PL-) plus the go-live checklist — not a keyword grep (grep silently misses items). The per-item Status lines in those files remain the source of truth; **regenerate this whenever items open/close/reopen.** Anything already verified / won't-fix / closed is intentionally omitted.

---

## 🔴 NEEDS TODD — active

**No build/fix items remain on either punchlist as of 2026-08-05.** What's left below is his own E2E, one tabled discussion, and the cutover-only verifies further down.

| Item | Where | Priority | Ask |
|---|---|---|---|
| **PL-036** | PL | Todd E2E | Coach Approval Queue sort tech-debt — his end-to-end (not Ann's) |
| **PL-037** | PL | Todd E2E | *(item CLOSED)* — mixed cart (active + waitlist teams) → Pay Balance Due → WL badge renders on the payment grid. **Needs an API restart first** (badge depends on the server-computed `isWaitlisted` / `ageGroupDisplayName` fields) |
| **AM-043 / 044 / 045** | AM | 🔵 Tabled | Bulletin-AI cluster — needs a **Todd + Ann walkthrough** (incl. Ann's "AI Format appears to do nothing" note). *When it reopens, check AM-045 first: the 08-04/08-05 RTE work (curated font sizes on every editor, link-only Image) may already have overtaken the font-size/type ask* |

---

## 🅿️ PARKED / DEFERRED — post-launch (agreed)

| Item | Where | What |
|---|---|---|
| **AM-041 → CLOSED 08-05 / AM-089 deferred** | AM | **Donations are OFF structurally** — the SuperUser Payment Policy card was **removed entirely** from Job Settings → Payment (`6499531f`), so nobody can enable donations from the app. **AM-089 stays the hard prerequisite** for the day a client wants them: itemize the donation on the confirmation (tax) + surface it in the Director fly-in + allow a gift at $0 owed — **and first settle that `FeeDonation` is assigned, not accumulated, with no donation column on the payment-ledger row** (a repeat donor overwrites the first gift). Restoring the card is the last step of that build; the markup is preserved as a comment in place |
| **AM-063** | AM | Draft-with-AI reuse of prior email copy — **PARKED 08-05** (`c4a8c606`). Post-go-live; the AI is the small part, the work is a cross-job read of email bodies behind the `CanCrossCustomerJobs` gate. Investigation banked in the item |
| **AM-074** | AM | Headshot on Review (Todd+Ann agree; revisit post-launch) |
| **AM-087** | AM | CAC division-then-team sort (Todd+Ann agree, image transcribed, build-ready) |
| **AM-016** | AM | Widget Editor public settings (from CR-117) |
| **AM-017** | AM | Theme editor LocalStorage + `/brand-preview` route (from CR-125) |
| **AM-001** | AM | Bulletins RTE font controls — re-raised as AM-045 |
| **PL-035** | PL | eCheck ARB tooling parity (charge/setup works; surrounding tooling CC-centric) |
| **PL-025** | PL | Pre-submit intra-cart contention warning (persistent notice already shipped + verified) |
| **PL-063** | PL | Multiplayer (sibling) discount. **Premise corrected — Hero's never used it** (all 23 Hero's jobs NULL back to 2020; the 17 configured jobs are all Cape St. Claire `_chiuso`; no sibling code ever). Not a parity gap, so nothing was lost. Design banked: never-typed SuperUser rule row → writes `FeeDiscountMp`. Stopgap today = Hero's issues an ordinary discount code. **→ Ann to confirm with Hero's whether they want one going forward** |

---

## ⏳ GO-LIVE / PRODUCTION-ONLY VERIFY (cannot test locally — email/ADN/migration)

| Item | Where | What |
|---|---|---|
| **PL-061** | PL | 🔴 **HIGH** — club-rep balance-due payment sent no confirmation email; verify with a REAL CC + eCheck charge (one email/payment, eCheck settlement-pending banner, watch for a 500 on first charge). SES doesn't send off-prod |
| **PL-028** | PL | External ARB cancel/auto-terminate → "Refresh ARB Statuses" button; cancel a test sub in the ADN portal → Refresh → stored status flips to canceled |
| **PL-027** | PL | ARB Subscription — live test (dev-only "Stored record…" note gone in prod; exercise Cancel on a test sub) |
| **AM-018** | AM | CC/BCC office copies on registration confirmations reach the office inbox |
| **AM-020** | AM | Password-reset email delivery (username / family-email / duplicate → one per account) |
| **AM-036** | AM | Shoulberg club-rep permission migration spot-check vs Legacy |
| **AM-048** | AM | Re-run the PROD nav seed (USA Lacrosse rename) at cutover |
| **Email sweep** | both | ALL email surfaces only truly send in Production — full surface list in the go-live memory (registration confirmations incl. CC/BCC, batch + fly-in email, ARB defensive + expiring-cards, password reset, reschedule emails, invite-to-register, USAL one-time-code, batch-completion receipt) |

---

## 🔎 BUILT 08-05 — awaiting Ann's verify

| Item | Where | What |
|---|---|---|
| **AM-065 pt1** | AM | Club Rep + coach / referee / recruiter / adult-waiver confirmation sections now **start open** regardless of the QuickLink / registration-availability toggles (5 disclosure signals → `signal(true)`; no template change). Also retires the deferred bug where flipping a toggle mid-edit tore down the RTE and ate unblurred copy. **Verify on a job with those flows OFF** — steps in the punchlist item |

---

### Status snapshot (2026-08-05, regenerated after the AM-041/089 + AM-063 rulings)
- **The build queue is EMPTY on both punchlists.** Everything outstanding is (a) Todd's own E2E — PL-036, PL-037 *(restart the API first)*, (b) the tabled bulletin-AI cluster, (c) cutover-only verifies that cannot run off-prod, (d) Ann's local verify of AM-065 pt1, (e) one Ann conversation: **PL-063 — ask Hero's whether they want a sibling discount going forward** (they never had one).

- **Closed this pass:** AM-015 (Job Clone — covered by AM-071…081), AM-037 (refund policy — via AM-009/033), AM-069 (Convert button WON'T-DO accepted), AM-088 (Coach confirmations closed, accepted), **AM-084 (pt1 accepted as shipped — Ann withdrew the rewording; pt2 already verified. Item fully closed, no code change)**, **AM-075 (closed with the CURRENT event-dates copy — Ann's revision not applied; hint retains its two documented inaccuracies knowingly, `eventEndInPast` banner is the guardrail)**, **AM-082 (NON-ISSUE — retesting did not reproduce the Select-Events bottom-scroll; go-live blocker withdrawn, no fix shipped, investigation banked in the item)**.
- **⚠ There is no longer a go-live blocker on this list.** AM-082 was the only item carrying that tag.
- **Verified this pass:** AM-011/067/068/070/073/076/077/078/079/080/081/084/085/086, PL-060.
- **All of Todd's recent decisions reviewed by Ann** — none outstanding.
