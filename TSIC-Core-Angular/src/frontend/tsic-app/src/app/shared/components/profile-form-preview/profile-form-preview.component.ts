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
    @Input() readonly = false; // Allow interaction to test dropdowns

    private readonly _metadata = signal<ProfileMetadata | null>(null);
    readonly _jobOptions = signal<Record<string, unknown> | null>(null);
    formGroup = signal<FormGroup | null>(null);

    // Computed sorted fields by order (sorting is done during migration)
    sortedFields = computed(() => {
        const meta = this._metadata();
        if (!meta) return [];
        return [...meta.fields].sort((a, b) => a.order - b.order);
    });

    getDropdownOptions(field: ProfileMetadataField): Array<{ value: string; label: string }> {
        // Call _jobOptions() to establish reactive dependency
        const jobOptions = this._jobOptions();
        return this.computeDropdownOptions(field, jobOptions);
    }

    private computeDropdownOptions(
        field: ProfileMetadataField,
        jobOptions: Record<string, unknown> | null
    ): Array<{ value: string; label: string }> {
        // PRIORITY 1: Use options from field metadata (populated during migration)
        if (field.options && field.options.length > 0) {
            return field.options;
        }

        if (!field.dataSource) return [];

        // PRIORITY 2: Try job-specific JsonOptions
        if (jobOptions) {
            const normalize = (s: string) => (s.toLowerCase().match(/[a-z0-9]/g) || []).join('');

            const dsNorm = normalize(field.dataSource);
            const candidates = new Set<string>();
            candidates.add(dsNorm);

            const stripPrefix = (s: string, prefix: string) => s.startsWith(prefix) ? s.substring(prefix.length) : s;
            const noList = stripPrefix(dsNorm, 'list');
            const noListSizes = stripPrefix(dsNorm, 'listsizes');
            candidates.add(noList);
            candidates.add(noListSizes);
            candidates.add('list' + noList);
            candidates.add('listsizes' + noList);

            const idx = dsNorm.indexOf('sizes');
            if (idx >= 0) {
                let before = dsNorm.substring(0, idx);
                let after = dsNorm.substring(idx + 'sizes'.length);
                before = normalize(before);
                after = normalize(after);
                if (after) {
                    candidates.add(before + after + 'sizes');
                    candidates.add('list' + after + 'sizes');
                    candidates.add(after + 'sizes');
                }
            }

            const keys = Object.keys(jobOptions);
            const optionsKey = keys.find(key => {
                const nk = normalize(key);
                for (const cand of candidates) {
                    if (!cand) continue;
                    if (nk.includes(cand) || cand.includes(nk)) {
                        return true;
                    }
                }
                return false;
            });

            if (optionsKey) {
                const optionsArray = jobOptions[optionsKey];
                if (Array.isArray(optionsArray)) {
                    return optionsArray.map((opt: any) => ({
                        value: opt.Value || opt.value || '',
                        label: opt.Text || opt.text || opt.label || ''
                    }));
                }
            }
        }

        // PRIORITY 3: Mock data fallback
        return this.fieldDataService.getOptionsForDataSource(field.dataSource);
    }

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

    getValidationHint(field: ProfileMetadataField): string {
        if (!field.validation) return '';

        const hints: string[] = [];

        if (field.validation.required) {
            hints.push('Required');
        }

        if (field.validation.requiredTrue) {
            hints.push('Must be checked');
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
        // Check visibility property first, fallback to inputType for backward compatibility
        return field.visibility === 'hidden' || field.inputType === 'HIDDEN';
    }

    getVisibilityBadge(field: ProfileMetadataField): { class: string; label: string } | null {
        switch (field.visibility) {
            case 'hidden':
                return { class: 'bg-dark', label: 'Hidden' };
            case 'adminOnly':
                return { class: 'bg-warning text-dark', label: 'Admin Only' };
            default:
                return null; // No badge for public fields
        }
    }
}
