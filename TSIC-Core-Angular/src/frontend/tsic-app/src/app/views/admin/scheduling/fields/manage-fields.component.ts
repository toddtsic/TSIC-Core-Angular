import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import {
    FieldManagementService,
    FieldDto,
    LeagueSeasonFieldDto
} from './services/field-management.service';

type SortDir = 'asc' | 'desc' | null;
type AvailableSortCol = keyof FieldDto | null;
type AssignedSortCol = keyof LeagueSeasonFieldDto | null;

@Component({
    selector: 'app-manage-fields',
    standalone: true,
    imports: [CommonModule, FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './manage-fields.component.html',
    styleUrl: './manage-fields.component.scss'
})
export class ManageFieldsComponent {
    private readonly fieldService = inject(FieldManagementService);
    private readonly toast = inject(ToastService);

    // ── Available panel (not assigned to this league-season) ──
    readonly availableFields = signal<FieldDto[]>([]);
    readonly availableSelected = signal<Set<string>>(new Set());
    readonly availableFilter = signal('');
    readonly availableSortCol = signal<AvailableSortCol>(null);
    readonly availableSortDir = signal<SortDir>(null);
    readonly sortedFilteredAvailable = computed(() =>
        this.sortFields(this.filterAvailable(this.availableFields(), this.availableFilter()),
            this.availableSortCol(), this.availableSortDir()));

    // ── Assigned panel (active for this league-season) ──
    readonly assignedFields = signal<LeagueSeasonFieldDto[]>([]);
    readonly assignedSelected = signal<Set<string>>(new Set());
    readonly assignedFilter = signal('');
    readonly assignedSortCol = signal<AssignedSortCol>(null);
    readonly assignedSortDir = signal<SortDir>(null);
    readonly sortedFilteredAssigned = computed(() =>
        this.sortFields(this.filterAssigned(this.assignedFields(), this.assignedFilter()),
            this.assignedSortCol(), this.assignedSortDir()));

    // ── Transfer state ──
    readonly swappingId = signal<string | null>(null);
    readonly isBatchAssigning = signal(false);
    readonly isBatchRemoving = signal(false);

    // ── Detail editor ──
    readonly selectedField = signal<FieldDto | null>(null);
    readonly isCreating = signal(false);
    readonly isSaving = signal(false);
    readonly isDeleting = signal(false);

    // Editor form fields
    readonly editName = signal('');
    readonly editAddress = signal('');
    readonly editCity = signal('');
    readonly editState = signal('');
    readonly editZip = signal('');
    readonly editDirections = signal('');
    readonly editLatitude = signal<number | null>(null);
    readonly editLongitude = signal<number | null>(null);

    // ── General ──
    readonly isLoading = signal(false);

    constructor() {
        this.loadData();
    }

    // ── Data Loading ──

    loadData() {
        this.isLoading.set(true);
        this.fieldService.getFieldManagementData().subscribe({
            next: data => {
                this.availableFields.set(data.availableFields);
                this.assignedFields.set(data.assignedFields);
                this.isLoading.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load field data.', 'danger', 4000);
                this.isLoading.set(false);
            }
        });
    }

    // ── Sorting ──

    onSortAvailable(col: AvailableSortCol) {
        const { newCol, newDir } = this.nextSort(this.availableSortCol(), this.availableSortDir(), col);
        this.availableSortCol.set(newCol as AvailableSortCol);
        this.availableSortDir.set(newDir);
    }

    onSortAssigned(col: AssignedSortCol) {
        const { newCol, newDir } = this.nextSort(this.assignedSortCol(), this.assignedSortDir(), col);
        this.assignedSortCol.set(newCol as AssignedSortCol);
        this.assignedSortDir.set(newDir);
    }

    private nextSort(currentCol: string | null, currentDir: SortDir, col: string | null): { newCol: string | null, newDir: SortDir } {
        if (currentCol !== col) return { newCol: col, newDir: 'asc' };
        if (currentDir === 'asc') return { newCol: col, newDir: 'desc' };
        return { newCol: null, newDir: null };
    }

    sortIconAvailable(col: AvailableSortCol): string {
        return this.getSortIcon(this.availableSortCol(), this.availableSortDir(), col);
    }

    sortIconAssigned(col: AssignedSortCol): string {
        return this.getSortIcon(this.assignedSortCol(), this.assignedSortDir(), col);
    }

    private getSortIcon(currentCol: string | null, currentDir: SortDir, col: string | null): string {
        if (currentCol !== col || !currentDir) return 'bi-chevron-expand';
        return currentDir === 'asc' ? 'bi-sort-up' : 'bi-sort-down';
    }

    // ── Selection ──

    toggleAvailableSelect(fieldId: string) {
        const current = new Set(this.availableSelected());
        if (current.has(fieldId)) current.delete(fieldId);
        else current.add(fieldId);
        this.availableSelected.set(current);
    }

    toggleAssignedSelect(fieldId: string) {
        const current = new Set(this.assignedSelected());
        if (current.has(fieldId)) current.delete(fieldId);
        else current.add(fieldId);
        this.assignedSelected.set(current);
    }

    selectAllAvailable() {
        this.availableSelected.set(new Set(this.sortedFilteredAvailable().map(f => f.fieldId)));
    }

    deselectAllAvailable() {
        this.availableSelected.set(new Set());
    }

    selectAllAssigned() {
        this.assignedSelected.set(new Set(this.sortedFilteredAssigned().map(f => f.fieldId)));
    }

    deselectAllAssigned() {
        this.assignedSelected.set(new Set());
    }

    // ── Single-row assign/remove ──

    assignField(field: FieldDto) {
        this.swappingId.set(field.fieldId);
        this.fieldService.assignFields({ fieldIds: [field.fieldId] }).subscribe({
            next: () => {
                this.toast.show(`${field.fName} assigned.`, 'success', 2000);
                this.swappingId.set(null);
                this.loadData();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to assign field.', 'danger', 4000);
                this.swappingId.set(null);
            }
        });
    }

    removeField(field: LeagueSeasonFieldDto) {
        this.swappingId.set(field.fieldId);
        this.fieldService.removeFields({ fieldIds: [field.fieldId] }).subscribe({
            next: () => {
                this.toast.show(`${field.fName} removed.`, 'success', 2000);
                this.swappingId.set(null);
                this.loadData();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to remove field.', 'danger', 4000);
                this.swappingId.set(null);
            }
        });
    }

    // ── Batch assign/remove ──

    assignSelected() {
        if (this.availableSelected().size === 0) return;
        this.isBatchAssigning.set(true);
        this.fieldService.assignFields({ fieldIds: Array.from(this.availableSelected()) }).subscribe({
            next: () => {
                this.toast.show(`${this.availableSelected().size} field(s) assigned.`, 'success', 2000);
                this.isBatchAssigning.set(false);
                this.availableSelected.set(new Set());
                this.loadData();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to assign fields.', 'danger', 4000);
                this.isBatchAssigning.set(false);
            }
        });
    }

    removeSelected() {
        if (this.assignedSelected().size === 0) return;
        this.isBatchRemoving.set(true);
        this.fieldService.removeFields({ fieldIds: Array.from(this.assignedSelected()) }).subscribe({
            next: () => {
                this.toast.show(`${this.assignedSelected().size} field(s) removed.`, 'success', 2000);
                this.isBatchRemoving.set(false);
                this.assignedSelected.set(new Set());
                this.loadData();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to remove fields.', 'danger', 4000);
                this.isBatchRemoving.set(false);
            }
        });
    }

    // ── Detail editor ──

    selectFieldForEdit(field: FieldDto) {
        this.isCreating.set(false);
        this.selectedField.set(field);
        this.populateEditor(field);
    }

    selectAssignedFieldForEdit(field: LeagueSeasonFieldDto) {
        // Find the full FieldDto from available or fetch it from assigned data
        const fullField: FieldDto = {
            fieldId: field.fieldId,
            fName: field.fName,
            city: field.city ?? undefined,
            state: field.state ?? undefined
        } as FieldDto;
        this.isCreating.set(false);
        this.selectedField.set(fullField);
        this.populateEditor(fullField);
    }

    startCreate() {
        this.isCreating.set(true);
        this.selectedField.set(null);
        this.editName.set('');
        this.editAddress.set('');
        this.editCity.set('');
        this.editState.set('');
        this.editZip.set('');
        this.editDirections.set('');
        this.editLatitude.set(null);
        this.editLongitude.set(null);
    }

    cancelEdit() {
        this.isCreating.set(false);
        this.selectedField.set(null);
    }

    saveField() {
        if (!this.editName().trim()) {
            this.toast.show('Field name is required.', 'warning');
            return;
        }

        this.isSaving.set(true);

        if (this.isCreating()) {
            this.fieldService.createField({
                fName: this.editName().trim(),
                address: this.editAddress() || undefined,
                city: this.editCity() || undefined,
                state: this.editState() || undefined,
                zip: this.editZip() || undefined,
                directions: this.editDirections() || undefined,
                latitude: this.editLatitude() ?? undefined,
                longitude: this.editLongitude() ?? undefined
            }).subscribe({
                next: created => {
                    this.toast.show(`${created.fName} created.`, 'success', 2000);
                    this.isSaving.set(false);
                    this.isCreating.set(false);
                    this.selectedField.set(null);
                    this.loadData();
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to create field.', 'danger', 4000);
                    this.isSaving.set(false);
                }
            });
        } else {
            const field = this.selectedField();
            if (!field) return;

            this.fieldService.updateField({
                fieldId: field.fieldId,
                fName: this.editName().trim(),
                address: this.editAddress() || undefined,
                city: this.editCity() || undefined,
                state: this.editState() || undefined,
                zip: this.editZip() || undefined,
                directions: this.editDirections() || undefined,
                latitude: this.editLatitude() ?? undefined,
                longitude: this.editLongitude() ?? undefined
            }).subscribe({
                next: () => {
                    this.toast.show(`${this.editName()} updated.`, 'success', 2000);
                    this.isSaving.set(false);
                    this.selectedField.set(null);
                    this.loadData();
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to update field.', 'danger', 4000);
                    this.isSaving.set(false);
                }
            });
        }
    }

    deleteField() {
        const field = this.selectedField();
        if (!field) return;

        this.isDeleting.set(true);
        this.fieldService.deleteField(field.fieldId).subscribe({
            next: () => {
                this.toast.show(`${field.fName} deleted.`, 'success', 2000);
                this.isDeleting.set(false);
                this.selectedField.set(null);
                this.loadData();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Field is in use and cannot be deleted.', 'danger', 4000);
                this.isDeleting.set(false);
            }
        });
    }

    private populateEditor(field: FieldDto) {
        this.editName.set(field.fName ?? '');
        this.editAddress.set(field.address ?? '');
        this.editCity.set(field.city ?? '');
        this.editState.set(field.state ?? '');
        this.editZip.set(field.zip ?? '');
        this.editDirections.set(field.directions ?? '');
        this.editLatitude.set(field.latitude ?? null);
        this.editLongitude.set(field.longitude ?? null);
    }

    // ── Helpers ──

    private filterAvailable(fields: FieldDto[], filter: string): FieldDto[] {
        if (!filter.trim()) return fields;
        const lower = filter.toLowerCase();
        return fields.filter(f =>
            (f.fName ?? '').toLowerCase().includes(lower) ||
            (f.city ?? '').toLowerCase().includes(lower) ||
            (f.state ?? '').toLowerCase().includes(lower));
    }

    private filterAssigned(fields: LeagueSeasonFieldDto[], filter: string): LeagueSeasonFieldDto[] {
        if (!filter.trim()) return fields;
        const lower = filter.toLowerCase();
        return fields.filter(f =>
            (f.fName ?? '').toLowerCase().includes(lower) ||
            (f.city ?? '').toLowerCase().includes(lower) ||
            (f.state ?? '').toLowerCase().includes(lower));
    }

    private sortFields<T extends Record<string, any>>(fields: T[], col: string | null, dir: SortDir): T[] {
        if (!col || !dir) return fields;
        const mult = dir === 'asc' ? 1 : -1;
        return [...fields].sort((a, b) => {
            const aVal = a[col];
            const bVal = b[col];
            if (aVal == null && bVal == null) return 0;
            if (aVal == null) return 1;
            if (bVal == null) return -1;
            if (typeof aVal === 'string' && typeof bVal === 'string') {
                return aVal.localeCompare(bVal) * mult;
            }
            return String(aVal).localeCompare(String(bVal)) * mult;
        });
    }
}
