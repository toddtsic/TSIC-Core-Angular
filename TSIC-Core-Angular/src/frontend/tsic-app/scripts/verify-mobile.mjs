// Enforces the mechanically-decidable parts of .claude/rules/mobile-readiness.md.
// Run via `npm run verify:mobile`. Exits non-zero on any violation.
//
// Why a script: tsic-app has no ESLint, and these three defects are invisible in review —
// each shipped to production here at least once. Why only three: a guard that cries wolf
// gets ignored, so only checks that can be decided WITHOUT judgement are automated. Touch
// target sizes (57 files, most non-interactive), the 243 form-control-sm sites, and grid
// frozen-column sums all need a human looking at the actual surface. They live in the
// runbook's manual pass, not here.
//
// Checks run against COMPILED CSS, not raw SCSS. That matters: nesting means the same
// defect can be written `&:hover { display: flex }` or `.parent:hover & { display: flex }`,
// and a text-level matcher that handles one ordering silently misses the other. A first-pass
// text detector missed LADT's own violation for exactly that reason. Compiled CSS flattens
// both into the same shape.
//
//   1. HOVER-ONLY REVEAL — an element hidden by default and revealed only on :hover does not
//      exist on a touch device. LADT's tree add/delete buttons were unreachable on a phone
//      this way. NOT flagged: a visible element that merely brightens on hover (opacity
//      0.5 -> 1) — that is styling, not an affordance gate.
//
//   2. OFF-CONTRACT SLIDE PANEL — a fixed, full-height, fixed-width panel that neither uses
//      the shared `.detail-panel` contract nor anchors to --app-header-height-mobile will
//      cover the app header on mobile. This is the defect fixed in ac0b44c2.
//
//   3. 100vh WITHOUT dvh ON A FIXED OVERLAY — iOS Safari's collapsing URL bar makes 100vh
//      taller than the visible viewport, hiding the bottom of the overlay (where sticky
//      footers and Save buttons live). Scoped to position:fixed deliberately: for an
//      in-flow element, vh is a sizing choice, not a broken overlay contract.
//
// Opt-out: a file may declare `// mobile-readiness: desktop-only` to skip checks 2 and 3.
// Use it only where the view genuinely hides itself below 768px (search/teams does). It is
// a `//` comment on purpose — sass strips those, so declaring intent cannot alter output.
//
// BASELINE: mobile-readiness-baseline.json records violations that already existed when this
// guard was introduced, so the guard fails only on NEW ones. That is what makes it a ratchet
// rather than a wall — nobody can adopt a guard that fails on day one, and a guard that is
// routinely bypassed protects nothing.
//
// The baseline is deliberately self-tightening: a stale entry (a violation that has since been
// fixed) is ALSO a failure, with a one-line "delete this" fix. Without that, the baseline only
// ever grows and the debt is never paid down. Opt-out and baseline mean different things and
// should not be confused — opt-out is "this check does not apply here", baseline is "this is
// known debt we have not paid yet".
import { readdir, readFile } from 'node:fs/promises';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import * as sass from 'sass';

const scriptsDir = dirname(fileURLToPath(import.meta.url));
const appRoot = join(scriptsDir, '..');
const srcDir = join(appRoot, 'src');
const BACKTICK = String.fromCharCode(96);
const OPT_OUT = /\/\/\s*mobile-readiness:\s*desktop-only/;

const SASS_OPTS = {
  loadPaths: [join(appRoot, 'src'), join(appRoot, 'node_modules')],
  silenceDeprecations: ['import', 'global-builtin', 'color-functions', 'legacy-js-api'],
};

const VISIBLE_DISPLAY = /display:\s*(flex|block|inline-flex|inline-block|grid|inline)\b/;
const HIDDEN_DISPLAY = /display:\s*none\b/;
const OPACITY_ON = /opacity:\s*(1|100%)\s*(;|$)/;
const OPACITY_OFF = /opacity:\s*0\s*(;|$)/;

/** Walk compiled CSS, invoking cb(selector, declarations, mediaStack) for each style rule. */
function forEachRule(css, cb) {
  let i = 0;
  const media = [];

  function block(end) {
    while (i < end) {
      const open = css.indexOf('{', i);
      if (open < 0 || open >= end) return;

      const prelude = css.slice(i, open).trim();
      let depth = 0;
      let j = open;
      for (; j < css.length; j++) {
        if (css[j] === '{') depth++;
        else if (css[j] === '}') { depth--; if (depth === 0) break; }
      }

      if (prelude.startsWith('@')) {
        // Nested at-rule (@media / @supports); recurse into its body.
        if (/^@(media|supports|layer)/.test(prelude)) {
          media.push(prelude);
          const save = i;
          i = open + 1;
          block(j);
          i = save;
          media.pop();
        }
      } else if (prelude) {
        cb(prelude, css.slice(open + 1, j), [...media]);
      }
      i = j + 1;
    }
  }
  block(css.length);
}

const isMobileOnly = (conds) =>
  conds.some((c) => {
    const m = c.match(/max-width:\s*(\d+(?:\.\d+)?)px/);
    return m && parseFloat(m[1]) < 768 && !/min-width/.test(c);
  });

/**
 * Every element a rule styles: one entry per comma branch, reduced to that branch's last
 * compound selector.
 *
 * Per BRANCH, not just the first: `.btn-add-child, .btn-remove-node { display: none }` styles
 * two elements, and an earlier version of this only ever saw the first — so LADT's
 * .btn-remove-node was never tracked even while its twin was. A guard with a silent blind
 * spot is worse than no guard, because it is trusted.
 */
const targets = (sel) => [
  ...new Set(
    sel.split(',')
      .map((branch) => branch.trim().split(/\s+|>|\+|~/).filter(Boolean).pop()?.replace(/:hover\b/g, '') ?? '')
      .filter(Boolean)
  ),
];

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

// ── Collect every style source under src/ ─────────────────────────────────────
const entries = (await readdir(srcDir, { recursive: true, withFileTypes: true }))
  .filter((e) => e.isFile() && (e.name.endsWith('.scss') || e.name.endsWith('.ts')) && !e.name.endsWith('.spec.ts'))
  .map((e) => join(e.parentPath ?? e.path, e.name))
  .filter((p) => !p.includes(`${'node_modules'}`));

// key => human-readable message. The key is what the baseline stores, so it must stay stable
// across cosmetic edits: file + styled element + check id, never a line number.
const found = new Map();
const add = (where, t, check, message) => found.set(`${where}::${t}::${check}`, `${where}  ${t} — ${message}`);

for (const file of entries) {
  const text = await readFile(file, 'utf8');
  const where = relative(srcDir, file).replace(/\\/g, '/');
  const optedOut = OPT_OUT.test(text);

  const sources = file.endsWith('.ts') ? extractStyleBlocks(text) : [text];
  if (!sources.length) continue;
  // Partials are compiled through their entry point; compiling them alone can fail on
  // undefined upstream variables.
  if (/[/\\]_[^/\\]*\.scss$/.test(file)) continue;

  let css = '';
  for (const src of sources) {
    try {
      css += sass.compileString(src, { ...SASS_OPTS, url: pathToFileURL(file) }).css + '\n';
    } catch {
      // Not compilable in isolation (mixin fragment, template soup) — the build is the
      // authority on validity; this guard just skips what it cannot read.
    }
  }
  if (!css.trim()) continue;

  const hiddenByDefault = new Set();
  const revealedOnHover = new Map(); // target -> revealed under a mobile query?

  forEachRule(css, (selector, decls, conds) => {
    const els = targets(selector);
    if (!els.length) return;
    const hover = /:hover\b/.test(selector);

    for (const t of els) {
      if (!hover && (HIDDEN_DISPLAY.test(decls) || OPACITY_OFF.test(decls))) hiddenByDefault.add(t);
      if (hover && (VISIBLE_DISPLAY.test(decls) || OPACITY_ON.test(decls))) {
        revealedOnHover.set(t, (revealedOnHover.get(t) ?? false) || isMobileOnly(conds));
      }
      // A mobile-only rule that reveals the element unconditionally is the sanctioned fix.
      if (!hover && isMobileOnly(conds) && VISIBLE_DISPLAY.test(decls)) revealedOnHover.set(t, true);
    }

    if (optedOut) return;
    const t = els[0];

    const fixed = /position:\s*fixed\b/.test(decls);
    if (!fixed) return;

    const heights = [...decls.matchAll(/(?:^|;)\s*(?:min-|max-)?height:\s*([^;]+)/g)].map((m) => m[1]);
    if (heights.some((h) => h.includes('100vh')) && !heights.some((h) => h.includes('100dvh'))) {
      add(where, t, 'vh-no-dvh', 'position:fixed with 100vh and no 100dvh fallback (iOS URL bar hides the bottom)');
    }

    const fullHeight = heights.some((h) => /100[dsl]?vh/.test(h)) || (/top:\s*0/.test(decls) && /bottom:\s*0/.test(decls));
    const fixedWidth = /(?:^|;)\s*width:\s*\d+px/.test(decls);
    if (fullHeight && fixedWidth && !/\.detail-panel/.test(text) && !/--app-header-height-mobile/.test(text)) {
      add(where, t, 'off-contract-panel', 'fixed full-height panel off-contract (no .detail-panel, no --app-header-height-mobile)');
    }
  });

  for (const [t, hasMobileEscape] of revealedOnHover) {
    if (hiddenByDefault.has(t) && !hasMobileEscape) {
      add(where, t, 'hover-only', 'revealed only on :hover; unreachable on touch');
    }
  }
}

// ── Compare against the baseline ──────────────────────────────────────────────
const baselinePath = join(scriptsDir, 'mobile-readiness-baseline.json');

// --write-baseline is handled BEFORE reading the file, so the very first run can bootstrap
// one. (Checking for the file first makes the guard impossible to adopt — it fails asking
// for a baseline that only the failing branch could have written.)
if (process.argv.includes('--write-baseline')) {
  const { writeFile } = await import('node:fs/promises');
  const next = {
    note: 'Violations that predate the guard. The guard fails on anything NOT listed here, and ALSO on a stale entry (one that has since been fixed) so the list can only shrink. Do not add to this by hand — fix the defect instead.',
    generated: 'npm run verify:mobile -- --write-baseline',
    entries: [...found.keys()].sort(),
  };
  await writeFile(baselinePath, JSON.stringify(next, null, 2) + '\n', 'utf8');
  console.log(`[mobile] baseline written: ${next.entries.length} pre-existing violation(s)`);
  process.exit(0);
}

let baseline = { entries: [] };
try {
  baseline = JSON.parse(await readFile(baselinePath, 'utf8'));
} catch {
  console.error(`[mobile] missing or unreadable ${relative(appRoot, baselinePath)} — bootstrap it with:`);
  console.error(`  npm run verify:mobile -- --write-baseline`);
  process.exit(1);
}
const known = new Set(baseline.entries ?? []);

const fresh = [...found.keys()].filter((k) => !known.has(k)).sort();
const stale = [...known].filter((k) => !found.has(k)).sort();

if (fresh.length) {
  console.error(`[mobile] ${fresh.length} NEW violation(s) of .claude/rules/mobile-readiness.md:`);
  for (const k of fresh) console.error(`  - ${found.get(k)}`);
}
if (stale.length) {
  console.error(`\n[mobile] ${stale.length} baseline entr(ies) no longer violate — delete them from`);
  console.error(`  scripts/mobile-readiness-baseline.json so the ratchet tightens:`);
  for (const k of stale) console.error(`  - ${k}`);
}
if (fresh.length || stale.length) {
  console.error(`\n  Rules + fixes: .claude/rules/mobile-readiness.md`);
  console.error(`  Runbook:       docs/Frontend/mobile-readiness-status.md`);
  process.exit(1);
}

console.log(
  `[mobile] OK: no new violations across ${entries.length} style source(s)` +
  (known.size ? ` (${known.size} pre-existing, tracked in the baseline)` : '')
);
