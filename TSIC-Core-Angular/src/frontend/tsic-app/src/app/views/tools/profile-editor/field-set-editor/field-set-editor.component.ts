import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ProfileMetadataField, ValidationTestResult } from '@infrastructure/view-models/profile-migration.models';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { AllowedField } from '../allowed-fields';

type FieldType = 'TEXT' | 'TEXTAREA' | 'EMAIL' | 'NUMBER' | 'TEL' | 'DATE' | 'DATETIME' | 'CHECKBOX' | 'SELECT' | 'RADIO' | 'HIDDEN' | 'UPLOAD';

/**
 * Presentational per-field editor shared by the player and adult form designers.
 *
 * Host-agnostic: it holds NO HTTP/service/router dependency. It renders the visibility-grouped field
 * list (drag/drop reorder of public fields), and owns the add / edit / remove / test-validation modals.
 * Every mutation emits a brand-new immutable field array via {@link fieldsChange}; the host decides how
 * to persist it. Validation testing is delegated to the host via {@link validationTest} (result pushed
 * back through the {@link testResult} / {@link testing} inputs).
 */
@Component({
    selector: 'app-field-set-editor',
    standalone: true,
    imports: [CommonModule, FormsModule, DragDropModule, TsicDialogComponent],
    templateUrl: './field-set-editor.component.html',
    styleUrl: './field-set-editor.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class FieldSetEditorComponent {
    // ─── Inputs ──────────────────────────────────────────────────────────
    fields = input.required<ProfileMetadataField[]>();
    allowedFields = input.required<AllowedField[]>();
    disabled = input<boolean>(false);
    scopeLabel = input<string>('');
    // Optional context line shown under the "Form Fields" header (e.g. scope of edits).
    headerNote = input<string>('');
    removeWarning = input<string>('Are you sure you want to remove this field?');
    testResult = input<ValidationTestResult | null>(null);
    testing = input<boolean>(false);

    // ─── Outputs ─────────────────────────────────────────────────────────
    // Emits the FULL new field array on reorder / add / edit / remove. Host persists.
    fieldsChange = output<ProfileMetadataField[]>();
    // Host runs the HTTP validation test and pushes the result back via the testResult input.
    validationTest = output<{ field: ProfileMetadataField; testValue: string }>();

    // ─── Field grouping (derived from the fields() input) ────────────────
    registrantFields = computed(() =>
        this.fields().filter(f => f.visibility === 'public').slice().sort((a, b) => (a.order ?? 0) - (b.order ?? 0)));
    adminOnlyFields = computed(() =>
        this.fields().filter(f => f.visibility === 'adminOnly').slice().sort((a, b) => (a.order ?? 0) - (b.order ?? 0)));
    hiddenFields = computed(() =>
        this.fields().filter(f => f.visibility === 'hidden').slice().sort((a, b) => (a.order ?? 0) - (b.order ?? 0)));

    // ─── Edit modal state ────────────────────────────────────────────────
    isEditModalOpen = signal(false);
    editingField = signal<ProfileMetadataField | null>(null);
    editingFieldIndex = signal<number>(-1);
    // True when the edit modal is fine-tuning a just-added field not yet in the array.
    private editingIsNew = signal(false);
    orderOptions = signal<number[]>([]);
    selectedOrderIndex = signal<number>(-1);

    // ─── Add field modal state ───────────────────────────────────────────
    isAddFieldModalOpen = signal(false);
    selectedNewFieldName = signal<string | null>(null);
    addFieldPlacement = signal<'public' | 'adminOnly' | 'hidden'>('public');
    availableNewFields = computed(() => {
        const used = new Set(this.fields().map(f => f.name.toLowerCase()));
        return this.allowedFields().filter(f => !used.has(f.name.toLowerCase()));
    });
    selectedNewField = computed<AllowedField | null>(() => {
        const name = this.selectedNewFieldName();
        if (!name) return null;
        return this.allowedFields().find(f => f.name === name) ?? null;
    });

    // ─── Test validation modal state ─────────────────────────────────────
    isTestModalOpen = signal(false);
    testFieldName = signal<string>('');
    testValue = signal<string>('');
    // Only surface a test result once the user has run a test in the current modal session.
    private hasRunTest = signal(false);
    showTestResult = computed(() => this.hasRunTest() && !!this.testResult());

    // ─── Confirm (remove) modal state ────────────────────────────────────
    showConfirmModal = signal(false);
    confirmModalTitle = signal('Confirm Action');
    confirmModalMessage = signal('');
    confirmModalAction = signal<(() => void) | null>(null);

    fieldTypeOptions: FieldType[] = ['TEXT', 'TEXTAREA', 'EMAIL', 'NUMBER', 'TEL', 'DATE', 'DATETIME', 'CHECKBOX', 'SELECT', 'RADIO', 'UPLOAD'];

    // ============================================================================
    // FIELD EDITING
    // ============================================================================

    openEditModal(field: ProfileMetadataField, index: number) {
        this.editingField.set({ ...field }); // Clone to avoid direct mutation
        this.editingFieldIndex.set(index);
        this.editingIsNew.set(false);
        this.isEditModalOpen.set(true);
        this.prepareOrderOptions(field);
    }

    // Open editing using the field's name (for grouped lists)
    openEditByField(field: ProfileMetadataField) {
        const idx = this.fields().findIndex(f => f.name === field.name);
        if (idx >= 0) this.openEditModal(this.fields()[idx], idx);
    }

    private prepareOrderOptions(field: ProfileMetadataField) {
        const registrant = this.fields()
            .filter(f => f.visibility === 'public')
            .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
        this.orderOptions.set(registrant.map((_, i) => i));
        const pos = registrant.findIndex(f => f.name === field.name);
        this.selectedOrderIndex.set(pos >= 0 ? pos : -1);
    }

    closeEditModal() {
        this.isEditModalOpen.set(false);
        this.editingField.set(null);
        this.editingFieldIndex.set(-1);
        this.editingIsNew.set(false);
    }

    // Drag-and-drop within registrant-visible fields only
    onRegistrantDrop(event: CdkDragDrop<ProfileMetadataField[]>) {
        const registrant = this.registrantFields().slice();
        moveItemInArray(registrant, event.previousIndex, event.currentIndex);

        // Compute min order for registrant region from the original list
        const orders = this.fields().filter(f => f.visibility === 'public').map(f => f.order ?? 0);
        const minOrder = orders.length ? Math.min(...orders) : 0;
        // Reassign consecutive orders within registrant range
        const updatedFields = this.fields().slice();
        let i = 0;
        for (const f of registrant) {
            const idx = updatedFields.findIndex(x => x.name === f.name);
            if (idx >= 0) {
                updatedFields[idx] = { ...updatedFields[idx], order: minOrder + i };
            }
            i++;
        }
        this.fieldsChange.emit(updatedFields);
    }

    saveFieldEdit() {
        const field = this.editingField();
        if (!field) return;

        if (this.editingIsNew()) {
            // Just-added field: append it and persist.
            const updatedFields = [...this.fields(), field];
            this.fieldsChange.emit(updatedFields);
            this.closeEditModal();
            return;
        }

        const index = this.editingFieldIndex();
        if (index < 0) return;

        // Start from a copy of all fields
        const updatedFields = [...this.fields()];

        // Reorder within registrant-visible (public) region only if applicable
        if (field.visibility === 'public' && this.selectedOrderIndex() >= 0) {
            this.reorderRegistrantForEdit(updatedFields, field.name, this.selectedOrderIndex());
        }

        // Apply other edited field properties back to the array entry
        updatedFields[index] = { ...updatedFields[index], ...field };

        this.fieldsChange.emit(updatedFields);
        this.closeEditModal();
    }

    // Reorder registrant-visible fields for saveFieldEdit
    private reorderRegistrantForEdit(updatedFields: ProfileMetadataField[], fieldName: string, targetIdx: number) {
        const registrant = updatedFields
            .filter(f => f.visibility === 'public')
            .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

        const currentIdxInRegistrant = registrant.findIndex(f => f.name === fieldName);
        if (currentIdxInRegistrant < 0 || targetIdx < 0 || targetIdx >= registrant.length) {
            return;
        }

        const [moved] = registrant.splice(currentIdxInRegistrant, 1);
        registrant.splice(targetIdx, 0, moved);

        const orders = updatedFields.filter(f => f.visibility === 'public').map(f => f.order ?? 0);
        const minOrder = Math.min(...orders);
        const maxOrder = Math.max(...orders);

        const step = registrant.length > 1 ? Math.max(1, Math.floor((maxOrder - minOrder) / (registrant.length - 1))) : 1;
        let i = 0;
        for (const f of registrant) {
            const idx = updatedFields.findIndex(x => x.name === f.name);
            if (idx >= 0) {
                updatedFields[idx] = { ...updatedFields[idx], order: minOrder + i * step };
            }
            i++;
        }
    }

    addNewField() {
        this.selectedNewFieldName.set(null);
        this.addFieldPlacement.set('public');
        this.isAddFieldModalOpen.set(true);
    }

    closeAddFieldModal() {
        this.isAddFieldModalOpen.set(false);
        this.selectedNewFieldName.set(null);
    }

    confirmAddSelectedField() {
        const allowed = this.selectedNewField();
        if (!allowed) { this.closeAddFieldModal(); return; }
        const visibility = this.addFieldPlacement();
        // Determine next order within the chosen visibility group
        const groupOrders = this.fields()
            .filter(f => f.visibility === visibility)
            .map(f => f.order ?? 0);
        const nextOrder = (groupOrders.length ? Math.max(...groupOrders) : 0) + 1;

        const newField: ProfileMetadataField = {
            name: allowed.name,
            dbColumn: allowed.dbColumn || allowed.name,
            displayName: allowed.displayName,
            inputType: visibility === 'hidden' ? 'HIDDEN' : allowed.inputType,
            order: nextOrder,
            visibility: visibility,
            adminOnly: visibility === 'adminOnly',
            dataSource: allowed.dataSource
        } as ProfileMetadataField;

        this.closeAddFieldModal();
        // Open the edit modal to fine-tune, then Save appends + persists. Cancel discards.
        this.editingField.set(newField);
        this.editingFieldIndex.set(-1);
        this.editingIsNew.set(true);
        this.prepareOrderOptions(newField);
        this.isEditModalOpen.set(true);
    }

    removeField(index: number) {
        const updatedFields = this.fields().filter((_, i) => i !== index);
        this.fieldsChange.emit(updatedFields);
    }

    // Remove by field name (avoids arrow functions in templates)
    removeFieldByName(name: string) {
        const idx = this.fields().findIndex(f => f.name === name);
        if (idx >= 0) {
            this.openConfirm('Remove Field', this.removeWarning(), () => this.removeField(idx));
        }
    }

    // ============================================================================
    // TEST VALIDATION
    // ============================================================================

    openTestModal(fieldName: string) {
        this.testFieldName.set(fieldName);
        this.testValue.set('');
        this.hasRunTest.set(false);
        this.isTestModalOpen.set(true);
    }

    closeTestModal() {
        this.isTestModalOpen.set(false);
        this.testFieldName.set('');
        this.testValue.set('');
        this.hasRunTest.set(false);
    }

    runValidationTest() {
        const fieldName = this.testFieldName();
        if (!fieldName) return;
        const field = this.fields().find(f => f.name === fieldName);
        if (!field) return;
        this.hasRunTest.set(true);
        this.validationTest.emit({ field, testValue: this.testValue() });
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    trackByIndex(index: number): number { return index; }
    trackByName(_index: number, item: ProfileMetadataField) { return item.name; }

    // When visibility changes, enforce inputType HIDDEN for hidden fields
    onVisibilityChange(field: ProfileMetadataField) {
        if (!field) return;
        if (field.visibility === 'hidden') { field.inputType = 'HIDDEN'; return; }
        if (field.inputType === 'HIDDEN') {
            field.inputType = field.dataSource ? 'SELECT' : 'TEXT';
        }
    }

    // Summarize validation into short badges for the table view
    getValidationBadges(field: ProfileMetadataField): string[] {
        const v = field.validation;
        if (!v) return [];
        const badges: string[] = [];
        if (v.required) badges.push('required');
        if (v.requiredTrue) badges.push('requiredTrue');
        if (v.email || field.inputType === 'EMAIL') badges.push('email');
        if (typeof v.minLength === 'number') badges.push(`minLen:${v.minLength}`);
        if (typeof v.maxLength === 'number') badges.push(`maxLen:${v.maxLength}`);
        if (typeof v.min === 'number') badges.push(`min:${v.min}`);
        if (typeof v.max === 'number') badges.push(`max:${v.max}`);
        if (v.pattern) badges.push('pattern');
        if (v.remote) badges.push('remote');
        return badges;
    }

    // ── Confirm modal helpers ────────────────────────────────────────────
    openConfirm(title: string, message: string, action: () => void) {
        this.confirmModalTitle.set(title);
        this.confirmModalMessage.set(message);
        this.confirmModalAction.set(() => action());
        this.showConfirmModal.set(true);
    }

    confirmAction() {
        const action = this.confirmModalAction();
        if (action) action();
        this.closeConfirm();
    }

    closeConfirm() {
        this.showConfirmModal.set(false);
        this.confirmModalTitle.set('Confirm Action');
        this.confirmModalMessage.set('');
        this.confirmModalAction.set(null);
    }
}
