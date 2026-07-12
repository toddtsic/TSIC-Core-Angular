# Change Password — Domain Contract

**Status:** Authoritative. This is the acceptance checklist for `tools/change-password`.
**Audience:** anyone (human or agent) about to touch this tool.
**Read this before you read the code.** Every fact below is verified against source or measured
against `TSICV5`; the measurements are dated and reproducible.

---

## 0. Why this document exists

The tool was migrated from legacy inside a six-feature batch commit (`e706dc67`) and then corrected
by hand eight times — each commit restoring or removing one behavior the migration had misunderstood
(`c492b303` editable player rows · `6dd00c3a` legacy's field labels · `5a121a2c` clearing an email ·
`299f08e8` the player password field that should never have existed).

Every one of those was the same failure: **the domain was never written down**, so each change
re-derived it from scratch and got a different answer. This document is the fix. If you are about to
change this tool and something here is wrong, correct *this file first*, then the code.

---

## 1. The identity model

### One credential table

`dbo.AspNetUsers` is the **only** table in the system with a username or a password.
One password column: `PasswordHash`. **There is no salt column** — the salt is embedded in the hash
(stock ASP.NET Core Identity `PasswordHasher`, format-marker byte: `0x00` = Identity 2.x/PBKDF2-SHA1,
`0x01` = Core/PBKDF2-SHA256). Legacy hashes therefore verify **transparently, with no app code**, and
are silently upgraded on next successful login. There is no custom hasher and no legacy-compat branch.
Do not add one.

`dbo.Families` holds **no credentials**. It is keyed on `Family_UserId` (an `AspNetUsers.Id`) and
carries only the parent contact block: `Mom_FirstName`, `Mom_LastName`, `Mom_Cellphone`, `Mom_Email`,
`Dad_*`. Nothing else.

### Two FKs, and the whole tool turns on the difference

`Jobs.Registrations` points into `AspNetUsers` **twice**:

| Column | Meaning |
|---|---|
| `UserId` | **who the registration is about** |
| `Family_UserId` | **the login that owns it** |

### Players never sign in. The family does.

This is not a style preference — it is what the login code does.
`RegistrationRepository.GetPlayerRegistrationsAsync(userId)`, the method that builds the role-selection
screen after a parent authenticates, matches:

```csharp
where (r.Family_UserId == userId) && (r.BActive == true) && role.Id == RoleConstants.Player
...
DisplayText = $"...{u.FirstName} {u.LastName}..."   // u comes from r.UserId — the CHILD's name
```

The authenticated principal is matched against **`Family_UserId`**. The child's own `AspNetUsers` row
(`r.UserId`) is used **only to render their name**. It is never authenticated against.

Consequences you must hold on to:

- A player's `UserName` is frequently a **raw GUID** (e.g. `76da3519-7842-400e-84ed-4ea6005e974c`).
- A player's `PasswordHash` is **unreachable**. Resetting it changes a credential nobody can use.
- **Never put a password field on a player.** (`299f08e8` removed one. Do not re-add it.)

### Adults sign in as themselves

For `ClubRep`, `Director`, `SuperDirector`, `UnassignedAdult`, `Staff`, the person's own
`Registrations.UserId` **is** their login. There is no family in play.

**`Staff` is the coach role.** There is no `Coach` GUID in `RoleConstants`. A coach is `Staff`
(assigned to teams) or `UnassignedAdult` (not yet assigned) — see
`AdultRegistrationService`: *"Staff (tournament coach): ONE Registration PER SELECTED TEAM"* and
*"Coach roles only (UnassignedAdult / Staff)"*. Both are already searchable. **Coaches are covered.**

### `FamilyUserName` is not a column

It is a projection: `Registrations.Family_UserId → AspNetUsers.UserName`. Do not go looking for it in
the schema. Likewise `F-Email` is that same row's `Email`.

### A merge is not a rename

Selecting a different username **does not rename the account.** It **re-points registrations onto a
different, already-existing `AspNetUsers` row** and abandons the old one. It is a duplicate-account
consolidation, and it is **irreversible**.

- **Player merge** re-points `Registrations.UserId`.
- **Family merge** re-points `Registrations.Family_UserId`.
- They are independent. Consolidating a child's player accounts does **not** consolidate the parent's
  family logins, and vice versa.

The merge deliberately casts a **wide net** — it moves every registration matching the identity key,
not just the source account's. **This is correct and must not be "fixed".** A child accumulates one
account per season; the wide net consolidates all of them in a single action instead of forcing the
admin to repeat it six times. What the wide net requires is that the **blast radius be shown before
the admin confirms**, not that it be narrowed.

### An empty email and "no email" are different facts

Three distinct states, and the tool must express all three:

| State | Stored | Means |
|---|---|---|
| Has an address | `jane@example.com` | Mail them. |
| **No address exists** | **`not@given.com`** | We asked. There isn't one. *A recorded fact.* |
| Nothing on file | `NULL` | We never captured one. *Absence of a fact.* |

The marker is the house convention (247 rows; legacy's `EmailOptOutController` writes it on opt-out)
and is canonical in `EmailAddressRules.NotGiven`. It is a *well-formed* address, so format validation
cannot reject it — it is excluded by name.

**It is a flag, not an address. Never render it as one.** Legacy stripped it on the way to the screen
(`MomEmail.Replace("not@given.com", "")`) and so does this tool: the email box shows empty and a
**"No email" toggle** carries the state. The marker is written by the button, never typed — a marker
an admin types is a marker an admin typos, and a typo'd marker is just a live address that bounces.

---

## 2. What the data actually looks like

*Measured against `TSICV5`, 2026-07-12. Re-run before trusting these.*

**Families re-register from scratch every season.** Each time, the system mints a **new family login
and a new player `AspNetUsers` row**. That duplication is not an edge case — it is the steady state,
and cleaning it up is the tool's main job.

| | |
|---|---|
| Registrations (all) | 661,074 |
| Distinct player accounts | 130,831 |
| …with a NULL `dob` | **0** |
| …that have a name+DOB twin (would show a merge dropdown) | **46,821 (36%)** |
| Distinct family logins | 115,038 |
| …that have a **true duplicate** (another login owning the same child) | **60,780 (53%)** |
| …that the **shipped** family-merge predicate surfaces a candidate for | **28,755 (47%)** |

**Player `dob` is never null.** So the player-merge key reduces to *first name + last name + DOB +
role*, which is strong. Every collision sampled is a genuine seasonal duplicate — the same child, the
same parents, re-typed:

```
Maya Abell      2008-03-28  | "Melissa sHIMIZU / Michael Abell" vs "Mike Abell / melissa ashimizu"
Kayla Abraham   2013-03-09  | "Su Kang / Jesse Abraham"         vs "Jesse Abraham / Su Kang"
Andrew Adams    2006-02-28  | "Kimberly Hunt / Brian Adams"     vs "Brian Adams / Kim Hunt"
```

Mom and Dad **swapped slots**, typo'd, nicknamed. Which is exactly why the family-merge predicate —
an exact match on all six parent fields plus postal code — **is blind to 32,025 family logins that
have a real duplicate.** The family login is the *only* login a parent ever uses. This is the single
biggest defect in the tool.

---

## 3. Legacy capability matrix

Legacy = `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Search/ChangePasswordController.cs`
+ `Views/ChangePassword/Index.cshtml`. Superuser-only, cross-tenant, no job scoping.

| # | Legacy capability | Verdict | Why |
|---|---|---|---|
| 1 | Cross-job/customer search, **role-first** (6 roles: Player, Club Rep, Director, SuperDirector, Unassigned Adult, Staff), no tenant scoping | **PORT** | Correct. SuperUser support tool; scoping it to a job would defeat it. |
| 2 | Player search fans out: `LastName` also matches Mom/Dad last, `FirstName` → Mom/Dad first, `Email` → Mom/Dad email, `Phone` → Mom/Dad phone | **PORT** | You search for the kid by the parent who called you. Preserved. |
| 3 | Adult search — no family joins, own columns only | **PORT** | |
| 4 | Reset the **adult's own** password | **PORT** | Their `UserId` *is* their login. |
| 5 | Reset the **family** password | **PORT** | The login a parent actually uses. |
| 6 | Reset the **player's own** password (`NewUserPassword`) | **DROP** | A player has no usable login. Resets a credential nobody can sign in with; half are raw GUIDs. Removed in `299f08e8`. **Do not restore.** |
| 7 | Edit `AspNetUsers.Email` (adult / player / family) | **PORT** | |
| 8 | Edit `Families.Mom_Email` / `Dad_Email` | **PORT** | |
| 9 | **Clear** an email (blank = delete the address) | **PORT-WITH-FIX** | Legacy could clear Mom/Dad but **NRE'd** on clearing the user/family email (`email.ToUpper()` on null). Restored in `5a121a2c`; must write `NULL`, not `""`. |
| 10 | Merge player username (re-point `UserId`) | **PORT** | Key: first+last+DOB+role. Sound — DOB is always present. |
| 11 | Merge family username (re-point `Family_UserId`) | **PORT-WITH-FIX** | Legacy's key (6 exact parent fields + postal) finds **47%** of real duplicates. Re-key on **the child** (name+DOB), the same signal the player merge already uses. |
| 12 | Username cells are **dropdowns of existing duplicates**, never free text | **PORT** | You cannot invent a username. You can only merge into one that exists. |
| 13 | Edit player / Mom / Dad **phone** | **DROP** | Legacy's grid marked them editable, but `ChangeUserPassword` never bound them — **posted and silently discarded. It never worked.** Documented in `c492b303`. |
| 14 | One monolithic POST doing all of the above | **DROP** | Split into discrete endpoints. See §5. |
| 15 | No result cap | **DROP** | Cap **accounts** (50), never rows — see §5. |
| 16 | Add / Delete an account | **N/A** | Legacy disabled both (`add: false, del: false`). |
| 17 | Notify the user their password changed | **NEVER EXISTED** | Legacy sent no email. The admin tells them out of band. |

**Not this tool:** the anonymous self-service forgot/reset-by-email loop (`AuthController`
`forgot-password` / `reset-password`). Different feature, different auth, sends mail. Don't conflate them.

---

## 4. Roles

The dropdown offers **6 of the 17** roles in `RoleConstants`, hardcoded in
`ChangePasswordService.GetRoleOptionsAsync` — it is **not** read from `AspNetRoles`:

`Player` (default) · `Club Rep` · `Director` · `SuperDirector` · `Unassigned Adult` · `Staff`

- **`Family` is deliberately absent.** You reach the family login by searching **Player**; it arrives
  on the join. There is no separate family search.
- **`Superuser` is absent** — you cannot find another Superuser through search. This used to be
  theatre: the old reset endpoint ignored its own `regId` and reset whatever username the body named,
  so a Superuser could be reset by anyone who could type one. It is now enforced — the account is
  resolved from the registration, and a registration is never a Superuser's. See §5.3.
- **Not reachable at all:** `Referee`, `RefAssignor`, `Scorer`, `Recruiter`, `StoreAdmin`, `StpAdmin`.
  They have real logins and this tool cannot find them. **Open product decision — not a bug to
  silently "fix".**

---

## 5. Defects found by this audit, and what was done

Ordered by value. All **FIXED** unless marked otherwise.

1. **Family-merge key found only 47% of real duplicates.** §2. **FIXED** — re-keyed on the child
   (`FirstName` + `LastName` + `dob`), which is the signal the data actually carries. Coverage
   28,755 → 60,780 family logins, and the worst case is 11 candidates, so the net widened without
   fanning out.
2. **Merge candidates were unidentifiable.** `MergeCandidateDto` was `{ UserName, UserId }` — the
   admin picked between two raw GUIDs with nothing to tell them apart, on an irreversible operation.
   **FIXED** — it now carries the household, the children (with the *matched* child marked, since
   that child is the entire reason the candidate is a candidate), the jobs, and the registration
   count. The **blast radius** is stated before the confirm button goes live.
3. **`reset-password` and `reset-family-password` were byte-for-byte identical, and both ignored the
   `{regId:guid}` in their own route.** The reset was keyed purely on `request.UserName` from the
   body, so **all targeting lived in the browser** and any username in the system could be reset with
   any well-formed GUID in the path — including a Superuser's. **FIXED** — collapsed to one endpoint;
   the account is resolved from the *registration's* FK, and `ExpectedUserName` is a stale-UI guard,
   not the targeting mechanism.
4. **Neither merge validated its target against the candidate list.** The list was decoration: the
   API would re-point registrations onto *any* account named in the body. **FIXED** — both merges now
   re-derive the candidate set server-side and reject a target that isn't in it.
5. **No audit trail, no log line, no notification.** A cross-tenant tool that changes credentials kept
   no record of who did what to whom. **FIXED** — see §6.
6. **The players grid carried 11 columns that are constant on every row** (`Role`, `F-UserName`,
   `F-Email`, all four `Mom*`, all four `Dad*`) — family-level facts stamped onto every child inside
   that family's own card, three of them rendered twice. This is why it needed a horizontal scroll and
   three frozen columns. Cause: `6dd00c3a` adopted legacy's `colModel` labels for auditability and the
   **parity checklist got promoted into a layout constraint.** **FIXED** — hoisted to the account card
   as a `Household` block; the grid is six columns and fits.
7. **Clearing an email wrote `""`, not `NULL`** — including `NormalizedEmail`. **FIXED** (`BlankToNull`).
   *There are 2 pre-existing `Email = ''` rows in `AspNetUsers` from before this fix; they are
   cosmetic, and cleaning them up is a separate authorized `UPDATE`.*

---

## 6. The audit trail lives in Seq. There is no audit table.

Every mutating action emits a Serilog event tagged **`cp_audit=true`**. That tag is the whole
interface:

```
cp_audit = true                            -- everything this tool has ever done
cp_audit = true and Outcome = 'FAILED'     -- everything it refused to do, and why
cp_audit = true and AuditAction like 'Merge%'
TargetUserName = 'jsmith'                  -- everything ever done TO one account
```

Each event carries `Actor` (the `username` claim — **not** remapped), `ActorUserId`, `ClientIp`,
`AuditAction`, `RegistrationId`, `TargetUserName`, and `Outcome`.

**The password is never logged** — not the plaintext, not the hash, not a fragment. That the reset
happened, by whom, to whom, is the whole fact.

### The merge carries its own undo

A merge is irreversible and the count is **not** enough to reverse it: the target account normally
already owns registrations of its own, so afterwards "12 rows moved onto B" leaves you unable to say
*which* of B's rows used to be someone else's. So the merge events carry a **`ReversalPayload`** — a
JSON array pairing every moved `RegistrationId` with the `PreviousUserId` it was moved off:

```json
[{"RegistrationId":"3f2a…","PreviousUserId":"9c17…"}, …]
```

Copy it out of Seq, `OPENJSON` it, restore `PreviousUserId`. It is a pre-serialized *string*, not a
Serilog destructured collection, precisely so no collection-depth limit can silently truncate the one
field that makes the operation recoverable — a merge of a large household moves hundreds of rows, and
that is exactly the merge someone will need to undo.

**A table was considered and rejected.** Seq is already the logging system of record (prod:
`seq.teamsportsinfo.com`); a parallel `logs.ChangePasswordAudit` would be a second thing to keep in
sync. Note the corollary: **Seq's retention policy is now the audit-retention policy.**

*(Aside: `logs.AppLog` exists in `scripts/create-logs-schema.sql` and has **0 rows** — it was created
for a `Serilog.Sinks.MSSqlServer` sink that was never added to the csproj. It is dead. Don't route
anything through it.)*

---

## 7. Do NOT "fix" these — measured, and they are not problems

Recorded because they *look* like bugs on a code read, and someone will try.

- **The player search's INNER JOINs hide nothing.** Of 661,074 registrations: **0** player rows have a
  NULL `Family_UserId`, **0** have a NULL `UserId`, **0** point at a missing `AspNetUsers` row, and
  **0** point at a family login with no `Families` row. LEFT JOINs would buy exactly nothing.
  *(The entire blind spot is **6 adult registrations with a NULL `UserId`**.)*
- **Legacy's adult-row family-wipe never fired.** Legacy's monolithic POST would have wiped
  `Mom_Email`/`Dad_Email` and nulled `Family_UserId` when editing an adult row that carried a
  `Family_UserId` — but **0 of 661,074 registrations are an adult row with a `Family_UserId`.** Real in
  code, unreachable in data. The endpoint split is still right; it just wasn't a rescue.
- **`EmailConfirmed` is a dead column.** It looks like a bug that an admin email change leaves it
  `true`. It is not read *anywhere* in the backend — `RequireConfirmedEmail` is off. Resetting it
  would be a **footgun**: it would do nothing today and silently lock users out the day someone
  enables confirmation. Leave it.
- **The cap-warning "false negative" cannot fire.** `resultsCapped = accounts.length >= 50` sits after
  the client drops rows with no login key (`if (!key) continue`), which *looks* like it could
  under-count. **0 registrations have a NULL or empty `UserName`**, so that `continue` never executes.
- **The merge's wide net is a feature.** See §1. Show it; don't shrink it.
- **The person merge sweeps; the family merge does not.** This asymmetry is deliberate, not an
  oversight. Name + DOB + role identifies **one human**, so the person merge consolidates every
  seasonal duplicate of that child at once. "Shares a child" identifies households that **overlap** —
  divorced parents legitimately share a child — so the family merge is strictly **pairwise**, source
  onto the one target the admin picked. Making it sweep would fuse two real households.

---

## 8. Related

- [`security-policy-account-separation.md`](../Architecture/security-policy-account-separation.md) —
  the policy layer above this one: an account is locked to one privilege level. Note the merge
  predicate matches on `RoleId`, so a merge cannot cross privilege levels. Keep it that way.
- Legacy source: `reference/TSIC-Unify-2024/.../ChangePasswordController.cs`
- Superseded: `migration-plans/025-changepassword-utility.md` — **stale** (claims `views/admin/`,
  a 200-row cap, and a reset modal; none are true).
