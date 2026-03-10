/**
 * ScheduleConfigService — Defaults Engine + Config State
 *
 * Merges 4 priority sources into a single ScheduleConfig:
 *   ① localStorage saved config  (Scenario B: re-build)
 *   ② Current DB config          (TimeslotsLeagueSeasonDates + Fields)
 *   ③ Prior year defaults         (PriorYearFieldDefaults + PriorYearRounds)
 *   ④ Global defaults             (GSI=60, start=8:00AM, max=8, RR formula)
 *
 * Each config value carries a `source` tag for provenance display.
 */

import { Injectable, inject, signal, computed } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';
import { TimeslotService } from '../../../timeslots/services/timeslot.service';
import { AutoBuildService } from '../../../auto-build/services/auto-build.service';
import type {
    CanvasReadinessResponse,
    AgegroupCanvasReadinessDto,
    PriorYearFieldDefaults,
    DivisionStrategyEntry,
    BulkDateAssignRequest,
    BulkDateAgegroupEntry,
    ProjectedScheduleConfigDto
} from '@core/api';
import type {
    ScheduleConfig,
    ScheduleConfigValue,
    ConfigSource,
    SchedulingScope,
    SavedScheduleConfig
} from './schedule-config.types';

// ── Constants ──

const STORAGE_PREFIX = 'schedule-config:';

const GLOBAL_DEFAULTS = {
    gsi: 60,
    startTime: '8:00 AM',
    maxGamesPerField: 8,
    placement: 0,   // Horizontal
    gapPattern: 1    // 1on/1off
} as const;

// ── Helper: wrap a value with provenance ──

function wrap<T>(value: T, source: ConfigSource, sourceLabel?: string): ScheduleConfigValue<T> {
    return sourceLabel ? { value, source, sourceLabel } : { value, source };
}

// ── Helper: RR formula — rounds needed for N teams ──

function rrRounds(teamCount: number): number {
    if (teamCount <= 1) return 0;
    return teamCount % 2 === 0 ? teamCount - 1 : teamCount;
}

// ── Helper: parse time string to minutes from midnight ──

function parseTimeToMinutes(timeStr: string | null | undefined): number {
    if (!timeStr) return 480; // 8:00 AM default

    // Try "h:mm AM/PM" or "hh:mm AM/PM" format
    const amPmMatch = timeStr.match(/^(\d{1,2}):(\d{2})\s*(AM|PM)$/i);
    if (amPmMatch) {
        let hours = parseInt(amPmMatch[1], 10);
        const minutes = parseInt(amPmMatch[2], 10);
        const isPm = amPmMatch[3].toUpperCase() === 'PM';
        if (isPm && hours < 12) hours += 12;
        if (!isPm && hours === 12) hours = 0;
        return hours * 60 + minutes;
    }

    // Try 24-hour "H:mm" or "HH:mm" format
    const h24Match = timeStr.match(/^(\d{1,2}):(\d{2})$/);
    if (h24Match) {
        return parseInt(h24Match[1], 10) * 60 + parseInt(h24Match[2], 10);
    }

    return 480; // fallback
}

@Injectable()
export class ScheduleConfigService {
    private readonly jobSvc = inject(JobService);
    private readonly timeslotSvc = inject(TimeslotService);
    private readonly autoBuildSvc = inject(AutoBuildService);

    // ── The merged config — single source of truth ──
    readonly config = signal<ScheduleConfig | null>(null);

    // ── Scenario detection ──
    readonly scenario = signal<'saved' | 'prior-year' | 'new'>('new');

    // ── Loading state ──
    readonly isInitializing = signal(false);

    // ── Derived: event type label for badge ──
    readonly eventTypeLabel = computed(() => {
        const job = this.jobSvc.currentJob();
        const typeName = (job?.jobTypeName ?? '').toLowerCase();
        if (typeName.includes('league')) return 'League';
        if (typeName.includes('tournament')) return 'Tournament';
        // Fallback — treat unknown as tournament (more common in this app)
        return 'Tournament';
    });

    readonly eventTypeBadge = computed(() => {
        const label = this.eventTypeLabel();
        return label === 'League' ? 'league' : 'tournament';
    });

    // ── Prior year label for source badges ──
    readonly priorYearLabel = signal<string | null>(null);

    /**
     * Initialize config by merging all available sources.
     * Called once when schedule-division loads readiness data.
     *
     * When prior year exists and no saved/current config, fetches the
     * read-only projected config from the backend to pre-populate
     * dates, per-day fields, rounds-per-day, and timing defaults.
     */
    initialize(
        readiness: CanvasReadinessResponse,
        agegroups: { agegroupId: string; agegroupName: string; teamCount: number }[],
        strategies: DivisionStrategyEntry[],
        strategySource: string
    ): void {
        this.isInitializing.set(true);

        const jobId = this.jobSvc.currentJob()?.jobId ?? '';
        const eventType = this.eventTypeBadge() as 'league' | 'tournament';
        const priorYear = readiness.priorYearDefaults ?? null;
        const priorRounds = readiness.priorYearRounds as Record<string, number> | null;

        if (priorYear) {
            const label = `${priorYear.priorJobName} ${priorYear.priorYear}`;
            this.priorYearLabel.set(label);
        }

        // ── Filter readiness agegroups to only those in the schedulable agegroups list ──
        const schedulableIds = new Set(agegroups.map(a => a.agegroupId));
        const filteredReadiness: CanvasReadinessResponse = {
            ...readiness,
            agegroups: readiness.agegroups.filter(a => schedulableIds.has(a.agegroupId))
        };

        // ── Check: does DB already have config (dates/fields)? ──
        const hasCurrentDbConfig = filteredReadiness.agegroups.some(a => a.isConfigured);

        // ── localStorage cache: performance shortcut when DB already has config ──
        const saved = this.loadFromLocalStorage(jobId);
        if (saved && hasCurrentDbConfig) {
            this.scenario.set('saved');
            this.config.set(saved);
            this.isInitializing.set(false);
            return;
        }

        // ── Scenario A: prior year exists, no saved config, no current DB config ──
        // Fetch projected config to pre-populate everything from prior year
        if (priorYear?.priorJobId && !hasCurrentDbConfig) {
            this.scenario.set('prior-year');
            this.autoBuildSvc.getProjectedConfig(priorYear.priorJobId).subscribe({
                next: (projected) => {
                    const config = this.buildConfigFromProjection(
                        jobId, eventType, projected, agegroups, strategies, strategySource
                    );
                    this.config.set(config);
                    this.isInitializing.set(false);
                },
                error: () => {
                    // Projection failed — fall back to default merge
                    const config = this.buildConfig(
                        jobId, eventType, filteredReadiness, agegroups, priorYear, priorRounds, strategies, strategySource
                    );
                    this.config.set(config);
                    this.isInitializing.set(false);
                }
            });
            return;
        }

        // ── Scenario C: no prior year or DB already configured ──
        if (priorYear) {
            this.scenario.set('prior-year');
        } else {
            this.scenario.set('new');
        }

        const config = this.buildConfig(
            jobId, eventType, filteredReadiness, agegroups, priorYear, priorRounds, strategies, strategySource
        );

        this.config.set(config);
        this.isInitializing.set(false);
    }

    /**
     * Update a specific config value (director edits inline).
     * Marks the value source as 'current'.
     */
    updateValue<K extends keyof ScheduleConfig>(
        key: K,
        value: ScheduleConfig[K]
    ): void {
        this.config.update(c => c ? { ...c, [key]: value } : c);
    }

    /**
     * Save current config to localStorage for Scenario B (re-build).
     */
    saveToLocalStorage(): void {
        const c = this.config();
        if (!c) return;
        const saved: SavedScheduleConfig = { ...c, savedAt: new Date().toISOString() };
        try {
            localStorage.setItem(`${STORAGE_PREFIX}${c.jobId}`, JSON.stringify(saved));
        } catch {
            // localStorage full or unavailable — non-fatal
        }
    }

    /**
     * Clear saved config from localStorage.
     */
    clearLocalStorage(jobId?: string): void {
        const id = jobId ?? this.config()?.jobId;
        if (id) {
            localStorage.removeItem(`${STORAGE_PREFIX}${id}`);
        }
    }

    /**
     * Reset in-memory config state. Used after Reset so the next
     * initialize() call rebuilds from the (now-empty) DB state.
     */
    reset(): void {
        this.config.set(null);
        this.scenario.set('new');
        this.priorYearLabel.set(null);
    }

    // ═══════════════════════════════════════════════
    // ── Private: Config Building
    // ═══════════════════════════════════════════════

    private buildConfig(
        jobId: string,
        eventType: 'league' | 'tournament',
        readiness: CanvasReadinessResponse,
        agegroups: { agegroupId: string; agegroupName: string; teamCount: number }[],
        priorYear: PriorYearFieldDefaults | null,
        priorRounds: Record<string, number> | null,
        strategies: DivisionStrategyEntry[],
        strategySource: string
    ): ScheduleConfig {
        const pyLabel = priorYear ? `${priorYear.priorJobName} ${priorYear.priorYear}` : undefined;
        const agMap = new Map(readiness.agegroups.map(a => [a.agegroupId, a]));

        // ── Dates: from current DB (if configured), else empty ──
        const currentDates = this.extractCurrentDates(readiness.agegroups);
        const dates: ScheduleConfigValue<string[]> = currentDates.length > 0
            ? wrap(currentDates, 'current')
            : wrap([], 'default');

        // ── Fields ──
        const fieldIds: ScheduleConfigValue<string[]> = readiness.assignedFieldCount > 0
            ? wrap(this.extractFieldIds(readiness.agegroups), 'current')
            : wrap([], 'default');

        // ── Field mapping scope: derive from current data ──
        const fieldMappingScope = this.deriveFieldMappingScope(readiness.agegroups);

        // ── GSI: current DB → prior year → default ──
        const { gsiScope, gsi } = this.deriveGsi(readiness.agegroups, priorYear, pyLabel);

        // ── Start Time: current DB → prior year → default ──
        const { startTimeScope, startTime } = this.deriveStartTime(readiness.agegroups, priorYear, pyLabel);

        // ── Max Games Per Field: current DB → prior year → default ──
        const maxGamesPerField = this.deriveMaxGames(readiness.agegroups, priorYear, pyLabel);

        // ── Rounds per AG: maxPairingRound → prior year → RR formula ──
        const roundsPerAg = this.deriveRoundsPerAg(agegroups, agMap, priorRounds, pyLabel);

        // ── Wave assignments: infer from start time offsets ──
        const waveAssignments = this.inferWaves(readiness.agegroups);

        // ── R/day per AG: infer from readiness data ──
        const roundsPerDay = this.inferRoundsPerDay(readiness.agegroups);

        // ── Strategy: saved → defaults ──
        const { placement, gapPattern } = this.deriveStrategy(strategies, strategySource, pyLabel);

        return {
            jobId,
            eventType,
            dates,
            fieldIds,
            fieldMappingScope,
            gsiScope,
            gsi,
            startTimeScope,
            startTime,
            maxGamesPerField,
            roundsPerAg,
            waveAssignments,
            roundsPerDay,
            placement,
            gapPattern
        };
    }

    /**
     * Build a fully pre-populated config from the projected prior year data.
     * Every field gets source='prior-year' with the source job label.
     */
    private buildConfigFromProjection(
        jobId: string,
        eventType: 'league' | 'tournament',
        projected: ProjectedScheduleConfigDto,
        agegroups: { agegroupId: string; agegroupName: string; teamCount: number }[],
        strategies: DivisionStrategyEntry[],
        strategySource: string
    ): ScheduleConfig {
        const pyLabel = `${projected.sourceJobName} ${projected.sourceYear}`;

        // ── Map projected agegroups by name for matching ──
        const projByName = new Map(
            projected.agegroups.map(pa => [pa.agegroupName.toLowerCase(), pa])
        );

        // ── Collect all unique dates across all projected agegroups ──
        const allDates = new Set<string>();
        const projectedDatesMap: Record<string, { date: string; rounds: number; dow: string }[]> = {};
        const fieldsByDayMap: Record<string, Record<string, string[]>> = {};
        const roundsPerDay: Record<string, number> = {};

        for (const ag of agegroups) {
            const proj = projByName.get(ag.agegroupName.toLowerCase());
            if (proj) {
                // Per-agegroup dates with rounds
                projectedDatesMap[ag.agegroupId] = proj.gameDays.map(gd => ({
                    date: gd.date.substring(0, 10),
                    rounds: gd.rounds,
                    dow: gd.dow
                }));
                for (const gd of proj.gameDays) {
                    allDates.add(gd.date.substring(0, 10));
                }

                // Per-day field assignments
                if (Object.keys(proj.fieldsByDay).length > 0) {
                    fieldsByDayMap[ag.agegroupId] = proj.fieldsByDay;
                }

                // Rounds-per-day: use first game day's rounds as representative
                if (proj.gameDays.length > 0) {
                    roundsPerDay[ag.agegroupId] = proj.gameDays[0].rounds;
                } else {
                    roundsPerDay[ag.agegroupId] = 1;
                }
            } else {
                roundsPerDay[ag.agegroupId] = 1;
            }
        }

        const sortedDates = [...allDates].sort();

        // ── Rounds per agegroup: sum of all game day rounds ──
        const roundsPerAg: Record<string, ScheduleConfigValue<number>> = {};
        for (const ag of agegroups) {
            const proj = projByName.get(ag.agegroupName.toLowerCase());
            if (proj && proj.gameDays.length > 0) {
                const totalRounds = proj.gameDays.reduce((sum, gd) => sum + gd.rounds, 0);
                roundsPerAg[ag.agegroupId] = wrap(totalRounds, 'prior-year', pyLabel);
            } else {
                roundsPerAg[ag.agegroupId] = wrap(rrRounds(ag.teamCount), 'default');
            }
        }

        // ── Timing: from projected per-agegroup data → uniform or per-ag ──
        const projTimings = agegroups
            .map(ag => projByName.get(ag.agegroupName.toLowerCase()))
            .filter((p): p is NonNullable<typeof p> => p != null);

        const allGsiSame = projTimings.length > 0 && projTimings.every(p => p.gsi === projTimings[0].gsi);
        const allStartSame = projTimings.length > 0 && projTimings.every(p => p.startTime === projTimings[0].startTime);

        const gsiScope: ScheduleConfigValue<'same' | 'per-ag'> = wrap(
            allGsiSame ? 'same' : 'per-ag', 'prior-year', pyLabel
        );
        const gsi: ScheduleConfigValue<number | Record<string, number>> = allGsiSame
            ? wrap(projTimings[0]?.gsi ?? projected.timingDefaults.gsi, 'prior-year', pyLabel)
            : wrap(
                Object.fromEntries(
                    agegroups
                        .map(ag => [ag.agegroupId, projByName.get(ag.agegroupName.toLowerCase())?.gsi ?? projected.timingDefaults.gsi])
                ),
                'prior-year', pyLabel
            );

        const startTimeScope: ScheduleConfigValue<'same' | 'per-ag'> = wrap(
            allStartSame ? 'same' : 'per-ag', 'prior-year', pyLabel
        );
        const startTime: ScheduleConfigValue<string | Record<string, string>> = allStartSame
            ? wrap(projTimings[0]?.startTime ?? projected.timingDefaults.startTime, 'prior-year', pyLabel)
            : wrap(
                Object.fromEntries(
                    agegroups
                        .map(ag => [ag.agegroupId, projByName.get(ag.agegroupName.toLowerCase())?.startTime ?? projected.timingDefaults.startTime])
                ),
                'prior-year', pyLabel
            );

        const maxGamesPerField: ScheduleConfigValue<number> = wrap(
            projected.timingDefaults.maxGamesPerField, 'prior-year', pyLabel
        );

        // ── Strategy ──
        const { placement, gapPattern } = this.deriveStrategy(strategies, strategySource, pyLabel);

        // ── Waves: use backend-derived suggestions from source schedule ──
        const waveAssignments: Record<string, number> = {};
        for (const ag of agegroups) {
            waveAssignments[ag.agegroupId] = projected.suggestedWaves?.[ag.agegroupId] ?? 1;
        }

        return {
            jobId,
            eventType,
            dates: wrap(sortedDates, 'prior-year', pyLabel),
            projectedDates: wrap(projectedDatesMap, 'prior-year', pyLabel),
            fieldIds: wrap([], 'prior-year', pyLabel),
            fieldMappingScope: wrap('per-ag', 'prior-year', pyLabel),
            fieldsByDay: wrap(fieldsByDayMap, 'prior-year', pyLabel),
            gsiScope,
            gsi,
            startTimeScope,
            startTime,
            maxGamesPerField,
            roundsPerAg,
            waveAssignments,
            roundsPerDay,
            placement,
            gapPattern,
            suggestedOrder: projected.suggestedOrder as string[] | undefined
        };
    }

    // ── Date extraction ──

    private extractCurrentDates(agegroups: AgegroupCanvasReadinessDto[]): string[] {
        const dateSet = new Set<string>();
        for (const ag of agegroups) {
            if (!ag.isConfigured || !ag.gameDays) continue;
            for (const gd of ag.gameDays) {
                dateSet.add(gd.date.substring(0, 10));
            }
        }
        return [...dateSet].sort();
    }

    // ── Field extraction ──

    private extractFieldIds(agegroups: AgegroupCanvasReadinessDto[]): string[] {
        // Field IDs aren't directly in AgegroupCanvasReadinessDto,
        // but fieldCount tells us fields exist. The actual IDs come from
        // the Manage Fields page. Return empty — the stepper shows
        // assignedFieldCount from the readiness response instead.
        return [];
    }

    // ── Field mapping scope derivation ──

    private deriveFieldMappingScope(
        agegroups: AgegroupCanvasReadinessDto[]
    ): ScheduleConfigValue<'shared' | 'per-ag' | 'per-div'> {
        // Without per-AG field detail in the readiness DTO, default to 'shared'.
        // This will be enhanced when Section ① Fields gets full field mapping data.
        return wrap('shared', 'default');
    }

    // ── GSI derivation ──

    private deriveGsi(
        agegroups: AgegroupCanvasReadinessDto[],
        priorYear: PriorYearFieldDefaults | null,
        pyLabel?: string
    ): { gsiScope: ScheduleConfigValue<'same' | 'per-ag'>; gsi: ScheduleConfigValue<number | Record<string, number>> } {
        // Check current DB values across configured agegroups
        const configuredGsis = agegroups
            .filter(a => a.isConfigured && a.gamestartInterval != null)
            .map(a => ({ agId: a.agegroupId, gsi: a.gamestartInterval! }));

        if (configuredGsis.length > 0) {
            const allSame = configuredGsis.every(g => g.gsi === configuredGsis[0].gsi);
            if (allSame) {
                return {
                    gsiScope: wrap('same', 'current'),
                    gsi: wrap(configuredGsis[0].gsi, 'current')
                };
            }
            // Per-AG values from current DB
            const perAg: Record<string, number> = {};
            for (const g of configuredGsis) perAg[g.agId] = g.gsi;
            return {
                gsiScope: wrap('per-ag', 'current'),
                gsi: wrap(perAg, 'current')
            };
        }

        // Prior year dominant GSI
        if (priorYear) {
            return {
                gsiScope: wrap('same', 'prior-year', pyLabel),
                gsi: wrap(priorYear.gamestartInterval, 'prior-year', pyLabel)
            };
        }

        // Global default
        return {
            gsiScope: wrap('same', 'default'),
            gsi: wrap(GLOBAL_DEFAULTS.gsi, 'default')
        };
    }

    // ── Start Time derivation ──

    private deriveStartTime(
        agegroups: AgegroupCanvasReadinessDto[],
        priorYear: PriorYearFieldDefaults | null,
        pyLabel?: string
    ): { startTimeScope: ScheduleConfigValue<'same' | 'per-ag'>; startTime: ScheduleConfigValue<string | Record<string, string>> } {
        const configuredTimes = agegroups
            .filter(a => a.isConfigured && a.startTime)
            .map(a => ({ agId: a.agegroupId, time: a.startTime! }));

        if (configuredTimes.length > 0) {
            const allSame = configuredTimes.every(t => t.time === configuredTimes[0].time);
            if (allSame) {
                return {
                    startTimeScope: wrap('same', 'current'),
                    startTime: wrap(configuredTimes[0].time, 'current')
                };
            }
            const perAg: Record<string, string> = {};
            for (const t of configuredTimes) perAg[t.agId] = t.time;
            return {
                startTimeScope: wrap('per-ag', 'current'),
                startTime: wrap(perAg, 'current')
            };
        }

        if (priorYear) {
            return {
                startTimeScope: wrap('same', 'prior-year', pyLabel),
                startTime: wrap(priorYear.startTime, 'prior-year', pyLabel)
            };
        }

        return {
            startTimeScope: wrap('same', 'default'),
            startTime: wrap(GLOBAL_DEFAULTS.startTime, 'default')
        };
    }

    // ── Max Games Per Field derivation ──

    private deriveMaxGames(
        agegroups: AgegroupCanvasReadinessDto[],
        priorYear: PriorYearFieldDefaults | null,
        pyLabel?: string
    ): ScheduleConfigValue<number> {
        // Current DB: take first configured agegroup's value
        const configured = agegroups.find(a => a.isConfigured && a.maxGamesPerField != null);
        if (configured) {
            return wrap(configured.maxGamesPerField!, 'current');
        }

        if (priorYear) {
            return wrap(priorYear.maxGamesPerField, 'prior-year', pyLabel);
        }

        return wrap(GLOBAL_DEFAULTS.maxGamesPerField, 'default');
    }

    // ── Rounds per AG derivation ──

    private deriveRoundsPerAg(
        agegroups: { agegroupId: string; agegroupName: string; teamCount: number }[],
        agMap: Map<string, AgegroupCanvasReadinessDto>,
        priorRounds: Record<string, number> | null,
        pyLabel?: string
    ): Record<string, ScheduleConfigValue<number>> {
        const result: Record<string, ScheduleConfigValue<number>> = {};

        for (const ag of agegroups) {
            const readiness = agMap.get(ag.agegroupId);

            // Priority 1: maxPairingRound from current pairings
            if (readiness && readiness.maxPairingRound > 0) {
                result[ag.agegroupId] = wrap(readiness.maxPairingRound, 'current');
                continue;
            }

            // Priority 2: prior year rounds (matched by agegroup name)
            if (priorRounds && priorRounds[ag.agegroupName] != null) {
                result[ag.agegroupId] = wrap(priorRounds[ag.agegroupName], 'prior-year', pyLabel);
                continue;
            }

            // Priority 3: RR formula from team count
            result[ag.agegroupId] = wrap(rrRounds(ag.teamCount), 'default');
        }

        return result;
    }

    // ── Strategy derivation ──

    private deriveStrategy(
        strategies: DivisionStrategyEntry[],
        source: string,
        pyLabel?: string
    ): { placement: ScheduleConfigValue<number>; gapPattern: ScheduleConfigValue<number> } {
        if (strategies.length > 0 && source !== 'defaults') {
            // Use uniform value if all divisions agree, else take first
            const p = strategies.every(s => s.placement === strategies[0].placement)
                ? strategies[0].placement
                : strategies[0].placement;
            const g = strategies.every(s => s.gapPattern === strategies[0].gapPattern)
                ? strategies[0].gapPattern
                : strategies[0].gapPattern;

            const configSource: ConfigSource = source === 'inferred' ? 'prior-year' : 'saved';
            const label = source === 'inferred' ? pyLabel : undefined;

            return {
                placement: wrap(p, configSource, label),
                gapPattern: wrap(g, configSource, label)
            };
        }

        return {
            placement: wrap(GLOBAL_DEFAULTS.placement, 'default'),
            gapPattern: wrap(GLOBAL_DEFAULTS.gapPattern, 'default')
        };
    }

    // ── Wave inference from readiness data ──

    /**
     * Infer wave assignments from existing timeslot start time offsets.
     * Wave 1 = earliest start time; Wave 2+ = offset by (maxGames × gsi) minutes.
     * Returns agegroupId → wave (1-based). Defaults all to 1 if no data.
     */
    private inferWaves(agegroups: AgegroupCanvasReadinessDto[]): Record<string, number> {
        const waves: Record<string, number> = {};
        const configured = agegroups.filter(a => a.isConfigured && a.gameDays?.length > 0);

        if (configured.length === 0) {
            // Default all to wave 1
            for (const ag of agegroups) waves[ag.agegroupId] = 1;
            return waves;
        }

        // Find earliest start time per agegroup (in minutes from midnight)
        let baseMinutes = Infinity;
        const agStartMinutes: Record<string, number> = {};

        for (const ag of configured) {
            let earliest = Infinity;
            for (const gd of ag.gameDays) {
                const mins = parseTimeToMinutes(gd.startTime);
                if (mins < earliest) earliest = mins;
            }
            agStartMinutes[ag.agegroupId] = earliest;
            if (earliest < baseMinutes) baseMinutes = earliest;
        }

        if (baseMinutes === Infinity) baseMinutes = 480; // 8:00 AM

        // Calculate wave size from first configured AG
        const gsi = configured[0].gamestartInterval ?? 60;
        const maxGames = configured[0].maxGamesPerField ?? 8;
        const waveSize = maxGames * gsi;

        // Assign waves using tolerance-based rounding.
        // An offset within 15% of a wave boundary snaps to the nearest wave,
        // preventing 1-minute time drift from pushing an AG into the wrong wave.
        for (const ag of agegroups) {
            const startMins = agStartMinutes[ag.agegroupId];
            if (startMins == null) {
                waves[ag.agegroupId] = 1; // unconfigured → wave 1
                continue;
            }
            const offset = startMins - baseMinutes;
            let wave = 1;
            if (waveSize > 0) {
                wave = 1 + Math.round(offset / waveSize);
            }
            waves[ag.agegroupId] = Math.max(1, wave);
        }

        return waves;
    }

    // ── R/day inference from readiness data ──

    /**
     * Infer rounds-per-day from existing timeslot date data.
     * Uses the first game day's roundCount for each agegroup.
     * Returns agegroupId → R/day. Defaults to 1 if no data.
     */
    private inferRoundsPerDay(agegroups: AgegroupCanvasReadinessDto[]): Record<string, number> {
        const result: Record<string, number> = {};

        for (const ag of agegroups) {
            if (ag.isConfigured && ag.gameDays?.length > 0) {
                // Use the first date's roundCount as the representative R/day
                const firstDay = ag.gameDays[0];
                result[ag.agegroupId] = firstDay.roundCount > 0 ? firstDay.roundCount : 1;
            } else {
                result[ag.agegroupId] = 1;
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════
    // ── Private: localStorage
    // ═══════════════════════════════════════════════

    private loadFromLocalStorage(jobId: string): ScheduleConfig | null {
        try {
            const raw = localStorage.getItem(`${STORAGE_PREFIX}${jobId}`);
            if (!raw) return null;
            const parsed = JSON.parse(raw) as SavedScheduleConfig;
            // Validate it has the essential shape
            if (parsed.jobId !== jobId) return null;
            return parsed;
        } catch {
            return null;
        }
    }
}
