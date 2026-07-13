# Chelsea Review — Punch List

**Started:** 2026-07-12
**Status:** In Progress

## Topics

| Topic | Items |
|---|:-:|
| Team Registration | 9 |
| Player Registration | 11 |
| Accounting / Admin Payments | 13 |
| LADT / Event Setup | 14 |
| Admin Search Tools | 12 |
| Communications | 7 |
| Login, Navigation & Bulletins | 13 |
| Coach/Staff Registration & Roster Management | 18 |
| Profiles / Profile Editor | 10 |
| End-of-Month Reconciliation | 8 |
| Public Site / Landing / Branding | 10 |
| **Total** | **125** |

<div style="page-break-before: always;"></div>

## Team Registration — differences from the old system

_From a comparison of the old system and the new one (2026-07-12), each checked against the new build._

#### ☐ CR-001: How the Club Teams Library works
- **Type**: Training-note · **Audience**: Client support
- **How it works** (from the current build — confirm wording against the live screen during the walkthrough):
  1. **A rep's teams live in their Club Teams Library.** A team is created once and stays in the library across every event and season — it's never re-typed per event.
  2. **First team:** when a rep has no teams yet, they get a "Register Your First Team" form that captures the team (name, grad year, level of play) and the event slot (age group) together. In one step it both creates the library team and registers it for the event — the rep just experiences "register my team."
  3. **After that:** for later events the rep opens a side drawer, picks a team already in their library, and drops it into the event (choosing age group and level of play). Teams show as already-registered vs. not-yet-registered for that event, so it's clear what's in.
  4. **Same drawer manages teams:** edit, archive, delete, restore. Archived teams drop out of the picker but can be brought back.
- **Why it matters**: The idea is "build your team once, reuse it every event." Reps used to the old system may look for a per-event "add team" form that no longer exists — point them to their library instead. Worth explaining archive vs. delete (archive hides but keeps history; delete removes).
- **Say to clients**: "Your teams live in your Club Teams Library. Create a team once; for each event you just pick it from the library and choose the age group. Done with a team for now? Archive it — you can bring it back later."
- *Dev evidence: library-flyin.component.ts (picker + manage drawer), add-and-register-team-modal.component.ts (first-team create+register).*

#### ☐ CR-002: Only one person per club can register teams for an event
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system more than one person from the same club could register teams for the same event. Now only one can. If a teammate already started registering your club's teams, the next person gets a message naming who already did it and telling them to contact their administrator.
- **Why it matters**: The second person is stopped. They either work with the first person, or we move the teams to them from the admin side. This comes up a lot.
- **Say to clients**: "Each club has one person who registers teams for an event. If a teammate already started, work with them — or we can move the teams to you."
- *Dev evidence: one-rep-per-event check, TeamRegistrationService.cs:687-701; pre-check :275-326.*

#### ☐ CR-003: Whether registration is open now depends on event dates, not just a switch
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system a single "allow team registration" switch decided if registration was open — even long after an event ended. Now the system also checks whether the event is over, using (in order) the last scheduled game date, then the Event End Date, then User Expiry. If the event has concluded, registration is closed even if the switch is still on. Admins can still register after an event ends; regular users can't.
- **Why it matters**: This is exactly what we hit reopening Taste of the South. "Why can't my rep register?" now depends on the event's dates. The lever to reopen is **Event End Date** on the Scheduling tab — not User Expiry, which is only a last resort.
- *Dev evidence: JobLifecycle.cs:38-58, JobRegistrationCapabilities.cs:30-75, gate at TeamRegistrationService.cs:665-676.*

#### ☐ CR-004: The "max teams per club" limit is now actually enforced
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system let you set a max-teams-per-club limit but never enforced it — only the overall Max Teams per age group was checked. Now the per-club limit is enforced: once a club hits its cap in an age group, registration is refused with a message saying the club has reached its maximum.
- **Why it matters**: Directors who set a per-club cap now get real enforcement, and reps hit a new blocking message. Worth flagging to directors moving events over, in case they left a low cap set back when it did nothing.
- *Dev evidence: TeamRegistrationService.cs:712-730.*

#### ☐ CR-005: Editing and deleting teams follows clearer rules now (archive vs. delete)
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: In the old system a rep could edit a team freely, and deleting was blocked only if the team had payments or was scheduled. Now the rules are explicit: once a team has ever been on a schedule, it can't be edited or deleted; a team with schedule history is **archived** (kept and restorable) rather than deleted; a team still registered for an event has to be removed from the event first; and an archived team's name is reserved (you restore it rather than recreate it). Also, a team's age group locks once it's registered — to change it, remove it and re-register.
- **Why it matters**: Reps get clear, specific rules with on-screen messages support can point to. The key talking points are "archive vs. delete," "you can't edit a team once it's been scheduled," and "remove a team from the event before deleting it."
- *Dev evidence: TeamRegistrationService.cs:1000-1145, lock reasons library-flyin.component.ts:1511-1530, age-group lock teams-step.component.ts:716-775.*

#### ☐ CR-006: The club-rep permission switches (add/edit/delete) actually work now
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: In the old system the "allow club rep to add / edit / delete" switches only hid buttons in the UI — the server didn't re-check them, so a determined request could get around them. Now the server enforces them, so a disabled button and a refused action always agree.
- **Why it matters**: Turning a switch off now genuinely prevents the action, not just hides the button. Directors can rely on these switches.
- *Dev evidence: JobRegistrationCapabilities.cs:67-73, edit gate TeamRegistrationController.cs:404-415.*

#### ☐ CR-007: Club reps can add and manage their own clubs
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: In the old system, tying a rep to a club was an admin job — a rep couldn't add themselves. Now a rep can add a club to their own account, rename it, or remove it. When they add one, the system checks for a close match against existing clubs to avoid duplicates and offers that instead of creating a new one. Rename and remove are blocked once teams are registered under the club.
- **Why it matters**: Reps set up their own clubs now, so support doesn't have to do it by hand. The duplicate check may show a rep "a similar club already exists" — that's intended, to keep from creating duplicates.
- *Dev evidence: TeamRegistrationController.cs:527-628, TeamRegistrationService.cs:1147-1300.*

#### ☐ CR-008: Deposits can differ by age group now (they used to be the same for all)
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system assumed one deposit amount for every age group in an event. Now each age group can have its own deposit and balance, and per-team or per-league overrides apply too. In the registration wizard each team shows a small badge for its payment phase (deposit only, final balance due, or mixed).
- **Why it matters**: Directors can charge different deposits by age group now, which wasn't possible before. And a rep's cart can legitimately mix teams that owe just a deposit with teams that owe the full balance — which answers "why do my teams show different amounts due?"
- *Dev evidence: TeamRegistrationService.cs:584-612, applied :830-840, phase badge teams-step.component.ts:623-630.*

#### ☐ CR-009: For invitation-only events, reps really can't register without the invite link
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: When an event is set to require an invitation, the new system won't let a rep even start registering unless they came in through a valid, unexpired invite for that event. It's enforced on the server, not just hidden in the UI.
- **Why it matters**: On invitation-only events, reps genuinely can't get in without the emailed link. Expect "I don't have an invitation" tickets — route those reps to the director who sends the invites.
- *Dev evidence: new enforcement confirmed at TeamRegistrationService.cs:190-199. Whether the OLD system left this unenforced is unconfirmed — worth a quick check before making it a firm talking point.*

<div style="page-break-before: always;"></div>

## Player Registration — differences from the old system

_From a comparison of the old system and the new one (2026-07-12), each checked against the new build._

#### ☐ CR-010: Player registration can close on its own when the event is over
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system, one "allow player registration" switch decided whether parents could register. Now the system also checks whether the event is over (using the last scheduled game date, then Event End Date, then User Expiry) and whether a newer year of the same event has taken over. If the event has concluded or a newer one exists, player registration closes even if the switch is still on. Also, if no player fee has been set up, registration is blocked — even for an admin.
- **Why it matters**: Support may hear "the allow-player box is checked, but parents still get 'this event is not accepting registrations.'" The fix is the event's dates/expiry (or setting up a player fee), not the switch. Same idea as the team-side CR-003.
- **Say to clients**: "If registration won't open, check the event's end date and expiry, and make sure a player fee is set — not just the allow-registration box."
- *Dev evidence: JobRegistrationCapabilities.cs:47-74, PlayerRegistrationController.cs:148-155, landing check JobLookupService.cs:32-39.*

#### ☐ CR-011: Parents can pay by e-check now, and it shows "pending" for a few days
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system took credit cards (and mail-in checks) only. The new system adds e-check — paying straight from a bank account — if the director turns it on. When a parent pays by e-check, the player is marked **pending** until the bank clears it (3–5 business days), and the confirmation email carries an amber "settlement pending" note. If the payment bounces, it reverses automatically. E-check can't be combined with an autopay plan.
- **Why it matters**: E-check parents will see "pending" and may call asking why their kid isn't active yet — that's expected for a few days. It's only offered if the director enabled it.
- *Dev evidence: payment-v2.service.ts:350, PlayerRegistrationPaymentController.cs:94-118, PaymentService.ProcessEcheckPaymentAsync:1182.*

#### <span style="color:#0033cc">☐ CR-012: Directors/office no longer get a copy of every player confirmation email</span>
- **Type**: Workflow-change (needs a decision — see below) · **Audience**: SuperUser/Admin
- **What's new**: In the old system the player confirmation email copied the director/office through the job's CC/BCC email fields. The new system sends the confirmation only to the family and player — there's no CC or BCC.
- **Why it matters**: Directors or offices who relied on getting a copy of every registration will quietly stop receiving them. Expect "we're not getting registration copies anymore." Worth deciding whether to bring the director copy back.
- *Dev evidence: verified — recipients are family+player only (PaymentService.cs:2453-2468), no CC/BCC wiring. Confirm whether dropping director copies was intended.*

#### <span style="color:#c00000">☐ CR-013: The automatic sibling (multi-player) discount isn't being applied</span>
- **Type**: Bug (needs a decision) · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system, when a family registered more than one child, the 2nd and later children got an automatic multi-player discount. In the new system the setting still shows on the admin screen and gets saved and copied to new jobs — but it is **never actually applied to any charge**. Families are billed full price for every child.
- **Why it matters**: A director who sets a sibling discount will find it never comes off, and support could promise a discount that never happens. This looks like a defect: either wire the discount back up or remove the setting so it doesn't mislead.
- *Dev evidence: verified both ends. New: the PlayerReg_MultiPlayerDiscount fields are only saved/shown/cloned (JobConfigService, JobCloneService); never read in any charge or fee path. Old: the discount WAS applied automatically at registration (legacy PlayerBaseController.cs:891-913, plus a hardcoded SJAA $20 family discount at :868-889). So the old system discounted siblings and the new one doesn't.*

#### ☐ CR-014: Invitation-only player registration is now possible
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: An event can be set to require an invitation. When it is, a parent can't register unless they came in through the emailed invitation link — a plain URL is blocked with "this event requires a valid invitation."
- **Why it matters**: New capability. "The site says I need an invitation" means the parent used a plain link instead of their emailed invite — route them to the director who sends invites. (Same idea as the team-side CR-009.)
- *Dev evidence: PlayerRegistrationController.cs:96-102.*

#### ☐ CR-015: Pay-by-check now holds a spot — and can land a player on the waitlist
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the new system, choosing pay-by-check actively claims the player's roster spot and marks them active. But if the team filled up while the family was checking out, the player is moved to the team's waitlist (and re-priced to $0) instead of overfilling it. Also, once a registration is committed to one payment method, it can't be switched to another partway through.
- **Why it matters**: A check-paying parent can "finish registration" and still end up on the waitlist because the team filled. Explains "I chose pay-by-check but my kid is on the waitlist" and "I can't change how I'm paying."
- *Dev evidence: PlayerRegistrationService.SubmitByCheckAsync:716-859.*

#### ☐ CR-016: Parents can add an optional donation at checkout
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: If the director turns it on, parents see an optional donation field on the payment step. The donation is charged together with the registration (card or e-check only — not on an autopay plan or a mail-in check) and shows its own processing-fee line.
- **Why it matters**: New at checkout. Not available on autopay or check payments.
- *Dev evidence: payment-step.component.ts:1119-1126, PaymentService.cs:1124-1169.*

#### ☐ CR-017: All configured waivers must be accepted (once for the whole family)
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: The old system in practice only required the Release of Liability checkbox. The new system has a waivers step that lists every waiver the director set up (release of liability, code of conduct, COVID, refund terms) and requires each one to be accepted. A parent accepts them once and it covers all the children they're registering — not once per child.
- **Why it matters**: A parent "stuck on the waivers step" usually has a second waiver left unchecked. Good to know the accept-once-for-all-kids behavior when a parent asks.
- *Dev evidence: waivers-step.component.ts, waiver-state.service.ts:189-198.*

#### ☐ CR-018: Discount codes reduce the processing fee too, and percent codes come off the remaining balance
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: In the old system a code came off the fee total but left the processing fee untouched, and a percent code was figured on the whole amount owed (processing included). In the new system, applying a code also lowers the processing fee proportionally, and a percent code is figured on the base amount minus what's already been paid — so a parent who already paid a deposit only discounts the remaining balance. Codes also work across multiple camps at once and stack with early-bird.
- **Why it matters**: For the same code, the dollar amount that comes off (and the resulting processing fee) can differ from the old system, and a partially-paid parent only discounts the balance. Accounting reconciling "why is the discount a different amount than before" should know the basis changed.
- *Dev evidence: PlayerRegistrationPaymentController.ApplyDiscount:246-360.*

#### ☐ CR-019: Insurance can be bought even when nothing is owed, or when paying by check
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: In the old system, registration insurance was only offered inside the card-payment step and charged along with the registration. The new system adds two paths: buy insurance when the registration balance is $0 (card charged just for the premium), and buy insurance when paying the registration by check (card entered only for the premium). Same insurance provider as before — just more ways to buy it.
- **Why it matters**: Parents of free or check registrations can now be prompted for (and charged for) insurance on a separate card, which didn't happen before.
- *Dev evidence: payment-step.component.ts:1093-1116, 530-557.*

#### ☐ CR-020: The confirmation email subject line changed
- **Type**: Training-note · **Audience**: Client support
- **What's new**: The confirmation email subject changed from "[Event]: Registration Status Update" to "[Event] Registration Confirmation."
- **Why it matters**: Parents (and support searching inboxes) looking for the old subject won't find it, and any mail filters keyed on the old wording will miss. Minor, but useful when hunting for a parent's confirmation.
- *Dev evidence: PlayerRegConfirmationService.cs:83.*

<div style="page-break-before: always;"></div>

## Accounting / Admin Payments — differences from the old system

_From a comparison of the old system and the new one (2026-07-12), each checked against the new build._

#### ☐ CR-021: A refund reverses one payment, not the family's whole card history
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: In the old system, entering a credit-card refund checked it against everything the family had paid by card and spread the credit across all the kids' registrations. In the new system a refund acts on the single payment the admin clicked — the amount has to be between a penny and that one payment, and only that transaction is reversed.
- **Why it matters**: To refund a family that made several charges, the admin now refunds each payment separately. There's no more "credit the family $X and let it spread" — the money and the limit belong to the one payment selected.
- *Dev evidence: ProcessRefundAsync, RegistrationSearchService.cs:340-359, 407-445.*

#### ☐ CR-022: A recorded check applies to one child, not the whole family
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: In the old system, entering a check let the admin type one amount and the system walked through every child in the family applying it until it ran out. In the new system a check is recorded against a single registration; if a player is registered for more than one event, a "which registration is this for?" picker appears.
- **Why it matters**: A family check covering several kids can't be entered once and auto-split anymore — the admin books it against a specific child (or splits it by hand).
- *Dev evidence: RecordCheckOrCorrectionAsync, RegistrationSearchService.cs:487-567; picker accounting-ledger.component.ts:317-344.*

#### ☐ CR-023: Checks are capped at the "check owed" figure (no processing fee), and rejected if over
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: A check doesn't carry the card processing fee, so the amount owed by check is lower than the amount owed by card. The new system knows this: it shows a "check balance owed" figure (card-owed minus the processing fee) and rejects a check that's over it — e.g. "check payment $207 exceeds the check balance owed of $200." The old system would accept the higher amount and unwind the fee afterward.
- **Why it matters**: The dollar amount support quotes for a mail-in check differs from the card amount by the processing fee, and the admin is now held to the true check figure. (Related to the Accounting punchlist "Check Owed" items.)
- *Dev evidence: RegistrationSearchService.cs:511-526.*

#### ☐ CR-024: Corrections can't credit more than was paid or exceed what's owed
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: In the old system an admin correction applied whatever amount was typed, with no limit. Now a correction is bounded: a positive correction (which lowers the balance) can't exceed what's owed, and a negative correction (which raises it) can't take back more than was actually paid. Sign convention: positive decreases owed, negative increases it.
- **Why it matters**: A correction can no longer accidentally drive a balance negative or credit more than was collected — a mistaken large correction is rejected with a clear message.
- *Dev evidence: RegistrationSearchService.cs:503-532.*

#### <span style="color:#0033cc">☐ CR-025: A refund done from the ledger records a generic "Admin refund" note, not the typed reason</span>
- **Type**: UX (needs a decision) · **Audience**: SuperUser/Admin
- **What's new**: When an admin refunds a payment directly from the accounting ledger, the reason recorded on the refund row is always "Admin refund" — the actual reason isn't captured there. (There's a separate refund dialog elsewhere on the admin registration-search screen that does capture a typed reason, so the two refund paths behave differently.)
- **Why it matters**: A refund done the common way — from the ledger — doesn't record why. Later, anyone reviewing the family's history sees only "Admin refund" with no detail. Worth deciding whether the ledger refund should also ask for a reason so both paths match.
- *Dev evidence: in-ledger path hardcodes reason 'Admin refund' (registration-detail-panel.component.ts:732); the standalone refund dialog captures free text (refund-modal.component.ts:64) and is wired at search-registrations.component.html:887.*

#### ☐ CR-026: Enter the plain discount amount — the system adds the processing fee for you
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: In the old system you had to pad the discount amount by hand to cover the processing fee — a $25 discount was entered as $25.90. The new system adds the processing fee automatically, so you just enter the plain discount. Example: on UM Summer Camps the old code was $25.90; in the new system enter $25.
- **Why it matters**: If someone copies an old code forward at the padded amount, the new system over-discounts (it takes off the padding *and* handles the fee), and each player ends up with a small credit. On UM this showed up as everyone sitting at −$0.88.
- **Say to clients**: "Enter the discount you actually want to give — don't add the processing fee, the system does that now. Carrying a code over from the old system? Drop the extra cents, so $25.90 becomes $25."
- *Dev evidence: this is the cause behind Accounting PL-025 (UM Summer Camp −$0.88 credits). Next year's codes should be authored at the flat value.*

#### <span style="color:#c00000">☐ CR-027: An already-used discount code's amount and dates can now be edited</span>
- **Type**: Bug / hazard (needs a decision) · **Audience**: SuperUser/Admin
- **What's new**: In the old system, once a discount code had been redeemed, its amount, percent/flat type, and start/end dates were locked — only the on/off flag could change. In the new system all of those can be edited even after the code has been used; only the code's name is locked.
- **Why it matters**: An admin can change the amount or type of a code parents already redeemed, which can quietly change what users get. The old system prevented this on purpose. Worth deciding whether to restore the lock on used codes.
- *Dev evidence: verified — UpdateDiscountCodeAsync writes amount/dates/type unconditionally (DiscountCodeService.cs:149-155); only the name is locked in the edit form.*

#### ☐ CR-028: Discount codes now show how many times they've been used and whether they've expired
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: The old admin grid showed only a yes/no "has this been used." The new one shows the actual number of times a code was redeemed, plus a colored status chip — red "Expired," amber "N days left" when it's within a week, green "Active." (Neither system tracks *who* used it.)
- **Why it matters**: Support can see at a glance how many times a code was used and whether it's still good. This is also the fix for the old confusion where a code could read "Active" in one column and "Expired" in another.
- *Dev evidence: GetUsageCountAsync (JobDiscountCodeRepository.cs:96-103), status chip DiscountCodeService.cs:233 / discount-codes.component.ts:136-156. Fixes ConfigureMenus PL-028.*

#### ☐ CR-029: E-check payments can't be refunded from the admin ledger
- **Type**: Training-note · **Audience**: SuperUser/Admin + Client support
- **What's new**: The per-payment "Refund" button in the ledger only appears for credit-card payments. E-check (bank/ACH) payments don't get one — bounced or reversed e-checks are handled automatically by the settlement process, not by an admin refund.
- **Why it matters**: An admin trying to refund an e-check from the ledger won't find a button; that goes through the returns/settlement flow instead. Good to know before telling a parent "we'll refund that" for a bank payment.
- *Dev evidence: refund button gated on credit-card method + ADN transaction (RegistrationAccountingRepository.cs:215-216, 255-256; accounting-ledger.component.html:130-134).*

#### ☐ CR-030: Voids now record a clear explanation in the ledger
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: When a card payment is reversed before it settled (a "void," where no money actually moved), the ledger now writes a plain note explaining it — "voided on [date], the original charge was voided (not refunded), void transaction number [X]" — instead of just tacking "voided" onto the payment method. A void still doesn't create a new row (the original drops to $0); a true refund (money returned) still creates a negative "Credit Card Refund" row.
- **Why it matters**: Support can tell at a glance whether a reversal was a void (unsettled, no money moved) or a refund (settled, money returned) right in the ledger. (Related to the Accounting punchlist void/refund wording item.)
- *Dev evidence: RegistrationSearchService.cs:394-405.*

#### ☐ CR-031: Family accounting shows every child — including inactive check-payers — with card-owed vs check-owed
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: The new family accounting view includes all the siblings — including inactive ones who are paying by check — in the family totals and ledger. Each child shows what they owe by card and what they owe by check as separate columns (the check-owed column is hidden when the event only takes cards).
- **Why it matters**: A director or support person sees exactly what each child owes by each method, and a check-paying sibling who still owes real money is no longer left out of the family balance.
- *Dev evidence: GetFamilyAccountingAsync (RegistrationSearchService.cs:212-269), per-player owed split RegisteredPlayerShaper.cs:126-127.*

#### ☐ CR-032: Autopay (recurring billing) can only be viewed and cancelled live from Production
- **Type**: Training-note · **Audience**: SuperUser/Admin
- **What's new**: On the live Production system, the autopay/subscription card shows the real up-to-date status and offers a Cancel button. On staging/test it shows a stored snapshot instead, with a note that live status only appears in Production, and no Cancel button. A subscription set up in one environment can't be read or cancelled from another.
- **Why it matters**: If an admin needs to cancel a parent's real recurring billing, it has to be done from the Production system — not from a test/preview host. This is on purpose, to keep a preview environment from touching live subscriptions.
- *Dev evidence: env-bound GetSubscriptionDetailAsync/CancelSubscriptionAsync (RegistrationSearchService.cs:624-716); FE isProdEnv gating registration-detail-panel.component.ts:793-845.*

#### ☐ CR-033: New bulk tools and safer guards for discount codes
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: Admins can now generate discount codes in bulk (a prefix/suffix pattern with a starting number and a count up to 500, all-or-nothing if any would duplicate) and activate or deactivate all codes at once. Guards got stronger: the server blocks deleting a code that's been used (not just the browser), names are checked for duplicates case-insensitively with a live "already exists" check, and end dates must be after start dates. Two small trade-offs: new codes are always created active (no "born off" — deactivate after), and code windows are date-only now (no time-of-day).
- **Why it matters**: Much faster to create a batch of codes and safer to manage them. The "create it already turned off" option is gone — it's now create-then-deactivate.
- *Dev evidence: bulk/batch DiscountCodeService.cs:73-130, 163-209; guards DiscountCodesController.cs:82-110, 142-200.*

<div style="page-break-before: always;"></div>

## LADT / Event Setup — differences from the old system

_From a comparison of the old system and the new one (2026-07-12), each checked against the new build._

#### ☐ CR-034: Waitlist teams appear on their own — you don't create them anymore
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system you had to build all the waitlist teams by hand when setting up an age group. Now you don't. When an age group fills up to its Max Teams and another team tries to register, the system creates the waitlist automatically and puts the team there.
- **Why it matters**: Directors should stop pre-building placeholder waitlist teams — that's wasted work now, and it can leave empty or duplicate teams lying around. Just set Max Teams per age group and let the system handle the overflow. On the admin side, expect to see waitlist age groups (named "WAITLIST - [age group]") show up on their own in LADT once an age group fills.
- **Say to clients**: "Set your Max Teams per age group. You don't create waitlist teams anymore — they appear automatically when you go over the limit."
- *Dev evidence: legacy TeamBaseController.cs:548-608; new TeamPlacementService.cs:36-108, TeamRegistrationService.cs:783-875. Old per-job "Use Waitlists" toggle is now vestigial for teams.*

#### ☐ CR-035: Birthdate and grad-year eligibility is set in a new "Age Ranges" library, not on the age group
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system you set a team's or age group's birthdate range, grad-year range, and school-grade range right there on the age group or team. In the new system those inline fields are gone. Birthdate eligibility is now handled in a separate "Age Ranges" library (Configure → Age Ranges): you create named ranges with a "DOB from / DOB to" window, and players whose birthdate falls in a range are restricted to the teams assigned to it. Grad-year and school-grade no longer have a direct setup field.
- **Why it matters**: A director looking for the old birthdate/grad-year boxes on an age group won't find them — point them to the Age Ranges page for birthdates. If they relied on grad-year or school-grade limits, those don't have a direct editor anymore (worth confirming whether that's intended).
- *Dev evidence: eligibility fields pass through on save but have no inline input (agegroup-detail.component.ts:737-742, team-detail); Age Ranges library at configure/age-ranges. Ties to ConfigureMenus PL-008/PL-025.*

#### ☐ CR-036: Fees are entered as Deposit + Balance cards with a payment-phase switch, and fee "labels" are gone
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system had flat "Roster Fee" and "Team Fee" boxes, each with a custom label you could type. The new system uses fee cards: you enter a Deposit amount and a Balance Due amount and flip a "require full payment now" switch. There's no free-text fee-label field anywhere — the custom names directors used to show registrants are gone.
- **Why it matters**: The setup model changed from "type a roster/team fee" to "set a deposit and a balance, then choose the payment phase." Directors who used custom fee labels will notice they're gone.
- *Dev evidence: fee-card.component.ts:53-107; league/agegroup/team detail each render Player + Club-Rep fee cards.*

#### ☐ CR-037: Early Bird and Late Fee now require both a start and end date (and warn if they overlap)
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system an early-bird discount or late fee could have open-ended dates (a "permanent" late fee, say). The new system requires both a start and an end date on each — it won't save otherwise ("requires both a start date and an end date"). It also warns if an early-bird window overlaps a late-fee window.
- **Why it matters**: A director who wants an open-ended discount or a permanent late fee can't save that anymore — coach them to set both dates. The overlap warning is a new guardrail.
- *Dev evidence: fee-card.component.ts:19-28, 167-173, 401-416.*

#### ☐ CR-038: Changing a fee in setup can retroactively re-bill existing registrations
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system, editing an age-group fee just saved the number; repricing already-registered players was a separate button. Now, when you change a deposit or balance amount, the editor checks how many existing registrations/teams are affected and asks: "Update all" (reprice everyone, including already-registered) or "Future only." Flipping the payment phase asks whether to convert existing registrations, and at the age-group level it can offer to apply the change across every age group in the league.
- **Why it matters**: A routine fee tweak can now change the balances of people who already registered, depending on which button the director clicks. This is a common source of "why did this parent's balance suddenly change" — support needs to understand these prompts.
- *Dev evidence: blast-area probe + confirm (agegroup-detail.component.ts:548-568, 586-632; team-detail.component.ts:571-589).*

#### <span style="color:#0033cc">☐ CR-039: "Sort Age" can no longer be set</span>
- **Type**: Workflow-change (needs a decision) · **Audience**: Client support
- **What's new**: The old system let a director set a "Sort Age" number on each age group to control the order they appear in. The new system has no input for it, and new age groups all start at 0 — so ordering effectively falls back to the name.
- **Why it matters**: A director can't manually control age-group ordering anymore. Worth confirming whether dropping this was intended.
- *Dev evidence: sortAge passed through on save with no input (agegroup-detail.component.ts:749); new stubs default SortAge=0 (LadtService.cs:561).*

#### <span style="color:#0033cc">☐ CR-040: "Max Teams per Club" can't be set in setup — even though the system enforces it when it's non-zero</span>
- **Type**: Workflow-change (gap — needs a decision) · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system let a director set a per-club team cap on each age group (default 100). The new system has no input for it — only overall Max Teams is editable — and new age groups default it to 0. A value of 0 means "no limit," so registration isn't blocked. But when the value is above 0, it IS enforced (see CR-004). So the cap still works if set, but there's no longer a way to set it through the UI.
- **Why it matters**: A tournament director who wants to limit how many teams one club can enter in an age group can't do it in setup anymore. The enforcement exists (CR-004) but the control to configure it is gone. Worth deciding whether to bring the field back.
- *Dev evidence: verified — enforced only when > 0 (TeamRegistrationService.cs:713); no input, passed through on save (agegroup-detail.component.ts:744); new stubs default 0 (LadtService.cs:560).*

#### ☐ CR-041: Leagues can't be added or removed from the setup tree
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system let you add and delete leagues from the setup tree. In the new system the "+" add button only appears for age groups, divisions, and teams, and a league can't be deleted. You can rename and configure a league, but not create or remove one from the tree.
- **Why it matters**: The league structure is now fixed at the job level. If a director needs a new league or wants one removed, that's not a self-serve tree action anymore.
- *Dev evidence: add "+" only for agegroup/division/team (ladt.component.html:162,243,248,253); canDelete false for leagues (ladt.component.ts:574-576).*

#### ☐ CR-042: Deleting a team now moves it to "Dropped Teams" and zeroes its players' fees (unless it's empty)
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system, deleting a team removed it outright. In the new system, "deleting" a team that has any history moves it into an auto-created "Dropped Teams" age group and deactivates it — zeroing its players' fees and re-syncing the club rep's finances. It's only truly deleted if it has no players, payments, or schedule history. The confirm dialog explains this.
- **Why it matters**: A "deleted" team doesn't vanish — it shows up under Dropped Teams, and its players' balances go to zero. Support needs this to explain where a removed team went and why balances changed.
- *Dev evidence: drop path + Dropped-Teams auto-create (LadtService.cs:994-1026); confirm dialog ladt.component.ts:228-230, 596-609.*

#### ☐ CR-043: Cloning now asks what to copy (and the copy is named "(Copy)")
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old Clone Team made a single full copy named "[name] - Clone" — always copying everything, no choices. The new Clone Team opens a dialog with toggles for what to copy: club linkage, fees, eligibility, roster settings, dates, visual identity (with a rule that copying club linkage forces copying fees). There's also a matching Clone Age Group dialog, which didn't exist before. The default copy name is now "[name] (Copy)."
- **Why it matters**: Cloning is more capable but more decision-heavy. A clone made with "copy fees" turned off comes in with no fees — support should know the copy is selective now, not an automatic full duplicate.
- *Dev evidence: clone-team-dialog.component.ts:33-103; clone-agegroup-dialog.component.ts:33-81. (Clone Age Group is LADT SP-021.)*

#### ☐ CR-044: Some league settings from the old system aren't in the new league editor
- **Type**: Question (needs a check) · **Audience**: SuperUser/Admin + Client support
- **What's new**: The old league editor had Allow Coach Score Entry, Show Schedule to Team Members, a Standings Sort Profile, and a per-league Player Fee Override. The new league panel has only League Name, Sport, Hide Contacts, Hide Standings, a "reschedule emails to" field, and the fee cards. The Player Fee Override is covered by the new fee cascade, but the other three aren't set here anymore.
- **Why it matters**: If a director asks for Allow-Coach-Score-Entry, Show-Schedule-to-Team-Members, or Standings-Sort-Profile, support needs to know whether those moved to another screen or were dropped. Flagged to confirm before it becomes a talking point.
- *Dev evidence: league-detail.component.ts:42-107; confirmed absent from the LADT editor, not traced to another page.*

#### ☐ CR-045: New bulk "Division Name Sync" tool (rename/create/delete divisions across all age groups at once)
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: A new Actions tool lets an admin enter a set of "theme names" and preview and apply them to create, rename, or remove divisions across every age group in one operation.
- **Why it matters**: A powerful structural bulk edit — it can rename and create divisions job-wide, and it can also delete them. Admins should know it exists and that it can remove divisions in bulk.
- *Dev evidence: ladt.component.ts:1273-1374.*

#### ☐ CR-046: The "Unassigned" division is now protected and can't be renamed or deleted
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: Every new age group still gets an "Unassigned" division automatically, and now the system blocks renaming or deleting it ("The 'Unassigned' division is required and cannot be renamed or deleted").
- **Why it matters**: Support can reassure directors the Unassigned bucket is intentional and safe — attempts to rename or delete it are refused on purpose.
- *Dev evidence: division-detail.component.ts:18-35, 155; auto-create LadtService.cs:567-576.*

#### ☐ CR-047: The "max roster of 0 means unlimited" warning is gone
- **Type**: UX (worth restoring) · **Audience**: Client support
- **What's new**: In the old system, setting a team's Max Roster to 0 popped a warning: "a roster max of 0 means UNLIMITED ROSTER SIZE." The new system shows no such warning — 0 still means unlimited, but silently.
- **Why it matters**: A director who enters 0 (or leaves it at 0) no longer gets told they've created an unlimited roster — a quiet trap. Worth restoring the warning.
- *Dev evidence: no warning on the team-detail Max Roster input; 0 is still treated as unlimited in the read-only roster display.*

<div style="page-break-before: always;"></div>

## Admin Search Tools — differences from the old system

_From a comparison of the old system and the new one (2026-07-12/13), each checked against the new build. Covers the admin Search Registrations and Search Teams screens plus the SuperUser Change Password / account-repair tool (CR-056–059); deeper payment detail lives in the Accounting section._

#### ☐ CR-048: The admin Team Search screen now handles refunds, charges, and more in one place
- **Type**: Training-note · **Audience**: SuperUser/Admin
- **What's new**: The old admin team tools could search teams, edit basic info, and view transactions. The new screen adds, all in one place: issue a refund (it decides whether to void or refund based on the payment's status), charge a team or a whole club by card, record checks and corrections, view and cancel an autopay subscription, resend invoices to anyone whose autopay failed, and move a team to a different club (or move all of a rep's teams and deactivate).
- **Why it matters**: Admins now handle refunds, manual charges, check and e-check entry, autopay cancels, and payment-failure follow-ups from one screen — a lot to learn for whoever runs support operations.
- *Dev evidence: TeamSearchService.cs — refund :367-501, charge :506-563, check/correction :590-739, autopay :178-279, resend :808-942, club moves :749-797.*

#### ☐ CR-049: You can no longer text families from the search tool — batch send is email only
- **Type**: Workflow-change (removed capability) · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system, the batch action on Search Registrations could send both email AND text messages — it looked up each person's cell carrier and sent texts. The new system's batch tool is email only; there's no SMS/text option anywhere on the search screens. Batch email now runs as a background job with a progress bar and an optional "email me the summary."
- **Why it matters**: Anyone trained to "check the Text Message box" to text families needs to know that capability is gone. Batch communication from search is email only now.
- *Dev evidence: verified — no SMS/text anywhere in views/search; batch endpoint is email-only (RegistrationSearchController.cs:437-460).*

#### ☐ CR-050: Deactivating a team from Team Search "drops" it (zeroes player fees) and can't be undone from that screen
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: In the old system, the team active toggle in search just flipped a flag on and off. In the new system, turning a team off routes through a confirmed "Drop Team" — it reports how many players are affected and zeroes their fees. You can't turn it back on from the search panel; reactivating means re-adding the team via Pool Assignment / setup so fees and club-rep finances recompute.
- **Why it matters**: "Deactivate" is now a financial event, not a cosmetic flag, and there's no un-drop button on this screen. (This is the Team Search view of the same drop behavior as CR-042.)
- *Dev evidence: team-detail-panel.component.ts:239-278; LadtController.cs:347-355.*

#### ☐ CR-051: Admins can now delete a registration outright (heavily gated)
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old search had only active/inactive — no way to delete a registrant. The new detail panel has a Delete control, but it's heavily gated: it's blocked if the registration has any accounting history (it tells you to "make inactive" instead), and for a club rep it only shows to a superuser searching club-reps-only whose rep owns zero teams.
- **Why it matters**: A new destructive action exists. Support should know it's there, that it's blocked whenever there's any money history, and that "make inactive" is the fallback.
- *Dev evidence: registration-detail-panel.component.ts:287-302, 1011-1029; RegistrationSearchController.cs:524-539.*

#### ☐ CR-052: You edit a registrant in a detail panel now, not by editing the grid directly
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: In the old system the results grid was directly editable — an admin changed sizes, position, grad year, grade, uniform number, waiver checkboxes, etc. right in the grid rows. In the new system the grid is read-only; all editing happens in the per-registrant detail panel, with its own Save buttons per section and unsaved-changes warnings. The same fields are there — the workflow changed from "edit in the grid" to "open the record and edit."
- **Why it matters**: Anyone used to fast in-grid edits now works one record at a time in a drawer. This is a real retraining point.
- *Dev evidence: read-only grid (search-registrations.component.html:751-831); editing in detail panel (registration-detail-panel.component.ts:475-724).*

#### ☐ CR-053: The search filters are expanded — but the Covid-waiver filter is gone
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The new search adds a lot of filters the old one didn't have: a club-ownership tree (by club rep), insured vs. not, autopay-health (behind / expired), USA Lacrosse membership expired, roster-threshold, and a combined payment-type / discount-code filter. One old filter is gone: the Covid-19 waiver Signed / Not-Signed filter.
- **Why it matters**: Much stronger targeting for follow-up (insurance, delinquent autopay, lapsed memberships), but if support used the Covid-waiver filter, it's no longer available.
- *Dev evidence: RegistrationSearchDtos.cs:42-66; filter handlers search-registrations.component.ts:1119-1195.*

#### ☐ CR-054: New "Live update" button re-checks a player's USA Lacrosse membership on the spot
- **Type**: Workflow-change · **Audience**: SuperUser/Admin (lacrosse events)
- **What's new**: The old system showed USA Lacrosse membership details as read-only, possibly stale query data. The new detail panel has a "Live update" link that re-checks USA Lacrosse and records the returned expiry date right onto the record. The expiry field itself is read-only, so an admin can't hand-type an unverified date.
- **Why it matters**: Support can refresh a membership that looks lapsed on the spot, instead of trusting stale data or guessing.
- *Dev evidence: registration-detail-panel.component.ts:415-456; RegistrationSearchController.cs:129-139.*

#### ☐ CR-055: On big events, "select all on screen" isn't everyone — Email All and Export act on the whole filtered set
- **Type**: Training-note · **Audience**: SuperUser/Admin
- **What's new**: The old search loaded all results into the grid at once, so email/export worked on everything loaded. The new registration search pages the results (100/500/1000 at a time) and holds only one page. Selecting "all" selects the current page (selections do carry across pages as you go). But Excel Export re-runs the search across the entire match, and "Email All" sends to the whole filtered audience resolved on the server — not just the page you can see.
- **Why it matters**: On a big event, "select all on screen" is NOT the same as "everyone." Email All and Export deliberately act on the full filtered set. Operators should understand this so they don't under-send (thinking one page was everyone) or over-send (thinking they only emailed the visible page).
- *Dev evidence: server-side paging + cross-page selection (search-registrations.component.ts:235, 707-771); export unpaged (843-869); Email-All server-resolved (884-905).*

#### ☐ CR-056: Merging duplicate logins is now a deliberate, confirmed step — not a side-effect of editing
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: In the old Change Password tool, merging two logins happened as a *side-effect* — you changed a username in the edit dialog and it quietly merged the accounts on save, so you could merge the wrong people by fat-fingering a dropdown. The new tool makes merge its own separate, confirmed dialog that shows exactly which registrations will move before you commit. The matching is also tighter: it lines up accounts by email AND phone AND name together, so it can't accidentally pull in a different family. And it now moves a person's inactive/dropped registrations too, instead of leaving them stranded on the old login.
- **Why it matters**: The most destructive, irreversible action in the tool can no longer happen by accident, and a merged parent gets their full history (including inactive registrations) rather than losing some of it. Worth knowing since you use this tool.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: dedicated confirmed merge dialog (change-password.component.ts:329-424); merge endpoints (ChangePasswordController.cs:284-368); email+phone+name identity, inactive included (ChangePasswordRepository.cs:526-544, 774-794).*

#### ☐ CR-057: Every password change and merge is now logged — and the log is the only way to undo a merge
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old tool kept no record of who changed whose password or who merged which accounts — just a success message on screen. The new tool logs every change and merge (who did it, their IP, the target account), and for a merge it records exactly which registrations moved and who owned them before. Since a merge can't be undone through the UI, that record is the only way to reverse one.
- **Why it matters**: There's now a real audit trail for this powerful cross-account tool, and it's the only path back from an accidental merge. (The log lives in Seq, tagged cp_audit.)
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: audit events + reversal payload (ChangePasswordController.cs:54-73, 151-175, 342-347).*

#### ☐ CR-058: Resetting a player's password now resets the FAMILY login, and shows who it signs in for
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: In the old tool, resetting a player's password reset the player's own account — but children don't have a usable login, so "I reset it and it still won't work" was a real trap. The new tool resets the family login instead (the one that actually signs in), labels the button as the family, and shows which children that login covers before you do it.
- **Why it matters**: The reset now targets the credential the family actually uses, and you can see its blast radius (which kids it covers) before committing. Removes a long-standing footgun.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: player reset targets family login + reset-context blast radius (change-password.component.ts:269; ChangePasswordController.cs:97-125; ChangePasswordRepository.cs:299-400).*

#### ☐ CR-059: The account search is capped at 50 logins and tells you when results were cut
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old search returned everything with no limit. The new one caps at 50 login accounts and flags the results when they were truncated, so a broad search doesn't silently hand you a giant or partial list. It also enforces a 6-character minimum on the new password and gives you a copy-to-clipboard reset dialog.
- **Why it matters**: You'll know when a search was too broad and got cut (narrow it down) rather than trusting an incomplete list. Minor quality-of-life on top of that.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: 50-account cap + truncation flag (ChangePasswordRepository.cs:23-24, 114-135; change-password.component.ts:28, 93-96); 6-char floor (ChangePasswordController.cs:145).*

<div style="page-break-before: always;"></div>

## Communications — differences from the old system

_From a comparison of the old system and the new one (2026-07-13), each checked against the new build._

#### ☐ CR-060: Emails now come "from" TeamSportsInfo, with the director on Reply-To
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system, a batch email sent from the admin search screen showed the sending director's own email address as the sender. In the new system, every outgoing email is sent "from" support@teamsportsinfo.com — with the sender's name branded as "[Name] (TEAMSPORTSINFO.COM)" — and the real person (the director or the job's contact) is put on Reply-To. Replies still go to the director.
- **Why it matters**: Parents and reps now see mail "from TeamSportsInfo," not their club or director — expect "who is this / is this legit / why isn't it from our club?" questions. Replies still reach the right person. One catch: if a recipient had whitelisted the director's address, that no longer matches the sender. This is deliberate — the email service can only send from a verified address.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: NormalizeFromHeader forces From to support@ + branding (EmailService.cs:186-195); real sender on Reply-To (:115-135).*

#### <span style="color:#c00000">☐ CR-061: Two Communications-tab email-list fields do nothing (Always Copy, Reschedule Email List)</span>
- **Type**: Bug (needs a decision) · **Audience**: SuperUser/Admin
- **What's new**: On the Communications tab, the "Always Copy Email List" and the job-level "Reschedule Email List" fields are still shown, saved, and copied to new jobs — but nothing actually uses them. No email-sending code reads either one. (Reschedule notices DO send extra copies, but they pull from a different, league-level "Reschedule Emails To" field, not this one.)
- **Why it matters**: A director who fills in "Always Copy" (say, to copy the league office on every blast) or the job-level "Reschedule Email List" silently gets nothing — the field looks like it works but doesn't. A support trap. Either wire these up or remove them; and point directors to the League-level "Reschedule Emails To" field for reschedule copies.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: verified — Alwayscopyemaillist / Rescheduleemaillist appear only in config save/load and clone (JobConfigService, JobCloneService), never in any send path. Reschedule extra-recipients come from Leagues.RescheduleEmailsToAddon (ScheduleRepository.cs:1164-1184).*

#### ☐ CR-062: Recipients can unsubscribe from an event's emails — and then stop getting them
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: Every batch email now includes an "Unsubscribe from emails for this event" footer with a per-person link. If a parent clicks it, they're opted out and dropped from all future batch emails AND reschedule notices for that event. The old system had no unsubscribe.
- **Why it matters**: A direct answer to "why didn't this family get the email?" — they may have unsubscribed. It's per-event and per-registration. Note: league-office / operational addresses added as extra recipients aren't registrations, so they can't unsubscribe and always get copies.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: unsubscribe footer + link (EmailBatchService.cs:253-270), opt-out endpoint (EmailController.cs:19-31), opted-out partitioned out before send (:64-65); reschedule footer (ScheduleRepository.cs:1108-1122).*

#### ☐ CR-063: The office is copied on TEAM confirmations but not PLAYER confirmations
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: The Communications-tab CC/BCC email lists still copy the office on team / club-rep confirmation emails (CC/BCC applied, and the job's From goes to Reply-To). But player confirmations ignore those lists — they go only to the family and player (that's the already-noted CR-012). So the two are asymmetric.
- **Why it matters**: "We get a copy of team registrations but not player registrations" is now expected behavior, not a bug. If a director wants office copies of player confirmations too, that ties to the CR-012 decision.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: team confirmation applies RegFormCcs→CC, RegFormBccs→BCC, RegFormFrom→Reply-To (TeamRegistrationService.cs:1652-1678); player confirmation sets none (PaymentService.cs:2458-2469).*

#### ☐ CR-064: The batch email composer has ready-made templates that appear based on your filter
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old composer was free-form subject/body only. The new one offers ready-made templates grouped by purpose — autopay follow-ups ("update card," "pay balance," "card expiring"), insurance reminders, and waitlist-activation. Which templates show up depends on the job's features and how you filtered the search grid: a template only appears when its conditions are met (e.g. the autopay templates only show on an autopay job filtered to behind-in-payment).
- **Why it matters**: "The template I expected isn't in the list" is usually because the grid filter or job settings don't match its rule — not a bug. Admins should know the menu is context-driven.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: template rules + availability (email-templates.ts:94-273; batch-email-modal.component.ts:186-197).*

#### ☐ CR-065: The batch composer can draft an email with AI
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: New "draft with AI" action — an admin types what they want to say, and the system generates a subject and body they can edit before sending. Didn't exist before.
- **Why it matters**: A new capability admins may ask about. The AI output is a starting draft, not auto-sent — the admin reviews and edits.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: draftWithAi (batch-email-modal.component.ts:260-276), AiComposeController.cs:26-40 (admin-only).*

#### ☐ CR-066: New "Invite" batch send with personal magic links that expire
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: A new "Invite" action sends each recipient a personalized invitation link (for players or club reps). The admin picks the target event and how long the link lasts (6 to 72 hours, default 24). Each link is unique to the recipient and can't be forwarded.
- **Why it matters**: This is how invitation-only events (CR-009 team, CR-014 player) get their links out. Because links expire (24h by default), "my invite link says expired" is expected — the fix is to re-send. Per-recipient links mean a forwarded link won't work for someone else.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: invite templates + per-recipient link/expiry + send guard (batch-email-modal.component.ts:30-63, 142-180, 316-345).*

<div style="page-break-before: always;"></div>

## Login, Navigation & Bulletins — differences from the old system

_From a comparison of the old system and the new one (2026-07-13), each checked against the new build. Grouped: Login/Auth, then Navigation, then Bulletins._

#### <span style="color:#0033cc">☐ CR-067: Password reset works by email only now — not username</span>
- **Type**: Workflow-change (needs a decision) · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old "forgot password" looked you up by username first, then email, and also matched a parent's family-account email. The new one takes an email address only — no username option, and it doesn't check family emails. It also always replies "if an account with that email exists, a link has been sent" (it won't tell you whether the account was found).
- **Why it matters**: A user who remembers only their username — common for adult and admin accounts — can't reset their password from the form anymore, and a parent whose login email differs from the one on file may not be found. A real "I can't reset my password" support case. Worth deciding whether to bring back the username path.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: verified — ForgotPassword uses FindByEmailAsync only, generic no-enumeration response (AuthController.cs:470-488); email-only form (forgot-password.component.ts:18-34).*

#### ☐ CR-068: Sessions refresh silently instead of logging you out
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system used cookie sessions that would expire and force a re-login. The new system uses a token that refreshes itself quietly in the background (it checks every minute and renews a few minutes before expiry), so an open tab keeps working. Logging out revokes the token and returns you to the login page.
- **Why it matters**: Far fewer "I got logged out" complaints. The whole session/timeout story support fields is different — a tab left open generally just keeps working.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: silent refresh timer (auth.service.ts:242-271, 539-571); token refresh endpoint (AuthController.cs:355-424).*

#### ☐ CR-069: Login no longer blocks or warns about "you're a Director, can't also be a Club Rep"
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system enforced role separation at login — it would block sign-in with warnings like "this account was used as a Director, it can't also be a Club Rep," and picking a role with no active registrations would sign you out. The new system doesn't do any of that at login: it shows all your active registrations grouped by role, handles role separation when a registration is created instead, and picking an empty role just shows a quiet "no registrations available" without logging you out.
- **Why it matters**: The old blocking warnings and forced logouts that families and admins saw at login are gone. Support scripts that referenced those messages need updating.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: RoleLookupService returns all active regs by role, separation now at registration-creation time (RoleLookupService.cs:7-13); passive empty-role state (role-selection.component.ts:75-93).*

#### ☐ CR-070: New role-selection screen, plus a "Suggested Events" list
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old role picker was a plain list. The new one shows role cards (or a type-ahead search when you have lots of registrations) and auto-opens your most recent role. It also adds a new "Suggested Events" section — events you have history with (as family or club rep) but haven't registered in yet.
- **Why it matters**: A different sign-in experience to walk users through, and "Suggested Events" is a net-new thing users may ask about.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: role-selection.component.ts:24-172; suggested-events (AuthController.cs:253-282).*

#### ☐ CR-071: Page URLs changed — every address now includes the event's jobPath
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system, page URLs looked like /[event]/Search/Index. In the new system every page lives under the event's jobPath and uses the new address style, like /[jobPath]/search/registrations. Old-style bookmarked links generally won't resolve unless there's a specific redirect for them.
- **Why it matters**: Any bookmarked or documented old URL may not work anymore. Directors or support who saved links from the old system will need to refresh them.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: app.routes.ts:47-48, 635-660; routerLinks (client-menu.component.ts:324-361).*

#### ☐ CR-072: Menus are now shared defaults plus per-event tweaks — editing a default changes every event
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old system gave each event its own complete copy of the menu, edited independently. The new system has one shared set of default menus that all events use, and each event stores only its overrides (hide certain items, add event-specific ones). The Nav Editor shows this as "Defaults" and "This Job" tabs.
- **Why it matters**: Editing a default menu item now changes it for EVERY event at once — a very different admin mental model. Per-event customization is limited to hiding items and adding new ones. Admins need to be sure which tab they're editing.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: shared defaults + per-job override merge (NavRepository.cs:72-233); Defaults/This-Job tabs (nav-editor.component.ts:121-136, 208-275).*

#### ☐ CR-073: Regular users no longer get a menu — they get a personalized task list
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: In the old system each role (family, player, coach, club rep) got a dropdown menu of items. In the new system non-admin users get no menu rail at all — instead the avatar dropdown shows a short task list built for them from the event's live state (e.g. a club rep sees Edit Profile, Team Registration, Pay Balance Due, Club Rosters). An urgent "Pay Balance Due" gets pushed to the top with a nudge — except for autopay registrants, who don't get the nudge.
- **Why it matters**: "Where did menu item X go?" now has a completely different answer for regular users — it's a dynamic task list, not a fixed menu, with different wording.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: client-header-bar.component.ts:107-194.*

#### ☐ CR-074: Which menu items appear is now driven by event settings, evaluated on the server
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: In the old system, showing or hiding a menu item based on features (store on, autopay on, job type, etc.) was hardcoded — changing it meant a code change. In the new system each menu item carries visibility rules the server evaluates against the event's flags (store enabled, autopay, mobile, age-range eligibility, etc.) plus sport / job-type / role.
- **Why it matters**: "Why is this menu item showing / missing?" is now answered by the event's settings and the item's rules, evaluated consistently — not by digging through code. Turning a feature on or off changes the menu accordingly.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: VisibilityRulesEvaluator.cs:34-118; rules on nav items (NavRepository.cs:272-298).*

#### ☐ CR-075: The Nav Editor has new bulk tools, and admins get a persistent nav rail
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old menu admin could only add/edit/delete/activate items and do a single swap within one event's menu. The new Nav Editor adds cloning a branch across roles, cascading a route change to every role that shares an item, exporting the tree as SQL, moving items between groups, and reorder-by-number. Admin users also get dedicated navigation chrome — a collapsible side rail, a horizontal-bar option, hover flyouts, and a mobile off-canvas menu.
- **Why it matters**: New admin capabilities and a changed admin navigation surface to train on.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: nav-editor.component.ts:466-497, 542-606, 643-727; admin chrome (client-menu.component.ts:31-38, 152-197).*

#### ☐ CR-076: "Smart Bulletins" — a new band of automatic, always-current announcements
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system's bulletins were 100% hand-typed static text. The new system adds a Smart Bulletins band, pinned above the hand-authored ones, that writes itself from the event's live state — it announces things like "registration opens tomorrow," "registration is closed," "this event has concluded," and shows a live game-day/schedule panel with a game clock, a store card, and a USA Lacrosse notice. Each piece hides itself when it doesn't apply. It shows on the public site and on the admin dashboard, and it's read-only (not directly editable).
- **Why it matters**: A whole new class of system-written announcements. Expect "where did this box come from / how do I turn it off?" — the answer is it's automatic and reflects the event's current state. (This is the "this event has concluded" banner Todd saw on Taste of the South.)
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: smart-bulletins.component.ts:40-164; event-status.component.ts:33-64.*

#### ☐ CR-077: Hand-written bulletins can now embed live buttons that appear/hide by event state
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: The old bulletins were static text (substitution could inject data values, but not live buttons). The new system lets an author drop in tokens like !REGISTER_PLAYER or !SCHEDULE that turn into live buttons/cards and hide themselves automatically when they don't apply — the register button disappears when registration is closed, the schedule button appears only once the schedule is published, and a token-gated event shows "(invite required)."
- **Why it matters**: One authored bulletin now behaves differently depending on the event's state, without editing. A new authoring concept for admins, and a reason a bulletin's buttons may look different at different times.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: token registry + resolvers (BulletinTokenRegistry.cs:27-41; Bulletins/TokenResolution/Resolvers/). Note: the !REGISTER_SELFROSTERPLAYERSANDCOACH token currently points at a placeholder route (link target unfinished) — flag if a bulletin uses it.*

#### ☐ CR-078: Old personalized bulletin tokens don't work on the public site anymore
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: In the old system, public bulletins could include personalized/financial tokens like !PERSON, !AMTOWED, !AMTPAID, !TEAMNAME. The new public bulletins deliberately carry no viewer identity, so those tokens aren't supported — only a small public-safe set works (!JOBNAME, the USA Lacrosse date, and the live-button tokens). An unsupported token is left as raw text.
- **Why it matters**: Any bulletin carried over from the old system that used a personalized or financial token will now show the raw !TOKEN text (or nothing) to the public. Migrated bulletins should be audited and rewritten. (Personalized content now lives in the email channel instead.)
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: verified — TokenContext deliberately has no viewer identity (TokenContext.cs:5-16); only public-safe tokens resolved (BulletinService.cs:234-239).*

#### ☐ CR-079: New bulletin authoring tools — AI drafting, a token palette, and live preview
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old bulletin editor was a plain form (title/text/active/dates). The new one adds "Draft with AI" and "Reformat with AI," a clickable palette of the available tokens, a side-by-side preview showing how the bulletin resolves, and toggles to simulate different event states (registration open, schedule published) so an author can see how the live tokens will behave. The token palette and preview are SuperUser-only.
- **Why it matters**: A big new authoring surface. The AI and preview features are what directors will ask about. Note that non-SuperUser admins don't see the token palette.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: bulletin-form-modal.component.ts:119-184, 450-566; BulletinsController.cs:46-103.*

<div style="page-break-before: always;"></div>

## Coach/Staff Registration & Roster Management — differences from the old system

_From a comparison of the old system and the new one (2026-07-13), each checked against the new build. Grouped: Coach/Staff Registration, Coach Approvals, Roster Swapper, then Roster Visibility._

#### ☐ CR-080: Coaches can no longer put themselves on a team — every coach goes through an approval queue
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system a coach registering for a tournament picked their teams and was rostered **immediately** — nobody had to approve them. (On club/league events they landed unassigned and a director dragged them onto a team.) In the new system every coach, on every event type, registers as an unassigned adult. The teams they pick are only a **request**. A director must approve them in the new Coach Approvals queue before they're actually on a roster.
- **Why it matters**: The single biggest change in the coach lifecycle, and the answer to "why isn't my coach on the roster yet?" Tournament directors who relied on coaches self-rostering must now work the approvals queue — otherwise no coach lands on a roster (and no coach can see one).
- **Say to clients**: "Coaches request the teams they want; a director approves them. Until that happens they're not on the roster."
- *Dev evidence: every team job type resolves to UnassignedAdult (AdultRegistrationService.cs:1332-1367); team picks stored as a request (:1051-1085); role keys can't resolve to Staff (AdultRegRoleKeys.cs:34-40).*

#### ☐ CR-081: Coach registration has its own on/off switch, and needs teams to exist first
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old staff-registration link worked whenever it was shared. Now each adult role (coach/staff, referee, recruiter) has its own "allow registration" switch, and the event must also have teams and not be concluded. If the switch is off — or there are no teams yet — the coach gets "coach/staff registration is not currently open for this event."
- **Why it matters**: A brand-new failure mode. If a coach says registration won't open, check the Allow Staff Registration switch and that the event actually has teams.
- **Status**: Open — pending review with Chelsea.
- *Dev evidence: per-role door + teams-exist/concluded check (AdultRegistrationService.cs:81-95, 1315-1322).*

#### ☐ CR-082: USA Lacrosse numbers are now really validated, with an email code to prove it's you
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system pinged USA Lacrosse and just stored the expiry — an expired or non-coach membership wasn't rejected, and nothing proved the person owned the number. The new system **rejects** the registration if the membership isn't an active *coach* membership. It also offers a two-step identity check: it emails a 6-digit code to the address USA Lacrosse has on file, and confirming it marks the coach "Verified" in the approval queue. A coach who can't reach that email can skip it and continue — they just show as "Unverified."
- **Why it matters**: Two new hard stops ("this USA Lacrosse number is not an active coach membership"), a new code-by-email support path, and a Verified/Unverified badge directors are meant to read before granting roster access.
- *Dev evidence: rejects non-active-coach memberships (AdultRegistrationService.cs:1096-1154); OTP begin/confirm (AdultRegistrationController.cs:127-165); badge (coach-approval-queue.component.html:110-117).*

#### ☐ CR-083: Coach/staff registrations can now carry a fee (in practice they're still free)
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: In the old system adult registrations were always free — there was no payment step at all. The new coach wizard has a payment step that appears only if a fee is actually owed. Today no adult fee is configured (and there's no UI to set one), so coaches still pay $0 and never see the step — but the plumbing exists, and adding a fee for a role would quietly switch a payment step on.
- **Why it matters**: Coaches are free today. If a coach is ever asked to pay, that's a fee-configuration problem, not a broken wizard.
- *Dev evidence: payment step gated on owed > 0 (adult.component.ts:60-66; adult-wizard-state.service.ts:165); adult fee defaults to $0 (FeeResolutionService.cs:136-141).*

#### <span style="color:#c00000">☐ CR-084: The confirmation screen says an email is on its way — but it never sends</span>
- **Type**: Bug · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old system emailed a coach a confirmation as soon as they registered. The new system shows "a confirmation email is on its way" on the final screen — but **nothing actually sends it**. The only way a coach gets one is if they notice and click "Resend Confirmation Email" themselves.
- **Why it matters**: Coaches will report "I never got a confirmation," and the screen told them one was coming. A real regression plus a false statement in the UI. (The team-registration flow does send its confirmation, so this is specific to the coach/adult path.)
- *Dev evidence: verified — the adult SendConfirmationEmailAsync has exactly one caller, the manual resend endpoint (AdultRegistrationController.cs:323); nothing in the submit or payment paths calls it. Compare TeamRegistrationController.cs:740, which does.*

#### ☐ CR-085: One coach wizard replaced five, and the profile questions are configurable per event
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old system had five separate staff-registration paths with hard-coded question sets. Now there's a single wizard, and the profile questions come from the event's configuration per role. If nothing is configured, a coach just gets an optional free-text "anything else the director should know?" — because the team picks are now the main thing they're telling you.
- **Why it matters**: Question sets are a settings concern now, not a code change. Don't expect the old required "Special Requests" prompt.
- *Dev evidence: AdultRegistrationService.cs:132-177, 1465-1500.*

#### ☐ CR-086: There's a new director approval queue for coaches — nothing like it existed before
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: A new screen (LADT → Coach Approvals) lists every coach awaiting approval. Each row shows who they are, the teams they **requested** (their own pick) vs teams a director **granted**, their USA Lacrosse Verified/Unverified badge and membership expiry, whether they've been staff on other events or seasons before, and any of their own children registered in this event — all as recognition signals. Ticking a team grants them onto it. The old system had no approval concept at all.
- **Why it matters**: A brand-new admin workflow with no old equivalent — pure training. A director who doesn't know this screen exists will have zero coaches on rosters.
- *Dev evidence: queue + signals (RosterSwapperService.cs:452-483; RegistrationRepository.cs:2814-2984); admin-only route (app.routes.ts:266-269).*

#### ☐ CR-087: "Unassigned Adults" is gone from the Roster Swapper — coaches are approved in the new queue instead
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: In the old system you promoted a coach by dragging them out of the "Unassigned Adults" pool in the Roster Swapper. That pool has been deliberately removed from the swapper's picker — the flow now lives in Coach Approvals.
- **Why it matters**: A director going by old muscle memory will open the Roster Swapper, not find Unassigned Adults, and conclude it's broken. Point them at Coach Approvals.
- *Dev evidence: pool intentionally excluded from the picker (roster-swapper.component.ts:389-393).*

#### ☐ CR-088: Un-ticking one team and "Deny" are very different — Deny revokes everything at once
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: Two different actions in the queue. Un-ticking a granted team removes the coach from **just that team**. "Deny" removes them from **every** team in the event and drops them off the queue entirely — and there's no undo from the screen (their request history is kept, but they're gone from the list). Nothing expires on its own; Deny is the only exit.
- **Why it matters**: Directors and support must know the difference. Deny is a bigger hammer than it looks, and it isn't reversible from the screen.
- *Dev evidence: revoke-one-team (RosterSwapperService.cs:309-342) vs deny-all + deactivate (RegistrationRepository.cs:3164-3209).*

#### <span style="color:#0033cc">☐ CR-089: Approving or denying a coach doesn't tell the coach anything</span>
- **Type**: Workflow-change (needs a decision) · **Audience**: Client support + SuperUser/Admin
- **What's new**: Neither approving a coach onto teams nor denying them sends the coach any notification. They find out by logging in — if they think to. (The old system didn't notify either, but it also had no approval step.)
- **Why it matters**: Combined with the missing registration confirmation (CR-084), a coach registers, is told an email is coming, gets nothing, and is then approved or denied in silence. Worth deciding whether approval/denial should email the coach.
- *Dev evidence: RosterSwapperService has no email service injected; neither approve nor deny sends mail (RosterSwapperService.cs:20-44, 461-483).*

#### ☐ CR-090: Coaches rostered the old way show up in the queue automatically
- **Type**: Training-note · **Audience**: SuperUser/Admin
- **What's new**: When the Coach Approvals queue loads, it back-fills itself: any coach already holding a team the old way is added to the queue with their current teams marked as director-granted. It's safe to re-run, and it won't resurrect a denied coach or downgrade a coach's own pick.
- **Why it matters**: Explains why an existing or migrated event's Coach Approvals screen already has coaches in it showing granted teams — that's correct, not phantom data.
- *Dev evidence: seed-on-load (RosterSwapperService.cs:452-459; RegistrationRepository.cs:3035-3130).*

#### ☐ CR-091: Moving someone between teams no longer wipes out their fees
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: In the old system a roster move zeroed the person's fees and payments and then recomputed — and that recompute only ran for **players**, so moving a **coach** between teams permanently zeroed their money columns. The new swapper never zeroes anything: a player's fee is re-resolved from the new team's fee cascade (keeping discounts, late fees and donations), amounts owed/paid come from the payment ledger, and a coach move doesn't touch their money at all.
- **Why it matters**: The old behavior was a genuine bug that destroyed payment history on a coach's row. If anyone says "the old system zeroed fees on a swap" — they're right, and it's fixed.
- *Dev evidence: canonical re-resolve, nothing zeroed (FeeResolutionService.cs:255-301); staff moves skip fee recalc (RosterSwapperService.cs:376-381).*

#### ☐ CR-092: You see the fee change before you commit a transfer
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old drag executed immediately. The new swapper previews first: for each person it shows what kind of move it is, the current vs new fee, the difference, and warnings ("already on this team — will be skipped"). Committing is a separate step.
- **Why it matters**: A director can see a cross-age-group move's price change before doing it — and it's where to look when someone asks "why did this player's balance change after a swap?"
- *Dev evidence: preview endpoint (RosterSwapperService.cs:63-195; RosterSwapperController.cs:56-73).*

#### ☐ CR-093: Admins can overfill a team from the swapper — the roster max only blocks self-registration
- **Type**: Training-note · **Audience**: SuperUser/Admin
- **What's new**: The roster maximum stops a parent or player self-registering onto a full team, but it deliberately does **not** stop an admin moving someone onto it from the swapper. If a transfer brings a team up to its max, the waitlist mirror team is created at that moment, same as during registration.
- **Why it matters**: Directors can intentionally overfill from the swapper while a family is blocked from the same team. That asymmetry is on purpose — otherwise it reads as a bug report.
- *Dev evidence: no capacity check for admin transfers, by design (RosterSwapperService.cs:232-233, 356-358); waitlist minted at max (:414-419).*

#### <span style="color:#0033cc">☐ CR-094: A logged-in parent sees every family's contact details and every child's birthdate — and can now download it</span>
- **Type**: Workflow-change (privacy — needs a decision) · **Audience**: Client support + SuperUser/Admin
- **What's new**: When "Allow Roster View — Player" is on, a logged-in player/parent can see the team roster — and that roster includes each person's email, phone, **date of birth**, and **both parents' names, emails and phone numbers**. That was true in the old system too (a parent saw exactly what a coach saw). What's new is that the same parent can now **download the whole thing as a PDF**, parent-contact columns included.
- **Why it matters**: Turning on roster view for players hands every parent on the team an offline, bulk copy of every other family's contact details and every child's birthdate. The old system showed it on screen; it didn't hand out a file. Worth a deliberate decision — e.g. redact the contact/DOB fields for the player audience, or make the PDF admin-only.
- *Dev evidence: verified — roster data carries DOB + Mom/Dad email/phone with no role filter (MyRosterDtos.cs:34-52; RegistrationRepository.cs:2639-2699); the PDF endpoint uses the same visibility gate as the on-screen roster (MyRosterController.cs:36-52; MyRosterPdfService.cs:95-100).*

#### <span style="color:#c00000">☐ CR-095: The per-team "Hide Roster" setting no longer does anything</span>
- **Type**: Bug · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system, ticking "Hide Roster" on a team hid the roster from that team's members even when the event-level roster setting was on. In the new system the checkbox is still in the team editor, still saved, still copied on clone — but **nothing reads it**. It has no effect.
- **Why it matters**: A director who ticks "Hide Roster" gets no result and isn't told. Because it's a *visibility* setting, the failure mode is over-exposure. Note too: auto-created waitlist teams are actually created with Hide Roster set to true — so a waitlisted player is offered "view roster" for a team the old system would have hidden.
- *Dev evidence: verified — every reference to BHideRoster is create/update/clone/DTO/grid/checkbox; nothing reads it to gate a roster. It IS set true on waitlist teams (TeamPlacementService.cs:320), with no effect.*

#### <span style="color:#0033cc">☐ CR-096: Public rosters now work on Club and League events too, and they're on by default</span>
- **Type**: Workflow-change (needs a decision) · **Audience**: Client support + SuperUser/Admin
- **What's new**: In the old system the anonymous public roster lookup was a tournament-only page, always on, with no way to turn it off. In the new system the public roster works for **any** event type — including Club and League, which never had one — reachable by anyone who knows the event's link. There's a new "Show Public Rosters" switch to turn it off, but it defaults to **on**.
- **Why it matters**: Directors of club/league events may not realize their team rosters are now publicly reachable. The contents are still contact-free (name, uniform number, position, club, team — no emails, phones or birthdates), so this is about how widely rosters are exposed, not a leak of personal details. Worth deciding whether it should default to off for non-tournament events.
- *Dev evidence: public endpoint works for any job type + new kill switch (PublicRosterController.cs:16-17, 70-93); "Show Public Rosters" = not restricted (JobVisibilityService.cs:45-48). No equivalent switch exists in the old codebase.*

#### ☐ CR-097: Club Rep rosters stay contact-free, and reps can now move and delete players themselves
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: A club rep's view of their team rosters still shows only names, uniform numbers and positions — no contact details. New: a rep can now move a player between their **own** teams (the system checks they own both), with the fee re-resolved correctly for the new team; and they can delete a registration, unless it has payment history, in which case it's blocked.
- **Why it matters**: Club reps can self-serve roster moves that used to need a director, and the money follows the move. "Why can't I delete this registration?" is answered by: they've paid.
- *Dev evidence: no contact details in the club-roster data (ClubRosterDtos.cs:17-25); move + delete with fee re-resolve and accounting guard (ClubRosterService.cs:33-131).*

<div style="page-break-before: always;"></div>

## Profiles / Profile Editor — differences from the old system

_From a comparison of the old system and the new one (2026-07-13), each checked against the new build._

#### ☐ CR-098: The "Profile Editor" is a form designer — registration form fields are now configurable, not hard-coded
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: In the old system every registration form was a compiled code file — the fields, labels and which ones were required were baked in. Adding "jersey size" to one customer's form meant a developer changing code and redeploying. The old admin screen was a **read-only viewer**. In the new system the fields live as data on the job, and a SuperUser edits them in the Profile Editor: add, remove, reorder, rename, mark required, attach dropdown lists, set conditional display.
- **Why it matters**: The biggest capability change here. Support can now say "yes, that's configurable" instead of "that needs a code change." Note the Profile Editor is **not** an "edit my profile" screen — it's the form *designer*.
- *Dev evidence: metadata saved at profile-editor.component.ts:323 → ProfileMetadataMigrationService.cs:1091. Legacy read-only viewer: RegformFieldsController.cs:25-43.*

#### <span style="color:#c00000">☐ CR-099: Saving in the Profile Editor rewrites that form for EVERY job using it — not just yours</span>
- **Type**: Bug / hazard (needs a decision) · **Audience**: SuperUser/Admin
- **What's new**: When you save a form in the Profile Editor, it stamps the new field layout onto **every job that uses that profile type** — with no year filter and no active filter, so past events and other customers' events are rewritten too. The screen only tells you afterwards ("N job(s) affected"). The adult side is worse: there are only two adult profiles, so an adult save fans out very widely.
- **Why it matters**: This is the easiest way to break another customer's registration form from inside your own tool. The safe workflow is **Clone Profile first** (which forks this job onto its own profile), or use **Copy Forms** (CR-105), which really is job-scoped. This needs to be a loud warning in training, not a footnote — and arguably the tool should warn before saving.
- *Dev evidence: unfiltered fan-out (ProfileMetadataMigrationService.cs:1097-1129 player, :1736-1760 adult; ProfileMetadataRepository.cs:28-33 — a StartsWith query with no filters). Clone-then-repoint at :1976-1984.*

#### <span style="color:#c00000">☐ CR-100: SECURITY — the family-update endpoint has no login check and trusts a username from the request</span>
- **Type**: Bug (security — escalate) · **Audience**: SuperUser/Admin
- **What's new**: `PUT /api/Family/update` — used by the "Edit Family Account" screen — has **no authentication requirement at all**, and it identifies the family from a **username sent in the request body** rather than from the logged-in user. So anyone who knows a family's username can rewrite that family's address and phone, both parents' names, emails and cellphones, update the children's details, and even add new children — **without logging in**.
- **Why it matters**: This is a live security hole, not a training note. The tell that it's an oversight: the sibling endpoints right beside it (add/edit/delete a child) **do** require a login and take the family from the logged-in user — this one skips the exact check its neighbours enforce. **The fix** is to match them: require authentication and derive the family from the logged-in user instead of the request body. (Worth checking first that the edit screen is only used by logged-in families, so the fix doesn't break new-family signup.) It does **not** change the parent's login email, so it isn't a one-step account takeover — but it is unauthenticated tampering with families' and minors' personal data.
- *Dev evidence: verified — no [Authorize] on the action and none on the controller (FamilyController.cs:101); no fallback policy, so the endpoint is genuinely anonymous (Program.cs:563); service trusts request.Username (FamilyService.cs:519) then overwrites contacts/children (:525-566). Sibling child endpoints DO authorize (FamilyController.cs:124+).*

#### ☐ CR-101: Fields can now be marked public / admin-only / hidden — and it's enforced on the server
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: Every form field now carries a visibility setting. Admin-only and hidden fields are stripped out of the form the registrant sees, skipped by required-field checks, and refused if someone tries to submit them anyway. On the coach form, the background-check fields ship as admin-only, so they're never registrant-visible.
- **Why it matters**: The usual support question is "I added the field in the editor but it isn't on the form" — the answer is almost always visibility. It's also what keeps sensitive fields off the public form.
- *Dev evidence: ProfileMetadata.cs:63-70; server enforcement PlayerFormValidationService.cs:85-105, PlayerRegistrationMetadataService.cs:103-133; admin-only defaults adult-allowed-fields.ts:31-32.*

#### ☐ CR-102: The editor can only place fields from a fixed catalogue — it can't invent new ones
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: The "Add Field" picker offers a fixed list — 64 player fields and about 25 adult fields — each mapping to a real column. You can't create a brand-new field from the editor.
- **Why it matters**: Sets the honest boundary for "can you add field X to my form?" — yes if it's in the catalogue, otherwise it's still a code change. Keeps support from over-promising.
- *Dev evidence: allowed-fields.ts (64 player entries); adult-allowed-fields.ts:1-61.*

#### ☐ CR-103: Coach, Referee and Recruiter now have separate forms
- **Type**: Workflow-change · **Audience**: SuperUser/Admin + Client support
- **What's new**: The old system had one coach form. Now the adult form is split by role — Coach/Volunteer (which also covers Staff), Referee, and Recruiter — each with its own field set, edited as separate tabs in the Profile Editor.
- **Why it matters**: "The referee form and the coach form on the same event are different forms now" is a real new mental model. Also explains why editing the Staff form means editing the *Coach/Volunteer* tab.
- *Dev evidence: AdultMetadataRoleResolver.cs:20-35; adult-profile-editor-panel.component.ts:32-37.*

#### ☐ CR-104: Dropdown option lists are edited in a proper UI now — and coach sizes are separate from player sizes
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old system edited dropdown options as a raw JSON blob. The new Profile Editor has an Options tab with create/rename/reorder/delete. Separately, coach apparel sizes now use their own lists, deliberately separate from the player size lists.
- **Why it matters**: No more raw-JSON editing. But a returning director will notice the coach/player size split ("my coach sizes are separate now / came up blank").
- *Dev evidence: options tab gating profile-editor.component.ts:76-81; coach-namespaced size keys adult-allowed-fields.ts:43-50.*

#### ☐ CR-105: New "Copy Forms" — seed this event's forms from another event (and it's job-scoped)
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: A new action copies another event's player and/or coach form directly onto **this** event. Unlike the main editor (CR-099), this really is scoped to the current job.
- **Why it matters**: This is the right answer to "make my new event's form look like last year's" — and the safe alternative to the cross-job fan-out.
- *Dev evidence: copy-forms-card.component.ts:7-14; ProfileMigrationController.cs:880-912.*

#### ☐ CR-106: The event's profile type and team constraint are set in the Profile Editor now
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: Choosing the event's profile type and its team constraint (by grad year / by age group / by age range / by club name / none) has moved out of the Job admin screen and into the Profile Editor, with an Apply/Reset pair and an unsaved-changes warning.
- **Why it matters**: Form design and profile choice now live in one tool. The discard-confirmation on switching profile is a behavior support will get asked about.
- *Dev evidence: profile-editor.component.ts:84-107, 375-439; ProfileMigrationController.cs:739-808.*

#### <span style="color:#c00000">☐ CR-107: Latent bug — a future profile named PP100 would collide with PP10</span>
- **Type**: Bug (latent — not firing yet) · **Audience**: SuperUser/Admin
- **What's new**: The system finds "which jobs use this profile" with a **starts-with** match, and new profiles created by cloning are named **without zero-padding**. Today's names (PP61, PP62…) are all two digits, so nothing collides. But the first clone past 99 creates **PP100** — and from that moment, editing **PP10** would also match and silently overwrite every PP100 job.
- **Why it matters**: Not firing today, but it's silent cross-job data loss with a known trigger. Cheap to fix now (match the exact profile name), painful to diagnose later.
- *Dev evidence: StartsWith match (ProfileMetadataRepository.cs:31); unpadded new names (ProfileMetadataMigrationService.cs:2036-2037).*

<div style="page-break-before: always;"></div>

## End-of-Month Reconciliation — differences from the old system

_From a comparison of the old system and the new one (2026-07-13), each checked against the new build. This is internal SuperUser billing/ops. The ADN month-end close is genuinely better than the old one; the invoice half of the cycle has real gaps._

#### <span style="color:#c00000">☐ CR-108: Clients can no longer get their monthly invoice — the whole delivery pipeline is gone</span>
- **Type**: Bug / regression · **Audience**: SuperUser/Admin
- **What's new**: The old system produced **one invoice per job**, stored it, gave the SuperUser a grid to review the month and flip each job's invoice **Active**, and then let that client's own admin log in and download their invoice — but only once it was marked Active. **None of that exists now.** There's no per-job invoice artifact, no publish/release step, and no way for a client to retrieve their invoice from the app. What exists instead is a single consolidated PDF the operator downloads for themselves.
- **Why it matters**: The customer-facing half of the billing cycle is missing. Clients have no way to get their monthly invoice, and there's no "release the month" control. Needs a decision on what replaces it.
- *Dev evidence: no invoice routes (app.routes.ts:409-455); reporting endpoints only produce/download a consolidated PDF (ReportingController.cs:544-568); the Jobinvoices / JobInvoiceNumbers tables are mapped but referenced nowhere in the app.*

#### <span style="color:#c00000">☐ CR-109: e-Check money is invisible to the invoice — wrong money</span>
- **Type**: Bug (money) · **Audience**: SuperUser/Admin
- **What's new**: e-Check payments are live for both player and team registration. But the invoice only recognises **"Credit Card Payment"** and **"Credit Card Credit"** as card money. An e-Check therefore gets **no processing fee charged**, is left out of "Credit Card Dollars Received," and so drops out of the **Balance Due Client** calculation entirely. (The separate revenue screen *does* see e-Check — so the two disagree.)
- **Why it matters**: Real money is wrong. Either the client is under-remitted or TSIC never bills the e-Check processing fee it configured. This should be fixed before the next month-end close.
- *Dev evidence: verified — only the two CC method names count as card (InvoiceReportPdfService.cs:222); non-CC gets zero fee (:228); Balance Due = (ccReceived + ccRefunded) − totalCharges (:201), so e-Check never lands in it.*

#### <span style="color:#c00000">☐ CR-110: Admin charges can't be added or edited any more — only deleted</span>
- **Type**: Bug · **Audience**: SuperUser/Admin
- **What's new**: The old system had a full add/edit/delete grid for job admin charges (one-off setup or support fees). In the new system the panel is **read-only with a delete button** — there's no add row and no edit. The "add admin charge" function exists in the code but **nothing calls it**.
- **Why it matters**: Admin charges are a real billing input that feeds the revenue screen. An operator who needs to bill a one-off fee can't enter one at all, and a mis-keyed charge can only be deleted, not corrected.
- *Dev evidence: verified — addAdminCharge is defined at job-config.service.ts:182 and called from nowhere (single grep hit, its own definition). Backend has POST/DELETE but no update (JobConfigController.cs:272, 286).*

#### <span style="color:#0033cc">☐ CR-111: The old invoice-producing action still calls Crystal Reports and is wired to nothing</span>
- **Type**: Question (needs a decision) · **Audience**: SuperUser/Admin
- **What's new**: The endpoint that used to generate the month's per-job invoices still exists and still calls the Crystal Reports service — but no screen calls it any more. When Crystal is retired, it will silently start failing.
- **Why it matters**: This is the last thread holding the old invoice pipeline (CR-108) together. Retiring Crystal retires the client invoice with it, and nothing in the new app replaces it. Needs a decision alongside CR-108.
- *Dev evidence: still calls the Crystal service (ReportingService.cs:860-867; ReportingController.cs:544-550); no frontend caller.*

#### <span style="color:#c00000">☐ CR-112: The invoice prints the customer name twice</span>
- **Type**: Bug · **Audience**: SuperUser/Admin
- **What's new**: Every venue heading on the invoice reads like "Acme:Acme:Fall 2026" — the customer name is emitted twice. The old invoice printed it once.
- **Why it matters**: Cosmetic, but it's at the top of the money document a client may be shown. Trivial to fix and obvious to anyone comparing against an old invoice.
- *Dev evidence: verified — Title = "{CustomerName}:{CustomerName}:{JobName}" (InvoiceReportPdfService.cs:188).*

#### ☐ CR-113: The month-end stats grid lost the inline QuickBooks-name edit
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old month-end grid let the operator fix a job's QuickBooks (QBP) name right there while reconciling. The new grid shows the customer and job as read-only — to fix a QBP name you have to leave the reconciliation screen and go into that job's General settings.
- **Why it matters**: Not wrong money, but a real workflow regression at exactly the wrong moment — mid-close, per job.
- *Dev evidence: the stats row carries only the six counts, no QBP name (LastMonthsJobStatsDtos.cs:3-25); QBP name now only in Configure → Job → General.*

#### <span style="color:#c00000">☐ CR-114: A SuperDirector can open Customer Job Revenue but the API refuses them</span>
- **Type**: Bug · **Audience**: SuperUser/Admin
- **What's new**: The screen's route lets a SuperDirector in, but the API behind it is SuperUser-only. So a SuperDirector navigates in successfully and gets a bare "Failed to load revenue data."
- **Why it matters**: Either the page should be SuperUser-only or the API should admit SuperDirectors — someone has to decide. Today it's a broken page instead of a clean "not allowed."
- *Dev evidence: route admits SuperDirector (app.routes.ts:344-347); API is SuperUserOnly (CustomerJobRevenueController.cs:13).*

#### ☐ CR-115: The ADN month-end close was rebuilt — and it's better
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old close pulled the month's transactions (with the gateway login hard-coded in the source) and dumped an Excel file. The new one is a guided 3-step wizard: download the transactions, see a **match verdict** (matched/unmatched counts and the latest settlement time), then download a **zip** containing the QuickBooks .iif files, their backing spreadsheets and a summary — with a parity check that warns if a transaction didn't survive consolidation. Credentials now come from the database, not source code. There's also an automated path that **withholds the .iif** if the autopay sweep wasn't trustworthy.
- **Why it matters**: A genuinely better close, but a different one to learn. Two caveats: every accounting screen is **hard-pinned to last month** (there's no month picker, so you can't re-run a prior month in-app), and a job with no stats row silently bills $0 registrant charges with no warning — same as the old system, but with the old review grid gone (CR-108) there's one fewer place to catch it.
- *Dev evidence: 3-step wizard + parity check (get-reconciliation-records.component.ts:186-330; AdnReconciliationService.cs:196-238); credentials from DB (:73); month hard-pinned (get-reconciliation-records.component.ts:39-48).*

<div style="page-break-before: always;"></div>

## Public Site / Landing / Branding — differences from the old system

_From a comparison of the old system and the new one (2026-07-13), each checked against the new build. Bulletins are covered separately (CR-072–075)._

#### <span style="color:#c00000">☐ CR-116: A LIVE event can be wrongly declared "concluded" — its page goes dark and registration stops</span>
- **Type**: Bug (critical) · **Audience**: Client support + SuperUser/Admin
- **What's new**: The system decides an event is "superseded" purely by checking whether a **later-year event of the same name exists and is live** (not suspended, registration on, not expired). **It never checks whether the current event is actually over.** When it decides an event is superseded, the entire public landing page collapses to *"This event has concluded — Register for [next year]"* — banner, bulletins, everything — for every visitor including logged-in families and admins. It also **blocks all non-admin registration** on that event.
- **Why it matters**: A director who opens **next season's early-bird registration while this season is still running** — a completely routine thing to do — instantly takes the current event dark and stops it accepting registrations. There's no admin override on the page and no "continue anyway." This is a live-event outage, and it's the most serious item in this review. The fix is to require the current event to actually be over before treating it as superseded.
- *Dev evidence: verified — the superseding-sibling query checks only the sibling's state, never the current job's (JobRepository.cs:700-717); the create-door is then closed on supersession alone (:732 — `door = !concluded && SupersededByLaterEvent is null`); the landing page collapses entirely (job-landing.component.html:20-34 — the code comment says "entire landing collapses").*

#### <span style="color:#c00000">☐ CR-117: The Widget Editor's "public" settings do nothing</span>
- **Type**: Bug · **Audience**: SuperUser/Admin
- **What's new**: The Widget Editor has a **Public** panel with on/off switches for the banner, bulletins, event-contact and job-pulse widgets. **Nothing reads them.** The public landing page hard-codes the banner and bulletins, so toggling a public widget off changes nothing. Two of the widgets (Event Contact, Job Pulse) are never rendered anywhere at all.
- **Why it matters**: A SuperUser will configure these switches and see no effect whatsoever. Event Contact in particular would be genuinely useful on a public page and simply isn't wired up.
- *Dev evidence: landing hard-codes the widgets (job-landing.component.html:42, 72); dashboard reads only the dashboard workspace (widget-dashboard.component.ts:77); the two widgets are referenced only in the registry, never rendered.*

#### ☐ CR-118: The admin dashboard shows charts only — no banner, no bulletins
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old system had no admin dashboard — a director landed on the same public page as everyone. The new admin dashboard renders **only chart tiles**. There is a fallback that would show the banner and bulletins, but it only fires when the dashboard is empty, and every admin role ships with chart widgets — so in practice it never runs. The dashboard even fetches the bulletins and then never displays them.
- **Why it matters**: Directors won't see their own event's bulletins on their dashboard, which surprises people who expect the dashboard to reflect the public page.
- *Dev evidence: dashboard-only filter (widget-dashboard.component.ts:74-93); bulletins fetched but unrendered (:145); dead fallback (widget-dashboard.component.html:18-27).*

#### ☐ CR-119: Banner images are now cropped to a fixed shape (the old one tiled them)
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old banner laid the image in at its natural size and **tiled** it to fill a fixed-height band. The new banner crops the image to fill a fixed 50:11 frame. The Branding tab tells you to upload at 1920 × 422 so nothing is cropped.
- **Why it matters**: An event whose old banner was a tiling texture or an odd shape will now render zoomed and cropped. Expect "my banner looks wrong / blurry" tickets from migrated events — the fix is a correctly-sized image.
- *Dev evidence: aspect-ratio 50/11 with background-size: cover (client-banner.component.scss:18-25); guidance at branding-tab.component.ts:43.*

#### ☐ CR-120: The banner now appears on phones and tablets (it used to be hidden)
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: The old site hid the banner **entirely** on anything smaller than a laptop. The new one always shows it, with a mobile layout that repositions the overlay image and scales the text down.
- **Why it matters**: Most youth-sports traffic is mobile. Clients who only ever tuned their banner for desktop are now seeing it on phones for the first time — expect "my banner text sits on top of my logo on my phone."
- *Dev evidence: always rendered with a 768px breakpoint (client-banner.component.scss:11-14, 111-114, 138-145). Legacy hid it below 992px.*

#### ☐ CR-121: Banner overlay text no longer accepts any HTML styling
- **Type**: Workflow-change · **Audience**: Client support + SuperUser/Admin
- **What's new**: The old overlay headline rendered whatever markup a director stored — inline colours, italics, spans. The new one strips **all** tags (only line breaks survive), on both the server and the client, and the Branding tab is a plain text box.
- **Why it matters**: Migrated overlay text that carried styling loses it silently. Safer (no injected markup on the banner), but a visible regression for any event that used it.
- *Dev evidence: sanitised twice (client-banner.component.ts:79-108; JobConfigService.cs:611-634); plain textarea (branding-tab.component.ts:68-83).*

#### ☐ CR-122: Events with no banner image now get a title card instead of nothing
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: The old page showed no header at all if there was no background image. The new one always presents something: background + overlay if both exist, an overlay stretched full-width if only that exists, or an elevated card showing the event name if there's no image at all.
- **Why it matters**: Every event now presents *something* at the top, which is generally a win — but it's a visible change for events that previously had a bare page.
- *Dev evidence: three-way fallback (client-banner.component.html:24-43).*

#### ☐ CR-123: Branding uploads are safer now — but the footer logo is gone
- **Type**: Workflow-change · **Audience**: SuperUser/Admin
- **What's new**: The old upload accepted any file, any size, and saved it as-is. The new one accepts only jpg/png/webp, caps at 5 MB, and resizes server-side, with clear sizing guidance. **Gone:** the **Logo Footer** — the field still exists in the data and is still copied on clone, but there's no upload for it and nothing renders it.
- **Why it matters**: Upload hygiene is a clear win, but any client who relied on a footer logo has lost it with no replacement.
- *Dev evidence: allowed types/size/resize (branding-tab.component.ts:38-99; JobImageService.cs); LogoFooter has no UI and no renderer.*

#### ☐ CR-124: The landing page now builds its own registration buttons
- **Type**: Workflow-change · **Audience**: Client support
- **What's new**: The old landing page had **no registration buttons at all** — it was banner + bulletins, and directors hand-typed registration links into their bulletin text. The new landing page assembles a panel from the event's live state: registration links for each open role, a Manage column (Pay Balance Due, Change Team or Uniform #, My Registration, add insurance you skipped at checkout), and a Teams column (My Teams, Register a Team, Public Rosters). It hides itself when empty and retitles to "Wrap-Up" once the event ends.
- **Why it matters**: Directors no longer need to hand-build registration links. Some entries are deliberately gated by event type — Public Rosters and "Change Team" are tournament-only, and Register-a-Team never shows for leagues — so "why doesn't my league show a Rosters link?" is a deliberate gate, not a bug.
- *Dev evidence: registration-panel.component.ts:98-105, 146-161, 209-213, 291-303.*

#### <span style="color:#c00000">☐ CR-125: The Theme editor only saves to the current browser — nobody else sees the change</span>
- **Type**: Bug · **Audience**: SuperUser/Admin
- **What's new**: The Theme editor (Configure → Theme) looks like it sets an event's colours. Its Save button is literally labelled **"Save (LocalStorage)"** — the colours are stored **in that one browser only**. Nothing is saved to the server, so no visitor, no family, and no other admin ever sees the change. On top of that, three of its five theme targets emit styling that is never applied to anything.
- **Why it matters**: Per-event colours look configurable but aren't. Anyone who "brands" an event this way will believe they've changed it and be the only person who can see it. (Separately: `/brand-preview` — an internal design showcase — is publicly reachable on every event's URL with no login required. Harmless content, but it shouldn't be on a client's public site.)
- *Dev evidence: Save (LocalStorage) button + localStorage-only persistence (theme-editor.component.ts:78, 274-288; theme-overrides.service.ts:42-68); brand-preview route has no auth guard (app.routes.ts:118-121).*

<div style="page-break-before: always;"></div>

## Comparison — looked at but not filed
- **Change Club / Transfer All Teams, and the "autopay failed" reminder queue on Team Search** — real and useful, but already captured in CR-048 (the Team Search console's club moves and autopay resend). Not re-filed.
- **Processing-fee ceiling — unresolved.** The new system clamps the card processing rate to 3.5–4.0% and e-check to 1.5–2.0%. The comparison suggested the old system had only a 3.5% floor with no ceiling (so a job set above 4% would now charge less) — but the new code's own comment says it "mirrors the legacy clamps (CC 3.5–4.0%)," which contradicts that. The new clamp is certain; whether it's a *change* from the old system is not. Left unfiled until the old ceiling is confirmed either way. (New: ProcessingRateMath.cs:13-24, FeeConstants.cs.)
- **"House Team" per-player fee is unchanged.** Both the old and new systems charge a self-rostering tournament player only when their assigned team has a per-registrant fee (otherwise $0) — same behavior on both sides, so it's not a difference. (This is the separate House-Team topic from the accounting review.)
- **Automatic recurring billing (autopay/ARB) exists in both systems.** The new app lists one subscription per player and records no money up front, but "no money until the first draft" is true in both — not a support-visible change.
- **Player "pay a fixed amount / percent of owed" option** — like the sibling discount (CR-013), this is still a saved setting but isn't used in the new charge path (only pay-in-full, deposit, and autopay are). Low confidence it was ever used much; flag if a director relied on it.
- **Live late-fee recalculation** in the payment preview (so the amount shown matches what will actually be charged) is real but only visible in the rare case where a late-fee window opens after a deposit was already paid. Revisit if it ever causes confusion. (TeamRegistrationService.cs:420-453.)
- **Batch email is a background job (progress bar, opt-in "email me the summary")** — already noted in CR-049. New detail only: if the server restarts mid-send, the composer shows "Lost track of the batch." Minor.
- **The eCheck "settlement pending" banner also appears on TEAM confirmations**, not just player (CR-011). Edge case — most team payments aren't eCheck — so not filed separately.
- **Self-service / admin confirmation resend** — a parent can re-trigger their own confirmation (goes to player + mom + dad, no CC/BCC); an admin can force-resend a team confirmation. Whether this is genuinely new vs the old system wasn't confirmed on the Legacy side — left unfiled.
- **Email Log admin audit surface and a Push-notification tab** exist under Communications, but a Legacy equivalent wasn't ruled out (push almost certainly existed for the mobile app), so neither is filed as a change.
- **Role selection may no longer list the STPAdmin role** — flagged during the Login/Nav comparison (medium confidence). Confirm STPAdmin is still active in prod before treating it as a real change.
- **Bulletins can now be open-ended** — a blank end date shows the bulletin indefinitely; the old system required an end date. Minor.
- **Admins get inline edit / quick-hide controls on each bulletin** right on the live public page. Minor admin convenience, folds under CR-079.
- **Public roster CONTENTS are unchanged** (so not filed as a difference). A player still only appears on the public roster once they've signed the waiver; staff always appear; waitlist/dropped age groups and inactive teams are excluded; and no contact details are shown. The standing support answer — "a player missing from the public roster hasn't signed the waiver" — is still correct. (What *did* change is scope + the new off-switch: see CR-096.)
