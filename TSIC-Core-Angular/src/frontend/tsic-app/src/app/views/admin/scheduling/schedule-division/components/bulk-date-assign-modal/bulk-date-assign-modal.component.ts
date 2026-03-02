import { Component, ChangeDetectionStrategy, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { TimeslotService } from '../../../timeslots/services/timeslot.service';
import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import type { AgegroupCanvasReadinessDto, PriorYearFieldDefaults } from '@core/api';
import { contrastText, agTeamCount } from '../../../shared/utils/scheduling-helpers';

interface AppliedEntry {
    dow: string;
    dateFormatted: string;
    count: number;
    wave: number;
}

@Component({
    selector: 'app-bulk-date-assign-modal',
    standalone: true,
    imports: [CommonModule, FormsModule, TsicDialogComponent],
    templateUrl: './bulk-date-assign-modal.component.html',
    styleUrl: './bulk-date-assign-modal.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BulkDateAssignModalComponent implements OnInit {
    private readonly timeslotSvc = inject(TimeslotService);

    // ── Inputs ──
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});
    readonly priorYearDefaults = input<PriorYearFieldDefaults | null>(null);

    // ── Outputs ──
    readonly applied = output<void>();
    readonly closed = output<void>();

    // ── Helpers ──
    readonly contrastText = contrastText;
    readonly agTeamCount = agTeamCount;

    // ── Local state ──
    readonly selectedDate = signal('');
    readonly wave = signal(1);
    readonly startTime = signal('08:00 AM');
    readonly gsi = signal(60);
    readonly maxGames = signal(8);
    readonly checkedIds = signal<Set<string>>(new Set());
    readonly isApplying = signal(false);
    readonly appliedLog = signal<AppliedEntry[]>([]);

    // ── Computed ──

    /** Agegroups sorted by sortAge for consistent display order */
    readonly sortedAgegroups = computed(() =>
        [...this.agegroups()].sort((a, b) => a.sortAge - b.sortAge)
    );

    readonly allChecked = computed(() =>
        this.checkedIds().size === this.agegroups().length && this.agegroups().length > 0
    );

    readonly someChecked = computed(() => {
        const size = this.checkedIds().size;
        return size > 0 && size < this.agegroups().length;
    });

    readonly canApply = computed(() =>
        this.selectedDate() !== '' && this.checkedIds().size > 0 && !this.isApplying()
    );

    /** Source of inherited defaults, shown in the UI */
    readonly defaultsSource = signal('');

    ngOnInit(): void {
        // Three-tier field defaults: current config → prior year → hardcoded
        const map = this.readinessMap();
        const configured = Object.values(map).find(r => r.isConfigured);

        if (configured) {
            this.startTime.set(configured.startTime ?? '08:00 AM');
            this.gsi.set(configured.gamestartInterval ?? 60);
            this.maxGames.set(configured.maxGamesPerField ?? 8);
            this.defaultsSource.set('Current job');
        } else {
            const prior = this.priorYearDefaults();
            if (prior) {
                this.startTime.set(prior.startTime);
                this.gsi.set(prior.gamestartInterval);
                this.maxGames.set(prior.maxGamesPerField);
                this.defaultsSource.set(`From ${prior.priorYear}`);
            }
        }

        // Check all agegroups
        this.checkAll();
    }

    // ── Actions ──

    toggleAgegroup(agId: string): void {
        const current = new Set(this.checkedIds());
        if (current.has(agId)) {
            current.delete(agId);
        } else {
            current.add(agId);
        }
        this.checkedIds.set(current);
    }

    isChecked(agId: string): boolean {
        return this.checkedIds().has(agId);
    }

    checkAll(): void {
        this.checkedIds.set(new Set(this.agegroups().map(ag => ag.agegroupId)));
    }

    uncheckAll(): void {
        this.checkedIds.set(new Set());
    }

    toggleAll(): void {
        if (this.allChecked()) {
            this.uncheckAll();
        } else {
            this.checkAll();
        }
    }

    /** Formatted date list for an agegroup, e.g. ["Sat 06/06/2026", "Sun 06/07/2026"] */
    agExistingDates(agId: string): string[] {
        const r = this.readinessMap()[agId];
        if (!r?.gameDays || r.gameDays.length === 0) return [];
        return [...r.gameDays]
            .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
            .map(gd => {
                const d = new Date(gd.date);
                const mm = String(d.getMonth() + 1).padStart(2, '0');
                const dd = String(d.getDate()).padStart(2, '0');
                const yyyy = d.getFullYear();
                return `${gd.dow} ${mm}/${dd}/${yyyy}`;
            });
    }

    apply(): void {
        const dateStr = this.selectedDate();
        if (!dateStr || this.checkedIds().size === 0) return;

        this.isApplying.set(true);

        const gDate = new Date(dateStr + 'T00:00:00');

        this.timeslotSvc.bulkAssignDate({
            gDate: gDate.toISOString(),
            startTime: this.startTime(),
            gamestartInterval: this.gsi(),
            maxGamesPerField: this.maxGames(),
            agegroupIds: [...this.checkedIds()]
        }).subscribe({
            next: (response) => {
                const created = response.results.filter(r => r.dateCreated).length;

                // Format the applied entry
                const dow = gDate.toLocaleDateString('en-US', { weekday: 'short' });
                const mm = String(gDate.getMonth() + 1).padStart(2, '0');
                const dd = String(gDate.getDate()).padStart(2, '0');
                const yyyy = gDate.getFullYear();

                this.appliedLog.set([
                    ...this.appliedLog(),
                    { dow, dateFormatted: `${mm}/${dd}/${yyyy}`, count: created, wave: this.wave() }
                ]);

                // Reset for next pass
                this.selectedDate.set('');
                this.checkAll();
                this.isApplying.set(false);

                // Notify parent to refresh readiness
                this.applied.emit();
            },
            error: () => {
                this.isApplying.set(false);
            }
        });
    }
}
