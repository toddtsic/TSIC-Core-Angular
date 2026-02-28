import {
    Component, ChangeDetectionStrategy, signal, computed,
    inject, ElementRef, viewChild, AfterViewChecked
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

import { AutoBuildService } from './services/auto-build.service';
import { ScheduleQaService } from '../qa-results/services/schedule-qa.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

import type {
    AutoBuildSourceJobDto,
    AutoBuildV2Request,
    AutoBuildV2Result,
    AutoBuildQaResult,
    GameSummaryResponse,
    ScheduleGameSummaryDto,
    PrerequisiteCheckResponse,
    ProfileExtractionResponse,
    DivisionSizeProfile,
    UnplacedGameDto,
    ConstraintSacrificeDto,
    PreFlightDisconnect,
    DivisionStrategyEntry,
    DivisionStrategyProfileResponse,
} from '@core/api';

// ── Step enum (sequential flow) ──────────────────────────
type AutoBuildStep =
    | 'loading'          // Fetching game summary
    | 'summary'          // Show current game status
    | 'preparing'        // Automated: prerequisites + source + profiles
    | 'order'            // Interactive: processing order + constraint priorities
    | 'building'         // Building schedule
    | 'results'          // Results + unplaced + sacrifices
    | 'error';

interface AgegroupOrderItem {
    agegroupId: string;
    agegroupName: string;
    agegroupColor: string | null;
    teamCount: number;
    divisionCount: number;
    included: boolean;
}

// ── Constraint display labels ─────────────────────────────
const CONSTRAINT_LABELS: Record<string, string> = {
    'placement-shape': 'Game day pattern',
    'field-distribution': 'Field variety',
    'team-span': 'Time between games',
    'wrong-day': 'Day assignment',
    'team-gap': 'Rest between games',
    'target-time': 'Preferred time slot',
    'round-layout': 'Game day pattern',
    'field-balance': 'Field variety',
};

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
    imports: [CommonModule, RouterModule, ConfirmDialogComponent, TsicDialogComponent],
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

    // Game summary
    gameSummary = signal<GameSummaryResponse | null>(null);
    isDeleting = signal(false);

    // Confirm dialog state (shared by Delete All + Undo All)
    showConfirmDialog = signal(false);
    confirmDialogTitle = signal('');
    confirmDialogMessage = signal('');
    confirmDialogAction = signal<(() => void) | null>(null);

    // Source jobs
    sourceJobs = signal<AutoBuildSourceJobDto[]>([]);
    selectedSourceJob = signal<AutoBuildSourceJobDto | null>(null);

    // Build
    isUndoing = signal(false);

    // Operation modal (shared by build / delete / undo)
    showOperationModal = signal(false);
    operationTitle = signal('');
    operationSubtitle = signal('');
    operationIcon = signal('bi-lightning-charge-fill');

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

    // ── Agegroup color helpers ────────────────────────────
    agBadgeStyle(hexColor: string | null | undefined): Record<string, string> {
        if (!hexColor) return {};
        const rgb = this.hexToRgb(hexColor);
        if (!rgb) return {};
        const luminance = (0.299 * rgb.r + 0.587 * rgb.g + 0.114 * rgb.b) / 255;
        return {
            background: `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, 0.15)`,
            color: hexColor,
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
    // Game Summary
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
                    jobName: '',
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
                this.openOperationModal(
                    'Clearing the Schedule',
                    `Removing ${total} games before rebuilding`,
                    'bi-trash3'
                );
                this.svc.undo().subscribe({
                    next: () => {
                        this.showOperationModal.set(false);
                        this.isDeleting.set(false);
                        this.startBuild();
                    },
                    error: (err) => {
                        this.showOperationModal.set(false);
                        this.isDeleting.set(false);
                        this.addMessage('error',
                            `Delete failed: ${err.error?.message ?? 'Unknown error'}`
                        );
                        this.step.set('summary');
                    }
                });
            }
        );
    }

    /** Shared confirm dialog for all destructive operations */
    private showDestructiveConfirm(title: string, bodyHtml: string, action: () => void): void {
        this.confirmDialogTitle.set(title);
        this.confirmDialogMessage.set(
            bodyHtml +
            `<p class="text-warning mb-0"><strong>This action cannot be undone.</strong> Make sure you want to proceed.</p>`
        );
        this.confirmDialogAction.set(action);
        this.showConfirmDialog.set(true);
    }

    private openOperationModal(title: string, subtitle: string, icon: string): void {
        this.operationTitle.set(title);
        this.operationSubtitle.set(subtitle);
        this.operationIcon.set(icon);
        this.showOperationModal.set(true);
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
    // Auto-Build Flow
    // ══════════════════════════════════════════════════════

    // ── State ─────────────────────────────────────────────
    prerequisites = signal<PrerequisiteCheckResponse | null>(null);
    preparationFailed = signal(false);
    preparationStatus = signal<'pending' | 'ready' | 'failed'>('pending');
    preparationErrors = signal<string[]>([]);
    isCleanSheet = signal(false);
    profiles = signal<DivisionSizeProfile[]>([]);
    profileSourceName = signal('');
    profileSourceYear = signal('');
    agegroupOrder = signal<AgegroupOrderItem[]>([]);
    disconnects = signal<PreFlightDisconnect[]>([]);
    divisionOrderStrategy = signal<'alpha' | 'odd-first'>('alpha');
    buildResult = signal<AutoBuildV2Result | null>(null);
    sourceJobId = signal<string | null>(null);

    // Strategy profiles
    strategyProfiles = signal<DivisionStrategyEntry[]>([]);
    strategySource = signal<string>('defaults');
    strategySourceJobName = signal<string>('');

    // Build timing (client-side)
    buildStartTime = signal<number>(0);
    buildElapsedMs = signal<number>(0);

    // QA result (inline on results page)
    qaResult = signal<AutoBuildQaResult | null>(null);
    qaLoading = signal(false);

    // Computed: excluded division IDs (from agegroupOrder items with included=false)
    excludedDivisionIds = computed(() => {
        const excluded: string[] = [];
        for (const ag of this.agegroupOrder()) {
            if (!ag.included) {
                const gs = this.gameSummary();
                if (gs) {
                    for (const div of gs.divisions) {
                        if (div.agegroupId === ag.agegroupId) {
                            excluded.push(div.divId);
                        }
                    }
                }
            }
        }
        return excluded;
    });

    includedAgegroupCount = computed(() =>
        this.agegroupOrder().filter(a => a.included).length
    );

    unplacedGames = computed(() => this.buildResult()?.unplacedGames ?? []);
    sacrificeLog = computed(() => this.buildResult()?.sacrificeLog ?? []);

    // Result verdict based on build outcome + QA
    resultStatus = computed<'success' | 'warning' | 'error'>(() => {
        const r = this.buildResult();
        if (!r) return 'success';
        const qa = this.qaResult();
        const criticalCount = qa
            ? (qa.fieldDoubleBookings?.length ?? 0)
            + (qa.teamDoubleBookings?.length ?? 0)
            + (qa.rankMismatches?.length ?? 0)
            : 0;
        if (criticalCount > 0) return 'error';
        if (r.gamesFailedToPlace > 0) return 'warning';
        return 'success';
    });

    // Games-per-team range from QA
    gamesPerTeamRange = computed(() => {
        const qa = this.qaResult();
        if (!qa?.gamesPerTeam?.length) return null;
        const counts = qa.gamesPerTeam.map(t => t.gameCount);
        return { min: Math.min(...counts), max: Math.max(...counts) };
    });

    // Avg daily time per team (minutes) from QA GameSpreads
    avgDailyTimePerTeam = computed(() => {
        const qa = this.qaResult();
        if (!qa?.gameSpreads?.length) return null;
        const total = qa.gameSpreads.reduce((sum, s) => sum + s.spreadMinutes, 0);
        return Math.round(total / qa.gameSpreads.length);
    });

    // QA issue counts
    qaCriticalCount = computed(() => {
        const qa = this.qaResult();
        if (!qa) return 0;
        return (qa.fieldDoubleBookings?.length ?? 0)
            + (qa.teamDoubleBookings?.length ?? 0)
            + (qa.rankMismatches?.length ?? 0);
    });

    qaWarningCount = computed(() => {
        const qa = this.qaResult();
        if (!qa) return 0;
        return (qa.unscheduledTeams?.length ?? 0)
            + (qa.backToBackGames?.length ?? 0)
            + (qa.repeatedMatchups?.length ?? 0)
            + (qa.inactiveTeamsInGames?.length ?? 0);
    });

    // ── Automated preparation: prerequisites → source → profiles → order ──

    startBuild(): void {
        this.step.set('preparing');
        this.preparationFailed.set(false);
        this.preparationStatus.set('pending');
        this.preparationErrors.set([]);
        this.isCleanSheet.set(false);
        this.messages.set([]);
        this.addMessage('thinking', 'Checking your setup...');

        this.svc.checkPrerequisites().subscribe({
            next: (result) => {
                this.removeThinking();
                this.prerequisites.set(result);

                if (!result.allPassed) {
                    const errors: string[] = [];
                    if (!result.poolsAssigned) {
                        errors.push(`${result.unassignedTeamCount} teams haven't been assigned to a pool yet`);
                    }
                    if (!result.pairingsCreated) {
                        errors.push(`Pairings are missing for team count${result.missingPairingTCnts.length > 1 ? 's' : ''}: ${result.missingPairingTCnts.join(', ')}`);
                    }
                    if (!result.timeslotsConfigured) {
                        errors.push(`Timeslots not configured for: ${result.agegroupsMissingTimeslots.join(', ')}`);
                    }
                    this.preparationErrors.set(errors);
                    this.preparationStatus.set('failed');
                    this.preparationFailed.set(true);
                    return;
                }

                // Prerequisites passed — load source and profiles
                this.loadSourceAndProfiles();
            },
            error: (err) => {
                this.removeThinking();
                this.preparationErrors.set([err.error?.message ?? 'Unknown error']);
                this.preparationStatus.set('failed');
                this.preparationFailed.set(true);
            }
        });
    }

    private loadSourceAndProfiles(): void {
        this.addMessage('thinking', 'Loading scheduling preferences...');

        // First check for source jobs (still needed for inference path)
        this.svc.getSourceJobs().subscribe({
            next: (jobs) => {
                this.sourceJobs.set(jobs);
                const source = jobs.length > 0 ? jobs[0] : null;
                if (source) {
                    this.selectedSourceJob.set(source);
                    this.sourceJobId.set(source.jobId);
                } else {
                    this.sourceJobId.set(null);
                }

                // Load strategy profiles (three-layer: saved → inferred → defaults)
                this.svc.getStrategyProfiles(source?.jobId).subscribe({
                    next: (response) => {
                        this.removeThinking();
                        this.strategyProfiles.set(response.strategies.map(s => ({ ...s })));
                        this.strategySource.set(response.source);
                        this.strategySourceJobName.set(response.inferredFromJobName ?? '');
                        this.isCleanSheet.set(response.source === 'defaults' && jobs.length === 0);
                        this.preparationStatus.set('ready');
                        this.transitionToOrder();
                    },
                    error: (err) => {
                        this.removeThinking();
                        this.preparationErrors.set([`Couldn't load strategy profiles: ${err.error?.message ?? 'Unknown error'}`]);
                        this.preparationStatus.set('failed');
                        this.preparationFailed.set(true);
                    }
                });
            },
            error: (err) => {
                this.removeThinking();
                this.preparationErrors.set([`Couldn't load source jobs: ${err.error?.message ?? 'Unknown error'}`]);
                this.preparationStatus.set('failed');
                this.preparationFailed.set(true);
            }
        });
    }

    // ── Processing Order + Priorities ────────────────────────
    private transitionToOrder(): void {
        const gs = this.gameSummary();
        if (gs) {
            const agMap = new Map<string, AgegroupOrderItem>();
            for (const div of gs.divisions) {
                let item = agMap.get(div.agegroupId);
                if (!item) {
                    item = {
                        agegroupId: div.agegroupId,
                        agegroupName: div.agegroupName,
                        agegroupColor: div.agegroupColor ?? null,
                        teamCount: 0,
                        divisionCount: 0,
                        included: true,
                    };
                    agMap.set(div.agegroupId, item);
                }
                item.divisionCount++;
                item.teamCount += div.teamCount;
            }
            this.agegroupOrder.set(Array.from(agMap.values()));
        }
        this.step.set('order');
    }

    moveAgegroupUp(index: number): void {
        if (index <= 0) return;
        this.agegroupOrder.update(order => {
            const arr = [...order];
            [arr[index - 1], arr[index]] = [arr[index], arr[index - 1]];
            return arr;
        });
    }

    moveAgegroupDown(index: number): void {
        const order = this.agegroupOrder();
        if (index >= order.length - 1) return;
        this.agegroupOrder.update(o => {
            const arr = [...o];
            [arr[index], arr[index + 1]] = [arr[index + 1], arr[index]];
            return arr;
        });
    }

    toggleAgegroupInclude(index: number): void {
        this.agegroupOrder.update(order =>
            order.map((item, i) => i === index ? { ...item, included: !item.included } : item)
        );
    }

    // ── Strategy Grid ──────────────────────────────────────────

    togglePlacement(divisionName: string): void {
        this.strategyProfiles.update(profiles =>
            profiles.map(p =>
                p.divisionName === divisionName
                    ? { ...p, placement: p.placement === 0 ? 1 : 0 }
                    : p
            )
        );
    }

    cycleGapPattern(divisionName: string): void {
        this.strategyProfiles.update(profiles =>
            profiles.map(p =>
                p.divisionName === divisionName
                    ? { ...p, gapPattern: (p.gapPattern + 1) % 3 }
                    : p
            )
        );
    }

    placementLabel(placement: number): string {
        return placement === 1 ? 'Vertical' : 'Horizontal';
    }

    gapPatternLabel(gapPattern: number): string {
        switch (gapPattern) {
            case 0: return 'Back-to-back';
            case 1: return 'One on, one off';
            case 2: return 'One on, two off';
            default: return 'Unknown';
        }
    }

    strategySourceLabel(): string {
        switch (this.strategySource()) {
            case 'saved': return 'Saved from last year';
            case 'inferred': return `Based on ${this.strategySourceJobName() || 'prior year'}`;
            case 'defaults': return 'Default settings';
            default: return '';
        }
    }

    // ── Execute Build ────────────────────────────────────────
    executeBuild(): void {
        const request: AutoBuildV2Request = {
            sourceJobId: this.sourceJobId() ?? undefined,
            agegroupOrder: this.agegroupOrder()
                .filter(ag => ag.included)
                .map(ag => ag.agegroupId),
            divisionOrderStrategy: this.divisionOrderStrategy(),
            excludedDivisionIds: this.excludedDivisionIds(),
            divisionStrategies: this.strategyProfiles(),
            saveProfiles: true,
        };

        this.step.set('building');
        this.messages.set([]);
        this.qaResult.set(null);
        this.qaLoading.set(false);
        this.buildStartTime.set(Date.now());
        this.openOperationModal(
            'Building Your Schedule',
            'Reproducing last year\'s patterns and finding the best slot for every game',
            'bi-lightning-charge-fill'
        );

        this.svc.executeV2(request).subscribe({
            next: (result) => {
                this.showOperationModal.set(false);
                this.buildElapsedMs.set(Date.now() - this.buildStartTime());
                this.buildResult.set(result);
                this.step.set('results');
                this.runQaValidation();
            },
            error: (err) => {
                this.showOperationModal.set(false);
                this.addMessage('error', `Build failed: ${err.error?.message ?? 'Unknown error'}`);
                this.step.set('error');
            }
        });
    }

    // ── QA Validation ────────────────────────────────────
    private runQaValidation(): void {
        this.qaLoading.set(true);

        this.qaSvc.validate().subscribe({
            next: (qa) => {
                this.qaResult.set(qa);
                this.qaLoading.set(false);
            },
            error: () => {
                this.qaLoading.set(false);
            }
        });
    }

    constraintLabel(name: string): string {
        return CONSTRAINT_LABELS[name] ?? name;
    }

    cleanExample(example: string): string {
        return example.replace(/\s*\(\+[\d.]+\)$/, '');
    }

    formatBuildTime(): string {
        const ms = this.buildElapsedMs();
        if (ms < 1000) return `${ms}ms`;
        return `${(ms / 1000).toFixed(1)}s`;
    }

    printUnplacedGames(): void {
        document.body.classList.add('printing-unplaced');
        setTimeout(() => {
            window.print();
            document.body.classList.remove('printing-unplaced');
        });
    }

    // ── Undo ─────────────────────────────────────────────
    undoBuild(): void {
        const placed = this.buildResult()?.totalGamesPlaced ?? 0;
        this.showDestructiveConfirm(
            'Undo Build',
            `<p>Remove ALL <strong>${placed}</strong> games just placed by auto-build?</p>`,
            () => {
                this.isUndoing.set(true);
                this.openOperationModal(
                    'Removing Games',
                    `Undoing ${placed} games placed by auto-build`,
                    'bi-arrow-counterclockwise'
                );
                this.svc.undo().subscribe({
                    next: (result) => {
                        this.showOperationModal.set(false);
                        this.isUndoing.set(false);
                        this.addMessage('success', `Done — ${result.gamesDeleted} games removed.`);
                        this.resetAndReload();
                    },
                    error: (err) => {
                        this.showOperationModal.set(false);
                        this.isUndoing.set(false);
                        this.addMessage('error',
                            `Undo failed: ${err.error?.message ?? 'Unknown error'}`
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
        this.selectedSourceJob.set(null);
        this.sourceJobs.set([]);
        this.buildResult.set(null);
        this.qaResult.set(null);
        this.qaLoading.set(false);
        this.buildElapsedMs.set(0);
        this.preparationFailed.set(false);
        this.preparationStatus.set('pending');
        this.preparationErrors.set([]);
        this.isCleanSheet.set(false);
        this.strategyProfiles.set([]);
        this.strategySource.set('defaults');
        this.strategySourceJobName.set('');
        this.loadGameSummary();
    }

}
