import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { MyRosterService } from './my-roster.service';
import type { BatchEmailResponse } from '@core/api/models/BatchEmailResponse';

@Component({
    selector: 'app-my-roster-email-dialog',
    standalone: true,
    imports: [CommonModule, FormsModule, TsicDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="close()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title">
            <i class="bi bi-envelope-fill me-2"></i>
            Email {{ mode() === 'all' ? 'All Teammates' : 'Selected Teammates' }}
          </h5>
          <button type="button" class="btn-close" (click)="close()" aria-label="Close"></button>
        </div>

        <div class="modal-body">
          <div class="mb-3">
            <label class="field-label">Recipients ({{ recipients().length }})</label>
            <div class="recipients-box">
              @if (recipients().length === 0) {
                <span class="text-body-secondary">No teammates have an email address on file.</span>
              } @else {
                @for (r of recipients(); track r.email) {
                  <span class="recipient-chip">{{ r.name }}</span>
                }
              }
            </div>
          </div>

          <div class="mb-3">
            <label for="mr-subject" class="field-label">Subject</label>
            <input id="mr-subject" type="text" class="field-input"
                   [ngModel]="subject()" (ngModelChange)="subject.set($event)"
                   [disabled]="isSending()"
                   placeholder="What's this about?" />
          </div>

          <div class="mb-2">
            <label for="mr-body" class="field-label">Message</label>
            <textarea id="mr-body" rows="8" class="field-input"
                      [ngModel]="body()" (ngModelChange)="body.set($event)"
                      [disabled]="isSending()"
                      placeholder="Write your message..."></textarea>
          </div>
          <p class="small text-body-secondary mb-0">
            Use <code>!PERSON</code> to address each teammate by name — it's replaced per recipient when the email is sent.
          </p>
        </div>

        <div class="modal-footer">
          <button type="button" class="btn btn-outline-secondary btn-sm"
                  (click)="close()" [disabled]="isSending()">Cancel</button>
          <button type="button" class="btn btn-primary btn-sm"
                  [disabled]="!canSend() || isSending()"
                  (click)="send()">
            @if (isSending()) {
              <span class="spinner-border spinner-border-sm me-1"></span>
            } @else {
              <i class="bi bi-send me-1"></i>
            }
            Send
          </button>
        </div>
      </div>
    </tsic-dialog>
  `,
    styles: [`
    :host { display: contents; }
    .recipients-box {
      display: flex; flex-wrap: wrap; gap: 6px;
      padding: 8px 10px;
      background: var(--bs-body-bg);
      border: 1px solid var(--brand-border);
      border-radius: var(--radius-sm, 6px);
      max-height: 120px; overflow-y: auto;
    }
    .recipient-chip {
      display: inline-flex; align-items: center;
      padding: 2px 8px;
      font-size: 0.8rem;
      background: var(--bs-primary-bg-subtle, rgba(13, 110, 253, 0.1));
      color: var(--bs-primary);
      border-radius: 999px;
    }
    code {
      background: var(--bs-body-bg);
      padding: 1px 4px;
      border-radius: 3px;
    }
  `],
})
export class MyRosterEmailDialogComponent {
    private readonly rosterService = inject(MyRosterService);
    private readonly toast = inject(ToastService);

    readonly mode = input.required<'all' | 'selected'>();
    readonly recipients = input.required<{ name: string; email: string }[]>();
    readonly registrationIds = input.required<string[]>();

    readonly closed = output<void>();
    readonly sent = output<BatchEmailResponse>();

    readonly subject = signal('');
    readonly body = signal('');
    readonly isSending = signal(false);

    readonly canSend = computed(() =>
        this.recipients().length > 0
        && this.subject().trim().length > 0
        && this.body().trim().length > 0);

    close(): void {
        if (this.isSending()) return;
        this.closed.emit();
    }

    send(): void {
        if (!this.canSend() || this.isSending()) return;
        this.isSending.set(true);

        const ids = this.mode() === 'all' ? null : this.registrationIds();

        this.rosterService.sendEmail({
            registrationIds: ids,
            subject: this.subject().trim(),
            bodyTemplate: this.body().trim(),
        }).subscribe({
            next: (response) => {
                this.isSending.set(false);
                const failedCount = response.failedAddresses?.length ?? 0;
                const note = response.optedOut > 0 ? `, ${response.optedOut} opted out` : '';
                const msg = `Emails sent: ${response.sent} of ${response.totalRecipients}${note}`;
                if (failedCount > 0) {
                    this.toast.show(`${msg}. ${failedCount} failed.`, 'warning', 5000);
                } else {
                    this.toast.show(msg, 'success', 3000);
                }
                this.sent.emit(response);
            },
            error: (err) => {
                this.isSending.set(false);
                this.toast.show(err?.error?.message ?? 'Email send failed.', 'danger', 4000);
            },
        });
    }
}
