import { Component, ChangeDetectionStrategy, signal, computed, input, output, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { BatchEmailResponse, JobOptionDto, FilterOption, RegistrationSearchRequest } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { EMAIL_TEMPLATE_CATEGORIES, isTemplateAvailable, type EmailTemplate, type JobFlagsForTemplates } from '../email-templates';

const BASE_TOKENS = [
  { token: '!PERSON', description: 'Contact person name' },
  { token: '!EMAIL', description: 'Contact email address' },
  { token: '!FAMILYUSERNAME', description: 'Recipient\'s login username' },
  { token: '!JOBNAME', description: 'League/Organization name' },
  { token: '!JOBLINK', description: 'Link to the job page (as a clickable phrase)' },
  { token: '!AMTFEES', description: 'Total fees amount' },
  { token: '!AMTPAID', description: 'Amount paid' },
  { token: '!AMTOWED', description: 'Amount owed' },
  { token: '!SEASON', description: 'Season name' },
  { token: '!SPORT', description: 'Sport name' },
  { token: '!CUSTOMERNAME', description: 'Customer name' },
  { token: '!INVITE_LINK', description: 'Personalized registration invitation link (requires target event selection)' }
];

const CLUBREP_INVITE_TOKEN = { token: '!CLUBREP_INVITE_LINK', description: 'Club rep team registration invitation link (requires target event selection)' };
const USLAX_VALID_THROUGH_TOKEN = { token: '!USLAXVALIDTHROUGHDATE', description: 'USA Lacrosse membership must be valid through this date' };

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

  // Full search context — drives template availability (VI filters, etc.)
  searchRequest = input<RegistrationSearchRequest | null>(null);
  jobFlags = input<JobFlagsForTemplates | null>(null);

  closed = output<void>();
  sent = output<BatchEmailResponse>();

  subject = signal<string>('');
  bodyTemplate = signal<string>('');
  isSending = signal<boolean>(false);
  sendResult = signal<BatchEmailResponse | null>(null);
  showConfirm = signal<boolean>(false);

  // AI compose
  aiPrompt = signal<string>('');
  isDrafting = signal<boolean>(false);

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

  readonly availableTokens = computed(() => {
    const tokens = [...BASE_TOKENS];
    if (this.isClubRepOnly()) tokens.push(CLUBREP_INVITE_TOKEN);
    if (this.jobFlags()?.usLaxMembershipValidated) tokens.push(USLAX_VALID_THROUGH_TOKEN);
    return tokens;
  });

  readonly requiresInviteLink = computed(() => {
    const body = this.bodyTemplate();
    return body.includes('!INVITE_LINK') || body.includes('!CLUBREP_INVITE_LINK');
  });

  readonly canSend = computed(() =>
    !this.requiresInviteLink() || this.selectedInviteTargetJobId() !== null
  );

  /** Categories with at least one template whose availability rule passes. */
  readonly availableTemplateCategories = computed(() => {
    const req = this.searchRequest();
    const flags = this.jobFlags();
    if (!req) return [];
    return EMAIL_TEMPLATE_CATEGORIES
      .map(cat => ({
        category: cat.category,
        templates: cat.templates.filter(t => isTemplateAvailable(t, req, flags))
      }))
      .filter(cat => cat.templates.length > 0);
  });

  readonly hasAvailableTemplates = computed(() => this.availableTemplateCategories().length > 0);

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

  applyTemplate(template: EmailTemplate): void {
    this.subject.set(template.subject);
    this.bodyTemplate.set(template.body);
  }

  onTemplateSelected(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const value = select.value;
    if (!value) return;
    const [catName, label] = value.split('|');
    const cat = this.availableTemplateCategories().find(c => c.category === catName);
    const tmpl = cat?.templates.find(t => t.label === label);

    // Reset immediately so re-selecting the same template re-applies, regardless of confirm outcome.
    select.value = '';
    if (!tmpl) return;

    const hasDraft = this.subject().trim().length > 0 || this.bodyTemplate().trim().length > 0;
    if (hasDraft && !confirm('Replace current subject and body with this template?')) return;
    this.applyTemplate(tmpl);
  }

  draftWithAi(): void {
    const prompt = this.aiPrompt().trim();
    if (!prompt) { this.toast.show('Describe the email you want to send', 'danger', 4000); return; }

    this.isDrafting.set(true);
    this.searchService.aiComposeEmail(prompt).subscribe({
      next: (response) => {
        this.subject.set(response.subject);
        this.bodyTemplate.set(response.body);
        this.isDrafting.set(false);
      },
      error: (err) => {
        this.isDrafting.set(false);
        this.toast.show(`AI draft failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
      }
    });
  }

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
    this.aiPrompt.set('');
    this.isDrafting.set(false);
  }
}
