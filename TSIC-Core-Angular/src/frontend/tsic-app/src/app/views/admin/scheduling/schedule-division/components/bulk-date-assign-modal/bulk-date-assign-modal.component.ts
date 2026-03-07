import { Component, ChangeDetectionStrategy, computed, ElementRef, inject, input, OnInit, output, signal, viewChild } from '@angular/core';
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
    roundsPerDay: number;
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
    readonly priorYearRounds = input<Record<string, number> | null>(null);

    // ── Outputs ──
    readonly applied = output<void>();
    readonly closed = output<void>();

    // ── Helpers ──
    readonly contrastText = contrastText;
    readonly agTeamCount = agTeamCount;

    // ── ViewChild ──
    readonly dateInput = viewChild<ElementRef<HTMLInputElement>>('dateInput');

    // ── Local state ──
    readonly selectedDate = signal('');
    readonly showDateInput = signal(false);
    readonly roundsMap = signal<Map<string, number>>(new Map());
    readonly waveMap = signal<Map<string, number>>(new Map());
    readonly startTime = signal('08:00 AM');
    readonly gsi = signal(60);
    readonly maxGames = signal(8);
    readonly checkedIds = signal<Set<string>>(new Set());
    /** Original agegroup IDs when an existing date was selected (for computing removals) */
    readonly originalAgIds = signal<Set<string>>(new Set());
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

    readonly hasDate = computed(() => this.selectedDate() !== '');

    /** Aggregate all unique game dates across all agegroups from readiness data */
    readonly existingDates = computed(() => {
        const map = this.readinessMap();
        const dateAgg = new Map<string, { isoDate: string; dow: string; dateFormatted: string; agCount: number; agIds: Set<string> }>();

        for (const [agId, r] of Object.entries(map)) {
            if (!r.gameDays) continue;
            for (const gd of r.gameDays) {
                const key = gd.date.substring(0, 10);
                const existing = dateAgg.get(key);
                if (existing) {
                    existing.agCount++;
                    existing.agIds.add(agId);
                } else {
                    const d = new Date(gd.date);
                    const mm = String(d.getMonth() + 1).padStart(2, '0');
                    const dd = String(d.getDate()).padStart(2, '0');
                    const yyyy = d.getFullYear();
                    dateAgg.set(key, { isoDate: key, dow: gd.dow, dateFormatted: `${mm}/${dd}/${yyyy}`, agCount: 1, agIds: new Set([agId]) });
                }
            }
        }

        return [...dateAgg.values()]
            .map(v => ({ isoDate: v.isoDate, dow: v.dow, dateFormatted: v.dateFormatted, agCount: v.agCount, agIds: [...v.agIds] }))
            .sort((a, b) => a.isoDate.localeCompare(b.isoDate));
    });

    /** True when actively editing an existing configured date (not adding a new one) */
    readonly isEditingExisting = computed(() =>
        this.hasDate() && !this.showDateInput() && this.existingDates().some(d => d.isoDate === this.selectedDate())
    );

    readonly canApply = computed(() =>
        this.hasDate() && !this.isApplying() && (
            this.checkedIds().size > 0 || this.computeRemovedIds().length > 0
        )
    );

    /** Format the selected date as DOW MM/DD/YYYY for display */
    readonly selectedDateLabel = computed(() => {
        const dateStr = this.selectedDate();
        if (!dateStr) return '';
        const d = new Date(dateStr + 'T00:00:00');
        const dow = d.toLocaleDateString('en-US', { weekday: 'long' });
        const mm = String(d.getMonth() + 1).padStart(2, '0');
        const dd = String(d.getDate()).padStart(2, '0');
        const yyyy = d.getFullYear();
        return `${dow} ${mm}/${dd}/${yyyy}`;
    });

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

        // Auto-select first existing date (ready to edit), or check all for new entry
        const dates = this.existingDates();
        if (dates.length > 0) {
            this.selectExistingDate(dates[0].isoDate, dates[0].agIds);
        } else {
            this.checkAll();
        }
    }

    // ── Actions ──

    onAddDate(): void {
        this.showDateInput.set(true);
        this.selectedDate.set('');
        this.originalAgIds.set(new Set());
        this.checkAll();
        // Focus the input after Angular renders it
        setTimeout(() => {
            this.dateInput()?.nativeElement.focus();
            this.dateInput()?.nativeElement.showPicker?.();
        });
    }

    /** Click an existing date → load it as active, pre-check agegroups that already play on it */
    selectExistingDate(isoDate: string, agIds: string[]): void {
        this.selectedDate.set(isoDate);
        this.showDateInput.set(false);
        // Pre-check only the agegroups that already have this date configured
        this.checkedIds.set(new Set(agIds));
        // Remember originals so we can detect removals on Apply
        this.originalAgIds.set(new Set(agIds));
        // Initialize waves for checked agegroups
        const current = new Map(this.waveMap());
        for (const id of agIds) {
            if (!current.has(id)) current.set(id, 1);
        }
        this.waveMap.set(current);
    }

    /** Agegroups that were originally on this date but are now unchecked */
    computeRemovedIds(): string[] {
        const originals = this.originalAgIds();
        const checked = this.checkedIds();
        return [...originals].filter(id => !checked.has(id));
    }

    /** Whether a given ISO date is the currently selected date */
    isSelectedDate(isoDate: string): boolean {
        return this.selectedDate() === isoDate;
    }

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
        const ids = this.agegroups().map(ag => ag.agegroupId);
        this.checkedIds.set(new Set(ids));
        // Initialize wave to 1 for any newly checked agegroups
        const current = new Map(this.waveMap());
        for (const id of ids) {
            if (!current.has(id)) current.set(id, 1);
        }
        this.waveMap.set(current);
    }

    getWave(agId: string): number {
        return this.waveMap().get(agId) ?? 1;
    }

    setWave(agId: string, wave: number): void {
        const updated = new Map(this.waveMap());
        updated.set(agId, wave);
        this.waveMap.set(updated);
    }

    getRounds(agId: string): number {
        return this.roundsMap().get(agId) ?? this.defaultRounds(agId);
    }

    setRounds(agId: string, rounds: number): void {
        const updated = new Map(this.roundsMap());
        updated.set(agId, rounds);
        this.roundsMap.set(updated);
    }

    /** Three-tier round suggestion: current pairings → prior year → default 3 */
    private defaultRounds(agId: string): number {
        const readiness = this.readinessMap()[agId];
        // Tier 1: Current pairings tell us exactly how many rounds
        if (readiness?.maxPairingRound && readiness.maxPairingRound > 0) return readiness.maxPairingRound;
        // Tier 2: Prior year by agegroup name match
        const ag = this.agegroups().find(a => a.agegroupId === agId);
        const priorRounds = this.priorYearRounds();
        if (ag && priorRounds && ag.agegroupName && priorRounds[ag.agegroupName]) {
            return priorRounds[ag.agegroupName];
        }
        // Tier 3: Default minimum
        return 3;
    }

    /** Tooltip explaining where the rounds default comes from */
    roundsSource(agId: string): string {
        if (this.roundsMap().has(agId)) return 'Manual override';
        const readiness = this.readinessMap()[agId];
        if (readiness?.maxPairingRound && readiness.maxPairingRound > 0) return 'From current pairings';
        const ag = this.agegroups().find(a => a.agegroupId === agId);
        const priorRounds = this.priorYearRounds();
        if (ag && priorRounds && ag.agegroupName && priorRounds[ag.agegroupName]) return 'From prior year';
        return 'Default (3)';
    }

    /** Whether an agegroup has any dates configured */
    agHasDates(agId: string): boolean {
        const r = this.readinessMap()[agId];
        return !!(r?.gameDays && r.gameDays.length > 0);
    }

    /** Total rounds configured for an agegroup (across all dates) */
    agTotalRounds(agId: string): number {
        const r = this.readinessMap()[agId];
        return r?.totalRounds ?? 0;
    }

    /** Rounds needed from pairings */
    agRoundsNeeded(agId: string): number {
        const r = this.readinessMap()[agId];
        return r?.maxPairingRound ?? 0;
    }

    /** Options 1-8 for round-per-day select */
    readonly roundOptions = [1, 2, 3, 4, 5, 6, 7, 8];

    /** Label for round option — appends "(all)" when value matches needed RR rounds */
    roundOptionLabel(agId: string, value: number): string {
        const needed = this.agRoundsNeeded(agId);
        return needed > 0 && value === needed ? `${value} (all)` : `${value}`;
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

    /** Formatted date list for an agegroup, e.g. ["Sat 06/06/2026 (3R)", "Sun 06/07/2026"] */
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
                const rnd = gd.roundCount > 1 ? ` (${gd.roundCount}R)` : '';
                return `${gd.dow} ${mm}/${dd}/${yyyy}${rnd}`;
            });
    }

    apply(): void {
        const dateStr = this.selectedDate();
        const removedIds = this.computeRemovedIds();
        if (!dateStr || (this.checkedIds().size === 0 && removedIds.length === 0)) return;

        this.isApplying.set(true);
        const isExisting = this.isEditingExisting();

        const gDate = new Date(dateStr + 'T00:00:00');

        const entries = [...this.checkedIds()].map(agId => ({
            agegroupId: agId,
            wave: this.getWave(agId),
            roundsPerDay: this.getRounds(agId)
        }));

        this.timeslotSvc.bulkAssignDate({
            gDate: gDate.toISOString(),
            startTime: this.startTime(),
            gamestartInterval: this.gsi(),
            maxGamesPerField: this.maxGames(),
            roundsPerDay: 1,
            entries,
            removedAgegroupIds: removedIds.length > 0 ? removedIds : undefined
        }).subscribe({
            next: (response) => {
                const created = response.results?.filter(r => r.dateCreated).length ?? 0;

                // Log when agegroups were added or removed
                if (created > 0 || removedIds.length > 0) {
                    const dow = gDate.toLocaleDateString('en-US', { weekday: 'short' });
                    const mm = String(gDate.getMonth() + 1).padStart(2, '0');
                    const dd = String(gDate.getDate()).padStart(2, '0');
                    const yyyy = gDate.getFullYear();
                    const wavesUsed = [...new Set(entries.map(e => e.wave))].sort();
                    const maxRpd = entries.length > 0 ? Math.max(...entries.map(e => e.roundsPerDay)) : 0;

                    this.appliedLog.set([
                        ...this.appliedLog(),
                        { dow, dateFormatted: `${mm}/${dd}/${yyyy}`, count: created - removedIds.length, roundsPerDay: maxRpd, wave: wavesUsed[0] ?? 1 }
                    ]);
                }

                // New dates: reset for next entry. Existing dates: stay put.
                if (!isExisting) {
                    this.selectedDate.set('');
                    this.showDateInput.set(false);
                    this.checkAll();
                }
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
