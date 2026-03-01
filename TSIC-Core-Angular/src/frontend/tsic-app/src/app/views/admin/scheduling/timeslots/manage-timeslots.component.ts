import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
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
import type { AgegroupWithDivisionsDto, DivisionSummaryDto } from '@core/api';

const DAYS_OF_WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

@Component({
    selector: 'app-manage-timeslots',
    standalone: true,
    imports: [CommonModule, FormsModule, TsicDialogComponent, ConfirmDialogComponent],
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
    readonly isLoading = signal(false);

    // ── Tabs ──
    readonly activeTab = signal<'dates' | 'fields' | 'capacity'>('dates');

    // ── Date Modal ──
    readonly showDateModal = signal(false);
    readonly dateModalMode = signal<'add' | 'edit'>('add');
    readonly dateModalDate = signal('');
    readonly dateModalRnd = signal(1);
    readonly dateModalAi = signal<number | null>(null);
    readonly isSavingDate = signal(false);

    // ── Field Modal ──
    readonly showFieldModal = signal(false);
    readonly fieldModalMode = signal<'add' | 'edit'>('add');
    readonly fieldModalDow = signal('Saturday');
    readonly fieldModalStartTime = signal('08:00');
    readonly fieldModalInterval = signal(60);
    readonly fieldModalMaxGames = signal(6);
    readonly fieldModalAi = signal<number | null>(null);
    readonly fieldModalFieldName = signal('');
    readonly fieldModalDivName = signal<string | null>(null);
    readonly isSavingField = signal(false);

    // ── Clone Agegroup ──
    readonly cloneTargetAgId = signal('');
    readonly isCloningDates = signal(false);
    readonly isCloningFields = signal(false);

    // ── Clone by Field ──
    readonly cloneSourceFieldId = signal('');
    readonly cloneTargetFieldId = signal('');
    readonly isCloningByField = signal(false);

    // ── Clone by Division ──
    readonly cloneSourceDivId = signal('');
    readonly cloneTargetDivId = signal('');
    readonly isCloningByDiv = signal(false);

    // ── Clone by DOW ──
    readonly cloneSourceDow = signal('');
    readonly cloneTargetDow = signal('');
    readonly cloneDowStartTime = signal('');
    readonly isCloningByDow = signal(false);

    // ── Clone panel toggle ──
    readonly showClonePanel = signal(false);

    // ── Delete All ──
    readonly showDeleteDatesConfirm = signal(false);
    readonly showDeleteFieldsConfirm = signal(false);
    readonly isDeletingAllDates = signal(false);
    readonly isDeletingAllFields = signal(false);

    // ── Constants ──
    readonly daysOfWeek = DAYS_OF_WEEK;

    // ── Computed: unique fields from field timeslots ──
    readonly uniqueFields = computed(() => {
        const map = new Map<string, string>();
        for (const f of this.fields()) {
            map.set(f.fieldId, f.fieldName);
        }
        return Array.from(map, ([id, name]) => ({ id, name }))
            .sort((a, b) => a.name.localeCompare(b.name));
    });

    // ── Computed: divisions from selected agegroup ──
    readonly divisions = computed(() => {
        return this.selectedAgegroup()?.divisions ?? [];
    });

    // ── Computed: agegroups available for clone targets ──
    readonly cloneableAgegroups = computed(() => {
        const sel = this.selectedAgegroup();
        return this.agegroups().filter(ag => ag.agegroupId !== sel?.agegroupId);
    });

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
                    `Each calendar date should have a unique starting round so the engine knows which games go on which day.`
                );
            }
        }

        const uniqueRounds = new Set(agDates.map(d => d.rnd));
        const uniqueDates = new Set(agDates.map(d => d.gDate.substring(0, 10)));
        if (uniqueDates.size > 1 && uniqueRounds.size === 1) {
            warnings.push(
                `All ${uniqueDates.size} dates share Round ${agDates[0].rnd}. ` +
                `For multi-day events, each day needs a different starting round (e.g., Day 1 = Rnd 1, Day 2 = Rnd 2).`
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
            },
            error: () => this.isNavLoading.set(false)
        });
    }

    selectAgegroup(ag: AgegroupWithDivisionsDto): void {
        this.selectedAgegroup.set(ag);
        this.activeTab.set('dates');
        this.capacityPreview.set([]);
        this.showClonePanel.set(false);
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

    setTab(tab: 'dates' | 'fields' | 'capacity'): void {
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

    // ── Field Modal ──

    openAddFieldModal(): void {
        this.fieldModalMode.set('add');
        this.fieldModalDow.set('Saturday');
        this.fieldModalStartTime.set('08:00');
        this.fieldModalInterval.set(60);
        this.fieldModalMaxGames.set(6);
        this.fieldModalAi.set(null);
        this.fieldModalFieldName.set('');
        this.fieldModalDivName.set(null);
        this.showFieldModal.set(true);
    }

    openEditFieldModal(field: TimeslotFieldDto): void {
        this.fieldModalMode.set('edit');
        this.fieldModalDow.set(field.dow);
        this.fieldModalStartTime.set(field.startTime);
        this.fieldModalInterval.set(field.gamestartInterval);
        this.fieldModalMaxGames.set(field.maxGamesPerField);
        this.fieldModalAi.set(field.ai);
        this.fieldModalFieldName.set(field.fieldName);
        this.fieldModalDivName.set(field.divName ?? null);
        this.showFieldModal.set(true);
    }

    closeFieldModal(): void {
        this.showFieldModal.set(false);
        this.isSavingField.set(false);
    }

    saveFieldModal(): void {
        const ag = this.selectedAgegroup();
        if (!ag) return;

        this.isSavingField.set(true);

        if (this.fieldModalMode() === 'add') {
            this.svc.addFieldTimeslot({
                agegroupId: ag.agegroupId,
                startTime: this.fieldModalStartTime(),
                gamestartInterval: this.fieldModalInterval(),
                maxGamesPerField: this.fieldModalMaxGames(),
                dow: this.fieldModalDow()
            }).subscribe({
                next: (newFields) => {
                    this.fields.update(curr => [...curr, ...newFields]);
                    this.closeFieldModal();
                },
                error: () => this.isSavingField.set(false)
            });
        } else {
            const ai = this.fieldModalAi()!;
            this.svc.editFieldTimeslot({
                ai,
                startTime: this.fieldModalStartTime(),
                gamestartInterval: this.fieldModalInterval(),
                maxGamesPerField: this.fieldModalMaxGames(),
                dow: this.fieldModalDow()
            }).subscribe({
                next: () => {
                    this.fields.update(curr => curr.map(f =>
                        f.ai === ai
                            ? {
                                ...f,
                                dow: this.fieldModalDow(),
                                startTime: this.fieldModalStartTime(),
                                gamestartInterval: this.fieldModalInterval(),
                                maxGamesPerField: this.fieldModalMaxGames()
                            }
                            : f
                    ));
                    this.closeFieldModal();
                },
                error: () => this.isSavingField.set(false)
            });
        }
    }

    deleteFieldTimeslot(ai: number): void {
        this.svc.deleteFieldTimeslot(ai).subscribe({
            next: () => this.fields.update(curr => curr.filter(f => f.ai !== ai))
        });
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

    // ── Clone Field DOW (next day cycle) ──

    cloneFieldDow(ai: number): void {
        this.svc.cloneFieldDow({ ai }).subscribe({
            next: (newField) => {
                this.fields.update(curr => [...curr, newField]);
            }
        });
    }

    // ── Clone panel toggle ──

    toggleClonePanel(): void {
        this.showClonePanel.update(v => !v);
    }

    // ── Agegroup-level cloning ──

    cloneDatesToAgegroup(): void {
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

        this.isCloningFields.set(true);
        this.svc.cloneFields({
            sourceAgegroupId: source.agegroupId,
            targetAgegroupId: targetId
        }).subscribe({
            next: () => this.isCloningFields.set(false),
            error: () => this.isCloningFields.set(false)
        });
    }

    // ── Clone by Field ──

    cloneByField(): void {
        const ag = this.selectedAgegroup();
        const srcId = this.cloneSourceFieldId();
        const tgtId = this.cloneTargetFieldId();
        if (!ag || !srcId || !tgtId) return;

        this.isCloningByField.set(true);
        this.svc.cloneByField({
            agegroupId: ag.agegroupId,
            sourceFieldId: srcId,
            targetFieldId: tgtId
        }).subscribe({
            next: () => {
                this.isCloningByField.set(false);
                this.loadConfiguration(ag.agegroupId);
            },
            error: () => this.isCloningByField.set(false)
        });
    }

    // ── Clone by Division ──

    cloneByDivision(): void {
        const ag = this.selectedAgegroup();
        const srcId = this.cloneSourceDivId();
        const tgtId = this.cloneTargetDivId();
        if (!ag || !srcId || !tgtId) return;

        this.isCloningByDiv.set(true);
        this.svc.cloneByDivision({
            agegroupId: ag.agegroupId,
            sourceDivId: srcId,
            targetDivId: tgtId
        }).subscribe({
            next: () => {
                this.isCloningByDiv.set(false);
                this.loadConfiguration(ag.agegroupId);
            },
            error: () => this.isCloningByDiv.set(false)
        });
    }

    // ── Clone by DOW ──

    cloneByDow(): void {
        const ag = this.selectedAgegroup();
        const src = this.cloneSourceDow();
        const tgt = this.cloneTargetDow();
        if (!ag || !src || !tgt) return;

        this.isCloningByDow.set(true);
        this.svc.cloneByDow({
            agegroupId: ag.agegroupId,
            sourceDow: src,
            targetDow: tgt,
            newStartTime: this.cloneDowStartTime() || undefined
        }).subscribe({
            next: () => {
                this.isCloningByDow.set(false);
                this.loadConfiguration(ag.agegroupId);
            },
            error: () => this.isCloningByDow.set(false)
        });
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

    dowShort(dow: string): string {
        return dow.substring(0, 3);
    }
}
