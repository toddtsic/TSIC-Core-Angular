# TSIC-Core-Angular

.NET 8 backend + Angular 21 frontend. Standards live in [.claude/rules/](.claude/rules/) (auto-loaded every session) and `docs/`.

## Build & Workflow

```bash
# Regenerate frontend API models after backend changes
.\scripts\2-Regenerate-API-Models.ps1

# Routine commits
git commit --no-verify -m "message"

# Before major commits
dotnet format TSIC-Core-Angular.sln
dotnet build && dotnet test
```

## Auth Model

- Two-phase: (1) Username → role selection, (2) Username + regId + jobPath → JWT
- jobPath validated on every request; SuperUser exempt for cross-job ops
