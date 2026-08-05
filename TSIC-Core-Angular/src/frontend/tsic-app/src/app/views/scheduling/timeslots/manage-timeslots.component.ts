import {
    ChangeDetectionStrategy, Component, OnInit, computed, inject, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
    TimeslotService,
    type TimeslotConfigurationResponse,
    type TimeslotDateDto,
    type TimeslotFieldDto,
    type CapacityPreviewDto
} from './services/timeslot.service';
import { PairingsService } from '../pairings/services/pairings.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { AgegroupWithDivisionsDto, DivisionSummaryDto, EventFieldSummaryDto } from '@core/api';
import { ChecklistBackLinkComponent } from '../shared/components/checklist-back-link/checklist-back-link.component';
import { AgeGroupPickerComponent, type AgePickerItem } from '../shared/components/age-group-picker/age-group-picker.component';

const DAYS_OF_WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

/**
 * One field on one day, with every pool that shares its exact timing folded in.
 *
 * Timeslots are stored per division, so a 31-field agegroup with 6 pools is 186 rows that
 * differ only in Division — unreadable, and it buries the one question worth asking: which
 * pools have no timeslots. Grouping on timing means pools that diverge fall into their own
 * row on their own, with no special case.
 */
interface FieldGroup {
    key: string;
    fieldId: string;
    fieldName: string;
    dow: string;
    startTime: string;
    gamestartInterval: number;
    maxGamesPerField: number;
    /** Row ids behind this group — one per pool. */
    ais: number[];
    poolNames: string[];
    /** Every division in the agegroup is covered by this group. */
    allPools: boolean;
    /** Pools of the agegroup with no timeslot on this field/day. */
    missingPools: number;
}

interface DowSection {
    dow: string;
    groups: FieldGroup[];
    /** "08:00 · 60m · max 6" when uniform across the day, else "mixed". */
    summary: string;
}

/**
 * One game day's timeslot shape, as edited in the setup dialog.
 *
 * The day is NOT a choice. The build engine walks the agegroup's game DATES, takes each date's
 * day of week and reads only field rows carrying that Dow — a play day with no rows is silently
 * skipped, and rows for a day never played are never read at all. So the rows of this dialog are
 * the distinct days among the game dates, no more and no less.
 */
interface DaySetupRow {
    dow: string;
    /** Fields hosting this agegroup that day. Defaults to every event field. */
    fieldIds: string[];
    startTime: string;
    gamestartInterval: number;
    slotsPerField: number;
}

/**
 * A clone that would overwrite an agegroup's data, held until the user confirms. Cloning
 * replaces the target, so the only honest thing to do is name what is about to be lost.
 */
interface PendingClone {
    kind: 'ag-dates' | 'ag-fields';
    title: string;
    message: string;
}

@Component({
    selector: 'app-manage-timeslots',
    standalone: true,
    imports: [
        CommonModule, FormsModule, TsicDialogComponent, ConfirmDialogComponent,
        ChecklistBackLinkComponent, AgeGroupPickerComponent
    ],
    templateUrl: './manage-timeslots.component.html',
    styleUrl: './manage-timeslots.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ManageTimeslotsComponent implements OnInit {
    private readonly svc = inject(TimeslotService);
    private readonly pairingSvc = inject(PairingsService);

    // ── Navigator state ──
    readonly agegroups = signal<AgegroupWithDivisionsDto[]>([]);
    readonly selectedAgegroup = signal<AgegroupWithDivisionsDto | null>(null);
    readonly isNavLoading = signal(false);

    // ── Configuration state ──
    readonly dates = signal<TimeslotDateDto[]>([]);
    readonly fields = signal<TimeslotFieldDto[]>([]);
    readonly capacityPreview = signal<CapacityPreviewDto[]>([]);
    readonly eventFields = signal<EventFieldSummaryDto[]>([]);
    readonly isLoading = signal(false);

    // ── Tabs ──
    // Dates are a panel above, not a tab: they are the context for field timeslots and hiding
    // the small list to show the big one was the regression against legacy's single page.
    readonly activeTab = signal<'fields' | 'capacity'>('fields');

    // ── Fields sort (within each day) ──
    readonly fieldSort = signal<'field' | 'start'>('field');

    // ── Date Modal ──
    readonly showDateModal = signal(false);
    readonly dateModalMode = signal<'add' | 'edit'>('add');
    readonly dateModalDate = signal('');
    readonly dateModalRnd = signal(1);
    readonly dateModalAi = signal<number | null>(null);
    readonly isSavingDate = signal(false);

    // ── Clone to another agegroup ──
    readonly cloneTargetAgId = signal('');
    readonly isCloningDates = signal(false);
    readonly isCloningFields = signal(false);

    // ── Timeslot setup — the ONLY way slots are created or changed ──
    // Everything the old per-row actions did is a checkbox or a number in here: unticking a
    // field on a day removes it, ticking it on another day copies it, and the timing fields
    // replace the per-row edit. The table below is a read-out.
    readonly showSetupModal = signal(false);
    readonly setupDays = signal<DaySetupRow[]>([]);
    /** Which day's field picker is expanded — one at a time keeps the dialog compact. */
    readonly setupExpandedDow = signal<string | null>(null);
    readonly isSavingSetup = signal(false);

    /** Set when a clone would overwrite the target agegroup; cleared on confirm or cancel. */
    readonly pendingClone = signal<PendingClone | null>(null);

    // ── Delete All ──
    readonly showDeleteDatesConfirm = signal(false);
    readonly showDeleteFieldsConfirm = signal(false);
    readonly isDeletingAllDates = signal(false);
    readonly isDeletingAllFields = signal(false);

    /**
     * Pools that actually get scheduled: not the "Unassigned" holding division
     * TeamPlacementService creates per agegroup, not a Dropped bucket, and holding at least
     * one active team. `teamCount` is already active-only (PairingsRepository:170).
     *
     * An empty pool needs no timeslot, so counting one made a fully-configured field read
     * "1 missing" on every row.
     */
    readonly schedulablePools = computed(() => this.poolsOf(this.selectedAgegroup()));

    /** The rail badge, the clone picker and the header all count pools this one way. */
    poolCountOf(ag: AgegroupWithDivisionsDto): number {
        return this.poolsOf(ag).length;
    }

    private poolsOf(ag: AgegroupWithDivisionsDto | null): DivisionSummaryDto[] {
        return (ag?.divisions ?? []).filter(d => {
            const name = (d.divName ?? '').toUpperCase();
            return d.teamCount > 0 && name !== 'UNASSIGNED' && !name.startsWith('DROPPED');
        });
    }

    /**
     * Field timeslots folded to one row per field/day/timing and sectioned by day. This is the
     * whole point of the view: 186 raw rows for a 31-field, 6-pool agegroup become 31.
     */
    readonly dowSections = computed<DowSection[]>(() => {
        const poolCount = this.schedulablePools().length;
        const sort = this.fieldSort();
        const byKey = new Map<string, FieldGroup>();

        for (const f of this.fields()) {
            const key = [f.fieldId, f.dow, f.startTime, f.gamestartInterval, f.maxGamesPerField].join('|');
            const group = byKey.get(key);
            if (group) {
                group.ais.push(f.ai);
                if (f.divName) group.poolNames.push(f.divName);
            } else {
                byKey.set(key, {
                    key,
                    fieldId: f.fieldId,
                    fieldName: f.fieldName,
                    dow: f.dow,
                    startTime: f.startTime,
                    gamestartInterval: f.gamestartInterval,
                    maxGamesPerField: f.maxGamesPerField,
                    ais: [f.ai],
                    poolNames: f.divName ? [f.divName] : [],
                    allPools: false,
                    missingPools: 0
                });
            }
        }

        const byDow = new Map<string, FieldGroup[]>();
        for (const g of byKey.values()) {
            g.poolNames.sort((a, b) => a.localeCompare(b));
            // A row with no divId covers the agegroup outright; otherwise it covers the pools
            // named on it. Anything short of every pool is a gap the build would trip over.
            g.allPools = g.poolNames.length === 0 || (poolCount > 0 && g.poolNames.length >= poolCount);
            g.missingPools = g.allPools ? 0 : Math.max(0, poolCount - g.poolNames.length);

            const list = byDow.get(g.dow) ?? [];
            list.push(g);
            byDow.set(g.dow, list);
        }

        return DAYS_OF_WEEK
            .filter(d => byDow.has(d))
            .map(dow => {
                const groups = (byDow.get(dow) ?? []).sort((a, b) =>
                    sort === 'start'
                        ? a.startTime.localeCompare(b.startTime) || a.fieldName.localeCompare(b.fieldName)
                        : a.fieldName.localeCompare(b.fieldName) || a.startTime.localeCompare(b.startTime));

                const uniform = groups.every(g =>
                    g.startTime === groups[0].startTime
                    && g.gamestartInterval === groups[0].gamestartInterval
                    && g.maxGamesPerField === groups[0].maxGamesPerField);

                return {
                    dow,
                    groups,
                    summary: uniform
                        ? `${groups[0].startTime} · ${groups[0].gamestartInterval}m · max ${groups[0].maxGamesPerField}`
                        : 'mixed timing'
                } satisfies DowSection;
            });
    });

    /** Distinct fields on screen, after folding — the count the user is really managing. */
    readonly foldedFieldCount = computed(() =>
        this.dowSections().reduce((sum, s) => sum + s.groups.length, 0));

    /**
     * The pencils: every droppable cell across every game day. One folded row is one field on
     * one day, so it contributes its own slot count; pools do NOT multiply it — six pools share
     * the same physical field-hour.
     *
     * This is the headline number. The stored row count (96 here) is what legacy displayed and
     * what the fold note still reconciles to, but it answers no question anyone has: it is
     * fields × pools × days, an artifact of per-division storage. 224 is how many games fit.
     */
    readonly totalGameSlots = computed(() =>
        this.dowSections().reduce((sum, s) =>
            sum + s.groups.reduce((inner, g) => inner + g.maxGamesPerField, 0), 0));

    // ══ Game days ═════════════════════════════════════════════════════════════

    /**
     * The distinct days this agegroup actually plays, in week order — derived from the game
     * dates above, never chosen. Everything about timeslots hangs off this list: it is the
     * agenda of the setup dialog and the only set of days the build engine will ever read.
     */
    readonly playDays = computed(() => {
        const seen = new Set(this.dates().map(d => this.dayOfWeek(d.gDate)));
        return DAYS_OF_WEEK.filter(d => seen.has(d));
    });

    /** "Friday and Saturday" — the days named back to the user, so a wrong date is obvious. */
    readonly playDaysLabel = computed(() => {
        const days = this.playDays();
        if (days.length === 0) return '';
        if (days.length === 1) return days[0];
        return `${days.slice(0, -1).join(', ')} and ${days[days.length - 1]}`;
    });

    /** Days that are played but hold no timeslots — the gap that silently kills a build. */
    readonly daysWithoutSlots = computed(() => {
        const withSlots = new Set(this.fields().map(f => f.dow));
        return this.playDays().filter(d => !withSlots.has(d));
    });

    /** Timeslots sitting on days this agegroup never plays — dead rows the engine never reads. */
    readonly orphanSlotDays = computed(() => {
        const played = new Set(this.playDays());
        return Array.from(new Set(this.fields().map(f => f.dow)))
            .filter(d => !played.has(d))
            .sort((a, b) => DAYS_OF_WEEK.indexOf(a) - DAYS_OF_WEEK.indexOf(b));
    });

    /** Nothing to set up until there are dates: no dates, no days, no slots. */
    readonly canSetupTimeslots = computed(() => this.playDays().length > 0);

    readonly setupButtonLabel = computed(() =>
        this.fields().length > 0 ? 'Edit Timeslots' : 'Set Up Timeslots');

    /** Slots a day will produce: one per field per interval. Pools do not multiply this. */
    slotsForDay(day: DaySetupRow): number {
        return day.fieldIds.length * day.slotsPerField;
    }

    readonly setupTotalSlots = computed(() =>
        this.setupDays().reduce((sum, d) => sum + this.slotsForDay(d), 0));

    // ── Computed: agegroups available for clone targets ──
    readonly cloneableAgegroups = computed(() => {
        const sel = this.selectedAgegroup();
        return this.agegroups().filter(ag => ag.agegroupId !== sel?.agegroupId);
    });

    /** Clone target as dot + name, the same picker Standings and Brackets use. */
    readonly cloneTargetItems = computed<AgePickerItem[]>(() =>
        this.cloneableAgegroups().map(ag => ({
            id: ag.agegroupId,
            label: ag.agegroupName ?? '',
            color: ag.color ?? null,
            count: this.poolCountOf(ag)
        })));

    // ── Computed: round validation warnings ──
    readonly roundWarnings = computed(() => {
        const allDates = this.dates();
        if (allDates.length < 2) return [];

        const agDates = allDates.filter(d => !d.divId);
        const byRound = new Map<number, string[]>();
        for (const d of agDates) {
            const dateStr = d.gDate.substring(0, 10);
            const existing = byRound.get(d.rnd) ?? [];
            if (!existing.includes(dateStr)) {
                existing.push(dateStr);
            }
            byRound.set(d.rnd, existing);
        }

        const warnings: string[] = [];
        for (const [rnd, dates] of byRound) {
            if (dates.length > 1) {
                warnings.push(
                    `Round ${rnd} is assigned to ${dates.length} different dates. ` +
                    `Each calendar date should have a unique first round number.`
                );
            }
        }

        const uniqueRounds = new Set(agDates.map(d => d.rnd));
        const uniqueDates = new Set(agDates.map(d => d.gDate.substring(0, 10)));
        if (uniqueDates.size > 1 && uniqueRounds.size === 1) {
            warnings.push(
                `All ${uniqueDates.size} dates share Round ${agDates[0].rnd}. ` +
                `Each day should have a different first round (e.g., Day 1 = Rnd 1, Day 2 = Rnd 2).`
            );
        }

        return warnings;
    });

    // ── Computed: suggested next round number ──
    readonly suggestedNextRnd = computed(() => {
        const allDates = this.dates();
        if (allDates.length === 0) return 1;
        return Math.max(...allDates.map(d => d.rnd)) + 1;
    });

    // ── Computed: capacity summary ──
    readonly capacityTotalSlots = computed(() =>
        this.capacityPreview().reduce((sum, c) => sum + c.totalGameSlots, 0));

    readonly capacityTotalNeeded = computed(() =>
        this.capacityPreview().reduce((sum, c) => sum + c.gamesNeeded, 0));

    readonly capacityAllSufficient = computed(() =>
        this.capacityPreview().length > 0 && this.capacityPreview().every(c => c.isSufficient));

    readonly capacityShortDays = computed(() =>
        this.capacityPreview().filter(c => !c.isSufficient));

    ngOnInit(): void {
        this.loadAgegroups();

        // The event's whole field inventory, so "Add" can offer fields this agegroup does not
        // use yet. The configuration call only ever returns fields already assigned.
        this.svc.getReadiness().subscribe({
            next: (r) => this.eventFields.set(r.eventFields ?? [])
        });
    }

    // ── Navigator ──

    loadAgegroups(): void {
        this.isNavLoading.set(true);
        this.pairingSvc.getAgegroups().subscribe({
            next: (data) => {
                const filtered = data
                    .filter(ag => {
                        const name = (ag.agegroupName ?? '').toUpperCase();
                        return name !== 'DROPPED TEAMS' && !name.startsWith('WAITLIST');
                    })
                    .sort((a, b) => (a.agegroupName ?? '').localeCompare(b.agegroupName ?? ''));
                this.agegroups.set(filtered);
                this.isNavLoading.set(false);
                if (filtered.length > 0 && !this.selectedAgegroup()) {
                    this.selectAgegroup(filtered[0]);
                }
            },
            error: () => this.isNavLoading.set(false)
        });
    }

    selectAgegroup(ag: AgegroupWithDivisionsDto): void {
        this.selectedAgegroup.set(ag);
        this.capacityPreview.set([]);
        this.activeTab.set('fields');
        this.loadConfiguration(ag.agegroupId);
    }

    loadConfiguration(agegroupId: string): void {
        this.isLoading.set(true);
        this.svc.getConfiguration(agegroupId).subscribe({
            next: (config) => {
                this.dates.set(config.dates);
                this.fields.set(config.fields);
                this.isLoading.set(false);
            },
            error: () => this.isLoading.set(false)
        });
    }

    setTab(tab: 'fields' | 'capacity'): void {
        this.activeTab.set(tab);
        if (tab === 'capacity') {
            this.loadCapacity();
        }
    }

    loadCapacity(): void {
        const ag = this.selectedAgegroup();
        if (!ag) return;
        this.svc.getCapacityPreview(ag.agegroupId).subscribe({
            next: (data) => this.capacityPreview.set(data)
        });
    }

    // ── Date Modal ──

    openAddDateModal(): void {
        this.dateModalMode.set('add');
        this.dateModalDate.set('');
        this.dateModalRnd.set(this.suggestedNextRnd());
        this.dateModalAi.set(null);
        this.showDateModal.set(true);
    }

    openEditDateModal(date: TimeslotDateDto): void {
        this.dateModalMode.set('edit');
        this.dateModalDate.set(this.toDateInput(date.gDate));
        this.dateModalRnd.set(date.rnd);
        this.dateModalAi.set(date.ai);
        this.showDateModal.set(true);
    }

    closeDateModal(): void {
        this.showDateModal.set(false);
        this.isSavingDate.set(false);
    }

    saveDateModal(): void {
        const ag = this.selectedAgegroup();
        if (!ag || !this.dateModalDate()) return;

        this.isSavingDate.set(true);

        if (this.dateModalMode() === 'add') {
            this.svc.addDate({
                agegroupId: ag.agegroupId,
                gDate: this.dateModalDate(),
                rnd: this.dateModalRnd()
            }).subscribe({
                next: (newDate) => {
                    this.dates.update(curr => [...curr, newDate]
                        .sort((a, b) => new Date(a.gDate).getTime() - new Date(b.gDate).getTime()));
                    this.closeDateModal();
                },
                error: () => this.isSavingDate.set(false)
            });
        } else {
            const ai = this.dateModalAi()!;
            this.svc.editDate({
                ai,
                gDate: this.dateModalDate(),
                rnd: this.dateModalRnd()
            }).subscribe({
                next: () => {
                    this.dates.update(curr => curr.map(d =>
                        d.ai === ai
                            ? { ...d, gDate: this.dateModalDate(), rnd: this.dateModalRnd() }
                            : d
                    ).sort((a, b) => new Date(a.gDate).getTime() - new Date(b.gDate).getTime()));
                    this.closeDateModal();
                },
                error: () => this.isSavingDate.set(false)
            });
        }
    }

    // ── Date Clone Actions ──

    cloneDateDay(ai: number): void {
        this.svc.cloneDateRecord({ ai, cloneType: 'day' }).subscribe({
            next: (newDate) => {
                this.dates.update(curr => [...curr, newDate]
                    .sort((a, b) => new Date(a.gDate).getTime() - new Date(b.gDate).getTime()));
            }
        });
    }

    cloneDateWeek(ai: number): void {
        this.svc.cloneDateRecord({ ai, cloneType: 'week' }).subscribe({
            next: (newDate) => {
                this.dates.update(curr => [...curr, newDate]
                    .sort((a, b) => new Date(a.gDate).getTime() - new Date(b.gDate).getTime()));
            }
        });
    }

    cloneDateRound(ai: number): void {
        this.svc.cloneDateRecord({ ai, cloneType: 'round' }).subscribe({
            next: (newDate) => {
                this.dates.update(curr => [...curr, newDate]
                    .sort((a, b) => new Date(a.gDate).getTime() - new Date(b.gDate).getTime()));
            }
        });
    }

    deleteDate(ai: number): void {
        this.svc.deleteDate(ai).subscribe({
            next: () => this.dates.update(curr => curr.filter(d => d.ai !== ai))
        });
    }

    // ── Delete All Dates ──

    deleteAllDates(): void {
        const ag = this.selectedAgegroup();
        if (!ag) return;

        this.isDeletingAllDates.set(true);
        this.showDeleteDatesConfirm.set(false);
        this.svc.deleteAllDates(ag.agegroupId).subscribe({
            next: () => {
                this.dates.set([]);
                this.isDeletingAllDates.set(false);
            },
            error: () => this.isDeletingAllDates.set(false)
        });
    }

    // ── Timeslot setup dialog ──

    /**
     * Opens with one row per play day, prefilled from what exists. This replaced an "Add All"
     * that pre-answered every question with Saturday / 08:00 / 60 / 6 — an agegroup playing
     * Friday and Saturday silently got half its slots and no warning.
     */
    openTimeslotSetup(): void {
        const allFieldIds = this.eventFields().map(f => f.fieldId);
        const existing = this.fields();

        this.setupDays.set(this.playDays().map(dow => {
            const rows = existing.filter(f => f.dow === dow);
            if (rows.length === 0) {
                // Unconfigured day: seed from a day already set up, so a second day matches the
                // first by default instead of resetting the user to house defaults.
                const template = existing[0];
                return {
                    dow,
                    fieldIds: allFieldIds,
                    startTime: template ? this.normalizeTime(template.startTime) : '08:00',
                    gamestartInterval: template?.gamestartInterval ?? 60,
                    slotsPerField: template?.maxGamesPerField ?? 6
                } satisfies DaySetupRow;
            }

            return {
                dow,
                fieldIds: Array.from(new Set(rows.map(r => r.fieldId))),
                startTime: this.normalizeTime(rows[0].startTime),
                gamestartInterval: rows[0].gamestartInterval,
                slotsPerField: rows[0].maxGamesPerField
            } satisfies DaySetupRow;
        }));

        this.setupExpandedDow.set(null);
        this.showSetupModal.set(true);
    }

    closeTimeslotSetup(): void {
        this.showSetupModal.set(false);
        this.isSavingSetup.set(false);
    }

    /** Signals are replaced, never mutated — a spliced array would not re-render the totals. */
    private patchSetupDay(dow: string, patch: Partial<DaySetupRow>): void {
        this.setupDays.update(days =>
            days.map(d => d.dow === dow ? { ...d, ...patch } : d));
    }

    setDayStartTime(dow: string, value: string): void {
        this.patchSetupDay(dow, { startTime: value });
    }

    setDayInterval(dow: string, value: number): void {
        this.patchSetupDay(dow, { gamestartInterval: Number(value) || 0 });
    }

    setDaySlots(dow: string, value: number): void {
        this.patchSetupDay(dow, { slotsPerField: Number(value) || 0 });
    }

    toggleSetupFieldPicker(dow: string): void {
        this.setupExpandedDow.update(curr => curr === dow ? null : dow);
    }

    toggleDayField(dow: string, fieldId: string): void {
        const day = this.setupDays().find(d => d.dow === dow);
        if (!day) return;
        const fieldIds = day.fieldIds.includes(fieldId)
            ? day.fieldIds.filter(f => f !== fieldId)
            : [...day.fieldIds, fieldId];
        this.patchSetupDay(dow, { fieldIds });
    }

    setAllDayFields(dow: string, all: boolean): void {
        this.patchSetupDay(dow, {
            fieldIds: all ? this.eventFields().map(f => f.fieldId) : []
        });
    }

    dayHasField(day: DaySetupRow, fieldId: string): boolean {
        return day.fieldIds.includes(fieldId);
    }

    /** "All 31 fields" / "8 of 31 fields" / "No fields" — the trigger's own summary. */
    fieldSummary(day: DaySetupRow): string {
        const total = this.eventFields().length;
        if (day.fieldIds.length === 0) return 'No fields';
        if (day.fieldIds.length >= total) return `All ${total} fields`;
        return `${day.fieldIds.length} of ${total} fields`;
    }

    saveTimeslotSetup(): void {
        const ag = this.selectedAgegroup();
        if (!ag) return;

        this.isSavingSetup.set(true);
        this.svc.saveTimeslotSetup({
            agegroupId: ag.agegroupId,
            days: this.setupDays().map(d => ({
                dow: d.dow,
                fieldIds: d.fieldIds,
                startTime: d.startTime,
                gamestartInterval: d.gamestartInterval,
                slotsPerField: d.slotsPerField
            }))
        }).subscribe({
            next: () => {
                this.showSetupModal.set(false);
                this.isSavingSetup.set(false);
                this.loadConfiguration(ag.agegroupId);
            },
            error: () => this.isSavingSetup.set(false)
        });
    }

    /** Stored times can be "08:00 AM"; <input type="time"> only accepts "HH:mm". */
    private normalizeTime(value: string): string {
        if (/^\d{2}:\d{2}$/.test(value)) return value;
        const parsed = new Date(`1970-01-01 ${value}`);
        if (isNaN(parsed.getTime())) return '08:00';
        return `${String(parsed.getHours()).padStart(2, '0')}:${String(parsed.getMinutes()).padStart(2, '0')}`;
    }

    /** "All 6 pools" / "Blush, Coral" — the POOLS cell of a folded row. */
    poolLabel(group: FieldGroup): string {
        const total = this.schedulablePools().length;
        if (group.allPools) return total > 0 ? `All ${total} pools` : 'All pools';
        return group.poolNames.join(', ');
    }

    // ── Delete All Fields ──

    deleteAllFields(): void {
        const ag = this.selectedAgegroup();
        if (!ag) return;

        this.isDeletingAllFields.set(true);
        this.showDeleteFieldsConfirm.set(false);
        this.svc.deleteAllFieldTimeslots(ag.agegroupId).subscribe({
            next: () => {
                this.fields.set([]);
                this.isDeletingAllFields.set(false);
            },
            error: () => this.isDeletingAllFields.set(false)
        });
    }

    // ── Agegroup-level cloning ──

    /** Name of the currently picked clone target, for confirm copy. */
    private cloneTargetAgName(): string {
        return this.cloneTargetItems().find(i => i.id === this.cloneTargetAgId())?.label ?? 'the target';
    }

    /**
     * Both agegroup clones replace the target, and the target's contents are off-screen — the
     * user is looking at the source. So we read the target first and say exactly what is there
     * before touching it. One extra GET buys a confirmation that can name a number.
     */
    cloneDatesToAgegroup(): void {
        const source = this.selectedAgegroup();
        const targetId = this.cloneTargetAgId();
        if (!source || !targetId) return;

        const name = this.cloneTargetAgName();
        this.isCloningDates.set(true);
        this.svc.getConfiguration(targetId).subscribe({
            next: (cfg) => {
                const existing = cfg.dates?.length ?? 0;
                if (existing === 0) {
                    this.runCloneDatesToAgegroup();
                    return;
                }
                this.isCloningDates.set(false);
                this.pendingClone.set({
                    kind: 'ag-dates',
                    title: `Replace ${name}'s game dates?`,
                    message: `<strong>${name}</strong> already has <strong>${existing}</strong> `
                        + `game date${existing === 1 ? '' : 's'}. Cloning removes them and puts `
                        + `<strong>${this.dates().length}</strong> from `
                        + `<strong>${source.agegroupName}</strong> in their place.`
                });
            },
            error: () => this.isCloningDates.set(false)
        });
    }

    private runCloneDatesToAgegroup(): void {
        const source = this.selectedAgegroup();
        const targetId = this.cloneTargetAgId();
        if (!source || !targetId) return;

        this.isCloningDates.set(true);
        this.svc.cloneDates({
            sourceAgegroupId: source.agegroupId,
            targetAgegroupId: targetId
        }).subscribe({
            next: () => this.isCloningDates.set(false),
            error: () => this.isCloningDates.set(false)
        });
    }

    cloneFieldsToAgegroup(): void {
        const source = this.selectedAgegroup();
        const targetId = this.cloneTargetAgId();
        if (!source || !targetId) return;

        const name = this.cloneTargetAgName();
        this.isCloningFields.set(true);
        this.svc.getConfiguration(targetId).subscribe({
            next: (cfg) => {
                const existing = cfg.fields?.length ?? 0;
                if (existing === 0) {
                    this.runCloneFieldsToAgegroup();
                    return;
                }
                this.isCloningFields.set(false);
                this.pendingClone.set({
                    kind: 'ag-fields',
                    title: `Replace ${name}'s timeslots?`,
                    message: `<strong>${name}</strong> already has <strong>${existing}</strong> `
                        + `field timeslot${existing === 1 ? '' : 's'}. Cloning removes them and puts `
                        + `<strong>${this.fields().length}</strong> from `
                        + `<strong>${source.agegroupName}</strong> in their place.`
                });
            },
            error: () => this.isCloningFields.set(false)
        });
    }

    private runCloneFieldsToAgegroup(): void {
        const source = this.selectedAgegroup();
        const targetId = this.cloneTargetAgId();
        if (!source || !targetId) return;

        this.isCloningFields.set(true);
        this.svc.cloneFields({
            sourceAgegroupId: source.agegroupId,
            targetAgegroupId: targetId
        }).subscribe({
            next: () => this.isCloningFields.set(false),
            error: () => this.isCloningFields.set(false)
        });
    }

    // ── Confirmation for anything that overwrites ──

    confirmPendingClone(): void {
        const pending = this.pendingClone();
        this.pendingClone.set(null);
        if (!pending) return;

        switch (pending.kind) {
            case 'ag-dates': this.runCloneDatesToAgegroup(); break;
            case 'ag-fields': this.runCloneFieldsToAgegroup(); break;
        }
    }

    // ── Helpers ──

    dayOfWeek(dateStr: string): string {
        const d = new Date(dateStr + (dateStr.includes('T') ? '' : 'T12:00:00'));
        return ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][d.getDay()];
    }

    formatDate(dateStr: string): string {
        const d = new Date(dateStr + (dateStr.includes('T') ? '' : 'T12:00:00'));
        return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    }

    toDateInput(dateStr: string): string {
        return dateStr.substring(0, 10);
    }
}
