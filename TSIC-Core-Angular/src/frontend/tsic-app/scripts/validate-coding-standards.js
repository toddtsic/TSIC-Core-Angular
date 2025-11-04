const fs = require('node:fs');
const path = require('node:path');
const { execSync } = require('node:child_process');

console.log('🔍 Validating Coding Standards...\n');

// Get staged files (if running in pre-commit) or all TypeScript files
function getStagedFiles() {
    try {
        const staged = execSync('git diff --cached --name-only --diff-filter=ACM', { encoding: 'utf8' });
        return staged.trim().split('\n').filter(f => f && f.includes('/src/'));
    } catch (err) { // NOSONAR: handled by returning empty list in non-git environments
        // Not a git repo or no staged files; treat as no-op
        return [];
    }
}

let filesToCheck = getStagedFiles().filter(f => f.endsWith('.ts') || f.endsWith('.html'));
if (filesToCheck.length === 0) {
    console.log('ℹ️  No staged app files (.ts/.html) to check.');
    process.exit(0);
}
console.log(`📝 Checking ${filesToCheck.length} staged file(s)...\n`);

let hasViolations = false;

for (const file of filesToCheck) {
    if (!fs.existsSync(file)) continue;

    const content = fs.readFileSync(file, 'utf8');
    const lines = content.split('\n');
    const isComponent = file.endsWith('.component.ts');
    const isService = file.endsWith('.service.ts');
    const isTemplate = file.endsWith('.html');
    const isAppCode = file.includes('/src/app/');

    // Rule 1: No .subscribe() in components (except ngOnDestroy)
    if (isComponent) {
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            if (line.includes('.subscribe(') && !content.includes('ngOnDestroy')) {
                console.error(`❌ ${file}:${i + 1}`);
                console.error(`   VIOLATION: .subscribe() found in component`);
                console.error(`   GUIDELINE: Use signals and callback-based service methods`);
                console.error(`   Line: ${line.trim()}\n`);
                hasViolations = true;
            }
        }
    }

    // Rule 2: Services should expose readonly signals, not observables
    if (isService) {
        const hasObservableReturn = content.match(/:\s*Observable</g);
        const hasSignals = content.includes('signal(');

        if (hasObservableReturn && !content.includes('HttpClient')) {
            console.error(`⚠️  ${file}`);
            console.error(`   WARNING: Service returns Observable<>`);
            console.error(`   GUIDELINE: Use signals with callback-based methods instead\n`);
        }

        if (hasSignals && !content.includes('.asReadonly()')) {
            console.error(`❌ ${file}`);
            console.error(`   VIOLATION: Service has signals but doesn't expose readonly versions`);
            console.error(`   GUIDELINE: Expose signals as readonly: readonly data = this._data.asReadonly();\n`);
            hasViolations = true;
        }
    }

    // Rule 3: No window.confirm() or window.alert()
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        if (line.match(/window\.(confirm|alert)\(/)) {
            console.error(`❌ ${file}:${i + 1}`);
            console.error(`   VIOLATION: window.confirm/alert() found`);
            console.error(`   GUIDELINE: Use tsic-dialog for confirmations`);
            console.error(`   Line: ${line.trim()}\n`);
            hasViolations = true;
        }
    }

    // Rule 4: No raw <dialog> in templates (use <tsic-dialog>)
    if (isTemplate && isAppCode) {
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            if (line.includes('<dialog')) {
                console.error(`❌ ${file}:${i + 1}`);
                console.error(`   VIOLATION: Raw <dialog> tag found in template`);
                console.error(`   GUIDELINE: Use <tsic-dialog> component with built-in focus trap`);
                console.error(`   Line: ${line.trim()}\n`);
                hasViolations = true;
            }
        }
    }

    // Rule 5: New components should use new control flow syntax
    if (isComponent && content.includes('@Component')) {
        const hasOldSyntax = content.match(/\*ngIf|\*ngFor|\*ngSwitch/);

        if (hasOldSyntax) {
            try {
                // Check if this is a new file (no git history)
                execSync(`git log --oneline "${file}"`, { stdio: 'ignore' });
            } catch (e) { // NOSONAR: absence of git history is expected for new files
                // No git history = new file
                console.error(`❌ ${file}`);
                console.error(`   VIOLATION: New component uses old control flow syntax`);
                console.error(`   GUIDELINE: Use @if, @for, @switch instead of *ngIf, *ngFor, *ngSwitch\n`);
                hasViolations = true;
            }
        }
    }

    // Rule 6: No Subject/BehaviorSubject in new code (prefer signals)
    if (content.includes('new Subject<') || content.includes('new BehaviorSubject<')) {
        console.error(`⚠️  ${file}`);
        console.error(`   WARNING: Using Subject/BehaviorSubject`);
        console.error(`   GUIDELINE: Prefer signals for reactive state management\n`);
    }

    // Rule 7: Avoid console.* in app code (allow in main.ts and dev-guarded)
    if (isAppCode && file.endsWith('.ts')) {
        const isMain = file.endsWith('/main.ts');
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            if (line.match(/console\.(log|debug|warn|error)\(/)) {
                const windowLines = lines.slice(Math.max(0, i - 4), i + 5).join('\n');
                const devGuarded = windowLines.includes('environment.production') || windowLines.includes('!environment.production');
                // Allow if main.ts, or dev-guarded; otherwise flag
                if (!isMain && !devGuarded) {
                    console.error(`❌ ${file}:${i + 1}`);
                    console.error(`   VIOLATION: console.* found in app code without dev guard`);
                    console.error(`   GUIDELINE: Use ToastService or guard with environment.production === false`);
                    console.error(`   Line: ${line.trim()}\n`);
                    hasViolations = true;
                }
            }
        }
    }
}

if (hasViolations) {
    console.error('\n❌ CODING STANDARDS VIOLATIONS DETECTED!\n');
    console.error('Please fix the issues above before committing.');
    console.error('Review guidelines: docs/ANGULAR-CODING-STANDARDS.md');
    console.error('Enforcement details: docs/CODING-STANDARDS-ENFORCEMENT.md\n');
    process.exit(1);
}

console.log('✅ All coding standards checks passed!\n');
process.exit(0);
