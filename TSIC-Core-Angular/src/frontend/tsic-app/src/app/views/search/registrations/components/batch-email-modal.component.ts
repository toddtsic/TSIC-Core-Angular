import { Component, ChangeDetectionStrategy, signal, computed, input, output, inject, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { EmailBatchJobStatus, JobOptionDto, RegistrationSearchRequest } from '@core/api';
import { environment } from '@environments/environment';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { EMAIL_TEMPLATE_CATEGORIES, isTemplateAvailable, type EmailTemplate, type JobFlagsForTemplates } from '../email-templates';

const BASE_TOKENS = [
  { token: '!PERSON', description: 'Contact person name' },
  { token: '!EMAIL', description: 'Contact email address' },
  { token: '!FAMILYUSERNAME', description: 'Recipient\'s login username' },
  { token: '!JOBNAME', description: 'League/Organization name' },
  { token: '!JOBLINK', description: 'Job name as a clickable link (e.g., "visit !JOBLINK")' },
  { token: '!AMTFEES', description: 'Total fees amount' },
  { token: '!AMTPAID', description: 'Amount paid' },
  { token: '!AMTOWED', description: 'Amount owed' },
  { token: '!SEASON', description: 'Season name' },
  { token: '!SPORT', description: 'Sport name' },
  { token: '!CUSTOMERNAME', description: 'Customer name' }
];

const USLAX_VALID_THROUGH_TOKEN = { token: '!USLAXVALIDTHROUGHDATE', description: 'USA Lacrosse membership must be valid through this date' };

// Invite tokens are NEVER hand-picked from the palette — they are SEEDED by the "Invite" action,
// which knows the role and pre-places the correct personalized link + expiry text. Offering them in
// the palette let an admin drop the wrong invitation (or an invite into a plain email) by hand.
export type InviteMode = 'player' | 'clubrep';

// Selectable lifetimes (hours) for the signed invite token. Short by design — magic-link style.
const INVITE_EXPIRY_OPTIONS = [6, 12, 24, 48, 72] as const;
const DEFAULT_INVITE_EXPIRY_HOURS = 24;

// The name of the target event the invite is FOR. Unlike the per-recipient tokens, this value is
// batch-constant and already known on the client (it IS the dropdown selection), so it's filled
// client-side at send — the backend never sees it. Using it (rather than !JOBNAME, which resolves
// to the admin's CURRENT job) keeps the copy honest about which event the recipient is invited to.
const EVENT_INVITED_TO_TOKEN = '!EVENT_INVITEDTO';

/** Seed content for an invite send. The link (!INVITE_LINK / !CLUBREP_INVITE_LINK) and the expiry
 *  (!INVITE_EXPIRES) are resolved per recipient server-side; !EVENT_INVITEDTO is filled client-side
 *  from the target-event dropdown. The admin can edit the surrounding copy but must keep the link
 *  token (a send-time guard enforces this). */
const INVITE_TEMPLATES: Record<InviteMode, { subject: string; body: string }> = {
  player: {
    subject: 'You\'re invited to register for !EVENT_INVITEDTO',
    body:
      'Hi !PERSON,\n\n' +
      'You\'ve been invited to register for !EVENT_INVITEDTO. Use your personalized link below:\n\n' +
      '!INVITE_LINK\n\n' +
      'This invitation is unique to you and expires on !INVITE_EXPIRES. Please complete your registration before then.',
  },
  clubrep: {
    subject: 'You\'re invited to register your team for !EVENT_INVITEDTO',
    body:
      'Hi !PERSON,\n\n' +
      'You\'ve been invited to register your club/team for !EVENT_INVITEDTO. Use your personalized link below:\n\n' +
      '!CLUBREP_INVITE_LINK\n\n' +
      'This invitation is unique to you and expires on !INVITE_EXPIRES. Please complete your registration before then.',
  },
};

@Component({
  selector: 'app-batch-email-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './batch-email-modal.component.html',
  styleUrl: './batch-email-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatchEmailModalComponent implements OnInit, OnDestroy {
  private searchService = inject(RegistrationSearchService);
  private toast = inject(ToastService);

  /** Dev-only "TEST BATCH PROCESSING" button gate — never rendered in production builds. */
  readonly isDev = !environment.production;

  /** Staging-only gate for the invite "To (test inbox)" field. The Staging build sets
   *  production:true (so isDev is false there), so key on the explicit envName instead. The
   *  backend re-checks IsSandbox() before honoring the test inbox — this is purely UI exposure. */
  readonly isStaging = environment.envName === 'staging';

  registrationIds = input<string[]>([]);
  recipientCount = input<number>(0);
  recipients = input<{ name: string; email: string }[]>([]);
  isOpen = input<boolean>(false);

  // Full search context — drives template availability (VI filters, etc.)
  searchRequest = input<RegistrationSearchRequest | null>(null);
  jobFlags = input<JobFlagsForTemplates | null>(null);

  /** Transient UI mode: true when the grid is showing Authorize.net
   *  card-expiring-this-month lookup results. Gates the CC-Expiring template. */
  isCardExpiringMode = input<boolean>(false);

  /** When set, the modal auto-applies the template with this label on open.
   *  Useful for tool-launched flows (e.g. USLax reconcile page) that know
   *  exactly which template the user wants before the modal appears. */
  initialTemplateLabel = input<string | null>(null);

  closed = output<void>();
  sent = output<EmailBatchJobStatus>();

  subject = signal<string>('');
  bodyTemplate = signal<string>('');
  isSending = signal<boolean>(false);
  sendResult = signal<EmailBatchJobStatus | null>(null);
  showConfirm = signal<boolean>(false);

  /** Background-job tracking: handle id + latest polled progress snapshot. */
  private batchJobId = signal<string | null>(null);
  status = signal<EmailBatchJobStatus | null>(null);
  isEmailingSummary = signal<boolean>(false);
  summaryEmailed = signal<boolean>(false);
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollErrors = 0;

  /** 0–100 progress for the bar, derived from processed/total. Floors to 1% once any
   *  record is processed so a huge batch (where the true % is sub-1) still shows movement. */
  readonly progressPct = computed(() => {
    const s = this.status();
    if (!s || s.totalRecipients === 0) return 0;
    const processed = s.processed ?? (s.sent + s.failed);
    if (processed === 0) return 0;
    if (processed >= s.totalRecipients) return 100;
    return Math.max(1, Math.round((processed / s.totalRecipients) * 100));
  });

  /** Snapshot of subject/body set by the most recent template apply. When the
   *  live values still match, the draft is "clean" and swapping templates
   *  doesn't need a confirm prompt. */
  private lastAppliedTemplate = signal<{ subject: string; body: string } | null>(null);

  // AI compose
  aiPrompt = signal<string>('');
  isDrafting = signal<boolean>(false);

  // Invite mode — set by the parent's "Invite" action. When non-null, the modal opens seeded with the
  // role's invite template and shows the target-event + expiry pickers.
  inviteMode = input<InviteMode | null>(null);
  // Eligible target events for the active invite role (from the job-scoped init load, passed by parent).
  inviteTargetJobs = input<JobOptionDto[]>([]);
  selectedInviteTargetJobId = signal<string | null>(null);
  readonly inviteExpiryOptions = INVITE_EXPIRY_OPTIONS;
  selectedInviteExpiryHours = signal<number>(DEFAULT_INVITE_EXPIRY_HOURS);

  // Staging-only test inbox. Every invite email in the batch is delivered to this one address
  // server-side so the token link can be received and clicked. Editable; defaults below.
  readonly defaultSandboxTestRecipient = 'anntsic@gmail.com';
  sandboxTestRecipient = signal<string>('anntsic@gmail.com');

  readonly availableTokens = computed(() => {
    // Invite links are intentionally NOT offered here — they are seeded by the Invite action.
    const tokens = [...BASE_TOKENS];
    if (this.jobFlags()?.usLaxMembershipValidated) tokens.push(USLAX_VALID_THROUGH_TOKEN);
    return tokens;
  });

  readonly requiresInviteLink = computed(() => {
    const body = this.bodyTemplate();
    return body.includes('!INVITE_LINK') || body.includes('!CLUBREP_INVITE_LINK');
  });

  /** The link token the active invite mode uses — surfaced in the guidance panel so the admin
   *  keeps the right one in the body. Club reps register teams; players register themselves. */
  readonly inviteLinkToken = computed(() =>
    this.inviteMode() === 'clubrep' ? '!CLUBREP_INVITE_LINK' : '!INVITE_LINK'
  );

  /** Display name of the selected target event — the value !EVENT_INVITEDTO is filled with. */
  private selectedInviteTargetJobName(): string {
    const id = this.selectedInviteTargetJobId();
    return this.inviteTargetJobs().find(j => j.jobId === id)?.jobName ?? '';
  }

  readonly canSend = computed(() =>
    (!this.inviteMode() && !this.requiresInviteLink()) || this.selectedInviteTargetJobId() !== null
  );

  /** Single source of truth for both Send and the dev TEST button — they enable/disable together. */
  readonly canSubmit = computed(() => !this.isSending() && this.canSend());

  /** Categories with at least one template whose availability rule passes. */
  readonly availableTemplateCategories = computed(() => {
    const req = this.searchRequest();
    const flags = this.jobFlags();
    const modes = { cardExpiring: this.isCardExpiringMode() };
    if (!req) return [];
    return EMAIL_TEMPLATE_CATEGORIES
      .map(cat => ({
        category: cat.category,
        templates: cat.templates.filter(t => isTemplateAvailable(t, req, flags, modes))
      }))
      .filter(cat => cat.templates.length > 0);
  });

  readonly hasAvailableTemplates = computed(() => this.availableTemplateCategories().length > 0);

  ngOnInit(): void {
    // Invite send: seed the role's template and default the pickers. The eligible target list is
    // passed in from the job-scoped init load (no per-open fetch). Auto-select when there's exactly one.
    const mode = this.inviteMode();
    if (mode) {
      const seed = INVITE_TEMPLATES[mode];
      this.subject.set(seed.subject);
      this.bodyTemplate.set(seed.body);
      this.lastAppliedTemplate.set({ subject: seed.subject, body: seed.body });
      this.selectedInviteExpiryHours.set(DEFAULT_INVITE_EXPIRY_HOURS);
      const targets = this.inviteTargetJobs();
      if (targets.length === 1) this.selectedInviteTargetJobId.set(targets[0].jobId);
      return;
    }

    const initialLabel = this.initialTemplateLabel();
    if (initialLabel) {
      for (const cat of EMAIL_TEMPLATE_CATEGORIES) {
        const tmpl = cat.templates.find(t => t.label === initialLabel);
        if (tmpl) { this.applyTemplate(tmpl); break; }
      }
    }
  }

  /** Eligible target events for the active invite role — supplied by the parent from the
   *  job-scoped init load (RegistrationFilterOptionsDto.eligible{Player,ClubRep}InviteTargetJobs). */
  readonly targetJobOptions = computed(() => this.inviteTargetJobs());

  applyTemplate(template: EmailTemplate): void {
    this.subject.set(template.subject);
    this.bodyTemplate.set(template.body);
    this.lastAppliedTemplate.set({ subject: template.subject, body: template.body });
  }

  /** True when the user has edited the subject or body since the last template apply. */
  private hasUserEdits(): boolean {
    const applied = this.lastAppliedTemplate();
    const subj = this.subject();
    const body = this.bodyTemplate();
    if (!applied) return subj.trim().length > 0 || body.trim().length > 0;
    return subj !== applied.subject || body !== applied.body;
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

    if (this.hasUserEdits() && !confirm('Replace current subject and body with this template?')) return;
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
    if ((this.inviteMode() || this.requiresInviteLink()) && !this.selectedInviteTargetJobId()) {
      this.toast.show('Select a target registration event for the invitation', 'danger', 4000);
      return;
    }
    this.showConfirm.set(true);
  }

  confirmSend(): void {
    this.showConfirm.set(false);
    this.startBatch(undefined);
  }

  /** Dev-only: runs the real engine end-to-end with a simulated per-unit delay (no real SES transmit). */
  testBatchProcessing(): void {
    if (!this.isDev) return;
    if (!this.subject().trim() || !this.bodyTemplate().trim()) { this.toast.show('Subject and body are required', 'danger', 4000); return; }
    if (this.registrationIds().length === 0) { this.toast.show('No registrations selected', 'danger', 4000); return; }
    // 0 = no artificial per-send delay; the render stage paces the run. (On Windows Task.Delay is
    // quantized to ~15ms, so any 1–14ms value would behave like 15ms — 0 is the only real speedup.)
    this.startBatch(0);
  }

  /** Kicks off a background batch (real or simulated), then polls the status endpoint for progress. */
  private startBatch(simulatedPerUnitDelayMs: number | undefined): void {
    this.sendResult.set(null);
    this.status.set(null);
    this.summaryEmailed.set(false);
    this.pollErrors = 0;
    this.isSending.set(true);

    // !EVENT_INVITEDTO is filled here, client-side, from the target-event dropdown — it's the same
    // for the whole batch, so there's no reason to make the server resolve it per recipient. The
    // backend only ever receives the literal event name.
    const eventName = this.selectedInviteTargetJobName();
    const subject = this.subject().replaceAll(EVENT_INVITED_TO_TOKEN, eventName);
    const bodyTemplate = this.bodyTemplate().replaceAll(EVENT_INVITED_TO_TOKEN, eventName);

    this.searchService.sendBatchEmail({
      registrationIds: this.registrationIds(),
      subject,
      bodyTemplate,
      inviteLinkTargetJobId: this.selectedInviteTargetJobId() ?? undefined,
      // Only meaningful when an invite link is present; harmless otherwise. Stamps the token's
      // lifetime AND the !INVITE_EXPIRES copy from the same server instant (consistent by construction).
      inviteExpiryHours: this.requiresInviteLink() ? this.selectedInviteExpiryHours() : undefined,
      simulatedPerUnitDelayMs: simulatedPerUnitDelayMs ?? undefined,
      // Staging-only: deliver every send to the tester's inbox so the token link is receivable.
      // Never sent from any other build; the backend also re-gates on IsSandbox().
      sandboxTestRecipient: this.isStaging ? (this.sandboxTestRecipient().trim() || undefined) : undefined
    }).subscribe({
      next: (handle) => {
        this.batchJobId.set(handle.jobId);
        // Prime the bar with totals before the first poll lands.
        this.status.set({ jobId: handle.jobId, totalRecipients: handle.totalRecipients, sent: 0, failed: 0, optedOut: 0, done: false, failedAddresses: [], processed: 0 });
        this.pollStatus(handle.jobId);
      },
      error: (err) => { this.isSending.set(false); this.toast.show(`Email send failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000); }
    });
  }

  private pollStatus(jobId: string): void {
    this.searchService.getBatchEmailStatus(jobId).subscribe({
      next: (s) => {
        this.pollErrors = 0;
        if (this.batchJobId() !== jobId) return; // superseded (modal reset/closed)
        this.status.set(s);
        if (s.done) { this.onBatchComplete(s); }
        else { this.pollTimer = setTimeout(() => this.pollStatus(jobId), 1000); }
      },
      error: () => {
        if (this.batchJobId() !== jobId) return;
        // Transient blips tolerated; a recycled server loses the ephemeral job permanently.
        if (++this.pollErrors >= 5) {
          this.isSending.set(false);
          this.batchJobId.set(null);
          this.toast.show('Lost track of the batch (server may have restarted). The send may still be running.', 'warning', 6000);
          return;
        }
        this.pollTimer = setTimeout(() => this.pollStatus(jobId), 2000);
      }
    });
  }

  private onBatchComplete(s: EmailBatchJobStatus): void {
    this.isSending.set(false);
    this.batchJobId.set(null);
    this.sendResult.set(s);
    const optedOutNote = s.optedOut > 0 ? `, ${s.optedOut} opted out` : '';
    const msg = `Emails sent: ${s.sent} of ${s.totalRecipients}${optedOutNote}`;
    if (s.failedAddresses.length > 0) { this.toast.show(`${msg}. ${s.failedAddresses.length} failed.`, 'warning', 5000); }
    else { this.toast.show(msg, 'success', 3000); }
    this.sent.emit(s);
  }

  /** Opt-in: ask the server to email the completion summary to the current admin. */
  emailSummary(): void {
    const result = this.sendResult();
    if (!result) return;
    // The summary is sourced from the ephemeral registry, keyed by the just-finished job id.
    const jobId = this.status()?.jobId;
    if (!jobId) return;
    this.isEmailingSummary.set(true);
    this.searchService.emailBatchSummary(jobId).subscribe({
      next: () => { this.isEmailingSummary.set(false); this.summaryEmailed.set(true); this.toast.show('Summary emailed to you', 'success', 3000); },
      error: (err) => { this.isEmailingSummary.set(false); this.toast.show(`Could not email summary: ${err.error?.message || 'Unknown error'}`, 'danger', 4000); }
    });
  }

  dismissConfirm(): void { this.showConfirm.set(false); }

  private stopPolling(): void {
    if (this.pollTimer) { clearTimeout(this.pollTimer); this.pollTimer = null; }
    this.batchJobId.set(null);
  }

  ngOnDestroy(): void { this.stopPolling(); }

  private resetForm(): void {
    this.stopPolling();
    this.subject.set('');
    this.bodyTemplate.set('');
    this.lastAppliedTemplate.set(null);
    this.isSending.set(false);
    this.sendResult.set(null);
    this.status.set(null);
    this.isEmailingSummary.set(false);
    this.summaryEmailed.set(false);
    this.showConfirm.set(false);
    this.selectedInviteTargetJobId.set(null);
    this.selectedInviteExpiryHours.set(DEFAULT_INVITE_EXPIRY_HOURS);
    this.sandboxTestRecipient.set(this.defaultSandboxTestRecipient);
    this.aiPrompt.set('');
    this.isDrafting.set(false);
  }
}
