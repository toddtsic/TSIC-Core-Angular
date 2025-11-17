import { Component, EventEmitter, Output, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';
import { TeamService } from '../team.service';

@Component({
    selector: 'app-rw-confirmation',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Confirmation</h5>
      </div>
      <div class="card-body">
        <p class="text-success">Your payment was processed successfully.</p>
        <h6 class="mt-3">Summary</h6>
        <ul class="mb-3">
          <li>Players: {{ selectedPlayers().length }}</li>
          <li>Payment Option: {{ state.lastPayment()?.option || state.paymentOption() }}</li>
          <li>Amount: {{ state.lastPayment()?.amount || 0 | currency }}</li>
          @if (state.lastPayment()?.transactionId) {
            <li>Transaction ID: {{ state.lastPayment()?.transactionId }}</li>
          }
          @if (state.lastPayment()?.subscriptionId) {
            <li>Subscription ID: {{ state.lastPayment()?.subscriptionId }}</li>
          }
          @if (state.regSaverDetails()) {
            <li>RegSaver Policy: {{ state.regSaverDetails()!.policyNumber }} ({{ state.regSaverDetails()!.policyCreateDate | date:'mediumDate' }})</li>
          }
        </ul>
        @if (state.lastPayment()?.message) {
          <div class="alert alert-info border-0">{{ state.lastPayment()?.message }}</div>
        }
        <button type="button" class="btn btn-primary" (click)="completed.emit()">Finish</button>
      </div>
    </div>
  `
})
export class ConfirmationComponent {
    @Output() completed = new EventEmitter<void>();
    readonly state = inject(RegistrationWizardService);
    readonly teamService = inject(TeamService);

    selectedPlayers = computed(() => this.state.familyPlayers().filter(p => p.selected || p.registered));
}
