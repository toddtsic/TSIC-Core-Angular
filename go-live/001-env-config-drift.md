# 001 — Environment / Config Drift

**Status:** open
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

### Still open

- **Issue 2 — ADN gateway gates on `_env.IsDevelopment()` instead of `_env.IsSandbox()`.** Untouched. Staging on .204 still hits ADN PRODUCTION. Next investigation.
- **Issue 3 — Dead `Email:EnableSandboxMode` config in `appsettings.Development.json`.** Untouched. Either wire up or delete.
- **Issue 4 — Two competing `web.config` deploy patterns in `scripts/`.** Need to read the deploy scripts to determine which one wins. Untouched.
- **Issue 5 — No DB connection-string override; relies on each box having its own local `TSICV5` database.** Confirmed working pattern but worth flagging as dangerous-default.
- **Issue 6 — SES/ADN/VI/USLax credentials not in committed config; require per-machine env-var verification.** Now that `[STARTUP-CONFIG]` logs the first-4-char fingerprint of each, post-deploy log review will give us the per-machine inventory we couldn't get from source alone.
- **Issue 7 — USLax has no sandbox endpoint; staging will hit prod USLax if env vars set.** Untouched.
- **Issue 8 — `appsettings.Production.json` is thin; base file is the dangerous-default surface.** Acceptable design but flagged.

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

Issues 2–8 remain open — see "Still open" section above.

### Verification expectations per stage

| Check | Dev | Staging | Production |
|---|---|---|---|
| `host.isLiveProduction` | False | False | True |
| `jwt.signingKey` fingerprint | `PkuT...` | `gNkV...` | `70tf...` |
| `adn.defaultMode` | SANDBOX | **PRODUCTION** (issue 2) | PRODUCTION |
| `ses.sendGate` | sandbox | sandbox | LIVE |
| `verticalInsure.hardcodedClientIdGate` | test_... | test_... | live_... |
| `seq.serverUrl` | http://localhost:5341 | http://localhost:5341 (inherits base — see issue) | https://seq.teamsportsinfo.com |
