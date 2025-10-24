# TSIC-Core-Angular — Setup Guide (6-step checklist)

This repository was reset to a clean baseline and scaffolded with a few project hygiene files. Below is a condensed checklist inspired by "6 steps for setting up a new .NET project the right way".

1) Project layout and solution: keep a simple layout, group backend/frontend/services under `src/` and centralize shared artifacts.
2) Versioning and package management: prefer small, explicit package updates and avoid mixing IDE auto-changes in commits.
3) Configuration and secrets: use appsettings.{Development}.json and environment variables; do not check secrets into source control.
4) Git hygiene: add `.editorconfig`, `.gitattributes`, and `.gitignore` (done).
5) CI and local dev experience: add lightweight scripts and VS Code tasks to run services locally.
6) Documentation: keep a short README and a quickstart guide (this file).

Local dev quickstart

- Start the backend (from repo root):
  ```powershell
  dotnet restore
  dotnet build
  dotnet run --project TSIC-Core-Angular/src/backend/TSIC.API/TSIC.API.csproj
  ```

- Start the frontend (from repo root):
  ```powershell
  cd TSIC-Core-Angular/src/frontend/tsic-app
  npm install
  npm start
  ```

If you want me to scaffold `tasks.json` / `launch.json` for VS Code to start both servers with a single click, tell me and I'll add them.


Container/Docker notes (removed from `master`)

Per project policy the repository no longer contains working Docker or Aspire scaffolding in `master`. If you need to run the application in containers later, the sample files are preserved on the `pre-rollback-backup` branch and can be restored selectively.

To restore container samples from the backup branch (PowerShell):

```powershell
git checkout pre-rollback-backup -- docker-compose.yml
git checkout pre-rollback-backup -- TSIC-Core-Angular/src/backend/TSIC.API/Dockerfile
```

If you want a separate branch with container samples (recommended to avoid cluttering `master`), I can create `container-samples` with a working `docker-compose.yml`, Dockerfiles, and a short README.

CI (GitHub Actions)

- A minimal GitHub Actions workflow has been added at `.github/workflows/ci.yml`. It:
  - runs on push/PR to `master`
  - builds the .NET solution, runs tests (if any), checks formatting with `dotnet format`, and builds the Angular app
  - uploads the backend build artifact

 - This project does not use Azure; CI is configured using GitHub Actions and publishing can be added to GHCR / GitHub Releases if desired.
