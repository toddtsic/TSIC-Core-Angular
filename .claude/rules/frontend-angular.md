# Frontend Angular Rules

## Signal Architecture

- **Signals** for all component/domain state
- **Observables** for HTTP calls only — no BehaviorSubject for state
- Update signals in `.subscribe()` callbacks or `tap()` operators
- Template syntax: `user()` not `user`

## Signal Updates Are Immutable

NEVER mutate signal values in place — create a new value and `.set()` it. Mutation doesn't trigger reactivity; the UI won't update.

```typescript
// WRONG — mutates array, no reactivity
users().push(newUser);

// CORRECT — new array, triggers reactivity
users.set([...users(), newUser]);

// CORRECT — update pattern for objects
user.set({ ...user(), name: 'new' });
```

See `docs/Frontend/angular-signal-patterns.md` for the full pattern catalog.

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

## HTTP URLs

NEVER use `/api/...` in HTTP services. The Angular dev server (port 4200) returns `index.html` for unknown routes — you'll get 200 OK with HTML instead of JSON and waste hours debugging. Always use `` `${environment.apiUrl}/...` `` to hit the backend (port 7215).

## Routing

- NEVER use absolute routerLinks (`routerLink="/admin/..."`)
- Always use relative paths so `:jobPath` prefix is preserved
- Use `../../` to navigate up from nested routes
