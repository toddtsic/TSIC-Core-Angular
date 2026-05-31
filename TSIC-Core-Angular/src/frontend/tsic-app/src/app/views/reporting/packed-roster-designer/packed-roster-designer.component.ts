import {
    ChangeDetectionStrategy, Component, computed, inject, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ToastService } from '@shared-ui/toast.service';
import { PackedRosterService } from '@infrastructure/services/packed-roster.service';
import { ReportingService } from '@infrastructure/services/reporting.service';
import type { PackedRosterFieldDto, PackedRosterRequestDto } from '@core/api';

/** Working column model — a chosen field plus its placement/render options. */
interface DesignerColumn {
    key: string;
    label: string;
    widthWeight: number;
    align: string;
    supportsLongText: boolean;
    longText: 'Truncate' | 'Wrap';
    truncateAt: number | null;
}

/**
 * PackedRoster Designer — director-built replacement for the canned "Tournament Roster
 * Packed" Bold RDLs. Pick + order + size player columns, toggle the card chrome, then
 * generate the PDF in-process. The two retired RDLs survive as the starter presets.
 */
@Component({
    selector: 'app-packed-roster-designer',
    standalone: true,
    imports: [CommonModule, DragDropModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './packed-roster-designer.component.html',
    styleUrl: './packed-roster-designer.component.scss',
})
export class PackedRosterDesignerComponent implements OnInit {
    private readonly packedSvc = inject(PackedRosterService);
    private readonly reportingSvc = inject(ReportingService);
    private readonly toast = inject(ToastService);

    readonly availableFields = signal<PackedRosterFieldDto[]>([]);
    readonly selectedColumns = signal<DesignerColumn[]>([]);
    readonly nUp = signal<2 | 3>(3);

    // Card chrome toggles
    readonly showCoaches = signal(true);
    readonly showRepName = signal(true);
    readonly showRepEmail = signal(true);
    readonly showRepPhone = signal(true);
    readonly schoolShowsCommit = signal(false);

    readonly isLoading = signal(true);
    readonly isGenerating = signal(false);

    readonly canGenerate = computed(() => this.selectedColumns().length > 0 && !this.isGenerating());
    readonly selectedKeys = computed(() => new Set(this.selectedColumns().map(c => c.key)));

    ngOnInit(): void {
        this.packedSvc.getFields().subscribe({
            next: (fields) => {
                this.availableFields.set(fields);
                this.applyClassic3Up(); // sensible default + proves the picker
                this.isLoading.set(false);
            },
            error: () => {
                this.isLoading.set(false);
                this.toast.show('Failed to load packed-roster fields', 'danger');
            },
        });
    }

    // ── Field selection ──

    isSelected(key: string): boolean {
        return this.selectedKeys().has(key);
    }

    toggleField(field: PackedRosterFieldDto): void {
        const cols = this.selectedColumns();
        if (cols.some(c => c.key === field.key)) {
            this.selectedColumns.set(cols.filter(c => c.key !== field.key));
        } else {
            this.selectedColumns.set([...cols, this.buildColumn(field.key)]);
        }
    }

    updateColumn(key: string, patch: Partial<DesignerColumn>): void {
        this.selectedColumns.set(
            this.selectedColumns().map(c => (c.key === key ? { ...c, ...patch } : c)),
        );
    }

    onDropColumn(event: CdkDragDrop<DesignerColumn[]>): void {
        if (event.previousIndex === event.currentIndex) return;
        const cols = this.selectedColumns().slice();
        moveItemInArray(cols, event.previousIndex, event.currentIndex);
        this.selectedColumns.set(cols);
    }

    // ── Starter presets (the two retired RDLs) ──

    applyClassic3Up(): void {
        this.nUp.set(3);
        this.selectedColumns.set([
            this.buildColumn('uniform_no'),
            this.buildColumn('player'),
            this.buildColumn('position'),
            this.buildColumn('school_name', { longText: 'Truncate', truncateAt: 14 }),
        ]);
        this.showCoaches.set(true);
        this.showRepName.set(true);
        this.showRepEmail.set(true);
        this.showRepPhone.set(true);
        this.schoolShowsCommit.set(false);
    }

    applyCollegeCommit2Up(): void {
        this.nUp.set(2);
        this.selectedColumns.set([
            this.buildColumn('uniform_no'),
            this.buildColumn('player'),
            this.buildColumn('position'),
            this.buildColumn('gradYear'),
            this.buildColumn('gpa'),
            this.buildColumn('collegeCommit', { longText: 'Wrap' }),
        ]);
        this.showCoaches.set(false);
        this.showRepName.set(true);
        this.showRepEmail.set(true);
        this.showRepPhone.set(true);
        this.schoolShowsCommit.set(false);
    }

    // ── Generate ──

    generate(): void {
        if (!this.canGenerate()) return;

        const request: PackedRosterRequestDto = {
            nUp: this.nUp(),
            columns: this.selectedColumns().map(c => ({
                key: c.key,
                widthWeight: c.widthWeight,
                align: c.align,
                longText: c.supportsLongText ? c.longText : 'Truncate',
                truncateAt: c.supportsLongText && c.longText === 'Truncate' ? c.truncateAt : null,
            })),
            showCoaches: this.showCoaches(),
            showRepName: this.showRepName(),
            showRepEmail: this.showRepEmail(),
            showRepPhone: this.showRepPhone(),
            schoolShowsCommit: this.schoolShowsCommit(),
        };

        this.isGenerating.set(true);
        this.packedSvc.generate(request).subscribe({
            next: (response) => {
                this.reportingSvc.triggerDownload(response, 'PackedRoster');
                this.isGenerating.set(false);
            },
            error: () => {
                this.isGenerating.set(false);
                this.toast.show('Failed to generate packed roster', 'danger');
            },
        });
    }

    // ── Helpers ──

    setNUp(n: 2 | 3): void {
        this.nUp.set(n);
    }

    setChecked(setter: (v: boolean) => void, event: Event): void {
        setter((event.target as HTMLInputElement).checked);
    }

    private buildColumn(key: string, overrides: Partial<DesignerColumn> = {}): DesignerColumn {
        const f = this.availableFields().find(x => x.key === key);
        return {
            key,
            label: f?.label ?? key,
            widthWeight: f?.defaultWidthWeight ?? 30,
            align: f?.defaultAlign ?? 'Left',
            supportsLongText: f?.supportsLongText ?? false,
            longText: f?.supportsLongText ? 'Wrap' : 'Truncate',
            truncateAt: 14,
            ...overrides,
        };
    }
}
