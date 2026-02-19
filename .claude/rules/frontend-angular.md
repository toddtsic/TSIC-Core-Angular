# Frontend Angular Rules

## Signal Architecture

- **Signals** for all component/domain state
- **Observables** for HTTP calls only — no BehaviorSubject for state
- Update signals in `.subscribe()` callbacks or `tap()` operators
- Template syntax: `user()` not `user`

## Modern Patterns

- 100% standalone components
- `@if` / `@for` (not `*ngIf` / `*ngFor`)
- OnPush change detection on all components
- `inject()` function (not constructor injection)

## Auto-Generated API Models (STRICT)

- NEVER edit files in `src/app/core/api/models/`
- NEVER create local TypeScript type definitions for backend DTOs — not even "temporary" ones
- ALWAYS run `.\scripts\2-Regenerate-API-Models.ps1` BEFORE writing frontend code using new/changed DTOs
- Import from `@core/api` only — never duplicate locally
- Check for stale model folders after major refactoring

## Routing

- NEVER use absolute routerLinks (`routerLink="/admin/..."`)
- Always use relative paths so `:jobPath` prefix is preserved
- Use `../../` to navigate up from nested routes
