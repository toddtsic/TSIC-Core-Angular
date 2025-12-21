# Repository Pattern & Clean Architecture Standards

## Purpose
This document establishes **mandatory architectural standards** for all data access in the TSIC application. All developers and AI agents **MUST** comply with these patterns to ensure consistency, maintainability, and testability as the application scales.

---

## Core Principle

**Controllers and Services MUST NEVER directly reference `SqlDbContext` or any EF Core types.**

All data access flows through repositories:
```
Controller → Service → Repository → SqlDbContext → Database
```

---

## Layer Responsibilities

### 1. Controllers (`TSIC.API/Controllers`)
**Responsibilities**:
- HTTP request/response handling
- Authorization checks
- Input validation
- Call services or repositories
- Return appropriate HTTP status codes

**PROHIBITED**:
- ❌ Direct `SqlDbContext` injection or usage
- ❌ LINQ queries against database entities
- ❌ Any `using Microsoft.EntityFrameworkCore;`
- ❌ Any `.Include()`, `.AsNoTracking()`, or EF Core methods

**Example - WRONG**:
```csharp
public class FamilyController : ControllerBase
{
    private readonly SqlDbContext _db; // ❌ NEVER DO THIS

    public async Task<IActionResult> GetFamily(string id)
    {
        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == id); // ❌ NEVER
        return Ok(family);
    }
}
```

**Example - CORRECT**:
```csharp
public class FamilyController : ControllerBase
{
    private readonly IFamilyRepository _familyRepo; // ✅ Repository abstraction

    public async Task<IActionResult> GetFamily(string id)
    {
        var family = await _familyRepo.GetByIdAsync(id); // ✅ Clean abstraction
        return Ok(family);
    }
}
```

---

### 2. Services (`TSIC.API/Services`, `TSIC.Application`)
**Responsibilities**:
- Business logic
- Complex orchestration across multiple repositories
- Data transformation and business rule enforcement
- Return DTOs (not entities)

**PROHIBITED**:
- ❌ Direct `SqlDbContext` injection or usage
- ❌ Any EF Core LINQ operations

**When to use**:
- Complex business rules that span multiple entities
- Workflows requiring multiple repository calls
- Business calculations or validations

**Example - CORRECT**:
```csharp
public class PaymentService : IPaymentService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IEmailService _emailService;

    public async Task<PaymentResultDto> ProcessPaymentAsync(PaymentRequestDto request)
    {
        // Get registrations
        var registrations = await _registrationRepo.GetByIdsAsync(request.RegistrationIds);
        
        // Apply business rules
        var totalAmount = CalculateTotalWithDiscounts(registrations, request.DiscountCode);
        
        // Process payment
        var payment = await _paymentRepo.CreatePaymentAsync(totalAmount, request.CreditCard);
        
        // Send notification
        await _emailService.SendPaymentConfirmationAsync(payment);
        
        return new PaymentResultDto { Success = true, TransactionId = payment.Id };
    }
}
```

---

### 3. Repositories (`TSIC.Infrastructure/Repositories`)
**Responsibilities**:
- **ALL** data access logic
- EF Core queries, joins, includes
- Return entities OR DTOs (depending on use case)
- Encapsulate complex LINQ queries

**Structure**:
```
TSIC.Contracts/Repositories/      ← Interfaces (abstractions)
TSIC.Infrastructure/Repositories/  ← Implementations (concrete classes)
```

**REQUIRED**:
- ✅ One interface per entity (e.g., `IRegistrationRepository`)
- ✅ One implementation per interface (e.g., `RegistrationRepository`)
- ✅ Must inject `SqlDbContext` (ONLY place this is allowed)
- ✅ All public methods must be async
- ✅ All must have CancellationToken parameter (default)

---

## Repository Implementation Patterns

### Pattern 1: Simple Entity Retrieval

**When to use**: Single entity lookups by ID or simple filters

```csharp
// Interface
public interface IFamilyRepository
{
    Task<Families?> GetByIdAsync(string familyId, CancellationToken cancellationToken = default);
    Task<Families?> GetByFamilyUserIdAsync(string familyUserId, CancellationToken cancellationToken = default);
}

// Implementation
public class FamilyRepository : IFamilyRepository
{
    private readonly SqlDbContext _context;

    public FamilyRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<Families?> GetByIdAsync(
        string familyId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.FamilyId == familyId, cancellationToken);
    }

    public async Task<Families?> GetByFamilyUserIdAsync(
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.FamilyUserId == familyUserId, cancellationToken);
    }
}
```

---

### Pattern 2: Parameterized Queries with Optional Filters

**When to use**: Queries that support multiple filter combinations

```csharp
// Interface
public interface IRegistrationRepository
{
    Task<List<Registrations>> GetByUserIdAsync(
        string userId,
        bool includeJob = false,
        bool includeRole = false,
        bool activeOnly = false,
        string? roleIdFilter = null,
        CancellationToken cancellationToken = default);
}

// Implementation
public async Task<List<Registrations>> GetByUserIdAsync(
    string userId,
    bool includeJob = false,
    bool includeRole = false,
    bool activeOnly = false,
    string? roleIdFilter = null,
    CancellationToken cancellationToken = default)
{
    var query = _context.Registrations.AsQueryable();

    // Apply filters
    query = query.Where(r => r.UserId == userId);
    
    if (activeOnly)
    {
        query = query.Where(r => r.BActive == true);
    }
    
    if (roleIdFilter != null)
    {
        query = query.Where(r => r.RoleId == roleIdFilter);
    }

    // Apply includes
    if (includeJob)
    {
        query = query.Include(r => r.Job);
    }
    
    if (includeRole)
    {
        query = query.Include(r => r.Role);
    }

    return await query
        .AsNoTracking()
        .ToListAsync(cancellationToken);
}
```

---

### Pattern 3: Complex Multi-Entity Query Returning DTO

**When to use**: Cross-entity joins with specific data needs (MOST COMMON FOR COMPLEX QUERIES)

**CRITICAL**: For queries spanning 3+ entities, **return a DTO directly from the repository**. Do NOT return full entities with complex navigation properties.

```csharp
// Define DTO in TSIC.Contracts/Dtos/
public record RegistrantWithClubRepDto(
    string RegistrantName,
    decimal RegistrantBalance,
    string TeamName,
    string ClubRepName,
    decimal ClubRepBalance);

// Interface
public interface IRegistrationRepository
{
    Task<RegistrantWithClubRepDto?> GetRegistrantWithClubRepAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default);
}

// Implementation - Encapsulate entire complex query
public async Task<RegistrantWithClubRepDto?> GetRegistrantWithClubRepAsync(
    Guid registrationId,
    CancellationToken cancellationToken = default)
{
    return await (
        from r in _context.Registrations
        join ra in _context.RegistrationAccounting on r.RegistrationId equals ra.RegistrationId
        join t in _context.Teams on r.AssignedTeamId equals t.TeamId
        join clubRepReg in _context.Registrations 
            on new { t.ClubRepId, JobId = r.JobId } 
            equals new { ClubRepId = clubRepReg.UserId, clubRepReg.JobId }
        join clubRepAcct in _context.RegistrationAccounting 
            on clubRepReg.RegistrationId equals clubRepAcct.RegistrationId
        where r.RegistrationId == registrationId
        select new RegistrantWithClubRepDto(
            RegistrantName: $"{r.User.FirstName} {r.User.LastName}",
            RegistrantBalance: ra.Balance ?? 0m,
            TeamName: t.TeamName ?? string.Empty,
            ClubRepName: $"{clubRepReg.User.FirstName} {clubRepReg.User.LastName}",
            ClubRepBalance: clubRepAcct.Balance ?? 0m
        )
    ).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
}
```

**Why return DTO from repository?**
- ✅ Single optimized database query
- ✅ No circular reference issues
- ✅ No over-fetching of data
- ✅ Controller/Service code stays simple
- ✅ Repository owns all EF Core complexity

---

### Pattern 4: Raw Queryable Access (Advanced)

**When to use**: Rare cases where dynamic filtering is needed

```csharp
public interface IRegistrationRepository
{
    IQueryable<Registrations> Query();
}

public IQueryable<Registrations> Query()
{
    return _context.Registrations.AsQueryable();
}
```

**WARNING**: Only use `Query()` in services, never in controllers. Service layer must still abstract this from controllers.

---

## Naming Conventions

### Repository Interfaces
```
I<Entity>Repository
```
Examples: `IRegistrationRepository`, `IFamilyRepository`, `IPaymentRepository`

### Repository Implementations
```
<Entity>Repository
```
Examples: `RegistrationRepository`, `FamilyRepository`, `PaymentRepository`

### Repository Methods - Naming Pattern
```
Get<Entity><Criteria>Async
```

**Examples**:
- `GetByIdAsync(Guid id)`
- `GetByUserIdAsync(string userId)`
- `GetActiveForJobAsync(Guid jobId)`
- `GetExpiredBetweenDatesAsync(DateTime start, DateTime end)`
- `GetPlayerRegistrationsAsync(string familyUserId)` ← Domain-specific name OK

**Command Methods**:
- `AddAsync(TEntity entity)`
- `UpdateAsync(TEntity entity)`
- `RemoveAsync(TEntity entity)`
- `SaveChangesAsync()` ← Only if unit of work pattern not used

---

## Dependency Injection Registration

**Location**: `TSIC.API/Program.cs`

**Pattern** (place BEFORE services):
```csharp
// Infrastructure Repositories (ALWAYS SCOPED)
builder.Services.AddScoped<IRegistrationRepository, RegistrationRepository>();
builder.Services.AddScoped<IFamilyRepository, FamilyRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITeamRepository, TeamRepository>();

// Application & Infrastructure Services
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPlayerRegistrationService, PlayerRegistrationService>();
// ... etc
```

**Rules**:
- ✅ Always use `AddScoped` for repositories (per-request lifetime)
- ✅ Register repositories BEFORE services (logical ordering)
- ✅ One line per repository (readability)
- ✅ Alphabetical within repositories section (maintainability)

---

## Decision Matrix: Where Should Logic Go?

| Scenario | Location | Reason |
|----------|----------|--------|
| Simple ID lookup | Repository method | Pure data access |
| Filter by single field | Repository method | Pure data access |
| Multi-entity join (3+ tables) | Repository method returning DTO | Encapsulate complexity, single query |
| Business calculation | Service | Business logic, not data access |
| Email sending | Service | Side effect, not data access |
| Authorization check | Controller | HTTP concern |
| Multi-step workflow | Service calling multiple repos | Orchestration |
| Dynamic filtering with many combinations | Repository `Query()` method + Service composition | Flexibility needed |

---

## Anti-Patterns (NEVER DO THIS)

### ❌ Anti-Pattern 1: SqlDbContext in Controller
```csharp
public class FamilyController : ControllerBase
{
    private readonly SqlDbContext _db; // ❌ WRONG
    
    public async Task<IActionResult> Get(string id)
    {
        var family = await _db.Families.FirstOrDefaultAsync(f => f.Id == id); // ❌ WRONG
        return Ok(family);
    }
}
```
**Fix**: Inject `IFamilyRepository`, call `GetByIdAsync(id)`

---

### ❌ Anti-Pattern 2: Generic Repository
```csharp
public interface IRepository<T>
{
    Task<T?> GetByIdAsync(Guid id);
    Task<List<T>> GetAllAsync();
}
```
**Why wrong**: No type-specific query methods, forces complex logic into service layer, defeats purpose of encapsulation.

**Fix**: Create entity-specific repositories with domain-meaningful methods.

---

### ❌ Anti-Pattern 3: Returning Full Entities from Complex Queries
```csharp
public async Task<Registrations> GetRegistrationWithAllRelations(Guid id)
{
    return await _context.Registrations
        .Include(r => r.Job)
            .ThenInclude(j => j.JobDisplayOptions)
        .Include(r => r.User)
        .Include(r => r.Team)
            .ThenInclude(t => t.Club)
        .Include(r => r.Role)
        .FirstOrDefaultAsync(r => r.RegistrationId == id); // ❌ Circular refs, over-fetching
}
```
**Fix**: Return a purpose-built DTO with only needed fields.

---

### ❌ Anti-Pattern 4: Business Logic in Repository
```csharp
public async Task<decimal> CalculatePlayerBalanceWithDiscountsAsync(Guid registrationId)
{
    var reg = await _context.Registrations
        .Include(r => r.Accounting)
        .FirstOrDefaultAsync(r => r.RegistrationId == registrationId);
    
    // ❌ BUSINESS LOGIC IN REPOSITORY
    var discount = reg.Accounting.Discount ?? 0m;
    var total = reg.Accounting.Amount - discount;
    if (reg.IsEarlyBird) total *= 0.9m;
    
    return total;
}
```
**Fix**: Repository returns data, service performs calculation.

---

## Testing Implications

### Repository Tests
- Test against **in-memory database** or **real test database**
- Verify LINQ queries produce correct results
- Test edge cases (null handling, empty results)

```csharp
[Fact]
public async Task GetByUserIdAsync_ReturnsMatchingRegistrations()
{
    // Arrange
    var options = new DbContextOptionsBuilder<SqlDbContext>()
        .UseInMemoryDatabase(databaseName: "TestDb")
        .Options;
    
    using var context = new SqlDbContext(options);
    var repo = new RegistrationRepository(context);
    
    // Add test data
    context.Registrations.Add(new Registrations { UserId = "user1", ... });
    await context.SaveChangesAsync();
    
    // Act
    var results = await repo.GetByUserIdAsync("user1");
    
    // Assert
    Assert.Single(results);
}
```

### Service Tests
- Mock repositories
- Test business logic in isolation
- No database required

```csharp
[Fact]
public async Task ProcessPayment_AppliesDiscountCorrectly()
{
    // Arrange
    var mockRepo = new Mock<IRegistrationRepository>();
    mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
        .ReturnsAsync(new Registrations { Amount = 100m });
    
    var service = new PaymentService(mockRepo.Object);
    
    // Act
    var result = await service.ProcessPaymentAsync(...);
    
    // Assert
    Assert.Equal(90m, result.FinalAmount); // 10% discount applied
}
```

### Controller Tests
- Mock services/repositories
- Test HTTP concerns only
- No database required

---

## Migration Strategy for Existing Code

When encountering code that violates these standards:

1. **Identify the violation** (SqlDbContext in controller/service)
2. **Create repository interface** in `TSIC.Contracts/Repositories/`
3. **Create repository implementation** in `TSIC.Infrastructure/Repositories/`
4. **Extract LINQ queries** from controller/service into repository methods
5. **Register in DI** (Program.cs)
6. **Refactor controller/service** to use repository
7. **Remove SqlDbContext** reference from controller/service
8. **Build and verify** (0 errors)

---

## Checklist for New Features

Before submitting any PR or completing a feature, verify:

- [ ] No `SqlDbContext` in any controller
- [ ] No `SqlDbContext` in any service
- [ ] All new entities have corresponding repository interface + implementation
- [ ] All repositories registered in Program.cs
- [ ] Complex queries (3+ joins) return DTOs, not entities
- [ ] Repository methods follow naming convention (`Get<Entity><Criteria>Async`)
- [ ] All async methods have CancellationToken parameter
- [ ] Build succeeds with 0 errors
- [ ] No EF Core types (`DbSet<T>`, `IQueryable<T>`) exposed in controller/service public interfaces

---

## Refactoring Completion Status

### ✅ COMPLETED - All Services Refactored (December 21, 2025)

The following services have been successfully refactored to use repositories exclusively:

#### **PaymentService**
- **Repositories Used**: `IRegistrationRepository`, `ITeamRepository`, `IFamiliesRepository`, `IRegistrationAccountingRepository`
- **Key Changes**: Removed direct DbContext for registrations, teams, families, accounting queries
- **Email**: Migrated to DTO-based `IEmailService` (Contracts)

#### **TeamRegistrationService**
- **Repositories Used**: `IClubRepRepository`, `IClubRepository`, `IClubTeamRepository`, `IJobRepository`, `IJobLeagueRepository`, `IAgeGroupRepository`, `ITeamRepository`
- **Key Changes**: Eliminated all `_db` queries; added `IClubTeamRepository` for club team operations
- **New Methods**: Extended `IClubRepository` with search/name lookups, `IClubRepRepository` with remove operations

#### **FamilyService**
- **Repositories Used**: `IJobRepository`, `IRegistrationRepository`, `ITeamRepository`, `IFamiliesRepository`, `IUserRepository`, `IJobDiscountCodeRepository`, `IFamilyMemberRepository`
- **Key Changes**: Complete removal of SqlDbContext; added `IFamilyMemberRepository` for family links; switched user updates to `UserManager`
- **New Repositories**: `IFamilyMemberRepository` created for `FamilyMembers` entity operations
- **Extensions**: Added write methods to `IFamiliesRepository` (Add, Update, SaveChanges)

### Repository Infrastructure Added
- **IFamilyMemberRepository**: Child user link management
- **IClubTeamRepository**: Club team CRUD operations  
- **Extended Interfaces**: IFamiliesRepository, IClubRepository, IClubRepRepository with additional query/write methods

### Validation Results
- ✅ **0 remaining SqlDbContext usages** in TSIC.API services
- ✅ **0 remaining SqlDbContext usages** in TSIC.Application services  
- ✅ **Solution builds successfully** with warnings only
- ✅ **All DI registrations** properly configured in Program.cs
- ✅ **Email standardized** to DTO pattern across all services

---

## Summary

**The Golden Rule**: 
> If you see `SqlDbContext` anywhere except repository implementations, **IT IS WRONG**.

**The Pattern**:
```
Controller → Service → Repository → SqlDbContext
     ↓          ↓           ↓
   HTTP     Business    EF Core
  Concerns   Logic    Data Access
```

Follow these standards **without exception**. When in doubt, create a new repository method rather than leak data access concerns into services or controllers.

---

## Questions or Clarifications

If architectural guidance is needed:
1. Review this document first
2. Check existing repository implementations for patterns
3. Default to creating a new repository method over adding complexity to services
4. When in doubt, prefer **more specific repository methods** over generic ones

**Document Version**: 2.0  
**Last Updated**: December 21, 2025  
**Status**: MANDATORY FOR ALL DEVELOPMENT - REFACTORING COMPLETE ✅
