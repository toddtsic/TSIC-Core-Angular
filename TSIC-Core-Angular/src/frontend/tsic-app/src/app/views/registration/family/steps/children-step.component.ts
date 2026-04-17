import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, ValidatorFn, AbstractControl } from '@angular/forms';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { FamilyService } from '@infrastructure/services/family.service';
import { FamilyStateService } from '../state/family-state.service';

/**
 * Family wizard v2 — Children step.
 * Manages a dynamic list of children. Each child is added via a form,
 * then stored in the state service. Navigation handled by shell.
 */
@Component({
    selector: 'app-fam-children-step',
    standalone: true,
    imports: [ReactiveFormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        <h5 class="mb-1 fw-semibold">Add Player</h5>

        <!-- Children list -->
        <div class="mb-4">
          @if (state.children().length === 0) {
            <div class="alert alert-info mb-3">Add at least one player to continue.</div>
          }
          @if (state.children().length > 0) {
            <div class="border border-primary rounded p-3 bg-primary-subtle">
              <h6 class="fw-semibold mb-3">{{ state.children().length === 1 ? 'Player 1 added' : state.children().length + ' players added' }}</h6>
              <ul class="list-group mb-0">
                @for (c of state.children(); track $index) {
                  <li class="list-group-item d-flex justify-content-between align-items-center"
                      [class.editing-row]="editingIndex() === $index">
                    <div>
                      <div class="fw-semibold">{{ c.firstName }} {{ c.lastName }}</div>
                      @if (c.dob) { <div class="text-muted small">DOB: {{ formatDob(c.dob) }}</div> }
                      @if (c.email) { <div class="text-muted small">Email: {{ c.email }}</div> }
                      @if (c.phone) { <div class="text-muted small">Cell: {{ formatPhone(c.phone) }}</div> }
                    </div>
                    <div class="d-flex gap-1 align-items-center">
                      <button type="button" class="icon-action" (click)="edit($index)"
                              [attr.aria-label]="'Edit ' + c.firstName">
                        <i class="bi bi-pencil-fill"></i>
                      </button>
                      @if (c.hasRegistrations) {
                        <span class="icon-locked" title="Has registrations — cannot remove">
                          <i class="bi bi-lock-fill"></i>
                        </span>
                      } @else if (removing() === $index) {
                        <span class="icon-locked">
                          <span class="spinner-border spinner-border-sm text-danger"></span>
                        </span>
                      } @else {
                        <button type="button" class="icon-action icon-danger" (click)="remove($index)"
                                [attr.aria-label]="'Remove ' + c.firstName">
                          <i class="bi bi-trash3-fill"></i>
                        </button>
                      }
                    </div>
                  </li>
                }
              </ul>
            </div>
          }
        </div>

        <!-- Add child form -->
        <div class="card border-0 mb-3" [class.form-editing]="editingIndex() !== null"
             [class.bg-body-tertiary]="editingIndex() === null">
          <div class="card-body">
            <form [formGroup]="form" (ngSubmit)="addChild()" class="row g-3">
              <div class="col-12 col-md-3">
                <label class="field-label" for="v2-childFirst">First name</label>
                <input id="v2-childFirst" type="text" formControlName="firstName" class="field-input"
                       [class.is-required]="!form.controls.firstName.value?.trim()"
                       [class.is-invalid]="submitted() && form.controls.firstName.invalid" />
                @if (submitted() && form.controls.firstName.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12 col-md-3">
                <label class="field-label" for="v2-childLast">Last name</label>
                <input id="v2-childLast" type="text" formControlName="lastName" class="field-input"
                       [class.is-required]="!form.controls.lastName.value?.trim()"
                       [class.is-invalid]="submitted() && form.controls.lastName.invalid" />
                @if (submitted() && form.controls.lastName.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12 col-md-3">
                <label class="field-label" for="v2-childGender">Gender</label>
                <select id="v2-childGender" formControlName="gender" class="field-input field-select"
                        [class.is-required]="!form.controls.gender.value"
                        [class.is-invalid]="submitted() && form.controls.gender.invalid">
                  <option value="" disabled>Select</option>
                  @for (g of genderOptions; track g.value) { <option [value]="g.value">{{ g.label }}</option> }
                </select>
                @if (submitted() && form.controls.gender.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12 col-md-3">
                <label class="field-label" for="v2-childDob">Date of birth</label>
                <input id="v2-childDob" type="date" formControlName="dob" class="field-input"
                       [attr.min]="minDob" [attr.max]="maxDob"
                       [class.is-required]="!form.controls.dob.value"
                       [class.is-invalid]="submitted() && form.controls.dob.invalid" />
                @if (submitted() && form.controls.dob.errors?.['required']) { <div class="field-error">Required</div> }
                @if (submitted() && (form.controls.dob.errors?.['ageTooYoung'] || form.controls.dob.errors?.['ageTooOld'])) {
                  <div class="field-error">Age must be between 2 and 99 years.</div>
                }
              </div>
              <div class="col-12 col-md-6">
                <label class="field-label" for="v2-childEmail">Email <span class="tip">(optional)</span></label>
                <input id="v2-childEmail" type="email" formControlName="email" class="field-input"
                       [class.is-invalid]="submitted() && form.controls.email.errors?.['email']" />
                @if (submitted() && form.controls.email.errors?.['email']) { <div class="field-error">Invalid email</div> }
              </div>
              <div class="col-12 col-md-6">
                <label class="field-label" for="v2-childPhone">Cellphone <span class="tip">(optional)</span></label>
                <input id="v2-childPhone" type="tel" inputmode="numeric" formControlName="phone" class="field-input"
                       autocomplete="off" placeholder="Numbers only"
                       (input)="onDigitsOnly($event)"
                       [class.is-invalid]="submitted() && form.controls.phone.errors?.['pattern']" />
                @if (submitted() && form.controls.phone.errors?.['pattern']) { <div class="field-error">Numbers only</div> }
              </div>
              <div class="col-12 d-flex gap-2 justify-content-end">
                <button type="submit" class="btn btn-outline-primary">
                  {{ editingIndex() !== null ? 'Save changes' : 'Add Player' }}
                </button>
                @if (editingIndex() !== null) {
                  <button type="button" class="btn btn-outline-secondary" (click)="cancelEdit()">Cancel</button>
                }
              </div>
            </form>
          </div>
        </div>
      </div>
    </div>
  `,
    styles: [`
      .icon-action {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 32px;
        height: 32px;
        border: none;
        background: none;
        border-radius: var(--radius-sm);
        color: var(--neutral-500);
        cursor: pointer;
        transition: color 0.15s ease, background-color 0.15s ease;

        &:hover {
          color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.08);
        }

        &.icon-danger:hover {
          color: var(--bs-danger);
          background: rgba(var(--bs-danger-rgb), 0.08);
        }
      }

      .icon-locked {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 32px;
        height: 32px;
        color: var(--neutral-400);
      }

      .editing-row {
        border-color: var(--bs-primary) !important;
        background: rgba(var(--bs-primary-rgb), 0.04);
      }

      .form-editing {
        border: 2px solid var(--bs-primary) !important;
        background: rgba(var(--bs-primary-rgb), 0.03);
        animation: edit-pulse 1.5s ease-in-out 2;
      }

      @keyframes edit-pulse {
        0%, 100% { border-color: var(--bs-primary); }
        50% { border-color: rgba(var(--bs-primary-rgb), 0.3); }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildrenStepComponent {
    private readonly fb = inject(FormBuilder);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly familyApi = inject(FamilyService);
    readonly state = inject(FamilyStateService);
    readonly removing = signal<number | null>(null);

    readonly submitted = signal(false);
    readonly editingIndex = signal<number | null>(null);
    readonly genderOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('genders');

    readonly form = this.fb.group({
        firstName: ['', [Validators.required]],
        lastName: ['', [Validators.required]],
        gender: ['', [Validators.required]],
        dob: ['', [Validators.required, this.ageRangeValidator(2, 99)]],
        email: ['', [Validators.email]],
        phone: ['', [Validators.pattern(/^\d*$/)]],
    });

    get maxDob(): string {
        const d = new Date();
        d.setFullYear(d.getFullYear() - 2);
        return d.toISOString().slice(0, 10);
    }

    get minDob(): string {
        const d = new Date();
        d.setFullYear(d.getFullYear() - 99);
        return d.toISOString().slice(0, 10);
    }

    /**
     * If the form has unsaved input, commit it before the wizard advances.
     * Returns true if a commit happened or the form is empty; false if the form
     * has data but is invalid (caller should block navigation).
     */
    commitPending(): boolean {
        if (this.form.pristine) return true;
        const hasAnyValue = Object.values(this.form.value).some(v => !!(v ?? '').toString().trim());
        if (!hasAnyValue) return true;
        if (this.form.invalid) {
            this.submitted.set(true);
            return false;
        }
        this.addChild();
        return true;
    }

    readonly saving = signal(false);

    addChild(): void {
        this.submitted.set(true);
        if (this.form.invalid) return;
        const v = this.form.value;
        const child = {
            firstName: v.firstName ?? '',
            lastName: v.lastName ?? '',
            dob: v.dob || undefined,
            gender: v.gender ?? '',
            email: v.email || undefined,
            phone: v.phone || undefined,
        };
        const idx = this.editingIndex();
        if (idx !== null) {
            const existing = this.state.children()[idx];
            if (existing?.userId) {
                this.saving.set(true);
                this.familyApi.updateChild(existing.userId, child).subscribe({
                    next: () => {
                        this.state.updateChildAt(idx, { ...child, userId: existing.userId, hasRegistrations: existing.hasRegistrations });
                        this.editingIndex.set(null);
                        this.form.reset();
                        this.submitted.set(false);
                        this.saving.set(false);
                    },
                    error: (err) => {
                        console.error('[ChildrenStep] update failed', err);
                        this.saving.set(false);
                    },
                });
            } else {
                this.state.updateChildAt(idx, { ...child, userId: existing?.userId, hasRegistrations: existing?.hasRegistrations });
                this.editingIndex.set(null);
                this.form.reset();
                this.submitted.set(false);
            }
        } else {
            this.saving.set(true);
            this.familyApi.addChild(child).subscribe({
                next: (res) => {
                    this.state.addChild({ ...child, userId: res.childUserId ?? undefined });
                    this.form.reset();
                    this.submitted.set(false);
                    this.saving.set(false);
                },
                error: (err) => {
                    console.error('[ChildrenStep] add failed', err);
                    this.saving.set(false);
                },
            });
        }
    }

    edit(index: number): void {
        const c = this.state.children()[index];
        if (!c) return;
        this.form.patchValue({
            firstName: c.firstName,
            lastName: c.lastName,
            gender: c.gender,
            dob: c.dob ?? '',
            email: c.email ?? '',
            phone: c.phone ?? '',
        });
        this.editingIndex.set(index);
        this.submitted.set(false);
    }

    cancelEdit(): void {
        this.editingIndex.set(null);
        this.form.reset();
        this.submitted.set(false);
    }

    remove(index: number): void {
        const child = this.state.children()[index];
        if (!child) return;
        if (this.editingIndex() === index) this.cancelEdit();

        // New child (no userId) — just remove locally
        if (!child.userId) {
            this.state.removeChildAt(index);
            return;
        }

        // Existing child — delete via API, then remove locally
        this.removing.set(index);
        this.familyApi.removeChild(child.userId).subscribe({
            next: () => {
                this.state.removeChildAt(index);
                this.removing.set(null);
            },
            error: (err) => {
                console.error('[ChildrenStep] remove failed', err);
                this.removing.set(null);
            },
        });
    }

    formatDob(iso: string): string {
        // ISO yyyy-mm-dd → MM/DD/YYYY (avoid Date parsing to dodge UTC off-by-one)
        const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
        return m ? `${m[2]}/${m[3]}/${m[1]}` : iso;
    }

    formatPhone(raw: string): string {
        const d = (raw || '').replaceAll(/\D+/g, '');
        if (d.length === 10) return `${d.slice(0, 3)}-${d.slice(3, 6)}-${d.slice(6)}`;
        if (d.length === 11 && d.startsWith('1')) return `${d.slice(1, 4)}-${d.slice(4, 7)}-${d.slice(7)}`;
        return raw;
    }

    onDigitsOnly(ev: Event): void {
        const input = ev.target as HTMLInputElement;
        const digits = (input.value || '').replaceAll(/\D+/g, '');
        if (digits !== input.value) {
            this.form.controls.phone.setValue(digits, { emitEvent: false });
        }
    }

    private ageRangeValidator(minYears: number, maxYears: number): ValidatorFn {
        return (control: AbstractControl) => {
            const val: string | null = control.value;
            if (!val) return null;
            const dob = new Date(val);
            if (Number.isNaN(dob.getTime())) return null;
            const today = new Date();
            let age = today.getFullYear() - dob.getFullYear();
            const m = today.getMonth() - dob.getMonth();
            if (m < 0 || (m === 0 && today.getDate() < dob.getDate())) age--;
            if (age < minYears) return { ageTooYoung: true };
            if (age > maxYears) return { ageTooOld: true };
            return null;
        };
    }
}
