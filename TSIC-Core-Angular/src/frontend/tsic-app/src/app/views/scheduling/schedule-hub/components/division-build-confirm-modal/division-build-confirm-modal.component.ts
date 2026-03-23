import { Component, ChangeDetectionStrategy, input, output, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

/** A field available for scheduling. */
export interface FieldOption {
    fieldId: string;
    fieldName: string;
}

/** One-shot overrides emitted when the user confirms the build. */
export interface DivisionBuildOverrides {
    /** null = all fields (no filter). */
    selectedFieldIds: string[] | null;
    /** null = use DB config. */
    overrideStartTime: string | null;
    placement: 'H' | 'V';
    brr: number;
}


@Component({
    selector: 'app-division-build-confirm-modal',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent],
    templateUrl: './division-build-confirm-modal.component.html',
    styleUrl: './division-build-confirm-modal.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class DivisionBuildConfirmModalComponent implements OnInit {
    // ── Inputs ──
    readonly divisionName = input.required<string>();
    readonly agegroupName = input.required<string>();
    readonly availableFields = input.required<FieldOption[]>();
    readonly availableTimeOptions = input.required<string[]>();
    readonly defaultStartTime = input.required<string>();
    readonly defaultPlacement = input.required<'H' | 'V'>();
    readonly defaultBrr = input.required<number>();
    readonly hasExistingGames = input(false);

    // ── Outputs ──
    readonly buildRequested = output<DivisionBuildOverrides>();
    readonly cancelled = output<void>();

    // ── Editable state ──
    readonly selectedFieldIds = signal<Set<string>>(new Set());
    readonly startTime = signal('8:00 AM');
    readonly placement = signal<'H' | 'V'>('H');
    readonly brr = signal<number>(1);

    // ── Computed ──

    readonly selectedFieldNames = computed(() => {
        const ids = this.selectedFieldIds();
        return this.availableFields().filter(f => ids.has(f.fieldId)).map(f => f.fieldName);
    });

    readonly allFieldsSelected = computed(() =>
        this.selectedFieldIds().size === this.availableFields().length && this.availableFields().length > 0
    );

    readonly placementLabel = computed(() => this.placement() === 'H' ? 'horizontally' : 'vertically');

    readonly brrLabel = computed(() => {
        const v = this.brr();
        if (v === 0) return 'no skip rows';
        if (v === 1) return '1 skip row';
        return `${v} skip rows`;
    });

    readonly summaryText = computed(() => {
        const fields = this.selectedFieldNames();
        const fieldStr = fields.length === 0
            ? 'no fields'
            : fields.length <= 4
                ? fields.join(', ')
                : `${fields.slice(0, 3).join(', ')} + ${fields.length - 3} more`;

        return `I'll schedule ${this.agegroupName()}:${this.divisionName()} for you now using `
            + `Fields ${fieldStr} starting at ${this.startTime()} using `
            + `${this.brrLabel()} between rounds and scheduling ${this.placementLabel()}.`;
    });

    readonly canBuild = computed(() => this.selectedFieldIds().size > 0);

    /** True if any value differs from the cascade/config defaults. */
    /** Normalized version of the default start time (for change detection). */
    private normalizedDefault = '';

    readonly hasChanges = computed(() => {
        const allIds = new Set(this.availableFields().map(f => f.fieldId));
        const selIds = this.selectedFieldIds();
        const fieldsChanged = selIds.size !== allIds.size || ![...selIds].every(id => allIds.has(id));
        return fieldsChanged
            || this.startTime() !== this.normalizedDefault
            || this.placement() !== this.defaultPlacement()
            || this.brr() !== this.defaultBrr();
    });

    ngOnInit(): void {
        this.selectedFieldIds.set(new Set(this.availableFields().map(f => f.fieldId)));
        this.normalizedDefault = this.defaultStartTime();
        this.startTime.set(this.defaultStartTime());
        this.placement.set(this.defaultPlacement());
        this.brr.set(this.defaultBrr());
    }

    // ── Actions ──

    toggleField(fieldId: string): void {
        const current = new Set(this.selectedFieldIds());
        if (current.has(fieldId)) {
            current.delete(fieldId);
        } else {
            current.add(fieldId);
        }
        this.selectedFieldIds.set(current);
    }

    toggleAllFields(): void {
        if (this.allFieldsSelected()) {
            this.selectedFieldIds.set(new Set());
        } else {
            this.selectedFieldIds.set(new Set(this.availableFields().map(f => f.fieldId)));
        }
    }

    onStartTimeChange(value: string): void {
        this.startTime.set(value);
    }

    onPlacementChange(value: 'H' | 'V'): void {
        this.placement.set(value);
    }

    onBrrChange(value: number): void {
        this.brr.set(value);
    }

    onBuild(): void {
        const allIds = new Set(this.availableFields().map(f => f.fieldId));
        const selIds = this.selectedFieldIds();
        const allSelected = selIds.size === allIds.size && [...selIds].every(id => allIds.has(id));

        this.buildRequested.emit({
            selectedFieldIds: allSelected ? null : [...selIds],
            overrideStartTime: this.startTime() !== this.normalizedDefault ? this.startTime() : null,
            placement: this.placement(),
            brr: this.brr(),
        });
    }
}
