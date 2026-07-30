# TSIC Cutover — IIS Manager. Open `inetmgr` as admin on PHOENIX.

**0. Database — before any IIS work**
- DB cutover.
- **Seed fees from legacy** — on PHOENIX:
  `sqlcmd -S "lpc:.\SS2016" -d TSICV5 -I -b -f 65001 -i "scripts/6a) seed-fees-from-legacy.sql"`
  Idempotent: clears `fees.JobFees` + `fees.FeeModifiers`, then repopulates from the legacy Agegroup/Team fee columns. Safe to re-run.
- Verify the seed: `6b) verify-fees-feebase-concordance.sql`, then `6c) verify-fees-concordance.sql`.

**1. Stop legacy**
- Application Pools → *legacy pool* → **Stop**
- Sites → **TSIC-Unify-2024** → Manage Website → **Stop**

**2. Remove legacy's bindings**
- TSIC-Unify-2024 → **Bindings…** → select each → **Remove** → all gone → Close.

**3. claude-app → catch-all** (edit in place, keeps the cert)
- claude-app → **Bindings…**
- **https :443** row → **Edit** → clear **Host name** → uncheck **Require SNI** → OK
- **http :80** row → **Edit** → clear **Host name** → OK
- Restart the claude-app site.

**4. Verify (external device, not this box)**
- `https://claude-app.teamsportsinfo.com/` → login, open a director view, real data
- `https://<any-other>.teamsportsinfo.com/` → new app
- `https://claude-api.teamsportsinfo.com/api/jobs/tsic` → JSON
- legacy URL → dead

**5. Lock legacy down**
- Sites → TSIC-Unify-2024 → Advanced Settings → **Start Automatically = False**
- App Pools → legacy pool → Advanced Settings → **Start Mode = OnDemand**

**5b. Retire Crystal Reports site** (CR is retired; only legacy's backend ever called it — server-to-server to `cr2025.*`)
- Sites → **TSIC-CR-2025** → Manage Website → **Stop**; Advanced Settings → **Start Automatically = False**
- App Pools → its pool → **Stop**; Advanced Settings → **Start Mode = OnDemand**
- If `cr2025.teamsportsinfo.com` is pinned in the box's hosts file from a past DNS outage, remove the pin.

**6. Move the ADN offline-tx sweep 05:00 → 04:00** — *cutover day only; legacy owns the 4:00 slot until it's stopped. No redeploy — edit BOTH copies, then recycle:*
- **Deployed copy** (PHOENIX): `appsettings.Production.json` in the claude-api site folder → `AdnSweep:SweepHourLocal: 5` → **`4`**.
- **Repo copy**: same edit in `src/backend/TSIC.API/appsettings.Production.json`; commit — next deploy re-asserts the same value, zero drift.
- **Recycle the claude-api app pool** — mandatory: the service snapshots the option and computes its timer at startup; the file edit alone changes nothing.
- **Retime the warmup task**: Task Scheduler (`taskschd.msc`) → `TSIC-ClaudeApi-Warmup` → Properties → Triggers → Edit → **3:55 AM**. (Script default already updated to 3:55 — a future re-run of `00-Register-AdnSweep-Warmup-Task-PRODUCTION.ps1` re-creates it correctly.)
- Verify next morning: `[claude-api] AdnSweep …` digest email after the 04:00 run; `SELECT TOP 5 * FROM echeck.SweepLog ORDER BY startedAt DESC`.

**Rollback:** if step 6 already ran, revert `SweepHourLocal` to `5` (both copies) + recycle — legacy reclaims the 4:00 slot. Then: claude-app Bindings → Edit both rows, restore host `claude-app.teamsportsinfo.com` (re-check Require SNI on 443). Legacy Bindings → Add blank http+https (cert = wildcard, SNI unchecked); Start Automatically = True; start pool + site.

*(Deploy scripts need no changes.)*
