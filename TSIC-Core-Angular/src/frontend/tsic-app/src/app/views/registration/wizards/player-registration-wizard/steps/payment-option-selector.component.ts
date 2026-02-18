import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PaymentService } from '../services/payment.service';
import { RegistrationWizardService } from '../registration-wizard.service';

@Component({
  selector: 'app-payment-option-selector',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, FormsModule],
  template: `
    <section class="p-3 p-sm-4 mb-3 rounded-3" aria-labelledby="pay-option-title"
             style="background: var(--bs-secondary-bg); border: 1px solid var(--bs-border-color-translucent)">
      <h6 id="pay-option-title" class="fw-semibold mb-3">Payment Option</h6>

      @if (state.jobHasActiveDiscountCodes()) {
        <div class="mb-3">
          <label for="discountCode" class="form-label small mb-1 me-2 d-block d-md-inline">Discount Code</label>
          <div class="input-group w-auto d-inline-flex align-items-center">
            <input id="discountCode" type="text" [(ngModel)]="code" class="form-control" placeholder="Enter code"
                   [disabled]="svc.discountApplying()" style="min-width: 180px; min-height: 2.75rem;">
            <button type="button" class="btn btn-outline-primary" (click)="apply()" [disabled]="svc.discountApplying() || !code" style="min-height: 2.75rem;">Apply</button>
          </div>
          @if (svc.discountMessage()) {
            <div class="form-text mt-1"
                 [class.text-success]="svc.appliedDiscount() > 0"
                 [class.text-danger]="svc.appliedDiscount() === 0">{{ svc.discountMessage() }}</div>
          }
        </div>
      }

      <div role="radiogroup" aria-labelledby="pay-option-title">
      @if (svc.isArbScenario()) {
        <div class="form-check">
          <input class="form-check-input" type="radio" id="arb" name="payOpt" [checked]="state.paymentOption() === 'ARB'" (change)="choose('ARB')">
          <label class="form-check-label" for="arb">
            Automated Recurring Billing (ARB)
            <div class="small text-muted">
              {{ svc.arbOccurrences() }} payments of {{ svc.arbPerOccurrence() | currency }} every
              {{ svc.arbIntervalLength() }} {{ svc.monthLabel() }} starting {{ svc.arbStartDate() | date:'mediumDate' }}
            </div>
          </label>
        </div>
        <div class="form-check">
          <input class="form-check-input" type="radio" id="pifArb" name="payOpt" [checked]="state.paymentOption() === 'PIF'" (change)="choose('PIF')">
          <label class="form-check-label" for="pifArb">Pay In Full - {{ svc.totalAmount() | currency }}</label>
        </div>
      } @else if (svc.isDepositScenario()) {
        <div class="form-check">
          <input class="form-check-input" type="radio" id="dep" name="payOpt" [checked]="state.paymentOption() === 'Deposit'" (change)="choose('Deposit')">
          <label class="form-check-label" for="dep">Deposit Only - {{ svc.depositTotal() | currency }}</label>
        </div>
        <div class="form-check">
          <input class="form-check-input" type="radio" id="pifDep" name="payOpt" [checked]="state.paymentOption() === 'PIF'" (change)="choose('PIF')">
          <label class="form-check-label" for="pifDep">Pay In Full - {{ svc.totalAmount() | currency }}</label>
        </div>
      } @else {
        <div class="form-check">
          <input class="form-check-input" type="radio" id="pifOnly" name="payOpt" checked (change)="$event.preventDefault()">
          <label class="form-check-label" for="pifOnly">Pay In Full - {{ svc.totalAmount() | currency }}</label>
        </div>
      }
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PaymentOptionSelectorComponent {
  code = '';
  public readonly svc = inject(PaymentService);
  public readonly state = inject(RegistrationWizardService);

  constructor() { }

  choose(opt: 'PIF' | 'Deposit' | 'ARB') {
    this.state.paymentOption.set(opt);
    this.svc.resetDiscount();
  }

  apply() { this.svc.applyDiscount(this.code.trim()); }
}
