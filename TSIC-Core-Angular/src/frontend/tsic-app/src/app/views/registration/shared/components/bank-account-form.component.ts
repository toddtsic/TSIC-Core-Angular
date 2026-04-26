import { ChangeDetectionStrategy, Component, EventEmitter, Output, Input, OnInit, OnChanges, DestroyRef, inject, SimpleChanges } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';

/**
 * Customer-facing eCheck (ACH) bank account form.
 *
 * Visual + structural mirror of CreditCardFormComponent — same prefill/output
 * contract, same field-input styling, same blank-and-pristine prefill rule.
 *
 * Field caps follow Authorize.Net's bankAccountType XSD:
 *   - routingNumber: exactly 9 digits
 *   - accountNumber: 4–17 alphanumeric chars
 *   - nameOnAccount: ≤22 chars
 *   - accountType: checking | savings | businessChecking
 */
@Component({
    selector: 'app-bank-account-form',
    standalone: true,
    imports: [ReactiveFormsModule],
    template: `
    <section class="ba-form-section" aria-labelledby="ba-title">
      <h6 id="ba-title" class="ba-form-heading">
        <i class="bi bi-bank me-2"></i>Bank Account (eCheck)
      </h6>
      <div class="alert alert-info border-0 small mb-3" role="status">
        <i class="bi bi-info-circle me-1"></i>
        Your registration will be marked <strong>pending</strong> until your bank confirms the debit (typically 3–5 business days).
      </div>
      <form [formGroup]="form" (ngSubmit)="noop()">
        <!-- Personal Information First -->
        <div class="row g-2">
          <div class="col-md-6">
            <label for="ba-firstName" class="form-label small mb-1">First Name</label>
            <input id="ba-firstName" class="form-control form-control-sm" formControlName="firstName" aria-required="true"
              aria-describedby="ba-firstName-err" [attr.aria-invalid]="form.get('firstName')?.invalid && form.get('firstName')?.touched">
            @if (err('firstName')) {
              <div id="ba-firstName-err" class="form-text text-danger" role="alert">{{ err('firstName') }}</div>
            }
          </div>
          <div class="col-md-6">
            <label for="ba-lastName" class="form-label small mb-1">Last Name</label>
            <input id="ba-lastName" class="form-control form-control-sm" formControlName="lastName" aria-required="true"
              aria-describedby="ba-lastName-err" [attr.aria-invalid]="form.get('lastName')?.invalid && form.get('lastName')?.touched">
            @if (err('lastName')) {
              <div id="ba-lastName-err" class="form-text text-danger" role="alert">{{ err('lastName') }}</div>
            }
          </div>
        </div>
        <div class="row g-2 mt-2">
          <div class="col-md-8">
            <label for="ba-address" class="form-label small mb-1">Address</label>
            <input id="ba-address" class="form-control form-control-sm" formControlName="address" aria-required="true"
              aria-describedby="ba-address-err" [attr.aria-invalid]="form.get('address')?.invalid && form.get('address')?.touched">
            @if (err('address')) {
              <div id="ba-address-err" class="form-text text-danger" role="alert">{{ err('address') }}</div>
            }
          </div>
          <div class="col-md-4">
            <label for="ba-zip" class="form-label small mb-1">Zip Code</label>
            <input id="ba-zip" class="form-control form-control-sm" formControlName="zip" aria-required="true"
              aria-describedby="ba-zip-err" [attr.aria-invalid]="form.get('zip')?.invalid && form.get('zip')?.touched">
            @if (err('zip')) {
              <div id="ba-zip-err" class="form-text text-danger" role="alert">{{ err('zip') }}</div>
            }
          </div>
        </div>
        <div class="row g-2 mt-2">
          <div class="col-md-6">
            <label for="ba-email" class="form-label small mb-1">Email</label>
            <input id="ba-email" class="form-control form-control-sm" formControlName="email" autocomplete="email" aria-required="true"
              aria-describedby="ba-email-err" [attr.aria-invalid]="form.get('email')?.invalid && form.get('email')?.touched">
            @if (err('email')) {
              <div id="ba-email-err" class="form-text text-danger" role="alert">{{ err('email') }}</div>
            }
          </div>
          <div class="col-md-6">
            <label for="ba-phone" class="form-label small mb-1">Phone</label>
            <input id="ba-phone" class="form-control form-control-sm" formControlName="phone" (input)="formatPhone()" autocomplete="tel" aria-required="true"
              aria-describedby="ba-phone-err" [attr.aria-invalid]="form.get('phone')?.invalid && form.get('phone')?.touched">
            @if (err('phone')) {
              <div id="ba-phone-err" class="form-text text-danger" role="alert">{{ err('phone') }}</div>
            }
          </div>
        </div>

        <!-- Bank Details -->
        <hr class="form-divider my-3">
        <div class="row g-2">
          <div class="col-md-4">
            <label for="ba-accountType" class="form-label small mb-1">Account Type</label>
            <select id="ba-accountType" class="form-select form-select-sm" formControlName="accountType" required aria-required="true"
              aria-describedby="ba-accountType-err" [attr.aria-invalid]="form.get('accountType')?.invalid && form.get('accountType')?.touched">
              <option value=""></option>
              <option value="checking">Checking</option>
              <option value="savings">Savings</option>
              <option value="businessChecking">Business Checking</option>
            </select>
            @if (err('accountType')) {
              <div id="ba-accountType-err" class="form-text text-danger" role="alert">{{ err('accountType') }}</div>
            }
          </div>
          <div class="col-md-4">
            <label for="ba-routing" class="form-label small mb-1">Routing Number</label>
            <input id="ba-routing" class="form-control form-control-sm" formControlName="routingNumber"
              (input)="formatRouting()" inputmode="numeric" autocomplete="off"
              placeholder="9 digits" aria-required="true"
              aria-describedby="ba-routing-help ba-routing-err"
              [attr.aria-invalid]="form.get('routingNumber')?.invalid && form.get('routingNumber')?.touched">
            @if (!err('routingNumber')) {
              <div id="ba-routing-help" class="form-text">9-digit ABA routing number</div>
            }
            @if (err('routingNumber')) {
              <div id="ba-routing-err" class="form-text text-danger" role="alert">{{ err('routingNumber') }}</div>
            }
          </div>
          <div class="col-md-4">
            <label for="ba-account" class="form-label small mb-1">Account Number</label>
            <input id="ba-account" class="form-control form-control-sm" formControlName="accountNumber"
              (input)="formatAccount()" inputmode="numeric" autocomplete="off"
              placeholder="4–17 chars" aria-required="true"
              aria-describedby="ba-account-err"
              [attr.aria-invalid]="form.get('accountNumber')?.invalid && form.get('accountNumber')?.touched">
            @if (err('accountNumber')) {
              <div id="ba-account-err" class="form-text text-danger" role="alert">{{ err('accountNumber') }}</div>
            }
          </div>
        </div>
        <div class="row g-2 mt-2">
          <div class="col-md-12">
            <label for="ba-nameOnAccount" class="form-label small mb-1">Name on Account</label>
            <input id="ba-nameOnAccount" class="form-control form-control-sm" formControlName="nameOnAccount"
              maxlength="22" aria-required="true"
              aria-describedby="ba-nameOnAccount-help ba-nameOnAccount-err"
              [attr.aria-invalid]="form.get('nameOnAccount')?.invalid && form.get('nameOnAccount')?.touched">
            @if (!err('nameOnAccount')) {
              <div id="ba-nameOnAccount-help" class="form-text">Must match the bank account exactly (max 22 characters)</div>
            }
            @if (err('nameOnAccount')) {
              <div id="ba-nameOnAccount-err" class="form-text text-danger" role="alert">{{ err('nameOnAccount') }}</div>
            }
          </div>
        </div>
      </form>
    </section>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BankAccountFormComponent implements OnInit, OnChanges {
    @Input() defaultFirstName: string | null = null;
    @Input() defaultLastName: string | null = null;
    @Input() defaultAddress: string | null = null;
    @Input() defaultZip: string | null = null;
    @Input() defaultEmail: string | null = null;
    @Input() defaultPhone: string | null = null;
    @Input() defaultNameOnAccount: string | null = null;

    @Output() validChange = new EventEmitter<boolean>();
    @Output() valueChange = new EventEmitter<Record<string, string>>();

    private readonly fb = inject(FormBuilder);
    private readonly destroyRef = inject(DestroyRef);

    form = this.fb.group({
        accountType: ['', Validators.required],
        routingNumber: ['', [Validators.required, this.routingValidator]],
        accountNumber: ['', [Validators.required, this.accountValidator]],
        nameOnAccount: ['', [Validators.required, this.nameValidator]],
        firstName: ['', Validators.required],
        lastName: ['', Validators.required],
        address: ['', Validators.required],
        zip: ['', [Validators.required, this.zipValidator]],
        email: ['', [Validators.required, this.emailValidator]],
        phone: ['', [Validators.required, this.phoneValidator]]
    });

    ngOnInit(): void {
        this.form.valueChanges.pipe(
            takeUntilDestroyed(this.destroyRef)
        ).subscribe(v => {
            this.validChange.emit(this.form.valid);
            this.valueChange.emit(v as Record<string, string>);
        });
        this.applyDefaultsOnce();
    }

    ngOnChanges(changes: SimpleChanges): void {
        if (changes['defaultFirstName'] || changes['defaultLastName'] || changes['defaultAddress']
            || changes['defaultZip'] || changes['defaultEmail'] || changes['defaultPhone']
            || changes['defaultNameOnAccount']) {
            this.applyDefaultsOnce();
        }
    }

    noop() { }

    err(name: string): string | null {
        const c = this.form.get(name);
        if (!c || (c.pristine && !c.touched)) return null;
        if (!c.errors) return null;
        if (c.errors['required']) return 'Required';
        if (c.errors['routing']) return c.errors['routing'];
        if (c.errors['account']) return c.errors['account'];
        if (c.errors['name']) return c.errors['name'];
        if (c.errors['zip']) return c.errors['zip'];
        if (c.errors['email']) return c.errors['email'];
        if (c.errors['phone']) return c.errors['phone'];
        return 'Invalid';
    }

    private applyDefaultsOnce(): void {
        const setIfBlank = (controlName: string, value: string | null | undefined) => {
            if (!value) return;
            const ctrl = this.form.get(controlName);
            if (!ctrl) return;
            const raw = String(ctrl.value || '').trim();
            if (!raw && ctrl.pristine) ctrl.setValue(String(value).trim(), { emitEvent: true });
        };
        setIfBlank('firstName', this.defaultFirstName);
        setIfBlank('lastName', this.defaultLastName);
        setIfBlank('address', this.defaultAddress);
        setIfBlank('zip', this.defaultZip);
        setIfBlank('email', this.defaultEmail);
        setIfBlank('phone', this.defaultPhone);
        // If no explicit name-on-account default, fall back to "First Last" so the field
        // is not blank — most personal accounts match the cardholder's legal name.
        const fallbackName = this.defaultNameOnAccount
            || [this.defaultFirstName, this.defaultLastName].filter(Boolean).join(' ').trim()
            || null;
        setIfBlank('nameOnAccount', fallbackName);
    }

    formatRouting() {
        const ctrl = this.form.get('routingNumber');
        if (!ctrl) return;
        const digits = String(ctrl.value || '').replaceAll(/\D+/g, '').slice(0, 9);
        if (digits !== ctrl.value) ctrl.setValue(digits, { emitEvent: true });
    }

    formatAccount() {
        const ctrl = this.form.get('accountNumber');
        if (!ctrl) return;
        const cleaned = String(ctrl.value || '').replaceAll(/[^a-zA-Z0-9]+/g, '').slice(0, 17);
        if (cleaned !== ctrl.value) ctrl.setValue(cleaned, { emitEvent: true });
    }

    formatPhone() {
        const ctrl = this.form.get('phone');
        if (!ctrl) return;
        const digits = String(ctrl.value || '').replace(/\D+/g, '').slice(0, 15);
        if (digits !== ctrl.value) ctrl.setValue(digits, { emitEvent: true });
    }

    // Validators
    routingValidator(control: AbstractControl): ValidationErrors | null {
        const raw = String(control.value || '').replaceAll(/\D+/g, '');
        if (!raw) return { required: true };
        if (raw.length !== 9) return { routing: 'Must be 9 digits' };
        return null;
    }
    accountValidator(control: AbstractControl): ValidationErrors | null {
        const raw = String(control.value || '').trim();
        if (!raw) return { required: true };
        if (raw.length < 4 || raw.length > 17) return { account: '4–17 characters' };
        if (!/^[a-zA-Z0-9]+$/.test(raw)) return { account: 'Letters and digits only' };
        return null;
    }
    nameValidator(control: AbstractControl): ValidationErrors | null {
        const raw = String(control.value || '').trim();
        if (!raw) return { required: true };
        if (raw.length > 22) return { name: 'Max 22 characters' };
        return null;
    }
    zipValidator(control: AbstractControl): ValidationErrors | null {
        const raw = String(control.value || '').trim();
        if (!raw) return { required: true };
        if (!/^[0-9A-Za-z -]{3,10}$/.test(raw)) return { zip: 'Invalid zip format' };
        return null;
    }
    emailValidator(control: AbstractControl): ValidationErrors | null {
        const raw = String(control.value || '').trim();
        if (!raw) return { required: true };
        if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(raw)) return { email: 'Invalid email' };
        return null;
    }
    phoneValidator(control: AbstractControl): ValidationErrors | null {
        const raw = String(control.value || '').replace(/\D+/g, '');
        if (!raw) return { required: true };
        if (raw.length < 7 || raw.length > 15) return { phone: 'Invalid length' };
        return null;
    }
}
