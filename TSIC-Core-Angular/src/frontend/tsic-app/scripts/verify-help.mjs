// Route <-> help integrity check. The frontend owns the route table, so it's the only place that can
// statically prove every keyed route has help and every help folder is reachable from a route. Run via
// `npm run verify:help` (CI / pre-commit). Exits non-zero on drift.
//
// Rules:
//   1. Every route `helpKey: '<component>'` must have public/help/<component>/overview.html.
//   2. Every public/help/<component> folder must be referenced by some route helpKey.
//   3. Every topic file must be named overview.html or faq.html (the two tabs the launcher renders).
import { readdir, readFile } from 'node:fs/promises';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptsDir = dirname(fileURLToPath(import.meta.url));
const appDir = join(scriptsDir, '..', 'src', 'app');
const helpDir = join(scriptsDir, '..', 'public', 'help');
const KNOWN_TOPICS = new Set(['overview', 'faq']);

// 1. Collect helpKeys declared in the route table(s).
const routeKeys = new Set();
const routeFiles = (await readdir(appDir, { recursive: true, withFileTypes: true }))
  .filter((e) => e.isFile() && e.name.endsWith('.routes.ts'))
  .map((e) => join(e.parentPath ?? e.path, e.name));
for (const file of routeFiles) {
  const text = await readFile(file, 'utf8');
  for (const m of text.matchAll(/helpKey:\s*['"]([^'"]+)['"]/g)) {
    routeKeys.add(m[1].split('/')[0]);
  }
}

// 2. Collect help component folders + validate topic file names.
const helpComponents = new Map(); // component -> Set(topics)
const errors = [];
for (const c of await readdir(helpDir, { withFileTypes: true })) {
  if (!c.isDirectory()) continue;
  const topics = new Set();
  for (const f of await readdir(join(helpDir, c.name))) {
    if (!f.endsWith('.html')) continue;
    const topic = f.slice(0, -'.html'.length);
    if (!KNOWN_TOPICS.has(topic)) {
      errors.push(`unexpected topic file public/help/${c.name}/${f} (only overview.html / faq.html render)`);
    }
    topics.add(topic);
  }
  helpComponents.set(c.name, topics);
}

// 3. Cross-check.
for (const key of routeKeys) {
  if (!helpComponents.has(key)) {
    errors.push(`route helpKey '${key}' has no public/help/${key}/ folder`);
  } else if (!helpComponents.get(key).has('overview')) {
    errors.push(`route helpKey '${key}' is missing public/help/${key}/overview.html`);
  }
}
for (const component of helpComponents.keys()) {
  if (!routeKeys.has(component)) {
    errors.push(`public/help/${component}/ is orphaned — no route declares helpKey '${component}'`);
  }
}

if (errors.length) {
  console.error(`[help] route<->help integrity FAILED (${errors.length}):`);
  for (const e of errors) console.error(`  - ${e}`);
  process.exit(1);
}
console.log(`[help] route<->help integrity OK: ${routeKeys.size} keyed routes, ${helpComponents.size} help components`);
