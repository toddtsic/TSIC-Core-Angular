# TSIC-Core-Angular

.NET 8 backend + Angular 21 frontend. Standards live in [.claude/rules/](.claude/rules/) (auto-loaded every session) and `docs/`.

## Build & Workflow

```bash
# Regenerate frontend API models after backend changes
.\scripts\2-Regenerate-API-Models.ps1

# Deploy local (this box → dev.teamsportsinfo.com, Staging env)
.\scripts\1-Build-And-Deploy-Local.ps1

# Deploy prod (TSIC-PHOENIX → claude-app.teamsportsinfo.com, Production env)
.\scripts\1-Build-And-Deploy-Prod.ps1

# Routine commits
git commit --no-verify -m "message"

# Before major commits
dotnet format TSIC-Core-Angular.sln
dotnet build && dotnet test
```

## Environment Model

Three environments aligned across both stacks:

| Env | Backend (`ASPNETCORE_ENVIRONMENT`) | Frontend (`ng build -c`) | Where it runs |
|---|---|---|---|
| Development | `Development` | `development` | local `ng serve` + `dotnet run` |
| Staging | `Staging` | `staging` | this box → dev.teamsportsinfo.com |
| Production | `Production` | `production` | TSIC-PHOENIX → claude-app.teamsportsinfo.com |

Backend overlay = `appsettings.{EnvironmentName}.json`. Frontend overlay = Angular `fileReplacements` in `angular.json`. **No regex patching of source files at deploy time** — deploy scripts pass the configuration name and let the build system swap files.

**Config policy:** all env overlays (`Development`, `Staging`, `Production`) are committed to source. They contain topology only — connection strings, hostnames, file paths, feature toggles. **Secrets never live in appsettings**; they go on the IIS app pool's `environmentVariables` collection (provisioned by `IIS-Config-{Dev,Prod}/Setup/07-Apply-Secrets.ps1`). `appsettings.Local.json` is the gitignored per-developer override slot for local-only tweaks.

`ASPNETCORE_ENVIRONMENT` is set exclusively on the app pool's env-var collection. `Program.cs` refuses to start if it is unset — a forgotten pool var fails loud instead of silently inheriting ASP.NET's default `Production`.

## Auth Model

- Two-phase: (1) Username → role selection, (2) Username + regId + jobPath → JWT
- jobPath validated on every request; SuperUser exempt for cross-job ops
