**Subject:** TSIC--The Next Generation!

Hi Directors!

**It's almost here.** The next-generation TeamSportsInfo.com — **rebuilt from the ground up**, developed in deep partnership with **Claude**, Anthropic's frontier AI. A genuine architectural reset — not a port, not a reskin. And yet you'll feel at home the moment you log in. **Zero training required.** The navigation is where you expect it. The workflows run the way they always have. Every feature you rely on today is there — preserved, sharpened, and verified to the penny against the legacy system.

**This release affects the web application *only*.** TSIC-Events — the tournament mobile app your parents, coaches, and players rely on — is completely unchanged and unaffected. It runs exactly as it does today, straight through the cutover and beyond. Live tournaments, score updates, the on-site mobile experience your attendees count on — all untouched.

What's new is the product itself. The polish. The speed. And **AI woven right into the product**: a **dramatically expanded scheduling engine** with **autoscheduling that learns from your prior years' patterns**, **AI-assisted email composition** in search/registrations, **AI-assisted national rankings capture**.

Alongside those, a set of brand-new capabilities you'll want to explore: a **completely reimagined Club Teams library**, **early-bird and late-fee modifiers on both player and team registrations**, **discount codes for team registrations** — and dozens of thoughtful refinements throughout. **This isn't just built with AI. It runs with AI, too.**

**End of May** is the target to flip the switch. That way users will already know their way around by the time they re-up for tournaments the day after this summer's events close. Before we do, **I'd like to invite you into the dev environment** — come explore, try things, push it around at your own pace. Your input between now and go-live will directly shape what ships — and even when it ships, if necessary. If something feels off, or you'd like to see a workflow behave differently, I'd love to hear about it while there's still room to act. **The first half of May is when feedback has the most room to land — after that, we're in polish-and-ship mode.**

**Where to go:** [dev.teamsportsinfo.com](https://dev.teamsportsinfo.com)

**Your login hasn't changed.** Same username, same password. Role, job access, everything you'd expect — it all works the same.

**A few things to know about dev before you dive in:**

- It's a **completely separate machine** from production. Nothing you do here touches live data.
- The dev database is a **rolling copy of prod** — I refresh it regularly from recent prod backups, so the data you see today may look different next week. That's normal.
- **No emails are sent.** Registration test flows, search/registrations batch emails — all suppressed. You won't spam anyone by accident.
- **No real payments.** Everything is sandboxed at Authorize.Net. If you want to test a credit card flow, use Visa **4111111111111111**. If you want to test USA Lacrosse Number validation, use **424242424242**.

So: break things, try weird things, click every button. That's what it's there for.

If you hit a wall, want to talk through something, or just want to send a "why does X do Y" note — reply to this email. No form, no ticket system, just me.

Can't wait to hear what you think.

Todd

---

## Highlights

### Everything you already know, you'll still know

**None of what's new asks you to learn a new way to work.** Your navigation, your registration wizards, your reports, your email flows — all of it is where you left it and behaves the way you remember. Fees, discount codes, modifiers, refunds, ARB, waivers, email tokens, admin roles, the login flow — match legacy to the penny.

The new capabilities — autoscheduling, AI-composed emails, the Club Teams library, fly-in search panels — sit **on top** of the product you already know. Nothing you rely on has been moved, renamed, or retired.

### Scheduling — vastly extended

The entire scheduling process has been enhanced — the flow is the same, but the capabilities have been vastly extended. The headline capability: **autoscheduling that learns from your prior years' patterns** — the system studies how you've scheduled before and drafts this year's schedule for you. You get a starting point that reflects how *you* run your tournaments. Tweak, adjust, publish.

A viewer upgrade worth calling out: **the game clock is now inline.** It's visible directly on the schedule view at all times — no more popup modal, no more clicking through to check the timer.

### Club Teams library — completely reimagined

A real structural leap, not a UI tweak. Club reps now build a **persistent, club-owned roster of teams** once, and reuse those entries across every tournament they register for — instead of re-typing the same team name, grad year, and level of play at every event.

**What it means for you:**

1. **Registration friction drops dramatically for your club reps.** Set up a team in the library once; from then on, registering that team into any tournament is effectively a single click. Cleaner data in, fewer duplicate teams, faster setup for every event on your calendar.

2. **Team identity persists across the ecosystem.** Each library team carries a stable, protected identity. Once that team appears on a schedule, its core attributes lock down automatically to preserve historical accuracy. **TSIC can now track the same team across every tournament it plays** — the foundation for cross-tournament stats, club profiles, and recruiting-friendly team pages is in this release.

### AI in the product (not just behind it)

**AI isn't just behind the scenes — it's baked into the workflows you run every day:**

- **Autoscheduling** — learns from your historical schedules to draft new ones (see above).
- **AI-assisted email composition** — in search/registrations batch email, the assistant helps you draft the message you want to send. Tell it what you mean; it gives you copy you can refine and send.
- **AI-assisted national rankings capture** — pull external rankings data into your tournament workflows without hand-entry.

More AI-powered features will land as we hear what would actually help. This is just the start.

### New capability

The rest of the pieces that weren't in legacy, or that got a meaningful rework:

**Registration wizards**
- **Early-bird and late-fee modifiers on both player *and* team registrations** — set the windows, set the amounts, the system handles the rest.
- **Discount codes for team registrations** — apply targeted pricing adjustments without hand-tweaking fees per team.

**Search / Registrations and Search / Teams**

Both search surfaces got a major UX overhaul built around **fly-in panels** — a major day-to-day quality-of-life upgrade:

- **Filters fly in from the side.** Dial in exactly the query you want without shrinking or crowding the results grid. Open it when you need it, tuck it away when you don't.
- **Row details fly in from the side.** Click any player or team and the full record slides in alongside — no page jump, no scroll reset, no loss of context. Close it and you're right back where you were in the list.
- The result: you scan faster, read more, and stay oriented even in lists of hundreds of rows.

**In Search / Registrations specifically**, email work got a major lift:

**Private invite links into closed registrations.** New `!CLUBREP_INVITE_LINK` and `!INVITE_LINK` tokens generate recipient-specific registration links that work **even when public registration is closed**. The classic use case: pull up last year's tournament, filter to the club reps (or players) you want to re-invite, target this year's event as the destination, and batch-send. Every recipient gets a unique, one-click path straight into *your* event — early access for your loyal customers, without the public rush.

Plus:
- **AI-assisted email composition** — tell it what you want to say; it drafts copy you can refine and send.
- **Expanded library of canned templates** — ready-to-send messaging for the jobs you run constantly: Waitlist, ARB cards expiring this month, and more.
