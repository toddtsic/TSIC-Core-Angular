import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, switchMap, tap } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    ScheduleCascadeSnapshot,
    SaveEventDefaultsRequest,
    SaveCascadeLevelRequest,
    SeedWavesRequest,
    ProcessingOrderEntryDto,
    SaveProcessingOrderRequest,
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

    // ── Write (save then reload snapshot) ──

    saveEventDefaults(request: SaveEventDefaultsRequest): Observable<ScheduleCascadeSnapshot> {
        return this.http.put<unknown>(
            `${this.apiUrl}/event-defaults`, request
        ).pipe(switchMap(() => this.loadCascade()));
    }

    saveAgegroupOverride(agegroupId: string, request: SaveCascadeLevelRequest): Observable<ScheduleCascadeSnapshot> {
        return this.http.put<unknown>(
            `${this.apiUrl}/agegroup/${agegroupId}`, request
        ).pipe(switchMap(() => this.loadCascade()));
    }

    saveDivisionOverride(divisionId: string, request: SaveCascadeLevelRequest): Observable<ScheduleCascadeSnapshot> {
        return this.http.put<unknown>(
            `${this.apiUrl}/division/${divisionId}`, request
        ).pipe(switchMap(() => this.loadCascade()));
    }

    /** Bulk-seed division waves from projected config (only seeds divisions with no existing waves). */
    seedWaves(request: SeedWavesRequest): Observable<ScheduleCascadeSnapshot> {
        return this.http.post<ScheduleCascadeSnapshot>(
            `${this.apiUrl}/seed-waves`, request
        ).pipe(tap(snapshot => this.cascade.set(snapshot)));
    }

    // ── Processing Order ──

    getProcessingOrder(): Observable<ProcessingOrderEntryDto[]> {
        return this.http.get<ProcessingOrderEntryDto[]>(
            `${this.apiUrl}/processing-order`
        );
    }

    saveProcessingOrder(entries: ProcessingOrderEntryDto[]): Observable<unknown> {
        const request: SaveProcessingOrderRequest = { entries };
        return this.http.put(`${this.apiUrl}/processing-order`, request);
    }

    deleteProcessingOrder(): Observable<unknown> {
        return this.http.delete(`${this.apiUrl}/processing-order`);
    }

    // ── Helpers ──

    /** True if all divisions in the cascade have empty wave assignments (no wave differentiation). */
    hasNoWaves(): boolean {
        const snapshot = this.cascade();
        if (!snapshot) return true;
        return snapshot.agegroups.every(ag =>
            ag.divisions.every(div =>
                Object.keys(div.effectiveWavesByDate).length === 0
            )
        );
    }

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
