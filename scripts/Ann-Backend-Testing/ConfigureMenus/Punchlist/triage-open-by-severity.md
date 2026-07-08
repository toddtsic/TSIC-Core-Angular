# ConfigureMenus — Open Items, Prioritized by Severity

**Regenerated for Ann's review-with-Todd session, as of 2026-07-08.** 36 Open items.
Source: `punchlist.md` (Open status only). Many items carry compound tags (e.g. "Bug (role visibility) + Question") — bucketed here by **primary** severity. **Ⓣ** = flagged "Open — Todd discussion".
Also parked: **PL-034** (Sport-whitelist → shared helper) is **Awaiting Todd approval** — plan agreed, not counted in the Open list below.

---

## Breakdown

| 🔴 Bug | 🟡 Question | 🟢 Feature | 🔵 UX | ♿ A11y | Ⓣ Todd | **Total** |
|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| 9 | 9 | 1 | 16 | 1 | 5 | **36** |

---

## 🔴 Bugs (9)

- [ ] **PL-070** — Payment: checking "Team Full Payment Required" throws exception; Save spins forever
- [ ] **PL-063** — gate all four "regform name" fields to SuperUser only (Legacy parity) *(role visibility)*
- [ ] **PL-061** — Teams: audit four "Team Options" toggles — 2 vestigial, 1 misplaced, 1 needs explanation *(zombie settings + UX)*
- [ ] **PL-060** — Teams: Club Rep permissions need post-registration auto-lock or time-based gating *(operational risk / Feature)*
- [ ] **PL-053** — Player: move Registration Form, Multi-Player Min, Discount % to SuperUser-only; verify RegSaver placement *(role visibility + Question)*
- [ ] **PL-046** — Payment: Check/Mail-In/Balance section cleanup — TFPR move, two vestigial flags *(zombie settings + UX)*
- [ ] **PL-041** — Payment: Per-Unit Charges section must be SuperUser-only *(role visibility)*
- [ ] **PL-040** — Branding: review with Todd re: Widget menu, banner/bulletins display and sizing *(clipping + architecture Q)*
- [ ] **PL-039** — Branding: remove from Director (all non-SuperUser) menus; SuperUser only *(role visibility)*

---

## 🟡 Questions (9) — need a Todd decision

- [ ] **PL-069** — Mobile: confirm Store Contact Email is single-recipient; rename + tip if multi
- [ ] **PL-066** — "Push Directors" toggle: vestigial today; decide remove vs restore-and-move-to-Mobile
- [ ] **PL-064** — Ⓣ Coaches: Referee & Recruiter sections — confirm support, audit usage
- [ ] **PL-062** — Ⓣ clarify Club Rep confirmation text (currently shares Coaches templates via the Adult flow)
- [ ] **PL-052** — Two-banner architecture: `client-banner` widget vs `dashboard-hero` inline — standardize or keep distinct
- [ ] **PL-035** — General: confirm QBP Name is an override whose default is Event Name; wire fallback if so *(possible Bug)*
- [ ] **PL-032** — General: Display Name — is it needed, and it's edited in two places
- [ ] **PL-031** — General: Legacy fields Administrators & JobAi not carried forward; Expiration confirmation
- [ ] **PL-024** — Dropdown Options for Directors: should they see it under Configure, and how is it added?

---

## 🟢 Feature (1)

- [ ] **PL-038** — General: Duplicate Job Path under SuperUser Only as an editable field

---

## 🔵 UX (16)

### Layout / density (pairs with the width design brief)
- [ ] **PL-036** — Job Configuration (all tabs): overall display much tighter; defer to PL-022
- [ ] **PL-037** — General: Admin Expiry should sit directly below User Expiry in the narrowed layout *(may now be partly addressed by the recent User Expiry move — verify)*
- [ ] **PL-023** — Alternating row-color contrast too subtle across all `tsic-grid` tables *(touches shared grid styles)*

### Field placement / tab moves
- [ ] **PL-067** — Ⓣ Mobile: break Mobile Features into TSIC-Events and TSIC-Teams subsections
- [ ] **PL-065** — move "Show Team Name Only in Schedules" from Teams tab to Scheduling tab (Legacy parity)
- [ ] **PL-058** — Ⓣ relocate Roster Visibility checkboxes to Player and Coaches tabs; add explanatory copy
- [ ] **PL-044** — Payment: move Refund Policy to Payment tab; show Player / Club Rep by job type
- [ ] **PL-042** — Payment: restructure "Teams Full Payment Required" as a Teams subsection with two radio options

### Role visibility (UX-primary)
- [ ] **PL-045** — Ⓣ Payment: clarify Balance Due % and Mail-in Payment Warning (gating, confirmations, help text)
- [ ] **PL-043** — Payment: hide Processing Fee fieldset entirely for non-SuperUsers

### Content / copy / misc
- [ ] **PL-073** — Branding: Overlay Headline shows stale "CLONE 2022"; define fallback display when cloned job has no Banner Background image
- [ ] **PL-072** — Discount Codes: Start Date should default to today (creation date), not tomorrow
- [ ] **PL-057** — Player + Coaches: normalize oversized text in migrated Legacy RTE content
- [ ] **PL-028** — Discount Codes: Expiry & Status columns both say "Active"; expired codes can read "Active" in Status *(possible Bug)*
- [ ] **PL-026** — replace hidden trash can with lock icon + explanatory tooltip when delete is blocked
- [ ] **PL-025** — Nav Editor shows flag-gated items in the "This Job" tree even when hidden at render time *(possible Bug)*

---

## ♿ Accessibility (1)

- [ ] **PL-051** — `<label class="field-label">` elements across Configure not linked to their inputs (WCAG 1.3.1 / 3.3.2, S6853) *(pre-existing, whole-tab)*

---

## Ⓣ Open — Todd discussion (5, also listed above)

- [ ] **PL-067** — Mobile Features → TSIC-Events / TSIC-Teams subsections
- [ ] **PL-064** — Referee & Recruiter sections support/usage
- [ ] **PL-062** — Club Rep confirmation text (shared Adult templates)
- [ ] **PL-058** — Roster Visibility checkbox relocation
- [ ] **PL-045** — Balance Due % / Mail-in Payment Warning clarity

*(Separately: **PL-034** Sport-whitelist shared helper is Awaiting Todd approval — plan agreed, ready to implement on sign-off.)*

---

*Companion files: `../../TRIAGE-REMAINING-OPEN.md` (12 items) and `../../Accounting/Punchlist/triage-open-by-severity.md` (16 items).*
