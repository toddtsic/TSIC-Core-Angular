// Generates two static artifacts from public/help, in one walk of the tree:
//
//   manifest.json      — the list of {component}/{topic} keys that actually have content, so the "?"
//                        launcher can hide itself where there's nothing to show.
//   search-index.json  — the same pages reduced to plain text + headings, so the header's search box
//                        can find a topic from any page. Search is client-side by construction: the
//                        corpus is 120 authored files that only change at deploy time, so an index
//                        built here is always exactly as fresh as the content shipped beside it.
//
// Runs on prestart / prebuild (see package.json) and BOTH results are committed, so a bare `ng build`
// still ships a fresh pair. Help content is a pure frontend static asset — no backend involved.
import { readdir, writeFile, readFile } from 'node:fs/promises';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptsDir = dirname(fileURLToPath(import.meta.url));
const helpDir = join(scriptsDir, '..', 'public', 'help');

/** The handful of entities the authored content actually uses. Not a general HTML decoder. */
function decodeEntities(s) {
  return s
    .replace(/&nbsp;/g, ' ')
    .replace(/&mdash;/g, '—')
    .replace(/&ndash;/g, '–')
    .replace(/&hellip;/g, '…')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&amp;/g, '&');
}

/** Tags out, entities in, whitespace collapsed. Block tags become a space so words don't fuse. */
function toText(html) {
  return decodeEntities(html.replace(/<[^>]*>/g, ' ')).replace(/\s+/g, ' ').trim();
}

/**
 * The searchable "landmarks" of a page: its section headings, plus every FAQ question (each lives in
 * a <summary>). Questions are the highest-signal text in the corpus — they are literally phrased the
 * way a user would ask — so they are indexed as headings and weighted accordingly at query time.
 */
function extractHeadings(html) {
  const out = [];
  for (const m of html.matchAll(/<(h[1-6])\b[^>]*>([\s\S]*?)<\/\1>/gi)) {
    const t = toText(m[2]);
    if (t) out.push(t);
  }
  for (const m of html.matchAll(/<summary\b[^>]*>([\s\S]*?)<\/summary>/gi)) {
    const t = toText(m[1]);
    if (t) out.push(t);
  }
  return out;
}

/** Title Case from the folder name — the fallback when a page has no leading heading of its own. */
function labelFromComponent(component) {
  const spaced = component.replace(/-/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

const keys = [];
const docs = [];

const components = await readdir(helpDir, { withFileTypes: true });
for (const c of components) {
  if (!c.isDirectory()) continue;
  const files = await readdir(join(helpDir, c.name));
  for (const f of files) {
    if (!f.endsWith('.html')) continue;
    const topic = f.slice(0, -'.html'.length);
    const key = `${c.name}/${topic}`;
    keys.push(key);

    const html = await readFile(join(helpDir, c.name, f), 'utf8');
    const headings = extractHeadings(html);
    docs.push({
      key,
      component: c.name,
      topic,
      // Authored pages open with an <h3> naming the screen; fall back to the folder name.
      title: headings[0] ?? labelFromComponent(c.name),
      headings,
      text: toText(html),
    });
  }
}

keys.sort();
docs.sort((a, b) => a.key.localeCompare(b.key));

await writeFile(join(helpDir, 'manifest.json'), JSON.stringify({ keys }, null, 2) + '\n');
// Minified: this one is fetched by the browser, and it is ~40x the manifest's size.
const indexJson = JSON.stringify({ docs });
await writeFile(join(helpDir, 'search-index.json'), indexJson + '\n');

const dirCount = components.filter((c) => c.isDirectory()).length;
console.log(`[help] manifest: ${keys.length} topics across ${dirCount} components`);
console.log(`[help] search-index: ${docs.length} docs, ${(indexJson.length / 1024).toFixed(0)} KB`);
