import {
    Component, ChangeDetectionStrategy, signal, computed,
    inject, ElementRef, viewChild, AfterViewChecked
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';

import { AutoBuildService } from './services/auto-build.service';
import { ScheduleQaService } from '../qa-results/services/schedule-qa.service';

import type { AutoBuildSourceJobDto } from '@core/api';
import type { AutoBuildAnalysisResponse } from '@core/api';
import type { AutoBuildResult } from '@core/api';
import type { AutoBuildRequest } from '@core/api';
import type { DivisionMatch } from '@core/api';
import type { SizeMismatchResolution } from '@core/api';
import type { AutoBuildQaResult } from '@core/api';

// ── Phase enum (matches conversation flow) ────────────────────────
type AutoBuildPhase =
    | 'idle'
    | 'source-selection'
    | 'analyzing'
    | 'feasibility'
    | 'questions'
    | 'approval'
    | 'building'
    | 'results'
    | 'error';

// ── Agent message model ───────────────────────────────────────────
type AgentMessageType = 'thinking' | 'info' | 'success' | 'warning' | 'question' | 'error' | 'result';

interface AgentMessage {
    id: string;
    type: AgentMessageType;
    content: string;
    timestamp: Date;
}

// ── DivisionMatchType enum values (from C# enum) ─────────────────
const MatchType = {
    ExactMatch: 0,
    SizeMismatch: 1,
    NewDivision: 2,
    RemovedDivision: 3
} as const;

@Component({
    selector: 'app-auto-build',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterModule],
    templateUrl: './auto-build.component.html',
    styleUrl: './auto-build.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AutoBuildComponent implements AfterViewChecked {
    private readonly svc = inject(AutoBuildService);
    private readonly qaSvc = inject(ScheduleQaService);

    // ── Scroll anchor ─────────────────────────────────────────────
    private readonly scrollContainer = viewChild<ElementRef<HTMLElement>>('scrollContainer');
    private shouldScroll = false;

    // ── State ─────────────────────────────────────────────────────
    phase = signal<AutoBuildPhase>('idle');
    messages = signal<AgentMessage[]>([]);

    // Phase 1: Source selection
    sourceJobs = signal<AutoBuildSourceJobDto[]>([]);
    selectedSourceJobId = signal<string | null>(null);

    // Phase 2-3: Analysis
    analysis = signal<AutoBuildAnalysisResponse | null>(null);

    // Phase 4: User answers for mismatch divisions
    mismatchStrategies = signal<Map<string, string>>(new Map());
    skipDivisionIds = signal<Set<string>>(new Set());

    // Phase 6: Build options
    includeBracketGames = signal(false);
    skipAlreadyScheduled = signal(true);

    // Phase 7-8: Build
    buildResult = signal<AutoBuildResult | null>(null);
    isUndoing = signal(false);

    // ── Computed helpers ──────────────────────────────────────────
    selectedSourceJob = computed(() => {
        const id = this.selectedSourceJobId();
        return this.sourceJobs().find(j => j.jobId === id) ?? null;
    });

    exactMatches = computed(() =>
        (this.analysis()?.divisionMatches ?? []).filter(d => d.matchType === MatchType.ExactMatch)
    );

    sizeMismatches = computed(() =>
        (this.analysis()?.divisionMatches ?? []).filter(d => d.matchType === MatchType.SizeMismatch)
    );

    newDivisions = computed(() =>
        (this.analysis()?.divisionMatches ?? []).filter(d => d.matchType === MatchType.NewDivision)
    );

    removedDivisions = computed(() =>
        (this.analysis()?.divisionMatches ?? []).filter(d => d.matchType === MatchType.RemovedDivision)
    );

    hasMismatches = computed(() => this.sizeMismatches().length > 0);

    allMismatchesResolved = computed(() => {
        const mismatches = this.sizeMismatches();
        const strategies = this.mismatchStrategies();
        return mismatches.every(d => d.currentDivId && strategies.has(d.currentDivId));
    });

    feasibilityColor = computed(() => {
        const level = this.analysis()?.feasibility?.confidenceLevel;
        if (level === 'green') return 'var(--bs-success)';
        if (level === 'yellow') return 'var(--bs-warning)';
        return 'var(--bs-danger)';
    });

    // ── Auto-scroll ──────────────────────────────────────────────
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

    // ── Message helpers ──────────────────────────────────────────
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

    // ── Phase 1: Start ───────────────────────────────────────────
    start(): void {
        this.phase.set('source-selection');
        this.addMessage('info', 'Let me find prior-year schedules we can use as templates...');
        this.addMessage('thinking', 'Searching for source jobs...');

        this.svc.getSourceJobs().subscribe({
            next: (jobs) => {
                this.removeThinking();
                this.sourceJobs.set(jobs);

                if (jobs.length === 0) {
                    this.addMessage('warning', 'No prior-year jobs with scheduled games found for this event. You\'ll need to use manual scheduling instead.');
                    this.phase.set('error');
                    return;
                }

                // Auto-select the first (most recent, most games)
                this.selectedSourceJobId.set(jobs[0].jobId);

                const jobList = jobs.map(j =>
                    `**${j.jobName}** (${j.year ?? 'N/A'}) — ${j.scheduledGameCount} games`
                ).join('\n');

                this.addMessage('success',
                    `Found ${jobs.length} prior-year schedule${jobs.length > 1 ? 's' : ''}:\n\n${jobList}\n\nI've pre-selected the most recent one. You can change it below, then click **Analyze** to continue.`
                );
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Failed to load source jobs: ${err.error?.message ?? 'Unknown error'}`);
                this.phase.set('error');
            }
        });
    }

    // ── Phase 2: Analyze ─────────────────────────────────────────
    analyze(): void {
        const sourceId = this.selectedSourceJobId();
        if (!sourceId) return;

        const sourceName = this.selectedSourceJob()?.jobName ?? 'selected job';
        this.phase.set('analyzing');
        this.addMessage('info', `Analyzing **${sourceName}** to extract scheduling patterns...`);
        this.addMessage('thinking', 'Extracting pattern, matching divisions, checking fields...');

        this.svc.analyze(sourceId).subscribe({
            next: (result) => {
                this.removeThinking();
                this.analysis.set(result);
                this.presentFeasibility(result);
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Analysis failed: ${err.error?.message ?? 'Unknown error'}`);
                this.phase.set('error');
            }
        });
    }

    // ── Phase 3: Feasibility report ──────────────────────────────
    private presentFeasibility(result: AutoBuildAnalysisResponse): void {
        const f = result.feasibility;
        const level = f.confidenceLevel;

        let emoji = '';
        if (level === 'green') emoji = 'Excellent';
        else if (level === 'yellow') emoji = 'Good';
        else emoji = 'Limited';

        this.addMessage('info',
            `**Pattern Analysis Complete** — ${result.sourceTotalGames} games extracted from ${result.sourceYear}\n\n` +
            `**Confidence: ${f.confidencePercent}% (${emoji})**\n\n` +
            `| Category | Count |\n` +
            `|---|---|\n` +
            `| Exact matches | ${f.exactMatches} |\n` +
            `| Size mismatches | ${f.sizeMismatches} |\n` +
            `| New divisions | ${f.newDivisions} |\n` +
            `| Removed divisions | ${f.removedDivisions} |`
        );

        // Field warnings
        if (f.fieldMismatches.length > 0) {
            this.addMessage('warning',
                `**Field mismatches detected:**\n\n` +
                f.fieldMismatches.map(m => `- ${m}`).join('\n') +
                `\n\nGames on unmatched fields will use the closest available timeslot.`
            );
        }

        // General warnings
        if (f.warnings.length > 0) {
            this.addMessage('warning', f.warnings.join('\n\n'));
        }

        // Move to questions phase if there are mismatches, else straight to approval
        if (this.sizeMismatches().length > 0) {
            this.phase.set('questions');
            this.addMessage('question',
                `**${this.sizeMismatches().length} division(s)** have different team counts than last year. ` +
                `Please tell me how to handle each one below.`
            );
        } else {
            this.phase.set('approval');
            this.addMessage('success',
                `All divisions match. We're ready to build whenever you are.`
            );
        }
    }

    // ── Phase 4: Mismatch resolution ─────────────────────────────
    setMismatchStrategy(divId: string, strategy: string): void {
        this.mismatchStrategies.update(map => {
            const updated = new Map(map);
            updated.set(divId, strategy);
            return updated;
        });
    }

    getMismatchStrategy(divId: string): string {
        return this.mismatchStrategies().get(divId) ?? '';
    }

    proceedToApproval(): void {
        // Add skipped divisions
        const skipped = new Set<string>();
        for (const [divId, strategy] of this.mismatchStrategies()) {
            if (strategy === 'skip') {
                skipped.add(divId);
            }
        }
        this.skipDivisionIds.set(skipped);

        this.phase.set('approval');
        this.addMessage('success', 'All questions answered. Review the summary below and click **Build Schedule** when ready.');
    }

    // ── Phase 6: Execute build ───────────────────────────────────
    executeBuild(): void {
        const sourceId = this.selectedSourceJobId();
        if (!sourceId) return;

        // Build mismatch resolutions
        const resolutions: SizeMismatchResolution[] = [];
        for (const [divId, strategy] of this.mismatchStrategies()) {
            resolutions.push({ divId, strategy });
        }

        const request: AutoBuildRequest = {
            sourceJobId: sourceId,
            skipDivisionIds: [...this.skipDivisionIds()],
            mismatchResolutions: resolutions.length > 0 ? resolutions : undefined,
            includeBracketGames: this.includeBracketGames(),
            skipAlreadyScheduled: this.skipAlreadyScheduled()
        };

        this.phase.set('building');
        this.addMessage('info', 'Building schedule...');
        this.addMessage('thinking', 'Placing games division by division...');

        this.svc.execute(request).subscribe({
            next: (result) => {
                this.removeThinking();
                this.buildResult.set(result);
                this.phase.set('results');
                this.presentResults(result);
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Build failed: ${err.error?.message ?? 'Unknown error'}. No games were placed.`);
                this.phase.set('error');
            }
        });
    }

    // ── Phase 8: Results ─────────────────────────────────────────
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

        // Per-division breakdown
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

        // Auto-run QA validation
        this.runQaValidation();
    }

    // ── QA Validation ────────────────────────────────────────────
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
                this.addMessage('info',
                    'You can **View Schedule** to review the results, or **Undo** to remove all placed games and start over.'
                );
            }
        });
    }

    private presentQaResults(qa: AutoBuildQaResult): void {
        const critical: string[] = [];
        const warnings: string[] = [];
        const info: string[] = [];

        // ── Critical ────────────────────────────────────────────
        if (qa.fieldDoubleBookings.length > 0) {
            critical.push(`**Field double-bookings: ${qa.fieldDoubleBookings.length}** — Same field, same time has multiple games`);
        }
        if (qa.teamDoubleBookings.length > 0) {
            critical.push(`**Team double-bookings: ${qa.teamDoubleBookings.length}** — A team is scheduled for 2+ games at the same time`);
        }
        if (qa.rankMismatches.length > 0) {
            critical.push(`**Rank mismatches: ${qa.rankMismatches.length}** — Seed number in game doesn't match team's division rank`);
        }

        // ── Warnings ────────────────────────────────────────────
        if (qa.unscheduledTeams.length > 0) {
            warnings.push(`**Unscheduled teams: ${qa.unscheduledTeams.length}** — Active teams with zero games`);
        }
        if (qa.backToBackGames.length > 0) {
            warnings.push(`**Back-to-back games: ${qa.backToBackGames.length}** — Teams playing within 90 minutes of each other`);
        }
        if (qa.repeatedMatchups.length > 0) {
            warnings.push(`**Repeated matchups: ${qa.repeatedMatchups.length}** — Same two teams playing each other more than once`);
        }
        if (qa.inactiveTeamsInGames.length > 0) {
            warnings.push(`**Inactive teams in games: ${qa.inactiveTeamsInGames.length}** — Inactive/dropped teams still in scheduled games`);
        }

        // ── Informational ───────────────────────────────────────
        if (qa.gamesPerDate.length > 0) {
            const total = qa.gamesPerDate.reduce((sum, d) => sum + d.gameCount, 0);
            info.push(`**Total games**: ${total} across ${qa.gamesPerDate.length} day(s)`);
        }

        if (qa.gamesPerTeam.length > 0) {
            const counts = qa.gamesPerTeam.map(t => t.gameCount);
            const min = Math.min(...counts);
            const max = Math.max(...counts);
            if (min !== max) {
                info.push(`**Games per team**: ${min}–${max} (some imbalance)`);
            } else {
                info.push(`**Games per team**: ${min} each (perfectly balanced)`);
            }
        }

        if (qa.gameSpreads.length > 0) {
            const maxSpread = Math.max(...qa.gameSpreads.map(s => s.spreadMinutes));
            const avgSpread = Math.round(qa.gameSpreads.reduce((sum, s) => sum + s.spreadMinutes, 0) / qa.gameSpreads.length);
            info.push(`**Game spread**: avg ${avgSpread} min, max ${maxSpread} min (first-to-last per team per day)`);
        }

        if (qa.bracketGames.length > 0) {
            info.push(`**Bracket/playoff games**: ${qa.bracketGames.length} — team assignments resolve after pool play standings`);
        }

        if (qa.rrGamesPerDivision.length > 0) {
            const shortDiv = qa.rrGamesPerDivision.find(d => d.gameCount < d.poolSize * (d.poolSize - 1) / 2);
            if (shortDiv) {
                warnings.push(`**Incomplete round-robin**: Some divisions have fewer games than a full round-robin`);
            }
        }

        // ── Build the message ───────────────────────────────────
        const allIssues = [...critical, ...warnings];
        const parts: string[] = [];

        if (critical.length > 0) {
            parts.push(`**Critical Issues**\n${critical.map(i => `- ${i}`).join('\n')}`);
        }
        if (warnings.length > 0) {
            parts.push(`**Warnings**\n${warnings.map(i => `- ${i}`).join('\n')}`);
        }
        if (info.length > 0) {
            parts.push(`**Overview**\n${info.map(i => `- ${i}`).join('\n')}`);
        }

        if (allIssues.length === 0 && info.length === 0) {
            this.addMessage('success', '**QA Validation: All checks passed.** No issues detected.');
        } else if (critical.length > 0) {
            this.addMessage('error', `**QA Validation: Issues Found**\n\n${parts.join('\n\n')}`);
        } else if (warnings.length > 0) {
            this.addMessage('warning', `**QA Validation Report**\n\n${parts.join('\n\n')}`);
        } else {
            this.addMessage('success', `**QA Validation: All Checks Passed**\n\n${parts.join('\n\n')}`);
        }

        this.addMessage('info',
            'You can **View Schedule** to review the results, or **Undo** to remove all placed games and start over.'
        );
    }

    // ── Undo ─────────────────────────────────────────────────────
    undoBuild(): void {
        this.isUndoing.set(true);
        this.addMessage('thinking', 'Removing all scheduled games...');

        this.svc.undo().subscribe({
            next: (result) => {
                this.removeThinking();
                this.isUndoing.set(false);
                this.addMessage('success', `Undo complete — ${result.gamesDeleted} games removed. You can run Auto-Build again or schedule manually.`);
                this.resetState();
            },
            error: (err) => {
                this.removeThinking();
                this.isUndoing.set(false);
                this.addMessage('error', `Undo failed: ${err.error?.message ?? 'Unknown error'}`);
            }
        });
    }

    // ── Reset ────────────────────────────────────────────────────
    private resetState(): void {
        this.phase.set('idle');
        this.sourceJobs.set([]);
        this.selectedSourceJobId.set(null);
        this.analysis.set(null);
        this.mismatchStrategies.set(new Map());
        this.skipDivisionIds.set(new Set());
        this.buildResult.set(null);
        this.includeBracketGames.set(false);
        this.skipAlreadyScheduled.set(true);
    }

    // ── Restart ──────────────────────────────────────────────────
    restart(): void {
        this.messages.set([]);
        this.resetState();
    }

    // ── Template helpers ─────────────────────────────────────────
    getMatchTypeLabel(matchType: number): string {
        switch (matchType) {
            case MatchType.ExactMatch: return 'Exact Match';
            case MatchType.SizeMismatch: return 'Size Mismatch';
            case MatchType.NewDivision: return 'New Division';
            case MatchType.RemovedDivision: return 'Removed';
            default: return 'Unknown';
        }
    }

    getMatchTypeClass(matchType: number): string {
        switch (matchType) {
            case MatchType.ExactMatch: return 'match-exact';
            case MatchType.SizeMismatch: return 'match-mismatch';
            case MatchType.NewDivision: return 'match-new';
            case MatchType.RemovedDivision: return 'match-removed';
            default: return '';
        }
    }
}
