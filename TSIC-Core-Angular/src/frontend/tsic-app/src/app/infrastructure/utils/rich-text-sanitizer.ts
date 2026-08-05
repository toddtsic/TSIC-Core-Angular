/**
 * Allowlist sanitizer for director-authored rich text.
 *
 * ## Why this exists
 *
 * Angular's built-in HTML sanitizer — the one that runs automatically on every
 * `[innerHTML]` binding — has no `style` in its attribute allowlist:
 *
 *     HTML_ATTRS = abbr,accesskey,align,alt,…,class,clear,color,cols,…,width
 *
 * It therefore deletes every inline `style` attribute. Syncfusion's RTE writes font
 * size, text colour and highlight colour *as inline styles*
 * (`<span style="font-size:20px">`), which means the formatting a director applies in
 * the editor is silently discarded the moment the content renders to a parent. It
 * looks right while authoring and wrong in production — the worst failure shape.
 *
 * `TranslateLegacyUrlsPipe` worked around this by rewriting a hand-listed set of
 * legacy values (`14px`/`18px`/`22px`, four hex colours) into CSS classes. That
 * covers the legacy corpus it was built for and nothing else: the curated size list
 * emits `12/16/20/24px` and the colour picker emits an unbounded palette, so none of
 * it survived. Enumerating values was never going to scale — the fix has to be at the
 * sanitizer, not at the value list.
 *
 * ## The trade this makes
 *
 * Output is handed to `bypassSecurityTrustHtml`, so **this function is the only thing
 * standing between authored content and the DOM**. Bulletins are written by directors
 * and read by the public, so the threat modelled here is a stored-XSS payload authored
 * by (or injected through) a director account. There is no server-side sanitization —
 * the API stores bulletin HTML verbatim — which makes this the sole gate.
 *
 * It is an allowlist in all four dimensions, because a denylist is a list of the
 * attacks someone already thought of:
 *
 *   - **elements**   — unknown tags are unwrapped (children kept), dangerous ones dropped
 *   - **attributes** — per-tag, plus a small global set; everything else removed
 *   - **URLs**       — scheme allowlist; `javascript:`/`vbscript:`/`blob:` never pass
 *   - **CSS**        — property allowlist, then a value guard for `url(`/`expression(`
 *
 * Parsing goes through `DOMParser`, which produces an **inert** document: scripts do
 * not execute and `src` attributes are not fetched while we walk the tree. Building
 * the tree with `innerHTML` on a live element would fetch and fire before we ever got
 * to inspect it.
 */

/**
 * Dropped with their entire subtree. Their *content* is the payload, so unwrapping
 * (which is what unknown elements get) would be the bug rather than the safe default:
 * unwrapping `<script>alert(1)</script>` leaves the text `alert(1)`, but unwrapping
 * `<style>` leaks CSS into the page, and unwrapping `<title>`/`<textarea>` changes how
 * the surrounding markup re-parses.
 */
const DROP_WITH_SUBTREE: ReadonlySet<string> = new Set([
  'script', 'style', 'iframe', 'object', 'embed', 'applet', 'frame', 'frameset',
  'form', 'input', 'button', 'select', 'option', 'textarea', 'label', 'fieldset',
  'link', 'meta', 'base', 'title', 'noscript', 'template', 'slot',
  'svg', 'math', 'canvas', 'audio', 'video', 'source', 'track', 'map', 'area',
  'portal', 'dialog',
]);

/**
 * Kept as-is. Everything an RTE can produce plus the legacy vocabulary already sitting
 * in the bulletin corpus (`<font>`, `<center>`, `<strike>` — deprecated in HTML, still
 * authored by the classic editor and still rendering fine).
 */
const ALLOWED_ELEMENTS: ReadonlySet<string> = new Set([
  'a', 'abbr', 'b', 'big', 'blockquote', 'br', 'caption', 'center', 'cite', 'code',
  'col', 'colgroup', 'dd', 'del', 'div', 'dl', 'dt', 'em', 'figcaption', 'figure',
  'font', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'hr', 'i', 'img', 'ins', 'li', 'mark',
  'ol', 'p', 'pre', 'q', 's', 'small', 'span', 'strike', 'strong', 'sub', 'sup',
  'table', 'tbody', 'td', 'tfoot', 'th', 'thead', 'tr', 'u', 'ul',
]);

/**
 * Allowed on any element. `id` is deliberately absent: an authored `id` can collide
 * with an app element's id and silently hijack `getElementById`, label targets and
 * in-page anchors. Nothing in the RTE needs one.
 */
const GLOBAL_ATTRS: ReadonlySet<string> = new Set([
  'class', 'style', 'title', 'dir', 'lang', 'align',
]);

/** Additional attributes, per element. Anything not listed here or above is removed. */
const TAG_ATTRS: Readonly<Record<string, readonly string[]>> = {
  a: ['href', 'target', 'rel', 'name'],
  img: ['src', 'alt', 'width', 'height', 'loading'],
  table: ['border', 'cellpadding', 'cellspacing', 'width', 'height', 'summary'],
  td: ['colspan', 'rowspan', 'valign', 'width', 'height', 'bgcolor', 'headers', 'scope'],
  th: ['colspan', 'rowspan', 'valign', 'width', 'height', 'bgcolor', 'headers', 'scope'],
  tr: ['valign', 'bgcolor'],
  col: ['span', 'width'],
  colgroup: ['span', 'width'],
  font: ['color', 'face', 'size'],
  ol: ['start', 'type'],
  ul: ['type'],
  blockquote: ['cite'],
};

/**
 * CSS properties that survive. Derived from Syncfusion's own paste allowlist
 * (`PasteCleanupSettings.allowedStyleProps`) so that what a director can *author* and
 * what actually *renders* are the same set — a narrower render list would reintroduce
 * the exact class of silent loss this file exists to fix.
 *
 * Removed from Syncfusion's list on purpose: `position`, `display`, `visibility`,
 * `overflow*`, `top`/`left`/`right`, `cursor` and `flex-direction`. Those are
 * layout-escape tools — they let authored content cover or hide chrome outside its own
 * box — and none of them are reachable from our toolbar, so dropping them costs no
 * authoring capability.
 *
 * The `background` shorthand IS allowed. It can carry `url(...)`, but so can several
 * others here, and the value guard below rejects that regardless of property — putting
 * the check on the value rather than duplicating it as a property exclusion. Email
 * confirmation bodies use the shorthand routinely.
 */
const ALLOWED_CSS_PROPS: ReadonlySet<string> = new Set([
  'background', 'background-color', 'border', 'border-bottom', 'border-collapse', 'border-color',
  'border-left', 'border-radius', 'border-right', 'border-spacing', 'border-style',
  'border-top', 'border-width', 'clear', 'color', 'direction', 'float', 'font-family',
  'font-size', 'font-style', 'font-variant', 'font-weight', 'height', 'letter-spacing',
  'line-height', 'list-style-type', 'margin', 'margin-bottom', 'margin-left',
  'margin-right', 'margin-top', 'max-height', 'max-width', 'min-height', 'min-width',
  'padding', 'padding-bottom', 'padding-left', 'padding-right', 'padding-top',
  'table-layout', 'text-align', 'text-decoration', 'text-indent', 'text-transform',
  'vertical-align', 'white-space', 'width', 'word-break', 'overflow-wrap',
]);

/**
 * Rejects a whole declaration. `url(` covers CSS-based exfiltration (a background
 * image whose URL encodes page content) and, historically, `javascript:` execution;
 * `expression(` is legacy IE script-in-CSS; `@import` pulls in an entire remote
 * stylesheet; `\` is the CSS escape character, which is how `\6a avascript:` gets past
 * a naive substring check.
 */
const UNSAFE_CSS_VALUE = /url\s*\(|expression\s*\(|javascript\s*:|vbscript\s*:|@import|[<>\\]/i;

/** Schemes a link may use. Everything else — `javascript:`, `blob:`, `file:` — is dropped. */
const SAFE_SCHEMES: ReadonlySet<string> = new Set(['http:', 'https:', 'mailto:', 'tel:']);

/**
 * Inert raster data URIs, permitted on `<img>` only.
 *
 * SVG is excluded even though it is an image format: an SVG document can carry
 * `<script>` and event handlers, and as a `data:` URI it is same-origin with us.
 *
 * This is an allowance for content that already exists, not an invitation — the RTE is
 * configured to refuse file ingestion precisely so no new base64 lands in the database
 * (see `rte-config.ts`). Stripping these on render would blank images a director had
 * legitimately embedded before that guard was in place.
 */
const SAFE_DATA_IMAGE = /^data:image\/(?:png|jpe?g|gif|webp|bmp);base64,/i;

/**
 * Returns HTML with everything outside the allowlists removed.
 *
 * Prefer the `richText` pipe in templates — it pairs this with `bypassSecurityTrustHtml`,
 * which is the half that must never be applied to unsanitized input.
 */
export function sanitizeRichText(html: string | null | undefined): string {
  if (!html) return '';
  const doc = new DOMParser().parseFromString(html, 'text/html');
  cleanChildren(doc.body);
  return doc.body.innerHTML;
}

function cleanChildren(parent: Element): void {
  // Snapshot first: unwrapping splices new siblings into the live list mid-walk. Those
  // spliced-in nodes were already cleaned before their parent was unwrapped, so they
  // must not be revisited — iterating the live NodeList would do exactly that.
  for (const node of Array.from(parent.childNodes)) {
    if (node.nodeType === Node.TEXT_NODE) continue;

    if (node.nodeType !== Node.ELEMENT_NODE) {
      // Comments and processing instructions. Comments are dropped rather than kept
      // because a malformed one (`<!--> …`) can terminate early in some parsers and
      // expose its contents as markup.
      node.parentNode?.removeChild(node);
      continue;
    }

    const el = node as Element;
    const tag = el.tagName.toLowerCase();

    if (DROP_WITH_SUBTREE.has(tag)) {
      el.remove();
      continue;
    }

    if (!ALLOWED_ELEMENTS.has(tag)) {
      // Unknown tag: keep the words, drop the wrapper. Clean the subtree *before*
      // unwrapping so the promoted children are already safe once they move up.
      cleanChildren(el);
      unwrap(el);
      continue;
    }

    cleanAttributes(el, tag);

    // An <img> whose src did not survive the URL check is a broken-image icon, which
    // is worse than nothing — remove the element rather than leave the placeholder.
    if (tag === 'img' && !el.hasAttribute('src')) {
      el.remove();
      continue;
    }

    cleanChildren(el);
  }
}

function unwrap(el: Element): void {
  const parent = el.parentNode;
  if (!parent) return;
  while (el.firstChild) parent.insertBefore(el.firstChild, el);
  parent.removeChild(el);
}

function cleanAttributes(el: Element, tag: string): void {
  const tagAllowed = TAG_ATTRS[tag];

  for (const attr of Array.from(el.attributes)) {
    const name = attr.name.toLowerCase();

    // Event handlers first and unconditionally — `onerror` on an <img> is the single
    // most common stored-XSS vector, and it would otherwise be judged by the same
    // allowlist as everything else rather than on its own terms.
    if (name.startsWith('on') || name.startsWith('xlink:') || name.startsWith('xmlns')) {
      el.removeAttribute(attr.name);
      continue;
    }

    if (!GLOBAL_ATTRS.has(name) && !tagAllowed?.includes(name)) {
      el.removeAttribute(attr.name);
      continue;
    }

    if (name === 'style') {
      const safe = filterStyle(attr.value);
      if (safe) el.setAttribute('style', safe);
      else el.removeAttribute('style');
      continue;
    }

    if ((name === 'href' || name === 'src') && !isSafeUrl(attr.value, tag === 'img')) {
      el.removeAttribute(attr.name);
    }
  }

  // Reverse tabnabbing: a `_blank` target hands the opened page a `window.opener`
  // handle it can navigate. Set rather than merged — nothing authored needs a
  // different rel, and merging would let an author reinstate the hole.
  if (tag === 'a' && el.getAttribute('target') === '_blank') {
    el.setAttribute('rel', 'noopener noreferrer');
  }
}

function filterStyle(value: string): string {
  const kept: string[] = [];

  for (const declaration of value.split(';')) {
    const separator = declaration.indexOf(':');
    if (separator < 0) continue;

    const property = declaration.slice(0, separator).trim().toLowerCase();
    const propertyValue = declaration.slice(separator + 1).trim();

    if (!property || !propertyValue) continue;
    if (!ALLOWED_CSS_PROPS.has(property)) continue;
    if (UNSAFE_CSS_VALUE.test(propertyValue)) continue;

    kept.push(`${property}: ${propertyValue}`);
  }

  return kept.join('; ');
}

function isSafeUrl(raw: string, isImage: boolean): boolean {
  // Strip control characters and whitespace before reading the scheme. Browsers ignore
  // them when resolving a URL, so `java\tscript:alert(1)` navigates as `javascript:` —
  // testing the raw string would see an unrecognised scheme and wave it through.
  const url = raw.replace(/[\u0000-\u0020\u00a0\u2028\u2029]/g, '').toLowerCase();
  if (!url) return false;

  if (isImage && SAFE_DATA_IMAGE.test(url)) return true;

  const scheme = /^([a-z][a-z0-9+.-]*):/.exec(url);
  if (!scheme) return true; // relative, protocol-relative, fragment or query — same origin

  return SAFE_SCHEMES.has(`${scheme[1]}:`);
}
