import { ChangeDetectionStrategy, Component, Input, OnInit, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { FamilyService } from '@infrastructure/services/family.service';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { ToastService } from '@shared-ui/toast.service';
import type { ChildDto } from '@core/api';

/**
 * Modal for adding or editing a child/player in the family account.
 * Used inline from the player-selection step.
 */
@Component({
    selector: 'app-player-form-modal',
    standalone: true,
    imports: [FormsModule, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="sm" (requestClose)="closed.emit()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title">
            <i class="bi me-2" [class.bi-person-plus-fill]="mode === 'add'" [class.bi-pencil-square]="mode === 'edit'"></i>
            {{ mode === 'add' ? 'Add Player' : 'Edit Player' }}
          </h5>
          <button type="button" class="btn-close" (click)="closed.emit()" aria-label="Close"></button>
        </div>
        <div class="modal-body">
          <div class="row g-2">
            <div class="col-6">
              <label for="pfm-first" class="form-label small fw-medium mb-1">First Name</label>
              <input id="pfm-first" type="text" class="form-control form-control-sm"
                     [value]="firstName()" (input)="firstName.set($any($event.target).value)"
                     [class.is-invalid]="submitted() && !firstName().trim()" />
              @if (submitted() && !firstName().trim()) {
                <div class="invalid-feedback">Required</div>
              }
            </div>
            <div class="col-6">
              <label for="pfm-last" class="form-label small fw-medium mb-1">Last Name</label>
              <input id="pfm-last" type="text" class="form-control form-control-sm"
                     [value]="lastName()" (input)="lastName.set($any($event.target).value)"
                     [class.is-invalid]="submitted() && !lastName().trim()" />
              @if (submitted() && !lastName().trim()) {
                <div class="invalid-feedback">Required</div>
              }
            </div>
            <div class="col-6">
              <label for="pfm-gender" class="form-label small fw-medium mb-1">Gender</label>
              <select id="pfm-gender" class="form-select form-select-sm"
                      [ngModel]="gender()" (ngModelChange)="gender.set($event)"
                      [class.is-invalid]="submitted() && !gender()">
                <option value="">— Select —</option>
                @for (opt of genderOptions; track opt.value) {
                  <option [value]="opt.value">{{ opt.label }}</option>
                }
              </select>
              @if (submitted() && !gender()) {
                <div class="invalid-feedback">Required</div>
              }
            </div>
            <div class="col-6">
              <label for="pfm-dob" class="form-label small fw-medium mb-1">Date of Birth</label>
              <input id="pfm-dob" type="date" class="form-control form-control-sm"
                     [value]="dob()" (input)="dob.set($any($event.target).value)" />
            </div>
          </div>
          <hr class="form-divider my-3">
          <div class="row g-2">
            <div class="col-6">
              <label for="pfm-email" class="form-label small fw-medium mb-1">
                Email <span class="tip">(optional)</span>
              </label>
              <input id="pfm-email" type="email" class="form-control form-control-sm"
                     [value]="email()" (input)="email.set($any($event.target).value)" />
            </div>
            <div class="col-6">
              <label for="pfm-phone" class="form-label small fw-medium mb-1">
                Phone <span class="tip">(optional)</span>
              </label>
              <input id="pfm-phone" type="tel" inputmode="numeric" class="form-control form-control-sm"
                     [value]="phone()" (input)="onDigitsOnly($event)"
                     placeholder="Digits only" />
            </div>
          </div>

          @if (errorMsg()) {
            <div class="alert alert-danger mt-3 mb-0 py-2 small">{{ errorMsg() }}</div>
          }
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-sm btn-outline-secondary" (click)="closed.emit()">Cancel</button>
          <button type="button" class="btn btn-sm btn-primary fw-semibold" (click)="save()" [disabled]="saving()">
            @if (saving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>Saving...
            } @else {
              <i class="bi me-1" [class.bi-plus-circle]="mode === 'add'" [class.bi-check-lg]="mode === 'edit'"></i>
              {{ mode === 'add' ? 'Add Player' : 'Save Changes' }}
            }
          </button>
        </div>
      </div>
    </tsic-dialog>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerFormModalComponent implements OnInit {
    @Input() mode: 'add' | 'edit' = 'add';
    @Input() playerId: string | null = null;
    @Input() initialData: { firstName?: string; lastName?: string; gender?: string; dob?: string; email?: string; phone?: string } | null = null;

    readonly saved = output<void>();
    readonly closed = output<void>();

    private readonly familyService = inject(FamilyService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly toast = inject(ToastService);

    readonly genderOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('genders');

    readonly firstName = signal('');
    readonly lastName = signal('');
    readonly gender = signal('');
    readonly dob = signal('');
    readonly email = signal('');
    readonly phone = signal('');
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);

    ngOnInit(): void {
        if (this.mode === 'edit' && this.initialData) {
            this.firstName.set(this.initialData.firstName ?? '');
            this.lastName.set(this.initialData.lastName ?? '');
            this.gender.set(this.initialData.gender ?? '');
            this.dob.set(this.initialData.dob ?? '');
            this.email.set(this.initialData.email ?? '');
            this.phone.set(this.initialData.phone ?? '');
        }
    }

    onDigitsOnly(event: Event): void {
        const input = event.target as HTMLInputElement;
        const digits = input.value.replace(/\D+/g, '').slice(0, 15);
        input.value = digits;
        this.phone.set(digits);
    }

    save(): void {
        this.submitted.set(true);
        if (!this.firstName().trim() || !this.lastName().trim() || !this.gender()) return;

        this.saving.set(true);
        this.errorMsg.set(null);

        const dto: ChildDto = {
            firstName: this.firstName().trim(),
            lastName: this.lastName().trim(),
            gender: this.gender(),
            dob: this.dob() || null,
            email: this.email().trim() || null,
            phone: this.phone().trim() || null,
        };

        const request$ = this.mode === 'edit' && this.playerId
            ? this.familyService.updateChild(this.playerId, dto)
            : this.familyService.addChild(dto);

        request$.subscribe({
            next: (resp) => {
                this.saving.set(false);
                if (resp.success) {
                    this.toast.show(this.mode === 'add' ? 'Player added' : 'Player updated', 'success', 2000);
                    this.saved.emit();
                } else {
                    this.errorMsg.set(resp.message || 'An error occurred.');
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
