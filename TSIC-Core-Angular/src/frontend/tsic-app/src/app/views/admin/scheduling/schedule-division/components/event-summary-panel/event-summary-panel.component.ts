import { Component, ChangeDetectionStrategy, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import { contrastText, agTeamCount } from '../../../shared/utils/scheduling-helpers';
import type { GameSummaryResponse } from '@core/api';

@Component({
    selector: 'app-event-summary-panel',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './event-summary-panel.component.html',
    styleUrl: './event-summary-panel.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class EventSummaryPanelComponent {
    // ── Inputs ──
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly gameSummary = input<GameSummaryResponse | null>(null);
    readonly hasGamesInScope = input(false);
    readonly scopeGameCount = input(0);
    readonly isExecutingV2 = input(false);
    readonly isDeletingGames = input(false);
    readonly showDeleteConfirm = input(false);

    // ── Outputs ──
    readonly autoScheduleRequested = output<void>();
    readonly deleteRequested = output<void>();
    readonly deleteConfirmed = output<void>();
    readonly deleteCancelled = output<void>();

    // ── Local state for delete confirmation ──
    readonly deleteConfirmText = signal('');

    // ── Helpers ──
    readonly contrastText = contrastText;
    readonly agTeamCount = agTeamCount;

    agGameCount(agegroupId: string): number {
        const summary = this.gameSummary();
        if (!summary) return 0;
        return summary.divisions
            .filter(d => d.agegroupId === agegroupId)
            .reduce((sum, d) => sum + d.gameCount, 0);
    }

    onDeleteConfirmed(): void {
        this.deleteConfirmed.emit();
        this.deleteConfirmText.set('');
    }

    onDeleteCancelled(): void {
        this.deleteCancelled.emit();
        this.deleteConfirmText.set('');
    }
}
