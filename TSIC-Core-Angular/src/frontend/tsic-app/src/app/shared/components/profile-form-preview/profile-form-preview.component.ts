import { Component, Input, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ProfileMetadata, ProfileMetadataField } from '../../../core/services/profile-migration.service';
import { FormFieldDataService } from '../../../core/services/form-field-data.service';

@Component({
    selector: 'app-profile-form-preview',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    templateUrl: './profile-form-preview.component.html',
    styleUrls: ['./profile-form-preview.component.scss']
})
export class ProfileFormPreviewComponent {
    private readonly fb = inject(FormBuilder);
    private readonly fieldDataService = inject(FormFieldDataService);

    @Input() set metadata(value: ProfileMetadata | null) {
        if (value) {
            this._metadata.set(value);
            this.buildForm();
        }
    }

    @Input() showFieldNumbers = true;
    @Input() showValidationHints = true;
    @Input() readonly = true;

    private readonly _metadata = signal<ProfileMetadata | null>(null);
    formGroup = signal<FormGroup | null>(null);

    // Computed sorted fields
    sortedFields = computed(() => {
        const meta = this._metadata();
        if (!meta) return [];
        return [...meta.fields].sort((a, b) => a.order - b.order);
    });

    private buildForm(): void {
        const meta = this._metadata();
        if (!meta) return;

        const group: any = {};
        for (const field of meta.fields) {
            // Initialize with empty value based on input type
            const initialValue = this.getInitialValue(field);
            group[field.name] = [{ value: initialValue, disabled: this.readonly }];
        }

        this.formGroup.set(this.fb.group(group));
    }

    private getInitialValue(field: ProfileMetadataField): any {
        switch (field.inputType) {
            case 'CHECKBOX':
                return false;
            case 'NUMBER':
                return null;
            case 'DATE':
                return null;
            default:
                return '';
        }
    }

    getDropdownOptions(field: ProfileMetadataField): Array<{ value: string; label: string }> {
        if (!field.dataSource) return [];
        return this.fieldDataService.getOptionsForDataSource(field.dataSource);
    }

    getValidationHint(field: ProfileMetadataField): string {
        if (!field.validation) return '';

        const hints: string[] = [];

        if (field.validation.required) {
            hints.push('Required');
        }

        if (field.validation.minLength) {
            hints.push(`Min ${field.validation.minLength} chars`);
        }

        if (field.validation.maxLength) {
            hints.push(`Max ${field.validation.maxLength} chars`);
        }

        if (field.validation.min !== null && field.validation.min !== undefined) {
            hints.push(`Min ${field.validation.min}`);
        }

        if (field.validation.max !== null && field.validation.max !== undefined) {
            hints.push(`Max ${field.validation.max}`);
        }

        if (field.validation.pattern) {
            hints.push('Pattern validation');
        }

        if (field.validation.email) {
            hints.push('Valid email required');
        }

        if (field.validation.remote) {
            hints.push('Remote validation');
        }

        return hints.join(' • ');
    }

    getFieldIcon(field: ProfileMetadataField): string {
        switch (field.inputType) {
            case 'TEXT':
                return 'bi-input-cursor-text';
            case 'EMAIL':
                return 'bi-envelope';
            case 'TEL':
                return 'bi-telephone';
            case 'NUMBER':
                return 'bi-123';
            case 'DATE':
                return 'bi-calendar-date';
            case 'SELECT':
                return 'bi-list-ul';
            case 'CHECKBOX':
                return 'bi-check-square';
            case 'FILE':
                return 'bi-file-earmark-arrow-up';
            case 'HIDDEN':
                return 'bi-eye-slash';
            default:
                return 'bi-input-cursor';
        }
    }

    isHiddenField(field: ProfileMetadataField): boolean {
        return field.inputType === 'HIDDEN';
    }
}
