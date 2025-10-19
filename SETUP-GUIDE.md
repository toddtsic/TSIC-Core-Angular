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
