# Backend Architecture Rules

## Repository Pattern (MANDATORY)

All data access: Controller → Service → Repository → SqlDbContext

- Services depend on repository **interfaces** (in TSIC.Contracts), never implementations
- `AsNoTracking()` on all read-only queries
- 20+ repositories with 140+ optimized methods — search existing repos before creating new ones

## DTO Pattern

```csharp
// CORRECT — OpenAPI required-field detection works
public record MyDto { public required string Prop { get; init; } }

// WRONG — breaks OpenAPI generation
public record MyDto(string Prop);
```

- `required` keyword + `init` properties
- Object initializer syntax: `new MyDto { Prop = value }`
- NO positional records for DTOs

## DbContext Safety

NEVER use `Task.WhenAll` with multiple repo calls sharing the same scoped DbContext:

```csharp
// WRONG — InvalidOperationException (concurrent access)
var t1 = _repo.GetFooAsync(id, ct);
var t2 = _repo.GetBarAsync(id, ct);
await Task.WhenAll(t1, t2);

// CORRECT — sequential awaits
var foo = await _repo.GetFooAsync(id, ct);
var bar = await _repo.GetBarAsync(id, ct);
```

## JWT Claim Access

ASP.NET Core remaps standard JWT claim names. NEVER use raw strings:

```csharp
// WRONG — "sub" is remapped, returns null
var userId = User.FindFirst("sub")?.Value;

// CORRECT — use ClaimTypes constants
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
var role = User.FindFirst(ClaimTypes.Role)?.Value;
```

| JWT Claim | ASP.NET Remaps To |
|-----------|-------------------|
| `sub` | `ClaimTypes.NameIdentifier` |
| `role` | `ClaimTypes.Role` |
| Custom claims (`username`, `regId`, `jobPath`) | No remapping — use raw string |

## Clean Architecture Layers

```
API → Application → Infrastructure → Domain/Contracts
```

Strict unidirectional dependency flow. Services depend on interfaces, never implementations.
