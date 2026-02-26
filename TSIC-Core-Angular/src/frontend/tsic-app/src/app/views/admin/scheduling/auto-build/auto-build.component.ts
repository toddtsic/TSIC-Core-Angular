import {
    Component, ChangeDetectionStrategy, signal, computed,
    inject, ElementRef, viewChild, AfterViewChecked
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';

import { AutoBuildService } from './services/auto-build.service';
import { ScheduleQaService } from '../qa-results/services/schedule-qa.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { environment } from '@environments/environment';

import type {
    AutoBuildSourceJobDto,
    AutoBuildAnalysisResponse,
    AutoBuildResult,
    AutoBuildRequest,
    AutoBuildQaResult,
    GameSummaryResponse,
    ScheduleGameSummaryDto,
    AgegroupMappingResponse,
    ConfirmedAgegroupMapping,
    PoolSizeCoverage,
} from '@core/api';

// ── Step enum (sequential flow) ──────────────────────────
type AutoBuildStep =
    | 'loading'      // Fetching game summary
    | 'summary'      // Step 1: Show current game status
    | 'mapping'      // Step 1.5: Confirm agegroup mappings
    | 'analyzing'    // Step 2: Auto-analyzing prior year
    | 'analysis'     // Step 2b: Show analysis results
    | 'building'     // Step 3: Building schedule
    | 'results'      // Step 3b: Build results + QA
    | 'error';

// ── Agent message model ───────────────────────────────────
type AgentMessageType = 'thinking' | 'info' | 'success' | 'warning' | 'question' | 'error' | 'result' | 'pitch';

interface AgentMessage {
    id: string;
    type: AgentMessageType;
    content: string;
    timestamp: Date;
}

@Component({
    selector: 'app-auto-build',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterModule, ConfirmDialogComponent],
    templateUrl: './auto-build.component.html',
    styleUrl: './auto-build.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AutoBuildComponent implements AfterViewChecked {
    private readonly svc = inject(AutoBuildService);
    private readonly qaSvc = inject(ScheduleQaService);

    // ── Scroll anchor ─────────────────────────────────────
    private readonly scrollContainer = viewChild<ElementRef<HTMLElement>>('scrollContainer');
    private shouldScroll = false;

    // ── State ─────────────────────────────────────────────
    step = signal<AutoBuildStep>('loading');
    messages = signal<AgentMessage[]>([]);

    // Step 1: Game summary
    gameSummary = signal<GameSummaryResponse | null>(null);
    isDeleting = signal(false);

    // Confirm dialog state (shared by Delete All + Undo All)
    showConfirmDialog = signal(false);
    confirmDialogTitle = signal('');
    confirmDialogMessage = signal('');
    confirmDialogAction = signal<(() => void) | null>(null);

    // Step 1.5: Agegroup mapping
    sourceJobs = signal<AutoBuildSourceJobDto[]>([]);
    selectedSourceJob = signal<AutoBuildSourceJobDto | null>(null);
    mappingResponse = signal<AgegroupMappingResponse | null>(null);
    confirmedMappings = signal<ConfirmedAgegroupMapping[]>([]);

    // Step 2: Analysis
    analysis = signal<AutoBuildAnalysisResponse | null>(null);

    // Step 3: Build
    buildResult = signal<AutoBuildResult | null>(null);
    isUndoing = signal(false);

    // Build options (set to sensible defaults, no UI toggle needed)
    readonly includeBracketGames = signal(false);
    readonly skipAlreadyScheduled = signal(true);

    // ── Summary state helpers ─────────────────────────────
    isFullyScheduled = computed(() => {
        const gs = this.gameSummary();
        if (!gs || gs.totalDivisions === 0) return false;
        return gs.divisionsWithGames === gs.totalDivisions;
    });

    // ── Tree: group divisions by agegroup ─────────────────
    agegroupTree = computed(() => {
        const gs = this.gameSummary();
        if (!gs) return [];
        const map = new Map<string, { agegroupName: string; agegroupId: string; agegroupColor: string | null; divisions: ScheduleGameSummaryDto[]; totalGames: number; totalExpected: number; totalTeams: number }>();
        for (const div of gs.divisions) {
            let group = map.get(div.agegroupId);
            if (!group) {
                group = { agegroupName: div.agegroupName, agegroupId: div.agegroupId, agegroupColor: div.agegroupColor ?? null, divisions: [], totalGames: 0, totalExpected: 0, totalTeams: 0 };
                map.set(div.agegroupId, group);
            }
            group.divisions.push(div);
            group.totalGames += div.gameCount;
            group.totalExpected += div.expectedRrGames;
            group.totalTeams += div.teamCount;
        }
        return Array.from(map.values());
    });

    expandedAgegroups = signal<Set<string>>(new Set());

    toggleAgegroup(agegroupId: string): void {
        this.expandedAgegroups.update(set => {
            const next = new Set(set);
            if (next.has(agegroupId)) next.delete(agegroupId);
            else next.add(agegroupId);
            return next;
        });
    }

    isExpanded(agegroupId: string): boolean {
        return this.expandedAgegroups().has(agegroupId);
    }

    getAgegroupStatus(group: { totalGames: number; totalExpected: number }): 'complete' | 'partial' | 'empty' {
        if (group.totalGames === 0) return 'empty';
        if (group.totalGames >= group.totalExpected) return 'complete';
        return 'partial';
    }

    // ── 3-state coverage computed helpers ─────────────────
    nameMatchedDivisions = computed(() =>
        (this.analysis()?.divisionCoverage ?? []).filter(d => d.matchStrategy === 'name-matched')
    );

    poolSizeFallbackDivisions = computed(() =>
        (this.analysis()?.divisionCoverage ?? []).filter(d => d.matchStrategy === 'pool-size-fallback')
    );

    noMatchDivisions = computed(() =>
        (this.analysis()?.divisionCoverage ?? []).filter(d => d.matchStrategy === 'no-match')
    );

    noMatchDivisionNames = computed(() =>
        this.noMatchDivisions().map(d => d.divName).join(', ')
    );

    availablePatterns = computed(() =>
        this.analysis()?.feasibility?.availablePatterns ?? []
    );

    /** Group coverage divisions by agegroup for sectioned display */
    coverageByAgegroup = computed(() => {
        const coverage = this.analysis()?.divisionCoverage ?? [];
        const map = new Map<string, { agegroupName: string; divisions: PoolSizeCoverage[] }>();
        for (const div of coverage) {
            let group = map.get(div.agegroupName);
            if (!group) {
                group = { agegroupName: div.agegroupName, divisions: [] };
                map.set(div.agegroupName, group);
            }
            group.divisions.push(div);
        }
        return Array.from(map.values());
    });

    // ── Agegroup color helpers ────────────────────────────
    /** Build inline style for an agegroup badge using its entity color */
    agBadgeStyle(hexColor: string | null | undefined): Record<string, string> {
        if (!hexColor) return {};
        const rgb = this.hexToRgb(hexColor);
        if (!rgb) return {};
        const luminance = (0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b) / 255;
        return {
            background: `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, 0.15)`,
            color: hexColor,
            // For very light colors, darken the text for readability
            ...(luminance > 0.7 ? { color: `rgb(${Math.round(rgb.r * 0.55)}, ${Math.round(rgb.g * 0.55)}, ${Math.round(rgb.b * 0.55)})` } : {})
        };
    }

    private hexToRgb(hex: string): { r: number; g: number; b: number } | null {
        const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
        return result ? {
            r: parseInt(result[1], 16),
            g: parseInt(result[2], 16),
            b: parseInt(result[3], 16)
        } : null;
    }

    /** Get color for a current agegroup name from the mapping response color map */
    currentAgColor(agName: string): string | null {
        return this.mappingResponse()?.currentAgegroupColors?.[agName] ?? null;
    }

    // ── Lifecycle ─────────────────────────────────────────
    constructor() {
        this.loadGameSummary();
    }

    ngAfterViewChecked(): void {
        if (this.shouldScroll) {
            this.scrollToBottom();
            this.shouldScroll = false;
        }
    }

    private scrollToBottom(): void {
        const el = this.scrollContainer()?.nativeElement;
        if (el) {
            el.scrollTop = el.scrollHeight;
        }
    }

    // ── Message helpers ───────────────────────────────────
    private addMessage(type: AgentMessageType, content: string): void {
        const msg: AgentMessage = {
            id: crypto.randomUUID(),
            type,
            content,
            timestamp: new Date()
        };
        this.messages.update(msgs => [...msgs, msg]);
        this.shouldScroll = true;
    }

    private removeThinking(): void {
        this.messages.update(msgs => msgs.filter(m => m.type !== 'thinking'));
    }

    // ══════════════════════════════════════════════════════
    // Step 1: Game Summary
    // ══════════════════════════════════════════════════════

    private loadGameSummary(): void {
        this.step.set('loading');

        this.svc.getGameSummary().subscribe({
            next: (summary) => {
                this.gameSummary.set(summary);
                this.step.set('summary');
            },
            error: () => {
                this.gameSummary.set({
                    totalGames: 0,
                    totalDivisions: 0,
                    divisionsWithGames: 0,
                    divisions: []
                });
                this.step.set('summary');
            }
        });
    }

    getDivStatus(div: ScheduleGameSummaryDto): 'complete' | 'partial' | 'empty' {
        if (div.gameCount === 0) return 'empty';
        if (div.gameCount >= div.expectedRrGames) return 'complete';
        return 'partial';
    }

    breakdownAndRebuild(): void {
        const total = this.gameSummary()?.totalGames ?? 0;
        this.showDestructiveConfirm(
            'Breakdown Schedule & Re-Auto-Build',
            `<p>This will delete ALL <strong>${total}</strong> scheduled games (round-robin, bracket, and any other game types) and start a fresh auto-build.</p>`,
            () => {
                this.isDeleting.set(true);
                this.svc.undo().subscribe({
                    next: () => {
                        this.isDeleting.set(false);
                        this.continueToAnalysis();
                    },
                    error: (err) => {
                        this.isDeleting.set(false);
                        this.addMessage('error',
                            err.status === 403
                                ? 'Delete blocked: only available in Development environment.'
                                : `Delete failed: ${err.error?.message ?? 'Unknown error'}`
                        );
                        this.step.set('summary');
                    }
                });
            }
        );
    }

    /** Shared confirm dialog for all destructive operations */
    private showDestructiveConfirm(title: string, bodyHtml: string, action: () => void): void {
        const host = window.location.hostname;
        const api = environment.apiUrl;
        this.confirmDialogTitle.set(title);
        this.confirmDialogMessage.set(
            bodyHtml +
            `<p class="text-success mb-0">You are safe to proceed — you are working on the development server as evidenced by:</p>` +
            `<ul class="mb-0"><li>Host: <strong>${host}</strong></li>` +
            `<li>API: <strong>${api}</strong></li></ul>`
        );
        this.confirmDialogAction.set(action);
        this.showConfirmDialog.set(true);
    }

    onConfirmDialogAccepted(): void {
        const action = this.confirmDialogAction();
        this.showConfirmDialog.set(false);
        this.confirmDialogAction.set(null);
        action?.();
    }

    onConfirmDialogCancelled(): void {
        this.showConfirmDialog.set(false);
        this.confirmDialogAction.set(null);
    }

    // ══════════════════════════════════════════════════════
    // Step 1.5: Source Job Selection → Propose Mappings
    // ══════════════════════════════════════════════════════

    continueToAnalysis(): void {
        this.step.set('analyzing');
        this.addMessage('info', 'Finding prior-year schedules...');
        this.addMessage('thinking', 'Searching for source jobs...');

        this.svc.getSourceJobs().subscribe({
            next: (jobs) => {
                this.removeThinking();
                this.sourceJobs.set(jobs);

                if (jobs.length === 0) {
                    this.addMessage('warning',
                        'No prior-year jobs with scheduled games found. You\'ll need to use manual scheduling instead.'
                    );
                    this.step.set('error');
                    return;
                }

                // Auto-select the most recent/largest prior year
                const source = jobs[0];
                this.selectedSourceJob.set(source);
                this.addMessage('success',
                    `Using <span class="msg-badge">${source.jobName}</span> (${source.year ?? 'N/A'}) as template — <span class="db db-game">${source.scheduledGameCount}</span> games.`
                );

                // Propose agegroup mappings for user confirmation
                this.loadMappings(source.jobId);
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Failed to load source jobs: ${err.error?.message ?? 'Unknown error'}`);
                this.step.set('error');
            }
        });
    }

    private loadMappings(sourceJobId: string): void {
        this.addMessage('thinking', 'Proposing agegroup mappings...');

        this.svc.proposeMappings(sourceJobId).subscribe({
            next: (response) => {
                this.removeThinking();
                this.mappingResponse.set(response);

                // Initialize confirmed mappings from proposals
                const mappings: ConfirmedAgegroupMapping[] = response.proposals.map(p => ({
                    sourceAgegroupName: p.sourceAgegroupName,
                    currentAgegroupName: p.proposedCurrentAgegroupName ?? undefined,
                }));
                this.confirmedMappings.set(mappings);

                const matched = response.proposals.filter(p => p.matchStrategy !== 'none').length;
                this.addMessage('info',
                    `Found <span class="db db-ag">${response.proposals.length}</span> source agegroups — ` +
                    `<span class="db db-ag">${matched}</span> auto-matched. Please confirm the mappings below.`
                );
                this.step.set('mapping');
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Failed to propose mappings: ${err.error?.message ?? 'Unknown error'}`);
                this.step.set('error');
            }
        });
    }

    /** Update a confirmed mapping when user changes a dropdown */
    updateMapping(sourceAgegroupName: string, currentAgegroupName: string | null): void {
        this.confirmedMappings.update(mappings =>
            mappings.map(m =>
                m.sourceAgegroupName === sourceAgegroupName
                    ? { ...m, currentAgegroupName: currentAgegroupName || undefined }
                    : m
            )
        );
    }

    /** Get current confirmed mapping value for a given source agegroup */
    getMappedValue(sourceAgegroupName: string): string {
        const mapping = this.confirmedMappings().find(m => m.sourceAgegroupName === sourceAgegroupName);
        return mapping?.currentAgegroupName ?? '';
    }

    /** Confirm mappings and run analysis */
    confirmMappingsAndAnalyze(): void {
        const source = this.selectedSourceJob();
        if (!source) return;

        this.step.set('analyzing');
        this.runAnalysis(source.jobId);
    }

    // ══════════════════════════════════════════════════════
    // Step 2: Analysis (with confirmed mappings)
    // ══════════════════════════════════════════════════════

    private runAnalysis(sourceJobId: string): void {
        this.addMessage('thinking', 'Extracting patterns by name and pool size, checking fields...');

        const mappings = this.confirmedMappings().length > 0
            ? this.confirmedMappings()
            : undefined;

        this.svc.analyze(sourceJobId, mappings).subscribe({
            next: (result) => {
                this.removeThinking();
                this.analysis.set(result);
                this.presentAnalysis(result);
                this.step.set('analysis');
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Analysis failed: ${err.error?.message ?? 'Unknown error'}`);
                this.step.set('error');
            }
        });
    }

    private presentAnalysis(result: AutoBuildAnalysisResponse): void {
        const f = result.feasibility;

        this.addMessage('info',
            `<strong>Pattern Analysis Complete</strong> — <span class="db db-game">${result.sourceTotalGames}</span> games extracted from <span class="msg-badge">${result.sourceYear}</span><br><br>` +
            `<strong>Coverage: ${f.confidencePercent}%</strong> — proven patterns for <span class="db db-div">${f.coveredDivisions}</span> of <span class="db db-div">${f.coveredDivisions + f.uncoveredDivisions}</span> divisions`
        );

        // Only show address-matched fields (positive signal); suppress non-actionable warnings
        for (const w of f.warnings) {
            if (w.includes('same address')) {
                this.addMessage('success', w);
            }
        }

        // Build the pitch — explain what auto-build actually does
        const totalDiv = f.coveredDivisions + f.uncoveredDivisions;
        let pitch = `<strong>Ready to Build Your Schedule</strong><br><br>`;

        pitch += `Last year's director spent hours placing <span class="db db-game">${result.sourceTotalGames}</span> games across <span class="db db-div">${totalDiv}</span> divisions — `;
        pitch += `and none of it was random. Which pools play on which fields. What time of day. Which tournament day. `;
        pitch += `Every single placement was a deliberate decision shaped by people who know your event:<br><br>`;

        pitch += `<span class="pitch-stakeholders">`;
        pitch += `<span class="pitch-point">College recruiters need top-ranked teams on showcase fields at peak viewing times</span>`;
        pitch += `<span class="pitch-point">Parents need younger age groups finishing early so families can get home</span>`;
        pitch += `<span class="pitch-point">Vendors need foot traffic spread across the full day, not front-loaded</span>`;
        pitch += `<span class="pitch-point">Coaches need rest time between games — no back-to-back for the same pool</span>`;
        pitch += `<span class="pitch-point">Parking and field proximity shaped which pools play where and when</span>`;
        pitch += `</span><br>`;

        pitch += `We captured <em>all</em> of that scheduling intelligence. `;

        const covered = f.coveredDivisions;
        if (covered === totalDiv) {
            pitch += `We found proven patterns for <strong>all ${totalDiv}</strong> divisions.`;
        } else {
            const fresh = totalDiv - covered;
            const freshDivs = (this.analysis()?.divisionCoverage ?? [])
                .filter(d => d.matchStrategy === 'no-match')
                .map(d => d.divName);
            pitch += `We found proven patterns for <strong>${covered} of ${totalDiv}</strong> divisions. `;
            if (freshDivs.length > 0) {
                pitch += `The remaining <strong>${fresh}</strong> (${freshDivs.join(', ')}) will be built fresh — give those a quick look after.`;
            } else {
                pitch += `The remaining <strong>${fresh}</strong> will be built fresh — give those a quick look after.`;
            }
        }

        pitch += `<br><br><span class="pitch-closer">`;
        pitch += `This isn't generating a schedule from scratch — it's <strong>replaying a schedule that already worked</strong>, `;
        pitch += `mapped onto your current fields and timeslots.</span>`;

        this.addMessage('pitch', pitch);
    }

    // ══════════════════════════════════════════════════════
    // Step 3: Execute Build
    // ══════════════════════════════════════════════════════

    executeBuild(): void {
        const source = this.selectedSourceJob();
        if (!source) return;

        const request: AutoBuildRequest = {
            sourceJobId: source.jobId,
            skipDivisionIds: [],
            includeBracketGames: this.includeBracketGames(),
            skipAlreadyScheduled: this.skipAlreadyScheduled(),
            agegroupMappings: this.confirmedMappings().length > 0
                ? this.confirmedMappings()
                : undefined,
        };

        this.step.set('building');
        this.addMessage('info', 'Building schedule...');
        this.addMessage('thinking', 'Placing games division by division...');

        this.svc.execute(request).subscribe({
            next: (result) => {
                this.removeThinking();
                this.buildResult.set(result);
                this.step.set('results');
                this.presentResults(result);
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Build failed: ${err.error?.message ?? 'Unknown error'}. No games were placed.`);
                this.step.set('error');
            }
        });
    }

    private presentResults(result: AutoBuildResult): void {
        const failedGames = result.gamesFailedToPlace;
        const sourceGames = this.analysis()?.sourceTotalGames ?? 0;

        // ── Hero declaration ──
        let hero = failedGames === 0
            ? `<strong>Schedule Built Successfully</strong><br><br>`
            : `<strong>Schedule Built with Warnings</strong><br><br>`;

        hero += `<span class="db db-game">${result.totalGamesPlaced}</span> games placed across `;
        hero += `<span class="db db-div">${result.divisionsScheduled}</span> divisions`;
        if (result.divisionsSkipped > 0) {
            hero += ` (<span class="db db-div">${result.divisionsSkipped}</span> skipped)`;
        }
        hero += `.`;

        // ── Year-over-year comparison ──
        if (sourceGames > 0) {
            hero += `<br><br>`;
            if (result.totalGamesPlaced === sourceGames) {
                hero += `<strong>Year-over-year:</strong> Last year had <span class="db db-game">${sourceGames}</span> games — this year matches exactly. Same structure, your current fields and timeslots.`;
            } else {
                const diff = result.totalGamesPlaced - sourceGames;
                const direction = diff > 0 ? 'more' : 'fewer';
                hero += `<strong>Year-over-year:</strong> Last year had <span class="db db-game">${sourceGames}</span> games — this year has <span class="db db-game">${result.totalGamesPlaced}</span> (${Math.abs(diff)} ${direction}, likely due to division size changes).`;
            }
        }

        if (failedGames > 0) {
            hero += `<br><br><span class="db db-game">${failedGames}</span> games could not be placed — likely field/timeslot conflicts. Review these in the schedule view.`;
        }

        this.addMessage(failedGames > 0 ? 'warning' : 'success', hero);

        this.runQaValidation();
    }

    // ── QA Validation ────────────────────────────────────
    private runQaValidation(): void {
        this.addMessage('thinking', 'Running quality checks...');

        this.qaSvc.validate().subscribe({
            next: (qa) => {
                this.removeThinking();
                this.presentQaResults(qa);
            },
            error: () => {
                this.removeThinking();
                this.addMessage('warning', 'QA validation could not be run. You can review the schedule manually.');
            }
        });
    }

    private presentQaResults(qa: AutoBuildQaResult): void {
        const criticalCount = qa.fieldDoubleBookings.length
            + qa.teamDoubleBookings.length
            + qa.rankMismatches.length;
        const warningCount = qa.unscheduledTeams.length
            + qa.backToBackGames.length
            + qa.repeatedMatchups.length
            + qa.inactiveTeamsInGames.length;

        // ── Single-line verdict ──
        if (criticalCount === 0 && warningCount === 0) {
            let verdict = `<strong>Quality Check: All Clear</strong> — no double bookings, no back-to-backs, every team scheduled.`;

            // Add balance insight
            if (qa.gamesPerTeam.length > 0) {
                const counts = qa.gamesPerTeam.map(t => t.gameCount);
                const min = Math.min(...counts);
                const max = Math.max(...counts);
                verdict += min === max
                    ? ` Games per team: <span class="db db-game">${min}</span> each (balanced).`
                    : ` Games per team: <span class="db db-game">${min}–${max}</span>.`;
            }

            this.addMessage('success', verdict);
        } else {
            // Build concise issue summary — only what needs attention
            const issues: string[] = [];
            if (qa.fieldDoubleBookings.length > 0)
                issues.push(`<span class="db db-game">${qa.fieldDoubleBookings.length}</span> field double-bookings`);
            if (qa.teamDoubleBookings.length > 0)
                issues.push(`<span class="db db-game">${qa.teamDoubleBookings.length}</span> team double-bookings`);
            if (qa.rankMismatches.length > 0)
                issues.push(`<span class="db db-game">${qa.rankMismatches.length}</span> rank mismatches`);
            if (qa.unscheduledTeams.length > 0)
                issues.push(`<span class="db db-team">${qa.unscheduledTeams.length}</span> unscheduled teams`);
            if (qa.backToBackGames.length > 0)
                issues.push(`<span class="db db-game">${qa.backToBackGames.length}</span> back-to-back games`);
            if (qa.repeatedMatchups.length > 0)
                issues.push(`<span class="db db-game">${qa.repeatedMatchups.length}</span> repeated matchups`);

            const verdict = `<strong>Quality Check: ${criticalCount > 0 ? 'Issues Found' : 'Needs Review'}</strong> — ${issues.join(', ')}. See full details in QA Results.`;
            this.addMessage(criticalCount > 0 ? 'error' : 'warning', verdict);
        }
    }

    // ── Undo ─────────────────────────────────────────────
    undoBuild(): void {
        const placed = this.buildResult()?.totalGamesPlaced ?? 0;
        this.showDestructiveConfirm(
            'Undo Build',
            `<p>Remove ALL <strong>${placed}</strong> games just placed by auto-build?</p>`,
            () => {
                this.isUndoing.set(true);
                this.addMessage('thinking', 'Removing all scheduled games...');
                this.svc.undo().subscribe({
                    next: (result) => {
                        this.removeThinking();
                        this.isUndoing.set(false);
                        this.addMessage('success', `Undo complete — ${result.gamesDeleted} games removed.`);
                        this.resetAndReload();
                    },
                    error: (err) => {
                        this.removeThinking();
                        this.isUndoing.set(false);
                        this.addMessage('error',
                            err.status === 403
                                ? 'Undo blocked: only available in Development environment.'
                                : `Undo failed: ${err.error?.message ?? 'Unknown error'}`
                        );
                    }
                });
            }
        );
    }

    // ── Navigation ───────────────────────────────────────
    backToSummary(): void {
        this.resetAndReload();
    }

    backToMapping(): void {
        this.analysis.set(null);
        this.messages.set([]);
        this.step.set('mapping');
    }

    private resetAndReload(): void {
        this.messages.set([]);
        this.analysis.set(null);
        this.selectedSourceJob.set(null);
        this.sourceJobs.set([]);
        this.mappingResponse.set(null);
        this.confirmedMappings.set([]);
        this.buildResult.set(null);
        this.loadGameSummary();
    }

}
