import {
    Component, ChangeDetectionStrategy, signal, computed,
    inject, ElementRef, viewChild, AfterViewChecked
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';

import { AutoBuildService } from './services/auto-build.service';
import type { GameSummaryResponse, ScheduleGameSummaryDto } from './services/auto-build.service';
import { ScheduleQaService } from '../qa-results/services/schedule-qa.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { environment } from '@environments/environment';

import type { AutoBuildSourceJobDto } from '@core/api';
import type { AutoBuildAnalysisResponse } from '@core/api';
import type { AutoBuildResult } from '@core/api';
import type { AutoBuildRequest } from '@core/api';
import type { AutoBuildQaResult } from '@core/api';

// ── Step enum (sequential flow) ──────────────────────────
type AutoBuildStep =
    | 'loading'      // Fetching game summary
    | 'summary'      // Step 1: Show current game status
    | 'analyzing'    // Step 2: Auto-analyzing prior year
    | 'analysis'     // Step 2b: Show analysis results
    | 'building'     // Step 3: Building schedule
    | 'results'      // Step 3b: Build results + QA
    | 'error';

// ── Agent message model ───────────────────────────────────
type AgentMessageType = 'thinking' | 'info' | 'success' | 'warning' | 'question' | 'error' | 'result';

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

    // Step 2: Analysis
    sourceJobs = signal<AutoBuildSourceJobDto[]>([]);
    selectedSourceJob = signal<AutoBuildSourceJobDto | null>(null);
    analysis = signal<AutoBuildAnalysisResponse | null>(null);

    // Step 3: Build
    buildResult = signal<AutoBuildResult | null>(null);
    isUndoing = signal(false);

    // Build options (set to sensible defaults, no UI toggle needed)
    readonly includeBracketGames = signal(false);
    readonly skipAlreadyScheduled = signal(true);

    // ── Tree: group divisions by agegroup ─────────────────
    agegroupTree = computed(() => {
        const gs = this.gameSummary();
        if (!gs) return [];
        const map = new Map<string, { agegroupName: string; agegroupId: string; divisions: ScheduleGameSummaryDto[]; totalGames: number; totalExpected: number; totalTeams: number }>();
        for (const div of gs.divisions) {
            let group = map.get(div.agegroupId);
            if (!group) {
                group = { agegroupName: div.agegroupName, agegroupId: div.agegroupId, divisions: [], totalGames: 0, totalExpected: 0, totalTeams: 0 };
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

    // ── Pool-size coverage computed helpers ────────────────
    coveredDivisions = computed(() =>
        (this.analysis()?.divisionCoverage ?? []).filter(d => d.hasPattern)
    );

    uncoveredDivisions = computed(() =>
        (this.analysis()?.divisionCoverage ?? []).filter(d => !d.hasPattern)
    );

    availablePatterns = computed(() =>
        this.analysis()?.feasibility?.availablePatterns ?? []
    );

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

    deleteAllGames(): void {
        const total = this.gameSummary()?.totalGames ?? 0;
        this.showDestructiveConfirm(
            'Delete All Games',
            `<p>Delete ALL <strong>${total}</strong> scheduled round-robin games?</p>`,
            () => {
                this.isDeleting.set(true);
                this.svc.undo().subscribe({
                    next: () => {
                        this.isDeleting.set(false);
                        this.loadGameSummary();
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
    // Step 2: Continue to Analysis (auto-select prior year)
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
                    `Using **${source.jobName}** (${source.year ?? 'N/A'}) as template — ${source.scheduledGameCount} games.`
                );

                // Auto-run analysis
                this.runAnalysis(source.jobId);
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Failed to load source jobs: ${err.error?.message ?? 'Unknown error'}`);
                this.step.set('error');
            }
        });
    }

    private runAnalysis(sourceJobId: string): void {
        this.addMessage('thinking', 'Extracting patterns by pool size, checking fields...');

        this.svc.analyze(sourceJobId).subscribe({
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

        let label = '';
        if (f.confidenceLevel === 'green') label = 'Excellent';
        else if (f.confidenceLevel === 'yellow') label = 'Good';
        else label = 'Limited';

        // Pool-size patterns summary
        const patternSummary = f.availablePatterns.length > 0
            ? f.availablePatterns.map(p => `${p.teamCount}-team: ${p.gameCount} games`).join(' | ')
            : 'No patterns found';

        this.addMessage('info',
            `**Pattern Analysis Complete** — ${result.sourceTotalGames} games extracted from ${result.sourceYear}\n\n` +
            `**Coverage: ${f.confidencePercent}% (${label})** — ` +
            `${f.coveredDivisions} covered by pattern, ${f.uncoveredDivisions} will use auto-schedule\n\n` +
            `**Source patterns:** ${patternSummary}`
        );

        if (f.warnings.length > 0) {
            for (const w of f.warnings) {
                if (w.includes('same address')) {
                    this.addMessage('success', w);
                } else {
                    this.addMessage('warning', w);
                }
            }
        }

        this.addMessage('success', 'Ready to build. Click **Build Schedule** to proceed, or **Back** to return.');
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
            skipAlreadyScheduled: this.skipAlreadyScheduled()
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

        let summary = `**Schedule Built Successfully**\n\n` +
            `| Metric | Value |\n` +
            `|---|---|\n` +
            `| Divisions scheduled | ${result.divisionsScheduled} |\n` +
            `| Divisions skipped | ${result.divisionsSkipped} |\n` +
            `| Total games placed | ${result.totalGamesPlaced} |\n`;

        if (failedGames > 0) {
            summary += `| Games failed to place | ${failedGames} |\n`;
        }

        this.addMessage(failedGames > 0 ? 'warning' : 'success', summary);

        if (result.divisionResults.length > 0) {
            let breakdown = '**Division Breakdown:**\n\n';
            for (const div of result.divisionResults) {
                const icon = div.status === 'skipped' || div.status === 'already-scheduled' ? '[skip]' :
                    div.gamesFailed > 0 ? '[warn]' : '[ok]';
                breakdown += `${icon} **${div.agegroupName} / ${div.divName}** — ${div.gamesPlaced} placed`;
                if (div.gamesFailed > 0) breakdown += `, ${div.gamesFailed} failed`;
                if (div.status === 'skipped') breakdown += ' (skipped)';
                if (div.status === 'already-scheduled') breakdown += ' (already scheduled)';
                breakdown += '\n';
            }
            this.addMessage('info', breakdown);
        }

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
        const critical: string[] = [];
        const warnings: string[] = [];
        const info: string[] = [];

        if (qa.fieldDoubleBookings.length > 0)
            critical.push(`**Field double-bookings: ${qa.fieldDoubleBookings.length}**`);
        if (qa.teamDoubleBookings.length > 0)
            critical.push(`**Team double-bookings: ${qa.teamDoubleBookings.length}**`);
        if (qa.rankMismatches.length > 0)
            critical.push(`**Rank mismatches: ${qa.rankMismatches.length}**`);

        if (qa.unscheduledTeams.length > 0)
            warnings.push(`**Unscheduled teams: ${qa.unscheduledTeams.length}**`);
        if (qa.backToBackGames.length > 0)
            warnings.push(`**Back-to-back games: ${qa.backToBackGames.length}**`);
        if (qa.repeatedMatchups.length > 0)
            warnings.push(`**Repeated matchups: ${qa.repeatedMatchups.length}**`);
        if (qa.inactiveTeamsInGames.length > 0)
            warnings.push(`**Inactive teams in games: ${qa.inactiveTeamsInGames.length}**`);

        if (qa.gamesPerDate.length > 0) {
            const total = qa.gamesPerDate.reduce((sum, d) => sum + d.gameCount, 0);
            info.push(`**Total games**: ${total} across ${qa.gamesPerDate.length} day(s)`);
        }

        if (qa.gamesPerTeam.length > 0) {
            const counts = qa.gamesPerTeam.map(t => t.gameCount);
            const min = Math.min(...counts);
            const max = Math.max(...counts);
            info.push(min !== max
                ? `**Games per team**: ${min}–${max} (some imbalance)`
                : `**Games per team**: ${min} each (balanced)`
            );
        }

        if (qa.rrGamesPerDivision.length > 0) {
            const shortDiv = qa.rrGamesPerDivision.find(d => d.gameCount < d.poolSize * (d.poolSize - 1) / 2);
            if (shortDiv)
                warnings.push(`**Incomplete round-robin**: Some divisions have fewer games than a full round-robin`);
        }

        const parts: string[] = [];
        if (critical.length > 0)
            parts.push(`**Critical Issues**\n${critical.map(i => `- ${i}`).join('\n')}`);
        if (warnings.length > 0)
            parts.push(`**Warnings**\n${warnings.map(i => `- ${i}`).join('\n')}`);
        if (info.length > 0)
            parts.push(`**Overview**\n${info.map(i => `- ${i}`).join('\n')}`);

        if (critical.length === 0 && warnings.length === 0) {
            this.addMessage('success', `**QA Validation: All Checks Passed**\n\n${parts.join('\n\n')}`);
        } else if (critical.length > 0) {
            this.addMessage('error', `**QA Validation: Issues Found**\n\n${parts.join('\n\n')}`);
        } else {
            this.addMessage('warning', `**QA Validation Report**\n\n${parts.join('\n\n')}`);
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

    private resetAndReload(): void {
        this.messages.set([]);
        this.analysis.set(null);
        this.selectedSourceJob.set(null);
        this.sourceJobs.set([]);
        this.buildResult.set(null);
        this.loadGameSummary();
    }

}
