import { Component, ChangeDetectionStrategy, inject, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GridAllModule, GridComponent, EditSettingsModel, ToolbarItems, IEditCell } from '@syncfusion/ej2-angular-grids';
import { ReportingService } from '@infrastructure/services/reporting.service';
import type {
    JobReportEditorRoleDto,
    JobReportEditorRowDto,
    JobReportEditorUpdateDto,
    JobReportEditorCreateDto
} from '@core/api';

@Component({
    selector: 'app-library-editor',
    standalone: true,
    imports: [CommonModule, FormsModule, GridAllModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './library-editor.component.html',
    styleUrls: ['./library-editor.component.scss'],
})
export class LibraryEditorComponent {
    private readonly reporting = inject(ReportingService);

    readonly isLoadingRoles = signal(false);
    readonly isLoadingRows = signal(false);
    readonly errorMessage = signal('');
    readonly successMessage = signal('');

    readonly roles = signal<JobReportEditorRoleDto[]>([]);
    readonly selectedRoleId = signal<string>('');
    readonly rows = signal<JobReportEditorRowDto[]>([]);

    readonly editSettings: EditSettingsModel = {
        allowEditing: true,
        allowAdding: true,
        allowDeleting: false,
        newRowPosition: 'Top',
    };

    readonly toolbar: ToolbarItems[] = ['Add', 'Edit', 'Cancel', 'Update'];

    // The grid's dropdownedit forces the DropDownList's fields to the COLUMN name
    // ({ text: 'kind', value: 'kind' }), so a primitive string array resolves every
    // item's text and value to undefined and the list renders "No records found".
    // The rows must be objects carrying a `kind` property. SpaComponent is a live
    // Kind (Roster Table / Schedule List / Check-In rows all use it) and was missing.
    readonly kindEdit: IEditCell = {
        params: {
            dataSource: [
                { kind: 'StoredProcedure' },
                { kind: 'CrystalReport' },
                { kind: 'SpaComponent' },
            ],
            fields: { text: 'kind', value: 'kind' },
            allowFiltering: false,
        },
    };

    // Spin buttons OFF: the Sort column is narrow, and a NumericTextBox that keeps them
    // squeezes its own input to zero width — the seeded value is invisible and the cell
    // cannot be typed into. Sort is typed, not nudged.
    readonly sortEdit: IEditCell = {
        params: { format: 'N0', decimals: 0, validateDecimalOnType: true, showSpinButton: false },
    };

    readonly grid = viewChild.required<GridComponent>('grid');

    constructor() {
        this.loadRoles();
    }

    loadRoles(): void {
        this.isLoadingRoles.set(true);
        this.errorMessage.set('');

        this.reporting.getEditorRoles().subscribe({
            next: roles => {
                this.roles.set(roles);
                this.isLoadingRoles.set(false);
                if (roles.length > 0 && !this.selectedRoleId()) {
                    this.onRoleChange(roles[0].roleId);
                }
            },
            error: err => {
                this.isLoadingRoles.set(false);
                this.errorMessage.set(this.formatError(err, 'Failed to load roles.'));
            },
        });
    }

    onRoleChange(roleId: string): void {
        this.selectedRoleId.set(roleId);
        this.rows.set([]);
        if (!roleId) return;
        this.loadRows(roleId);
    }

    loadRows(roleId: string): void {
        this.isLoadingRows.set(true);
        this.errorMessage.set('');

        this.reporting.getEditorRows(roleId).subscribe({
            next: rows => {
                this.rows.set(rows);
                this.isLoadingRows.set(false);
            },
            error: err => {
                this.isLoadingRows.set(false);
                this.errorMessage.set(this.formatError(err, 'Failed to load editor rows.'));
            },
        });
    }

    onActionBegin(args: { requestType?: string; data?: Partial<JobReportEditorRowDto> }): void {
        // Seed defaults on the new row so the user only fills the fields that vary.
        // SortOrder defaults to max(existing) + 10; falls back to 10 for an empty role.
        if (args.requestType === 'add' && args.data) {
            const maxSort = this.rows().reduce((m, r) => Math.max(m, r.sortOrder ?? 0), 0);
            args.data.controller = args.data.controller ?? 'Reporting';
            args.data.kind = args.data.kind ?? 'StoredProcedure';
            args.data.active = args.data.active ?? true;
            args.data.sortOrder = args.data.sortOrder ?? (maxSort + 10);
            args.data.groupLabel = args.data.groupLabel ?? 'Reports';
        }
    }

    onActionComplete(args: { requestType?: string; action?: string; data?: JobReportEditorRowDto }): void {
        if (args.requestType !== 'save' || !args.data) return;

        if (args.action === 'edit') {
            this.handleEditSave(args.data);
        } else if (args.action === 'add') {
            this.handleAddSave(args.data);
        }
    }

    private handleEditSave(row: JobReportEditorRowDto): void {
        const dto: JobReportEditorUpdateDto = {
            title: row.title,
            iconName: row.iconName ?? null,
            groupLabel: row.groupLabel ?? null,
            sortOrder: row.sortOrder,
            active: row.active,
        };
        this.successMessage.set('');
        this.reporting.updateEditorRow(row.jobReportId, dto).subscribe({
            next: updated => {
                // Reflect server-side audit fields (Modified, LebUserId) back into the grid.
                const current = this.rows();
                const idx = current.findIndex(r => r.jobReportId === updated.jobReportId);
                if (idx >= 0) {
                    const next = [...current];
                    next[idx] = updated;
                    this.rows.set(next);
                }
                this.successMessage.set(`Saved "${updated.title}".`);
            },
            error: err => {
                this.errorMessage.set(this.formatError(err, 'Failed to save row.'));
            },
        });
    }

    private handleAddSave(row: JobReportEditorRowDto): void {
        const roleId = this.selectedRoleId();
        if (!roleId) {
            this.errorMessage.set('Cannot add a row before a role is selected.');
            return;
        }
        const dto: JobReportEditorCreateDto = {
            roleId,
            title: row.title,
            iconName: row.iconName ?? null,
            controller: row.controller,
            action: row.action,
            kind: row.kind,
            groupLabel: row.groupLabel ?? null,
            sortOrder: row.sortOrder,
            active: row.active,
        };
        this.successMessage.set('');
        this.reporting.createEditorRow(dto).subscribe({
            next: created => {
                // Reload so SF's locally-inserted placeholder is replaced with the
                // server's authoritative row order (sortOrder, modified, lebUserId).
                this.successMessage.set(`Added "${created.title}".`);
                this.loadRows(roleId);
            },
            error: err => {
                // SF inserted the placeholder into its view — reload to drop it.
                this.loadRows(roleId);
                if (err?.status === 409) {
                    this.errorMessage.set(err?.error?.message ||
                        'A report with that Controller, Action, and Group already exists for this role.');
                } else {
                    this.errorMessage.set(this.formatError(err, 'Failed to add row.'));
                }
            },
        });
    }

    private formatError(err: { status?: number; error?: { message?: string } }, fallback: string): string {
        if (err?.status === 401) return 'You must be logged in.';
        if (err?.status === 403) return 'SuperUser access is required for the reports library editor.';
        return err?.error?.message || fallback;
    }
}
