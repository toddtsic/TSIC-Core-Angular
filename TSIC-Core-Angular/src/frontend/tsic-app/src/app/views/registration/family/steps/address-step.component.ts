import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { AutofocusDirective } from '@shared-ui/directives/autofocus.directive';
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
    imports: [ReactiveFormsModule, AutofocusDirective],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        <h5 class="mb-1 fw-semibold">Address</h5>
        <p class="wizard-tip">Enter your player's/family's mailing address.</p>

        <div [formGroup]="form" class="row g-3">
          <div class="col-12">
            <label class="field-label" for="v2-addr1">Street Address</label>
            <input id="v2-addr1" type="text" formControlName="address1" class="field-input" appAutofocus
                   [class.is-required]="!form.controls.address1.value?.trim()"
                   [class.is-invalid]="touched() && form.controls.address1.invalid" (input)="syncToState()" (blur)="syncToState()" />
            @if (touched() && form.controls.address1.errors?.['required']) { <div class="field-error">Required</div> }
          </div>
          <div class="col-12 col-md-6">
            <label class="field-label" for="v2-city">City</label>
            <input id="v2-city" type="text" formControlName="city" class="field-input"
                   [class.is-required]="!form.controls.city.value?.trim()"
                   [class.is-invalid]="touched() && form.controls.city.invalid" (input)="syncToState()" (blur)="syncToState()" />
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
                   [class.is-invalid]="touched() && form.controls.postalCode.invalid" (input)="syncToState()" (blur)="syncToState()" />
            @if (touched() && form.controls.postalCode.errors?.['required']) { <div class="field-error">Required</div> }
            @if (touched() && form.controls.postalCode.errors?.['minlength']) { <div class="field-error">Must be at least 5 characters</div> }
          </div>
        </div>

        @if (state.mode() === 'edit') {
          <div class="d-flex align-items-center gap-2 justify-content-end mt-4">
            @if (state.profileSaved()) {
              <span class="text-success small"><i class="bi bi-check-circle-fill me-1"></i>Saved</span>
            }
            <button type="button" class="btn btn-outline-primary"
                    [disabled]="!canUpdate() || state.profileSaving()"
                    (click)="update()">
              @if (state.profileSaving()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              }
              Update Address
            </button>
          </div>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddressStepComponent {
    private readonly fb = inject(FormBuilder);
    readonly state = inject(FamilyStateService);
    private readonly fieldData = inject(FormFieldDataService);

    readonly touched = signal(false);
    readonly statesOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');

    readonly form = this.fb.group({
        address1: ['', [Validators.required]],
        city: ['', [Validators.required]],
        state: ['', [Validators.required]],
        postalCode: ['', [Validators.required, Validators.minLength(5)]],
    });

    constructor() {
        // Seed the form when state arrives asynchronously (edit mode: loadProfile() resolves
        // after this step mounts on a deep link, so a one-shot ngOnInit seed would miss it).
        //
        // Patch only the controls that actually drifted. syncToState() writes a fresh object
        // into state on every keystroke, so this re-emits on our own writes; patching the
        // control being typed in re-assigns input.value and jumps the caret to the end.
        toObservable(this.state.address)
            .pipe(takeUntilDestroyed())
            .subscribe(a => this.patchDrift({
                address1: a.address1, city: a.city,
                state: a.state, postalCode: a.postalCode,
            }));
    }

    /** Patch only the controls whose value differs from `next`. */
    private patchDrift(next: Record<string, string>): void {
        const current = this.form.getRawValue() as Record<string, string>;
        const drift: Record<string, string> = {};
        for (const key of Object.keys(next)) {
            if (current[key] !== next[key]) drift[key] = next[key];
        }
        if (Object.keys(drift).length > 0) {
            this.form.patchValue(drift, { emitEvent: false });
        }
    }

    syncToState(): void {
        this.touched.set(true);
        this.state.clearSavedFlag();
        const v = this.form.value;
        this.state.setAddress({
            address1: v.address1 ?? '', city: v.city ?? '',
            state: v.state ?? '', postalCode: v.postalCode ?? '',
        });
    }

    canUpdate(): boolean {
        return this.state.hasValidAddress();
    }

    update(): void {
        this.syncToState();
        this.state.persistProfile();
    }
}
