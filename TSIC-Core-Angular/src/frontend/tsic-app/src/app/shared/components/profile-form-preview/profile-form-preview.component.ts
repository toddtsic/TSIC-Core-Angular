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

    @Input() set jobOptions(value: Record<string, unknown> | null) {
        this._jobOptions.set(value);
    }

    @Input() showFieldNumbers = true;
    @Input() showValidationHints = true;
    @Input() readonly = true;

    private readonly _metadata = signal<ProfileMetadata | null>(null);
    private readonly _jobOptions = signal<Record<string, unknown> | null>(null);
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
        // PRIORITY 1: Use options from field metadata (populated during migration with job-specific data)
        if (field.options && field.options.length > 0) {
            return field.options;
        }

        // Return empty array if no dataSource specified
        if (!field.dataSource) return [];

        // PRIORITY 2: Try to get options from job-specific JsonOptions (for preview with job selector)
        const jobOptions = this._jobOptions();
        if (jobOptions) {
            // Try to find matching key in JsonOptions
            // JsonOptions keys might be like "List_Positions", "ListSizes_Jersey", etc.
            const optionsKey = Object.keys(jobOptions).find(key =>
                key.toLowerCase().includes(field.dataSource!.toLowerCase())
            );

            if (optionsKey) {
                const rawOptions = jobOptions[optionsKey];
                if (Array.isArray(rawOptions)) {
                    // Convert JsonOptions format to our format
                    return rawOptions.map((opt: any) => ({
                        value: opt.Value || opt.value || opt,
                        label: opt.Text || opt.text || opt.Value || opt.value || opt
                    }));
                }
            }
        }

        // PRIORITY 3: Fallback to mock data service (for unmigrated profiles or preview without job selection)
        return this.fieldDataService.getOptionsForDataSource(field.dataSource!);
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
