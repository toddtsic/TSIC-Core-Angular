import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';
import type { AdultRegField } from '@infrastructure/services/adult-registration.service';

@Component({
    selector: 'app-adult-profile-step',
    standalone: true,
    imports: [FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="step-content">
            <h3 class="step-title">Profile Information</h3>

            @if (state.schemaLoading()) {
                <div class="text-center py-4">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Loading...</span>
                    </div>
                </div>
            } @else if (state.schemaError()) {
                <div class="alert alert-danger" role="alert">{{ state.schemaError() }}</div>
            } @else {
                <div class="row g-3">
                    @for (field of visibleFields(); track field.name) {
                        @if (shouldShowField(field)) {
                            <div [class]="getFieldColClass(field)">
                                <label class="field-label">
                                    {{ field.displayName }}
                                    @if (field.validation?.required) {
                                        <span class="text-danger">*</span>
                                    }
                                </label>

                                @switch (normalizeInputType(field.inputType)) {
                                    @case ('textarea') {
                                        <textarea class="field-input"
                                            rows="3"
                                            [ngModel]="getFieldValue(field.name)"
                                            (ngModelChange)="onFieldChange(field.name, $event)"
                                            [placeholder]="field.validation?.message ?? ''"
                                            [maxlength]="field.validation?.maxLength ?? null"></textarea>
                                    }
                                    @case ('checkbox') {
                                        <div class="form-check">
                                            <input type="checkbox" class="form-check-input"
                                                [id]="'field-' + field.name"
                                                [ngModel]="getFieldValue(field.name)"
                                                (ngModelChange)="onFieldChange(field.name, $event)" />
                                            <label class="form-check-label" [for]="'field-' + field.name">
                                                {{ field.displayName }}
                                            </label>
                                        </div>
                                    }
                                    @case ('date') {
                                        <input type="date" class="field-input"
                                            [ngModel]="getFieldValue(field.name)"
                                            (ngModelChange)="onFieldChange(field.name, $event)" />
                                    }
                                    @case ('select') {
                                        <select class="field-select"
                                            [ngModel]="getFieldValue(field.name)"
                                            (ngModelChange)="onFieldChange(field.name, $event)">
                                            <option value="">-- Select --</option>
                                            @for (opt of field.options; track opt.value) {
                                                <option [value]="opt.value">{{ opt.label }}</option>
                                            }
                                        </select>
                                    }
                                    @default {
                                        <input type="text" class="field-input"
                                            [ngModel]="getFieldValue(field.name)"
                                            (ngModelChange)="onFieldChange(field.name, $event)"
                                            [placeholder]="field.validation?.message ?? ''"
                                            [maxlength]="field.validation?.maxLength ?? null" />
                                    }
                                }
                            </div>
                        }
                    }
                </div>
            }
        </div>
    `,
})
export class ProfileStepComponent {
    readonly state = inject(AdultWizardStateService);

    readonly visibleFields = this.state.formFields;

    normalizeInputType(inputType: string): string {
        return (inputType ?? 'text').toLowerCase();
    }

    getFieldColClass(field: AdultRegField): string {
        const type = this.normalizeInputType(field.inputType);
        return type === 'textarea' ? 'col-12' : 'col-md-6';
    }

    getFieldValue(fieldName: string): string | number | boolean | null {
        return this.state.formValues()[fieldName] ?? null;
    }

    onFieldChange(fieldName: string, value: string | number | boolean | null): void {
        this.state.setFieldValue(fieldName, value);
    }

    shouldShowField(field: AdultRegField): boolean {
        if (field.visibility === 'hidden' || field.visibility === 'adminOnly') return false;
        if (!field.conditionalOn) return true;

        const depValue = this.state.formValues()[field.conditionalOn.field];
        const expected = field.conditionalOn.value;
        const op = field.conditionalOn.operator ?? 'equals';

        if (op === 'equals') return depValue == expected;
        if (op === 'notEquals') return depValue != expected;
        return true;
    }
}
