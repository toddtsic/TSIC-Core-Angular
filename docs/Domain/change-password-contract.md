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

`dbo.AspNetUsers` is the **only** table with a username or a password. Every account this tool can
touch is a row in it, and a reset is always `UserManager.GeneratePasswordResetTokenAsync` →
`ResetPasswordAsync` against one. The tool never reads or writes `PasswordHash` itself.

**`dbo.Families` is not an account table.** The name misleads: it is keyed on `Family_UserId` — which
is an `AspNetUsers.Id` — and holds **no credentials at all**, only the parent contact block
(`Mom_FirstName`, `Mom_LastName`, `Mom_Cellphone`, `Mom_Email`, and the `Dad_*` equivalents). It is a
contact record hanging off an account, not the account.

So "the family login" is always an `AspNetUsers` row. `Families` is where you find *who the parents
are*; it is never where you find *what they sign in with*.

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

### The merge exists for exactly one scenario

**A parent forgets their credentials, builds a brand new family account, and re-registers their
children under it.** Two family logins. Two copies of every child. Then they phone in and ask us to
put everything on the one login they can actually get into — *and they name it*.

Everything about the merge follows from that sentence:

- The unit is the **household**, not the child. The children collapse *because* the households do.
- The **survivor is an input**, not a deduction. The parent said `mabell2025`; the SuperUser finds
  that account in the list and picks it. The tool never guesses.
- The merge is therefore **two writes in one action**: `Family_UserId` for every registration under a
  losing login, and `UserId` for each duplicate child — because a parent who signs into the account
  they asked for and sees **Maya twice and Ethan twice** has not been helped.

> A duplicate player row inside **one** family login is a different thing entirely, and is often
> **deliberate** — it is how you get a second registration past an event's one-per-player rule. The
> merge must not fuse those. See §5.

### The identity key is a security control

`TSIC.Domain/Constants/HouseholdIdentity.cs`. **Read it before you touch the merge.**

```
email  AND  phone  AND  name        all three, normalized.  A placeholder is not an identity.
```

For a **family login** those three come from the **mother** — the family account *is* her data
(`Families.Mom_Email`, `Mom_Cellphone`, `Mom_FirstName`/`Mom_LastName`). For an **adult**, from their
own `AspNetUsers` row.

The SuperUser pulls an irreversible trigger on a list **this key produced**, so the candidate list
*is* the security boundary. Nothing downstream can recover from a wrong answer here. And the two
failure modes cost wildly different amounts:

| | |
|---|---|
| **A miss** | The parent is told to use their new account going forward. Nobody is harmed. |
| **A false match** | A SuperUser hands one family **another family's children**, cross-tenant, irreversibly. |

So the key is narrow on purpose, and **the recall it gives up is not worth arguing about.** Do not
"improve" it. In particular: normalization strips *formatting* only — it does **not** collapse Gmail
dots or `+tags`, even though Gmail delivers them to one inbox. Every such rule widens what counts as
the same person, and width is the attack surface. Soundex on the *name* is safe, and only because it
runs on a set `email AND phone` has already gated: it can only ever narrow.

### The child is never the key

It is tempting — 53% of family logins own a child that another login also owns, and keying on the
child finds all of them. **It is wrong.** "Owns the same child" says two households **overlap**, not
that they **are** one:

```
fam_A   Melissa & Michael Abell      →  Maya
fam_B   Michael Abell, post-divorce  →  Maya
fam_C   Melissa Shimizu, remarried   →  Maya, Ethan Shimizu
```

There is one Maya, correctly identified. There are **three households**. A child-keyed merge would
put **Ethan** — who is not Michael's son — into Michael Abell's login.

Divorced parents legitimately share a child. They do not share the mother's email and phone. The key
gets this right for free, and it needs no special case to do it.

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
| Distinct family logins | 115,038 |
| …that have a **true duplicate** (another login owning the same child) | **60,780 (53%)** |

### The name+DOB key that legacy merged on cannot identify a person

**33,214 clusters** of player accounts share a first name, last name and date of birth — **79,622
accounts**. In 175 of those clusters the accounts **disagree on gender**. Legacy's player merge swept
every registration in the system matching `(FirstName, LastName, RoleId)` — with `NULL`-permissive DOB
and email branches — and re-pointed all of them. That sweep:

- can fuse **two genuinely different children** who share a birthday, and
- **re-fuses a deliberate double-registration** (see §5), silently undoing an admin's workaround.

It is gone. A child is collapsed only inside their household's merge, and only when the match is
unambiguous.

### What actually identifies a household: the mother's contact block

Every measurement below is against the ground-truth duplicate set — **60,895 pairs** of family logins
that own the same child.

| Key | Pairs it links | |
|---|---|---|
| Mom email **AND** Mom phone | 47,437 | of which **592 look like strangers** — a different mother, children with unrelated surnames |
| **+ Mom name** | — | **deletes all 592**, at a cost of 2,749 genuine duplicates (7%) |
| **+ Soundex on the name** | — | recovers 868 of those (`Mellisa`/`Melissa`, `Kathy`/`Kathi`) |

That trade is free under the asymmetry in §1: 2,749 parents told to use their new account, versus 592
chances to hand a stranger somebody's children.

**Placeholders are the fan-out, and they are excluded by name.** People type junk to get past a
required field, and matching on it links strangers:

```
phone  0000000000      106 households      email  na@gmail.com     22
phone  5555555555       30                 email  none@none.com    21
phone  1111111111       27                 email  na@na.com        17
```

Same shape as `not@given.com`: a placeholder is a **flag**, not a contact. An account whose contact
block is a placeholder has **no key** and gets **no candidates**. Absence must never match absence.

**More than one candidate is normal, and is not a red flag.** A parent who has forgotten their
password twice has three logins; the worst real household on file has **eleven**, all keyed to one
mother (`kschlatts13@gmail.com` and `kmschlatter@optonline.net` even share her phone — one mom, two
addresses). They are all the same household. The SuperUser folds them in together.

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
| 10 | Merge **player** username (re-point `UserId`), on a global `name + DOB + role` sweep | **DROP** | A child is not an account you merge. The sweep reached **every customer in the system**, could fuse two children who share a birthday (33,214 name+DOB clusters; 175 disagree on gender), and would re-fuse a deliberate double-registration. A duplicate child is now collapsed **inside their household's merge**, unambiguously or not at all. |
| 11 | Merge **family** username (re-point `Family_UserId`), on 6 exact parent fields + postal | **PORT-WITH-FIX** | Re-keyed on **the mother** — email AND phone AND name, normalized, placeholders excluded. §2. Legacy also derived its own work-set from field equality *at write time*, so the candidate list had no bearing on what moved; the write is now bounded by the accounts the SuperUser explicitly selected. |
| 11a | Merge **adult** logins | **PORT-WITH-FIX** | Same key, on the adult's own row. An adult signs in as themselves. |
| 12 | Username cells are **dropdowns of existing duplicates**, never free text | **PORT** | You cannot invent a username. You can only merge into one that exists — and the server re-validates every name against the key. |
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

1. **Both merges were global sweeps whose candidate list was decoration.** Legacy re-derived the
   work-set from field equality *at write time*, so what the admin saw and what the write did were two
   unrelated computations. The player sweep reached every customer in the system on
   `name + DOB + role`. **FIXED** — one identity key (`HouseholdIdentity`), used by the preview and
   *validated at* the write, and the write itself is bounded by the accounts the SuperUser explicitly
   selected and re-read by FK. Nothing the browser sends can widen it.
2. **The family key was the wrong shape.** Six exact parent fields plus postal code. **FIXED** — it is
   the mother: email AND phone AND name (§2). And it is deliberately *not* the child, which is the
   obvious-looking answer and the wrong one — see §1.
3. **The player merge could fuse two different children, or un-do a deliberate double-registration.**
   **FIXED** — deleted. A child is collapsed only inside their household's merge, and only when both
   sides hold exactly one row for that `(name, DOB)`. Two rows on either side is ambiguous — it is
   what a deliberate double-registration looks like — so **both are left alone.**
4. **The merge moved the household but not the children.** The parent would sign into the account they
   asked for and find **Maya twice and Ethan twice** — one player row from each login that was just
   fused. **FIXED** — the child collapse is part of the same operation, not a second trip through the
   tool.
5. **Merge candidates were unidentifiable.** `MergeCandidateDto` was `{ UserName, UserId }` — the
   admin picked between two raw GUIDs with nothing to tell them apart, on an irreversible operation.
   **FIXED** — it now carries the household, the children, the jobs, and the registration count. The
   **blast radius** is computed from what is *checked*, and shown before the confirm button goes live.
6. **The merge left inactive registrations behind** on a login nobody can sign into again. Legacy
   filtered on `BActive`. **FIXED** — everything moves. The whole point is that the parent gets their
   history back, and a dropped or pending registration is still theirs.
7. **`reset-password` and `reset-family-password` were byte-for-byte identical, and both ignored the
   `{regId:guid}` in their own route.** The reset was keyed purely on `request.UserName` from the
   body, so **all targeting lived in the browser** and any username in the system could be reset with
   any well-formed GUID in the path — including a Superuser's. **FIXED** — collapsed to one endpoint;
   the account is resolved from the *registration's* FK, and `ExpectedUserName` is a stale-UI guard,
   not the targeting mechanism.
8. **No audit trail, no log line, no notification.** A cross-tenant tool that changes credentials kept
   no record of who did what to whom. **FIXED** — see §6.
9. **The players grid carried 11 columns that are constant on every row** (`Role`, `F-UserName`,
   `F-Email`, all four `Mom*`, all four `Dad*`) — family-level facts stamped onto every child inside
   that family's own card, three of them rendered twice. This is why it needed a horizontal scroll and
   three frozen columns. Cause: `6dd00c3a` adopted legacy's `colModel` labels for auditability and the
   **parity checklist got promoted into a layout constraint.** **FIXED** — hoisted to the account card
   as a `Household` block; the grid is six columns and fits.
10. **Clearing an email wrote `""`, not `NULL`** — including `NormalizedEmail`. **FIXED** (`BlankToNull`).
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
- **The identity key gives up recall on purpose.** It misses ~7% of genuine duplicates and it will
  never find a household whose contact block is a placeholder. Both are deliberate. A miss costs the
  parent nothing — they use their new account — and every rule you would add to close the gap
  (Gmail-dot folding, fuzzy emails, an OR instead of the AND, keying on the child) widens what counts
  as the same person. Width is the attack surface. See the header of `HouseholdIdentity.cs`.
- **The child is not the key, and the coverage argument for making it the key is a trap.** It finds
  more duplicates *and it is wrong*: sharing a child means two households OVERLAP. §1.
- **More than one merge candidate is not a red flag.** A mother with eleven logins is a real row in
  this database. They all key to her; fold them all in.
- **The child collapse refuses ambiguity rather than guessing.** Two player rows for one child on
  either side of the merge and it leaves BOTH alone. That is not a gap — a family may hold two rows
  for one child *deliberately*, to get a second registration past an event's one-per-player rule, and
  fusing them back together silently destroys that.

---

## 8. Related

- [`security-policy-account-separation.md`](../Architecture/security-policy-account-separation.md) —
  the policy layer above this one: an account is locked to one privilege level. Note the merge
  predicate matches on `RoleId`, so a merge cannot cross privilege levels. Keep it that way.
- Legacy source: `reference/TSIC-Unify-2024/.../ChangePasswordController.cs`
- Superseded: `migration-plans/025-changepassword-utility.md` — **stale** (claims `views/admin/`,
  a 200-row cap, and a reset modal; none are true).
