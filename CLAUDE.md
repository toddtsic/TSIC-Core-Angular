# TSIC-Core-Angular

## Architecture

```
Controller → Service → Repository → SqlDbContext
```

- NEVER use `SqlDbContext` in controllers or services — repository pattern only
- NEVER use `Task.WhenAll` with multiple repo calls (DbContext is not thread-safe) — `await` sequentially
- DTOs: `public record MyDto { public required string Prop { get; init; } }` — NO positional records
- Use `AsNoTracking()` for all read-only queries

## Frontend (Angular 21)

- Signals for state, Observables for HTTP only (no BehaviorSubject for state)
- Standalone components, `@if`/`@for`, OnPush, `inject()` function
- NEVER edit files in `src/app/core/api/models/` — auto-generated
- NEVER create local TypeScript type definitions for backend DTOs
- Run `.\scripts\2-Regenerate-API-Models.ps1` BEFORE writing frontend code that uses new/changed DTOs
- Import models from `@core/api` only

## Design System

- NO hardcoded colors, spacing, or shadows — use CSS variables only
- 8px spacing grid: `--space-1` through `--space-20`
- 8 dynamic palettes — test all when changing styles
- WCAG AA (4.5:1 contrast minimum)

## Routing

- NEVER use absolute routerLinks (`routerLink="/admin/..."`) — always relative, so `:jobPath` is preserved

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

## CWCC Prefix

Type "CWCC" before coding requests to ensure full compliance with all conventions.

## Key Reference Docs

- @docs/Standards/AI-AGENT-CODING-CONVENTIONS.md
- @docs/Standards/REPOSITORY-PATTERN-STANDARDS.md
- @docs/Standards/WIDGET-COMPONENT-TAXONOMY.md
- @docs/DesignSystem/DESIGN-SYSTEM.md
- @docs/Frontend/angular-signal-patterns.md

## Auth Model

- Two-phase: (1) Username → role selection, (2) Username + regId + jobPath → JWT
- jobPath validated on every request; SuperUser exempt for cross-job ops
