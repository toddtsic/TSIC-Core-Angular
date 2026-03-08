/**
 * FieldConfigSectionComponent — Stepper Section ① (Fields)
 *
 * Inline expandable section for per-agegroup field assignment.
 * Displays a field×agegroup checkbox matrix where each cell controls
 * whether that agegroup uses that field.
 *
 * Matrix layout: Fields as rows × Agegroups as columns.
 * (AG names are short like U8, U10 → narrow columns; field names need width.)
 */

import { Component, ChangeDetectionStrategy, computed, effect, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import type { AgegroupCanvasReadinessDto, EventFieldSummaryDto } from '@core/api';
import type { AgegroupWithDivisionsDto } from '../../services/schedule-division.service';
import type { FieldConfigApplyEvent } from './schedule-config.types';
import { contrastText } from '../../../shared/utils/scheduling-helpers';

/** 2D cell map: cellMap[fieldId][agegroupId] = checked */
type FieldCellMap = Record<string, Record<string, boolean>>;

@Component({
    selector: 'app-field-config-section',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './field-config-section.component.html',
    styleUrl: './field-config-section.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class FieldConfigSectionComponent {
    // ── Inputs ──
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});
    readonly eventFields = input<EventFieldSummaryDto[]>([]);
    readonly isExpanded = input(false);
    readonly isSaving = input(false);

    // ── Outputs ──
    readonly toggleExpanded = output<void>();
    readonly applyRequested = output<FieldConfigApplyEvent>();

    // ── Helpers ──
    readonly contrastText = contrastText;

    // ── 2D matrix editing state ──
    readonly cellMap = signal<FieldCellMap>({});

    // ── Column popover (agegroupId of open popover, null = closed) ──
    readonly popoverAgId = signal<string | null>(null);

    constructor() {
        // Sync cellMap whenever backend data changes (inputs update → initialCellMap recomputes).
        // Local checkbox edits don't trigger this because they only modify cellMap, not the inputs.
        effect(() => {
            const initial = this.initialCellMap();
            if (Object.keys(initial).length > 0) {
                this.cellMap.set(initial);
            }
        });
    }

    // ── Computed: initial cell map from readiness data ──

    readonly initialCellMap = computed((): FieldCellMap => {
        const fields = this.eventFields();
        const ags = this.agegroups();
        const map = this.readinessMap();
        const result: FieldCellMap = {};

        if (fields.length === 0 || ags.length === 0) return result;

        const allFieldIds = new Set(fields.map(f => f.fieldId));

        for (const field of fields) {
            result[field.fieldId] = {};
            for (const ag of ags) {
                const readiness = map[ag.agegroupId];
                if (readiness?.isConfigured && readiness.fieldIds?.length > 0) {
                    // Configured AG: check if this field is in its field list
                    result[field.fieldId][ag.agegroupId] = readiness.fieldIds.includes(field.fieldId);
                } else {
                    // Unconfigured AG: default to all fields checked
                    result[field.fieldId][ag.agegroupId] = true;
                }
            }
        }
        return result;
    });

    // ── Computed: summary label for collapsed view ──

    readonly summaryLabel = computed((): string => {
        const fields = this.eventFields();
        if (fields.length === 0) return 'No fields assigned';

        const ags = this.agegroups();
        const map = this.readinessMap();
        const customCount = ags.filter(ag => {
            const r = map[ag.agegroupId];
            if (!r?.isConfigured || !r.fieldIds?.length) return false;
            return r.fieldIds.length < fields.length;
        }).length;

        if (customCount === 0) {
            return `${fields.length} fields · all agegroups`;
        }
        return `${fields.length} fields · ${customCount} AG${customCount > 1 ? 's' : ''} customized`;
    });

    // ── Computed: is complete (at least 1 field exists) ──

    readonly isComplete = computed(() => this.eventFields().length > 0);

    // ── Computed: dirty state (any cell differs from initial) ──

    readonly isDirty = computed((): boolean => {
        const current = this.cellMap();
        const initial = this.initialCellMap();

        for (const fieldId of Object.keys(initial)) {
            const initialRow = initial[fieldId];
            const currentRow = current[fieldId];
            if (!currentRow) return true;

            for (const agId of Object.keys(initialRow)) {
                if (currentRow[agId] !== initialRow[agId]) return true;
            }
        }
        return false;
    });

    // ── Per-agegroup checked count (for column headers) ──

    agCheckedCount(agegroupId: string): number {
        const cells = this.cellMap();
        let count = 0;
        for (const fieldId of Object.keys(cells)) {
            if (cells[fieldId]?.[agegroupId]) count++;
        }
        return count;
    }

    // ── Cell toggle ──

    toggleCell(fieldId: string, agegroupId: string): void {
        const current = this.cellMap();
        const fieldRow = { ...(current[fieldId] ?? {}) };
        fieldRow[agegroupId] = !fieldRow[agegroupId];

        this.cellMap.set({
            ...current,
            [fieldId]: fieldRow
        });
    }

    getCellValue(fieldId: string, agegroupId: string): boolean {
        return this.cellMap()[fieldId]?.[agegroupId] ?? true;
    }

    // ── Column popover toggle ──

    togglePopover(agegroupId: string, event: Event): void {
        event.stopPropagation();
        this.popoverAgId.set(this.popoverAgId() === agegroupId ? null : agegroupId);
    }

    closePopover(): void {
        this.popoverAgId.set(null);
    }

    // ── Set entire column (all fields for one agegroup) ──

    setColumn(agegroupId: string, value: boolean): void {
        const cells = this.cellMap();
        const fields = this.eventFields();
        const updated = { ...cells };
        for (const f of fields) {
            updated[f.fieldId] = {
                ...(updated[f.fieldId] ?? {}),
                [agegroupId]: value
            };
        }
        this.cellMap.set(updated);
        this.popoverAgId.set(null);
    }

    isColumnAllChecked(agegroupId: string): boolean {
        const cells = this.cellMap();
        return this.eventFields().every(f => cells[f.fieldId]?.[agegroupId] !== false);
    }

    isColumnNoneChecked(agegroupId: string): boolean {
        const cells = this.cellMap();
        return this.eventFields().every(f => !cells[f.fieldId]?.[agegroupId]);
    }

    // ── Toggle entire row (one field for all agegroups) ──

    toggleRow(fieldId: string): void {
        const cells = this.cellMap();
        const ags = this.agegroups();
        const row = cells[fieldId] ?? {};
        const allChecked = ags.every(ag => row[ag.agegroupId] !== false);

        const updated = { ...cells };
        updated[fieldId] = { ...row };
        for (const ag of ags) {
            updated[fieldId][ag.agegroupId] = !allChecked;
        }
        this.cellMap.set(updated);
    }

    // ── Actions ──

    onToggle(): void {
        this.toggleExpanded.emit();
    }

    onCancel(): void {
        // Reset to initial state
        this.cellMap.set(this.initialCellMap());
        this.toggleExpanded.emit();
    }

    onSave(): void {
        const cells = this.cellMap();
        const fields = this.eventFields();
        const ags = this.agegroups();
        const allFieldIds = fields.map(f => f.fieldId);
        const overrides: Record<string, string[]> = {};

        for (const ag of ags) {
            const checkedFieldIds = allFieldIds.filter(fId => cells[fId]?.[ag.agegroupId] !== false);
            // Only include if fewer than all fields (= override)
            if (checkedFieldIds.length < allFieldIds.length) {
                overrides[ag.agegroupId] = checkedFieldIds;
            }
        }

        this.applyRequested.emit({ overrides });
    }
}
