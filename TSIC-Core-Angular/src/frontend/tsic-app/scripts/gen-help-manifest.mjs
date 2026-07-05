// Generates public/help/manifest.json — the list of {component}/{topic} keys that actually have
// content, so the "?" launcher can hide itself where there's nothing to show. Runs on prestart /
// prebuild (see package.json) and the result is committed, so a bare `ng build` still ships a fresh
// manifest. Help content is a pure frontend static asset now — no backend involved.
import { readdir, writeFile } from 'node:fs/promises';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptsDir = dirname(fileURLToPath(import.meta.url));
const helpDir = join(scriptsDir, '..', 'public', 'help');

const keys = [];
const components = await readdir(helpDir, { withFileTypes: true });
for (const c of components) {
  if (!c.isDirectory()) continue;
  const files = await readdir(join(helpDir, c.name));
  for (const f of files) {
    if (f.endsWith('.html')) keys.push(`${c.name}/${f.slice(0, -'.html'.length)}`);
  }
}
keys.sort();

const out = join(helpDir, 'manifest.json');
await writeFile(out, JSON.stringify({ keys }, null, 2) + '\n');
console.log(`[help] manifest: ${keys.length} topics across ${components.filter((c) => c.isDirectory()).length} components`);
