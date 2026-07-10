// Enforces the effect() ban (.claude/rules/frontend-angular.md, docs/Frontend/angular-signal-patterns.md
// Pattern 6). Run via `npm run verify:no-effect` (CI / pre-commit). Exits non-zero on any violation.
//
// Why a script and not a lint rule: tsic-app has no ESLint. Why enforce at all: an effect() that writes a
// signal it transitively reads re-triggers itself and reverts the write a frame later, silently. A sweep on
// 2026-07-09 found four live bugs of that shape. The ban only holds if it is mechanical.
//
// Rules:
//   1. No `effect` imported from '@angular/core'. It cannot be called without being imported, so the import
//      is the chokepoint — and matching the import (not the call) means prose in comments never false-positives.
//   2. No local wrapper that re-exports or aliases effect() back into reach (the deleted `effectWith` helper).
//
// Deliberately NOT banned: afterNextRender / afterRenderEffect. Those run once against the rendered DOM and
// carry none of the re-entrant write-loop risk. Four legitimate afterNextRender sites exist.
import { readdir, readFile } from 'node:fs/promises';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptsDir = dirname(fileURLToPath(import.meta.url));
const appDir = join(scriptsDir, '..', 'src', 'app');

// `import { ... } from '@angular/core'` — possibly multi-line. Captures the specifier list.
const CORE_IMPORT = /import\s*(?:type\s*)?\{([^}]*)\}\s*from\s*['"]@angular\/core['"]/gs;
// A bare `effect` specifier: not afterRenderEffect, not `effect as x` aliasing away, not effectWith.
const BARE_EFFECT = /(^|,)\s*effect\s*(,|$)/;
// Anything that hands effect() back out under a new name.
const REEXPORT = /export\s*\{[^}]*\beffect\b[^}]*\}\s*from\s*['"]@angular\/core['"]/s;

const files = (await readdir(appDir, { recursive: true, withFileTypes: true }))
  .filter((e) => e.isFile() && e.name.endsWith('.ts') && !e.name.endsWith('.spec.ts'))
  .map((e) => join(e.parentPath ?? e.path, e.name));

const errors = [];
for (const file of files) {
  const text = await readFile(file, 'utf8');
  const where = relative(appDir, file).replace(/\\/g, '/');

  for (const m of text.matchAll(CORE_IMPORT)) {
    // Normalize the specifier list, dropping aliases' right-hand sides.
    const specifiers = m[1].split(',').map((s) => s.trim().split(/\s+as\s+/)[0].trim()).filter(Boolean);
    if (specifiers.includes('effect')) {
      const line = text.slice(0, m.index).split('\n').length;
      errors.push(`${where}:${line} imports effect from '@angular/core'`);
    }
    // Belt and braces: catch a specifier list the split above could mangle.
    if (BARE_EFFECT.test(m[1]) && !specifiers.includes('effect')) {
      const line = text.slice(0, m.index).split('\n').length;
      errors.push(`${where}:${line} imports effect from '@angular/core'`);
    }
  }

  if (REEXPORT.test(text)) {
    errors.push(`${where} re-exports effect from '@angular/core' — wrappers put it back in reach`);
  }
}

if (errors.length) {
  console.error(`[no-effect] effect() is BANNED — ${errors.length} violation(s):`);
  for (const e of errors) console.error(`  - ${e}`);
  console.error(`\n  Replacement per job: .claude/rules/frontend-angular.md ("BANNED: effect()")`);
  console.error(`  Rationale + reference implementations: docs/Frontend/angular-signal-patterns.md Pattern 6`);
  process.exit(1);
}
console.log(`[no-effect] OK: no effect() import across ${files.length} .ts files`);
