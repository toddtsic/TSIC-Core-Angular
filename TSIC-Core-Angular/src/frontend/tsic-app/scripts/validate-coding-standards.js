const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

console.log('🔍 Validating Coding Standards...\n');

// Get staged files (if running in pre-commit) or all TypeScript files
let filesToCheck = [];
try {
    const staged = execSync('git diff --cached --name-only --diff-filter=ACM', { encoding: 'utf8' });
    filesToCheck = staged.trim().split('\n').filter(f => f && f.endsWith('.ts') && f.includes('/src/'));
    if (filesToCheck.length === 0) {
        console.log('ℹ️  No staged TypeScript files to check.');
        process.exit(0);
    }
    console.log(`📝 Checking ${filesToCheck.length} staged file(s)...\n`);
} catch (e) {
    console.log('⚠️  Not in git repository or no staged files. Skipping validation.');
    process.exit(0);
}

let hasViolations = false;

filesToCheck.forEach(file => {
    if (!fs.existsSync(file)) return;

    const content = fs.readFileSync(file, 'utf8');
    const lines = content.split('\n');
    const isComponent = file.includes('.component.ts');
    const isService = file.includes('.service.ts');

    // Rule 1: No .subscribe() in components (except ngOnDestroy)
    if (isComponent) {
        lines.forEach((line, index) => {
            if (line.includes('.subscribe(') && !content.includes('ngOnDestroy')) {
                console.error(`❌ ${file}:${index + 1}`);
                console.error(`   VIOLATION: .subscribe() found in component`);
                console.error(`   GUIDELINE: Use signals and callback-based service methods`);
                console.error(`   Line: ${line.trim()}\n`);
                hasViolations = true;
            }
        });
    }

    // Rule 2: Services should expose readonly signals, not observables
    if (isService) {
        const hasObservableReturn = content.match(/:\s*Observable</g);
        const hasSignals = content.includes('signal(');

        if (hasObservableReturn && !content.includes('HttpClient')) {
            console.error(`⚠️  ${file}`);
            console.error(`   WARNING: Service returns Observable<>`);
            console.error(`   GUIDELINE: Use signals with callback-based methods instead\n`);
            // Warning only, not blocking
        }

        if (hasSignals && !content.includes('.asReadonly()')) {
            console.error(`❌ ${file}`);
            console.error(`   VIOLATION: Service has signals but doesn't expose readonly versions`);
            console.error(`   GUIDELINE: Expose signals as readonly: readonly data = this._data.asReadonly();\n`);
            hasViolations = true;
        }
    }

    // Rule 3: No window.confirm() or window.alert()
    lines.forEach((line, index) => {
        if (line.match(/window\.(confirm|alert)\(/)) {
            console.error(`❌ ${file}:${index + 1}`);
            console.error(`   VIOLATION: window.confirm/alert() found`);
            console.error(`   GUIDELINE: Use Bootstrap modals for confirmations`);
            console.error(`   Line: ${line.trim()}\n`);
            hasViolations = true;
        }
    });

    // Rule 4: New components should use new control flow syntax
    if (isComponent && content.includes('@Component')) {
        const hasOldSyntax = content.match(/\*ngIf|\*ngFor|\*ngSwitch/);

        if (hasOldSyntax) {
            try {
                // Check if this is a new file (no git history)
                execSync(`git log --oneline "${file}"`, { stdio: 'ignore' });
            } catch (e) {
                // No git history = new file
                console.error(`❌ ${file}`);
                console.error(`   VIOLATION: New component uses old control flow syntax`);
                console.error(`   GUIDELINE: Use @if, @for, @switch instead of *ngIf, *ngFor, *ngSwitch\n`);
                hasViolations = true;
            }
        }
    }

    // Rule 5: No Subject/BehaviorSubject in new code (prefer signals)
    if (content.includes('new Subject<') || content.includes('new BehaviorSubject<')) {
        console.error(`⚠️  ${file}`);
        console.error(`   WARNING: Using Subject/BehaviorSubject`);
        console.error(`   GUIDELINE: Prefer signals for reactive state management\n`);
        // Warning only for now
    }
});

if (hasViolations) {
    console.error('\n❌ CODING STANDARDS VIOLATIONS DETECTED!\n');
    console.error('Please fix the issues above before committing.');
    console.error('Review guidelines: docs/ANGULAR-CODING-STANDARDS.md');
    console.error('Enforcement details: docs/CODING-STANDARDS-ENFORCEMENT.md\n');
    process.exit(1);
}

console.log('✅ All coding standards checks passed!\n');
process.exit(0);
