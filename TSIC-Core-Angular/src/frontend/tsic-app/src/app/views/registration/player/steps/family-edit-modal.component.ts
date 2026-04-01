import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { FamilyService } from '@infrastructure/services/family.service';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import type { FamilyUpdateRequest, ChildDto } from '@core/api';

/**
 * Quick-edit modal for family account contacts + address.
 * Loads current profile on init, saves via PUT /api/family/update.
 */
@Component({
    selector: 'app-family-edit-modal',
    standalone: true,
    imports: [ReactiveFormsModule, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="closed.emit()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title"><i class="bi bi-people-fill me-2"></i>Edit Family Account</h5>
          <button type="button" class="btn-close" (click)="closed.emit()" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          @if (loading()) {
            <div class="text-center py-4">
              <span class="spinner-border text-primary"></span>
            </div>
          } @else {
            <form [formGroup]="form">
              <!-- Parent 1 -->
              <h6 class="section-heading"><i class="bi bi-person-fill me-1"></i>Primary Contact</h6>
              <div class="row g-2 mb-2">
                <div class="col-6">
                  <label class="form-label small fw-medium mb-1" for="fem-p1f">First Name</label>
                  <input id="fem-p1f" class="form-control form-control-sm" formControlName="p1First"
                         [class.is-invalid]="submitted() && form.controls.p1First.invalid" />
                </div>
                <div class="col-6">
                  <label class="form-label small fw-medium mb-1" for="fem-p1l">Last Name</label>
                  <input id="fem-p1l" class="form-control form-control-sm" formControlName="p1Last"
                         [class.is-invalid]="submitted() && form.controls.p1Last.invalid" />
                </div>
                <div class="col-6">
                  <label class="form-label small fw-medium mb-1" for="fem-p1ph">Cellphone</label>
                  <input id="fem-p1ph" type="tel" inputmode="numeric" class="form-control form-control-sm"
                         formControlName="p1Phone" (input)="digitsOnly('p1Phone', $event)" />
                </div>
                <div class="col-6">
                  <label class="form-label small fw-medium mb-1" for="fem-p1e">Email</label>
                  <input id="fem-p1e" type="email" class="form-control form-control-sm" formControlName="p1Email"
                         [class.is-invalid]="submitted() && form.controls.p1Email.invalid" />
                </div>
              </div>

              <hr class="form-divider">

              <!-- Parent 2 -->
              <h6 class="section-heading"><i class="bi bi-person me-1"></i>Secondary Contact <span class="text-muted fw-normal">(optional)</span></h6>
              <div class="row g-2 mb-2">
                <div class="col-6">
                  <label class="form-label small fw-medium mb-1" for="fem-p2f">First Name</label>
                  <input id="fem-p2f" class="form-control form-control-sm" formControlName="p2First" />
                </div>
                <div class="col-6">
                  <label class="form-label small fw-medium mb-1" for="fem-p2l">Last Name</label>
                  <input id="fem-p2l" class="form-control form-control-sm" formControlName="p2Last" />
                </div>
                <div class="col-6">
                  <label class="form-label small fw-medium mb-1" for="fem-p2ph">Cellphone</label>
                  <input id="fem-p2ph" type="tel" inputmode="numeric" class="form-control form-control-sm"
                         formControlName="p2Phone" (input)="digitsOnly('p2Phone', $event)" />
                </div>
                <div class="col-6">
                  <label class="form-label small fw-medium mb-1" for="fem-p2e">Email</label>
                  <input id="fem-p2e" type="email" class="form-control form-control-sm" formControlName="p2Email" />
                </div>
              </div>

              <hr class="form-divider">

              <!-- Address -->
              <h6 class="section-heading"><i class="bi bi-house-door me-1"></i>Address</h6>
              <div class="row g-2">
                <div class="col-12">
                  <label class="form-label small fw-medium mb-1" for="fem-addr">Street Address</label>
                  <input id="fem-addr" class="form-control form-control-sm" formControlName="address"
                         [class.is-invalid]="submitted() && form.controls.address.invalid" />
                </div>
                <div class="col-5">
                  <label class="form-label small fw-medium mb-1" for="fem-city">City</label>
                  <input id="fem-city" class="form-control form-control-sm" formControlName="city"
                         [class.is-invalid]="submitted() && form.controls.city.invalid" />
                </div>
                <div class="col-4">
                  <label class="form-label small fw-medium mb-1" for="fem-state">State</label>
                  <select id="fem-state" class="form-select form-select-sm" formControlName="state"
                          [class.is-invalid]="submitted() && form.controls.state.invalid">
                    <option value="" disabled>Select</option>
                    @for (s of stateOptions; track s.value) {
                      <option [value]="s.value">{{ s.label }}</option>
                    }
                  </select>
                </div>
                <div class="col-3">
                  <label class="form-label small fw-medium mb-1" for="fem-zip">Zip</label>
                  <input id="fem-zip" class="form-control form-control-sm" formControlName="zip"
                         [class.is-invalid]="submitted() && form.controls.zip.invalid" />
                </div>
              </div>
            </form>

            @if (errorMsg()) {
              <div class="alert alert-danger mt-3 mb-0 py-2 small">{{ errorMsg() }}</div>
            }
          }
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="closed.emit()">Cancel</button>
          <button type="button" class="btn btn-sm btn-primary fw-semibold" (click)="save()" [disabled]="saving() || loading()">
            @if (saving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>Saving...
            } @else {
              <i class="bi bi-check-lg me-1"></i>Save Changes
            }
          </button>
        </div>
      </div>
    </tsic-dialog>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FamilyEditModalComponent implements OnInit {
    readonly saved = output<void>();
    readonly closed = output<void>();

    private readonly fb = inject(FormBuilder);
    private readonly familyService = inject(FamilyService);
    private readonly auth = inject(AuthService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly stateOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');
    readonly loading = signal(true);
    readonly saving = signal(false);
    readonly submitted = signal(false);
    readonly errorMsg = signal<string | null>(null);

    // Preserved from load — sent back unchanged on save
    private username = '';
    private children: ChildDto[] = [];

    readonly form = this.fb.group({
        p1First: ['', Validators.required],
        p1Last: ['', Validators.required],
        p1Phone: [''],
        p1Email: ['', [Validators.required, Validators.email]],
        p2First: [''],
        p2Last: [''],
        p2Phone: [''],
        p2Email: [''],
        address: ['', Validators.required],
        city: ['', Validators.required],
        state: ['', Validators.required],
        zip: ['', Validators.required],
    });

    ngOnInit(): void {
        this.familyService.getMyFamily()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (profile) => {
                    this.username = profile.username;
                    this.children = profile.children ?? [];
                    this.form.patchValue({
                        p1First: profile.primary?.firstName ?? '',
                        p1Last: profile.primary?.lastName ?? '',
                        p1Phone: profile.primary?.cellphone ?? '',
                        p1Email: profile.primary?.email ?? '',
                        p2First: profile.secondary?.firstName ?? '',
                        p2Last: profile.secondary?.lastName ?? '',
                        p2Phone: profile.secondary?.cellphone ?? '',
                        p2Email: profile.secondary?.email ?? '',
                        address: profile.address?.streetAddress ?? '',
                        city: profile.address?.city ?? '',
                        state: profile.address?.state ?? '',
                        zip: profile.address?.postalCode ?? '',
                    });
                    this.loading.set(false);
                },
                error: () => {
                    this.loading.set(false);
                    this.errorMsg.set('Failed to load family profile.');
                },
            });
    }

    digitsOnly(controlName: string, event: Event): void {
        const input = event.target as HTMLInputElement;
        const digits = input.value.replace(/\D+/g, '').slice(0, 15);
        input.value = digits;
        this.form.get(controlName)?.setValue(digits);
    }

    save(): void {
        this.submitted.set(true);
        if (this.form.invalid) return;

        this.saving.set(true);
        this.errorMsg.set(null);

        const v = this.form.value;
        const request: FamilyUpdateRequest = {
            username: this.username,
            primary: {
                firstName: v.p1First ?? '',
                lastName: v.p1Last ?? '',
                cellphone: v.p1Phone ?? '',
                email: v.p1Email ?? '',
            },
            secondary: {
                firstName: v.p2First ?? '',
                lastName: v.p2Last ?? '',
                cellphone: v.p2Phone ?? '',
                email: v.p2Email ?? '',
            },
            address: {
                streetAddress: v.address ?? '',
                city: v.city ?? '',
                state: v.state ?? '',
                postalCode: v.zip ?? '',
            },
            children: this.children,
        };

        this.familyService.updateFamily(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp) => {
                    this.saving.set(false);
                    if (resp.success) {
                        this.toast.show('Family account updated', 'success', 2000);
                        this.saved.emit();
                    } else {
                        this.errorMsg.set(resp.message || 'Update failed.');
                    }
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Request failed.');
                },
            });
    }
}
