// Generation of the two derived help artifacts, in ONE place so the generator and the freshness
// check can never disagree about what "current" means. gen-help-manifest.mjs writes what this
// returns; verify-help.mjs compares it to what's committed.
//
//   manifest.json      — the {component}/{topic} keys that have content, so the "?" launcher can
//                        hide itself where there's nothing to show.
//   search-index.json  — the same pages reduced to plain text + headings, so the drawer can search
//                        and browse the whole manual.
import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';

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

/**
 * Tags out, entities in, whitespace collapsed. Block tags become a space so words don't fuse.
 * Collapsing \s+ also makes the result line-ending agnostic, which is what lets the freshness
 * check compare a CRLF working copy against LF-generated output without crying wolf.
 */
function toText(html) {
  return decodeEntities(html.replace(/<[^>]*>/g, ' ')).replace(/\s+/g, ' ').trim();
}

/**
 * The searchable "landmarks" of a page: its section headings, plus every FAQ question (each lives
 * in a <summary>). Questions are the highest-signal text in the corpus — they are literally phrased
 * the way a user would ask — so they are indexed as headings and weighted accordingly at query time.
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

/**
 * Walk public/help once and return both artifacts as the exact file contents they should have.
 * Returns { manifestJson, indexJson, keyCount, docCount, componentCount }.
 */
export async function buildHelpArtifacts(helpDir) {
  const keys = [];
  const docs = [];

  const entries = await readdir(helpDir, { withFileTypes: true });
  for (const c of entries) {
    if (!c.isDirectory()) continue;
    for (const f of await readdir(join(helpDir, c.name))) {
      if (!f.endsWith('.html')) continue;
      const topic = f.slice(0, -'.html'.length);
      keys.push(`${c.name}/${topic}`);

      const html = await readFile(join(helpDir, c.name, f), 'utf8');
      const headings = extractHeadings(html);
      docs.push({
        key: `${c.name}/${topic}`,
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

  return {
    manifestJson: JSON.stringify({ keys }, null, 2) + '\n',
    // Minified: this one is fetched by the browser, and it is ~40x the manifest's size.
    indexJson: JSON.stringify({ docs }) + '\n',
    keyCount: keys.length,
    docCount: docs.length,
    componentCount: entries.filter((c) => c.isDirectory()).length,
  };
}

/** Compare ignoring line endings — the working copy is CRLF on Windows, generated output is LF. */
export function sameContent(a, b) {
  return a.replace(/\r\n/g, '\n') === b.replace(/\r\n/g, '\n');
}
