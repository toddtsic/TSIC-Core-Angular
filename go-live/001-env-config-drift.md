# 001 — Environment / Config Drift

**Status:** closed (2026-05-19)
**Risk class:** catastrophic — wrong-environment credentials could charge real cards, send real email, or write real DB from a dev/staging deploy.

## The bruise

A prior publish incident left **production referencing dev endpoints** because the deploy didn't correctly swap `appsettings.{Env}.json` / `web.config.{Env}` files. Caught by Todd, not by any system check.

## The fear

The reverse direction. A misdeploy that leaves **dev or staging referencing prod** — real ADN merchant, real SES, real VerticalInsure prod, real DB. The 2026-05-01 finding (six sites across SES / ADN / VI gated on `IsDevelopment()`, which fails under IIS because the env defaults to `Production`) is a documented example of this shape — code that *thought* it was sandboxed but wasn't.

## Claimed defenses (per `CLAUDE.md`)

1. Deploy scripts pass `-c <Configuration>` and let the build system swap files — **no regex patching of source files at deploy time**.
2. Bootstrap assertions in `Program.cs` and `main.ts` are supposed to throw if env name and host don't agree.
3. `SANDBOX-RULE`: only `TSIC-PHOENIX` may make real Email / ADN / VI calls; everything else must sandbox by `Environment.MachineName`, not `IsDevelopment()`.

None of these caught the original bruise, so **claimed ≠ working**. Each needs verification.

## Probes

- **(A)** Read `Program.cs` and `main.ts`. Confirm the env↔host assertion exists, throws (not logs), and covers all three envs.
- **(B)** Build a matrix of every `appsettings.*.json` and `web.config.*` value that could destroy money/data if invoked from the wrong env: connection strings, SES creds, ADN merchant IDs, VI keys, Mapbox tokens, base URLs. Confirm each env's value is correct for that env.
- **(C)** Re-audit the 2026-05-01 `IsDevelopment()` → `MachineName == "TSIC-PHOENIX"` sweep. Confirm all six sites flipped; grep for any *new* `IsDevelopment()` gates added since.
- **(D)** Diff `1-Build-And-Deploy-Local.ps1` against `1-Build-And-Deploy-Prod.ps1`. Look for asymmetries in how config files are selected, copied, or transformed.

## Findings — Step 1 (inventory)

### Existing defenses (already in place, verified by reading source)

| Defense | Location | What it does |
|---|---|---|
| Backend env↔machine assertion | `TSIC.API/Program.cs` L72–98 | Throws if `Production` env runs on non-PHOENIX or `Staging`/`Development` runs on PHOENIX. Runs BEFORE service registration. |
| Frontend env↔apiUrl assertion | `tsic-app/src/main.ts` L7, L18–29 | Throws and paints a red error page if `environment.envName` and `environment.apiUrl` disagree on host. Runs BEFORE `bootstrapApplication`. |
| Backend startup snapshot to Seq | `Program.cs` L656–668 | Logs `Env`, `Machine`, `DbServer`, `FrontendBaseUrl` at boot. Foundation already there — needs expansion to cover all external refs per Step 2. |
| `HostEnvironmentExtensions.IsLiveProduction()` | `TSIC.API/Extensions/HostEnvironmentExtensions.cs` | `IsProduction() && MachineName == "TSIC-PHOENIX"`. `IsSandbox() = !IsLiveProduction()`. The SANDBOX-RULE pattern. |
| Email gates on `IsSandbox()` | `EmailService.cs` L51 | Non-Phoenix boxes skip SES transmission (unless `sendInDevelopment` flag explicitly set). |
| VerticalInsure gates client ID on `IsSandbox()` | `VerticalInsureService.cs` L339, L365 | Dev/staging use `test_…` client ID + dev secret env vars; only PHOENIX uses `live_…` client ID + prod secret env vars. |
| Angular `fileReplacements` | `angular.json` L66–104 | Build-time swap of `environment.ts` for each configuration; no source-file patching needed at deploy. |

### Inventory table — external references per environment

| Reference | Development | Staging (this box, .204) | Production (PHOENIX, .202) | Source |
|---|---|---|---|---|
| **Backend env selector** (`ASPNETCORE_ENVIRONMENT`) | `Development` (launchSettings/`dotnet run`) | `Staging` | `Production` | `web.config` per IIS site; assertion in Program.cs forces match w/ MachineName |
| **DB connection** | `Server=.\SS2016;Database=TSICV5;Trusted_Connection=True` | (inherits base — same string) | (inherits base — same string) | `appsettings.json` only. **No env-specific override.** Each box has its own local SQL Server. |
| **Frontend Base URL** (for email links) | `http://localhost:4200` | `https://dev.teamsportsinfo.com` | `https://claude-app.teamsportsinfo.com` | `appsettings.{Env}.json` `FrontendSettings:BaseUrl` |
| **Seq log sink** | `http://localhost:5341` | (inherits base — `http://localhost:5341`) | `https://seq.teamsportsinfo.com` | `appsettings.json` (dev/staging), `appsettings.Production.json` (prod) |
| **Crystal Reports** | `https://tsic-cr-2025.conveyor.cloud/api/` | (inherits base — `https://cr2025.teamsportsinfo.com/api/`) | (inherits base — same) | `appsettings.json` + Development override |
| **Banner files path** | `C:\Websites\TSIC-STATICS\BannerFiles` | (inherits — same) | `E:\Websites\TSIC-STATICS\BannerFiles` | base + Production override |
| **Statics URL** | `https://statics.teamsportsinfo.com` | (same) | (same) | base — never overridden |
| **JWT issuer / audience** | `TSIC.API` / `TSIC.Client` | (same) | (same) | base — never overridden |
| **JWT signing key** | committed plaintext in `appsettings.json` | (same — inherited) | (same — inherited) | ⚠️ **Critical issue. See below.** |
| **ADN sweep enabled** | (unset — defaults off) | explicitly `false` | `true`, runs at 5:00 local | per-env override |
| **ADN gateway** | SANDBOX | **PRODUCTION** (!!) | PRODUCTION | code gates on `_env.IsDevelopment()`, NOT `IsSandbox()` — see findings |
| **ADN credentials source** | user-secrets / env vars (`ADN_SANDBOX_LOGINID` / `_TRANSACTIONKEY`) | per-customer in DB | per-customer in DB | `AdnApiService.GetJobAdnCredentials_FromJobId` |
| **SES region** | AWS SDK default chain | (same) | (same) | env vars (`AWS_REGION`/`AWS_DEFAULT_REGION`) or `AwsSettings`/`EmailSettings` (neither populated in any committed config) |
| **SES credentials source** | env vars or SDK default chain | (same) | (same) | not in any committed config — **need to verify per machine** |
| **SES send gate** | sandboxed (machine != PHOENIX) | sandboxed (machine != PHOENIX) | LIVE | `EmailService.SendAsync` L51 |
| **VerticalInsure base URL** | `https://api.verticalinsure.com` (same prod URL for sandbox & live) | (same) | (same) | `Program.cs` L229–237, single endpoint, sandbox vs live separated by *client ID*, not URL |
| **VerticalInsure client ID** | `test_GREVHKFHJY87CGWW9RF15JD50W5PPQ7U` (hardcoded) | `test_…` (same — non-PHOENIX) | `live_VJ8O8O81AZQ8MCSKWM98928597WUHSMS` (hardcoded) | `VerticalInsureService.BuildPlayerObject` L337–339. **Hardcoded constants in source code.** |
| **VerticalInsure secrets** | env vars `VI_DEV_CLIENT_ID` / `VI_DEV_SECRET` | (same) | env vars `VI_PROD_CLIENT_ID` / `VI_PROD_SECRET` | `VerticalInsureService.ResolveCredentials` |
| **USA Lacrosse API base** | `https://api.usalacrosse.com/` | (same) | (same) | `Program.cs` L377 fallback. Settings section `UsLax:ApiBase` overrides — currently unset in any appsettings file. |
| **USA Lacrosse credentials** | env vars `USLAX_CLIENT_ID` / `_SECRET` / `_USERNAME` / `_PASSWORD` | (same) | (same) | `UsLaxService.ResolveCredentials` L526–535 — **no sandbox endpoint exists; all envs hit prod USLax with whichever creds are set** |
| **Frontend → backend `apiUrl`** | `https://localhost:7215/api` | `https://devapi.teamsportsinfo.com/api` | `https://claude-api.teamsportsinfo.com/api` | `environment.{env}.ts` |
| **Frontend default build configuration** | n/a (`ng serve` defaults `development`) | n/a | n/a | `angular.json` `defaultConfiguration: "staging"` for build, `"development"` for serve |
| **Frontend US Lax test number** | `'424242424242'` | `null` | `null` | `environment.{env}.ts` — only dev injects a test value |
| **CORS origins** | `https://localhost:4200`, `https://*.teamsportsinfo.com`, `https://teamsportsinfo.com` | (same — single policy) | (same — single policy) | `Program.cs` L620–644 |
| **AllowedHosts** | `*` | (inherits) | (inherits) | `appsettings.json` |
| **Anthropic API model** | `claude-haiku-4-5-20251001` | (inherits) | (inherits) | base — no per-env override |

### Issues found

1. **⚠️ CRITICAL — JWT signing key committed in plaintext.** `appsettings.json` L6:
   ```
   "SecretKey": "TSIC-Production-Secret-Key-Change-This-To-Something-Secure-67890!"
   ```
   Anyone with read access to this repo can mint a JWT for any role (Superuser included) for any env. No env overrides this — base value wins everywhere. The value even contains the literal "Change This" as a self-reminder that was never acted on. **This is the single most catastrophic exposure surface in the codebase.**

2. **⚠️ MAJOR — ADN gateway gates on `IsDevelopment()`, not `IsSandbox()`.** `AdnApiService.cs` L36, L45, L65:
   ```csharp
   if (_env.IsDevelopment() && !bProdOnly) return SANDBOX;
   return PRODUCTION;
   ```
   This is the documented 2026-05-01 SANDBOX-RULE bug pattern. Consequence: **Staging on .204 hits ADN PRODUCTION** — pulls real merchant credentials from the customer DB and would issue real charges if anything triggered a charge path. The corresponding email/VI surfaces are fixed; the ADN surface is not. Should be `_env.IsSandbox()` (which is `!IsLiveProduction()`).

3. **Dead `Email:EnableSandboxMode` config.** `appsettings.Development.json` sets `"EnableSandboxMode": true`, but `EmailService` never reads it — the real gate is `_env.IsSandbox()` (machine-based). Misleading: a reader of appsettings would think this flag matters. Either wire it up or remove.

4. **Two competing web.config deploy patterns in `scripts/`.**
   - `scripts/web.config.api.staging` and `web.config.api.production`: hardcoded `ASPNETCORE_ENVIRONMENT` values
   - `scripts/IIS-Config-{Dev,Prod}/web.config.api`: uses `__ASPNET_ENV__` placeholder (implies regex substitution at deploy)

   These cannot both be active; one is dead code. Which one the deploy script actually uses is unknown without reading `1-Build-And-Deploy-Local.ps1` / `1-Build-And-Deploy-Prod.ps1`. If `__ASPNET_ENV__` substitution is live, that contradicts `CLAUDE.md`'s claim of "no regex patching at deploy time."

5. **Production has no DB connection-string override.** All three envs inherit `Server=.\SS2016;Database=TSICV5` from base. Works only because each box has its own local SQL Server with a database literally named `TSICV5`. If someone ever wanted to point PHOENIX at a different DB, the connection string is invisible to environment-overlay logic. Worth confirming each box's `TSICV5` is actually the right database.

6. **SES / AWS credentials not visible in committed config.** `EmailSettings`, `AwsSettings`, and `AuthorizeNet` sections are all bound in `Program.cs` but no committed `appsettings.*.json` populates them. Real values are sourced from env vars or the SDK credential chain on each machine. **Inventory of these values requires checking each machine's environment** — they are not deducible from source.

7. **USA Lacrosse: no sandbox endpoint exists.** `UsLaxService` always hits `https://api.usalacrosse.com/`. All envs use whichever creds the env vars carry. If dev/staging boxes have creds set (env vars or appsettings), they will ping the real USLax API. The vendor has blacklisted us before (per code comment) — so a misconfigured dev box has a real-world incident shape. Verify staging has no `USLAX_*` env vars unless intentional.

8. **`appsettings.Production.json` is thin.** Only overrides `FrontendSettings`, `FileStorage`, `Seq`, and `AdnSweep`. Everything else (DB, JWT, Anthropic, AllowedHosts, FileStorage.MedFormsPath) comes from base. Acceptable design but means base values must be **correct for production** by default — base is the dangerous-default surface.

### Inventory gaps (need machine-side verification, not file-based)

- AWS_REGION / AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY env vars on each machine
- ADN_SANDBOX_LOGINID / ADN_SANDBOX_TRANSACTIONKEY on dev box
- VI_DEV_CLIENT_ID / VI_DEV_SECRET / VI_PROD_CLIENT_ID / VI_PROD_SECRET on each machine
- USLAX_CLIENT_ID / USLAX_SECRET / USLAX_USERNAME / USLAX_PASSWORD on each machine
- Anthropic API key (probably an env var)
- Firebase credentials file (`FirebaseAuth_TSICEvents.json` referenced in base config) — present/correct per machine?

### Deploy-script audit (open — separate probe)

Which `web.config.api*` does each deploy script actually push? `1-Build-And-Deploy-Local.ps1` and `1-Build-And-Deploy-Prod.ps1` need to be read to answer this. Not done in this step.

## Decisions / fixes

### Done

**JWT signing key rotated out of source (issue 1 above)**

- Generated three independent 64-byte (512-bit) cryptographically random keys, one per env.
- Dev: stored in `dotnet user-secrets` (store ID `8eb66727-9711-4eb5-a22e-55bc44b2b6fa`). Fingerprint `PkuT...Ow==`.
- Staging (`dev-api` on TSIC-SEDONA): app-pool env var `JwtSettings__SecretKey` in `applicationHost.config`. Fingerprint `gNkV...Uw==`.
- Production (`claude-api` on TSIC-PHOENIX): same pattern. Fingerprint `70tf...OA==`.
- Removed the committed `SecretKey` line from `appsettings.json`. `Program.cs:439` still throws if no key resolves, so any box missing its env var fails loud at boot (fail-fast safety net).
- The previously-committed key is permanently in git history; treat as burned, never reuse. Each env now uses its own key.
- Takes effect when each box's app pool recycles (publish does this automatically). All issued JWTs invalidate at that moment — users re-authenticate.

**[STARTUP-CONFIG] audit logging added (Step 2 / 3 of the original three-step plan)**

- Backend `Program.cs` startup-snapshot block expanded from one line to a `[STARTUP-CONFIG]` series. Twelve categories: host, db, jwt, adn, ses, verticalInsure, usLax, frontend, seq, cors, adnSweep. Each line is one `LogInformation` tagged `boot_audit=true` for Seq filtering. Secrets log first-4-char fingerprint only.
- Frontend `main.ts` emits one `[STARTUP-CONFIG]` line to `console.info` before `bootstrapApplication`: envName, host, apiUrl, staticsUrl, buildVersion.
- Verification handle for both the JWT rotation and any future env-overlay change. Reviewable in `seq.teamsportsinfo.com` for staging+prod and browser console for dev.

**Issue 8 — `appsettings.Production.json` thin / base file as dangerous-default (2026-05-19, closed)**

Functionally resolved by Issues 5 + 9. Production overlay now explicitly sets `ConnectionStrings:DefaultConnection`, `FrontendSettings:BaseUrl`, `FileStorage` paths, `Seq:ServerUrl`, and `AdnSweep`. Remaining base-inherited values are either identity-uniform (`JwtSettings`), already-prod-correct (`Reporting.CrystalReportsBaseUrl`, `TsicSettings.StaticsBaseUrl`), or static data (`Anthropic.Model`, `Firebase.CredentialFilePath`, `TsicSettings.DefaultCustomerId`).

**One residual hardening logged as future-work, intentionally not addressed**: `AllowedHosts: *` on prod accepts any Host header. Tightening to an explicit allowlist is a defensive measure against Host-header injection. User decision (2026-05-19): leave as `*` for now to preserve flexibility on hostname additions during go-live; revisit after the launch hostname set stabilizes.

**Issue 7 — USLax has no sandbox endpoint (2026-05-19, closed as by-design)**

User-confirmed intentional posture: every env hits the live USLax API. There is no USLax sandbox available, and dev/staging deliberately use prod USLax for verification testing. SANDBOX-RULE exception — USLax is treated like a read-mostly external service whose only credentials are prod.

Mitigation in place: USLax credentials (CLIENT_ID, SECRET, USERNAME, PASSWORD) all live on pool env vars per box; the credentials themselves are real-prod regardless of `ASPNETCORE_ENVIRONMENT`. No app code conditionalizes the USLax endpoint. Documented exception, not a bug.

**Issue 6 — per-machine secret inventory completed (2026-05-19, commit `a5411795`)**

Extended the `[STARTUP-CONFIG]` boot block so every required secret produces a first-4-char fingerprint at startup. New/changed lines:

- `adn:` + `sandboxTransactionKeyFp`
- `ses:` + `secretKeyFp`
- `verticalInsure:` + `configSecretFp`
- `usLax:` + `secretFp` + `usernameFp` + `password=set|unset` (password is set/unset only — minimal attack surface)
- new `anthropic:` line — `model` + `apiKeyFp`
- new `firebase:` line — `credentialFilePath` + `fileExists`

Verified on both runtime boxes:

| Secret | .204 (Staging) fp | PHOENIX (Prod) fp |
|---|---|---|
| jwt.signingKey | gNkV... | 70tf... |
| adn.sandboxLoginId | 4dE5... | 4d5m... |
| adn.sandboxTransactionKey | 6zmz... | 6Zmz... |
| ses.accessKey | AKIA... | AKIA... |
| ses.secretKey | 2guN... | 2guN... |
| vi.configClientId | test... | live... |
| vi.configSecret | test... | live... |
| usLax.clientId | 9cc2... | 9cc2... |
| usLax.secret | ac58... | ac58... |
| usLax.username | team... | team... |
| usLax.password | set | set |
| anthropic.apiKey | sk-a... | sk-a... |
| firebase.credentialFile | fileExists=true | fileExists=true |

All required secrets resolve on both boxes. Every future boot re-prints this inventory — drift becomes visible in Seq seconds after a deploy.

Side effect: cleaned up a stale `TSIC-PHOENIX` literal in the `isLive` computation that was missed in `ec3988b0`. Source no longer contains the machine name anywhere.

**Issue 9 — env overlays moved into source; gitignore rule retired (2026-05-19)**

The original gitignore was set when env overlays held secrets. After yesterday's JWT key rotation and this morning's gate cleanup, the three overlays (`Development`, `Staging`, `Production`) contain only topology: connection strings (trusted auth, no password), hostnames, file paths, feature toggles. No secrets remain.

Removed the `**/appsettings.*.json` and `**/appsettings.Development.json` rules from both `.gitignore` files. Kept `**/appsettings.Local.json` as the per-developer override slot. Committed all three overlay files to source. New canonical-recovery story: any box can be rebuilt from a fresh clone and per-pool env-var setup (`07-Apply-Secrets.ps1`). No more "Sedona is the deployer of record because nowhere else has the overlays."

`CLAUDE.md` updated to document the new policy: env overlays committed; secrets only in pool env vars; `appsettings.Local.json` is the per-developer escape hatch.

**Issue 5 — explicit per-env `ConnectionStrings:DefaultConnection` overlay added (2026-05-19)**

Added the same connection string (`Server=.\SS2016;Database=TSICV5;...`) to both `appsettings.Staging.json` and `appsettings.Production.json` on the Sedona deployer box. Value unchanged today on both runtime boxes; the wiring is now visible per env so future DB renames, server moves, or auth-mode changes have an obvious per-env editing point.

**Caveat surfaced:** `**/appsettings.*.json` is gitignored at `TSIC-Core-Angular/.gitignore:42` — only `appsettings.json` is in the repo. The Sedona deployer owns the per-env overlay files; they travel to .204 (local IIS site) and PHOENIX (via the prod deploy script) as part of the published build output, not via git. A fresh clone of this repo has NO env overlay files. This is a separate architectural risk worth its own issue — see new Issue 9.

**Issue 4 — web.config templates consolidated + redundant regex patching removed from deploy scripts (2026-05-19)**

Three coordinated changes, zero runtime behavior change:

1. **One canonical `scripts/web.config.api`** template, env-agnostic (no `<environmentVariables>` block). `ASPNETCORE_ENVIRONMENT` is set exclusively on the app pool's env-var collection (provisioned by `IIS-Config-{Dev,Prod}/Setup/07-Apply-Secrets.ps1`). `Program.cs` refuses to start if the env var is missing — drift surfaces loud.
2. **Deleted five dead templates**: `scripts/web.config.api.staging`, `scripts/web.config.api.production`, `scripts/web.config`, `scripts/IIS-Config-Prod/web.config.api`, `scripts/IIS-Config-Dev/web.config.api`.
3. **Removed regex patching** from `1-Build-And-Deploy-Prod.ps1` and `IIS-Config-Dev/Deployment/Publish.ps1`:
   - `__ASPNET_ENV__` substitution gone (placeholder no longer exists in the template).
   - `appsettings.json` / `appsettings.Production.json` regex patches (`C:\Websites` → `E:\Websites`, `devapi.teamsportsinfo.com` → `claude-api…`, `dev.teamsportsinfo.com` → `claude-app…`) deleted. All values already overridden by `appsettings.Production.json`, so the patches were rewriting correct values with the same correct values — pure redundancy with drift risk.

End state matches the CLAUDE.md design claim: "no regex patching of source files at deploy time" now also extends to publish output. Env name lives only on the pool env var. One web.config template across all envs.

**Issue 3 — dead `Email:EnableSandboxMode` config + unused `EmailSettings.SandboxMode` property removed (2026-05-19)**

Three coordinated edits, zero runtime behavior change:

1. `appsettings.Development.json` — deleted the `Email: { EnableSandboxMode, EmailingEnabled }` block. Both keys were inert (wrong section name — `Email` vs the bound `EmailSettings` — and `EnableSandboxMode` didn't match the property `SandboxMode` either way). Misleading config gone.
2. `EmailSettings.cs` — removed the `SandboxMode` property. Never bound from any config, so always `false`; only `EmailHealthService` read it.
3. `EmailHealthService.cs` — `status.SandboxMode` now populated from `_env.IsSandbox()` (the real source of truth, matching the SES send gate at `EmailService.cs:51`). The quota-warning suppression logic at L66 still works identically.

Design clarification recorded: email gating is `_env.IsSandbox()` (off on Staging/Development by default) + `SendAsync(..., sendInDevelopment: true)` per-call override. No config flag participates. `EmailSettings.EmailingEnabled` remains as a global kill switch (default `true`, can be set false via `EmailSettings:EmailingEnabled` in any appsettings overlay to disable email entirely — useful for maintenance windows; not wired today but kept as a hook).

**Issue 2 — ADN sandbox/prod gate corrected; pool env names normalized (2026-05-19, commit `ec3988b0`)**

Three coupled changes in one commit:

1. **`AdnApiService.cs` (3 sites: L33, L45, L65)** — gate switched from `_env.IsDevelopment()` to `!_env.IsProduction()`. With `_env.IsDevelopment()` the gate only caught the local vscode case; Staging silently fell through to PRODUCTION and pulled real customer ADN credentials. New gate sandboxes every non-Production env name.
2. **`AdnApiService.cs:82-95` (`ResolveCredentials`)** — `??` operator replaced with explicit `IsNullOrWhiteSpace` checks. `AdnSettings` string properties default to `""` (not null), so `??` short-circuited on the empty default and the env-var fallback (`ADN_SANDBOX_LOGINID` / `_TRANSACTIONKEY`) was unreachable. When the pool flipped to Staging and `dotnet user-secrets` stopped auto-loading, the empty defaults stayed empty and `ResolveCredentials` threw a 500 on the first request. Bug existed in the original code; surfaced as a side effect of the env-name flip.
3. **`HostEnvironmentExtensions.IsLiveProduction` and `Program.cs:72-86`** — both lost their `MachineName == "TSIC-PHOENIX"` clauses. The new `Program.cs` startup check refuses to start if `ASPNETCORE_ENVIRONMENT` is unset, so the env name is trustworthy as the single runtime source of truth. Machine identity is enforced once at provisioning, not at every gate. Source no longer contains the literal `TSIC-PHOENIX` anywhere.

Pool env on TSIC-SEDONA's `dev-api` was changed from `Development` to `Staging` as part of this work — completes the env-model alignment (`appsettings.Staging.json` now loads, dev-only middleware off, etc.). Side effect to be aware of: `_env.IsDevelopment()` is now false on .204 IIS, so `DevRegistrationController`, `JobCloneController` debug endpoints (L235/L249), and the dev auth shortcuts in `AuthController` (L97/L155) no longer respond on .204 — they were only ever intended for local vscode, but they previously also served on .204. Decision per-site if any need to migrate to `!_env.IsProduction()`.

### Still open



### Verification log

**Dev gate — PASSED (2026-05-18, VSCode debugger)**

Verified:
- App startup completed (i.e. `Program.cs:439` did not throw — proves `JwtSettings:SecretKey` resolved from user-secrets, since the committed value is gone).
- All twelve `[STARTUP-CONFIG]` lines present in debug console, correct order: host, db, jwt, adn, ses, verticalInsure, usLax, frontend, seq, cors, adnSweep.
- `[STARTUP-CONFIG] jwt:` line shows `signingKey="PkuT..."` — matches the generated dev fingerprint. **Rotation proof.**
- Frontend `[STARTUP-CONFIG]` line in browser console: `env=development host=localhost apiUrl=https://localhost:7215/api staticsUrl=https://statics.teamsportsinfo.com build=dev`.
- Login as `TSICSuperUser` minted a valid HS256 accessToken (`iss=TSIC.API`, `aud=TSIC.Client`) — confirms signing/validation end-to-end with the rotated key.

Informational (visible in the log, individual values not separately OCR-confirmed):
- adn, ses, verticalInsure, usLax, frontend, seq, cors, adnSweep lines all populated with non-empty values. Spot-checks deferred to staging/prod where Seq is searchable.

**Staging gate — PASSED (2026-05-18, after `1-Build-And-Deploy-Local.ps1` to dev.teamsportsinfo.com)**

Verified:
- App pool `dev-api` recycled cleanly by deploy script ("Started AppPool: dev-api", DB login verified, API warmup succeeded).
- All eleven `[STARTUP-CONFIG]` lines emitted to `seq.teamsportsinfo.com` on boot at 17:45:44:
  - `host: env=Staging machine=TSIC-SEDONA isLiveProduction=false`
  - `db: server=.\SS2016 database=TSICV5`
  - `jwt: issuer=TSIC.API audience=TSIC.Client signingKey=gNkV...` **← rotation proof (matches staging key set on `dev-api` app-pool env var)**
  - `adn: defaultMode=PRODUCTION sandboxLoginIdFp=4dE5...` — issue-2 evidence captured (staging hits ADN PRODUCTION; bug, not fix-yet)
  - `ses: ... sendGate=sandbox` — staging gated off real SES correctly
  - `verticalInsure: hardcodedClientIdGate=test_...` — staging uses sandbox VI client ID correctly
  - usLax, frontend, seq, cors, adnSweep all present (confirmed via Seq text filter `STARTUP-CONFIG`)
  - `adnSweep: enabled=False` — staging correctly has the 5 AM sweep disabled per `appsettings.Staging.json`
- Frontend `[STARTUP-CONFIG]` line in `dev.teamsportsinfo.com` browser console: `env=staging host=dev.teamsportsinfo.com apiUrl=https://devapi.teamsportsinfo.com/api ...`
- Authenticated request succeeded: `HTTP GET /api/jobs/tsic responded 200` — proves the rotated staging key signs and validates end-to-end against the recycled worker.

Caveat: the deploy went out on uncommitted working-directory edits. The build version stamp `f6506d1b` is the pre-edit master SHA. A commit is required before staging is treated as authoritative.

**Staging + Production re-verification — PASSED (2026-05-19, after `ec3988b0` deploy)**

Verified on .204 (TSIC-SEDONA, `dev-api` pool now `Staging`):
- `[STARTUP-CONFIG] host: env=Staging machine=TSIC-SEDONA isLiveProduction=false`
- `[STARTUP-CONFIG] adn: defaultMode=SANDBOX` — **issue 2 fix proof**
- End-to-end ADN sandbox charge: card `4242…` remapped to `4111…` by `MapSandboxTestCard`, posted to `apitest.authorize.net`, ADN sandbox approved with Auth Code `5NB4PP`, txn `120082914172`. Confirmed via receipt email from `noreply@mail.testauthorize.net` (sandbox sender — prod is `authorize.net`).

Verified on PHOENIX (TSIC-PHOENIX, `claude-api` pool):
- `[STARTUP-CONFIG] host: env=Production machine=TSIC-PHOENIX isLiveProduction=true`
- `[STARTUP-CONFIG] adn: defaultMode=PRODUCTION ses: sendGate=LIVE verticalInsure: live_… adnSweep: enabled=True hourLocal=5` — unchanged from prior production posture.
- Frontend build version stamp `v260519.1357.ec3988b0` matches the deployed commit hash; backend `env=Production` and frontend `env=production` are aligned.
- No synthetic CC test on PHOENIX — natural production traffic exercises the same code path that staging verified.

**Production gate — PASSED (2026-05-18, after `1-Build-And-Deploy-Prod.ps1` to TSIC-PHOENIX)**

Verified:
- Deploy shipped commit `c1ab9fc8` (this work). Build version stamp on the prod frontend confirmed: `v260518.1803.c1ab9fc8`.
- All eleven `[STARTUP-CONFIG]` lines emitted to `seq.teamsportsinfo.com` on PHOENIX boot at 18:05:39 (23ms burst):
  - `host: env=Production machine=TSIC-PHOENIX isLiveProduction=true`
  - `db: server=.\SS2016 database=TSICV5`
  - `jwt: issuer=TSIC.API audience=TSIC.Client signingKey=70tf...` **← rotation proof (matches prod key set on `claude-api` app-pool env var)**
  - `adn: defaultMode=PRODUCTION sandboxLoginIdFp=4d5m...` — correct for prod (this is where ADN PRODUCTION is supposed to fire; staging instance of same value is the bug)
  - `ses: sendGate=LIVE` — prod sends real email
  - `verticalInsure: hardcodedClientIdGate=live_... configClientIdFp=live...` — prod uses live VI client ID
  - `usLax: clientIdFp=9cc2...` — USLax creds resolved on PHOENIX
  - `frontend: baseUrl=https://claude-app.teamsportsinfo.com`
  - `seq: serverUrl=https://seq.teamsportsinfo.com`
  - `cors: origins=[localhost:4200, *.teamsportsinfo.com, teamsportsinfo.com]`
  - `adnSweep: enabled=True hourLocal=5` — prod 5 AM sweep enabled per `appsettings.Production.json`
- Frontend `[STARTUP-CONFIG]` line on `ai.teamsportsinfo.com`: `env=production host=ai.teamsportsinfo.com apiUrl=https://claude-api.teamsportsinfo.com/api ... build=v260518.1803.c1ab9fc8`. Hostname is an additional customer-branded prod surface (CLAUDE.md mentions only `claude-app.teamsportsinfo.com`; doc update needed).
- Login as `TSICSuperUser` on prod minted a valid HS256 accessToken (`iss=TSIC.API`, `aud=TSIC.Client`) — confirms the rotated prod key signs end-to-end on PHOENIX after pool recycle.

### Investigation status

**Headline finding — the original bruise (prod referencing dev endpoints, or dev referencing prod) is no longer present.** Each environment's `[STARTUP-CONFIG]` output today proved each is pointed at its own stack end-to-end:

| Env | Machine | apiUrl | frontend | ses.sendGate | vi.clientIdGate | adnSweep |
|---|---|---|---|---|---|---|
| Dev | TSIC-SEDONA | `localhost:7215` | `localhost:4200` | sandbox | test_... | unset |
| Staging | TSIC-SEDONA | `devapi.teamsportsinfo.com` | `dev.teamsportsinfo.com` | sandbox | test_... | False |
| Production | TSIC-PHOENIX | `claude-api.teamsportsinfo.com` | `claude-app.teamsportsinfo.com` | LIVE | live_... | True @5:00 |

No cross-stack references. No reused endpoints. And from this point forward, every boot emits this inventory to Seq (or browser console for dev), so any future misdeploy is visible within seconds rather than caught by humans after damage.

**Issue 1 (JWT signing key committed in plaintext) — CLOSED.** Each env rotated to its own independent key:
- Dev → `dotnet user-secrets` (fingerprint `PkuT...`)
- Staging → `dev-api` app-pool env var on TSIC-SEDONA (fingerprint `gNkV...`)
- Production → `claude-api` app-pool env var on TSIC-PHOENIX (fingerprint `70tf...`)

Committed value (in git history forever) is now unused on every box. `[STARTUP-CONFIG] jwt:` log line is the ongoing verification handle.

**Issue 2 (ADN gateway gated on `IsDevelopment()` — Staging hit ADN prod) — CLOSED 2026-05-19.** Gate corrected to `!_env.IsProduction()`; pool env on `dev-api` flipped to `Staging`; machine-name check removed from runtime gates and replaced with `ASPNETCORE_ENVIRONMENT must be set` startup assertion. End-to-end sandbox charge verified on .204 (txn `120082914172`, ADN sandbox receipt). PHOENIX behavior unchanged.

**Issue 3 (dead `Email:EnableSandboxMode` config + unused `EmailSettings.SandboxMode` property) — CLOSED 2026-05-19.** Dead config block deleted; unused property removed; `EmailHealthService.SandboxMode` field now sourced from `_env.IsSandbox()`. Zero runtime behavior change — email gating remains `_env.IsSandbox()` + per-call `sendInDevelopment` override.

**Issue 4 (two competing web.config deploy patterns + redundant regex patching) — CLOSED 2026-05-19.** One canonical `scripts/web.config.api` template (env-agnostic), five dead variants deleted, deploy scripts use it directly. `__ASPNET_ENV__` placeholder substitution gone (env name lives on pool env var). `appsettings.json`/`Production.json` regex patches in prod deploy removed — overlay already supplies E:\ paths, claude-app hostname, prod Seq URL. Zero runtime behavior change.

**Issue 5 (no DB connection-string override) — CLOSED 2026-05-19.** Explicit `ConnectionStrings:DefaultConnection` block added to `appsettings.Staging.json` and `appsettings.Production.json` with the current value. Zero runtime behavior change; per-env DB changes now have a visible editing point.

**Issue 9 (env overlays gitignored — no canonical recovery source) — CLOSED 2026-05-19.** Gitignore rules retired; `appsettings.{Development,Staging,Production}.json` committed to source. Secrets never lived in these files post-cleanup; remaining content is topology. `appsettings.Local.json` is the gitignored per-developer override. `CLAUDE.md` updated with the new config policy.

**Issue 6 (per-machine secret inventory unverified) — CLOSED 2026-05-19.** Boot block extended to fingerprint every required credential. Verified across .204 and PHOENIX: 13 secrets each, all resolve. Inventory is durable — every boot re-prints it.

**Issue 7 (USLax has no sandbox endpoint) — CLOSED 2026-05-19 as by-design.** Intentional: every env hits live USLax. Documented SANDBOX-RULE exception.

**Issue 8 (Production overlay thin / base as dangerous-default) — CLOSED 2026-05-19.** Functionally resolved by Issues 5+9. Residual `AllowedHosts: *` hardening left as future-work per user decision — preserves flexibility on hostname additions during go-live.

**All issues closed.** Investigation 001 complete.

### Verification expectations per stage

| Check | Dev | Staging | Production |
|---|---|---|---|
| `host.isLiveProduction` | False | False | True |
| `jwt.signingKey` fingerprint | `PkuT...` | `gNkV...` | `70tf...` |
| `adn.defaultMode` | SANDBOX | SANDBOX (issue 2 closed 2026-05-19) | PRODUCTION |
| `ses.sendGate` | sandbox | sandbox | LIVE |
| `verticalInsure.hardcodedClientIdGate` | test_... | test_... | live_... |
| `seq.serverUrl` | http://localhost:5341 | http://localhost:5341 (inherits base — see issue) | https://seq.teamsportsinfo.com |
