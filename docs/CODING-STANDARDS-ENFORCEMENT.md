# Coding Standards Enforcement Strategy

**Date:** November 1, 2025  
**Purpose:** Ensure Angular coding standards are consistently followed across the codebase

## Overview

This document outlines automated and manual strategies to enforce our coding standards, particularly around **signals-first architecture** and avoiding observables in components.

---

## Interfaces & Services

- Convention: Place public service interfaces in separate files named `I*.cs` alongside their implementations within `TSIC.API/Services`.
- Scope: Applies to outward-facing services used via DI. Internal helpers and local DTO records may remain co-located.
- Rationale: Improves discoverability, testing/mocking, and keeps diffs focused (interface vs implementation changes).
- Examples: `IPaymentService.cs`, `IPlayerBaseTeamFeeResolverService.cs`, `ITeamLookupService.cs`, `IAccountingService.cs`, `IJobLookupService.cs`, `ITextSubstitutionService.cs`, `IPlayerRegConfirmationService.cs`, `IAdnApiService.cs`.

This convention is now enforced for new services; legacy services should be brought in line opportunistically during refactors.

---

## 1. Code Review Checklist

### Pre-Commit Checklist
Before committing code, verify:

- [ ] **No `.subscribe()` calls in components** (except in `ngOnDestroy` cleanup)
- [ ] **Signals originate in services**, not components
- [ ] **Services expose readonly signals** (`signal.asReadonly()`)
- [ ] **Components use callback-based service methods** instead of observables
- [ ] **No `BehaviorSubject` or `Subject` in new code**
- [ ] **Bootstrap modals instead of `window.confirm()`**
- [ ] **Standalone components** (no NgModule unless legacy)
- [ ] **New control flow** (`@if`, `@for`) instead of `*ngIf`, `*ngFor`

### Pull Request Review Checklist
Reviewers should reject PRs that contain:

- ❌ Component files with `.subscribe(` 
- ❌ Services returning `Observable<T>` for new features
- ❌ `new Subject()` or `new BehaviorSubject()`
- ❌ `window.confirm()` or `window.alert()`
- ❌ `*ngIf` or `*ngFor` in new components
- ❌ `@NgModule` decorators in new code

---

## 2. Automated Linting Rules

### Custom ESLint Rules (To Be Implemented)

Create `.eslintrc.json` in `tsic-app/` directory:

```json
{
  "extends": [
    "plugin:@angular-eslint/recommended",
    "plugin:@typescript-eslint/recommended"
  ],
  "rules": {
    "no-restricted-syntax": [
      "error",
      {
        "selector": "CallExpression[callee.property.name='subscribe']",
        "message": "Avoid .subscribe() in components. Use signals and callbacks instead."
      },
      {
        "selector": "CallExpression[callee.name='confirm']",
        "message": "Use Bootstrap modal confirmations instead of window.confirm()"
      },
      {
        "selector": "CallExpression[callee.name='alert']",
        "message": "Use Toast notifications instead of window.alert()"
      }
    ],
    "no-restricted-imports": [
      "error",
      {
        "paths": [
          {
            "name": "rxjs",
            "importNames": ["Subject", "BehaviorSubject"],
            "message": "Use signals instead of Subjects in new code. Import from rxjs/operators if needed."
          }
        ]
      }
    ]
  }
}
```

### TypeScript Compiler Options

Update `tsconfig.json` to enforce strict typing:

```json
{
  "compilerOptions": {
    "strict": true,
    "noImplicitAny": true,
    "strictNullChecks": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true
  }
}
```

---

## 3. Git Pre-Commit Hooks

### Install Husky for Git Hooks

```powershell
# In tsic-app directory
npm install --save-dev husky lint-staged
npx husky init
```

### Configure Pre-Commit Hook

Create `.husky/pre-commit`:

```bash
#!/bin/sh
. "$(dirname "$0")/_/husky.sh"

# Run linting on staged files
npx lint-staged

# Custom validation script
node scripts/validate-coding-standards.js
```

### Lint-Staged Configuration

Add to `package.json`:

```json
{
  "lint-staged": {
    "*.ts": [
      "eslint --fix",
      "prettier --write"
    ],
    "*.html": [
      "prettier --write"
    ]
  }
}
```

---

## 4. Custom Validation Script

Create `scripts/validate-coding-standards.js`:

```javascript
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

// Get staged files
const stagedFiles = execSync('git diff --cached --name-only --diff-filter=ACM')
  .toString()
  .trim()
  .split('\n')
  .filter(file => file.endsWith('.ts') && file.includes('/src/'));

let hasViolations = false;

stagedFiles.forEach(file => {
  const content = fs.readFileSync(file, 'utf8');
  const lines = content.split('\n');

  // Check for .subscribe() in component files
  if (file.includes('.component.ts')) {
    lines.forEach((line, index) => {
      if (line.includes('.subscribe(') && !line.includes('ngOnDestroy')) {
        console.error(`❌ ${file}:${index + 1}`);
        console.error(`   Found .subscribe() in component. Use signals and callbacks instead.`);
        console.error(`   Line: ${line.trim()}`);
        hasViolations = true;
      }
    });
  }

  // Check for window.confirm or window.alert
  lines.forEach((line, index) => {
    if (line.match(/window\.(confirm|alert)\(/)) {
      console.error(`❌ ${file}:${index + 1}`);
      console.error(`   Found window.confirm/alert. Use Bootstrap modals instead.`);
      console.error(`   Line: ${line.trim()}`);
      hasViolations = true;
    }
  });

  // Check for old control flow syntax in new files
  if (!content.includes('@Component')) return; // Skip non-components
  
  const hasOldSyntax = content.match(/\*ngIf|\*ngFor|\*ngSwitch/);
  const isNewFile = !execSync(`git log --oneline ${file}`).toString().trim();
  
  if (hasOldSyntax && isNewFile) {
    console.error(`❌ ${file}`);
    console.error(`   New component uses old control flow (*ngIf, *ngFor).`);
    console.error(`   Use new syntax: @if, @for, @switch`);
    hasViolations = true;
  }
});

if (hasViolations) {
  console.error('\n❌ Coding standards violations detected!');
  console.error('Please fix the issues above before committing.');
  console.error('See docs/ANGULAR-CODING-STANDARDS.md for guidelines.');
  process.exit(1);
}

console.log('✅ All coding standards checks passed!');
```

---

## 5. CI/CD Pipeline Integration

### GitHub Actions Workflow

Create `.github/workflows/code-quality.yml`:

```yaml
name: Code Quality Checks

on:
  pull_request:
    branches: [master, develop]
  push:
    branches: [master, develop]

jobs:
  lint:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup Node.js
        uses: actions/setup-node@v3
        with:
          node-version: '22'
          cache: 'npm'
          cache-dependency-path: TSIC-Core-Angular/src/frontend/tsic-app/package-lock.json
      
      - name: Install dependencies
        working-directory: TSIC-Core-Angular/src/frontend/tsic-app
        run: npm ci
      
      - name: Run ESLint
        working-directory: TSIC-Core-Angular/src/frontend/tsic-app
        run: npm run lint
      
      - name: Validate Coding Standards
        working-directory: TSIC-Core-Angular/src/frontend/tsic-app
        run: node scripts/validate-coding-standards.js
      
      - name: Check for Observable usage in components
        run: |
          if grep -r "\.subscribe(" TSIC-Core-Angular/src/frontend/tsic-app/src/app --include="*.component.ts" | grep -v "ngOnDestroy"; then
            echo "❌ Found .subscribe() in components!"
            exit 1
          fi
          echo "✅ No observable subscriptions in components"
```

---

## 6. Documentation in Code

### Component Template Comments

Add reminder comments to component templates:

```typescript
/**
 * CODING STANDARD REMINDER:
 * - Use signals from services, not observables
 * - Service methods should use callbacks, not return observables
 * - Expose computed signals for derived state
 * - Use Bootstrap modals for confirmations
 */
@Component({
  selector: 'app-example',
  standalone: true,
  // ...
})
export class ExampleComponent {
  // ✅ Good: Use service signals
  data = this.dataService.items;
  isLoading = this.dataService.isLoading;
  
  // ❌ Bad: Local signal for service data
  // data = signal<Item[]>([]);
  
  loadData(): void {
    // ✅ Good: Callback-based service method
    this.dataService.loadItems();
    
    // ❌ Bad: Subscribe in component
    // this.dataService.getItems().subscribe(...)
  }
}
```

---

## 7. Training & Onboarding

### New Developer Checklist

When onboarding new developers:

1. **Read** `docs/ANGULAR-CODING-STANDARDS.md`
2. **Review** example components in `src/app/admin/profile-migration/`
3. **Pair program** on first feature with senior developer
4. **Submit** sample PR for code review before working independently

### Code Review Training

Teach reviewers to look for:

- **Service structure**: Private writable signals, public readonly signals
- **Component structure**: Inject services, use their signals directly
- **Method patterns**: Callback-based service methods with `onSuccess`/`onError`
- **Error handling**: Centralized in service, exposed via error message signal
- **Loading states**: Managed in service, consumed in component

---

## 8. Quarterly Code Audits

### Automated Audit Script

Create `scripts/audit-codebase.js`:

```javascript
const fs = require('fs');
const path = require('path');
const glob = require('glob');

const srcDir = 'src/app';
const componentFiles = glob.sync(`${srcDir}/**/*.component.ts`);
const serviceFiles = glob.sync(`${srcDir}/**/*.service.ts`);

console.log('🔍 Auditing Codebase for Coding Standards...\n');

let violations = [];

// Check components for .subscribe()
componentFiles.forEach(file => {
  const content = fs.readFileSync(file, 'utf8');
  const subscribeCount = (content.match(/\.subscribe\(/g) || []).length;
  
  if (subscribeCount > 0) {
    violations.push({
      file,
      type: 'Component Observable',
      message: `Found ${subscribeCount} .subscribe() call(s)`
    });
  }
});

// Check services for Observable returns
serviceFiles.forEach(file => {
  const content = fs.readFileSync(file, 'utf8');
  const observableReturns = (content.match(/:\s*Observable</g) || []).length;
  
  if (observableReturns > 0) {
    violations.push({
      file,
      type: 'Service Observable',
      message: `Found ${observableReturns} Observable<> return type(s)`
    });
  }
});

// Print report
if (violations.length === 0) {
  console.log('✅ No coding standards violations found!');
} else {
  console.log(`❌ Found ${violations.length} potential violations:\n`);
  violations.forEach(v => {
    console.log(`${v.file}`);
    console.log(`  Type: ${v.type}`);
    console.log(`  ${v.message}\n`);
  });
}

// Generate report file
const report = {
  date: new Date().toISOString(),
  totalFiles: componentFiles.length + serviceFiles.length,
  violations: violations.length,
  details: violations
};

fs.writeFileSync('audit-report.json', JSON.stringify(report, null, 2));
console.log('📊 Report saved to audit-report.json');
```

Run quarterly:
```powershell
node scripts/audit-codebase.js
```

---

## 9. IDE Integration

### VS Code Settings

Add to `.vscode/settings.json`:

```json
{
  "typescript.tsdk": "node_modules/typescript/lib",
  "editor.codeActionsOnSave": {
    "source.fixAll.eslint": true
  },
  "eslint.validate": [
    "typescript",
    "html"
  ],
  "files.associations": {
    "*.component.ts": "typescript"
  },
  "editor.snippets": {
    "angular-service-signal": {
      "prefix": "ng-service-signal",
      "body": [
        "private readonly _${1:data} = signal<${2:Type}[]>([]);",
        "readonly ${1:data} = this._${1:data}.asReadonly();"
      ]
    }
  }
}
```

### Code Snippets

Create `.vscode/angular-snippets.code-snippets`:

```json
{
  "Signal-based Service": {
    "prefix": "ng-service-signals",
    "body": [
      "private readonly _${1:data} = signal<${2:Type}[]>([]);",
      "private readonly _isLoading = signal(false);",
      "private readonly _errorMessage = signal<string | null>(null);",
      "",
      "readonly ${1:data} = this._${1:data}.asReadonly();",
      "readonly isLoading = this._isLoading.asReadonly();",
      "readonly errorMessage = this._errorMessage.asReadonly();"
    ]
  },
  "Callback-based Service Method": {
    "prefix": "ng-service-method",
    "body": [
      "${1:methodName}(${2:params}, onSuccess: (result: ${3:ResultType}) => void, onError?: (error: any) => void): void {",
      "  this._isLoading.set(true);",
      "  this._errorMessage.set(null);",
      "  ",
      "  this.http.${4:get}<${3:ResultType}>(`\\${this.apiUrl}/${5:endpoint}`).subscribe({",
      "    next: (result) => {",
      "      this._isLoading.set(false);",
      "      onSuccess(result);",
      "    },",
      "    error: (error) => {",
      "      this._isLoading.set(false);",
      "      const message = error.error?.message || '${6:Error message}';",
      "      this._errorMessage.set(message);",
      "      if (onError) {",
      "        onError(error);",
      "      }",
      "    }",
      "  });",
      "}"
    ]
  }
}
```

---

## 10. Enforcement Priority

### Immediate (Week 1)
- ✅ Add code review checklist to PR template
- ✅ Update `ANGULAR-CODING-STANDARDS.md` with enforcement section
- ✅ Create validation script in `scripts/`

### Short-term (Month 1)
- [ ] Install and configure ESLint with custom rules
- [ ] Set up pre-commit hooks with Husky
- [ ] Create code snippets for VS Code

### Medium-term (Quarter 1)
- [ ] Implement GitHub Actions CI/CD checks
- [ ] Run first quarterly codebase audit
- [ ] Train team on coding standards

### Long-term (Ongoing)
- [ ] Quarterly audits and reports
- [ ] Update standards based on Angular releases
- [ ] Refactor legacy code incrementally

---

## Quick Reference Commands

```powershell
# Validate staged files before commit
node scripts/validate-coding-standards.js

# Run full codebase audit
node scripts/audit-codebase.js

# Fix ESLint issues automatically
npm run lint -- --fix

# Check for observable usage in components
grep -r ".subscribe(" src/app --include="*.component.ts" | grep -v "ngOnDestroy"
```

---

## Success Metrics

Track these metrics monthly:

1. **Observable Usage**: Number of `.subscribe()` calls in components
2. **Signal Adoption**: Percentage of services using signals vs observables
3. **Modal Usage**: Bootstrap modals vs `window.confirm()`
4. **Code Review Rejections**: PRs rejected due to standards violations
5. **Audit Violations**: Trending down over time

**Target**: Zero new violations after 3 months of enforcement.
