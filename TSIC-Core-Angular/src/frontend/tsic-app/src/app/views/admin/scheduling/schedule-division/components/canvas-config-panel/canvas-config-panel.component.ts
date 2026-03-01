import { Component, ChangeDetectionStrategy, inject, input, output, signal, computed, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TimeslotService } from '../../../timeslots/services/timeslot.service';
import { ToastService } from '@shared-ui/toast.service';
import type {
    AgegroupWithDivisionsDto,
    AgegroupCanvasReadinessDto,
    TimeslotDateDto,
    TimeslotFieldDto,
    CapacityPreviewDto
} from '@core/api';

@Component({
    selector: 'app-canvas-config-panel',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './canvas-config-panel.component.html',
    styleUrl: './canvas-config-panel.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class CanvasConfigPanelComponent implements OnChanges {
    private readonly svc = inject(TimeslotService);
    private readonly toast = inject(ToastService);

    // ── Inputs ──
    readonly agegroupId = input.required<string>();
    readonly agegroupName = input<string>('');
    readonly agegroupColor = input<string | null>(null);
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});

    // ── Outputs ──
    readonly canvasConfigured = output<void>();

    // ── Data signals ──
    readonly dates = signal<TimeslotDateDto[]>([]);
    readonly fields = signal<TimeslotFieldDto[]>([]);
    readonly capacity = signal<CapacityPreviewDto[]>([]);
    readonly isLoading = signal(false);

    // ── Date form ──
    readonly showDateForm = signal(false);
    readonly dateFormDate = signal('');
    readonly dateFormRnd = signal(1);
    readonly isSavingDate = signal(false);

    // ── Field form ──
    readonly showFieldForm = signal(false);
    readonly fieldFormDow = signal('Saturday');
    readonly fieldFormStartTime = signal('08:00');
    readonly fieldFormInterval = signal(60);
    readonly fieldFormMaxGames = signal(6);
    readonly isSavingField = signal(false);

    // ── Clone ──
    readonly showClonePanel = signal(false);
    readonly cloneSourceId = signal('');
    readonly isCloning = signal(false);
    readonly isCloningAll = signal(false);

    // ── Computeds ──
    readonly suggestedNextRnd = computed(() => {
        const d = this.dates();
        if (d.length === 0) return 1;
        return Math.max(...d.map(dd => dd.rnd)) + 1;
    });

    readonly capacityTotalSlots = computed(() =>
        this.capacity().reduce((sum, c) => sum + c.totalGameSlots, 0));

    readonly capacityTotalNeeded = computed(() =>
        this.capacity().reduce((sum, c) => sum + c.gamesNeeded, 0));

    readonly capacityAllSufficient = computed(() =>
        this.capacity().length > 0 && this.capacity().every(c => c.isSufficient));

    readonly canFinish = computed(() =>
        this.dates().length > 0 && this.fields().length > 0);

    readonly cloneableAgegroups = computed(() =>
        this.agegroups().filter(ag => {
            if (ag.agegroupId === this.agegroupId()) return false;
            const r = this.readinessMap()[ag.agegroupId];
            return r?.isConfigured === true;
        })
    );

    readonly unconfiguredAgegroups = computed(() =>
        this.agegroups().filter(ag => {
            if (ag.agegroupId === this.agegroupId()) return false;
            const r = this.readinessMap()[ag.agegroupId];
            return !r?.isConfigured;
        })
    );

    readonly daysOfWeek = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

    ngOnChanges(changes: SimpleChanges): void {
        if (changes['agegroupId']) {
            this.loadConfiguration();
        }
    }

    // ── Data loading ──

    loadConfiguration(): void {
        this.isLoading.set(true);
        this.svc.getConfiguration(this.agegroupId()).subscribe({
            next: (config) => {
                this.dates.set(config.dates);
                this.fields.set(config.fields);
                this.isLoading.set(false);
                this.refreshCapacity();
            },
            error: () => {
                this.dates.set([]);
                this.fields.set([]);
                this.isLoading.set(false);
            }
        });
    }

    refreshCapacity(): void {
        if (this.dates().length === 0 || this.fields().length === 0) {
            this.capacity.set([]);
            return;
        }
        this.svc.getCapacityPreview(this.agegroupId()).subscribe({
            next: (data) => this.capacity.set(data),
            error: () => this.capacity.set([])
        });
    }

    // ── Date CRUD ──

    openDateForm(): void {
        this.dateFormDate.set('');
        this.dateFormRnd.set(this.suggestedNextRnd());
        this.showDateForm.set(true);
    }

    saveDate(): void {
        if (!this.dateFormDate()) return;
        this.isSavingDate.set(true);
        this.svc.addDate({
            agegroupId: this.agegroupId(),
            gDate: this.dateFormDate(),
            rnd: this.dateFormRnd()
        }).subscribe({
            next: (newDate) => {
                this.dates.update(curr => [...curr, newDate]
                    .sort((a, b) => new Date(a.gDate).getTime() - new Date(b.gDate).getTime()));
                this.isSavingDate.set(false);
                this.showDateForm.set(false);
                this.refreshCapacity();
            },
            error: () => {
                this.toast.show('Failed to add date', 'danger');
                this.isSavingDate.set(false);
            }
        });
    }

    deleteDate(ai: number): void {
        this.svc.deleteDate(ai).subscribe({
            next: () => {
                this.dates.update(curr => curr.filter(d => d.ai !== ai));
                this.refreshCapacity();
            },
            error: () => this.toast.show('Failed to delete date', 'danger')
        });
    }

    // ── Field CRUD ──

    openFieldForm(): void {
        this.fieldFormDow.set('Saturday');
        this.fieldFormStartTime.set('08:00');
        this.fieldFormInterval.set(60);
        this.fieldFormMaxGames.set(6);
        this.showFieldForm.set(true);
    }

    saveField(): void {
        this.isSavingField.set(true);
        this.svc.addFieldTimeslot({
            agegroupId: this.agegroupId(),
            startTime: this.fieldFormStartTime(),
            gamestartInterval: this.fieldFormInterval(),
            maxGamesPerField: this.fieldFormMaxGames(),
            dow: this.fieldFormDow()
        }).subscribe({
            next: (newFields) => {
                this.fields.update(curr => [...curr, ...newFields]);
                this.isSavingField.set(false);
                this.showFieldForm.set(false);
                this.refreshCapacity();
            },
            error: () => {
                this.toast.show('Failed to add field schedule', 'danger');
                this.isSavingField.set(false);
            }
        });
    }

    deleteField(ai: number): void {
        this.svc.deleteFieldTimeslot(ai).subscribe({
            next: () => {
                this.fields.update(curr => curr.filter(f => f.ai !== ai));
                this.refreshCapacity();
            },
            error: () => this.toast.show('Failed to delete field schedule', 'danger')
        });
    }

    // ── Clone ──

    cloneFromAgegroup(): void {
        const sourceId = this.cloneSourceId();
        if (!sourceId) return;
        this.isCloning.set(true);

        // Clone dates first, then fields sequentially (shared DbContext)
        this.svc.cloneDates({ sourceAgegroupId: sourceId, targetAgegroupId: this.agegroupId() }).subscribe({
            next: () => {
                this.svc.cloneFields({ sourceAgegroupId: sourceId, targetAgegroupId: this.agegroupId() }).subscribe({
                    next: () => {
                        this.isCloning.set(false);
                        this.showClonePanel.set(false);
                        this.loadConfiguration();
                        this.toast.show('Canvas cloned successfully', 'success');
                    },
                    error: () => {
                        this.isCloning.set(false);
                        this.toast.show('Failed to clone field schedules', 'danger');
                    }
                });
            },
            error: () => {
                this.isCloning.set(false);
                this.toast.show('Failed to clone dates', 'danger');
            }
        });
    }

    cloneToAllUnconfigured(): void {
        const targets = this.unconfiguredAgegroups();
        if (targets.length === 0) return;
        this.isCloningAll.set(true);
        this.cloneToTargetsSequentially(targets, 0);
    }

    private cloneToTargetsSequentially(targets: AgegroupWithDivisionsDto[], index: number): void {
        if (index >= targets.length) {
            this.isCloningAll.set(false);
            this.canvasConfigured.emit();
            this.toast.show(`Cloned to ${targets.length} agegroups`, 'success');
            return;
        }

        const targetId = targets[index].agegroupId;
        this.svc.cloneDates({ sourceAgegroupId: this.agegroupId(), targetAgegroupId: targetId }).subscribe({
            next: () => {
                this.svc.cloneFields({ sourceAgegroupId: this.agegroupId(), targetAgegroupId: targetId }).subscribe({
                    next: () => this.cloneToTargetsSequentially(targets, index + 1),
                    error: () => {
                        this.isCloningAll.set(false);
                        this.toast.show(`Failed to clone fields to ${targets[index].agegroupName}`, 'danger');
                    }
                });
            },
            error: () => {
                this.isCloningAll.set(false);
                this.toast.show(`Failed to clone dates to ${targets[index].agegroupName}`, 'danger');
            }
        });
    }

    // ── Finish ──

    finishSetup(): void {
        this.canvasConfigured.emit();
    }

    // ── Helpers ──

    formatDate(dateStr: string): string {
        const d = new Date(dateStr);
        return d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
    }

    shortDow(dow: string): string {
        return dow.substring(0, 3);
    }
}
