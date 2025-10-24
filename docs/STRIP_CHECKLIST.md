# Strip Checklist — Minimal repo (no Docker/Aspire)

This file documents the exact files removed to keep the repository minimal and how to restore container samples later.

What was removed (committed to `master`):

- `docker-compose.yml` (repo root) — local compose orchestration for api/db/seq.
- `TSIC-Core-Angular/src/backend/TSIC.API/Dockerfile` — Dockerfile for building the API image.
- `tools/aspire/*` — Aspire scaffolding and example compose files.
- `GitVersion.yml` — GitVersion configuration used previously for automated versioning.

Why:
- The project owner prefers a host-first development workflow (IIS/Kestrel + local SQL). Removing rarely-used container scaffolding keeps the repo small and the dev loop fast.

How to restore later:
- All removed files remain available on the `pre-rollback-backup` branch. To restore a specific file, run from the repo root (PowerShell):

```powershell
# restore a single file:
git checkout pre-rollback-backup -- docker-compose.yml
# restore a folder:
git checkout pre-rollback-backup -- tools/aspire
```

Checklist to reintroduce containers later (high level):
1. Reintroduce `Dockerfile`(s) and `docker-compose.yml` from the backup branch.
2. Replace secrets with environment variables and add `.env.example`.
3. Add a `docker-compose.override.yml` that uses volumes for dev (optional).
4. Update CI to build/publish images (create a separate workflow or gate behind tags).
5. Smoke-test `docker compose up --build` and verify Swagger + DB migrations.

Notes:
- If you want, we can create a separate branch `container-samples` containing a working Docker + Compose setup that won't affect `master` until you explicitly merge it.
- This file is intentionally short and actionable; expand it if you want more examples or templates.
