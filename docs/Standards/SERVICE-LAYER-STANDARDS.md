# Service Layer Architecture Standards

## Purpose
This document establishes **mandatory architectural standards** for service layer organization in the TSIC application. All developers and AI agents **MUST** comply with these patterns to ensure proper separation of concerns, testability, and maintainability.

---

## Core Principle

**Services MUST NEVER directly reference `SqlDbContext` or any EF Core data access types.**

All data access flows through repositories:
```
Controller → Service → Repository → SqlDbContext → Database
```

**Services orchestrate business logic, repositories handle data access.**

---

## Layer Organization

### Project Structure

```
TSIC.API/
  ├── Controllers/              ✅ HTTP concerns only
  ├── Middleware/               ✅ HTTP pipeline concerns
  └── Program.cs               ✅ DI registration, app config

TSIC.Application/
  ├── Services/                ✅ Business logic services
  ├── Validators/              ✅ FluentValidation validators
  └── DTOs/ (if needed)        ✅ Use case-specific DTOs

TSIC.Contracts/
  ├── Services/                ✅ Service interfaces (abstractions)
  ├── Repositories/            ✅ Repository interfaces
  └── Dtos/                    ✅ Shared DTOs

TSIC.Infrastructure/
  ├── Services/                ✅ External integrations, infrastructure services
  ├── Repositories/            ✅ Repository implementations
  └── Data/                    ✅ DbContext, Identity

TSIC.Domain/
  ├── Entities/                ✅ EF Core entities (anemic models)
  └── Constants/               ✅ Shared constants
```

---

## Service Types & Placement

### 1. Business Logic Services → `TSIC.Application/Services/`

**When to create**:
- Multi-step workflows
- Business rule enforcement
- Calculations and transformations
- Orchestration of multiple repositories
- Domain-specific operations

**Characteristics**:
- ✅ Depends on multiple repositories
- ✅ Contains business logic
- ✅ Returns DTOs
- ✅ No infrastructure dependencies (email, HTTP, etc.)
- ❌ NO SqlDbContext
- ❌ NO EF Core types

**Examples**:
- `PlayerRegistrationService` - orchestrates player registration workflow
- `PaymentService` - handles payment processing logic
- `FeeCalculatorService` - calculates fees based on business rules
- `FamilyService` - family management business logic

**Interface location**: `TSIC.Contracts/Services/`

---

### 2. Infrastructure Services → `TSIC.Infrastructure/Services/`

**When to create**:
- External API integrations
- Authentication/authorization logic
- Email/SMS sending
- File storage operations
- Caching implementations

**Characteristics**:
- ✅ Depends on external systems
- ✅ Uses HttpClient, AWS SDK, etc.
- ✅ Implements infrastructure concerns
- ❌ NO SqlDbContext (use repositories if data access needed)

**Examples**:
- `EmailService` - Amazon SES integration
- `TokenService` - JWT token generation
- `AuthService` - authentication logic

**Interface location**: `TSIC.Contracts/Services/`

---

### 3. Query/Lookup Services → `TSIC.Application/Services/` or Direct Repository Use

**When to create**:
- Complex read-only queries spanning multiple entities
- Frequently reused query patterns
- Query logic with business context

**When NOT to create** (use repository directly):
- Simple entity lookups
- Standard CRUD operations
- Single-entity queries

**Characteristics**:
- ✅ Read-only operations
- ✅ Depends on repositories
- ✅ Returns DTOs or view models
- ❌ NO SqlDbContext

**Examples**:
- `JobLookupService` - complex job queries with business context
- `RoleLookupService` - role-based query orchestration

**IMPORTANT**: If the "lookup service" is just wrapping repository calls with no additional logic, **DELETE IT** and use the repository directly in the controller/service.

---

### 4. Utility/Helper Services → `TSIC.Application/Services/Shared/`

**When to create**:
- Pure calculations with no data access
- Text processing, formatting
- Validation logic helpers

**Characteristics**:
- ✅ Stateless
- ✅ No dependencies on repositories or data access
- ✅ Pure functions

**Examples**:
- `DiscountCodeEvaluator` - pure discount calculation logic
- `TextSubstitutionService` - template processing (if refactored to not use DbContext)

---

## Anti-Patterns (NEVER DO THIS)

### ❌ Anti-Pattern 1: SqlDbContext in Services
```csharp
// ❌ WRONG - Service directly accessing database
public class PaymentService : IPaymentService
{
    private readonly SqlDbContext _db; // ❌ NEVER

    public async Task<PaymentResultDto> ProcessPaymentAsync(Guid registrationId)
    {
        var registration = await _db.Registrations
            .Include(r => r.Accounting)
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId); // ❌ NEVER
        
        // business logic...
    }
}
```

**Fix**: Use repositories
```csharp
// ✅ CORRECT - Service using repositories
public class PaymentService : IPaymentService
{
    private readonly IRegistrationRepository _registrationRepo; // ✅ Repository abstraction

    public async Task<PaymentResultDto> ProcessPaymentAsync(Guid registrationId)
    {
        var registration = await _registrationRepo.GetWithAccountingAsync(registrationId); // ✅
        
        // business logic...
    }
}
```

---

### ❌ Anti-Pattern 2: SqlDbContext as Method Parameter
```csharp
// ❌ WRONG - Exposing infrastructure in interface
public interface IAdnApiService
{
    Task<AdnCredentialsViewModel> GetJobAdnCredentials(
        SqlDbContext context, // ❌ NEVER IN INTERFACE
        Guid jobId);
}
```

**Fix**: Service encapsulates its data access needs
```csharp
// ✅ CORRECT - Clean interface
public interface IAdnApiService
{
    Task<AdnCredentialsViewModel> GetJobAdnCredentialsAsync(Guid jobId); // ✅
}

// Implementation uses repository
public class AdnApiService : IAdnApiService
{
    private readonly IJobRepository _jobRepo;
    private readonly ICustomerRepository _customerRepo;

    public async Task<AdnCredentialsViewModel> GetJobAdnCredentialsAsync(Guid jobId)
    {
        var job = await _jobRepo.GetWithAdnCredentialsAsync(jobId);
        // transform to view model...
    }
}
```

---

### ❌ Anti-Pattern 3: Business Logic in Wrong Layer (API Project)
```csharp
// ❌ WRONG - Business logic in API project
// TSIC.API/Services/Players/PlayerRegistrationService.cs
public class PlayerRegistrationService : IPlayerRegistrationService
{
    // Complex business logic that belongs in Application layer
}
```

**Fix**: Move to Application layer
```csharp
// ✅ CORRECT - Business logic in Application layer
// TSIC.Application/Services/Players/PlayerRegistrationService.cs
public class PlayerRegistrationService : IPlayerRegistrationService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IFeeCalculator _feeCalculator;
    // ... business logic
}
```

---

### ❌ Anti-Pattern 4: Anemic Lookup Services (Unnecessary Wrapper)
```csharp
// ❌ WRONG - Service that just wraps repository with no added value
public class TeamLookupService : ITeamLookupService
{
    private readonly SqlDbContext _db; // ❌

    public async Task<Teams?> GetTeamByIdAsync(Guid teamId)
    {
        return await _db.Teams.FindAsync(teamId); // ❌ Just wrapping EF Core
    }
}
```

**Fix**: Delete the service, use repository directly
```csharp
// ✅ CORRECT - Controller/Service uses repository directly
public class PlayerRegistrationService
{
    private readonly ITeamRepository _teamRepo; // ✅ Use repository directly

    public async Task AssignTeamAsync(Guid registrationId, Guid teamId)
    {
        var team = await _teamRepo.GetByIdAsync(teamId); // ✅ Direct repository use
        // ... business logic
    }
}
```

---

### ❌ Anti-Pattern 5: Service Dependencies on Concrete Classes
```csharp
// ❌ WRONG - Depending on concrete implementation
public class PaymentService
{
    private readonly EmailService _emailService; // ❌ Concrete class

    public PaymentService(EmailService emailService) // ❌
    {
        _emailService = emailService;
    }
}
```

**Fix**: Depend on abstractions
```csharp
// ✅ CORRECT - Depending on interface
public class PaymentService
{
    private readonly IEmailService _emailService; // ✅ Interface

    public PaymentService(IEmailService emailService) // ✅
    {
        _emailService = emailService;
    }
}
```

---

## Service Implementation Patterns

### Pattern 1: Multi-Repository Orchestration Service

**When**: Business logic spanning multiple entities/repositories

```csharp
// TSIC.Contracts/Services/IPaymentService.cs
public interface IPaymentService
{
    Task<PaymentResultDto> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken ct = default);
}

// TSIC.Application/Services/Payments/PaymentService.cs
public class PaymentService : IPaymentService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IDiscountCodeRepository _discountRepo;
    private readonly IEmailService _emailService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IRegistrationRepository registrationRepo,
        IPaymentRepository paymentRepo,
        IDiscountCodeRepository discountRepo,
        IEmailService emailService,
        ILogger<PaymentService> logger)
    {
        _registrationRepo = registrationRepo;
        _paymentRepo = paymentRepo;
        _discountRepo = discountRepo;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<PaymentResultDto> ProcessPaymentAsync(
        ProcessPaymentRequest request, 
        CancellationToken ct = default)
    {
        // 1. Get registration
        var registration = await _registrationRepo.GetByIdAsync(request.RegistrationId, ct);
        if (registration == null)
            return PaymentResultDto.NotFound();

        // 2. Validate discount code if provided
        decimal discount = 0m;
        if (!string.IsNullOrEmpty(request.DiscountCode))
        {
            var discountInfo = await _discountRepo.GetActiveCodeAsync(
                registration.JobId, 
                request.DiscountCode.ToLower(), 
                DateTime.UtcNow, 
                ct);
            
            if (discountInfo != null)
            {
                discount = CalculateDiscount(registration.FeeTotal ?? 0m, discountInfo);
            }
        }

        // 3. Calculate final amount
        var finalAmount = (registration.FeeTotal ?? 0m) - discount;

        // 4. Process payment (external service call via infrastructure service)
        var payment = new Payment
        {
            RegistrationId = request.RegistrationId,
            Amount = finalAmount,
            PaymentDate = DateTime.UtcNow
        };
        await _paymentRepo.AddAsync(payment, ct);
        await _paymentRepo.SaveChangesAsync(ct);

        // 5. Send confirmation email
        await _emailService.SendPaymentConfirmationAsync(registration.UserId, payment, ct);

        // 6. Return result
        return new PaymentResultDto
        {
            Success = true,
            TransactionId = payment.PaymentId,
            AmountCharged = finalAmount
        };
    }

    private static decimal CalculateDiscount(decimal amount, DiscountInfo discount)
    {
        // Pure business logic
        return discount.IsPercentage
            ? amount * (discount.Value / 100m)
            : discount.Value;
    }
}
```

**Key points**:
- ✅ Service orchestrates workflow
- ✅ Uses multiple repositories for data access
- ✅ Calls infrastructure service (email) for side effects
- ✅ Contains business logic (discount calculation)
- ✅ Returns DTO
- ❌ NO SqlDbContext

---

### Pattern 2: Calculation/Transformation Service (Stateless)

**When**: Pure business logic with no data access

```csharp
// TSIC.Contracts/Services/IPlayerFeeCalculator.cs
public interface IPlayerFeeCalculator
{
    decimal CalculateTotalFee(
        decimal baseFee,
        decimal? teamFee,
        bool includeProcessingFee,
        decimal? discount = null);
    
    decimal CalculateProcessingFee(decimal amount);
}

// TSIC.Application/Services/Players/PlayerFeeCalculator.cs
public class PlayerFeeCalculator : IPlayerFeeCalculator
{
    private readonly decimal _processingFeePercent;

    public PlayerFeeCalculator(decimal processingFeePercent)
    {
        _processingFeePercent = processingFeePercent;
    }

    public decimal CalculateTotalFee(
        decimal baseFee,
        decimal? teamFee,
        bool includeProcessingFee,
        decimal? discount = null)
    {
        var subtotal = baseFee + (teamFee ?? 0m) - (discount ?? 0m);
        
        if (includeProcessingFee)
        {
            subtotal += CalculateProcessingFee(subtotal);
        }

        return Math.Max(0m, subtotal); // Never negative
    }

    public decimal CalculateProcessingFee(decimal amount)
    {
        return Math.Round(amount * _processingFeePercent, 2);
    }
}
```

**Key points**:
- ✅ Pure calculation logic
- ✅ Stateless (except injected configuration)
- ✅ No data access
- ✅ Easily testable
- ✅ Can be used by multiple services

---

### Pattern 3: Query Service with Business Context

**When**: Complex read queries with business rules

```csharp
// TSIC.Contracts/Services/IRegistrationQueryService.cs
public interface IRegistrationQueryService
{
    Task<List<RegistrationSummaryDto>> GetActiveRegistrationsForJobAsync(
        Guid jobId, 
        bool includeUnpaid = true,
        CancellationToken ct = default);
}

// TSIC.Application/Services/Registration/RegistrationQueryService.cs
public class RegistrationQueryService : IRegistrationQueryService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IJobRepository _jobRepo;

    public RegistrationQueryService(
        IRegistrationRepository registrationRepo,
        IJobRepository jobRepo)
    {
        _registrationRepo = registrationRepo;
        _jobRepo = jobRepo;
    }

    public async Task<List<RegistrationSummaryDto>> GetActiveRegistrationsForJobAsync(
        Guid jobId,
        bool includeUnpaid = true,
        CancellationToken ct = default)
    {
        // Business rule: Only return active registrations
        var registrations = await _registrationRepo.GetByJobIdAsync(
            jobId, 
            activeOnly: true, 
            includeUser: true,
            includeAccounting: true,
            ct);

        // Business rule: Filter unpaid if requested
        if (!includeUnpaid)
        {
            registrations = registrations
                .Where(r => (r.PaidTotal ?? 0m) >= (r.FeeTotal ?? 0m))
                .ToList();
        }

        // Transform to DTOs
        return registrations.Select(r => new RegistrationSummaryDto
        {
            RegistrationId = r.RegistrationId,
            PlayerName = $"{r.User?.FirstName} {r.User?.LastName}",
            FeePaid = r.PaidTotal ?? 0m,
            FeeTotal = r.FeeTotal ?? 0m,
            Balance = (r.FeeTotal ?? 0m) - (r.PaidTotal ?? 0m),
            Status = DetermineStatus(r)
        }).ToList();
    }

    private static string DetermineStatus(Registrations registration)
    {
        // Business logic for status determination
        var paid = registration.PaidTotal ?? 0m;
        var total = registration.FeeTotal ?? 0m;

        if (paid >= total) return "Paid";
        if (paid > 0m) return "Partial";
        return "Unpaid";
    }
}
```

**Key points**:
- ✅ Uses repositories for data access
- ✅ Applies business rules (active only, paid filtering)
- ✅ Transforms to DTOs
- ✅ Returns data with business context

---

### Pattern 4: External Integration Service

**When**: Integrating with external APIs, third-party services

```csharp
// TSIC.Contracts/Services/IVerticalInsureService.cs
public interface IVerticalInsureService
{
    Task<InsuranceQuoteDto> GetInsuranceQuoteAsync(
        Guid registrationId,
        CancellationToken ct = default);
}

// TSIC.Infrastructure/Services/VerticalInsure/VerticalInsureService.cs
public class VerticalInsureService : IVerticalInsureService
{
    private readonly IRegistrationRepository _registrationRepo; // ✅ Use repository
    private readonly ITeamRepository _teamRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VerticalInsureService> _logger;
    private readonly bool _isProduction;

    public VerticalInsureService(
        IRegistrationRepository registrationRepo,
        ITeamRepository teamRepo,
        IHttpClientFactory httpClientFactory,
        IHostEnvironment env,
        ILogger<VerticalInsureService> logger)
    {
        _registrationRepo = registrationRepo;
        _teamRepo = teamRepo;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _isProduction = env.IsProduction();
    }

    public async Task<InsuranceQuoteDto> GetInsuranceQuoteAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        // 1. Get data via repository
        var registration = await _registrationRepo.GetWithUserAndJobAsync(registrationId, ct);
        if (registration == null)
            throw new ArgumentException("Registration not found", nameof(registrationId));

        // 2. Build request for external API
        var request = new VerticalInsureQuoteRequest
        {
            PlayerName = $"{registration.User.FirstName} {registration.User.LastName}",
            PlayerAge = CalculateAge(registration.User.DateOfBirth),
            Sport = "Lacrosse",
            CoverageAmount = 50000m
        };

        // 3. Call external API
        var httpClient = _httpClientFactory.CreateClient("verticalinsure");
        var response = await httpClient.PostAsJsonAsync("/api/quotes", request, ct);
        response.EnsureSuccessStatusCode();

        var externalQuote = await response.Content.ReadFromJsonAsync<VerticalInsureApiResponse>(ct);

        // 4. Transform to our DTO
        return new InsuranceQuoteDto
        {
            QuoteId = externalQuote.QuoteId,
            Premium = externalQuote.MonthlyPremium,
            CoverageAmount = externalQuote.Coverage
        };
    }

    private static int CalculateAge(DateTime? dateOfBirth)
    {
        if (dateOfBirth == null) return 0;
        var today = DateTime.Today;
        var age = today.Year - dateOfBirth.Value.Year;
        if (dateOfBirth.Value.Date > today.AddYears(-age)) age--;
        return age;
    }
}
```

**Key points**:
- ✅ Infrastructure service (in Infrastructure project)
- ✅ Uses repositories for data access (not SqlDbContext)
- ✅ Handles external HTTP calls
- ✅ Transforms between external API models and our DTOs

---

## Dependency Injection Registration

**Location**: `TSIC.API/Program.cs`

**Pattern** (organized by layer):

```csharp
// Infrastructure Repositories (ALWAYS FIRST)
builder.Services.AddScoped<IRegistrationRepository, RegistrationRepository>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
// ... all repositories

// Application Services (Business Logic)
builder.Services.AddScoped<IPlayerRegistrationService, PlayerRegistrationService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IFamilyService, FamilyService>();
builder.Services.AddScoped<IRegistrationQueryService, RegistrationQueryService>();
builder.Services.AddScoped<IFeeCalculator, PlayerFeeCalculator>();

// Infrastructure Services (External integrations)
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IVerticalInsureService, VerticalInsureService>();
builder.Services.AddScoped<IAdnApiService, AdnApiService>();
builder.Services.AddScoped<IUsLaxService, UsLaxService>();

// Authentication & Authorization
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
```

**Rules**:
- ✅ Group by layer (Repositories → Application → Infrastructure → Auth)
- ✅ Alphabetical within each group
- ✅ Always use `AddScoped` for services with state
- ✅ Use `AddSingleton` only for stateless utilities

---

## Service Responsibilities Decision Matrix

| Scenario | Create Service? | Service Type | Layer |
|----------|----------------|--------------|-------|
| Single entity lookup by ID | ❌ NO - Use repository directly | N/A | N/A |
| Multi-step workflow with business rules | ✅ YES | Business Logic | Application |
| Complex calculation (no data access) | ✅ YES | Utility/Calculator | Application |
| External API integration | ✅ YES | Infrastructure | Infrastructure |
| Multiple repository orchestration | ✅ YES | Business Logic | Application |
| Email/SMS sending | ✅ YES | Infrastructure | Infrastructure |
| Simple CRUD operations | ❌ NO - Use repository directly | N/A | N/A |
| Read query with business filtering | ✅ MAYBE - If reused frequently | Query Service | Application |
| JWT token generation | ✅ YES | Infrastructure | Infrastructure |
| Fee calculation with multiple rules | ✅ YES | Calculator | Application |

---

## Migration Strategy for Existing Services

### Step 1: Identify Service Type
Categorize each service:
- Business logic → Move to `TSIC.Application/Services/`
- Infrastructure → Keep in `TSIC.Infrastructure/Services/`
- Anemic wrapper → Delete, use repository directly

### Step 2: Remove SqlDbContext Dependencies
For each service with `SqlDbContext`:

1. **Identify data access patterns** - what queries does it perform?
2. **Check if repository exists** - do we have a repository for this entity?
3. **Create repository method** if missing
4. **Replace SqlDbContext injection** with repository injection
5. **Replace all `_db.*` calls** with repository method calls
6. **Build and verify** - ensure 0 errors

### Step 3: Fix Interface Contracts
For interfaces exposing `SqlDbContext`:

1. **Remove SqlDbContext parameters** from interface methods
2. **Service encapsulates data access** internally via repositories
3. **Update all callers** to new signature

### Step 4: Relocate Services to Correct Layer
Move services from `TSIC.API/Services/` to `TSIC.Application/Services/`:

```powershell
# Example: Move PlayerRegistrationService
mv TSIC.API/Services/Players/PlayerRegistrationService.cs `
   TSIC.Application/Services/Players/PlayerRegistrationService.cs

# Update namespace
# Change: namespace TSIC.API.Services.Players;
# To:     namespace TSIC.Application.Services.Players;
```

### Step 5: Update DI Registration
Ensure all services registered in `Program.cs` with correct lifetimes.

---

## Testing Implications

### Service Unit Tests
```csharp
public class PaymentServiceTests
{
    [Fact]
    public async Task ProcessPayment_AppliesDiscountCorrectly()
    {
        // Arrange
        var mockRegistrationRepo = new Mock<IRegistrationRepository>();
        mockRegistrationRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(new Registrations { FeeTotal = 100m });

        var mockDiscountRepo = new Mock<IDiscountCodeRepository>();
        mockDiscountRepo.Setup(d => d.GetActiveCodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), default))
            .ReturnsAsync(new DiscountInfo { IsPercentage = true, Value = 10m });

        var service = new PaymentService(
            mockRegistrationRepo.Object,
            Mock.Of<IPaymentRepository>(),
            mockDiscountRepo.Object,
            Mock.Of<IEmailService>(),
            Mock.Of<ILogger<PaymentService>>());

        // Act
        var result = await service.ProcessPaymentAsync(new ProcessPaymentRequest 
        { 
            RegistrationId = Guid.NewGuid(),
            DiscountCode = "SAVE10"
        });

        // Assert
        Assert.Equal(90m, result.AmountCharged); // 100 - 10% = 90
    }
}
```

**Key Points**:
- ✅ Mock repositories, not SqlDbContext
- ✅ Test business logic in isolation
- ✅ No database required
- ✅ Fast, reliable tests

---

## Common Questions

### Q: When do I create a service vs use repository directly in controller?

**A:** Create a service if:
- Multi-step workflow
- Business rules/calculations
- Multiple repository coordination
- Reused logic across multiple controllers

Use repository directly if:
- Simple CRUD
- Single entity lookup
- No business logic

### Q: Can a service call another service?

**A:** Yes, but prefer composition:
```csharp
// ✅ GOOD - Service composes other services
public class PlayerRegistrationService
{
    private readonly IFeeCalculator _feeCalculator; // ✅ Utility service
    private readonly IEmailService _emailService;   // ✅ Infrastructure service

    // Use specialized services for specific concerns
}

// ❌ AVOID - Deep service chains
// Service A → Service B → Service C → Service D
// Creates tight coupling and testing complexity
```

### Q: Where do validation services go?

**A:** Use FluentValidation in `TSIC.Application/Validators/`. Don't create "validation services" - use the framework.

### Q: What about services that need transactions across multiple operations?

**A:** Implement Unit of Work pattern (future standard document). For now, ensure services coordinate `SaveChangesAsync()` calls properly.

---

## Checklist for New Services

Before creating any service, verify:

- [ ] Service is in correct layer (Application vs Infrastructure)
- [ ] Service has interface in `TSIC.Contracts/Services/`
- [ ] Service does NOT inject SqlDbContext
- [ ] Service depends only on repository interfaces (not implementations)
- [ ] Service interface does NOT expose SqlDbContext in method signatures
- [ ] Service returns DTOs (not domain entities)
- [ ] Service is registered in Program.cs DI container
- [ ] Service responsibilities are clear (not "god object")
- [ ] Service is needed (not just wrapping repository calls)

---

## Summary

**The Golden Rules**:

1. **NO SqlDbContext in services** - Use repositories ALWAYS
2. **NO SqlDbContext in service interfaces** - Encapsulate data access
3. **Business logic in Application layer** - Not API project
4. **Infrastructure services in Infrastructure layer** - External integrations
5. **Delete anemic services** - If it just wraps repository, use repository directly
6. **Services orchestrate, repositories access** - Clear separation

**Service Layer Flow**:
```
Controller 
   ↓ (calls)
Service (Application)
   ↓ (uses)
Repository (Infrastructure)
   ↓ (uses)
SqlDbContext
   ↓ (queries)
Database
```

Follow these standards **without exception**. When in doubt, create a repository method rather than leak data access into services.

---

## Questions or Clarifications

If architectural guidance is needed:
1. Review this document first
2. Check existing service implementations for patterns
3. Default to using repositories over direct SqlDbContext access
4. When in doubt, ask: "Is this business logic (Application) or infrastructure (Infrastructure)?"

**Document Version**: 1.0  
**Last Updated**: December 21, 2025  
**Status**: MANDATORY FOR ALL DEVELOPMENT

---

## Related Documents

- [Repository Pattern Standards](./REPOSITORY-PATTERN-STANDARDS.md) - Data access layer patterns
