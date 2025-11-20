import { Component, Input, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ProfileMigrationService, ProfileMetadata, OptionSet, ProfileFieldOption } from '../../../core/services/profile-migration.service';
import { ToastService } from '../../../shared/toast.service';
import { TsicDialogComponent } from '../../../shared/components/tsic-dialog/tsic-dialog.component';

@Component({
    selector: 'app-options-panel',
    standalone: true,
    imports: [CommonModule, FormsModule, DragDropModule, TsicDialogComponent],
    templateUrl: './options-panel.component.html'
})
export class OptionsPanelComponent {
    private readonly migrationService = inject(ProfileMigrationService);
    private readonly toast = inject(ToastService);

    @Input({ required: true }) metadata: ProfileMetadata | null = null;

    // Mirror service signals
    optionSets = signal<OptionSet[]>([]);
    optionsLoading = signal(false);
    optionsError = signal<string | null>(null);

    // Create Option Set state
    isCreateOptionOpen = signal(false);
    newOptionKey = signal('');
    newOptionValues = signal<ProfileFieldOption[]>([]);
    isCreatingOption = signal(false);

    // Edit Option Set state
    editingOptionKey = signal<string | null>(null);
    editingOptionValues = signal<ProfileFieldOption[]>([]);
    isSavingOption = signal(false);
    isRenaming = signal(false);
    renameValue = signal('');

    // Delete confirmation dialog state
    showConfirm = signal(false);
    confirmTitle = signal('Delete Option Set');
    confirmMessage = signal('');
    pendingDeleteKey = signal<string | null>(null);

    // Filter used option sets
    showUsedOptionsOnly = signal(true);
    usedOptionKeys = computed(() => {
        const keys = new Set<string>();
        const fields = this.metadata?.fields ?? [];
        for (const f of fields) {
            const k = (f.dataSource || '').trim();
            if (k) keys.add(k.toLowerCase());
        }
        return keys;
    });
    visibleOptionSets = computed(() => {
        const sets = this.optionSets();
        if (!this.showUsedOptionsOnly()) return sets;
        const used = this.usedOptionKeys();
        return sets.filter(s => used.has(s.key.toLowerCase()));
    });

    constructor() {
        // Mirror service state
        effect(() => {
            this.optionSets.set(this.migrationService.currentOptionSets());
            this.optionsLoading.set(this.migrationService.optionsLoading());
            this.optionsError.set(this.migrationService.optionsError());
        });
    }

    // API calls
    loadOptionSets() {
        this.optionsLoading.set(true);
        this.optionsError.set(null);
        this.migrationService.getCurrentJobOptionSets(
            () => this.optionsLoading.set(false),
            (err) => {
                this.optionsLoading.set(false);
                this.optionsError.set(err?.error?.message || 'Failed to load option sets');
            }
        );
    }

    // UI helpers
    openCreateOptionSet() {
        this.isCreateOptionOpen.set(true);
        this.newOptionKey.set('');
        this.newOptionValues.set([]);
    }
    closeCreateOptionSet() {
        this.isCreateOptionOpen.set(false);
        this.newOptionKey.set('');
        this.newOptionValues.set([]);
    }

    addNewOptionRow(isForCreate = false) {
        const target = isForCreate ? this.newOptionValues : this.editingOptionValues;
        const current = [...target()];
        current.push({ value: '', label: '' });
        target.set(current);
    }
    removeOptionRow(index: number, isForCreate = false) {
        const target = isForCreate ? this.newOptionValues : this.editingOptionValues;
        const current = target().filter((_, i) => i !== index);
        target.set(current);
    }

    onEditOptionDrop(event: CdkDragDrop<ProfileFieldOption[]>) {
        const arr = this.editingOptionValues().slice();
        moveItemInArray(arr, event.previousIndex, event.currentIndex);
        this.editingOptionValues.set(arr);

        // Persist immediately
        const key = this.editingOptionKey();
        if (!key) return;
        this.isSavingOption.set(true);
        this.migrationService.updateCurrentJobOptionSet(
            key,
            arr,
            () => { this.isSavingOption.set(false); this.toast.show('Option order saved', 'success', 1500); },
            (err) => {
                this.isSavingOption.set(false);
                this.optionsError.set(err?.error?.message || 'Failed to save option order');
                this.toast.show('Failed to save option order', 'danger', 2500);
            }
        );
    }
    onCreateOptionDrop(event: CdkDragDrop<ProfileFieldOption[]>) {
        const arr = this.newOptionValues().slice();
        moveItemInArray(arr, event.previousIndex, event.currentIndex);
        this.newOptionValues.set(arr);
    }

    createOptionSet() {
        const key = this.newOptionKey().trim();
        const values = this.newOptionValues().filter(v => v.value.trim().length > 0);
        if (!key) { this.optionsError.set('Option set key is required'); return; }
        this.isCreatingOption.set(true);
        this.optionsError.set(null);

        this.migrationService.createCurrentJobOptionSet(
            { key, values },
            () => { this.isCreatingOption.set(false); this.closeCreateOptionSet(); },
            (err) => { this.isCreatingOption.set(false); this.optionsError.set(err?.error?.message || 'Failed to create option set'); }
        );
    }

    editOptionSet(set: OptionSet) {
        this.editingOptionKey.set(set.key);
        this.editingOptionValues.set(set.values.map(v => ({ ...v })));
        this.renameValue.set(set.key);
    }
    cancelEditOptionSet() {
        this.editingOptionKey.set(null);
        this.editingOptionValues.set([]);
        this.renameValue.set('');
        this.isSavingOption.set(false);
        this.isRenaming.set(false);
    }

    saveEditedOptionSet() {
        const key = this.editingOptionKey();
        if (!key) return;
        const values = this.editingOptionValues().filter(v => v.value.trim().length > 0);
        this.isSavingOption.set(true);
        this.migrationService.updateCurrentJobOptionSet(
            key,
            values,
            () => { this.isSavingOption.set(false); this.cancelEditOptionSet(); },
            (err) => { this.isSavingOption.set(false); this.optionsError.set(err?.error?.message || 'Failed to save option set'); }
        );
    }

    deleteOptionSet(key: string) {
        // Open confirm dialog instead of immediate delete
        this.pendingDeleteKey.set(key);
        this.confirmTitle.set('Delete Option Set');
        this.confirmMessage.set(`Are you sure you want to delete the option set "${key}"? This cannot be undone.`);
        this.showConfirm.set(true);
    }

    confirmDelete() {
        const key = this.pendingDeleteKey();
        if (!key) { this.closeConfirm(); return; }
        this.migrationService.deleteCurrentJobOptionSet(
            key,
            () => {
                if (this.editingOptionKey()?.toLowerCase() === key.toLowerCase()) this.cancelEditOptionSet();
                this.closeConfirm();
            },
            (err) => {
                this.optionsError.set(err?.error?.message || 'Failed to delete option set');
                this.closeConfirm();
            }
        );
    }

    closeConfirm() {
        this.showConfirm.set(false);
        this.pendingDeleteKey.set(null);
        this.confirmMessage.set('');
    }

    renameOptionSet(oldKey: string) {
        const newKey = this.renameValue().trim();
        if (!newKey || newKey.toLowerCase() === oldKey.toLowerCase()) { this.isRenaming.set(false); return; }
        this.isRenaming.set(true);
        this.migrationService.renameCurrentJobOptionSet(
            oldKey,
            newKey,
            () => {
                if (this.editingOptionKey()?.toLowerCase() === oldKey.toLowerCase()) {
                    this.editingOptionKey.set(newKey);
                }
                this.isRenaming.set(false);
            },
            (err) => { this.isRenaming.set(false); this.optionsError.set(err?.error?.message || 'Failed to rename option set'); }
        );
    }

    // Template helpers
    trackByIndex(i: number) { return i; }
    trackByKey(_i: number, item: OptionSet) { return item.key; }
}
