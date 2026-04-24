# Pre-Release Email to Directors

Working document for the pre-launch announcement. Iterate freely — the short email body is the thing that gets sent; the appendix is a reference we can link from the email or paste below the sign-off.

---

## Email Body

**Subject:** The next-generation TeamSportsInfo.com is almost here — come kick the tires

Hi Directors!

**It's here.** The next-generation TeamSportsInfo.com — **rebuilt from the ground up**, developed in deep partnership with **Claude**, Anthropic's frontier AI. A genuine architectural reset — not a port, not a reskin. And yet you'll feel at home the moment you log in. **Zero training required.** The navigation is where you expect it. The workflows run the way they always have. Every feature you rely on today is there — preserved, sharpened, and verified to the penny against the legacy system.

**And one thing to be absolutely clear about: this release affects the director-facing web application *only*. TSIC-Events — the tournament mobile app your parents, coaches, and players rely on — is completely unchanged and unaffected.** It runs exactly as it does today, straight through the cutover and beyond. Live tournaments, score updates, the on-site mobile experience your attendees count on — all untouched.

What's new is the product itself. The polish. The speed. And **AI woven right into the product**: a completely rebuilt scheduling engine with **autoscheduling that learns from your prior years' patterns**, **AI-assisted email composition** in search/registrations, **AI-assisted national rankings capture**. Alongside those, a set of brand-new capabilities you'll want to explore: a **completely reimagined Club Teams library**, **early-bird and late-fee modifiers on both player and team registrations**, **discount codes for team registrations**, bulk updating of USA Lacrosse Number expiration dates for your players — and dozens of thoughtful refinements throughout. **This isn't just built with AI. It runs with AI, too.**

**End of May** is the target to flip the switch. Before we do, **I'd like to invite you into the dev environment** — come explore, try things, push it around at your own pace. Your input between now and go-live will directly shape what ships. If something feels off, or you'd like to see a workflow behave differently, I'd love to hear about it while there's still room to act.

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

## Appendix: What's New / What's Preserved

### Familiar ground — preserved from legacy

If you've been running tournaments on TSIC, these won't surprise you. The behavior matches legacy on purpose — in many cases the legacy system *is* the spec.

- **Navigation layout** — the admin nav structure follows the same grouping you're used to.
- **Money behavior** — fee calculations, discount codes, early-bird / late-fee modifiers, processing fees, refunds, and waiver handling all match legacy math to the penny.
- **ARB (recurring billing)** — subscription creation, CC updates, and expiration tracking work the way they always have.
- **Email tokens** — `!NAME`, `!PERSON`, `!JOBLINK`, and the rest of the token library resolve the same way in templates and broadcasts.
- **Admin roles** — Superuser, Director, and SuperDirector are the same three admin tiers with the same permissions.
- **Login / job selection flow** — pick your role, pick your job, you're in.

### Scheduling — completely rebuilt

The entire scheduling process has been overhauled from the ground up. The headline capability: **autoscheduling that learns from your prior years' patterns** — the system studies how you've scheduled before and drafts this year's schedule for you. You get a starting point that reflects how *you* run your tournaments. Tweak, adjust, publish.

### Club Teams library — completely reimagined

A real structural leap, not a UI tweak. Club reps now build a **persistent, club-owned roster of teams** once, and reuse those entries across every tournament they register for — instead of re-typing the same team name, grad year, and level of play at every event.

**What it means for you:**

1. **Registration friction drops dramatically for your club reps.** Set up a team in the library once; from then on, registering that team into any tournament is effectively a single click. Cleaner data in, fewer duplicate teams, faster setup for every event on your calendar.

2. **Team identity persists across the ecosystem.** Each library team carries a stable, protected identity. Once that team appears on a schedule, its core attributes lock down automatically to preserve historical accuracy. **TSIC can now track the same team across every tournament it plays** — the foundation for cross-tournament stats, club profiles, and recruiting-friendly team pages is in this release.

### AI in the product (not just behind it)

Claude didn't just help build TSIC. AI is now baked into the workflows you run every day:

- **Autoscheduling** — learns from your historical schedules to draft new ones (see above).
- **AI-assisted email composition** — in search/registrations batch email, Claude helps you draft the message you want to send. Tell it what you mean; it gives you copy you can refine and send.
- **AI-assisted national rankings capture** — pull external rankings data into your tournament workflows without hand-entry.

More AI-powered features will land as we hear what would actually help. This is just the start.

### New capability

The rest of the pieces that weren't in legacy, or that got a meaningful rework:

**Registration wizards**
- **Early-bird and late-fee modifiers on both player *and* team registrations** — set the windows, set the amounts, the system handles the rest.
- **Discount codes for team registrations** — apply targeted pricing adjustments without hand-tweaking fees per team.
- Player wizard blocks cleanly when registration is closed, and surfaces BYAGEGROUP eligibility correctly.
- Club Rep registration has **inline Terms of Service acceptance** — no separate popup.
- "Proceed to Payment" button label now reflects exactly how many new teams you're paying for.

**Updating USA Lacrosse Number expirations** (new admin page)
- Pull your roster and bulk-update USA Lacrosse Number expiration dates for your players in one pass; export to Excel.
- Sortable columns, stable row numbers that survive sorting and export.
- Inline batch email for fixing membership issues — role-neutral templates, skips healthy members so you're not crying wolf.

**Search / Registrations**
- ARB credit cards expiring this month — dedicated lookup + email template.
- Batch email improvements: expanded template library, Waitlist template, smarter routing (Player rows go to mom/dad/player as appropriate).

**Communications**
- Team Links admin (ported from the mobile legacy tool, now web-first).
- Unified email token engine — one code path, consistent behavior across all senders.
- Bulletin inline styles preserved exactly as authored.

**Workspace dashboard**
- Typed workspace constants, role-aware widgets, cleaner editor.
- More polish coming based on what directors actually want pinned.

---

## Drafting Notes (scratch area)

Open threads we can tighten in later passes:

- Subject line currently generic ("come kick the tires"). Worth teasing a specific feature instead? **"Autoscheduling is here — come see the new TSIC"** might land harder given how much that changes a director's life.
- Worth adding a short "what's NOT changing for your end users (parents, coaches)?" paragraph? Directors will get asked this.
- Do we want a one-line mention of the go-live cutover plan (data migration, downtime window) or save that for a second email closer to the date?
- Feature list ordering in the appendix — Scheduling and AI-in-product are now top billing above "New capability." Good. Could also promote them into more prominent opener bullets if we want.
- **Autoscheduling claim — reality check.** The email pitches "learns from your prior years' patterns." Confirm the implementation actually does this (vs. a simpler heuristic). If it's forward-looking / in-progress, we should either soften the copy or clearly flag it as coming-this-season.
- **AI email composition claim — reality check.** Same question: is this shipped in the search/registrations batch email flow today, or coming? Pitching capability that isn't yet live will burn trust fast.
- **National rankings capture claim — reality check.** Same. What does "capture" mean concretely — ingestion from a specific external source? Worth tightening the copy once we know.
- "No real payments" language — double-check this is true for *all* payment paths (registration fees, ARB setup, ARB renewals, refunds). If any path is live-ish, flag it.
- Consider a PS with a direct Calendly/phone link for directors who'd rather talk than type.
- Consider naming two or three directors as "early eyes" in the email to set expectations for a feedback cadence.
- **AI angle calibration** — current opener names Claude/Anthropic directly. Dial up (add a short "How I built this with AI" sidebar) or dial down (just "modernized with AI") based on audience reaction. Some directors may find the AI framing exciting; others may find it unsettling. Worth A/B'ing a line with one or two people before the full send.
- Related: an honest line about what AI *doesn't* do might strengthen trust — something like "every line was reviewed before it shipped; Claude accelerated the work, it didn't replace the judgment."

Sections we haven't written yet but might want:

- Known rough edges (things that work but aren't pretty yet) — honesty builds trust.
- Explicit list of what's deferred to a post-launch release (if anything director-facing is).
- Screenshots / short screencast links inline.
