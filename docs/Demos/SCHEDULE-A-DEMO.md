# Schedule a Demo — How It Works

> **Last Updated**: 2026-02-23
> **Calendly URL**: https://calendly.com/demo-teamsportsinfo/30min
> **Google Workspace trial expires**: March 9, 2026 (downgrade to Business Starter $7/mo before then)

---

## Overview

The "Book a Demo" feature on the `/tsic` corporate landing page lets prospective customers schedule a 30-minute demo call directly from the website. It uses three pieces of infrastructure:

| Layer | Service | Account | Purpose |
|-------|---------|---------|---------|
| Calendar backend | Google Workspace | `demo@teamsportsinfo.com` | Stores events, manages availability, sends Google Meet links |
| Scheduling frontend | Calendly (free tier) | `demo@teamsportsinfo.com` | Embeddable calendar picker, booking form, confirmation emails |
| Website embed | Angular component | `tsic-landing.component` | Inline Calendly widget on the landing page |

---

## What Happens When Someone Books a Demo

### Step-by-step flow

1. **Visitor arrives at `/tsic`** and scrolls to the "Ready to See It in Action?" section
2. **Calendly inline widget loads** — shows a calendar with available dates/times pulled from the `demo@teamsportsinfo.com` Google Calendar
3. **Visitor picks a date and time slot**
4. **Visitor enters their info** — name, email, and any questions (Calendly's booking form)
5. **Visitor confirms the booking**

### What happens automatically after booking

| What | Who | Details |
|------|-----|---------|
| **Confirmation email** | Visitor (invitee) | Sent by Calendly with date, time, and Google Meet link. Includes `.ics` calendar attachment so it lands on their calendar. |
| **Host notification email** | `demo@teamsportsinfo.com` (NetworkSolutions mailbox) | Sent by `notifications@calendly.com` with the invitee's name, email, and answers. **WARNING: Nobody checks this mailbox by default — see "Team Access" below to set up forwarding so real people actually see these.** |
| **Google Calendar event created** | `demo@teamsportsinfo.com` calendar | Event auto-created with Google Meet video link attached. The time slot is now blocked (no double-booking). |
| **Google Calendar notification** | `demo@teamsportsinfo.com` | Google Calendar's own reminder notifications (configurable — default 10 min before). |

### Rescheduling and cancellation

- The visitor's confirmation email includes **Reschedule** and **Cancel** links
- Rescheduling opens Calendly's rebooking flow — new slot picked, old slot freed
- Cancellation removes the event from Google Calendar and frees the slot
- Calendly sends reschedule/cancel notification emails to `demo@teamsportsinfo.com` (the NetworkSolutions mailbox). **Again — unless forwarding is set up (Option B below), nobody will see these.**

---

## Team Access: Getting Todd, Ann, and Chelsea Notified

The Calendly **free plan** only supports one user account. It does NOT natively send booking notifications to multiple people. Here's how to get all three team members in the loop:

### Option A: Google Calendar Sharing (Recommended — Free)

Share the `demo@teamsportsinfo.com` Google Calendar with each person's Gmail so they can **see all booked demos** on their own calendar:

1. **Sign into Google Calendar** as `demo@teamsportsinfo.com`
2. Go to **Settings > Settings for my calendars > Share with specific people**
3. Add each person:
   - `toddtsic@gmail.com` — "See all event details"
   - `anntsic@gmail.com` — "See all event details"
   - `chelseatsic@gmail.com` — "See all event details"
4. Each person receives an email invitation to add the calendar
5. Once accepted, demo events appear on their personal Google Calendar alongside their own events

**Result**: Everyone sees when demos are booked. Google Calendar sends its own notifications (reminders before the meeting) to all shared users.

### Option B: NetworkSolutions Email Forwarding

Since `demo@teamsportsinfo.com` email lives on **NetworkSolutions** (NOT Gmail — Gmail/MX was never activated), set up forwarding at the NetworkSolutions level:

1. **Log into NetworkSolutions** email admin for `teamsportsinfo.com`
2. Set up a **forwarding rule** on the `demo@teamsportsinfo.com` mailbox to forward to:
   - `toddtsic@gmail.com`
   - `anntsic@gmail.com`
   - `chelseatsic@gmail.com`
3. Calendly sends booking notifications to `demo@teamsportsinfo.com` — NetworkSolutions forwards them to all three

**Result**: When anyone books/reschedules/cancels, all three people get the Calendly notification email via NetworkSolutions forwarding.

**Note**: The exact steps vary by NetworkSolutions plan — look for "Email Forwarding" or "Mail Aliases" in the email management panel. If NetworkSolutions doesn't support multi-recipient forwarding on your plan, create `demo@` as an **email alias/distribution** that delivers to all three mailboxes.

### Option C: Upgrade Calendly to Standard Plan ($10/seat/month)

If the team grows or you need richer features:
- Native multi-user notifications ("Email someone else" workflow)
- Automated reminder sequences (reduce no-shows)
- Custom branding (remove Calendly logo)
- Multiple event types (e.g., 15-min intro call vs 30-min full demo)
- Payment collection via Stripe

**For now, Options A + B together cover everything you need at zero extra cost.**

### Recommended Setup: A + B Combined

| Channel | What they see | When |
|---------|---------------|------|
| **Google Calendar** (Option A) | Demo event with attendee name, time, Meet link | Immediately on booking; reminders before event |
| **NetworkSolutions forwarding** (Option B) | Full Calendly notification with attendee email, answers, reschedule/cancel alerts | Immediately on booking, reschedule, or cancel |

---

## Admin: Google Workspace

### Login
- **URL**: https://admin.google.com
- **Account**: `demo@teamsportsinfo.com`
- **Password**: See `docs/Demos/SCHEDULE-A-DEMO.local.md` (untracked)
- **Purpose**: Calendar infrastructure only — email stays on NetworkSolutions

### Critical Warning: Do NOT Activate Gmail
Google will repeatedly prompt you to "Activate Gmail" or add MX records. **NEVER do this.** It would reroute all `@teamsportsinfo.com` email through Google and break your existing email on NetworkSolutions. You only use Google Workspace for the calendar.

### Trial & Billing
- Currently on **Business Plus 14-day free trial** (expires March 9, 2026)
- **Before March 9**: Downgrade to **Business Starter** ($7/month) — that's all you need for calendar
- In Google Admin Console: **Billing > Subscriptions > Change plan**

### Managing Availability
1. Sign into https://calendar.google.com as `demo@teamsportsinfo.com`
2. Click the gear icon > **Working hours & location**
3. Set which days/times are available for demos
4. Calendly reads this calendar — any blocked time = unavailable for booking

---

## Admin: NetworkSolutions (Email)

### Login
- **URL**: https://webmail-oxcs.networksolutionsemail.com/appsuite/
- **Account**: `demo@teamsportsinfo.com`
- **Password**: See `docs/Demos/SCHEDULE-A-DEMO.local.md` (untracked)
- **Purpose**: Email hosting for `demo@teamsportsinfo.com` — receives Calendly notifications
- **Forwarding setup guide**: https://www.networksolutions.com/help/article/auto-forward-email-cloud-mail

---

## Admin: Calendly

### Login
- **URL**: https://calendly.com
- **Account**: `demo@teamsportsinfo.com`
- **Password**: See `docs/Demos/SCHEDULE-A-DEMO.local.md` (untracked)
- **Plan**: Free (1 event type, unlimited bookings)

### Event Type Settings
- **Current event**: "30 Minute Meeting"
- **Duration**: 30 minutes
- **Location**: Google Meet (auto-generated link)
- To edit: Calendly dashboard > click event type > **Edit**

### What You Can Change (Free Plan)
| Setting | Where | Notes |
|---------|-------|-------|
| Event name | Event type > Edit | e.g., "TSIC Platform Demo" |
| Duration | Event type > Edit | 15, 30, 45, or 60 min |
| Available hours | Event type > Edit > Availability | Overrides Google Calendar working hours for this event type |
| Buffer time | Event type > Edit > Availability | Add padding before/after meetings |
| Max bookings per day | Event type > Edit > Availability | Prevent back-to-back demo days |
| Questions for invitee | Event type > Edit > Booking form | Add custom fields (org name, sport, etc.) |
| Confirmation page | Event type > Edit > Booking page | Custom message after booking |

### What Requires a Paid Plan
- Custom branding / remove Calendly logo
- Automated email reminders and follow-ups
- Multiple event types (only 1 on free)
- Team scheduling (round-robin, collective)
- CRM integrations (Salesforce, HubSpot)
- Payment collection

---

## Technical: Website Embed

### Files
| File | What it does |
|------|-------------|
| `src/app/views/home/tsic-landing/tsic-landing.component.html` | Contains the `calendly-inline-widget` div in the "Book a Demo" section |
| `src/app/views/home/tsic-landing/tsic-landing.component.ts` | `loadCalendlyWidget()` dynamically loads Calendly CSS + JS |
| `src/app/views/home/tsic-landing/tsic-landing.component.scss` | Styles for the widget container (border-radius, min-height) |

### How the embed works

```html
<!-- In the template -->
<div class="calendly-inline-widget"
     data-url="https://calendly.com/demo-teamsportsinfo/30min?hide_gdpr_banner=1"
     style="min-width:320px;height:700px;">
</div>
```

The component's `loadCalendlyWidget()` method runs inside `afterNextRender()` (browser-only) and dynamically injects:
1. Calendly's external CSS (`widget.css`) into `<head>`
2. Calendly's external JS (`widget.js`) into `<head>`

The JS detects the `calendly-inline-widget` div and renders the scheduling iframe inside it.

### Changing the Calendly URL

If the event type URL changes (e.g., you rename it in Calendly), update the `data-url` attribute in `tsic-landing.component.html`. The URL format is:

```
https://calendly.com/{username}/{event-slug}
```

Current: `https://calendly.com/demo-teamsportsinfo/30min`

### Query Parameters
| Param | Effect |
|-------|--------|
| `hide_gdpr_banner=1` | Hides the cookie consent banner (currently used) |
| `background_color=ffffff` | Set widget background color |
| `text_color=333333` | Set widget text color |
| `primary_color=0ea5e9` | Set accent color to match your palette |

---

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| Widget shows blank/empty | Calendly JS failed to load | Check browser console for blocked scripts (ad blocker, CSP) |
| No available times showing | Google Calendar has no free slots | Check working hours in Google Calendar for `demo@teamsportsinfo.com` |
| Nobody getting notifications | Email forwarding not set up | Set up forwarding per Option B above |
| "Activate Gmail" prompt in Google Admin | Google pushing email migration | **Ignore it.** Never click. Email stays on NetworkSolutions. |
| Double-bookings | Multiple calendars not synced | Ensure Calendly is connected to the `demo@teamsportsinfo.com` Google Calendar |
| Widget not showing after code change | Dev server cache | Restart `ng serve`; hard-refresh browser (Ctrl+Shift+R) |

---

## Maintenance Calendar

| When | Task |
|------|------|
| **Before March 9, 2026** | Downgrade Google Workspace from Business Plus trial to Business Starter ($7/mo) |
| **Monthly** | Verify demo@ calendar availability is up to date for the coming month |
| **Quarterly** | Review Calendly booking stats — consider upgrading if volume grows |
| **If team grows** | Evaluate Calendly Standard plan ($10/seat/mo) for native multi-user support |
| **If Calendly URL changes** | Update `data-url` in `tsic-landing.component.html` |
