# Plan: HTTP Error Handling Safety Net

## Context

A 500 error on the `/family/players` endpoint caused the UI to show "No players on this account yet" -- a completely misleading empty state. The root cause: the interceptor only handles 0/401/403, and the service's error handler silently resets data with a `console.warn`. This pattern exists in ~14 places across the app. Registration wizards (where money is involved) are the worst offenders.

## Approach: Interceptor Safety Net + Opt-Out

Add a catch-all in the existing interceptor for unhandled 4xx/5xx errors that shows a toast. Services that already have their own error UI can opt out per-request using Angular's `HttpContext`.

This is a two-layer fix:
1. **Interceptor** (safety net) -- ensures users ALWAYS see something on server errors
2. **Service cleanup** (refinement) -- remove redundant `console.warn`s, add opt-outs where inline error UI exists

---

## Phase 1: New utility files (no dependencies)

### 1a. Create `infrastructure/interceptors/http-error-context.ts`
- Export `SKIP_GLOBAL_ERROR_TOAST = new HttpContextToken<boolean>(() => false)`
- Export helper: `skipErrorToast()` returns `new HttpContext().set(SKIP_GLOBAL_ERROR_TOAST, true)`
- This is NOT an interceptor -- it's a small utility file that lives near the interceptor because it's consumed by it. Services import the token to opt out of global error toasts on specific requests.

### 1b. Create `infrastructure/interceptors/http-error-utils.ts`
- Export `extractHttpErrorMessage(error: unknown, fallback?: string): string`
- Extracts `error.error?.detail || error.error?.title || error.error?.message || fallback`
- Reusable by interceptor and any component that opts out and handles its own messaging

## Phase 2: Extend the existing interceptor

### File: `infrastructure/interceptors/auth.interceptor.ts`

In `handleRequest`'s existing `catchError`, after the 0/401/403 blocks, before the final `return throwError`:

```typescript
if (!request.context.get(SKIP_GLOBAL_ERROR_TOAST)) {
    if (error.status >= 500) {
        toastService.show('Something went wrong. Please try again or contact support.', 'danger', 7000);
    } else if (error.status >= 400) {
        const msg = extractHttpErrorMessage(error, 'The request could not be completed.');
        toastService.show(msg, 'warning', 5000);
    }
}
```

Always re-throws so downstream `.subscribe({ error })` still runs for state cleanup.

**After this phase alone, the app is already better** -- every silent 500 now shows a toast.

## Phase 3: Fix Tier 1 -- Registration Wizard services

### 3a. `views/registration/player/state/family-players.service.ts`
- `handleError()` (line 190): Remove `console.warn`. Keep the signal resets (loading, players, user, etc.) -- that's correct state cleanup. The interceptor now handles the toast.

### 3b. `views/registration/player/state/job-context.service.ts`
- Line 181: Remove `console.error`. Interceptor handles toast.
- Add `_metadataError = signal<string | null>(null)` + readonly accessor for optional future inline UI.

### 3c. `views/registration/player/state/player-wizard-state.service.ts`
- `loadConfirmation` (line 231): Remove `console.warn`. Keep `_confirmation.set(null)`. Interceptor toasts.
- `resendConfirmationEmail` (line 243): This returns a boolean. The calling component checks the return value for UX. Add `context: skipErrorToast()` on the HTTP call since the component handles it. Remove `console.warn`.

### 3d. `views/registration/player/services/registration-wizard.service.ts`
- `handleFamilyPlayersError` (line 320): Remove `console.warn`. Keep signal resets. Interceptor toasts.
- Job metadata error (line 506): Remove `console.error`. Interceptor toasts.
- `resendConfirmationEmail` (line 965): Same as 3c -- add `skipErrorToast()`, component handles.

## Phase 4: Fix Tier 2 -- Team Registration

### 4a. `views/registration/team/team.component.ts`
- `initAndAdvance` error (line 258): Remove `console.error`. Keep the logout+reset-to-step-0 logic. Interceptor toasts.

### 4b. `views/registration/team/state/team-wizard-state.service.ts`
- Line 60: Already sets `metadataError` signal (inline UI). Add `skipErrorToast()` on the HTTP call to avoid double-messaging. Remove `console.error`.

## Phase 5: Fix Tier 3 -- Admin/Search (lower priority, can be separate commit)

### 5a. Intentionally-silent handlers -- add opt-out
These already have comments documenting intent. After Phase 2 the interceptor would start toasting them. Add `skipErrorToast()`:
- `team/steps/review-step.component.ts` line 84 (auto-send email)
- `adult/state/adult-wizard-state.service.ts` line 246 (non-critical post-success)
- `search-registrations.component.ts` line 295 (optional CADT tree)

### 5b. `search-teams.component.ts` -- LADT tree errors
- Let interceptor toast (tree load failure IS user-relevant).

### 5c. `nav-editor.component.ts`
- Audit each of the ~8 error handlers. CRUD ops: let interceptor toast. Load ops with inline error UI: add `skipErrorToast()`.

---

## Files Changed

| File | Change |
|---|---|
| `infrastructure/interceptors/http-error-context.ts` | **NEW** -- HttpContextToken + helper (not an interceptor) |
| `infrastructure/interceptors/http-error-utils.ts` | **NEW** -- error message extraction utility |
| `infrastructure/interceptors/auth.interceptor.ts` | Add 4xx/5xx catch-all to existing interceptor |
| `views/registration/player/state/family-players.service.ts` | Remove console.warn from handleError |
| `views/registration/player/state/job-context.service.ts` | Remove console.error, add error signal |
| `views/registration/player/state/player-wizard-state.service.ts` | Remove console.warns, add skipErrorToast on resend |
| `views/registration/player/services/registration-wizard.service.ts` | Remove console.warn/error |
| `views/registration/team/team.component.ts` | Remove console.error |
| `views/registration/team/state/team-wizard-state.service.ts` | Add skipErrorToast, remove console.error |
| `views/registration/team/steps/review-step.component.ts` | Add skipErrorToast on auto-send |
| `views/registration/adult/state/adult-wizard-state.service.ts` | Add skipErrorToast on non-critical |

## Verification

1. **500 test**: Stop the backend, trigger a player registration load. Confirm toast appears instead of silent empty state.
2. **Opt-out test**: Trigger a resend-confirmation failure. Confirm only the component's inline message appears, no duplicate toast.
3. **Intentionally-silent test**: Verify auto-send email failure (team review step) does NOT show a toast.
4. **Existing behavior**: Confirm 401 (token refresh), 403 (permission), and network-down toasts still work unchanged.
5. **Build**: `ng build` with zero errors.
