/**
 * Shared conflict-detection pure functions for schedule grids.
 * Extracted from schedule-division and rescheduler (identical implementations).
 */

import type { ScheduleGridRow, ScheduleGameDto } from '@core/api';

/**
 * BREAKING: Same team in 2+ games at the exact same timeslot (same grid row, any division).
 * Returns Set of game IDs that are in a time clash.
 */
export function computeTimeClashGameIds(rows: ScheduleGridRow[]): Set<number> {
    const clashed = new Set<number>();

    for (const row of rows) {
        const teamGames = new Map<string, number[]>();
        for (const cell of row.cells) {
            if (!cell) continue;
            for (const tid of [cell.t1Id, cell.t2Id]) {
                if (!tid) continue;
                if (!teamGames.has(tid)) teamGames.set(tid, []);
                teamGames.get(tid)!.push(cell.gid);
            }
        }
        for (const gids of teamGames.values()) {
            if (gids.length > 1) gids.forEach(g => clashed.add(g));
        }
    }
    return clashed;
}

/**
 * NON-BREAKING: Same team in consecutive timeslot rows on the same calendar day (any division).
 * Only flags rows whose actual time gap is ≤ 90 minutes — filtered grids may place
 * non-consecutive timeslots in adjacent rows, which would otherwise create false positives.
 * Returns Set of game IDs that are back-to-back.
 */
export function computeBackToBackGameIds(rows: ScheduleGridRow[]): Set<number> {
    const MAX_GAP_MS = 90 * 60 * 1000; // 90 minutes
    const b2b = new Set<number>();

    for (let i = 0; i < rows.length - 1; i++) {
        const curTime = new Date(rows[i].gDate).getTime();
        const nextTime = new Date(rows[i + 1].gDate).getTime();
        if (nextTime - curTime > MAX_GAP_MS) continue;

        const curTeams = new Map<string, number[]>();
        for (const cell of rows[i].cells) {
            if (!cell) continue;
            for (const tid of [cell.t1Id, cell.t2Id]) {
                if (!tid) continue;
                if (!curTeams.has(tid)) curTeams.set(tid, []);
                curTeams.get(tid)!.push(cell.gid);
            }
        }

        for (const cell of rows[i + 1].cells) {
            if (!cell) continue;
            for (const tid of [cell.t1Id, cell.t2Id]) {
                if (!tid) continue;
                if (curTeams.has(tid)) {
                    curTeams.get(tid)!.forEach(g => b2b.add(g));
                    b2b.add(cell.gid);
                }
            }
        }
    }
    return b2b;
}

/**
 * Combined breaking conflict count: time clashes + backend-flagged slot collisions.
 */
export function computeBreakingConflictCount(rows: ScheduleGridRow[], timeClashIds: Set<number>): number {
    let count = timeClashIds.size;
    for (const row of rows) {
        for (const cell of row.cells) {
            if (cell?.isSlotCollision) count++;
        }
    }
    return count;
}

/** Check if game has backend-flagged slot collision (2+ games in same cell). */
export function isSlotCollision(game: ScheduleGameDto): boolean {
    return game.isSlotCollision === true;
}

/** Check if game is in a time clash set. */
export function isTimeClash(game: ScheduleGameDto, clashIds: Set<number>): boolean {
    return clashIds.has(game.gid);
}

/** Check if game is in a back-to-back set. */
export function isBackToBack(game: ScheduleGameDto, b2bIds: Set<number>): boolean {
    return b2bIds.has(game.gid);
}

/** Check if game has any breaking conflict (slot collision or time clash). */
export function isBreaking(game: ScheduleGameDto, clashIds: Set<number>): boolean {
    return isSlotCollision(game) || isTimeClash(game, clashIds);
}

/**
 * Pre-move check: would placing a game with these teamIds into this row cause a time clash?
 * Returns the clashing team's display label, or null if no clash.
 */
export function findTimeClashInRow(row: ScheduleGridRow, teamIds: string[], excludeGid: number): string | null {
    for (const cell of row.cells) {
        if (!cell || cell.gid === excludeGid) continue;
        for (const tid of teamIds) {
            if (cell.t1Id === tid) return cell.t1Label;
            if (cell.t2Id === tid) return cell.t2Label;
        }
    }
    return null;
}
