import { Component, ChangeDetectionStrategy, signal, computed, input, output, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { BatchEmailResponse, JobOptionDto, FilterOption } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';

const BASE_TOKENS = [
  { token: '!PERSON', description: 'Contact person name' },
  { token: '!EMAIL', description: 'Contact email address' },
  { token: '!JOBNAME', description: 'League/Organization name' },
  { token: '!AMTFEES', description: 'Total fees amount' },
  { token: '!AMTPAID', description: 'Amount paid' },
  { token: '!AMTOWED', description: 'Amount owed' },
  { token: '!SEASON', description: 'Season name' },
  { token: '!SPORT', description: 'Sport name' },
  { token: '!CUSTOMERNAME', description: 'Customer name' },
  { token: '!INVITE_LINK', description: 'Personalized registration invitation link (requires target event selection)' }
];

const CLUBREP_INVITE_TOKEN = { token: '!CLUBREP_INVITE_LINK', description: 'Club rep team registration invitation link (requires target event selection)' };

@Component({
  selector: 'app-batch-email-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './batch-email-modal.component.html',
  styleUrl: './batch-email-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatchEmailModalComponent implements OnInit {
  private searchService = inject(RegistrationSearchService);
  private toast = inject(ToastService);

  registrationIds = input<string[]>([]);
  recipientCount = input<number>(0);
  recipients = input<{ name: string; email: string }[]>([]);
  isOpen = input<boolean>(false);

  // Role context — used to conditionally show !CLUBREP_INVITE_LINK
  activeRoleIds = input<string[]>([]);
  roleOptions = input<FilterOption[] | null>(null);

  closed = output<void>();
  sent = output<BatchEmailResponse>();

  subject = signal<string>('');
  bodyTemplate = signal<string>('');
  isSending = signal<boolean>(false);
  sendResult = signal<BatchEmailResponse | null>(null);
  showConfirm = signal<boolean>(false);

  // Invite link support
  inviteTargetJobs = signal<JobOptionDto[]>([]);
  clubRepInviteTargetJobs = signal<JobOptionDto[]>([]);
  selectedInviteTargetJobId = signal<string | null>(null);

  /** True when exactly one role is selected and it's "Club Rep" */
  readonly isClubRepOnly = computed(() => {
    const ids = this.activeRoleIds();
    if (ids.length !== 1) return false;
    const opts = this.roleOptions();
    if (!opts) return false;
    const match = opts.find(o => o.value === ids[0]);
    return match?.text === 'Club Rep';
  });

  readonly availableTokens = computed(() =>
    this.isClubRepOnly() ? [...BASE_TOKENS, CLUBREP_INVITE_TOKEN] : BASE_TOKENS
  );

  readonly requiresInviteLink = computed(() => {
    const body = this.bodyTemplate();
    return body.includes('!INVITE_LINK') || body.includes('!CLUBREP_INVITE_LINK');
  });

  readonly canSend = computed(() =>
    !this.requiresInviteLink() || this.selectedInviteTargetJobId() !== null
  );

  ngOnInit(): void {
    this.searchService.getInviteTargetJobs().subscribe({
      next: (jobs) => this.inviteTargetJobs.set(jobs),
      error: () => { /* non-critical — just means dropdown stays empty */ }
    });
    this.searchService.getClubRepInviteTargetJobs().subscribe({
      next: (jobs) => this.clubRepInviteTargetJobs.set(jobs),
      error: () => { /* non-critical */ }
    });
  }

  /** Jobs shown in the target event dropdown — future-only for club rep invite, all for player invite */
  readonly targetJobOptions = computed(() => {
    const body = this.bodyTemplate();
    if (body.includes('!CLUBREP_INVITE_LINK')) return this.clubRepInviteTargetJobs();
    return this.inviteTargetJobs();
  });

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
    if (this.requiresInviteLink() && !this.selectedInviteTargetJobId()) {
      this.toast.show('Select a target registration event for the invite link', 'danger', 4000);
      return;
    }
    this.showConfirm.set(true);
  }

  confirmSend(): void {
    this.showConfirm.set(false);
    const ids = this.registrationIds();

    this.isSending.set(true);
    this.searchService.sendBatchEmail({
      registrationIds: ids,
      subject: this.subject(),
      bodyTemplate: this.bodyTemplate(),
      inviteLinkTargetJobId: this.selectedInviteTargetJobId() ?? undefined
    }).subscribe({
      next: (response) => {
        this.isSending.set(false);
        this.sendResult.set(response);
        const optedOutNote = response.optedOut > 0 ? `, ${response.optedOut} opted out` : '';
        const msg = `Emails sent: ${response.sent} of ${response.totalRecipients}${optedOutNote}`;
        if (response.failedAddresses.length > 0) { this.toast.show(`${msg}. ${response.failedAddresses.length} failed.`, 'warning', 5000); }
        else { this.toast.show(msg, 'success', 3000); }
        this.sent.emit(response);
      },
      error: (err) => { this.isSending.set(false); this.toast.show(`Email send failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000); }
    });
  }

  dismissConfirm(): void { this.showConfirm.set(false); }

  private resetForm(): void {
    this.subject.set('');
    this.bodyTemplate.set('');
    this.isSending.set(false);
    this.sendResult.set(null);
    this.showConfirm.set(false);
    this.selectedInviteTargetJobId.set(null);
  }
}
