# Frontend Angular Rules

## Signal Architecture

- **Signals** for all component/domain state
- **Observables** for HTTP calls and event streams — no BehaviorSubject for state
- Update signals in `.subscribe()` callbacks or `tap()` operators
- Template syntax: `user()` not `user`

## BANNED: `effect()` (NEVER USE)

`effect()` is **permanently banned** from this codebase. Do not import it from `@angular/core`.

**Why**: an effect re-runs whenever any signal it read changes. When it also writes a signal it
transitively reads, it re-triggers itself and reverts the write a frame later — silently. This
wiped user edits in the registration detail panel (fixed in `02e8bafd`), and a codebase-wide
sweep found it had also produced a theme editor whose reload never fired, an HTTP check racing
on every keystroke, a caret that jumped to the end of every field being edited, and an inert
public setter. The replacements below are *structural*: a `computed()` cannot write what it
reads, and a `linkedSignal` reseeds only when its `source` changes.

**Alternatives**, by the job the effect was doing:

| Job | Use |
|---|---|
| Pure derivation (incl. clamping) | `computed()` |
| Editable copy seeded from an `input()` | `linkedSignal({ source, computation })` |
| React to an `@Input`/`input()` change | `ngOnChanges` gated on `changes['key']` |
| React to a **service** signal | `toObservable(sig).pipe(…, takeUntilDestroyed()).subscribe()` |
| Fire HTTP on a user action | An explicit callback |
| Debounced typeahead → HTTP | `Subject` + `debounceTime` + `distinctUntilChanged` + `switchMap` |
| DOM work needing the rendered view | `ngOnChanges` sets a flag → `ngAfterViewChecked` drains it |
| One-shot cross-component command | `Subject<void>` — never a `signal(false)` pulse |

Debounce belongs at the **keystroke source**, never piped onto the per-request `http.get`
(which emits once and completes, so `debounceTime` there only delays the response).

See `docs/Frontend/angular-signal-patterns.md` Pattern 6.

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
