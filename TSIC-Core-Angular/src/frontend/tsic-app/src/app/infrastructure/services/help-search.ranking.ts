import type { HelpSearchDoc, HelpSearchHit } from './help.types';

/**
 * Pure ranking logic for help search — deliberately free of Angular imports so it can be exercised
 * directly against the generated public/help/search-index.json (node --experimental-strip-types)
 * without booting the framework. HelpSearchService is the thin Angular wrapper that fetches the
 * index and calls rank().
 */

/** A doc with its searchable fields pre-lowercased once, so a keystroke never re-lowercases 288 KB. */
export interface PreparedDoc {
  readonly doc: HelpSearchDoc;
  readonly titleLc: string;
  readonly headingsLc: readonly string[];
  readonly textLc: string;
}

const MAX_TERMS = 8;
const SNIPPET_RADIUS = 110;

/**
 * Function words carry no topic signal but match everywhere, and because the two-tier rule requires
 * EVERY term to hit, leaving them in also demotes the pages that actually answer the question.
 * "stop people from signing up" ranked a spreadsheet-upload page first until these came out.
 */
const STOPWORDS = new Set([
  'a', 'an', 'and', 'any', 'are', 'as', 'at', 'be', 'been', 'but', 'by', 'can', 'did', 'do',
  'does', 'for', 'from', 'get', 'had', 'has', 'have', 'how', 'if', 'in', 'into', 'is', 'it',
  'its', 'me', 'my', 'no', 'not', 'of', 'on', 'or', 'our', 'out', 'so', 'that', 'the', 'their',
  'them', 'then', 'there', 'these', 'they', 'this', 'to', 'up', 'was', 'we', 'were', 'what',
  'when', 'where', 'which', 'who', 'why', 'will', 'with', 'you', 'your',
]);

/**
 * Sentinels for match marking. Private Use Area code points: they cannot occur in authored help
 * text, and escapeHtml() passes them through untouched (unlike & < > " ').
 */
const MARK_OPEN = '\uE000';
const MARK_CLOSE = '\uE001';

export function prepare(docs: readonly HelpSearchDoc[]): readonly PreparedDoc[] {
  return docs.map((doc) => ({
    doc,
    titleLc: doc.title.toLowerCase(),
    headingsLc: doc.headings.map((h) => h.toLowerCase()),
    textLc: doc.text.toLowerCase(),
  }));
}

/**
 * Rank the corpus against a query.
 *
 * Two tiers, deliberately: pages matching EVERY term rank above pages matching only some. A user
 * typing three words has told you three things, and a page satisfying all three is categorically
 * better than one that happens to repeat a single common word — which raw term-frequency scoring
 * gets backwards. Within a tier, weight is title > heading > body, because a page named for what
 * you asked is the answer, while a page merely mentioning it is a lead.
 */
export function rank(
  docs: readonly PreparedDoc[],
  query: string,
  limit = 20
): HelpSearchHit[] {
  const q = query.trim().toLowerCase();
  if (q.length < 2) return [];

  const words = [...new Set(q.split(/[^a-z0-9]+/i).filter((t) => t.length >= 2))];
  // Drop stopwords — unless that would leave nothing, in which case the user meant them literally.
  const meaningful = words.filter((t) => !STOPWORDS.has(t));
  const terms = (meaningful.length > 0 ? meaningful : words).slice(0, MAX_TERMS);
  if (terms.length === 0) return [];

  const scored: { hit: HelpSearchHit; tier: number }[] = [];

  for (const p of docs) {
    let score = 0;
    let matched = 0;

    for (const term of terms) {
      let hit = false;

      // Word-START matching throughout: "code" must still find "codes", but "up" must NOT find
      // "Update" or "DayGroup", and "not" must not find "Notification". Mid-word substring hits
      // are almost always noise, and they were the other half of what wrecked loose queries.
      if (startsWord(p.titleLc, term)) {
        score += 20;
        hit = true;
      }
      if (p.headingsLc.some((h) => startsWord(h, term))) {
        score += 8;
        hit = true;
      }
      const occurrences = countWordStartOccurrences(p.textLc, term);
      if (occurrences > 0) {
        score += Math.min(occurrences, 6) * 1.5;
        hit = true;
      }

      if (hit) matched++;
    }

    if (matched === 0) continue;

    // Whole-phrase bonus: "discount code" landing intact beats the two words landing apart.
    if (terms.length > 1) {
      if (p.titleLc.includes(q)) score += 40;
      else if (p.headingsLc.some((h) => h.includes(q))) score += 25;
      else if (p.textLc.includes(q)) score += 12;
    }

    scored.push({
      tier: matched === terms.length ? 0 : 1,
      hit: {
        key: p.doc.key,
        component: p.doc.component,
        topic: p.doc.topic,
        title: p.doc.title,
        heading: pickHeading(p, terms),
        snippet: buildSnippet(p.doc.text, p.textLc, terms),
        score,
      },
    });
  }

  scored.sort(
    (a, b) => a.tier - b.tier || b.hit.score - a.hit.score || a.hit.title.localeCompare(b.hit.title)
  );
  return scored.slice(0, limit).map((s) => s.hit);
}

/** True when the term begins a word — "team" in "team fee" scores above "team" in "downstream". */
function startsWord(haystack: string, term: string): boolean {
  let from = 0;
  for (;;) {
    const i = haystack.indexOf(term, from);
    if (i < 0) return false;
    if (i === 0 || !/[a-z0-9]/.test(haystack[i - 1])) return true;
    from = i + 1;
  }
}

/** Occurrences of the term at a word start only — the body-text counterpart of startsWord(). */
function countWordStartOccurrences(haystack: string, term: string): number {
  let count = 0;
  let from = 0;
  for (;;) {
    const i = haystack.indexOf(term, from);
    if (i < 0) return count;
    if (i === 0 || !/[a-z0-9]/.test(haystack[i - 1])) count++;
    from = i + term.length;
  }
}

/** The first heading containing any term — usually the FAQ question the user was really asking. */
function pickHeading(p: PreparedDoc, terms: readonly string[]): string | null {
  for (let i = 0; i < p.headingsLc.length; i++) {
    if (terms.some((t) => startsWord(p.headingsLc[i], t))) {
      // headings[0] is the page title; surfacing it as the "section" is noise.
      return i === 0 ? null : p.doc.headings[i];
    }
  }
  return null;
}

/**
 * An excerpt centred on the first matched term, escaped, with every term wrapped in <mark>.
 *
 * Marking happens FIRST using sentinels, escaping SECOND, and only then do the sentinels become
 * tags. Marking after escaping would let a query like "amp" or "quot" match inside an escape
 * sequence and corrupt it. The only tags in the result are the ones added here, so the string is
 * safe for [innerHTML] without a sanitizer bypass.
 */
function buildSnippet(text: string, textLc: string, terms: readonly string[]): string {
  let anchor = -1;
  for (const term of [...terms].sort((a, b) => b.length - a.length)) {
    const i = textLc.indexOf(term);
    if (i >= 0) {
      anchor = i;
      break;
    }
  }
  if (anchor < 0) anchor = 0;

  let start = Math.max(0, anchor - SNIPPET_RADIUS);
  let end = Math.min(text.length, anchor + SNIPPET_RADIUS);
  // Don't cut mid-word at either edge.
  while (start > 0 && /\S/.test(text[start - 1])) start--;
  while (end < text.length && /\S/.test(text[end])) end++;

  const slice = text.slice(start, end).trim();
  const pattern = new RegExp(`(${terms.map(escapeRegExp).join('|')})`, 'gi');
  const marked = escapeHtml(slice.replace(pattern, `${MARK_OPEN}$1${MARK_CLOSE}`))
    .split(MARK_OPEN)
    .join('<mark>')
    .split(MARK_CLOSE)
    .join('</mark>');

  return `${start > 0 ? '…' : ''}${marked}${end < text.length ? '…' : ''}`;
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
