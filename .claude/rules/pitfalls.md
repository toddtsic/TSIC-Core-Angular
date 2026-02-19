# Common Pitfalls Checklist

Before submitting code, verify:

1. **Repository violations**: Grep for `SqlDbContext` in services — must be ZERO
2. **Positional DTOs**: Search for `record.*\(.*\)` pattern — use `{ get; init; }` instead
3. **Hardcoded colors**: Search for hex codes `#[0-9a-f]{6}` — use CSS variables
4. **BehaviorSubject for state**: Replace with `signal<T>(initialValue)`
5. **Editing generated models**: Check file headers for "AUTO-GENERATED" warnings
6. **Absolute routerLinks**: Must be relative to preserve `:jobPath`
7. **DbContext concurrency**: No `Task.WhenAll` with shared DbContext — `await` sequentially
