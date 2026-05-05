import { Injectable } from '@angular/core';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';
import {
    SCHEDULE_FILTERS_SCHEMA_VERSION,
    ScheduleFiltersStoreV1,
    TournamentFilterState,
    emptyTournamentState,
} from '../models/schedule-filters-store.model';

/**
 * Typed wrapper around localStorage for schedule-page filter persistence.
 * All read paths go through `parse()` which validates schemaVersion before
 * trusting the payload — corrupt or future-version data is treated as absent.
 */
@Injectable({ providedIn: 'root' })
export class ScheduleFiltersStore {
    private readonly key = LocalStorageKey.ScheduleFilters;

    /** Returns the persisted state for `jobPath`, or null if no entry exists. */
    getFor(jobPath: string): TournamentFilterState | null {
        if (!jobPath) return null;
        const store = this.read();
        return store.tournamentsByJobPath[jobPath] ?? null;
    }

    /** Patch persisted state for `jobPath`, merging into any existing entry. */
    patch(jobPath: string, partial: Partial<TournamentFilterState>): void {
        if (!jobPath) return;
        const store = this.read();
        const current = store.tournamentsByJobPath[jobPath] ?? emptyTournamentState();
        const next: TournamentFilterState = {
            ...current,
            ...partial,
            updatedAt: new Date().toISOString(),
        };
        this.write({
            schemaVersion: SCHEDULE_FILTERS_SCHEMA_VERSION,
            tournamentsByJobPath: { ...store.tournamentsByJobPath, [jobPath]: next },
        });
    }

    /** Hard-clear the entry for `jobPath`. */
    clear(jobPath: string): void {
        if (!jobPath) return;
        const store = this.read();
        if (!(jobPath in store.tournamentsByJobPath)) return;
        const next = { ...store.tournamentsByJobPath };
        delete next[jobPath];
        this.write({
            schemaVersion: SCHEDULE_FILTERS_SCHEMA_VERSION,
            tournamentsByJobPath: next,
        });
    }

    private read(): ScheduleFiltersStoreV1 {
        const raw = localStorage.getItem(this.key);
        if (!raw) return this.empty();
        try {
            const parsed = JSON.parse(raw) as Partial<ScheduleFiltersStoreV1>;
            if (parsed?.schemaVersion !== SCHEDULE_FILTERS_SCHEMA_VERSION) return this.empty();
            const tournaments = parsed.tournamentsByJobPath;
            if (!tournaments || typeof tournaments !== 'object') return this.empty();
            return { schemaVersion: SCHEDULE_FILTERS_SCHEMA_VERSION, tournamentsByJobPath: tournaments };
        } catch {
            return this.empty();
        }
    }

    private write(store: ScheduleFiltersStoreV1): void {
        try {
            localStorage.setItem(this.key, JSON.stringify(store));
        } catch {
            // Quota exceeded or storage disabled — degrade silently; next load just won't restore.
        }
    }

    private empty(): ScheduleFiltersStoreV1 {
        return { schemaVersion: SCHEDULE_FILTERS_SCHEMA_VERSION, tournamentsByJobPath: {} };
    }
}
