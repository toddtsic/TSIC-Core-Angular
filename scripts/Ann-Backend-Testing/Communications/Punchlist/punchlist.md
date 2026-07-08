# Communications - Punch List

**Tester:** Ann
**Date Started:** 2026-05-02
**Status:** In Progress

---

## How to Read Severity

| Label | Meaning |
|-------|---------|
| Bug | Something is broken or produces wrong results |
| UX | It works but is confusing, ugly, or hard to use |
| Question | Not sure if this is right -- need to ask Todd |

## How to Read Status

| Label | Meaning |
|-------|---------|
| Open | Not yet looked at |
| Fixed | Todd/Claude fixed it |
| Won't Fix | Intentional behavior, not changing |

---

## Test Areas

Use these as a guide for what to walk through. You don't have to go in order.

- [ ] **Bulletins** -- Public-facing tournament bulletins, director preview/public view
- [ ] **Email Templates** -- Registration confirmations, receipts, batch emails
- [ ] **Batch Email** -- Search/Registrations email sends, templating, scheduling
- [ ] **Notifications** -- Push notifications, in-app messages, alerts
- [ ] **AI-Assisted Composition** -- AI email drafting in search/registrations

---

## Punch List Items

### PL-001: Director's Public View should show the full bulletin
- **Area**: Bulletins
- **What I did**: Used the Public View as a Director to preview what the public sees on a tournament site
- **What I expected**: Full bulletin visible — same as a public visitor would see
- **What happened**: Bulletin is not shown in the Director's Public View. Directors never see what their public sees, so they don't know how their bulletin renders. The Public View should match the actual public experience exactly, including the full bulletin.
- **Severity**: Bug
- **Status**: Fixed

