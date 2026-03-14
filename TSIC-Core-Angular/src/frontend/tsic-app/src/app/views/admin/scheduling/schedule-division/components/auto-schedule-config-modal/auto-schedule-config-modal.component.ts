import { Component, ChangeDetectionStrategy, input, output, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { ScheduleScope } from '../../../shared/utils/scheduling-helpers';
import type { GameDateInfoDto } from '../../services/schedule-division.service';

/** Build-time config emitted when the user confirms. */
export interface AutoScheduleConfig {
    action: 'build' | 'delete-only';
    existingGameMode: 'rebuild' | 'keep';
    filterDate?: string;
}

/** Emitted when the user clicks the action button. */
export interface AutoScheduleBuildEvent {
    config: AutoScheduleConfig;
}

@Component({
    selector: 'app-auto-schedule-config-modal',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent],
    templateUrl: './auto-schedule-config-modal.component.html',
    styleUrl: './auto-schedule-config-modal.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AutoScheduleConfigModalComponent {
    // ── Inputs ──
    readonly scope = input.required<ScheduleScope>();
    readonly scopeLabel = input('');
    /** Whether games already exist in the current scope (controls mode toggle visibility). */
    readonly hasGamesInScope = input(false);
    /** Distinct game dates with counts for the day picker. */
    readonly gameDates = input<GameDateInfoDto[]>([]);

    // ── Outputs ──
    readonly buildRequested = output<AutoScheduleBuildEvent>();
    readonly cancelled = output<void>();

    // ── Local state ──
    readonly action = signal<'build' | 'delete-only'>('build');
    readonly existingGameMode = signal<'rebuild' | 'keep'>('rebuild');
    readonly filterDate = signal<string>('');

    /** Whether the day picker is available (delete-only + games exist + dates loaded). */
    readonly showDayPicker = computed(() =>
        this.action() === 'delete-only' && this.hasGamesInScope() && this.gameDates().length > 0
    );

    /** Action button label. */
    readonly actionLabel = computed(() =>
        this.action() === 'delete-only' ? 'Delete Games' : (this.hasGamesInScope() ? 'Re-Build Schedule' : 'Build Schedule')
    );

    /** Action button style class. */
    readonly actionBtnClass = computed(() =>
        this.action() === 'delete-only' ? 'btn-outline-danger' : 'btn-primary'
    );

    setAction(a: 'build' | 'delete-only'): void {
        this.action.set(a);
        if (a === 'build') this.filterDate.set('');
    }

    setExistingGameMode(mode: 'rebuild' | 'keep'): void {
        this.existingGameMode.set(mode);
    }

    setFilterDate(date: string): void {
        this.filterDate.set(date);
    }

    onConfirm(): void {
        this.buildRequested.emit({
            config: {
                action: this.action(),
                existingGameMode: this.existingGameMode(),
                filterDate: this.filterDate() || undefined,
            },
        });
    }
}
