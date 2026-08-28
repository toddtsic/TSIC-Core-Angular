/**
 * Local help types. Help content is a pure frontend concern now — static HTML fragments under
 * public/help, keyed by route, rendered with the app's own design system. These are the canonical
 * shapes (not backend DTOs), so defining them here is correct, not a duplication of a generated model.
 */
export interface HelpContent {
  readonly component: string;
  readonly topic: string;
  readonly html: string;
  readonly exists: boolean;
}

export interface HelpManifest {
  readonly keys: readonly string[];
}

/** One indexed help page, as emitted by scripts/gen-help-manifest.mjs into public/help/search-index.json. */
export interface HelpSearchDoc {
  readonly key: string;
  readonly component: string;
  readonly topic: string;
  readonly title: string;
  /** Section headings plus every FAQ question — the highest-signal text on the page. */
  readonly headings: readonly string[];
  readonly text: string;
}

export interface HelpSearchIndex {
  readonly docs: readonly HelpSearchDoc[];
}

/** A scored match, ready to render: the page, where it matched, and a marked-up excerpt. */
export interface HelpSearchHit {
  readonly key: string;
  readonly component: string;
  readonly topic: string;
  readonly title: string;
  /** The heading (often an FAQ question) that matched, when one did — else null. */
  readonly heading: string | null;
  /** Escaped excerpt with matched terms wrapped in <mark>. Safe for [innerHTML]. */
  readonly snippet: string;
  readonly score: number;
}
