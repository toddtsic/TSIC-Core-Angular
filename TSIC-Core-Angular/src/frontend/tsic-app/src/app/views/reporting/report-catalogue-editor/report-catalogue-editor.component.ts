import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportingService } from '@infrastructure/services/reporting.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { ReportCatalogueEntryDto, ReportCatalogueWriteDto } from '@core/api';

type VerifyStatus = 'idle' | 'checking' | 'ok' | 'missing' | 'error';

interface EditorFormState {
    reportId: string | null;  // null => new
    title: string;
    description: string;
    iconName: string;
    storedProcName: string;
    parametersJson: string;
    visibilityRules: string;
    sortOrder: number;
    active: boolean;
}

const BLANK_FORM: EditorFormState = {
    reportId: null,
    title: '',
    description: '',
    iconName: '',
    storedProcName: '',
    parametersJson: '',
    visibilityRules: '',
    sortOrder: 0,
    active: true
};

@Component({
    selector: 'app-report-catalogue-editor',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent],
    templateUrl: './report-catalogue-editor.component.html',
    styleUrl: './report-catalogue-editor.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportCatalogueEditorComponent implements OnInit {
    private readonly reportingService = inject(ReportingService);

    readonly entries = signal<ReportCatalogueEntryDto[]>([]);
    readonly loading = signal(false);
    readonly error = signal<string | null>(null);
    readonly filterText = signal('');

    readonly dialogOpen = signal(false);
    readonly form = signal<EditorFormState>({ ...BLANK_FORM });
    readonly saving = signal(false);
    readonly saveError = signal<string | null>(null);
    readonly verifyStatus = signal<VerifyStatus>('idle');

    readonly filteredEntries = computed(() => {
        const needle = this.filterText().trim().toLowerCase();
        const all = this.entries();
        if (!needle) return all;
        return all.filter(e =>
            e.title.toLowerCase().includes(needle)
            || e.storedProcName.toLowerCase().includes(needle)
            || (e.description?.toLowerCase().includes(needle) ?? false)
        );
    });

    readonly isNewEntry = computed(() => this.form().reportId === null);

    ngOnInit(): void {
        this.loadAll();
    }

    retry(): void {
        this.loadAll();
    }

    onFilterInput(value: string): void {
        this.filterText.set(value);
    }

    openNew(): void {
        const nextSort = this.nextSortOrder();
        this.form.set({ ...BLANK_FORM, sortOrder: nextSort });
        this.verifyStatus.set('idle');
        this.saveError.set(null);
        this.dialogOpen.set(true);
    }

    openEdit(entry: ReportCatalogueEntryDto): void {
        this.form.set({
            reportId: entry.reportId,
            title: entry.title,
            description: entry.description ?? '',
            iconName: entry.iconName ?? '',
            storedProcName: entry.storedProcName,
            parametersJson: entry.parametersJson ?? '',
            visibilityRules: entry.visibilityRules ?? '',
            sortOrder: entry.sortOrder,
            active: entry.active
        });
        this.verifyStatus.set('idle');
        this.saveError.set(null);
        this.dialogOpen.set(true);
    }

    closeDialog(): void {
        if (this.saving()) return;
        this.dialogOpen.set(false);
    }

    updateField<K extends keyof EditorFormState>(key: K, value: EditorFormState[K]): void {
        this.form.update(prev => ({ ...prev, [key]: value }));
        if (key === 'storedProcName') {
            this.verifyStatus.set('idle');
        }
    }

    verifySp(): void {
        const name = this.form().storedProcName.trim();
        if (!name) {
            this.verifyStatus.set('idle');
            return;
        }
        this.verifyStatus.set('checking');
        this.reportingService.verifyStoredProcedure(name).subscribe({
            next: result => this.verifyStatus.set(result.exists ? 'ok' : 'missing'),
            error: () => this.verifyStatus.set('error')
        });
    }

    save(): void {
        const f = this.form();
        if (!f.title.trim() || !f.storedProcName.trim()) {
            this.saveError.set('Title and Stored Proc Name are required.');
            return;
        }

        const dto: ReportCatalogueWriteDto = {
            title: f.title.trim(),
            description: nullIfBlank(f.description),
            iconName: nullIfBlank(f.iconName),
            storedProcName: f.storedProcName.trim(),
            parametersJson: nullIfBlank(f.parametersJson),
            visibilityRules: nullIfBlank(f.visibilityRules),
            sortOrder: f.sortOrder,
            active: f.active
        };

        this.saving.set(true);
        this.saveError.set(null);

        const request$ = f.reportId
            ? this.reportingService.updateCatalogueEntry(f.reportId, dto)
            : this.reportingService.createCatalogueEntry(dto);

        request$.subscribe({
            next: result => {
                this.saving.set(false);
                this.dialogOpen.set(false);
                this.mergeResult(result, f.reportId === null);
            },
            error: err => {
                this.saving.set(false);
                this.saveError.set(err?.error ?? 'Save failed. Please try again.');
            }
        });
    }

    confirmDelete(entry: ReportCatalogueEntryDto): void {
        if (!confirm(`Delete "${entry.title}"? This cannot be undone.`)) return;

        this.reportingService.deleteCatalogueEntry(entry.reportId).subscribe({
            next: () => {
                this.entries.update(rows => rows.filter(r => r.reportId !== entry.reportId));
            },
            error: () => {
                this.error.set(`Failed to delete "${entry.title}".`);
            }
        });
    }

    private loadAll(): void {
        this.loading.set(true);
        this.error.set(null);

        this.reportingService.getFullCatalogue().subscribe({
            next: rows => {
                this.entries.set(rows);
                this.loading.set(false);
            },
            error: () => {
                this.loading.set(false);
                this.error.set('Could not load the catalogue.');
            }
        });
    }

    private mergeResult(result: ReportCatalogueEntryDto, isNew: boolean): void {
        if (isNew) {
            this.entries.update(rows => [...rows, result].sort((a, b) => a.sortOrder - b.sortOrder));
        } else {
            this.entries.update(rows => rows
                .map(r => r.reportId === result.reportId ? result : r)
                .sort((a, b) => a.sortOrder - b.sortOrder));
        }
    }

    private nextSortOrder(): number {
        const max = this.entries().reduce((m, e) => Math.max(m, e.sortOrder), 0);
        return Math.ceil((max + 10) / 10) * 10;
    }
}

function nullIfBlank(s: string): string | null {
    const trimmed = s.trim();
    return trimmed.length === 0 ? null : trimmed;
}
