import { ChangeDetectionStrategy, Component, inject, signal, effect } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { FamilyStateService } from '../state/family-state.service';

/**
 * Family wizard v2 — Address step.
 * Collects street address, city, state (dropdown), postal code.
 */
@Component({
    selector: 'app-fam-address-step',
    standalone: true,
    imports: [ReactiveFormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        <h5 class="mb-1 fw-semibold">Address</h5>
        <p class="wizard-tip">Enter your family's mailing address.</p>

        <div [formGroup]="form" class="row g-3">
          <div class="col-12">
            <label class="field-label" for="v2-addr1">Street Address</label>
            <input id="v2-addr1" type="text" formControlName="address1" class="field-input"
                   [class.is-required]="!form.controls.address1.value?.trim()"
                   [class.is-invalid]="touched() && form.controls.address1.invalid" (blur)="syncToState()" />
            @if (touched() && form.controls.address1.errors?.['required']) { <div class="field-error">Required</div> }
          </div>
          <div class="col-12 col-md-6">
            <label class="field-label" for="v2-city">City</label>
            <input id="v2-city" type="text" formControlName="city" class="field-input"
                   [class.is-required]="!form.controls.city.value?.trim()"
                   [class.is-invalid]="touched() && form.controls.city.invalid" (blur)="syncToState()" />
            @if (touched() && form.controls.city.errors?.['required']) { <div class="field-error">Required</div> }
          </div>
          <div class="col-6 col-md-3">
            <label class="field-label" for="v2-state">State</label>
            <select id="v2-state" formControlName="state" class="field-input field-select"
                    [class.is-required]="!form.controls.state.value"
                    [class.is-invalid]="touched() && form.controls.state.invalid" (change)="syncToState()">
              <option value="" disabled>Select</option>
              @for (s of statesOptions; track s.value) {
                <option [value]="s.value">{{ s.label }}</option>
              }
            </select>
            @if (touched() && form.controls.state.errors?.['required']) { <div class="field-error">Required</div> }
          </div>
          <div class="col-6 col-md-3">
            <label class="field-label" for="v2-postal">Postal code</label>
            <input id="v2-postal" type="text" formControlName="postalCode" class="field-input"
                   [class.is-required]="!form.controls.postalCode.value?.trim()"
                   [class.is-invalid]="touched() && form.controls.postalCode.invalid" (blur)="syncToState()" />
            @if (touched() && form.controls.postalCode.errors?.['required']) { <div class="field-error">Required</div> }
          </div>
        </div>
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddressStepComponent {
    private readonly fb = inject(FormBuilder);
    private readonly state = inject(FamilyStateService);
    private readonly fieldData = inject(FormFieldDataService);

    readonly touched = signal(false);
    readonly statesOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');

    readonly form = this.fb.group({
        address1: ['', [Validators.required]],
        city: ['', [Validators.required]],
        state: ['', [Validators.required]],
        postalCode: ['', [Validators.required]],
    });

    constructor() {
        // Sync from state when profile loads asynchronously (edit mode)
        effect(() => {
            const a = this.state.address();
            this.form.patchValue({
                address1: a.address1, city: a.city,
                state: a.state, postalCode: a.postalCode,
            }, { emitEvent: false });
        });
    }

    syncToState(): void {
        this.touched.set(true);
        const v = this.form.value;
        this.state.setAddress({
            address1: v.address1 ?? '', city: v.city ?? '',
            state: v.state ?? '', postalCode: v.postalCode ?? '',
        });
    }
}
