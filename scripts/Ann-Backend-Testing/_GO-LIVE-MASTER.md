# 🚩 Last-Pass Master List — everything still needing action

**Compiled 2026-08-05 (Ann + Claude), last pass before go-live.** Consolidated from a **full-status read** of both punchlists (`Admin-Menus-Punchlist.md` = AM-, `Payment-Test-Punchlist.md` = PL-) plus the go-live checklist — not a keyword grep (grep silently misses items). The per-item Status lines in those files remain the source of truth; **regenerate this whenever items open/close/reopen.** Anything already verified / won't-fix / closed is intentionally omitted.

---

## 🔴 NEEDS TODD — build / fix (active)

| Item | Where | Priority | Ask |
|---|---|---|---|
| **AM-082** | AM | 🔴 **GO-LIVE BLOCKER** | CAC Select Events opens scrolled to the BOTTOM — must land at the **TOP** (do team + adult wizards too — shared shell) |
| **AM-065 pt1** | AM | 🔴 Reopened | Club Rep **AND all Adult** confirmation/waiver text boxes must be editable **all the time**, NOT gated on the QuickLink / registration-availability toggle (Player already is) |
| **AM-075** | AM | 🔴 Reopened | Job-Clone event-dates hint → Ann's wording: *"Leave blank to shift the source's event dates forward one year. Check that Event End is in the future — on any job type, a past end date opens the new job concluded (no registration links)."* |
| **AM-041 + AM-089** | AM | 🔴 Gate | Donations must **report** a donation (itemized on confirmation for tax + Director/fly-in view + allow at $0 owed) **before** the feature can be enabled by anyone. AM-089 is the hard prerequisite; AM-041 stays off until it ships |
| **PL-037** | PL | 🔴 Reopened | Club Rep "Pay Balance Due" — waitlist-badge / balance behavior |
| **PL-063** | PL | ◻ Open (new) | Multiplayer (sibling) Discount % not functional — **Hero's Lacrosse Players** parity: recover Hero's Legacy values (currently NULL) + surface the config field + wire `FeeDiscountMp` (reserved slot already carried through the money math) |
| **PL-036** | PL | Todd E2E | Coach Approval Queue sort tech-debt — his end-to-end (not Ann's) |
| **AM-063** | AM | ◻ Low | Draft-with-AI: reuse prior email copy (enhancement) |
| **AM-043 / 044 / 045** | AM | 🔵 Tabled | Bulletin-AI cluster — needs a **Todd + Ann walkthrough** (incl. Ann's "AI Format appears to do nothing" note) |

---

## 🅿️ PARKED / DEFERRED — post-launch (agreed)

| Item | Where | What |
|---|---|---|
| **AM-074** | AM | Headshot on Review (Todd+Ann agree; revisit post-launch) |
| **AM-087** | AM | CAC division-then-team sort (Todd+Ann agree, image transcribed, build-ready) |
| **AM-016** | AM | Widget Editor public settings (from CR-117) |
| **AM-017** | AM | Theme editor LocalStorage + `/brand-preview` route (from CR-125) |
| **AM-001** | AM | Bulletins RTE font controls — re-raised as AM-045 |
| **PL-035** | PL | eCheck ARB tooling parity (charge/setup works; surrounding tooling CC-centric) |
| **PL-025** | PL | Pre-submit intra-cart contention warning (persistent notice already shipped + verified) |

---

## ⏳ GO-LIVE / PRODUCTION-ONLY VERIFY (cannot test locally — email/ADN/migration)

| Item | Where | What |
|---|---|---|
| **AM-082** | AM | *(the blocker above)* — must be **fixed before** launch, then re-verify |
| **PL-061** | PL | 🔴 **HIGH** — club-rep balance-due payment sent no confirmation email; verify with a REAL CC + eCheck charge (one email/payment, eCheck settlement-pending banner, watch for a 500 on first charge). SES doesn't send off-prod |
| **PL-028** | PL | External ARB cancel/auto-terminate → "Refresh ARB Statuses" button; cancel a test sub in the ADN portal → Refresh → stored status flips to canceled |
| **PL-027** | PL | ARB Subscription — live test (dev-only "Stored record…" note gone in prod; exercise Cancel on a test sub) |
| **AM-018** | AM | CC/BCC office copies on registration confirmations reach the office inbox |
| **AM-020** | AM | Password-reset email delivery (username / family-email / duplicate → one per account) |
| **AM-036** | AM | Shoulberg club-rep permission migration spot-check vs Legacy |
| **AM-048** | AM | Re-run the PROD nav seed (USA Lacrosse rename) at cutover |
| **Email sweep** | both | ALL email surfaces only truly send in Production — full surface list in the go-live memory (registration confirmations incl. CC/BCC, batch + fly-in email, ARB defensive + expiring-cards, password reset, reschedule emails, invite-to-register, USAL one-time-code, batch-completion receipt) |

---

### Status snapshot (2026-08-05)
- **Closed this pass:** AM-015 (Job Clone — covered by AM-071…081), AM-037 (refund policy — via AM-009/033), AM-069 (Convert button WON'T-DO accepted), AM-088 (Coach confirmations closed, accepted), **AM-084 (pt1 accepted as shipped — Ann withdrew the rewording; pt2 already verified. Item fully closed, no code change)**.
- **Verified this pass:** AM-011/067/068/070/073/076/077/078/079/080/081/084/085/086, PL-060.
- **All of Todd's recent decisions reviewed by Ann** — none outstanding.
