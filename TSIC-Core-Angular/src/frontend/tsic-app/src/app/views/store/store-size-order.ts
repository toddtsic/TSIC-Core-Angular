/**
 * Human ordering for store size names.
 *
 * <p>Legacy has none. `IStoreService.GetJobStoreItemSkus` orders by
 * `StoreSize.StoreSizeName` — alphabetically — which puts Adult Large before Adult Small and
 * every Adult size before every Youth one. That is harmless in a dropdown a shopper scans, and
 * wrong the moment anything states a RANGE: the storefront card read "Adult Large – Youth
 * Small" on every garment, a range running backwards.</p>
 *
 * <p>The full vocabulary is 13 names platform-wide (measured 2026-08-30), so this is a closed
 * set rather than a guess at free text:</p>
 *
 * <pre>
 *   Youth Small · Youth Medium · Youth Large
 *   XS · Small · Medium · Large · XL
 *   Adult Small · Adult Medium · Adult Large · Adult XL
 *   Standard
 * </pre>
 *
 * <p>Unprefixed names sit in the ADULT tier, because a store using bare "Small/Medium/Large"
 * is not selling youth kit under a shorter label — the two vocabularies belong to different
 * jobs and never appear on one item.</p>
 *
 * <p>Anything unrecognised ranks last and keeps its original position, since the alternative is
 * inventing a place for a name we have never seen. Callers must sort STABLY for that to hold.</p>
 */

/** Sizes that carry no information and should never appear in a summary. */
const PLACEHOLDER = new Set(['standard', 'n/a', 'none', 'one size']);

/** Magnitude within a tier. Longest keys first so "x-small" is not matched as "small". */
const MAGNITUDE: readonly (readonly [string, number])[] = [
    ['xxxl', 6], ['3xl', 6], ['xxx-large', 6],
    ['xxl', 5], ['2xl', 5], ['xx-large', 5],
    ['x-large', 4], ['extra large', 4], ['xl', 4],
    ['x-small', 0], ['extra small', 0], ['xs', 0],
    ['small', 1], ['medium', 2], ['large', 3],
];

const YOUTH_TIER = 0;
const ADULT_TIER = 10;

/**
 * A sortable rank for one size name. Lower comes first; `Number.POSITIVE_INFINITY` for a name
 * outside the known vocabulary.
 */
export function sizeRank(name: string): number {
    const text = name.trim().toLowerCase();
    if (!text || PLACEHOLDER.has(text)) return Number.POSITIVE_INFINITY;

    const isYouth = /\byouth\b|\bjuvenile\b|^y[sml]$/.test(text);
    const tier = isYouth ? YOUTH_TIER : ADULT_TIER;

    for (const [token, magnitude] of MAGNITUDE) {
        if (text.includes(token)) return tier + magnitude;
    }

    // Bare "YS" / "YM" / "YL" — a shorthand the ladder above cannot see through.
    const short = /^y([sml])$/.exec(text);
    if (short) return tier + { s: 1, m: 2, l: 3 }[short[1]]!;

    return Number.POSITIVE_INFINITY;
}

/**
 * True for a variant name that tells a shopper nothing. Applies to COLOURS as well as sizes:
 * item create seeds a single default SKU with "Standard" on both dimensions, which is how a
 * card came to read "Standard · Standard".
 */
export function isPlaceholderVariantName(name: string): boolean {
    return PLACEHOLDER.has(name.trim().toLowerCase());
}

/**
 * Comparator for size names, smallest first. Equal ranks compare equal rather than falling back
 * to anything else, so a stable sort leaves same-rank names in the order they arrived — that is
 * what keeps unrecognised names, which all rank Infinity, in their original sequence.
 */
export function compareSizeNames(a: string, b: string): number {
    const ra = sizeRank(a);
    const rb = sizeRank(b);
    return ra === rb ? 0 : ra - rb;
}

/**
 * Size names smallest-first, with unrecognised names held at the end in their original order.
 * `Array.prototype.sort` is stable in every engine this app targets, which is what preserves it.
 */
export function orderSizes(names: readonly string[]): string[] {
    return [...names].sort(compareSizeNames);
}
