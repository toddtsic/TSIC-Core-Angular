import { Component, EventEmitter, Output, Input, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors, FormGroup } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-credit-card-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="cc-title"
             style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
      <h6 id="cc-title" class="fw-semibold mb-2">Credit Card Information</h6>
      @if (viOnly) {
        <div class="alert alert-secondary border-0" role="status">
          Your TSIC registration balance is $0. The card details below are for Vertical Insure only.
        </div>
      }
      <form [formGroup]="form" (ngSubmit)="noop()">
        <div class="row g-2">
          <div class="col-md-3">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>CC Type</mat-label>
              <mat-select formControlName="type" required>
                <mat-option value=""></mat-option>
                <mat-option value="MC">MC</mat-option>
                <mat-option value="VISA">VISA</mat-option>
                <mat-option value="AMEX">AMEX</mat-option>
              </mat-select>
              @if (err('type')) { <mat-error>{{ err('type') }}</mat-error> }
            </mat-form-field>
          </div>
          <div class="col-md-4">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Card Number</mat-label>
              <input matInput formControlName="number" (input)="formatNumber()" autocomplete="cc-number" />
              @if (err('number')) { <mat-error>{{ err('number') }}</mat-error> }
            </mat-form-field>
          </div>
          <div class="col-md-3">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Expiry (MM / YY)</mat-label>
              <input matInput id="cc-expiry" formControlName="expiry"
                     (input)="formatExpiry($event)" (blur)="forceMonthLeadingZero()"
                     placeholder="MM / YY" inputmode="numeric" autocomplete="cc-exp" aria-describedby="cc-expiry-help" />
              @if (!err('expiry')) { <mat-hint id="cc-expiry-help">Enter month and year, e.g. 04 / 27</mat-hint> }
              @if (err('expiry')) { <mat-error>{{ err('expiry') }}</mat-error> }
            </mat-form-field>
          </div>
          <div class="col-md-2">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>CVV</mat-label>
              <input matInput formControlName="code" (input)="formatCvv()" autocomplete="cc-csc" />
              @if (err('code')) { <mat-error>{{ err('code') }}</mat-error> }
            </mat-form-field>
          </div>
        </div>
        <div class="row g-2 mt-2">
          <div class="col-md-6">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>First Name</mat-label>
              <input matInput formControlName="firstName" autocomplete="given-name" />
              @if (err('firstName')) { <mat-error>{{ err('firstName') }}</mat-error> }
            </mat-form-field>
          </div>
          <div class="col-md-6">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Last Name</mat-label>
              <input matInput formControlName="lastName" autocomplete="family-name" />
              @if (err('lastName')) { <mat-error>{{ err('lastName') }}</mat-error> }
            </mat-form-field>
          </div>
        </div>
        <div class="row g-2 mt-2">
          <div class="col-md-8">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Address</mat-label>
              <input matInput formControlName="address" autocomplete="address-line1" />
              @if (err('address')) { <mat-error>{{ err('address') }}</mat-error> }
            </mat-form-field>
          </div>
          <div class="col-md-4">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Zip Code</mat-label>
              <input matInput formControlName="zip" autocomplete="postal-code" />
              @if (err('zip')) { <mat-error>{{ err('zip') }}</mat-error> }
            </mat-form-field>
          </div>
        </div>
        <div class="row g-2 mt-2">
          <div class="col-md-6">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Email</mat-label>
              <input matInput formControlName="email" autocomplete="email" />
              @if (err('email')) { <mat-error>{{ err('email') }}</mat-error> }
            </mat-form-field>
          </div>
          <div class="col-md-6">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Phone</mat-label>
              <input matInput formControlName="phone" (input)="formatPhone()" autocomplete="tel" />
              @if (err('phone')) { <mat-error>{{ err('phone') }}</mat-error> }
            </mat-form-field>
          </div>
        </div>
      </form>
    </section>
  `
})
export class CreditCardFormComponent implements OnInit, OnChanges {
  @Input() viOnly: boolean = false;
  // Default prefill values (from family user or ccInfo). Only applied to blank, pristine fields.
  @Input() defaultFirstName: string | null = null;
  @Input() defaultLastName: string | null = null;
  @Input() defaultAddress: string | null = null;
  @Input() defaultZip: string | null = null;
  @Input() defaultEmail: string | null = null;
  @Input() defaultPhone: string | null = null;
  // Original outputs retained for compatibility
  @Output() ccValidChange = new EventEmitter<boolean>();
  @Output() ccValueChange = new EventEmitter<any>();
  // Additional outputs matching payment component template expectations (no aliasing)
  @Output() validChange = new EventEmitter<boolean>();
  @Output() valueChange = new EventEmitter<any>();

  form!: FormGroup;

  constructor(private readonly fb: FormBuilder) {
    this.form = this.fb.group({
      type: ['', Validators.required],
      number: ['', [Validators.required, this.numberValidator.bind(this)]],
      expiry: ['', [Validators.required, this.expiryValidator]],
      code: ['', [Validators.required, this.cvvValidator.bind(this)]],
      firstName: ['', Validators.required],
      lastName: ['', Validators.required],
      address: ['', Validators.required],
      zip: ['', [Validators.required, this.zipValidator]],
      email: ['', [Validators.required, this.emailValidator]],
      phone: ['', [Validators.required, this.phoneValidator]]
    });
  }
  ngOnInit(): void {
    this.form.valueChanges.subscribe(v => {
      const valid = this.form.valid;
      this.ccValidChange.emit(valid);
      this.validChange.emit(valid);
      this.ccValueChange.emit(v);
      this.valueChange.emit(v);
    });
    this.applyDefaultsOnce();
  }
  ngOnChanges(changes: SimpleChanges): void {
    if (changes['defaultFirstName'] || changes['defaultLastName'] || changes['defaultAddress'] || changes['defaultZip'] || changes['defaultEmail'] || changes['defaultPhone']) {
      this.applyDefaultsOnce();
    }
  }

  noop() { }

  err(name: string): string | null {
    const c = this.form.get(name);
    if (!c || (c.pristine && !c.touched)) return null;
    if (!c.errors) return null;
    if (c.errors['required']) return 'Required';
    if (c.errors['length']) return c.errors['length'];
    if (c.errors['luhn']) return 'Invalid number';
    if (c.errors['expiry']) return c.errors['expiry'];
    if (c.errors['cvv']) return c.errors['cvv'];
    if (c.errors['zip']) return c.errors['zip'];
    return 'Invalid';
  }

  private applyDefaultsOnce(): void {
    // Only patch untouched blank fields to avoid overwriting user input.
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
  }

  formatNumber() {
    const ctrl = this.form.get('number');
    if (!ctrl) return;
    const digits = (ctrl.value || '').split(/\D+/).join('').slice(0, 16);
    ctrl.setValue(digits, { emitEvent: true });
  }
  formatExpiry(ev?: Event) {
    const ctrl = this.form.get('expiry');
    if (!ctrl) return;
    let raw = String(ctrl.value || '');
    // Strip non-digits
    const digits = raw.replaceAll(/\D+/g, '').slice(0, 4);
    let formatted = digits;
    if (digits.length >= 3) {
      formatted = digits.slice(0, 2) + ' / ' + digits.slice(2);
    } else if (digits.length >= 1) {
      // Allow single or two-digit month without slash until third digit typed
      formatted = digits;
    }
    ctrl.setValue(formatted, { emitEvent: true });
    // Optional caret preservation (basic): move cursor to end on auto insert
    if (ev && ev.target instanceof HTMLInputElement) {
      const el = ev.target; el.selectionStart = el.selectionEnd = formatted.length;
    }
  }
  forceMonthLeadingZero() {
    const ctrl = this.form.get('expiry');
    if (!ctrl) return;
    const val = String(ctrl.value || '').replaceAll(/\s+/g, '');
    const digits = val.replaceAll(/\D+/g, '').slice(0, 4);
    if (!digits) return;
    let mm = digits.slice(0, 2);
    let yy = digits.slice(2);
    if (mm.length === 1) mm = '0' + mm; // force leading zero on blur
    const out = yy ? mm + ' / ' + yy : mm;
    if (out !== ctrl.value) ctrl.setValue(out, { emitEvent: true });
  }
  formatCvv() {
    const type = (this.form.get('type')?.value || '').toUpperCase();
    const ctrl = this.form.get('code');
    if (!ctrl) return;
    const maxLen = type === 'AMEX' ? 4 : 3;
    const digits = (ctrl.value || '').split(/\D+/).join('').slice(0, maxLen);
    ctrl.setValue(digits, { emitEvent: true });
  }

  // Validators
  numberValidator(control: AbstractControl): ValidationErrors | null {
    const raw = String(control.value || '').split(/\D+/).join('');
    if (!raw) return { required: true };
    const type = (this.form?.get('type')?.value || '').toUpperCase();
    const lenOk = type === 'AMEX' ? raw.length === 15 : (raw.length >= 13 && raw.length <= 16);
    if (!lenOk) return { length: 'Invalid length' };
    if (!this.luhnValid(raw)) return { luhn: true };
    return null;
  }
  expiryValidator(control: AbstractControl): ValidationErrors | null {
    const raw = String(control.value || '').trim();
    if (!raw) return { required: true };
    // Accept MMYY or MM / YY (with optional spaces)
    const digits = raw.replaceAll(/\D+/g, '');
    if (digits.length !== 4) return { expiry: 'Use MM / YY' };
    const mm = Number.parseInt(digits.slice(0, 2), 10);
    const yyTwo = digits.slice(2); // e.g. 27 -> 2027
    const yy = Number.parseInt('20' + yyTwo, 10);
    if (Number.isNaN(mm) || mm < 1 || mm > 12) return { expiry: 'Invalid month' };
    // Consider card valid through end of month
    const expDate = new Date(yy, mm, 0, 23, 59, 59, 999);
    const now = new Date();
    if (expDate < now) return { expiry: 'Card expired' };
    // Optional: reject far-future ( > 15 years )
    if (yy - now.getFullYear() > 15) return { expiry: 'Year too far' };
    return null;
  }
  cvvValidator(control: AbstractControl): ValidationErrors | null {
    const type = (this.form?.get('type')?.value || '').toUpperCase();
    const raw = String(control.value || '').trim();
    if (!raw) return { required: true };
    const lenOk = type === 'AMEX' ? raw.length === 4 : raw.length === 3;
    if (!lenOk) return { cvv: 'Invalid CVV length' };
    if (!/^\d+$/.test(raw)) return { cvv: 'Digits only' };
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
    // Simple pattern for now
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(raw)) return { email: 'Invalid email' };
    return null;
  }
  phoneValidator(control: AbstractControl): ValidationErrors | null {
    const raw = String(control.value || '').replace(/\D+/g, '');
    if (!raw) return { required: true };
    if (raw.length < 7 || raw.length > 15) return { phone: 'Invalid length' };
    return null;
  }
  formatPhone() {
    const ctrl = this.form.get('phone');
    if (!ctrl) return;
    const digits = String(ctrl.value || '').replace(/\D+/g, '').slice(0, 15);
    ctrl.setValue(digits, { emitEvent: true });
  }

  private luhnValid(num: string): boolean {
    let sum = 0; let alt = false;
    for (let i = num.length - 1; i >= 0; i--) {
      let n = Number.parseInt(num.charAt(i), 10);
      if (alt) { n *= 2; if (n > 9) n -= 9; }
      sum += n; alt = !alt;
    }
    return (sum % 10) === 0;
  }
}
