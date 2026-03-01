import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import type { AutoBuildResult, AutoBuildQaResult } from '@core/api';

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

@Component({
    selector: 'app-build-results-panel',
    standalone: true,
    imports: [CommonModule, RouterModule],
    templateUrl: './build-results-panel.component.html',
    styleUrl: './build-results-panel.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BuildResultsPanelComponent {

    // ── Inputs ──
    readonly buildResult = input.required<AutoBuildResult>();
    readonly qaResult = input<AutoBuildQaResult | null>(null);
    readonly qaLoading = input(false);
    readonly buildElapsedMs = input(0);

    // ── Outputs ──
    readonly dismissed = output<void>();
    readonly undoRequested = output<void>();
    readonly runAgainRequested = output<void>();

    // ── Computed signals ──

    readonly unplacedGames = computed(() => this.buildResult().unplacedGames ?? []);
    readonly sacrificeLog = computed(() => this.buildResult().sacrificeLog ?? []);

    readonly resultStatus = computed<'success' | 'warning' | 'error'>(() => {
        const r = this.buildResult();
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

    readonly gamesPerTeamRange = computed(() => {
        const qa = this.qaResult();
        if (!qa?.gamesPerTeam?.length) return null;
        const counts = qa.gamesPerTeam.map(t => t.gameCount);
        return { min: Math.min(...counts), max: Math.max(...counts) };
    });

    readonly avgDailyTimePerTeam = computed(() => {
        const qa = this.qaResult();
        if (!qa?.gameSpreads?.length) return null;
        const total = qa.gameSpreads.reduce((sum, s) => sum + s.spreadMinutes, 0);
        return Math.round(total / qa.gameSpreads.length);
    });

    readonly qaCriticalCount = computed(() => {
        const qa = this.qaResult();
        if (!qa) return 0;
        return (qa.fieldDoubleBookings?.length ?? 0)
            + (qa.teamDoubleBookings?.length ?? 0)
            + (qa.rankMismatches?.length ?? 0);
    });

    readonly qaWarningCount = computed(() => {
        const qa = this.qaResult();
        if (!qa) return 0;
        return (qa.unscheduledTeams?.length ?? 0)
            + (qa.backToBackGames?.length ?? 0)
            + (qa.repeatedMatchups?.length ?? 0)
            + (qa.inactiveTeamsInGames?.length ?? 0);
    });

    // ── Helpers ──

    constraintLabel(name: string): string {
        return CONSTRAINT_LABELS[name] ?? name;
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
}
