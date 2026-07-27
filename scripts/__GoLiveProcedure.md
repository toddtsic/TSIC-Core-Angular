# TSIC Cutover — IIS Manager. Open `inetmgr` as admin on PHOENIX.

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

**Rollback:** claude-app Bindings → Edit both rows, restore host `claude-app.teamsportsinfo.com` (re-check Require SNI on 443). Legacy Bindings → Add blank http+https (cert = wildcard, SNI unchecked); Start Automatically = True; start pool + site.

*(DB cutover is your Step 0, before this. Deploy scripts need no changes.)*
