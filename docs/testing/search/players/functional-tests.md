# Search Players — Functional Tests

**Route:** `/:jobPath/search/players`
**Guard:** `authGuard` + `requireAdmin: true`
**Component:** `RegistrationSearchComponent`

---

## 1. Page Load & Filters

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 1.1 | Navigate to `/:jobPath/search/players` | Page loads, filter bar visible, no errors in console | |
| 1.2 | Filter dropdowns populate on load | Role, Pay Status, Position, Club dropdowns have options | |
| 1.3 | LADT tree loads | League > Agegroup > Division > Team hierarchy displays correctly | |
| 1.4 | Click "More Filters" | Expanded filter sections appear (Name, Email, Phone, Gender, Grade, School, etc.) | |
| 1.5 | Click "Less Filters" | Expanded filters collapse back to compact bar | |

## 2. Search & Filter Behavior

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 2.1 | Select a filter, click Search | Results populate in grid | |
| 2.2 | Active filter chips appear | Chips strip shows selected filters with X buttons | |
| 2.3 | Click X on a filter chip | Filter removed, chip disappears | |
| 2.4 | Click "Clear Filters" | All filters reset, chips cleared | |
| 2.5 | Search by name text | Matching registrations returned | |
| 2.6 | Search by email | Matching registrations returned | |
| 2.7 | Search by phone | Matching registrations returned | |
| 2.8 | LADT tree: check a parent node | All child nodes auto-check | |
| 2.9 | LADT tree: uncheck a parent node | All child nodes auto-uncheck | |
| 2.10 | LADT tree: check some children only | Parent shows indeterminate checkbox state | |
| 2.11 | Combine multiple filters (Role + Pay Status + LADT) | Results narrow correctly to intersection | |
| 2.12 | Search with no matching results | Empty state displayed (no errors) | |

## 3. Grid Behavior

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 3.1 | Pagination: navigate pages | Next/prev pages load correctly | |
| 3.2 | Column sorting: click a header | Rows sort ascending, click again for descending | |
| 3.3 | Frozen columns (checkbox, row #, name) | Stay pinned while scrolling horizontally | |
| 3.4 | Footer aggregates | $Paid and $Owed totals display at bottom | |
| 3.5 | Owed column color-coding | Red for balance owed, green for paid up | |
| 3.6 | Row numbering | Sequential row numbers across pages | |
| 3.7 | Checkbox selection | Select/deselect individual rows, select-all header works | |

## 4. Detail Panel

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 4.1 | Click a player name in grid | Detail panel slides open on right side | |
| 4.2 | Details tab: profile info | Player-specific fields (uniform #) and non-player fields (club, special requests) display | |
| 4.3 | Details tab: edit family contact | Update mom/dad name, email, phone — save succeeds, values persist on reload | |
| 4.4 | Details tab: edit demographics | Update email, cell, address, city, state, ZIP — save succeeds | |
| 4.5 | Accounting tab: transaction history | Payment records display with method, amount, date, comment | |
| 4.6 | Accounting tab: edit check/correction record | Update check # or comment — save succeeds | |
| 4.7 | Email tab: compose with tokens | Token buttons insert placeholders (!PERSON, !EMAIL, !AMTOWED, etc.) | |
| 4.8 | Email tab: send email | Email sends successfully, confirmation shown | |
| 4.9 | Close panel | Panel closes, grid returns to full width | |

## 5. Add Payment Modal

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 5.1 | Record a check payment | Enter amount + optional check # — appears in accounting history | |
| 5.2 | Record a correction (positive) | Amount added to balance | |
| 5.3 | Record a correction (negative) | Amount subtracted from balance | |
| 5.4 | Charge a credit card | Enter card details — confirmation dialog appears, charge processes | |
| 5.5 | CC validation | Amount <= owed total, card fields required | |
| 5.6 | Check validation | Amount > 0 required | |
| 5.7 | Correction validation | Amount != 0 required | |
| 5.8 | Cancel modal | No payment recorded, modal closes | |

## 6. Refund Modal

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 6.1 | Process full refund | Amount pre-filled, confirmation dialog, refund applied | |
| 6.2 | Process partial refund | Adjust amount down, correct partial amount applied | |
| 6.3 | Validation: amount > 0 | Cannot submit zero or negative | |
| 6.4 | Validation: amount <= original paid | Cannot refund more than was paid | |
| 6.5 | Validation: reason required | Cannot submit without reason | |
| 6.6 | Cancel modal | No refund processed, modal closes | |

## 7. Batch Email Modal

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 7.1 | Select multiple rows, click "Email Selected" | Modal opens showing recipient count | |
| 7.2 | Insert tokens into body | Token buttons add placeholders to email body | |
| 7.3 | Invite link token | Select target event — !INVITE_LINK token becomes available | |
| 7.4 | Club rep invite link token | Select target event — !CLUBREP_INVITE_LINK available (future jobs only) | |
| 7.5 | Send batch email | Confirmation dialog, then result summary (sent/failed/opted-out counts) | |
| 7.6 | Cancel modal | No emails sent, modal closes | |

## 8. Admin Actions

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 8.1 | Change Job | Select target event in detail panel — confirmation dialog, registration moves | |
| 8.2 | Delete Registration | Confirmation dialog, registration removed from results | |
| 8.3 | Email OptOut toggle | Click badge in grid column — status toggles between opted-in/opted-out | |
| 8.4 | View ARB Subscription | Accounting tab shows subscription details when active | |
| 8.5 | Cancel ARB Subscription | Cancel button, confirmation, subscription cancelled | |

## 9. Mobile

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 9.1 | Mobile viewport | Quick lookup interface shown instead of grid/filters | |
| 9.2 | Type a name | Debounced results appear as cards (~400ms delay) | |
| 9.3 | "Load More" button | Fetches next page of results | |
| 9.4 | Tap a result card | Expands inline with registration detail | |

## 10. Export

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 10.1 | Click "Export to Excel" | Spreadsheet downloads with current filtered results | |
| 10.2 | Export with no results | Graceful handling (empty file or message) | |

## 11. Edge Cases

| # | Test | Expected Result | Pass |
|---|------|-----------------|------|
| 11.1 | Non-admin user navigates to route | Redirected (guard blocks access) | |
| 11.2 | Unauthenticated user | Redirected to login | |
| 11.3 | Job with no registrations | Empty state, no errors | |
| 11.4 | Very long player name / email | Grid cell truncates gracefully, detail panel wraps | |
| 11.5 | Rapid filter changes | No race conditions, latest results displayed | |

---

**Last Updated:** 2026-03-19
**Tester:** _______________
**Environment:** _______________
