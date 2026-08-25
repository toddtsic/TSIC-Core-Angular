# ARB Failed-Draft Notification + Twice-Monthly Expiring-Card Send

Status: **DESIGN SETTLED, NOT BUILT.** Nothing modified.
Origin: AR-037 (Ann via Chelsea, 08-25) plus Todd's 08-25 session on the 4am sweep.
Every number below was verified against source and the production DB on 2026-08-25.

---

## 1. The finding that reframes AR-037

AR-037 alleges: *"a declined draft can lead to an ADN General Error which isn't swept/imported, so records silently diverge."*

**That premise is false. The sweep imports failed ARB drafts and always has.**

| tx status | rows | first | last |
|---|---|---|---|
| settledSuccessfully | 70,388 | 2020-08-27 | 2026-08-24 |
| declined | 3,832 | 2020-07-25 | 2026-08-24 |
| generalError | 119 | 2020-08-26 | 2026-05-01 |

Authorize.Net attributes declines and errors to settlement batches (`batchStatisticType` carries `declineCount` and `errorCount`), so `GetTransactionList_ByBatchId` returns them. `IsArbCandidate` accepts all three statuses. Post-golive the new stack has imported 936 settled + 50 declined, running 1-10 declines every morning.

Import is correct: $0 audit row, totals untouched, subscription status synced. **No accounting defect.**

### The real defect: nothing acts on the import

- **Digest alert list** filters `SubscriptionStatus != active`. ADN retries a declined card, so status stays `active`. **67 of 81 failing registrations are invisible to it by construction.**
- **Digest ARB table** shows the decline as one row of ~70, marked only by a Status column reading "declined" with $0.00 beside it.
- **The three edge-case sections at the bottom** (eCheck Watchdog, Untracked eCheck, Orphan Charges) are eCheck-scoped or settled-only and structurally cannot contain a failed ARB draft. On 08-24, a morning with 10 declines, all three printed green all-clears.
- **Payment ledger** renders it as a $0.00/$0.00 row identifiable only by comment text.
- **Behind-in-Payment** computes the arrears correctly but is a per-job manual click on a page that loads nothing on open.
- **Nobody is notified.** Legacy carried `/* consider adding SendDeclinedEmails here */` (2024), never built.

### Exposure

| Measure | Value |
|---|---|
| Registrations with a failed draft, 60 days | 81 |
| Owed across them | $269,727 |
| Still showing sub status `active` (invisible to the digest alert) | 67 of 81 |
| Dead plans with prior declines still owing, 12 months | 30 / **$53,856** |

The $53,856 is unrecoverable by auto-draft: those subscriptions are expired/canceled/terminated, so no future installment exists to collect it.

### The case in one registration

Maeve Corr, STEPS Lacrosse: Girls Elite Players 2025-2026. $5,150 over 10 installments of $515.

```
2025-07-26   $515.00   settledSuccessfully
2025-08-26     $0.00   declined
2025-09-26     $0.00   declined
2025-10-26   $515.00   settledSuccessfully
2025-11-26     $0.00   declined
2025-12-27   $515.00   settledSuccessfully
2026-01-26     $0.00   declined
2026-02-26     $0.00   declined
2026-03-26     $0.00   declined
2026-04-26     $0.00   declined
```

Paid 3 of 10. Seven declines across nine months, every one of them in a sweep digest. Subscription now `expired`, registration still `BActive = 1`, owes **$3,605** the system can no longer collect.

### Declines are recoverable, and per-installment

405 of 498 subscriptions declined exactly once; repeat offenders decline monthly, tracking the schedule — **not ADN retry storms**. Maeve recovered twice mid-plan with no intervention. This is why same-day contact is high leverage: it converts a recoverable failure into a fix instead of letting it compound. Volume is ~1-2 sends/day estate-wide — no throttling or dedupe machinery needed.

### Corroboration: directors already do this by hand

`Jobs.emailLogs` holds **300+ manually composed payment-decline emails**, 2022-03 to 2026-07, across ~12 jobs and ~24 subject variants ("Payment Decline - Please Reconcile ASAP" x88, "Payment Declined - Please Reconcile" x24, "Declined Payment" x24, ...). Same message retyped every time. The demand is proven; what is missing is the trigger and the consistency.

---

## 2. Workstream A — failed-draft notification in the 4am sweep

**Scope: the 4am sweep only.** No other feature's behavior changes. No schema changes.

**Capture is NOT in scope — it already works. Do not rebuild it.**

### Decisions (Todd, 08-25)

| # | Decision |
|---|---|
| 1 | Recipients: all addresses connected with the account |
| 2 | Sender: default director |
| 3 | Trigger: every decline, no threshold |
| 4/5 | Treat independently of the smart bulletin — assume nothing about it |

### Recipient resolution — use the existing canonical rule

`BatchEmailRecipientFilter.ResolveRecipients` (`TSIC.API/Services/Shared/Email/`):

- **Player role** → mom + dad + the player's own email (own is optional; child accounts often lack one)
- **Every other role** → the registrant's own `User.Email`
- All filtered through `EmailAddressRules.IsSendable` (drops blanks, the `not@given.com` sentinel, invalid addresses) and de-duped case-insensitively.

Matters here because ARB subscriptions sit on **both** player registrations and adult/club-rep registrations. The rule already branches correctly.

> `ArbDefensiveService` calls the simpler `BuildSendableSet(Mom, Dad, Registrant)` directly. The sweep should use `ResolveRecipients` so the role branch is honored.

### Sender identity — SES forces the From address

Enforced in `EmailService.BuildMimeMessage`, not merely convention:

| Field | Settable | Value |
|---|---|---|
| From **address** | **NO — forced** | `support@teamsportsinfo.com` (SES-verified identity) |
| `FromName` | yes | the club: `Jobs.DisplayName ?? JobName` |
| `ReplyToName` / `ReplyToAddress` | yes | the default director |

```
From:      "STEPS Lacrosse" <support@teamsportsinfo.com>
Reply-To:  "Erin Kay" <erinkay@stepslacrosse.com>
To:        mom + dad + player
```

Deliberate departure from `ArbDefensiveService`, which sets FromName and ReplyToName to the same person (the clicking admin). Here **FromName is the CLUB and ReplyToName is the PERSON**, because the sweep has no clicking user and the family should see the club in their inbox.

**Reply-To is load-bearing.** With none resolved, `EmailService` falls back to `support@teamsportsinfo.com` — so every family reply about a failed payment lands in TSIC's inbox. That is the CLIENTS-OWN-THEIR-MONEY failure mode arriving automatically and at volume.

### Default-director rule (Todd's ruling, verified)

`Jobs.PrimaryContactRegistrationId` if set, else **the earliest-registered active Director**.

**Ordering key MUST be `RegistrationAI`, not a date.** There is no `Createdate` on `Jobs.Registrations` — only `modified`, which is mutable and gives the wrong answer. `RegistrationAI` is `int IDENTITY`: monotonic, unique, never null, so **no ties are possible**. This matters because `JobCloneService` creates admin registrations in one batch and any timestamp ordering would be ambiguous on cloned jobs.

| Population | Resolves with valid email |
|---|---|
| Jobs with failed drafts (12mo) | 19 of 19 |
| All live jobs carrying ARB plans | 21 of 21 |

Every resolution lands on a real club person on a club domain or club-branded address (`erinkay@stepslacrosse.com`, `crainsonrose@gmail.com`, `yjbonnie12@gmail.com`, ...). **No TSIC accounts.**

Caveat worth surfacing in the digest: estate-wide, where a human *did* designate a primary contact, the fallback picks a different person **17 times out of 20**. Deterministic and safe, but not a good predictor of who a club would choose. Have the digest name which jobs ran on the fallback — it turns a guess into a monthly prompt for clubs to set the real value, and costs nothing. (It agrees on the one failure job that has a designation: All American Aim → Katharine Lee either way.)

### THE SAFETY RULE (Todd, 08-25)

> **New code runs AFTER all existing proven code has finished and been scored. Never put proven code at risk.**

**Why this is not optional — the trap it avoids:**

The sweep keeps a running error count. On the 1st of the month the close asks it one question: *did you have any errors?* If yes, it refuses to build the QuickBooks files at all.

```csharp
public bool IsTrustworthy => Succeeded && Errored == 0;
```

`counts.Errored` is incremented by the per-transaction catch blocks inside the sweep's processing loops. Today it can only mean "a payment failed to record" — a genuine reason to stop, because the books would be short money.

**If notification sends run inside those loops, a bounced email increments the same counter. A family with a dead mailbox becomes indistinguishable from a broken payment record, and the month-end close silently refuses to produce the IIF files.**

The sweep already establishes the correct pattern for its own digest send: it happens at the very end, outside the main try, with a catch that deliberately cannot change the verdict (*"Never let a mail failure mask the sweep's own outcome"*).

Required shape:

1. All existing steps (1-7) run and finish.
2. The sweep's verdict is locked.
3. **Then** the failed-card notifications go out, counted separately (`NotifyErrored` or similar), unable to change that verdict or reach a booking catch block.
4. The digest reports both.

### Digest reporting (Todd's design)

A new section carrying a **paired count: # failed CCs / # of those emailed.**

**The delta is the finding.** 10 failed / 10 emailed is a clean morning. 10 failed / 7 emailed means three families have a broken payment plan and don't know it — and those three are the only rows that need names.

Shortfall rows must carry a reason, because the causes mean different things:

- **No email on file** — a data defect, someone must fix the record.
- **Opted out** — the client's choice; legitimate, but the director should know they are unreachable.
- **Send failed** — infrastructure; retry or escalate.
- **No primary contact set** — the job ran on the earliest-director fallback.

Follow the existing bottom-section convention: an explicit line when zero were sent, so "nothing went out" is distinguishable from "the section didn't run."

### Counts-line defect (independent of any email work)

The digest header currently has **no field in which a failed draft can appear**:

- `counts.ArbImported` increments identically for a settled draft and a failed one, so the header reads "ARB imported: 70" and hides ten failures inside it.
- `counts.Errored` counts thrown exceptions only, so it correctly reads 0 on a morning with ten declines — and misleadingly.

Failures need their own count there regardless of whether a single email is ever sent.

### Email log

Sends must land in `Jobs.emailLogs` per job — Ann's stated requirement #5 on AR-037, and the same requirement she set on AR-021. `Jobs.emailLogs` is written by `EmailBatchService.cs:394`, **not** by plain `IEmailService`.

### Footprint

One file changes behavior. Three additive touches outside it, each checked for blast radius:

1. **`RegistrationRepository.GetByAdnSubscriptionIdAsync`** — loads `Job` + `User` only today, so mom/dad emails are unavailable. Needs `FamilyUser`.
   **VERIFIED CONTAINED:** its only production caller is the sweep itself (`AdnSweepService.cs:409`); every other hit in the codebase is a test mock. Zero reach.
2. **A default-director resolver** — net new. Nothing implements "primary contact else earliest active Director by `RegistrationAI`"; `GetDirectorsForJobsAsync` returns all directors, unordered. New repo method plus its interface line.
3. **`IEmailBatchService` injection** — *conditional, and the one real fork.* The sweep injects `IEmailService` only. The batch engine is what writes `Jobs.emailLogs` and also brings opt-out handling and the unsubscribe footer. But it is a background fan-out engine, so sends complete **after** the sweep returns — which affects how the digest reports the paired count. Decide at build.

Plus `AdnSweepServiceTests.cs` — existing mocks need the new dependencies.

**No schema changes.**

---

## 3. Workstream B — twice-monthly expiring-card send (2nd and 15th)

This is AR-037's stretch option (*"the system automatically sends this email out twice a month!!!"*), and the punchlist's own analysis already concluded the scheduled shape is the better one.

### The cost objection that shaped AR-037 is wrong by two orders of magnitude

AR-037 assumed one live forced-production ADN call **per job** across a few hundred jobs.

| | |
|---|---|
| Live jobs carrying ARB plans | 21 |
| Customers behind them | 11 |
| **Distinct ADN merchant accounts** | **1** (`teamspt52`) |

ADN credentials resolve per **Customer** (`Jobs.Customers.adnLoginID`), and `ARBGetSubscriptionList` returns the whole account's list regardless of who asks. Estate-wide there are only two accounts (`teamspt52` 14,480 subs, `3MGs4r72` 490).

**One API call covers every live ARB job.**

Corollary: the current per-job screen already pulls the entire estate and discards all but one job's worth, every time it is clicked. **A scheduled estate-wide sweep is cheaper than what happens today.**

### Timing is correct, not a guess

A card expiring this month still works all month; the draft that fails is *next* month's. The 2nd gives ~29 days of runway, the 15th ~15.

The 15th self-corrects: a family who updates their card gets a later expiry at ADN and drops off `cardExpiringThisMonth` automatically, so the second send only reaches those who have not acted.

### Why the 2nd and not the 1st (Todd, 08-25)

**The 1st is already fully occupied.** `AdnSweepBackgroundService` *diverts entirely* on day 1: it runs the sweep with its digest **suppressed**, folds it into the month-end close email, and attaches the IIF zip. There is no normal digest that morning.

Three consequences of putting expiring-card sends on the 1st, all avoided by moving to the 2nd:

1. It stacks a third job onto the one morning already carrying the heaviest, most safety-critical work — the month-end close, gated on sweep trustworthiness and producing the QuickBooks files.
2. The expiring-cards report would land inside the **close email**, a different email with a different structure, while the 15th's landed in a normal digest. Two sends, two shapes.
3. It competes for attention in the one email support reads most carefully.

`cardExpiringThisMonth` returns the identical set on the 1st or the 2nd, so the move costs nothing. **On the 2nd and the 15th both sends report identically, in a standard digest, on a quiet morning, and the month-end close keeps the 1st to itself.**

### Digest reporting

Same paired-count convention as the failed-CC section: **# expiring cards found / # emailed**, rows only for the shortfall, each carrying its reason (no email on file, opted out, send failed). Identical shape so the two sections read the same way.

Runs on the 2nd and the 15th only; on every other morning the section is absent rather than reporting zeros.

### Decisions (Todd, 08-25)

| # | Decision |
|---|---|
| 1 | Email directors as well — as a **separate per-job summary**, not CC'd on the family's notice |
| 2 | Scope is defined by the **subscription**, not the job window. A live ARB plan qualifies, full stop. |
| 3 | **Same service** — `AdnSweepBackgroundService`, which already branches on day-of-month |
| 4 | **2nd and 15th**, not the 1st — the 1st belongs to the month-end close (see below) |

On (1): CC'ing directors would put up to 9 addresses in a family's To line and expose one family's card problem to 9 people. The separate summary is the existing `notifyDirectors` pattern in `ArbDefensiveService` — same information, no cross-exposure, reuses shipped code.

### PREREQUISITE — a silent-failure defect that must be fixed first

`AdnApiService.ARBGetSubscriptionListRequest` (~line 452) ends in `catch { return null; }`, and null is indistinguishable from "no results."

Clicked by a human that is a shrug. **On a timer, a dead ADN call renders as "no expiring cards" — green all-clear, no mail sent, nobody told.** Silent, and failing in the direction that hides the problem. **Fix before anything runs unattended.**

### Structural gap — the one-month cliff

Authorize.Net offers exactly four search types (verified by reflecting `AuthorizeNet.dll` 2.0.4): `cardExpiringThisMonth`, `subscriptionActive`, `subscriptionExpiringThisMonth`, `subscriptionInactive`.

**There is no "already expired" search.** A card that expires in March and is never fixed drops off the list on April 1st and never reappears.

So the 2nd/15th mail is the **only** prevention, with exactly one month of reach. After that the family is invisible until a draft fails — which is exactly the population Workstream A notifies.

> **The two workstreams interlock. Neither covers the gap alone. Do not treat either as a substitute for the other.**

### Latent defect our change would activate

`ArbDefensiveService.StartDefensiveEmailsAsync` builds one `notifiedNames` list and hands it to every director. That is **correct today** because the whole flow is scoped to a single job.

**It breaks the moment we sweep estate-wide** — with one merchant account returning all 21 jobs at once, every director would receive every club's families. The list must be partitioned by job before it goes out. Requires `JobId` on `ArbFlaggedRegistrantDto` (the projection has it, `MapToDto` drops it), which means running `.\scripts\2-Regenerate-API-Models.ps1`.

Not a defect in what is shipped — a latent one our change activates. **Must land and be verified before the first scheduled send.**

### Minor

Paging exists on the ADN request (`Paging paging`) and our implementation never sets it. At ~300 expiring per month against 14,480 subscriptions it is under the limit today, but nothing enforces that as the estate grows. Harmless for `cardExpiringThisMonth`; would silently truncate if anyone ever switches the search type to list all subscriptions.

---

## 4. Disposition of AR-037

**Part A** — the SuperUser cross-job expiring-card send. Untouched by these findings; still open for Todd's decision. Note the cost objection is now disproven (one API call), and Workstream B delivers the same outcome by the route the CLIENTS-OWN-THEIR-MONEY ruling requires: automated, per job, sent under the club's identity, requiring no TSIC staffer.

**Part B** — "an ADN General Error the sweep does not import."

| Claim | Verdict |
|---|---|
| "This is infrequent" | **Correct.** 119 in six years; zero since go-live. |
| "which isn't swept/imported" | **False.** Both `generalError` and `declined` import, since 2020. |

The punchlist's instruction to determine *"why the sweep misses it"* has no answer, because it does not miss it. Records do not diverge; the ledger is accurate.

**Recommended disposition: Part B closes as MISDIAGNOSED, reopened as the reporting-and-notification defect.** Do *not* close it as "no defect" — Chelsea diagnosed the mechanism wrong and the consequence right, and the consequence is $53,856. Closing on the technicality would discard a genuine finding and guarantee a re-file.

Detail for the answer back to Ann: `generalError` has not fired since 2026-05-01, which is pre-golive. Anything Chelsea saw was on legacy. Post-golive the estate has produced 50 `declined` and zero `generalError`. Identical mechanism — a matter of which flavor occurred, not of coverage.

---

## 5. Rulings carried in, not re-litigated

- **CLIENTS OWN THEIR MONEY** — a TSIC staffer doing a client's recurring money chore by hand is the defect, not the case for tooling it. Both workstreams send under the club's identity with no TSIC person in the loop.
- **Director alerts were deliberately removed from this sweep** ("recipient design needed; jobs can have >1 director"). That ruling arose from the eCheck NSF case, where reversal machinery and inactivation were entangled. The ARB decline case is cleaner — nothing is booked, nothing is reversed, no inactivation decision. The default-director rule answers the recipient question that parked it.
- **ADN environment is host-bound** — never reintroduce a prod-forcing flag on charging paths. The expiring-card lookup's forced-production is a pre-existing, sanctioned READ-ONLY exception.
- **PL-055** made the expiring-cards lookup click-only because it is a live production query. A scheduled sweep does not undo that reasoning — it removes the operator from the wait entirely.

## 6. Related open item

**AR-036** (already filed, open): Job Clone overwrites `Jobs.DisplayName` with the new job name. That field is the From display name on outbound mail — and since SES forces the From *address*, `FromName` is the **only** thing identifying the club in a family's inbox. AR-036 degrades the single visible identity lever on every mail a cloned job sends, including both workstreams here. Not a prerequisite. Worth knowing they touch the same field.
