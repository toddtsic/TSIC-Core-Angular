// Writes the two derived help artifacts into public/help. Runs on prestart / prebuild (see
// package.json) and BOTH results are committed, so a bare `ng build` still ships a fresh pair.
// Help content is a pure frontend static asset — no backend involved.
//
// The generation itself lives in help-index.mjs, shared with verify-help.mjs so the freshness
// check can't drift from the generator it is checking.
import { writeFile } from 'node:fs/promises';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildHelpArtifacts } from './help-index.mjs';

const scriptsDir = dirname(fileURLToPath(import.meta.url));
const helpDir = join(scriptsDir, '..', 'public', 'help');

const built = await buildHelpArtifacts(helpDir);

await writeFile(join(helpDir, 'manifest.json'), built.manifestJson);
await writeFile(join(helpDir, 'search-index.json'), built.indexJson);

console.log(`[help] manifest: ${built.keyCount} topics across ${built.componentCount} components`);
console.log(
  `[help] search-index: ${built.docCount} docs, ${(built.indexJson.length / 1024).toFixed(0)} KB`
);
