import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    ScheduleCascadeSnapshot,
    SaveEventDefaultsRequest,
    SaveCascadeLevelRequest,
} from '@core/api';

/**
 * Service for the 3-level scheduling cascade (Event → Agegroup → Division).
 * Properties: GamePlacement ("H"/"V"), BetweenRoundRows (0/1/2), Wave (per-date).
 *
 * Single source of truth — replaces:
 * - phantom waveAssignments in ScheduleConfig
 * - phantom placement/gapPattern in ScheduleConfig
 * - inferWaves() heuristic
 * - localStorage wave persistence
 */
@Injectable({ providedIn: 'root' })
export class ScheduleCascadeService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/schedule-cascade`;

    /** Resolved cascade snapshot (null until loaded). */
    readonly cascade = signal<ScheduleCascadeSnapshot | null>(null);

    /** Whether a load is in progress. */
    readonly isLoading = signal(false);

    // ── Read ──

    loadCascade(): Observable<ScheduleCascadeSnapshot> {
        this.isLoading.set(true);
        return this.http.get<ScheduleCascadeSnapshot>(this.apiUrl).pipe(
            tap(snapshot => {
                this.cascade.set(snapshot);
                this.isLoading.set(false);
            })
        );
    }

    // ── Write ──

    saveEventDefaults(request: SaveEventDefaultsRequest): Observable<ScheduleCascadeSnapshot> {
        return this.http.put<ScheduleCascadeSnapshot>(
            `${this.apiUrl}/event-defaults`, request
        ).pipe(tap(snapshot => this.cascade.set(snapshot)));
    }

    saveAgegroupOverride(agegroupId: string, request: SaveCascadeLevelRequest): Observable<ScheduleCascadeSnapshot> {
        return this.http.put<ScheduleCascadeSnapshot>(
            `${this.apiUrl}/agegroup/${agegroupId}`, request
        ).pipe(tap(snapshot => this.cascade.set(snapshot)));
    }

    saveDivisionOverride(divisionId: string, request: SaveCascadeLevelRequest): Observable<ScheduleCascadeSnapshot> {
        return this.http.put<ScheduleCascadeSnapshot>(
            `${this.apiUrl}/division/${divisionId}`, request
        ).pipe(tap(snapshot => this.cascade.set(snapshot)));
    }

    // ── Helpers ──

    /** Flatten cascade to per-division wave map (divisionId → wave, using first date or default 1). */
    getWaveMap(): Record<string, number> {
        const snapshot = this.cascade();
        if (!snapshot) return {};

        const result: Record<string, number> = {};
        for (const ag of snapshot.agegroups) {
            for (const div of ag.divisions) {
                const waves = Object.values(div.effectiveWavesByDate);
                result[div.divisionId] = waves.length > 0 ? waves[0] : 1;
            }
        }
        return result;
    }

    /** Get effective placement and gap for all divisions (name-keyed for backward compat). */
    getStrategyEntries(): { divisionName: string; placement: number; gapPattern: number }[] {
        const snapshot = this.cascade();
        if (!snapshot) return [];

        const seen = new Set<string>();
        const entries: { divisionName: string; placement: number; gapPattern: number }[] = [];

        for (const ag of snapshot.agegroups) {
            for (const div of ag.divisions) {
                const key = div.divisionName.toLowerCase();
                if (seen.has(key)) continue;
                seen.add(key);

                entries.push({
                    divisionName: div.divisionName,
                    placement: div.effectiveGamePlacement === 'V' ? 1 : 0,
                    gapPattern: div.effectiveBetweenRoundRows,
                });
            }
        }

        return entries.sort((a, b) => a.divisionName.localeCompare(b.divisionName));
    }
}
