# TSIC-Core-Angular AI Coding Agent Instructions

## CRITICAL: Script Commands

**⚠️ DO NOT CONSTRUCT COMMANDS - ALWAYS COPY FROM BELOW**

All exact script execution commands. Copy directly - never modify paths or construct commands manually.

**Repository root is**: `C:\Users\Administrator\source\TSIC-Core-Angular` (NOT the nested TSIC-Core-Angular subfolder)

### Regenerate API Models
```powershell
cd C:\Users\Administrator\source\TSIC-Core-Angular; .\scripts\2-Regenerate-API-Models.ps1
```

### Build Backend Only
```powershell
cd C:\Users\Administrator\source\TSIC-Core-Angular; dotnet build src/backend/TSIC.API/TSIC.API.csproj
```

### Build Full Solution
```powershell
cd C:\Users\Administrator\source\TSIC-Core-Angular; dotnet build TSIC-Core-Angular.sln
```

### Run API Server
```powershell
cd C:\Users\Administrator\source\TSIC-Core-Angular; dotnet watch run --project src/backend/TSIC.API/TSIC.API.csproj --launch-profile https
```

### Start Angular Dev Server
```powershell
cd C:\Users\Administrator\source\TSIC-Core-Angular\TSIC-Core-Angular\src\frontend\tsic-app; npm start
```

### Format Code
```powershell
cd C:\Users\Administrator\source\TSIC-Core-Angular; dotnet format TSIC-Core-Angular.sln
```

**Always use these commands exactly as written. Never modify paths or add absolute paths.**

## Project Architecture

**Full-stack .NET + Angular application** with Clean Architecture:
- **Backend**: .NET 10.0 API (`TSIC-Core-Angular/src/backend/`)
  - `TSIC.API` - Controllers, middleware, OpenAPI (Microsoft built-in)
  - `TSIC.Application` - Use cases, DTOs, service interfaces
  - `TSIC.Domain` - Business entities, constants (`RoleConstants`, `TsicConstants`)
  - `TSIC.Infrastructure` - Data access implementations, EF Core contexts
  - `TSIC.Contracts` - Shared contracts
- **Frontend**: Angular 21 standalone components (`TSIC-Core-Angular/src/frontend/tsic-app/`)
  - Uses signals (not Observables) for state management
  - Modern syntax: `@if`, `@for` instead of `*ngIf`, `*ngFor`
  - Standalone components with `inject()` DI

**Critical dependency flow**: API → Application → Domain ← Infrastructure

## Authentication & Authorization (CRITICAL)

### Two-Phase Authentication System
1. **Phase 1 (Basic)**: Username only → Access role selection
2. **Phase 2 (Full)**: Username + `regId` + `jobPath` → Access job-specific features

### JWT Token Claims (Phase 2)
```json
{
  "sub": "username",
  "regId": "GUID",      // Registration ID - primary user identifier
  "jobPath": "string",  // Job path (e.g., "aim-cac-2026")
  "role": "Superuser",  // Role name from RoleConstants.Names
  "exp": 1234567890
}
```

### Route Protection Patterns
Use route data flags in `app.routes.ts`:
```typescript
// Allow unauthenticated access (public pages)
{ path: 'register', data: { allowAnonymous: true } }

// Require Phase 2 auth (regId + jobPath)
{ path: 'family', data: { requirePhase2: true } }

// Require SuperUser role
{ path: 'admin', data: { requireSuperUser: true } }

// Redirect authenticated users (login/landing pages)
{ path: 'login', data: { redirectAuthenticated: true } }
```

**Single unified guard**: `authGuard` handles all scenarios (replaced three separate guards)

### API Authorization Rules

**CRITICAL**: APIs under `[Authorize]` should NEVER require parameters that can be derived from JWT claims:

```csharp
// ❌ WRONG - jobId passed as parameter (security risk)
[Authorize]
public async Task<IActionResult> GetData(Guid jobId) { }

// ✅ CORRECT - Extract jobId from token claims
[Authorize]
public async Task<IActionResult> GetData() {
    var regId = Guid.Parse(User.FindFirst("regId")?.Value);
    var jobId = await _context.Registrations
        .Where(r => r.RegistrationId == regId)
        .Select(r => r.JobId)
        .FirstOrDefaultAsync();
}
```

**Automatic JobPath Validation**: Every `[Authorize]` endpoint validates token `jobPath` matches route `jobPath` (except SuperUsers)

### Authorization Policies
- `SuperUserOnly` - System-wide admin
- `AdminOnly` - Job-level admin (Director, SuperDirector, Superuser)
- `RefAdmin` - Referee management
- `StoreAdmin` - Store management
- `CanCrossCustomerJobs` - Multi-job operations (SuperUser, SuperDirector)
- `TeamMembersOnly` - Staff, Family, Player
- `StaffOnly` - Unassigned Adult, Staff

**Role constants**: Use `RoleConstants.Names` from `TSIC.Domain`

## Essential Developer Workflows

### Pre-change Checklist (MANDATORY)
- Before editing, read the entire file(s) you will touch and summarize intent; do not edit on partial context.
- For CSS/layout/sticky changes, trace the full DOM/container hierarchy and check for parent `overflow` or sticky ancestors before proposing a fix.
- If confidence in the approach is <90%, pause and ask clarifying questions instead of implementing speculative changes.
- Confirm caller/callee contracts (inputs/outputs/events) before changing signatures or component placement.

### API Model Generation (CRITICAL)
When backend DTOs change, regenerate TypeScript types:
```powershell
# From repo root
.\scripts\2-Regenerate-API-Models.ps1
```
- Script checks/starts API, waits for Swagger at `http://localhost:5022/swagger/v1/swagger.json`
- Runs `npm run generate:api` (uses `openapi-typescript-codegen`)
- **NEVER manually edit** files in `src/app/core/api/models/` - they're auto-generated

### Running the Application
```powershell
# Backend (from repo root)
dotnet run --project TSIC-Core-Angular/src/backend/TSIC.API/TSIC.API.csproj

# Frontend (from tsic-app/)
cd TSIC-Core-Angular/src/frontend/tsic-app
npm start
```

VS Code tasks available: `dotnet: run (API)`, `Start Angular Dev Server`, `Build, Wait API then Start Angular`

### Commits & Code Quality
```powershell
# Standard commit (bypass hooks)
git commit --no-verify -m "message"

# Format before major commits
dotnet format TSIC-Core-Angular.sln
```

## Data Access Pattern (MANDATORY - Clean Architecture)

**GOLDEN RULE**: Controllers, Services, and ALL non-Repository code MUST NEVER reference `SqlDbContext` or EF Core types.

### Three-Layer Data Access Flow
```
Controller → [Service] → Repository → SqlDbContext
   (HTTP)     (Business)  (Data Access)
```

### Repository Architecture
- **Interfaces**: `TSIC.Contracts/Repositories/I<Entity>Repository.cs` (abstractions)
- **Implementations**: `TSIC.Infrastructure/Repositories/<Entity>Repository.cs` (concrete EF Core code)
- **20+ repositories exist** - check `TSIC.Contracts/Repositories/` for all available interfaces
- **Dependency injection**: `AddScoped<IRepository, Repository>()` in Program.cs

### When to Create a New Repository Method
Create repository methods for:
- ✅ Data access logic (any EF Core query/include/join)
- ✅ Multi-entity queries returning DTOs (repository owns the EF Core complexity)
- ✅ Complex filtering or projection

Keep in service for:
- ✅ Business rule orchestration (calling multiple repos)
- ✅ Email/external service integration
- ✅ Business calculations

### Pattern Examples

**CORRECT - Controller with Repository**:
```csharp
public class FamilyController : ControllerBase {
    private readonly IFamilyRepository _familyRepo;
    
    public async Task<IActionResult> GetFamily(string id) {
        var family = await _familyRepo.GetByIdAsync(id);
        return Ok(family);
    }
}
```

**CORRECT - Complex Query in Repository (returns DTO)**:
```csharp
public interface ITeamRepository {
    Task<TeamDetailsDto?> GetTeamWithPlayersAsync(Guid teamId);
}

public class TeamRepository : ITeamRepository {
    public async Task<TeamDetailsDto?> GetTeamWithPlayersAsync(Guid teamId) {
        return await _context.Teams
            .AsNoTracking()
            .Include(t => t.Players)
            .Where(t => t.TeamId == teamId)
            .Select(t => new TeamDetailsDto {
                TeamId = t.TeamId,
                TeamName = t.Name,
                PlayerCount = t.Players.Count
            })
            .FirstOrDefaultAsync();
    }
}
```

**WRONG - NEVER DO THIS**:
```csharp
// ❌ SqlDbContext in controller
public class FamilyController : ControllerBase {
    private readonly SqlDbContext _db;  // NEVER
}

// ❌ EF Core queries in service
public class PaymentService : IPaymentService {
    private readonly SqlDbContext _context;  // NEVER
    var registrations = await _context.Registrations.ToListAsync();  // NEVER
}
```

### Repository Decision Matrix

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

## C# DTO Standards (OpenAPI Critical)

**Use `init` properties with `required` keyword** - NOT positional parameters (ensures Swagger generates correct `required` fields):

```csharp
// ✅ CORRECT - Generates proper TypeScript types
public record LoginResponseDto {
    public required string Token { get; init; }
    public required AuthenticatedUser User { get; init; }
}

// ❌ WRONG - Positional params break OpenAPI required field generation
public record LoginResponseDto(string Token, AuthenticatedUser User);
```

**Constructor calls**: Use object initializers:
```csharp
return new LoginResponseDto { Token = token, User = user };
```

## Angular Signal Patterns

**Use signals for state, observables for HTTP/streams**:

```typescript
// Service owns domain state
@Injectable({ providedIn: 'root' })
export class AuthService {
  // Public readonly signal - components READ, service WRITES
  public readonly currentUser = signal<AuthenticatedUser | null>(null);
  
  // HTTP returns Observable
  login(credentials: LoginRequest): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/login`, credentials)
      .pipe(tap(response => this.currentUser.set(response.user)));
  }
}

// Component reads service signal
export class HeaderComponent {
  private readonly auth = inject(AuthService);
  username = computed(() => this.auth.currentUser()?.username || '');
}
```

**Component local state**: Signals, not `BehaviorSubject`:
```typescript
export class WizardComponent {
  currentStep = signal(1);  // ✅ Signal for UI state
  isLoading = signal(false);
}
```

## TypeScript API Types (CRITICAL)

**ALWAYS import from auto-generated models** - NEVER create duplicate local interfaces:

```typescript
// ✅ CORRECT - Import from generated models
import { TeamsMetadataResponse, ClubTeamDto } from '../../../core/api/models';

@Injectable({ providedIn: 'root' })
export class TeamService {
  getMetadata(): Observable<TeamsMetadataResponse> {
    return this.http.get<TeamsMetadataResponse>(`${this.apiUrl}/metadata`);
  }
}

// ❌ WRONG - Local interface duplicates generated type
interface TeamsMetadataResponse { clubId: number; clubName: string; }
```

Generated models: `src/app/core/api/models/index.ts` (auto-generated, read-only)

## Design System & Styling

**Use CSS variables for all colors** (supports 8 dynamic palettes):

```scss
// ✅ CORRECT - Palette-aware
.my-button {
  background: var(--bs-primary);
  color: var(--bs-light);
  border: 1px solid var(--border-color);
}

// ❌ WRONG - Hardcoded colors won't adapt to palette changes
.my-button {
  background: #0ea5e9;
  color: #ffffff;
}
```

**Key variables**: `--bs-primary`, `--bs-success`, `--bs-danger`, `--bs-light`, `--bs-dark`, `--bs-body-bg`, `--bs-card-bg`

**Glassmorphic design**: Use backdrop-blur, subtle gradients, inset highlights (see `styles.scss`)

## File Locations & Project Structure

**Backend**:
- **Repository Interfaces**: `TSIC.Contracts/Repositories/` (in Contracts project, not Application)
- **Repository Implementations**: `TSIC.Infrastructure/Repositories/`
- **Services**: `TSIC.Application/Services/` (business logic, NOT data access)
- **DTOs**: `TSIC.Application/Services/<Feature>/` (grouped by domain feature)
- **Domain Entities**: `TSIC.Domain/Entities/`
- **Database Context**: `TSIC.Infrastructure/Data/SqlDbContext/`

**Frontend**:
- **Services**: `src/app/core/services/` (but most are deprecated - use signals instead)
- **Components**: `src/app/views/` or `src/app/layouts/` (standalone with `inject()`)
- **Shared Components**: `src/app/shared/` and `src/app/shared-ui/`
- **Guards**: `src/app/infrastructure/guards/auth.guard.ts` (unified guard, all scenarios)
- **Interceptors**: `src/app/infrastructure/interceptors/`
- **API Models** (AUTO-GENERATED, read-only): `src/app/core/api/models/index.ts`
- **Types**: `src/types/` for custom TypeScript types (if not generated)

**Critical**: 20+ repository implementations exist - **check `TSIC.Contracts/Repositories/` first** before creating new data access code.

## Documentation Reference & Key Patterns

**Before coding, ALWAYS check**:
1. `docs/REPOSITORY-PATTERN-STANDARDS.md` - **MANDATORY reading** for any backend data access work
2. `docs/SERVICE-LAYER-STANDARDS.md` - Business logic layer design & when to use services vs repositories
3. `docs/AI-AGENT-CODING-CONVENTIONS.md` - Project-specific conventions (use "CWCC" code in requests)
4. `docs/auth-guard-documentation.md` - Authentication & route protection patterns
5. `docs/authorization-policies.md` - API-level authorization rules & policies
6. `docs/jobpath-authorization-implementation.md` - JobPath validation in endpoints
7. `docs/angular-signal-patterns.md` - Modern Angular state management
8. `docs/DESIGN-SYSTEM.md` - UI/styling & CSS variable usage
9. `docs/CODING-STANDARDS-ENFORCEMENT.md` - DTO generation & TypeScript types
10. `docs/clean-architecture-implementation.md` - Layer separation & dependency flow

**Apply the Pre-change Checklist before any edits**:
- Read full scope of files you'll touch
- For CSS/layout changes, trace DOM hierarchy and check parent `overflow`/sticky ancestors
- Verify caller/callee contracts before changing signatures
- Ask clarifying questions if confidence < 90% (don't guess)

## External Integrations & Secrets

### Secret Management Patterns
**NEVER commit secrets to source control** - use `appsettings.Development.json` or environment variables:

```csharp
// ✅ CORRECT - Supports both appsettings and environment variables
var login = _config["AuthorizeNet:SandboxLoginId"] 
    ?? Environment.GetEnvironmentVariable("ADN_SANDBOX_LOGINID");
```

### Authorize.Net (Payment Processing)
- **Development**: Sandbox credentials in `appsettings.Development.json` or env vars (`ADN_SANDBOX_LOGINID`, `ADN_SANDBOX_TRANSACTIONKEY`)
- **Production**: Credentials loaded from database per Customer/Job (NEVER in config files)
- Service: `AdnApiService` - handles transactions, subscriptions, profiles

### VerticalInsure/RegSaver (Insurance)
- **Environment variables**: `VI_DEV_SECRET`, `VI_PROD_SECRET`, `VI_BASE_URL`
- **Named HttpClient**: "verticalinsure" with base URL from config
- Service: `VerticalInsureService` - policy purchases, card tokenization

### USA Lacrosse (Member Validation)
- **Configuration**: `UsLaxOptions` class with `ClientId`, `Secret`, `Username`, `Password`
- **Pattern**: Strongly-typed options via `IOptions<UsLaxOptions>`
- Service: `UsLaxService` - member number validation

### Email (Amazon SES)
- **Configuration**: `EmailSettings` in appsettings
  - `EmailingEnabled` (bool) - global kill switch
  - `SandboxMode` (bool) - suppress quota warnings in dev
  - `AwsRegion` (string) - explicit AWS region
- Service: `EmailService` - templated email sending

## Common Pitfalls to Avoid

1. **Don't edit generated files**: Anything in `core/api/models/` is auto-generated
2. **No DbContext in controllers**: Always use repository pattern
3. **No positional DTOs**: Use `init` properties with `required` keyword
4. **No hardcoded colors**: Use CSS variables for palette support
5. **No `BehaviorSubject` for state**: Use signals instead
6. **No `*ngIf`/`*ngFor`**: Use modern `@if`/`@for` syntax
7. **Regenerate after DTO changes**: Run `2-Regenerate-API-Models.ps1` script
8. **Never commit secrets**: Use appsettings.Development.json (gitignored) or environment variables
9. **Don't change CSS positioning without tracing hierarchy**: Identify sticky/overflow ancestors and layout context before changing placement.
10. **Don't proceed when unsure**: If you can't explain why a change will work, stop and ask for clarification instead of guessing.

## Quick Commands Reference

```powershell
# Build & run API
dotnet run --project TSIC-Core-Angular/src/backend/TSIC.API/TSIC.API.csproj

# Regenerate TypeScript API models
.\scripts\2-Regenerate-API-Models.ps1

# Format code
dotnet format TSIC-Core-Angular.sln

# Angular dev server
cd TSIC-Core-Angular/src/frontend/tsic-app && npm start
```
