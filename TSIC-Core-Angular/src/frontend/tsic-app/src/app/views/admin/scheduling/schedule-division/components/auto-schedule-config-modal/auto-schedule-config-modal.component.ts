import { Component, ChangeDetectionStrategy, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { ScheduleScope } from '../../../shared/utils/scheduling-helpers';

/** Build-time config emitted when the user confirms. */
export interface AutoScheduleConfig {
    existingGameMode: 'rebuild' | 'keep';
}

/** Emitted when the user clicks "Build Schedule". */
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

    // ── Outputs ──
    readonly buildRequested = output<AutoScheduleBuildEvent>();
    readonly cancelled = output<void>();

    // ── Local state ──
    readonly localConfig = signal<AutoScheduleConfig>({ existingGameMode: 'rebuild' });

    setExistingGameMode(mode: 'rebuild' | 'keep'): void {
        this.localConfig.update(c => ({ ...c, existingGameMode: mode }));
    }

    onBuild(): void {
        this.buildRequested.emit({
            config: this.localConfig(),
        });
    }
}
