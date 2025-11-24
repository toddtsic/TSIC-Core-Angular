import { Component, EventEmitter, Output, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';

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
        @if (!confirmationLoaded()) {
          <p class="text-muted">Loading confirmation summary…</p>
        } @else {
          <button type="button" class="btn btn-outline-primary mb-3" [disabled]="resending()" (click)="onResendClick()">
            {{ resending() ? 'Sending…' : 'Re-Send Confirmation Email' }}
          </button>
          @if (resendMessage()) { <div class="small text-muted mb-2">{{ resendMessage() }}</div> }
          <div class="mt-3" [innerHTML]="conf()!.confirmationHtml"></div>

          <button type="button" class="btn btn-primary mt-3" (click)="completed.emit()">Finish</button>
        }
      </div>
    </div>
  `
})
export class ConfirmationComponent implements OnInit {
  @Output() completed = new EventEmitter<void>();
  readonly state = inject(RegistrationWizardService);

  ngOnInit(): void {
    // Trigger load once jobId & familyUser are known.
    // Simple polling for initial presence; avoids race without extra subscriptions.
    const tryLoad = () => {
      if (this.state.jobId() && this.state.familyUser()?.familyUserId) {
        this.state.loadConfirmation();
        return true;
      }
      return false;
    };
    if (!tryLoad()) {
      const timer = setInterval(() => { if (tryLoad()) clearInterval(timer); }, 250);
      setTimeout(() => clearInterval(timer), 4000); // safety stop
    }
  }

  conf = computed(() => this.state.confirmation());
  confirmationLoaded = computed(() => !!this.conf());
  resending = signal(false);
  resendMessage = signal<string>('');

  async onResendClick(): Promise<void> {
    if (this.resending()) return;
    this.resendMessage.set('');
    this.resending.set(true);
    const ok = await this.state.resendConfirmationEmail();
    this.resending.set(false);
    this.resendMessage.set(ok ? 'Confirmation email sent.' : 'Failed to send confirmation email.');
  }
}
