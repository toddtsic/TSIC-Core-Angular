import { Component, ChangeDetectionStrategy, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { TimeslotService } from '../../../timeslots/services/timeslot.service';
import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import type { AgegroupCanvasReadinessDto, PriorYearFieldDefaults } from '@core/api';
import { contrastText, agTeamCount } from '../../../shared/utils/scheduling-helpers';

/** Per-agegroup entry in the ordered list (mutable local state). */
interface AgEntry {
    agegroupId: string;
    agegroupName: string;
    color: string | null;
    teamCount: number;
    divCount: number;
    wave: number;
    checked: boolean;
}

interface AppliedEntry {
    dow: string;
    dateFormatted: string;
    count: number;
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

    // ── Local state ──
    readonly selectedDate = signal('');
    readonly startTime = signal('08:00 AM');
    readonly gsi = signal(60);
    readonly maxGames = signal(8);
    readonly isApplying = signal(false);
    readonly appliedLog = signal<AppliedEntry[]>([]);

    /** Ordered agegroup entries with per-agegroup wave and check state. */
    readonly entries = signal<AgEntry[]>([]);

    // ── Computed ──

    readonly allChecked = computed(() => {
        const list = this.entries();
        return list.length > 0 && list.every(e => e.checked);
    });

    readonly someChecked = computed(() => {
        const list = this.entries();
        const checkedCount = list.filter(e => e.checked).length;
        return checkedCount > 0 && checkedCount < list.length;
    });

    readonly canApply = computed(() =>
        this.selectedDate() !== '' && this.entries().some(e => e.checked) && !this.isApplying()
    );

    /** Source of inherited defaults, shown in the UI */
    readonly defaultsSource = signal('');

    ngOnInit(): void {
        // Initialize ordered entries from agegroups (sorted by sortAge)
        const sorted = [...this.agegroups()].sort((a, b) => a.sortAge - b.sortAge);
        this.entries.set(sorted.map(ag => ({
            agegroupId: ag.agegroupId,
            agegroupName: ag.agegroupName,
            color: ag.color ?? null,
            teamCount: agTeamCount(ag),
            divCount: ag.divisions.length,
            wave: 1,
            checked: true,
        })));

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
    }

    // ── Agegroup check/uncheck ──

    toggleAgegroup(index: number): void {
        this.entries.update(list => list.map((e, i) =>
            i === index ? { ...e, checked: !e.checked } : e
        ));
    }

    toggleAll(): void {
        const newVal = !this.allChecked();
        this.entries.update(list => list.map(e => ({ ...e, checked: newVal })));
    }

    // ── Sort order ──

    moveUp(index: number): void {
        if (index <= 0) return;
        this.entries.update(list => {
            const updated = [...list];
            [updated[index - 1], updated[index]] = [updated[index], updated[index - 1]];
            return updated;
        });
    }

    moveDown(index: number): void {
        this.entries.update(list => {
            if (index >= list.length - 1) return list;
            const updated = [...list];
            [updated[index], updated[index + 1]] = [updated[index + 1], updated[index]];
            return updated;
        });
    }

    // ── Per-agegroup wave ──

    setWave(index: number, wave: number): void {
        this.entries.update(list => list.map((e, i) =>
            i === index ? { ...e, wave } : e
        ));
    }

    // ── Existing dates helper ──

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

    // ── Apply ──

    apply(): void {
        const dateStr = this.selectedDate();
        const checked = this.entries().filter(e => e.checked);
        if (!dateStr || checked.length === 0) return;

        this.isApplying.set(true);
        const gDate = new Date(dateStr + 'T00:00:00');

        this.timeslotSvc.bulkAssignDate({
            gDate: gDate.toISOString(),
            startTime: this.startTime(),
            gamestartInterval: this.gsi(),
            maxGamesPerField: this.maxGames(),
            entries: checked.map(e => ({
                agegroupId: e.agegroupId,
                wave: e.wave,
            })),
        }).subscribe({
            next: (response) => {
                const created = response.results.filter(r => r.dateCreated).length;

                const dow = gDate.toLocaleDateString('en-US', { weekday: 'short' });
                const mm = String(gDate.getMonth() + 1).padStart(2, '0');
                const dd = String(gDate.getDate()).padStart(2, '0');
                const yyyy = gDate.getFullYear();

                this.appliedLog.set([
                    ...this.appliedLog(),
                    { dow, dateFormatted: `${mm}/${dd}/${yyyy}`, count: created }
                ]);

                // Reset date for next pass, keep sort order and wave assignments
                this.selectedDate.set('');
                this.entries.update(list => list.map(e => ({ ...e, checked: true })));
                this.isApplying.set(false);

                this.applied.emit();
            },
            error: () => {
                this.isApplying.set(false);
            }
        });
    }
}
