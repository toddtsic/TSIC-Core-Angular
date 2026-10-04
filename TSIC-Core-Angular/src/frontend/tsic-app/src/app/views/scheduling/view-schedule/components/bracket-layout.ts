/**
 * Pure bracket layout: games (a tree via gid/parentGid) in, absolute card positions + connector
 * paths out. No DOM, no Syncfusion. Ported from TSIC-Events (src/app/utils/bracket-layout.ts),
 * which replaced its Syncfusion diagram the same way; only the game shape is adapted.
 *
 * Geometry: columns are rounds (first round leftmost, final rightmost); a game with feeders sits
 * at the vertical midpoint of its feeder games, the classic bracket shape.
 */

/** The fields the layout reads. The view's own node type carries everything else. */
export interface BracketLayoutGame {
    gid: number;
    parentGid: number | null;
    /** round header text for the game's column ('' = none) */
    roundLabel: string;
}

export interface BracketCardPos<T extends BracketLayoutGame> {
    game: T;
    x: number;
    y: number;
    /** the height this card was positioned for (measured, or the fallback) */
    h: number;
}

/** One round's header, sitting in the band above its column of cards. */
export interface BracketColumn {
    x: number;
    label: string;
}

export interface BracketLayoutResult<T extends BracketLayoutGame> {
    cards: BracketCardPos<T>[];
    /** SVG path `d` strings for the feeder→game elbow connectors */
    connectors: string[];
    columns: BracketColumn[];
    width: number;
    height: number;
}

export const BRACKET_CARD_W = 260;
/**
 * Fallback height, used before the view has measured its cards. Real heights come from the DOM:
 * team names wrap, so a card is as tall as its content and no taller. Sizing every card for the
 * worst case would pad the majority for the sake of the few.
 */
export const BRACKET_CARD_H = 76;
export const BRACKET_COL_GAP = 40;
export const BRACKET_ROW_GAP = 16;
export const BRACKET_PADDING = 12;
/** Band carrying the round headers; cards start below it. */
export const BRACKET_HEADER_H = 36;

const TOP = BRACKET_PADDING + BRACKET_HEADER_H;

/**
 * `heightOf` supplies each card's measured height. Height is content-driven (CSS) and independent
 * of position, so the view can measure once and feed the results back here for a single
 * re-position — no iteration, no convergence risk.
 */
export function layoutBracket<T extends BracketLayoutGame>(
    games: T[],
    heightOf: (gid: number) => number = () => BRACKET_CARD_H
): BracketLayoutResult<T> {
    if (!games.length) {
        return { cards: [], connectors: [], columns: [], width: 0, height: 0 };
    }

    const byGid = new Map(games.map(g => [g.gid, g]));
    const hOf = (g: T) => heightOf(g.gid) || BRACKET_CARD_H;

    // children[parentGid] = feeder games, in input order (stable)
    const children = new Map<number, T[]>();
    const roots: T[] = [];
    for (const g of games) {
        if (g.parentGid != null && byGid.has(g.parentGid) && g.parentGid !== g.gid) {
            const list = children.get(g.parentGid) ?? [];
            list.push(g);
            children.set(g.parentGid, list);
        } else {
            roots.push(g);
        }
    }

    // Column = how far a game sits from the final, walking UP the parent chain (final 0, semis 1,
    // quarters 2, ...). Measured from the root, not from the deepest feeder below: a game whose
    // teams were BOTH seeded directly has no feeders, so by feeder depth a round-of-16 match would
    // measure 0 and land in the first round's column.
    const distMemo = new Map<number, number>();
    const distFromFinal = (g: T, seen: Set<number>): number => {
        const memo = distMemo.get(g.gid);
        if (memo !== undefined) return memo;
        // a malformed parentGid cycle would recurse forever — treat the revisit as a root
        if (seen.has(g.gid)) return 0;
        seen.add(g.gid);
        const parent = g.parentGid != null && g.parentGid !== g.gid ? byGid.get(g.parentGid) : undefined;
        const d = parent ? 1 + distFromFinal(parent, seen) : 0;
        distMemo.set(g.gid, d);
        return d;
    };

    // y: DFS from each root keeps feeder groups adjacent. Feederless games stack by cumulative
    // height (cards differ in height, so a fixed row pitch would overlap them); every other game
    // centers on its feeders' CENTERS. Games feeding the same parent share a SLOT as tall as the
    // tallest of them, and a shorter card centers inside its slot — otherwise a short bye against
    // a tall game reads as a card floating high with its slack dumped below it.
    const slotOf = (g: T): number => {
        const siblings = g.parentGid != null ? children.get(g.parentGid) : undefined;
        return siblings?.length ? Math.max(...siblings.map(hOf)) : hOf(g);
    };

    const ys = new Map<number, number>();
    let nextTop = TOP;
    const placeY = (g: T): number => {
        const feeders = children.get(g.gid) ?? [];
        let y: number;
        if (!feeders.length) {
            const slot = slotOf(g);
            y = nextTop + (slot - hOf(g)) / 2;
            nextTop += slot + BRACKET_ROW_GAP;
        } else {
            const centers = feeders.map(f => placeY(f) + hOf(f) / 2);
            y = (Math.min(...centers) + Math.max(...centers)) / 2 - hOf(g) / 2;
        }
        ys.set(g.gid, y);
        return y;
    };
    roots.forEach(placeY);

    // A game taller than the span of its feeders centers above the header band; shift the whole
    // ladder down rather than let it clip off the canvas.
    const minY = Math.min(...ys.values());
    if (minY < TOP) {
        const shift = TOP - minY;
        for (const [gid, y] of ys) ys.set(gid, y + shift);
    }

    // Flip the distance so the first round is column 0 and the final is last.
    const maxDist = Math.max(...games.map(g => distFromFinal(g, new Set<number>())));
    const columnOf = (g: T) => maxDist - distFromFinal(g, new Set<number>());
    const xOf = (g: T) => BRACKET_PADDING + columnOf(g) * (BRACKET_CARD_W + BRACKET_COL_GAP);

    const cards: BracketCardPos<T>[] = games.map(game => ({
        game,
        x: xOf(game),
        y: ys.get(game.gid) ?? TOP,
        h: hOf(game)
    }));

    // elbow: feeder's right edge → halfway across the gap → parent's left edge
    const connectors: string[] = [];
    for (const [parentGid, feeders] of children) {
        const parent = byGid.get(parentGid)!;
        const px = xOf(parent);
        const pcy = (ys.get(parentGid) ?? 0) + hOf(parent) / 2;
        for (const feeder of feeders) {
            const fx = xOf(feeder) + BRACKET_CARD_W;
            const fcy = (ys.get(feeder.gid) ?? 0) + hOf(feeder) / 2;
            const midX = px - BRACKET_COL_GAP / 2;
            connectors.push(`M ${fx} ${fcy} H ${midX} V ${pcy} H ${px}`);
        }
    }

    // One header per column, labelled by the round its games carry (synthesized byes copy their
    // sibling's round).
    const labelByX = new Map<number, string>();
    for (const { game, x } of cards) {
        if (game.roundLabel && !labelByX.has(x)) labelByX.set(x, game.roundLabel);
    }
    const columns: BracketColumn[] = [...labelByX]
        .map(([x, label]) => ({ x, label }))
        .sort((a, b) => a.x - b.x);

    const width = Math.max(...cards.map(c => c.x)) + BRACKET_CARD_W + BRACKET_PADDING;
    const height = Math.max(...cards.map(c => c.y + c.h)) + BRACKET_PADDING;

    return { cards, connectors, columns, width, height };
}
