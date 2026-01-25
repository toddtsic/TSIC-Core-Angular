# Clean Architecture Implementation - TSIC Core API

## Overview

This document outlines the clean architecture implementation completed for the TSIC Core API project. The implementation follows Domain-Driven Design (DDD) principles with proper separation of concerns across multiple layers.

## Architecture Layers

### 1. Domain Layer (`TSIC.Domain`)
**Purpose**: Contains enterprise-wide business rules and entities.

**Contents**:
- **Constants**: `RoleConstants.cs`, `TsicConstants.cs`
  - Moved from API layer to Domain for proper dependency flow
  - Contains role IDs, URLs, and other shared constants

**Responsibilities**:
- Define core business entities and value objects
- Contain business rules that are independent of any technology
- Define interfaces for domain services

### 2. Application Layer (`TSIC.Application`)
**Purpose**: Contains application-specific business logic and use cases.

**Contents**:
- **DTOs**: `RegistrationDtos.cs`
  - `RegistrationRoleDto`: Groups registrations by role type
  - `RegistrationDto`: Individual registration data
- **Services**: `IRoleLookupService.cs`
  - Interface defining the contract for role lookup operations
- **Validators**: (Empty, ready for FluentValidation rules)
- **Mappings**: (Empty, ready for AutoMapper profiles)

**Responsibilities**:
- Define application use cases and workflows
- Coordinate between domain objects and external concerns
- Define DTOs for data transfer
- Contain validation and mapping logic

### 3. Infrastructure Layer (`TSIC.Infrastructure`)
**Purpose**: Contains external concerns and implementations.

**Contents**:
- **Services**: `RoleLookupService.cs`
  - Implementation of `IRoleLookupService` using Entity Framework
  - Contains complex LINQ queries for role-based data retrieval
- **Data**: `SqlDbContext`, `TsicIdentityDbContext`
  - Entity Framework contexts for data access

**Responsibilities**:
- Implement data persistence and retrieval
- Handle external API calls and integrations
- Provide concrete implementations of application interfaces
- Manage database connections and migrations

### 4. API Layer (`TSIC.API`)
**Purpose**: Contains presentation and API concerns.

**Contents**:
- **Controllers**: `AuthController.cs`, `TestController.cs`
- **DTOs**: API-specific request/response models
- **Middleware**: Custom middleware for cross-cutting concerns

**Responsibilities**:
- Handle HTTP requests and responses
- Perform input validation and serialization
- Manage authentication and authorization
- Provide API documentation (Swagger)

## Key Architectural Decisions

### 1. Dependency Direction
```
API → Application → Domain ← Infrastructure
     ↓                    ↑
     └─── Infrastructure ──┘
```

- **API depends on Application**: Controllers use application services
- **Application depends on Domain**: Uses domain constants and defines contracts
- **Infrastructure depends on Application**: Implements application interfaces
- **Infrastructure depends on Domain**: Uses domain constants

### 2. Service Layer Pattern
- **Interface in Application**: `IRoleLookupService` defines the contract
- **Implementation in Infrastructure**: `RoleLookupService` handles data access
- **Dependency Injection**: API registers Infrastructure implementation

### 3. DTO Layering
- **Application DTOs**: Define the shape of data crossing layer boundaries
- **API DTOs**: Handle HTTP-specific concerns (removed duplicate DTOs)
- **Single Source of Truth**: Application layer owns the DTO definitions

## Implementation Details

### Moved Components

#### From API to Application Layer:
- `IRoleLookupService` interface
- `RegistrationRoleDto` and `RegistrationDto` classes

#### From API to Infrastructure Layer:
- `RoleLookupService` implementation (moved from API.Services to Infrastructure.Services)

#### From API to Domain Layer:
- `RoleConstants` and `TsicConstants` (moved from API.Constants to Domain.Constants)

### Updated References

#### AuthController.cs:
```csharp
// Before
using TSIC.API.Services;

// After
using TSIC.Application.Services;
```

#### Program.cs:
```csharp
// Before
builder.Services.AddScoped<TSIC.API.Services.IRoleLookupService, TSIC.API.Services.RoleLookupService>();

// After
builder.Services.AddScoped<IRoleLookupService, RoleLookupService>();
```

#### LoginResponseDto.cs:
```csharp
// Before
using System.Collections.Generic;

namespace TSIC.API.Dtos
{
    public record LoginResponseDto(List<RegistrationRoleDto> Registrations);
}

// After
using System.Collections.Generic;
using TSIC.Application.DTOs;

namespace TSIC.API.Dtos
{
    public record LoginResponseDto(List<RegistrationRoleDto> Registrations);
}
```

## Benefits Achieved

### 1. Separation of Concerns
- **Business Logic**: Isolated in Application layer
- **Data Access**: Contained in Infrastructure layer
- **Presentation**: Focused in API layer
- **Domain Rules**: Centralized in Domain layer

### 2. Testability
- Application services can be unit tested with mocked Infrastructure
- Domain logic is independent of external dependencies
- Each layer can be tested in isolation

### 3. Maintainability
- Changes to data access don't affect business logic
- New implementations can be swapped without changing Application code
- Clear boundaries prevent tight coupling

### 4. Scalability
- Infrastructure can be changed (e.g., different database) without affecting Application
- New API endpoints can reuse existing Application services
- Domain rules remain stable across technology changes

## Project Structure After Implementation

```
src/backend/
├── TSIC.Domain/
│   └── Constants/
│       ├── RoleConstants.cs
│       └── TsicConstants.cs
├── TSIC.Application/
│   ├── DTOs/
│   │   └── RegistrationDtos.cs
│   ├── Services/
│   │   └── IRoleLookupService.cs
│   ├── Validators/
│   └── Mappings/
├── TSIC.Infrastructure/
│   ├── Services/
│   │   └── RoleLookupService.cs
│   └── Data/
│       ├── SqlDbContext/
│       └── Identity/
└── TSIC.API/
    ├── Controllers/
    │   ├── AuthController.cs
    │   └── TestController.cs
    ├── Dtos/
    │   ├── LoginResponseDto.cs
    │   └── ...
    └── Program.cs
```

## Validation and Testing

### Build Status: ✅ PASSING
- All layers compile successfully
- No circular dependencies
- Proper project references configured

### Runtime Status: ✅ WORKING
- API starts successfully on `http://localhost:5022`
- Swagger documentation available
- Authentication endpoints functional
- Database connections working

### Architecture Validation:
- ✅ Dependency injection working correctly
- ✅ Service registration in proper layers
- ✅ DTOs flowing correctly between layers
- ✅ Constants accessible across layers

## Future Enhancements

### Ready for Implementation:
1. **FluentValidation**: Add validators in `TSIC.Application.Validators`
2. **AutoMapper**: Add mapping profiles in `TSIC.Application.Mappings`
3. **Repository Pattern**: Can be added if needed (currently excluded per requirements)
4. **CQRS Pattern**: Application layer ready for command/query separation
5. **MediatR**: Can be added for request/response handling

### Monitoring and Logging:
- Consider adding application insights
- Implement structured logging
- Add health checks for all layers

## Conclusion

The clean architecture implementation successfully separates concerns across four distinct layers, following SOLID principles and DDD patterns. The codebase is now more maintainable, testable, and scalable while preserving all existing functionality.

**Date Completed**: October 24, 2025
**Architecture Pattern**: Clean Architecture / Onion Architecture
**Framework**: .NET 9 ASP.NET Core Web API
**Testing**: Manual validation completed, automated tests ready for implementation