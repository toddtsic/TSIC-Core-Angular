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
| Admin Search Tools | 8 |
| **Total** | **55** |

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

_From a comparison of the old system and the new one (2026-07-12), each checked against the new build. Covers the admin Search Registrations and Search Teams screens; deeper payment detail lives in the Accounting section._

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

<div style="page-break-before: always;"></div>

## Comparison — looked at but not filed
- **Change Club / Transfer All Teams, and the "autopay failed" reminder queue on Team Search** — real and useful, but already captured in CR-048 (the Team Search console's club moves and autopay resend). Not re-filed.
- **Processing-fee ceiling — unresolved.** The new system clamps the card processing rate to 3.5–4.0% and e-check to 1.5–2.0%. The comparison suggested the old system had only a 3.5% floor with no ceiling (so a job set above 4% would now charge less) — but the new code's own comment says it "mirrors the legacy clamps (CC 3.5–4.0%)," which contradicts that. The new clamp is certain; whether it's a *change* from the old system is not. Left unfiled until the old ceiling is confirmed either way. (New: ProcessingRateMath.cs:13-24, FeeConstants.cs.)
- **"House Team" per-player fee is unchanged.** Both the old and new systems charge a self-rostering tournament player only when their assigned team has a per-registrant fee (otherwise $0) — same behavior on both sides, so it's not a difference. (This is the separate House-Team topic from the accounting review.)
- **Automatic recurring billing (autopay/ARB) exists in both systems.** The new app lists one subscription per player and records no money up front, but "no money until the first draft" is true in both — not a support-visible change.
- **Player "pay a fixed amount / percent of owed" option** — like the sibling discount (CR-013), this is still a saved setting but isn't used in the new charge path (only pay-in-full, deposit, and autopay are). Low confidence it was ever used much; flag if a director relied on it.
- **Live late-fee recalculation** in the payment preview (so the amount shown matches what will actually be charged) is real but only visible in the rare case where a late-fee window opens after a deposit was already paid. Revisit if it ever causes confusion. (TeamRegistrationService.cs:420-453.)
