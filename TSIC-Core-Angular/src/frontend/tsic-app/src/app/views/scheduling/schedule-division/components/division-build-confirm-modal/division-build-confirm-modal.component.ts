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

/** Format minutes-from-midnight as "h:mm AM/PM". */
function formatTimeOption(totalMinutes: number): string {
    const h = Math.floor(totalMinutes / 60);
    const m = totalMinutes % 60;
    const ampm = h >= 12 ? 'PM' : 'AM';
    const h12 = h > 12 ? h - 12 : h === 0 ? 12 : h;
    return `${h12}:${m.toString().padStart(2, '0')} ${ampm}`;
}

/** Generate time options from 7:00 AM to 7:00 PM in 30-min steps. */
const TIME_OPTIONS: string[] = Array.from({ length: 25 }, (_, i) => formatTimeOption(420 + i * 30));

/**
 * Normalize a start time string from the DB (e.g. "08:00", "8:00 AM", "1/1/0001 8:00:00 AM")
 * into "h:mm AM/PM" format matching TIME_OPTIONS.
 */
function normalizeStartTime(raw: string): string {
    if (!raw) return '8:00 AM';
    // Try to parse with Date — handles many formats including "1/1/0001 8:00:00 AM"
    const d = new Date(`2000-01-01 ${raw.replace(/^.*?\s(\d)/, '$1')}`);
    if (!isNaN(d.getTime())) {
        const h = d.getHours();
        const m = d.getMinutes();
        const ampm = h >= 12 ? 'PM' : 'AM';
        const h12 = h > 12 ? h - 12 : h === 0 ? 12 : h;
        return `${h12}:${m.toString().padStart(2, '0')} ${ampm}`;
    }
    // If already in "h:mm AM/PM" format, return as-is
    if (/^\d{1,2}:\d{2}\s?(AM|PM)$/i.test(raw.trim())) return raw.trim();
    return '8:00 AM';
}

/** Ensure the DB value is in the options list. */
function ensureTimeInOptions(options: string[], time: string): string[] {
    if (options.includes(time)) return options;
    // Insert in sorted position
    const allTimes = [...options, time];
    allTimes.sort((a, b) => {
        const toMin = (t: string) => {
            const m = t.match(/(\d+):(\d+)\s?(AM|PM)/i);
            if (!m) return 0;
            let h = parseInt(m[1]);
            if (m[3].toUpperCase() === 'PM' && h !== 12) h += 12;
            if (m[3].toUpperCase() === 'AM' && h === 12) h = 0;
            return h * 60 + parseInt(m[2]);
        };
        return toMin(a) - toMin(b);
    });
    return allTimes;
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

    // ── Constants for template ──
    readonly timeOptions = signal<string[]>(TIME_OPTIONS);

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
        // Pre-populate from defaults
        this.selectedFieldIds.set(new Set(this.availableFields().map(f => f.fieldId)));
        const normalized = normalizeStartTime(this.defaultStartTime());
        this.normalizedDefault = normalized;
        this.startTime.set(normalized);
        this.timeOptions.set(ensureTimeInOptions(TIME_OPTIONS, normalized));
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
