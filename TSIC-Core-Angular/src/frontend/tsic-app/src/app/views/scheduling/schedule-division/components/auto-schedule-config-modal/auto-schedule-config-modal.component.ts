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

type ScheduleAction = 'rebuild-all' | 'rebuild-keep' | 'delete-only';

interface ActionOption {
    value: ScheduleAction;
    label: string;
    description: string;
    icon: string;
    danger: boolean;
}

const ACTION_OPTIONS: ActionOption[] = [
    {
        value: 'rebuild-all',
        label: 'Delete ALL games + rebuild from scratch',
        description: 'Every existing game in scope will be wiped and rebuilt from scratch.',
        icon: 'bi-arrow-repeat',
        danger: false
    },
    {
        value: 'rebuild-keep',
        label: 'Rebuild but KEEP existing games',
        description: 'Divisions that already have games will be left alone. Only unscheduled divisions will be built.',
        icon: 'bi-shield-check',
        danger: false
    },
    {
        value: 'delete-only',
        label: 'Delete ALL games (no rebuild)',
        description: 'Every game in scope will be permanently deleted. Nothing will be rebuilt.',
        icon: 'bi-trash3',
        danger: true
    }
];

@Component({
    selector: 'app-auto-schedule-config-modal',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent],
    templateUrl: './auto-schedule-config-modal.component.html',
    styleUrl: './auto-schedule-config-modal.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AutoScheduleConfigModalComponent {
    readonly scope = input.required<ScheduleScope>();
    readonly scopeLabel = input('');
    readonly hasGamesInScope = input(false);
    readonly gameDates = input<GameDateInfoDto[]>([]);

    readonly buildRequested = output<AutoScheduleBuildEvent>();
    readonly cancelled = output<void>();

    /** Filter out 'rebuild-keep' at division scope — it only makes sense for multi-division scopes. */
    readonly options = computed(() => {
        const scope = this.scope();
        return scope.level === 'division'
            ? ACTION_OPTIONS.filter(o => o.value !== 'rebuild-keep')
            : ACTION_OPTIONS;
    });
    readonly selectedAction = signal<ScheduleAction | null>(null);
    readonly filterDate = signal<string>('');
    readonly confirmed = signal(false);

    readonly selectedOption = computed(() =>
        ACTION_OPTIONS.find(o => o.value === this.selectedAction()) ?? null
    );

    readonly showDayPicker = computed(() =>
        this.selectedAction() === 'delete-only' && this.hasGamesInScope() && this.gameDates().length > 0
    );

    readonly actionLabel = computed(() => {
        const opt = this.selectedOption();
        return opt ? opt.label : 'Proceed';
    });

    readonly isDanger = computed(() => this.selectedOption()?.danger ?? false);
    readonly hasSelection = computed(() => this.selectedAction() !== null);

    onActionChange(value: string): void {
        this.selectedAction.set(value ? value as ScheduleAction : null);
        this.confirmed.set(false);
        if (value !== 'delete-only') this.filterDate.set('');
    }

    onFilterDateChange(value: string): void {
        this.filterDate.set(value);
        this.confirmed.set(false);
    }

    toggleConfirm(): void {
        this.confirmed.set(!this.confirmed());
    }

    onConfirm(): void {
        const action = this.selectedAction();
        this.buildRequested.emit({
            config: {
                action: action === 'delete-only' ? 'delete-only' : 'build',
                existingGameMode: action === 'rebuild-keep' ? 'keep' : 'rebuild',
                filterDate: this.filterDate() || undefined,
            },
        });
    }
}
