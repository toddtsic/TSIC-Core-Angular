import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ScheduleQaService } from './services/schedule-qa.service';
import type { AutoBuildQaResult } from '@core/api';

@Component({
    selector: 'app-qa-results',
    standalone: true,
    imports: [CommonModule, RouterLink],
    templateUrl: './qa-results.component.html',
    styleUrl: './qa-results.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class QaResultsComponent {
    private readonly qaService = inject(ScheduleQaService);

    qaResult = signal<AutoBuildQaResult | null>(null);
    isLoading = signal(false);
    errorMessage = signal('');
    expandedSections = signal<Set<string>>(new Set());

    overallSeverity = computed<'success' | 'warning' | 'error' | 'empty'>(() => {
        const qa = this.qaResult();
        if (!qa) return 'empty';
        if (qa.fieldDoubleBookings.length > 0 || qa.teamDoubleBookings.length > 0
            || qa.rankMismatches.length > 0) return 'error';
        if (qa.unscheduledTeams.length > 0 || qa.backToBackGames.length > 0
            || qa.repeatedMatchups.length > 0 || qa.inactiveTeamsInGames.length > 0) return 'warning';
        return 'success';
    });

    totalIssues = computed(() => {
        const qa = this.qaResult();
        if (!qa) return 0;
        return qa.fieldDoubleBookings.length
            + qa.teamDoubleBookings.length
            + qa.rankMismatches.length
            + qa.unscheduledTeams.length
            + qa.backToBackGames.length
            + qa.repeatedMatchups.length
            + qa.inactiveTeamsInGames.length;
    });

    gameSpreadSummary = computed(() => {
        const qa = this.qaResult();
        if (!qa || qa.gameSpreads.length === 0) return null;
        const spreads = qa.gameSpreads.map(s => s.spreadMinutes);
        return {
            avg: Math.round(spreads.reduce((a, b) => a + b, 0) / spreads.length),
            max: Math.max(...spreads),
            min: Math.min(...spreads)
        };
    });

    gamesPerTeamSummary = computed(() => {
        const qa = this.qaResult();
        if (!qa || qa.gamesPerTeam.length === 0) return null;
        const counts = qa.gamesPerTeam.map(t => t.gameCount);
        return {
            min: Math.min(...counts),
            max: Math.max(...counts),
            balanced: Math.min(...counts) === Math.max(...counts)
        };
    });

    constructor() {
        this.runValidation();
    }

    runValidation(): void {
        this.isLoading.set(true);
        this.errorMessage.set('');
        this.qaResult.set(null);

        this.qaService.validate().subscribe({
            next: (result) => {
                this.qaResult.set(result);
                this.isLoading.set(false);
            },
            error: (err) => {
                this.isLoading.set(false);
                this.errorMessage.set(
                    err?.error?.message || 'Failed to run QA validation. Ensure games have been scheduled.'
                );
            }
        });
    }

    toggleSection(key: string): void {
        const current = new Set(this.expandedSections());
        if (current.has(key)) {
            current.delete(key);
        } else {
            current.add(key);
        }
        this.expandedSections.set(current);
    }

    isSectionExpanded(key: string): boolean {
        return this.expandedSections().has(key);
    }

    formatDateTime(iso: string): string {
        const d = new Date(iso);
        return d.toLocaleDateString('en-US', {
            weekday: 'short', month: 'short', day: 'numeric',
            hour: 'numeric', minute: '2-digit'
        });
    }
}
