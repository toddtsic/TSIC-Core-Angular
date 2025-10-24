# Aspire (scaffold)

This folder holds a cleaned, minimal scaffold for Aspire orchestration artifacts.
It intentionally contains only configuration and documentation — not generated binaries or runtime outputs.

When to use this folder
- If you want to re-introduce Aspire-based orchestration for local or cloud runs, place only the config files here (YAML/TOML/etc.).
- Keep generated outputs excluded from git; see `.gitignore` entries in the repo root.

Included files
- `example-aspire.yml` — an example Aspire orchestration config with comments.
- `docker-compose.aspire.yml` — a small docker-compose snippet you can use or adapt when running Aspire locally.

How to restore original Aspire artifacts from the backup branch
If you want the original files that were present before the rollback, they are preserved in the `pre-rollback-backup` branch.
To restore them selectively (replace `path/to/file` with the actual path):

```powershell
git checkout pre-rollback-backup -- path/to/file
```

Recommendation
- Prefer the repository-level `docker-compose.yml` at the repo root for local development (it already contains API + SQL Server + Seq). Use Aspire only if you need the additional automation/features it provides.

If you want me to selectively restore files from the backup branch into this folder, say which paths to pull and I'll do it.

Run the Aspire compose scaffold
--------------------------------
If you want to run the example docker-compose in this folder, there are wrappers you can use:

Windows (PowerShell):

```powershell
.
\scripts\run-aspire.ps1
```

macOS / Linux / Git Bash:

```bash
./scripts/run-aspire.sh
```

These scripts look for `tools/aspire/docker-compose.aspire.yml` and run `docker compose -f <file> up --build`.
Make sure Docker is installed and running before executing them.

Aspire integration notes
=======================

This folder is reserved for Aspire orchestration artifacts if you choose to re-integrate Aspire.

Current repo status:
- We replaced the previous Aspire-generated orchestration with a `docker-compose.yml` for local development (API + SQL Server + Seq).
- The original pre-change state (including any Aspire files) is preserved in the `pre-rollback-backup` branch.

If you want to restore Aspire files from the backup branch:

```powershell
git checkout pre-rollback-backup -- path/to/aspire-folder
git switch -
```

Recommendation:
- Keep Aspire orchestration under `tools/aspire` and check in only configuration and not generated artifacts.
- Prefer docker-compose for reproducible local dev; keep Aspire for integrations that require it.
