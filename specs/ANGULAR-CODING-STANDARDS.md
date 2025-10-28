# Angular Coding Standards - TSIC Project

## Template Control Flow (Angular 17+)

**ALWAYS use the new control flow syntax (`@if`, `@for`, `@switch`) instead of structural directives (`*ngIf`, `*ngFor`, `*ngSwitch`).**

### ✅ Preferred Pattern (Angular 17+)

```html
<!-- Conditional rendering -->
@if (user) {
  <p>Welcome, {{ user.name }}!</p>
} @else if (isLoading) {
  <p>Loading...</p>
} @else {
  <p>Please log in.</p>
}

<!-- List rendering -->
@for (item of items; track item.id) {
  <div class="item">{{ item.name }}</div>
} @empty {
  <p>No items found.</p>
}

<!-- Switch statements -->
@switch (status) {
  @case ('active') {
    <span class="badge-success">Active</span>
  }
  @case ('pending') {
    <span class="badge-warning">Pending</span>
  }
  @default {
    <span class="badge-secondary">Unknown</span>
  }
}

<!-- Defer for lazy loading -->
@defer (on viewport) {
  <heavy-component />
} @placeholder {
  <p>Loading component...</p>
} @loading {
  <spinner />
} @error {
  <p>Failed to load</p>
}
```

### ❌ Avoid (Old Structural Directives)

```html
<!-- DON'T use these anymore -->
<div *ngIf="user">Welcome, {{ user.name }}!</div>
<div *ngFor="let item of items">{{ item.name }}</div>
<div [ngSwitch]="status">
  <span *ngSwitchCase="'active'">Active</span>
</div>
```

### Benefits of New Control Flow

1. **Better performance** - Built-in optimization and smaller bundle sizes
2. **Type safety** - Better TypeScript inference in templates
3. **Cleaner syntax** - More readable and less "magic"
4. **Track by default** - `@for` requires explicit tracking
5. **Better error handling** - `@defer` with error states

## Dependency Injection

**ALWAYS use `inject()` function instead of constructor-based dependency injection.**

### ✅ Preferred Pattern (Modern Angular 14+)

```typescript
import { Component, inject } from '@angular/core';
import { AuthService } from './services/auth.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-my-component',
  standalone: true,
  templateUrl: './my-component.component.html'
})
export class MyComponent {
  private authService = inject(AuthService);
  private router = inject(Router);
  
  // For public/protected access:
  userService = inject(UserService);
  protected logger = inject(LoggerService);
}
```

### ❌ Avoid (Old Constructor Pattern)

```typescript
export class MyComponent {
  constructor(
    private authService: AuthService,
    private router: Router
  ) { }
}
```

### Benefits of `inject()` Pattern

1. **Cleaner syntax** - No constructor clutter
2. **Better testability** - Easier to mock dependencies
3. **Functional alignment** - Works seamlessly with functional guards, interceptors, and resolvers
4. **Type safety** - TypeScript inference works better
5. **Flexibility** - Can inject conditionally or in helper functions

## Component Architecture

### Standalone Components (Angular 14+)

Always use standalone components with explicit imports:

```typescript
@Component({
  selector: 'app-example',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './example.component.html'
})
```

### Component Structure

```typescript
import { Component, OnInit, inject } from '@angular/core';

@Component({
  selector: 'app-example',
  standalone: true,
  imports: [...],
  templateUrl: './example.component.html',
  styleUrls: ['./example.component.scss']
})
export class ExampleComponent implements OnInit {
  // 1. Injected dependencies first
  private authService = inject(AuthService);
  private router = inject(Router);
  
  // 2. Public properties
  data: any[] = [];
  isLoading = false;
  
  // 3. Lifecycle hooks
  ngOnInit(): void {
    this.loadData();
  }
  
  // 4. Public methods
  loadData(): void {
    // ...
  }
  
  // 5. Private methods
  private handleError(error: any): void {
    // ...
  }
}
```

## Services

### Service Pattern

```typescript
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Injectable({
  providedIn: 'root'
})
export class DataService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;
  
  getData(): Observable<Data[]> {
    return this.http.get<Data[]>(`${this.apiUrl}/data`);
  }
}
```

## Guards (Functional Pattern)

```typescript
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  
  if (authService.isAuthenticated()) {
    return true;
  }
  
  return router.createUrlTree(['/login']);
};
```

## Interceptors (Functional Pattern)

```typescript
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.getToken();
  
  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    });
  }
  
  return next(req);
};
```

## TypeScript Standards

### Strict Typing

- Always provide explicit types for function parameters and return values
- Avoid `any` type - use `unknown` or proper interfaces
- Use `null` or `undefined` explicitly when needed

### Interfaces vs Types

Prefer interfaces for object shapes:

```typescript
export interface User {
  id: string;
  username: string;
  email: string;
}
```

Use types for unions, intersections, or primitives:

```typescript
export type AuthStatus = 'authenticated' | 'unauthenticated' | 'pending';
export type UserId = string;
```

## RxJS Patterns

### Observable Naming

Suffix observables with `$`:

```typescript
user$ = this.userService.getCurrentUser();
isLoading$ = this.store.select(selectIsLoading);
```

### Unsubscribe Pattern

Use `takeUntilDestroyed()` (Angular 16+):

```typescript
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

export class MyComponent {
  private dataService = inject(DataService);
  
  ngOnInit(): void {
    this.dataService.getData()
      .pipe(takeUntilDestroyed())
      .subscribe(data => this.data = data);
  }
}
```

## Naming Conventions

- **Components**: PascalCase with `.component` suffix - `UserProfileComponent`
- **Services**: PascalCase with `.service` suffix - `AuthService`
- **Interfaces**: PascalCase - `User`, `AuthTokenResponse`
- **Variables/Properties**: camelCase - `userData`, `isLoading`
- **Constants**: UPPER_SNAKE_CASE - `API_BASE_URL`, `MAX_RETRIES`
- **Files**: kebab-case - `user-profile.component.ts`, `auth.service.ts`

## File Organization

```
src/app/
├── core/
│   ├── guards/
│   ├── interceptors/
│   ├── models/
│   └── services/
├── shared/
│   ├── components/
│   ├── directives/
│   └── pipes/
└── features/
    ├── auth/
    │   ├── login/
    │   └── role-selection/
    └── dashboard/
```

## Authentication Standards

### Token Storage

- Use `localStorage` for persistent auth tokens
- Key naming: `auth_token`
- Store minimal data in tokens (JWT claims)

### JWT Handling

```typescript
private decodeToken(token: string): any {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    return JSON.parse(jsonPayload);
  } catch (error) {
    return null;
  }
}
```

## Testing Standards

(To be added as testing patterns are established)

## Documentation

- Add JSDoc comments for public APIs
- Document complex business logic
- Keep README.md updated with setup instructions

---

**Last Updated**: October 28, 2025  
**Maintainer**: Development Team
