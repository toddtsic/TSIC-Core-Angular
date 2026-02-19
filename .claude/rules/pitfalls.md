# Common Pitfalls Checklist

Before submitting code, verify:

1. **Repository violations**: Grep for `SqlDbContext` in services — must be ZERO
2. **Positional DTOs**: Search for `record.*\(.*\)` pattern — use `{ get; init; }` instead
3. **Hardcoded colors**: Search for hex codes `#[0-9a-f]{6}` — use CSS variables
4. **BehaviorSubject for state**: Replace with `signal<T>(initialValue)`
5. **Editing generated models**: Check file headers for "AUTO-GENERATED" warnings
6. **Absolute routerLinks**: Must be relative to preserve `:jobPath`
7. **DbContext concurrency**: No `Task.WhenAll` with shared DbContext — `await` sequentially
8. **JWT claim names**: NEVER use `User.FindFirst("sub")` — ASP.NET remaps `"sub"` to `ClaimTypes.NameIdentifier`. Always use `User.FindFirst(ClaimTypes.NameIdentifier)`
9. **Breadcrumb sync**: `ROUTE_TITLE_MAP` must match routes
10. **Relative API URLs**: NEVER use `/api/...` in HTTP services — the Angular dev server (port 4200) returns `index.html` for unknown routes, giving 200 OK with HTML instead of JSON. Always use `` `${environment.apiUrl}/...` `` to hit the backend (port 7215)
