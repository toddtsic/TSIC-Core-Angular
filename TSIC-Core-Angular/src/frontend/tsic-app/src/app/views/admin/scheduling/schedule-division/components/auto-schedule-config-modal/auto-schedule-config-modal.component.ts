import { Component, ChangeDetectionStrategy, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { DivisionStrategyEntry } from '@core/api';
import { contrastText } from '../../../shared/utils/scheduling-helpers';
import type { ScheduleScope } from '../../../shared/utils/scheduling-helpers';

/** Auto-schedule configuration persisted in localStorage. */
export interface AutoScheduleConfig {
    divisionOrderStrategy: 'alpha' | 'odd-first';
}

/** Modal-local agegroup entry for reordering/excluding at event scope. */
export interface ModalAgegroup {
    agegroupId: string;
    agegroupName: string;
    color: string | null;
    teamCount: number;
    divisionCount: number;
    included: boolean;
}

/** Emitted when the user clicks "Build Schedule". */
export interface AutoScheduleBuildEvent {
    strategies: DivisionStrategyEntry[];
    agegroups: ModalAgegroup[];
    config: AutoScheduleConfig;
}

@Component({
    selector: 'app-auto-schedule-config-modal',
    standalone: true,
    imports: [CommonModule, FormsModule, TsicDialogComponent],
    templateUrl: './auto-schedule-config-modal.component.html',
    styleUrl: './auto-schedule-config-modal.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AutoScheduleConfigModalComponent {
    // ── Inputs ──
    readonly scope = input.required<ScheduleScope>();
    readonly scopeLabel = input('');
    readonly strategies = input<DivisionStrategyEntry[]>([]);
    readonly strategySource = input('defaults');
    readonly strategySourceName = input('');
    readonly strategyLoading = input(false);
    readonly agegroups = input<ModalAgegroup[]>([]);
    readonly config = input<AutoScheduleConfig>({ divisionOrderStrategy: 'alpha' });

    // ── Outputs ──
    readonly buildRequested = output<AutoScheduleBuildEvent>();
    readonly cancelled = output<void>();

    // ── Helpers ──
    readonly contrastText = contrastText;

    // ── Local mutable copies (initialized from inputs via ngOnInit or setter) ──
    readonly localStrategies = signal<DivisionStrategyEntry[]>([]);
    readonly localAgegroups = signal<ModalAgegroup[]>([]);
    readonly localConfig = signal<AutoScheduleConfig>({ divisionOrderStrategy: 'alpha' });

    private initialized = false;

    ngOnChanges(): void {
        // Sync local state from inputs on first load
        if (!this.initialized) {
            this.localStrategies.set(this.strategies().map(s => ({ ...s })));
            this.localAgegroups.set(this.agegroups().map(a => ({ ...a })));
            this.localConfig.set({ ...this.config() });
            this.initialized = true;
        }
        // Keep strategies in sync when loading completes
        if (this.strategies().length > 0 && this.localStrategies().length === 0) {
            this.localStrategies.set(this.strategies().map(s => ({ ...s })));
        }
    }

    // ── Strategy manipulation ──

    togglePlacement(divisionName: string): void {
        this.localStrategies.update(list =>
            list.map(s => s.divisionName === divisionName
                ? { ...s, placement: s.placement === 0 ? 1 : 0 } : s)
        );
    }

    cycleGapPattern(divisionName: string): void {
        this.localStrategies.update(list =>
            list.map(s => s.divisionName === divisionName
                ? { ...s, gapPattern: (s.gapPattern + 1) % 3 } : s)
        );
    }

    cycleWave(divisionName: string): void {
        this.localStrategies.update(list =>
            list.map(s => s.divisionName === divisionName
                ? { ...s, wave: ((s.wave ?? 1) % 3) + 1 } : s)
        );
    }

    placementLabel(placement: number): string {
        return placement === 1 ? 'Vertical' : 'Horizontal';
    }

    gapLabel(gapPattern: number): string {
        switch (gapPattern) {
            case 0: return 'Back-to-back';
            case 1: return 'One on, one off';
            case 2: return 'One on, two off';
            default: return 'Unknown';
        }
    }

    waveLabel(wave: number | undefined): string {
        return `Wave ${wave ?? 1}`;
    }

    strategySourceLabel(): string {
        switch (this.strategySource()) {
            case 'saved': return 'Saved';
            case 'saved-cleaned': return 'Saved — updated after division rename';
            case 'inferred': return `Based on ${this.strategySourceName() || 'prior year'}`;
            default: return 'Defaults';
        }
    }

    // ── Division order strategy ──

    setDivisionOrderStrategy(strategy: 'alpha' | 'odd-first'): void {
        this.localConfig.update(c => ({ ...c, divisionOrderStrategy: strategy }));
    }

    // ── Agegroup ordering ──

    toggleAgegroup(index: number): void {
        this.localAgegroups.update(list => list.map((ag, i) =>
            i === index ? { ...ag, included: !ag.included } : ag
        ));
    }

    moveAgegroupUp(index: number): void {
        if (index <= 0) return;
        this.localAgegroups.update(list => {
            const updated = [...list];
            [updated[index - 1], updated[index]] = [updated[index], updated[index - 1]];
            return updated;
        });
    }

    moveAgegroupDown(index: number): void {
        this.localAgegroups.update(list => {
            if (index >= list.length - 1) return list;
            const updated = [...list];
            [updated[index], updated[index + 1]] = [updated[index + 1], updated[index]];
            return updated;
        });
    }

    // ── Build ──

    onBuild(): void {
        this.buildRequested.emit({
            strategies: this.localStrategies(),
            agegroups: this.localAgegroups(),
            config: this.localConfig(),
        });
    }
}
