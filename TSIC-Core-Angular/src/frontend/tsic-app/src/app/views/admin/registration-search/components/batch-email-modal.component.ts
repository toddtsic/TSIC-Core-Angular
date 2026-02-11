import { Component, ChangeDetectionStrategy, signal, input, output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { BatchEmailResponse } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';

const AVAILABLE_TOKENS = [
  { token: '!PERSON', description: 'Contact person name' },
  { token: '!EMAIL', description: 'Contact email address' },
  { token: '!JOBNAME', description: 'League/Organization name' },
  { token: '!AMTFEES', description: 'Total fees amount' },
  { token: '!AMTPAID', description: 'Amount paid' },
  { token: '!AMTOWED', description: 'Amount owed' },
  { token: '!SEASON', description: 'Season name' },
  { token: '!SPORT', description: 'Sport name' },
  { token: '!CUSTOMERNAME', description: 'Customer name' },
  { token: '!F-ACCOUNTING', description: 'Formatted accounting table' },
  { token: '!F-PLAYERS', description: 'Formatted player roster table' },
  { token: '!J-CONTACTBLOCK', description: 'JSON contact information block' }
];

@Component({
  selector: 'app-batch-email-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './batch-email-modal.component.html',
  styleUrl: './batch-email-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatchEmailModalComponent {
  private searchService = inject(RegistrationSearchService);
  private toast = inject(ToastService);

  registrationIds = input<string[]>([]);
  recipientCount = input<number>(0);
  isOpen = input<boolean>(false);

  closed = output<void>();
  sent = output<BatchEmailResponse>();

  subject = signal<string>('');
  bodyTemplate = signal<string>('');
  isSending = signal<boolean>(false);
  sendResult = signal<BatchEmailResponse | null>(null);

  readonly availableTokens = AVAILABLE_TOKENS;

  close(): void { this.closed.emit(); this.resetForm(); }

  insertToken(token: string, textarea: HTMLTextAreaElement): void {
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const currentBody = this.bodyTemplate();
    this.bodyTemplate.set(currentBody.substring(0, start) + token + currentBody.substring(end));
    setTimeout(() => { textarea.focus(); const p = start + token.length; textarea.setSelectionRange(p, p); }, 0);
  }

  sendEmail(): void {
    if (!this.subject().trim() || !this.bodyTemplate().trim()) { this.toast.show('Subject and body are required', 'danger', 4000); return; }
    const ids = this.registrationIds();
    if (ids.length === 0) { this.toast.show('No registrations selected', 'danger', 4000); return; }

    this.isSending.set(true);
    this.searchService.sendBatchEmail({
      registrationIds: ids, subject: this.subject(), bodyTemplate: this.bodyTemplate()
    }).subscribe({
      next: (response) => {
        this.isSending.set(false);
        this.sendResult.set(response);
        const msg = `Emails sent: ${response.sent} of ${response.totalRecipients}`;
        if (response.failedAddresses.length > 0) { this.toast.show(`${msg}. ${response.failedAddresses.length} failed.`, 'warning', 5000); }
        else { this.toast.show(msg, 'success', 3000); }
        this.sent.emit(response);
      },
      error: (err) => { this.isSending.set(false); this.toast.show(`Email send failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000); }
    });
  }

  private resetForm(): void {
    this.subject.set(''); this.bodyTemplate.set(''); this.isSending.set(false); this.sendResult.set(null);
  }
}
