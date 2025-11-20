import { Component, EventEmitter, Output, Input, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';

@Component({
    selector: 'app-credit-card-form',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
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
            <label class="form-label">CC Type</label>
            <select class="form-select" formControlName="type">
              <option value=""></option>
              <option value="MC">MC</option>
              <option value="VISA">VISA</option>
              <option value="AMEX">AMEX</option>
            </select>
            <div class="form-text text-danger" *ngIf="err('type')">{{ err('type') }}</div>
          </div>
          <div class="col-md-4">
            <label class="form-label">Card Number</label>
            <input class="form-control" formControlName="number" (input)="formatNumber()">
            <div class="form-text text-danger" *ngIf="err('number')">{{ err('number') }}</div>
          </div>
          <div class="col-md-3">
            <label class="form-label">Expiry (MMYY)</label>
            <input class="form-control" formControlName="expiry" (input)="formatExpiry()">
            <div class="form-text text-danger" *ngIf="err('expiry')">{{ err('expiry') }}</div>
          </div>
          <div class="col-md-2">
            <label class="form-label">CVV</label>
            <input class="form-control" formControlName="code" (input)="formatCvv()">
            <div class="form-text text-danger" *ngIf="err('code')">{{ err('code') }}</div>
          </div>
        </div>
        <div class="row g-2 mt-2">
          <div class="col-md-6">
            <label class="form-label">First Name</label>
            <input class="form-control" formControlName="firstName">
            <div class="form-text text-danger" *ngIf="err('firstName')">{{ err('firstName') }}</div>
          </div>
          <div class="col-md-6">
            <label class="form-label">Last Name</label>
            <input class="form-control" formControlName="lastName">
            <div class="form-text text-danger" *ngIf="err('lastName')">{{ err('lastName') }}</div>
          </div>
        </div>
        <div class="row g-2 mt-2">
          <div class="col-md-8">
            <label class="form-label">Address</label>
            <input class="form-control" formControlName="address">
            <div class="form-text text-danger" *ngIf="err('address')">{{ err('address') }}</div>
          </div>
          <div class="col-md-4">
            <label class="form-label">Zip Code</label>
            <input class="form-control" formControlName="zip">
            <div class="form-text text-danger" *ngIf="err('zip')">{{ err('zip') }}</div>
          </div>
        </div>
      </form>
    </section>
  `
})
export class CreditCardFormComponent implements OnInit {
    @Input() viOnly: boolean = false;
    @Output() ccValidChange = new EventEmitter<boolean>();
    @Output() ccValueChange = new EventEmitter<any>();

    form = this.fb.group({
        type: ['', Validators.required],
        number: ['', [Validators.required, this.numberValidator.bind(this)]],
        expiry: ['', [Validators.required, this.expiryValidator]],
        code: ['', [Validators.required, this.cvvValidator.bind(this)]],
        firstName: ['', Validators.required],
        lastName: ['', Validators.required],
        address: ['', Validators.required],
        zip: ['', [Validators.required, this.zipValidator]]
    });

    constructor(private readonly fb: FormBuilder) { }
    ngOnInit(): void {
        this.form.valueChanges.subscribe(v => {
            this.ccValidChange.emit(this.form.valid);
            this.ccValueChange.emit(v);
        });
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

    formatNumber() {
        const ctrl = this.form.get('number');
        if (!ctrl) return;
        const digits = (ctrl.value || '').replace(/\D+/g, '').slice(0, 16);
        ctrl.setValue(digits, { emitEvent: true });
    }
    formatExpiry() {
        const ctrl = this.form.get('expiry');
        if (!ctrl) return;
        const digits = (ctrl.value || '').replace(/\D+/g, '').slice(0, 4);
        ctrl.setValue(digits, { emitEvent: true });
    }
    formatCvv() {
        const type = (this.form.get('type')?.value || '').toUpperCase();
        const ctrl = this.form.get('code');
        if (!ctrl) return;
        const maxLen = type === 'AMEX' ? 4 : 3;
        const digits = (ctrl.value || '').replace(/\D+/g, '').slice(0, maxLen);
        ctrl.setValue(digits, { emitEvent: true });
    }

    // Validators
    numberValidator(control: AbstractControl): ValidationErrors | null {
        const raw = String(control.value || '').replace(/\D+/g, '');
        if (!raw) return { required: true };
        const type = (this.form?.get('type')?.value || '').toUpperCase();
        const lenOk = type === 'AMEX' ? raw.length === 15 : (raw.length >= 13 && raw.length <= 16);
        if (!lenOk) return { length: 'Invalid length' };
        if (!this.luhnValid(raw)) return { luhn: true };
        return null;
    }
    expiryValidator(control: AbstractControl): ValidationErrors | null {
        const v = String(control.value || '').trim();
        if (!v) return { required: true };
        if (!/^\d{4}$/.test(v)) return { expiry: 'Use MMYY' };
        const mm = Number.parseInt(v.slice(0, 2), 10);
        const yy = Number.parseInt('20' + v.slice(2), 10);
        if (mm < 1 || mm > 12) return { expiry: 'Invalid month' };
        const lastDay = new Date(yy, mm, 0);
        if (lastDay < new Date()) return { expiry: 'Card expired' };
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
