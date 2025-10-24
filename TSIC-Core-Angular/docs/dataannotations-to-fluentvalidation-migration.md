# DataAnnotations to FluentValidation Migration Guide

## For Your TSIC Project

### Phase 1: Setup (Easy - 15 minutes)
```csharp
// 1. Add to TSIC.Application.csproj
<PackageReference Include="FluentValidation" Version="12.0.0" />
<PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.0.0" />

// 2. Add to Program.cs
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
```

### Phase 2: Create Validators (Medium - 30-60 minutes per complex DTO)

#### Simple Migration Example:
```csharp
// BEFORE (DataAnnotations)
public class UserRegistrationDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string FirstName { get; set; }
}

// AFTER (FluentValidation)
public class UserRegistrationValidator : AbstractValidator<UserRegistrationDto>
{
    public UserRegistrationValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Please enter a valid email address");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .Length(2, 50).WithMessage("First name must be between 2 and 50 characters");
    }
}
```

### Phase 3: Update Controllers (Easy - 5 minutes each)

#### BEFORE:
```csharp
[HttpPost("register")]
public async Task<IActionResult> Register([FromBody] UserRegistrationDto request)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);
    // ... rest of method
}
```

#### AFTER:
```csharp
[HttpPost("register")]
public async Task<IActionResult> Register([FromBody] UserRegistrationDto request)
{
    var validationResult = await _validator.ValidateAsync(request);
    if (!validationResult.IsValid)
        return BadRequest(validationResult.Errors);
    // ... rest of method
}
```

### Phase 4: Advanced Features You'll Love

#### Multi-tenant Validation (Perfect for your Jobs system):
```csharp
public class JobSpecificValidator : AbstractValidator<RegistrationDto>
{
    public JobSpecificValidator(IJobContext jobContext)
    {
        When(x => jobContext.CurrentJob.Type == JobType.Tournament, () =>
        {
            RuleFor(x => x.InsuranceCertificate).NotEmpty();
            RuleFor(x => x.TeamSize).InclusiveBetween(8, 25);
        });

        When(x => jobContext.CurrentJob.Type == JobType.Camp, () =>
        {
            RuleFor(x => x.MedicalForm).NotEmpty();
            RuleFor(x => x.TeamSize).InclusiveBetween(5, 15);
        });
    }
}
```

#### Role-based Validation:
```csharp
public class AdminOnlyValidator : AbstractValidator<AdminActionDto>
{
    public AdminOnlyValidator(IUserContext userContext)
    {
        RuleFor(x => x)
            .Must(_ => userContext.IsAdmin)
            .WithMessage("Admin privileges required");
    }
}
```

## Migration Difficulty Assessment

### ✅ **Easy to Migrate:**
- Simple validation rules (Required, Length, Email)
- Basic DTOs
- Controllers with standard ModelState validation

### ⚠️ **Medium Difficulty:**
- Custom validation attributes
- Complex business rules
- Cross-property validation

### 🚫 **Keep DataAnnotations:**
- Simple CRUD operations
- Third-party libraries that expect DataAnnotations
- Areas where you want model + validation together

## Recommendation for Your Project

**Start with FluentValidation for new features**, especially:

1. **Authentication/Login** (complex business rules)
2. **Team Registration** (multi-tenant rules)
3. **Tournament Management** (role-based permissions)
4. **Payment Processing** (business rule validation)

**Keep DataAnnotations for:**
- Simple data transfer objects
- Existing working code (don't break what's working)
- Areas with minimal business logic

## Hybrid Approach (Recommended)

```csharp
// Use both in the same project
public class SimpleDto // Keep DataAnnotations
{
    [Required]
    public string Name { get; set; }
}

public class ComplexBusinessDto // Use FluentValidation
{
    // No attributes here
}

public class ComplexBusinessValidator : AbstractValidator<ComplexBusinessDto>
{
    // Complex validation logic here
}
```

This gives you the best of both worlds during transition!