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
import type { AgegroupWithDivisionsDto, DivisionSummaryDto } from '@core/api';

const DAYS_OF_WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

@Component({
    selector: 'app-manage-timeslots',
    standalone: true,
    imports: [CommonModule, FormsModule],
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

    // ── Add Date form ──
    readonly newDateValue = signal('');
    readonly newDateRnd = signal(1);
    readonly isAddingDate = signal(false);

    // ── Add Field Timeslot form ──
    readonly newFieldDow = signal('Saturday');
    readonly newFieldStartTime = signal('08:00');
    readonly newFieldInterval = signal(60);
    readonly newFieldMaxGames = signal(6);
    readonly isAddingField = signal(false);

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

    // ── Delete All ──
    readonly showDeleteDatesConfirm = signal(false);
    readonly showDeleteFieldsConfirm = signal(false);
    readonly isDeletingAllDates = signal(false);
    readonly isDeletingAllFields = signal(false);

    // ── Inline editing ──
    readonly editingDateAi = signal<number | null>(null);
    readonly editingFieldAi = signal<number | null>(null);

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
        this.editingDateAi.set(null);
        this.editingFieldAi.set(null);
        this.activeTab.set('dates');
        this.capacityPreview.set([]);
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

    // ── Add Date ──

    addDate(): void {
        const ag = this.selectedAgegroup();
        if (!ag || !this.newDateValue()) return;

        this.isAddingDate.set(true);
        this.svc.addDate({
            agegroupId: ag.agegroupId,
            gDate: this.newDateValue(),
            rnd: this.newDateRnd()
        }).subscribe({
            next: (newDate) => {
                this.dates.update(curr => [...curr, newDate]
                    .sort((a, b) => new Date(a.gDate).getTime() - new Date(b.gDate).getTime()));
                this.isAddingDate.set(false);
            },
            error: () => this.isAddingDate.set(false)
        });
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

    confirmDeleteAllDates(): void {
        this.showDeleteDatesConfirm.set(true);
    }

    cancelDeleteAllDates(): void {
        this.showDeleteDatesConfirm.set(false);
    }

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

    // ── Add Field Timeslot ──

    addFieldTimeslot(): void {
        const ag = this.selectedAgegroup();
        if (!ag) return;

        this.isAddingField.set(true);
        this.svc.addFieldTimeslot({
            agegroupId: ag.agegroupId,
            startTime: this.newFieldStartTime(),
            gamestartInterval: this.newFieldInterval(),
            maxGamesPerField: this.newFieldMaxGames(),
            dow: this.newFieldDow()
        }).subscribe({
            next: (newFields) => {
                this.fields.update(curr => [...curr, ...newFields]);
                this.isAddingField.set(false);
            },
            error: () => this.isAddingField.set(false)
        });
    }

    deleteFieldTimeslot(ai: number): void {
        this.svc.deleteFieldTimeslot(ai).subscribe({
            next: () => this.fields.update(curr => curr.filter(f => f.ai !== ai))
        });
    }

    // ── Delete All Fields ──

    confirmDeleteAllFields(): void {
        this.showDeleteFieldsConfirm.set(true);
    }

    cancelDeleteAllFields(): void {
        this.showDeleteFieldsConfirm.set(false);
    }

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

    // ── Inline editing: dates ──

    startEditDate(ai: number): void {
        this.editingDateAi.set(ai);
    }

    cancelEditDate(): void {
        this.editingDateAi.set(null);
        const ag = this.selectedAgegroup();
        if (ag) this.loadConfiguration(ag.agegroupId);
    }

    saveEditDate(date: TimeslotDateDto): void {
        this.svc.editDate({
            ai: date.ai,
            gDate: date.gDate,
            rnd: date.rnd
        }).subscribe({
            next: () => this.editingDateAi.set(null)
        });
    }

    // ── Inline editing: fields ──

    startEditField(ai: number): void {
        this.editingFieldAi.set(ai);
    }

    cancelEditField(): void {
        this.editingFieldAi.set(null);
        const ag = this.selectedAgegroup();
        if (ag) this.loadConfiguration(ag.agegroupId);
    }

    saveEditField(field: TimeslotFieldDto): void {
        this.svc.editFieldTimeslot({
            ai: field.ai,
            startTime: field.startTime,
            gamestartInterval: field.gamestartInterval,
            maxGamesPerField: field.maxGamesPerField,
            dow: field.dow
        }).subscribe({
            next: () => this.editingFieldAi.set(null)
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
