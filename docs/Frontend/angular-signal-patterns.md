# Angular Signal Architecture Patterns

## Overview

This document defines the standard patterns for using Angular signals in the TSIC application. Following these patterns ensures consistent, maintainable, and scalable code as the project grows.

---

## Core Principles

### 1. **Signals for State, Observables for Streams**
- **Signals**: Use for reactive state (data that changes over time and UI needs to react to)
- **Observables**: Use for HTTP requests and event streams (one-time or continuous async operations)

### 2. **Clear Ownership Boundaries**
- **Services**: Own domain/business state
- **Components**: Own UI state and local data
- **No Shared Mutations**: Only the owner updates its signals

### 3. **Favor Signals Over Observables**
- Avoid `BehaviorSubject`, `Subject`, `ReplaySubject` for state management
- Use signals for component properties instead of observables
- Keep observables only for HTTP calls and event streams

---

## Pattern 1: Service Domain State Signals

**Use Case**: Global application state that multiple components need to access.

### ✅ Correct Pattern

```typescript
// auth.service.ts
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  
  // Public readonly signal - components can READ, service WRITES
  public readonly currentUser = signal<AuthenticatedUser | null>(null);
  
  // HTTP method returns Observable
  login(credentials: LoginRequest): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/login`, credentials)
      .pipe(
        tap(response => {
          this.setToken(response.accessToken);
          this.initializeFromToken();  // ← Updates signal internally
        })
      );
  }
  
  // Private method updates the signal
  private initializeFromToken(): void {
    const token = this.getToken();
    if (!token) {
      this.currentUser.set(null);  // ← Service owns this update
      return;
    }
    
    const payload = this.decodeToken(token);
    const user: AuthenticatedUser = {
      username: payload.username,
      regId: payload.regId,
      jobPath: payload.jobPath
    };
    this.currentUser.set(user);  // ← Service owns this update
  }
  
  // Getter for convenience (returns signal value)
  getCurrentUser(): AuthenticatedUser | null {
    return this.currentUser();
  }
  
  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    this.currentUser.set(null);  // ← Service owns this update
    this.router.navigate(['/tsic/login']);
  }
}
```

### Components Read Service Signals

```typescript
// layout.component.ts
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  
  // Component's own signal, derived from service
  username = signal('');
  
  constructor() {
    // Read service signal value on init
    const user = this.auth.getCurrentUser();
    this.username.set(user?.username || '');
  }
  
  // Or use computed signal for reactivity
  username = computed(() => this.auth.currentUser()?.username || '');
}
```

### ❌ Anti-Pattern: Using BehaviorSubject

```typescript
// ❌ DON'T DO THIS
export class AuthService {
  private currentUserSubject = new BehaviorSubject<User | null>(null);
  public currentUser$ = this.currentUserSubject.asObservable();
  
  login() {
    return this.http.post(...).pipe(
      tap(user => this.currentUserSubject.next(user))  // ❌ Use signal.set() instead
    );
  }
}
```

---

## Pattern 2: Component UI State Signals

**Use Case**: Component-specific loading states, form validation, UI toggles.

### ✅ Correct Pattern

```typescript
// login.component.ts
@Component({...})
export class LoginComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  
  // Component owns and manages these UI signals
  isLoading = signal(false);
  errorMessage = signal('');
  submitted = signal(false);
  showPassword = signal(false);
  
  form = this.fb.group({
    username: ['', Validators.required],
    password: ['', Validators.required]
  });
  
  onSubmit() {
    this.submitted.set(true);  // ← Component controls UI state
    if (this.form.invalid) return;
    
    this.isLoading.set(true);  // ← Component controls loading
    this.errorMessage.set('');  // ← Component controls errors
    
    const credentials: LoginRequest = {
      username: this.form.get('username')?.value ?? '',
      password: this.form.get('password')?.value ?? ''
    };
    
    // Subscribe to Observable, update signals on response
    this.authService.login(credentials).subscribe({
      next: () => {
        this.isLoading.set(false);  // ← Component updates on success
        this.router.navigate(['/tsic/role-selection']);
      },
      error: (error) => {
        this.isLoading.set(false);  // ← Component updates on error
        this.errorMessage.set(error.error?.message || 'Login failed');
      }
    });
  }
  
  toggleShowPassword() {
    this.showPassword.set(!this.showPassword());  // ← Component controls toggle
  }
}
```

### Template Usage

```html
<!-- Use signal syntax () in templates -->
<input 
  [type]="showPassword() ? 'text' : 'password'"
  [class.is-invalid]="submitted() && form.get('password')?.invalid">

<div *ngIf="errorMessage()" class="alert alert-danger">
  {{ errorMessage() }}
</div>

<button [disabled]="isLoading() || form.invalid">
  <span *ngIf="!isLoading()">Sign In</span>
  <span *ngIf="isLoading()">
    <span class="spinner-border spinner-border-sm"></span>
    Signing in...
  </span>
</button>
```

### ❌ Anti-Pattern: Using Regular Properties

```typescript
// ❌ DON'T DO THIS
export class LoginComponent {
  isLoading = false;  // ❌ Use signal instead
  errorMessage = '';  // ❌ Use signal instead
  
  onSubmit() {
    this.isLoading = true;  // ❌ Won't trigger change detection reliably
  }
}
```

---

## Pattern 3: Component Data Signals

**Use Case**: Data fetched from services that the component needs to display.

### ✅ Correct Pattern

```typescript
// role-selection.component.ts
@Component({...})
export class RoleSelectionComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  
  // Component owns data signal
  registrations = signal<RegistrationRoleDto[]>([]);
  
  // Component owns UI state signals
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);
  
  constructor() {
    this.loadRegistrations();
  }
  
  private loadRegistrations(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    
    // Subscribe to service Observable, populate component signal
    this.authService.getAvailableRegistrations().subscribe({
      next: (data) => {
        this.registrations.set(data);  // ← Component stores data in signal
        this.isLoading.set(false);
      },
      error: (error) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to load');
      }
    });
  }
  
  selectRole(registration: RegistrationRoleDto): void {
    this.isLoading.set(true);
    
    this.authService.selectRegistration(registration.regId).subscribe({
      next: () => {
        const jobPath = this.authService.getJobPath();
        this.router.navigate([jobPath]);
      },
      error: (error) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to select role');
      }
    });
  }
}
```

### Template Usage

```html
@if (isLoading()) {
  <div class="spinner">Loading...</div>
}

@if (errorMessage()) {
  <div class="alert alert-danger">{{ errorMessage() }}</div>
}

@for (registration of registrations(); track registration.regId) {
  <div class="registration-card">
    <h3>{{ registration.jobName }}</h3>
    <button (click)="selectRole(registration)">Select</button>
  </div>
}
```

---

## Pattern 4: HTTP Service Methods

**Use Case**: Making HTTP requests to backend APIs.

### ✅ Correct Pattern

```typescript
// Service returns Observable, uses tap() for side effects
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  public readonly currentUser = signal<AuthenticatedUser | null>(null);
  
  // Returns Observable<T> - NOT Promise
  login(credentials: LoginRequest): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/login`, credentials)
      .pipe(
        tap(response => {
          // Side effect: update tokens and signal
          this.setToken(response.accessToken);
          if (response.refreshToken) {
            this.setRefreshToken(response.refreshToken);
          }
          this.initializeFromToken();  // Updates currentUser signal
        })
      );
  }
  
  getAvailableRegistrations(): Observable<RegistrationRoleDto[]> {
    return this.http.get<LoginResponse>(`${this.apiUrl}/registrations`)
      .pipe(
        map(response => response.registrations)  // Transform data
      );
  }
  
  selectRegistration(regId: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/select-registration`, { regId })
      .pipe(
        tap(response => {
          this.setToken(response.accessToken);
          this.initializeFromToken();  // Updates currentUser signal
        })
      );
  }
  
  refreshAccessToken(): Observable<AuthTokenResponse> {
    const refreshToken = this.getRefreshToken();
    
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available'));
    }
    
    return this.http.post<AuthTokenResponse>(`${this.apiUrl}/refresh`, { refreshToken })
      .pipe(
        tap(response => {
          this.setToken(response.accessToken);
          this.initializeFromToken();
        }),
        catchError(error => {
          this.logout();
          return throwError(() => error);
        })
      );
  }
}
```

### ❌ Anti-Pattern: Using async/await for Simple Requests

```typescript
// ❌ DON'T DO THIS (unless you have complex sequential flows)
async login(credentials: LoginRequest): Promise<AuthTokenResponse> {
  const response$ = this.http.post<AuthTokenResponse>(...);
  const response = await firstValueFrom(response$);  // ❌ Loses Observable benefits
  this.currentUser.set(user);
  return response;
}
```

### ✅ When async/await IS Appropriate

```typescript
// ✅ OK for complex sequential operations
async processMultiStepWorkflow(userId: string): Promise<void> {
  this.loadingSignal.set(true);
  
  try {
    // Step 1: Get user
    const user = await firstValueFrom(
      this.http.get<User>(`/users/${userId}`)
    );
    this.userSignal.set(user);
    
    // Step 2: Get profile (depends on step 1)
    const profile = await firstValueFrom(
      this.http.get<Profile>(`/profiles/${user.profileId}`)
    );
    this.profileSignal.set(profile);
    
    // Step 3: Get settings (depends on step 1)
    const settings = await firstValueFrom(
      this.http.get<Settings>(`/settings/${user.id}`)
    );
    this.settingsSignal.set(settings);
    
  } catch (error) {
    this.errorSignal.set(error.message);
  } finally {
    this.loadingSignal.set(false);
  }
}
```

---

## Pattern 5: Computed Signals

**Use Case**: Derived state that automatically updates when dependencies change.

### ✅ Correct Pattern

```typescript
export class UserProfileComponent {
  private readonly authService = inject(AuthService);
  
  // Computed signal automatically updates when currentUser changes
  displayName = computed(() => {
    const user = this.authService.currentUser();
    return user ? `${user.firstName} ${user.lastName}` : 'Guest';
  });
  
  isAdmin = computed(() => {
    const user = this.authService.currentUser();
    return user?.roles?.includes('Admin') ?? false;
  });
  
  canEdit = computed(() => {
    return this.isAdmin() || this.isOwner();
  });
}
```

### Template Usage

```html
<h1>Welcome, {{ displayName() }}</h1>

@if (isAdmin()) {
  <button>Admin Panel</button>
}

@if (canEdit()) {
  <button>Edit Profile</button>
}
```

---

## Pattern 6: `effect()` is BANNED

**Do not use `effect()`. There is no sanctioned use of it in this codebase.**

### Why

An `effect()` re-runs whenever *any* signal it read changes. That is fine until the effect
also *writes* a signal it transitively reads — then it re-triggers itself and reverts the
write a frame later. This is not hypothetical: a constructor effect in the registration
detail panel seeded editable form state from `detail()` while transitively reading
`profileValues`, so every user edit was silently wiped (fixed in `02e8bafd`).

The failure is silent, intermittent, and survives code review, because nothing in the
effect body looks wrong. The replacements below make the dependency graph *structural* —
a `computed()` cannot write what it reads, and a `linkedSignal` reseeds only when its
`source` changes. Correctness stops depending on discipline.

Real bugs this ban has already surfaced: a theme editor whose "reload on theme change"
never fired (an effect with no signal dependency), an HTTP duplicate-check firing on every
keystroke with racing responses, a caret that jumped to the end of every family-wizard
field being edited, and an inert public setter on the insurance offer state.

### What to use instead

| The job the effect was doing | Use | Reference |
|---|---|---|
| Pure derivation (including clamping / write-back) | `computed()` | `standings-tab.component.ts` `activeAgTabIndex` |
| Editable local copy seeded from an `input()` | `linkedSignal({ source, computation })` | `registration-detail-panel.component.ts` `profileValues` |
| Snapshot a baseline without depending on it | `linkedSignal` + `untracked()` in the computation | `registration-detail-panel.component.ts` `snapshotProfile` |
| React to an `@Input`/`input()` change (seed state, fire HTTP) | `ngOnChanges` gated on `changes['key']` | `edit-game-modal.component.ts` |
| React to a **service** signal | `toObservable(sig).pipe(…, takeUntilDestroyed()).subscribe()` | `email-log.component.ts` |
| Fire HTTP on a user action | An explicit callback | `setActiveTab()` in the detail panel |
| Debounced typeahead → HTTP | `Subject` + `debounceTime` + `distinctUntilChanged` + `switchMap` | `admin-form-modal.component.ts` |
| DOM work that needs the rendered view | `ngOnChanges` sets a flag → `ngAfterViewChecked` drains it | `ladt-sibling-grid.component.ts` |
| A one-shot command between components | `Subject<void>` on the service, not a `signal(false)` pulse | `menu-state.service.ts` `customizeDashboard$` |

### ❌ Anti-Pattern: a signal used as an event

A one-shot command is not state. Modelling it as `signal(false)` forces the consumer to
acknowledge-and-reset it, and the only place to observe it is — inevitably — an `effect()`.

```typescript
// ❌ DON'T DO THIS
customizeDashboardRequested = signal(false);
requestCustomizeDashboard() { this.customizeDashboardRequested.set(true); }
ackCustomizeDashboard()     { this.customizeDashboardRequested.set(false); }

// ✅ An event stream is an Observable
private readonly _customizeDashboard = new Subject<void>();
readonly customizeDashboard$ = this._customizeDashboard.asObservable();
requestCustomizeDashboard() { this._customizeDashboard.next(); }
```

### On write-through

Applying a side effect where the state changes — the `setTheme()` shape below — is the
**correct** pattern, not an anti-pattern. It is explicit, greppable, and runs exactly once.

```typescript
// ✅ CORRECT — write-through at the mutation site
setTheme(theme: 'light' | 'dark'): void {
  this.theme.set(theme);
  document.documentElement.setAttribute('data-bs-theme', theme);
  localStorage.setItem('theme', theme);
}
```

---

## Quick Reference Table

| Pattern | Owner | Updates | Example Use Case |
|---------|-------|---------|------------------|
| **Service Domain Signal** | Service | Service (via `tap()`) | `currentUser`, `currentJob`, `theme` |
| **Component UI Signal** | Component | Component (in `.subscribe()`) | `isLoading`, `errorMessage`, `submitted` |
| **Component Data Signal** | Component | Component (from service) | `registrations`, `menuItems`, `bulletins` |
| **Computed Signal** | Component/Service | Automatic (derived) | `displayName`, `isAdmin`, `filteredList` |
| **Linked Signal** | Component | Reseeds when `source` changes | `profileValues`, `refundAmount` |
| **Effect** | — | — | **BANNED — see Pattern 6** |
| **HTTP Observable** | Service | N/A (returns Observable) | `login()`, `fetchData()`, `saveSettings()` |

---

## Migration Checklist

When refactoring existing code:

- [ ] Replace `BehaviorSubject`/`Subject` with `signal()`
- [ ] Replace `.next()` calls with `.set()` calls
- [ ] Replace `.value` reads with signal function calls `()`
- [ ] Replace `.asObservable()` patterns with direct signal access
- [ ] Update templates: `user` → `user()`
- [ ] Use `@if` and `@for` instead of `*ngIf` and `*ngFor` where possible
- [ ] Convert component properties to signals where state is reactive
- [ ] Keep HTTP methods returning `Observable<T>`
- [ ] Use `.subscribe()` in components to update signals

---

## Testing Patterns

### Testing Components with Signals

```typescript
describe('LoginComponent', () => {
  it('should update isLoading signal on submit', () => {
    const component = new LoginComponent();
    
    expect(component.isLoading()).toBe(false);
    
    component.onSubmit();
    
    expect(component.isLoading()).toBe(true);
  });
  
  it('should set error message on login failure', fakeAsync(() => {
    const authService = { login: vi.fn().mockReturnValue(throwError(() => new Error('Login failed'))) };
    
    const component = new LoginComponent(authService);
    component.onSubmit();
    tick();
    
    expect(component.errorMessage()).toContain('Login failed');
    expect(component.isLoading()).toBe(false);
  }));
});
```

### Testing Services with Signals

```typescript
describe('AuthService', () => {
  it('should update currentUser signal on login', fakeAsync(() => {
    const service = TestBed.inject(AuthService);
    const httpMock = TestBed.inject(HttpTestingController);
    
    expect(service.currentUser()).toBeNull();
    
    service.login({ username: 'test', password: 'pass' }).subscribe();
    
    const req = httpMock.expectOne('/api/auth/login');
    req.flush({ accessToken: 'fake-token', refreshToken: 'fake-refresh' });
    tick();
    
    expect(service.currentUser()).toBeTruthy();
    expect(service.currentUser()?.username).toBe('test');
  }));
});
```

---

## Common Mistakes to Avoid

### ❌ Don't Mutate Signal Values Directly

```typescript
// ❌ WRONG
users = signal<User[]>([]);
addUser(user: User) {
  this.users().push(user);  // ❌ Mutates array, doesn't trigger reactivity
}

// ✅ CORRECT
users = signal<User[]>([]);
addUser(user: User) {
  this.users.set([...this.users(), user]);  // ✅ Creates new array
}
```

### ❌ Don't Subscribe to Signals

```typescript
// ❌ WRONG - signals are not observables
this.authService.currentUser.subscribe(...);  // ❌ Error!

// ✅ CORRECT - derive with computed()
displayName = computed(() => this.authService.currentUser()?.name ?? '');

// ✅ CORRECT - to *act* on a service signal changing, bridge to an Observable.
//    Never effect() — see Pattern 6.
toObservable(this.authService.currentUser)
  .pipe(distinctUntilChanged(), takeUntilDestroyed())
  .subscribe(user => this.loadDashboard());
```

### ❌ Don't Use Signals for One-Time Values

```typescript
// ❌ WRONG - component title doesn't change
title = signal('Login Page');

// ✅ CORRECT - use regular property
title = 'Login Page';
```

---

## Summary

1. **Services manage domain signals** - Updated via `tap()` in HTTP responses
2. **Components manage UI signals** - Updated in `.subscribe()` callbacks
3. **HTTP methods return Observables** - Not Promises (unless complex sequential flows)
4. **Use `computed()` for derived state** - Automatic reactivity
5. **NEVER use `effect()`** - It is banned; see Pattern 6 for the replacement per job
6. **Always use `.set()` to update signals** - Never mutate directly
7. **Use signal syntax `()` in templates** - Clean and explicit

Following these patterns will keep the codebase consistent, maintainable, and scalable as TSIC grows.
