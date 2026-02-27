import {
    Component, ChangeDetectionStrategy, signal, computed,
    inject, ElementRef, viewChild, AfterViewChecked
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

import { AutoBuildService } from './services/auto-build.service';
import { ScheduleQaService } from '../qa-results/services/schedule-qa.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';

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

// ── Default constraint order ─────────────────────────────
const DEFAULT_CONSTRAINTS = [
    { name: 'correct-day', label: 'Correct Day', impact: 'Preserve the same games/team/day ratio', locked: false },
    { name: 'field-assignment', label: 'Field Assignment', impact: 'Keep teams playing on the same set of fields they used last year — if U10 played on Fields 1–4, they stay on Fields 1–4', locked: false },
    { name: 'placement-shape', label: 'Placement Shape', impact: 'Preserve whether rounds were spread across fields (horizontal) or stacked on fewer fields (vertical) like last year', locked: false },
    { name: 'onsite-window', label: 'On-Site Window', impact: "Match the time spread between a team's first and last game to last year's pattern — whether that was tight or spread out across the day", locked: false },
    { name: 'field-distribution', label: 'Field Distribution', impact: 'Maintain per team the balance of different fields used', locked: false },
];

interface ConstraintPriorityItem {
    name: string;
    label: string;
    impact: string;
    locked: boolean;
}

interface AgegroupOrderItem {
    agegroupId: string;
    agegroupName: string;
    agegroupColor: string | null;
    teamCount: number;
    divisionCount: number;
    included: boolean;
}

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
    imports: [CommonModule, RouterModule, ConfirmDialogComponent],
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
                this.svc.undo().subscribe({
                    next: () => {
                        this.isDeleting.set(false);
                        this.startBuild();
                    },
                    error: (err) => {
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
    constraintPriorities = signal<ConstraintPriorityItem[]>([...DEFAULT_CONSTRAINTS]);
    divisionOrderStrategy = signal<'alpha' | 'odd-first'>('alpha');
    buildResult = signal<AutoBuildV2Result | null>(null);
    sourceJobId = signal<string | null>(null);

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
        this.addMessage('thinking', 'Analyzing prior-year patterns...');

        this.svc.getSourceJobs().subscribe({
            next: (jobs) => {
                this.removeThinking();
                this.sourceJobs.set(jobs);

                if (jobs.length === 0) {
                    // No prior year — clean sheet
                    this.sourceJobId.set(null);
                    this.profiles.set([]);
                    this.profileSourceName.set('Clean Sheet');
                    this.profileSourceYear.set('');
                    this.isCleanSheet.set(true);
                    this.preparationStatus.set('ready');
                    this.transitionToOrder();
                    return;
                }

                // Auto-select the most recent source
                const source = jobs[0];
                this.selectedSourceJob.set(source);
                this.sourceJobId.set(source.jobId);

                this.svc.extractProfiles(source.jobId).subscribe({
                    next: (response) => {
                        this.removeThinking();
                        this.profiles.set(response.profiles);
                        this.profileSourceName.set(response.sourceJobName);
                        this.profileSourceYear.set(response.sourceYear);
                        this.isCleanSheet.set(false);
                        this.preparationStatus.set('ready');
                        this.transitionToOrder();
                    },
                    error: (err) => {
                        this.removeThinking();
                        this.preparationErrors.set([`Couldn't extract profiles: ${err.error?.message ?? 'Unknown error'}`]);
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

    moveConstraintUp(index: number): void {
        if (index <= 0) return;
        this.constraintPriorities.update(cp => {
            const arr = [...cp];
            [arr[index - 1], arr[index]] = [arr[index], arr[index - 1]];
            return arr;
        });
    }

    moveConstraintDown(index: number): void {
        const cp = this.constraintPriorities();
        if (index >= cp.length - 1) return;
        this.constraintPriorities.update(c => {
            const arr = [...c];
            [arr[index], arr[index + 1]] = [arr[index + 1], arr[index]];
            return arr;
        });
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
            constraintPriorities: this.constraintPriorities().map(cp => cp.name),
        };

        this.step.set('building');
        this.messages.set([]);
        this.addMessage('info', 'Building your schedule now...');
        this.addMessage('thinking', 'Scoring every available slot and placing games...');

        this.svc.executeV2(request).subscribe({
            next: (result) => {
                this.removeThinking();
                this.buildResult.set(result);
                this.step.set('results');
                this.presentResults(result);
            },
            error: (err) => {
                this.removeThinking();
                this.addMessage('error', `Build failed: ${err.error?.message ?? 'Unknown error'}`);
                this.step.set('error');
            }
        });
    }

    private presentResults(result: AutoBuildV2Result): void {
        const hasFailures = result.gamesFailedToPlace > 0;

        let hero = hasFailures
            ? `<strong>Schedule built, but with some gaps.</strong><br><br>`
            : `<strong>Schedule built successfully.</strong><br><br>`;

        hero += `<span class="db db-game">${result.totalGamesPlaced}</span> games placed across `;
        hero += `<span class="db db-div">${result.divisionsScheduled}</span> divisions`;
        if (result.divisionsSkipped > 0) {
            hero += ` (<span class="db db-div">${result.divisionsSkipped}</span> excluded)`;
        }
        hero += `.`;

        if (result.gamesFailedToPlace > 0) {
            hero += `<br><br><span class="db db-game">${result.gamesFailedToPlace}</span> games couldn't be placed — not enough open slots. See the details below.`;
        }

        this.addMessage(hasFailures ? 'warning' : 'success', hero);

        // Sacrifice summary
        if (result.sacrificeLog.length > 0) {
            let sacMsg = `<strong>Trade-offs made:</strong><br>`;
            for (const sac of result.sacrificeLog) {
                sacMsg += `&bull; <strong>${sac.constraintName}</strong> &mdash; ${sac.violationCount} time${sac.violationCount > 1 ? 's' : ''}`;
                if (sac.exampleGames.length > 0) {
                    sacMsg += ` (e.g. ${sac.exampleGames.join(', ')})`;
                }
                sacMsg += `<br>`;
            }
            this.addMessage('info', sacMsg);
        }

        // Agegroup/Division breakdown (collapsed by default)
        let divMsg = `<details><summary><strong>Agegroup/Division breakdown:</strong></summary><div style="margin-top:0.5em">`;
        for (const dr of result.divisionResults) {
            if (dr.status === 'excluded') {
                divMsg += `&mdash; ${dr.agegroupName} / ${dr.divName}: <em>excluded</em><br>`;
            } else if (dr.gamesFailed > 0) {
                divMsg += `&bull; ${dr.agegroupName} / ${dr.divName}: <strong>${dr.gamesPlaced}</strong> placed, <strong class="text-warning">${dr.gamesFailed} failed</strong><br>`;
            } else if (dr.gamesPlaced > 0) {
                divMsg += `&bull; ${dr.agegroupName} / ${dr.divName}: <strong>${dr.gamesPlaced}</strong> placed<br>`;
            } else {
                divMsg += `&bull; ${dr.agegroupName} / ${dr.divName}: <em>no games</em><br>`;
            }
        }
        divMsg += `</div></details>`;
        this.addMessage('info', divMsg);

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

        if (criticalCount === 0 && warningCount === 0) {
            let verdict = `<strong>Quality check passed.</strong> No double bookings, no back-to-backs, every team scheduled.`;

            if (qa.gamesPerTeam.length > 0) {
                const counts = qa.gamesPerTeam.map(t => t.gameCount);
                const min = Math.min(...counts);
                const max = Math.max(...counts);
                verdict += min === max
                    ? ` Games per team: <span class="db db-game">${min}</span> each (balanced).`
                    : ` Games per team: <span class="db db-game">${min}&ndash;${max}</span>.`;
            }

            this.addMessage('success', verdict);
        } else {
            const issues: string[] = [];
            if (qa.fieldDoubleBookings.length > 0)
                issues.push(`<strong>${qa.fieldDoubleBookings.length}</strong> field double-booking${qa.fieldDoubleBookings.length > 1 ? 's' : ''}`);
            if (qa.teamDoubleBookings.length > 0)
                issues.push(`<strong>${qa.teamDoubleBookings.length}</strong> team double-booking${qa.teamDoubleBookings.length > 1 ? 's' : ''}`);
            if (qa.rankMismatches.length > 0)
                issues.push(`<strong>${qa.rankMismatches.length}</strong> rank mismatch${qa.rankMismatches.length > 1 ? 'es' : ''}`);
            if (qa.unscheduledTeams.length > 0)
                issues.push(`<strong>${qa.unscheduledTeams.length}</strong> unscheduled team${qa.unscheduledTeams.length > 1 ? 's' : ''}`);
            if (qa.backToBackGames.length > 0)
                issues.push(`<strong>${qa.backToBackGames.length}</strong> back-to-back game${qa.backToBackGames.length > 1 ? 's' : ''}`);
            if (qa.repeatedMatchups.length > 0)
                issues.push(`<strong>${qa.repeatedMatchups.length}</strong> repeated matchup${qa.repeatedMatchups.length > 1 ? 's' : ''}`);

            const verdict = criticalCount > 0
                ? `<strong>Quality check found issues:</strong> ${issues.join(', ')}. See QA Results for full details.`
                : `<strong>Quality check — worth a look:</strong> ${issues.join(', ')}. See QA Results for details.`;
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
                        this.addMessage('success', `Done — ${result.gamesDeleted} games removed.`);
                        this.resetAndReload();
                    },
                    error: (err) => {
                        this.removeThinking();
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
        this.preparationFailed.set(false);
        this.preparationStatus.set('pending');
        this.preparationErrors.set([]);
        this.isCleanSheet.set(false);
        this.loadGameSummary();
    }

}
