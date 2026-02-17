import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, inject, OnInit, OnDestroy, signal } from '@angular/core';

import { RegistrationWizardService } from '../registration-wizard.service';

@Component({
  selector: 'app-rw-confirmation',
  standalone: true,
  imports: [],
  styles: [`
    .confirmation-content {
      overflow-x: auto;
    }
    .confirmation-content ::ng-deep table {
      width: 100%;
      min-width: 600px;
    }
  `],
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
          <div class="confirmation-content mt-3" [innerHTML]="conf()!.confirmationHtml"></div>

          <button type="button" class="btn btn-primary mt-3" (click)="completed.emit()">Finish</button>
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ConfirmationComponent implements OnInit, OnDestroy {
  @Output() completed = new EventEmitter<void>();
  readonly state = inject(RegistrationWizardService);
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private safetyTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    const tryLoad = () => {
      if (this.state.jobId() && this.state.familyUser()?.familyUserId) {
        this.state.loadConfirmation();
        return true;
      }
      return false;
    };
    if (!tryLoad()) {
      this.pollTimer = setInterval(() => {
        if (tryLoad()) this.clearTimers();
      }, 250);
      this.safetyTimer = setTimeout(() => this.clearTimers(), 4000);
    }
  }

  ngOnDestroy(): void {
    this.clearTimers();
  }

  private clearTimers(): void {
    if (this.pollTimer) { clearInterval(this.pollTimer); this.pollTimer = null; }
    if (this.safetyTimer) { clearTimeout(this.safetyTimer); this.safetyTimer = null; }
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
