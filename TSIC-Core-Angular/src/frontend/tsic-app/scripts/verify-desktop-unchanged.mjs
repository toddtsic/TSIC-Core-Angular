// Proves a mobile pass changed nothing a DESKTOP user can see.
// Run via `npm run verify:desktop-unchanged` (see .claude/rules/mobile-readiness.md).
// Exits non-zero if any changed style source alters the desktop-applicable CSS.
//
// Why this exists: the mobile-readiness programme edits many components. The claim
// "I only added rules inside a mobile media query" is easy to believe and easy to get
// wrong — one rule pasted a brace too early lands outside the block and ships to every
// desktop user. Reviewing for that by eye works until the fifth component. This makes
// it mechanical.
//
// How it works: compile each changed style source, DELETE every @media block that only
// applies below 768px, normalise whitespace, hash the remainder. Compare the hash of the
// working tree against the same file at HEAD. Identical hash => the CSS a desktop browser
// would apply is byte-identical. Not "reviewed and looks fine" — identical.
//
// Covers .scss files, `styles: [...]` blocks inside component .ts, and the global bundle.
// A component pass that touches a global must re-fingerprint everything, because globals
// reach every view — so src/styles.scss is ALWAYS checked, changed or not.
//
// What it CANNOT prove: TypeScript behaviour and template semantics. That gap is why the
// risk ladder exists (Phase 1 = CSS only; Phase 2 = TS early-returned on desktop;
// Phase 3 = explicit go/no-go). Do not read a green run as "the change is safe" — read it
// as "the change did not alter desktop CSS".
//
// Baseline is read with `git show HEAD:./path`. NEVER `git stash` — this must be safe to
// run against a dirty tree with unrelated work in it.
import { execFileSync } from 'node:child_process';
import { readFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import * as sass from 'sass';

const scriptsDir = dirname(fileURLToPath(import.meta.url));
const appRoot = join(scriptsDir, '..');
const GLOBAL_ENTRY = 'src/styles.scss';
const BACKTICK = String.fromCharCode(96);

// Deprecations we already silence in the Angular build; they are noise, not signal.
// 'mixed-decls' is deliberately absent — it is obsolete in this sass version and listing
// it makes sass itself print a warning on every run.
const SASS_OPTS = {
  loadPaths: [join(appRoot, 'src'), join(appRoot, 'node_modules')],
  silenceDeprecations: ['import', 'global-builtin', 'color-functions', 'legacy-js-api'],
};

/**
 * git hands back LF; on Windows the working tree is CRLF (core.autocrlf). Without this,
 * every file reads as "changed" and the harness recompiles the whole app on every run —
 * correct, but slow enough that people stop running it.
 */
const normalizeEol = (s) => s.replace(/\r\n/g, '\n');

/**
 * Remove @media blocks that apply ONLY below 768px. Everything else — including
 * `min-width` blocks and non-width queries like prefers-reduced-motion — is desktop-
 * affecting and stays in the hash.
 *
 * Sass flattens nested queries into `(max-width: 767.98px) and (prefers-reduced-motion: reduce)`,
 * which still reads as mobile-only here. That is correct: it cannot match a desktop viewport.
 *
 * Brace-counting rather than regex: @media blocks nest, and a regex that stops at the first
 * `}` would truncate the block and silently corrupt the hash on BOTH sides — passing green
 * while comparing garbage.
 */
function stripMobileOnly(css) {
  const out = [];
  let i = 0;
  while (i < css.length) {
    const at = css.indexOf('@media', i);
    if (at < 0) { out.push(css.slice(i)); break; }

    const open = css.indexOf('{', at);
    if (open < 0) { out.push(css.slice(i)); break; }

    const condition = css.slice(at + '@media'.length, open);
    const maxWidth = condition.match(/max-width:\s*(\d+(?:\.\d+)?)px/);
    const mobileOnly = !!maxWidth && parseFloat(maxWidth[1]) < 768 && !/min-width/.test(condition);

    let depth = 0;
    let j = open;
    for (; j < css.length; j++) {
      if (css[j] === '{') depth++;
      else if (css[j] === '}') { depth--; if (depth === 0) break; }
    }

    out.push(css.slice(i, at));
    if (!mobileOnly) out.push(css.slice(at, j + 1));
    i = j + 1;
  }
  return out.join('').replace(/\s+/g, ' ').trim();
}

/** Pull every `styles: [`...`]` literal out of a component .ts. */
function extractStyleBlocks(source) {
  const blocks = [];
  const re = /styles:\s*\[/g;
  let m;
  while ((m = re.exec(source))) {
    let i = re.lastIndex;
    let depth = 1;
    while (i < source.length && depth > 0) {
      const c = source[i];
      if (c === BACKTICK) {
        const start = i + 1;
        i = source.indexOf(BACKTICK, start);
        if (i < 0) return blocks;
        blocks.push(source.slice(start, i));
      } else if (c === '[') depth++;
      else if (c === ']') depth--;
      i++;
    }
  }
  return blocks;
}

/**
 * Desktop fingerprint of one style source. `url` lets relative @use resolve as though the
 * string lived at its real path — needed for the global bundle, whose baseline text is
 * piped in from git rather than read from disk.
 */
function fingerprint(path, text) {
  const sources = path.endsWith('.ts') ? extractStyleBlocks(text) : [text];
  if (!sources.length) return null; // .ts with no styles block — nothing to compare

  const compiled = sources
    .map((src) => sass.compileString(src, { ...SASS_OPTS, url: pathToFileURL(join(appRoot, path)) }).css)
    .join('\n');

  return createHash('sha256').update(stripMobileOnly(compiled)).digest('hex').slice(0, 16);
}

function git(args) {
  return execFileSync('git', args, { cwd: appRoot, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

// ── Collect candidates: everything changed vs HEAD, plus the global entry always ──
// --relative makes git emit paths relative to cwd (the app root) instead of the repo root,
// which also drops anything outside the app — the backend, docs, sibling submodules.
const changed = git(['diff', '--name-only', '--relative', 'HEAD', '--', '*.scss', '*.ts'])
  .split('\n')
  .map((l) => l.trim().replace(/\\/g, '/'))
  .filter(Boolean);

const targets = [...new Set([...changed, GLOBAL_ENTRY])].sort();

const drift = [];
const skipped = [];
let compared = 0;

for (const path of targets) {
  const abs = join(appRoot, path);

  if (!existsSync(abs)) { skipped.push(`${path} — deleted in working tree`); continue; }

  let baselineText;
  try {
    baselineText = git(['show', `HEAD:./${path}`]);
  } catch {
    skipped.push(`${path} — new file, no HEAD baseline to compare`);
    continue;
  }

  const currentText = readFileSync(abs, 'utf8');
  if (normalizeEol(baselineText) === normalizeEol(currentText)) continue; // untouched; nothing to prove

  let before, after;
  try {
    before = fingerprint(path, baselineText);
    after = fingerprint(path, currentText);
  } catch (e) {
    drift.push(`${path} — could not compile: ${e.message.split('\n')[0]}`);
    continue;
  }

  if (before === null && after === null) continue; // .ts with no styles either side
  compared++;
  if (before !== after) {
    drift.push(`${path}\n      HEAD desktop CSS ${before}\n      yours            ${after}`);
  }
}

if (skipped.length) {
  console.warn('[desktop-unchanged] not compared:');
  for (const s of skipped) console.warn(`  - ${s}`);
}

if (drift.length) {
  console.error(`\n[desktop-unchanged] DESKTOP CSS CHANGED — ${drift.length} file(s):`);
  for (const d of drift) console.error(`  - ${d}`);
  console.error(`
  A mobile pass must not alter the CSS a desktop browser applies. Either the rule landed
  outside the @media (max-width: 767.98px) block, or it belongs to a different phase.

  If the change is intentional and desktop-affecting, it is NOT a Phase 1 change — see
  .claude/rules/mobile-readiness.md ("The risk ladder") and split it into its own commit.`);
  process.exit(1);
}

console.log(`[desktop-unchanged] OK: desktop CSS byte-identical across ${compared} changed style source(s)`);
