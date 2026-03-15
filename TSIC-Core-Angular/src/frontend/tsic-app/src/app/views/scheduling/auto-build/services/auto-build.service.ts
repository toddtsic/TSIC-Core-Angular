import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    AutoBuildRequest,
    AutoBuildResult,
    DivisionStrategyEntry,
    DivisionStrategyProfileResponse,
    EnsurePairingsRequest,
    EnsurePairingsResponse,
    GameSummaryResponse,
    PrerequisiteCheckResponse,
    ProfileExtractionResponse,
    ProjectedScheduleConfigDto,
    SaveGameGuaranteeRequest,
    SaveGameGuaranteeResponse,
} from '@core/api';

export interface DevResetPreconfigResult {
    colorsApplied: number;
    datesSeeded: number;
    fieldAssignmentsSeeded: number;
    fieldTimeslotRowsCreated: number;
    pairingsGenerated: number[];
    pairingsAlreadyExisted: number[];
    cascadeSeeded: boolean;
    fieldsLeagueSeasonCopied: boolean;
}

@Injectable({ providedIn: 'root' })
export class AutoBuildService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/auto-build`;

    getGameSummary(): Observable<GameSummaryResponse> {
        return this.http.get<GameSummaryResponse>(`${this.apiUrl}/game-summary`);
    }

    undo(gameDate?: string): Observable<{ gamesDeleted: number }> {
        return this.http.post<{ gamesDeleted: number }>(
            `${this.apiUrl}/undo`,
            gameDate ? { gameDate } : {}
        );
    }

    checkPrerequisites(): Observable<PrerequisiteCheckResponse> {
        return this.http.get<PrerequisiteCheckResponse>(
            `${this.apiUrl}/prerequisites`
        );
    }

    extractProfiles(sourceJobId: string): Observable<ProfileExtractionResponse> {
        return this.http.post<ProfileExtractionResponse>(
            `${this.apiUrl}/extract-profiles`,
            { sourceJobId }
        );
    }

    execute(request: AutoBuildRequest): Observable<AutoBuildResult> {
        return this.http.post<AutoBuildResult>(
            `${this.apiUrl}/execute`,
            request
        );
    }

    getStrategyProfiles(sourceJobId?: string): Observable<DivisionStrategyProfileResponse> {
        const params = sourceJobId ? `?sourceJobId=${sourceJobId}` : '';
        return this.http.get<DivisionStrategyProfileResponse>(
            `${this.apiUrl}/strategy-profiles${params}`
        );
    }

    saveStrategyProfiles(strategies: DivisionStrategyEntry[]): Observable<DivisionStrategyProfileResponse> {
        return this.http.put<DivisionStrategyProfileResponse>(
            `${this.apiUrl}/strategy-profiles`,
            strategies
        );
    }

    getProjectedConfig(sourceJobId: string): Observable<ProjectedScheduleConfigDto> {
        return this.http.get<ProjectedScheduleConfigDto>(
            `${this.apiUrl}/projected-config?sourceJobId=${sourceJobId}`
        );
    }

    ensurePairings(request: EnsurePairingsRequest): Observable<EnsurePairingsResponse> {
        return this.http.post<EnsurePairingsResponse>(
            `${this.apiUrl}/ensure-pairings`,
            request
        );
    }

    saveGameGuarantee(request: SaveGameGuaranteeRequest): Observable<SaveGameGuaranteeResponse> {
        return this.http.put<SaveGameGuaranteeResponse>(
            `${environment.apiUrl}/dev-scheduling/game-guarantee`,
            request
        );
    }

    /** Clear selected scheduling config, then optionally preconfigure from source. */
    resetSchedule(options: {
        games: boolean;
        strategyProfiles: boolean;
        pairings: boolean;
        dates?: boolean;
        fieldTimeslots?: boolean;
        fieldAssignments: boolean;
        sourceJobId?: string;
    }): Observable<{
        gamesDeleted: number;
        agegroupsCleared: number;
        pairingGroupsCleared: number;
        fieldsCleared: number;
        profilesCleared: boolean;
        preconfig: DevResetPreconfigResult | null;
    }> {
        return this.http.post<{
            gamesDeleted: number;
            agegroupsCleared: number;
            pairingGroupsCleared: number;
            fieldsCleared: number;
            profilesCleared: boolean;
            preconfig: DevResetPreconfigResult | null;
        }>(
            `${environment.apiUrl}/dev-scheduling/reset`,
            options
        );
    }
}
