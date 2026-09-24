import type { ClubTeamEventHistoryDto } from '@core/api';

/**
 * One chip per EVENT. A club team can carry several Teams rows in one job — dropped, re-entered,
 * dropped again (Test Club 1's 2030 Test has three for Merry Laxmas North 2025) — and the history
 * feed returns every row. A row per registration reads as three events and gives an @for tracked
 * by jobId duplicate keys.
 *
 * The winner for a job is the row that best says where the team stands there: a live
 * registration beats a dropped one, waitlisted beats dropped, and among equals the most recent
 * registration wins. Order is the order the first row for each job arrived in, so a caller that
 * pinned this event first keeps it first.
 *
 * Shared by the library page and the director's club-reps panel so the two never disagree.
 */
export function collapseHistoryPerEvent(history: readonly ClubTeamEventHistoryDto[]): ClubTeamEventHistoryDto[] {
    const byJob = new Map<string, ClubTeamEventHistoryDto>();
    for (const h of history) {
        const current = byJob.get(h.jobId);
        if (!current || beats(h, current)) byJob.set(h.jobId, h);
    }
    return [...byJob.values()];
}

function rank(h: ClubTeamEventHistoryDto): number {
    if (h.isDropped) return 0;
    if (h.isWaitlisted) return 1;
    return 2;
}

function beats(a: ClubTeamEventHistoryDto, b: ClubTeamEventHistoryDto): boolean {
    const ra = rank(a), rb = rank(b);
    if (ra !== rb) return ra > rb;
    return a.registeredOn > b.registeredOn;
}
