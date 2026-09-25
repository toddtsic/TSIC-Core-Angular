import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EmailBodyEditorComponent } from '@shared-ui/components/email-body-editor/email-body-editor.component';
import { TestSendButtonComponent, type TestSendOptions } from '@shared-ui/components/test-send-button/test-send-button.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ChecklistBackLinkComponent } from '../shared/components/checklist-back-link/checklist-back-link.component';
import { RegistrationSearchService } from '@views/search/registrations/services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import { environment } from '@environments/environment';
import { ScheduleReviewEmailService } from './schedule-review-email.service';
import type { EmailBatchJobStatus, ScheduleReviewRecipientDto } from '@core/api';

/**
 * "Send Club Coaches Preview Email" — the Scheduling Checklist's letter to the club reps
 * whose teams are in the schedule, asking them to review it.
 *
 * WHY THIS IS NOT AN INVITE. The reps forward this mail to their own coaches, who review and
 * report back through the rep. The schedule-preview invite cannot serve that: its token is
 * bound to the recipient's login AND re-checked for the ClubRep role and an active
 * registration, so a forwarded copy is dead in a coach's hands. This letter therefore carries
 * an ORDINARY schedule link (!SCHEDULELINK) that survives forwarding — which works because
 * the director has released the schedule publicly by the time they send it.
 *
 * The audience is FIXED and server-resolved (active club reps holding at least one team that
 * appears in the built schedule) rather than assembled by hand in Search Registrations. It is
 * listed in full rather than counted, because a letter that gets forwarded deserves a visible
 * recipient list.
 */
@Component({
    selector: 'app-schedule-review-email',
    standalone: true,
    imports: [
        FormsModule,
        EmailBodyEditorComponent,
        TestSendButtonComponent,
        ConfirmDialogComponent,
        ChecklistBackLinkComponent
    ],
    templateUrl: './schedule-review-email.component.html',
    styleUrl: './schedule-review-email.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScheduleReviewEmailComponent implements OnInit, OnDestroy {
    private readonly recipientsSvc = inject(ScheduleReviewEmailService);
    private readonly searchService = inject(RegistrationSearchService);
    private readonly jobService = inject(JobService);
    private readonly toast = inject(ToastService);

    /** Test-send is a non-production affordance, same rule as every other compose surface. */
    protected readonly isTestSendVisible = !environment.production;

    protected readonly isLoading = signal(true);
    protected readonly loadFailed = signal(false);
    protected readonly recipients = signal<ScheduleReviewRecipientDto[]>([]);

    protected readonly subject = signal('');
    protected readonly body = signal('');

    protected readonly isSending = signal(false);
    protected readonly isSendingTest = signal(false);
    protected readonly showConfirm = signal(false);
    protected readonly status = signal<EmailBatchJobStatus | null>(null);
    protected readonly sendResult = signal<EmailBatchJobStatus | null>(null);

    private batchJobId: string | null = null;
    private pollTimer: ReturnType<typeof setTimeout> | null = null;
    private pollErrors = 0;

    protected readonly jobName = computed(() => this.jobService.currentJob()?.jobName ?? 'this event');

    /** Everyone shown, including opt-outs — the engine drops those at send time. */
    protected readonly recipientIds = computed(() => this.recipients().map(r => r.registrationId));

    /** What the send will actually deliver, so the button's count matches the outcome. */
    protected readonly deliverableCount = computed(() => this.recipients().filter(r => !r.emailOptOut).length);
    protected readonly optedOutCount = computed(() => this.recipients().filter(r => r.emailOptOut).length);

    /** A rep with no address on file cannot be reached; worth surfacing before the send, not after. */
    protected readonly noEmailCount = computed(() => this.recipients().filter(r => !r.email?.trim()).length);

    protected readonly canSend = computed(() =>
        !this.isSending()
        && this.deliverableCount() > 0
        && this.subject().trim().length > 0
        && this.body().trim().length > 0);

    ngOnInit(): void {
        this.seedLetter();
        this.load();
    }

    ngOnDestroy(): void {
        if (this.pollTimer) clearTimeout(this.pollTimer);
        // Orphans any in-flight poll: the batch keeps running server-side, we just stop watching.
        this.batchJobId = null;
    }

    protected load(): void {
        this.isLoading.set(true);
        this.loadFailed.set(false);
        this.recipientsSvc.getRecipients().subscribe({
            next: list => { this.recipients.set(list); this.isLoading.set(false); },
            error: () => { this.loadFailed.set(true); this.isLoading.set(false); }
        });
    }

    /** `processed` is optional on the status DTO and absent on the first, primed snapshot. */
    protected percentProcessed(s: EmailBatchJobStatus): number {
        if (!s.totalRecipients) return 0;
        return ((s.processed ?? 0) / s.totalRecipients) * 100;
    }

    protected displayName(r: ScheduleReviewRecipientDto): string {
        const name = `${r.firstName ?? ''} ${r.lastName ?? ''}`.trim();
        return name.length > 0 ? name : '(no name on file)';
    }

    /**
     * The seed letter. !SCHEDULELINK is a plain anchor to the public schedule — it holds no
     * per-recipient token, so it keeps working when a rep forwards this to their coaches,
     * which is the entire point of this tool. !PERSON and !JOBNAME resolve per recipient.
     */
    private seedLetter(): void {
        this.subject.set('Please review the schedule for !JOBNAME');
        this.body.set(
            '<p>Hi !PERSON,</p>' +
            '<p>The schedule for <strong>!JOBNAME</strong> is posted. Please review your teams and ' +
            'let us know right away if anything looks wrong.</p>' +
            '<p>!SCHEDULELINK</p>' +
            '<p><strong>Please forward this to your coaches</strong> so they can check their own ' +
            'games, and send any corrections back through us.</p>' +
            '<p>Thank you.</p>'
        );
    }

    protected sendTest(options: TestSendOptions): void {
        if (!this.subject().trim() || !this.body().trim()) {
            this.toast.show('Subject and body are required', 'danger', 4000);
            return;
        }
        if (this.recipientIds().length === 0) {
            this.toast.show('No recipients to render the test against', 'danger', 4000);
            return;
        }

        this.isSendingTest.set(true);
        this.searchService.sendTestEmail({
            registrationIds: this.recipientIds(),
            subject: this.subject(),
            bodyTemplate: this.body(),
            testRecipient: options.recipient
        }).subscribe({
            next: result => {
                this.isSendingTest.set(false);
                if (result.sent) {
                    this.toast.show(`Test email (rendered for ${result.renderedFor}) sent to ${result.recipient}`, 'success', 6000);
                } else {
                    this.toast.show(result.message || 'Test send failed', 'danger', 5000);
                }
            },
            error: err => {
                this.isSendingTest.set(false);
                this.toast.show(`Test send failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
            }
        });
    }

    protected askToSend(): void {
        if (!this.canSend()) return;
        this.showConfirm.set(true);
    }

    protected confirmSend(): void {
        this.showConfirm.set(false);
        this.startBatch();
    }

    /**
     * Explicit recipient ids, never criteria: this audience ("holds a scheduled team") is not
     * expressible as a registration search, and the director has just read the list.
     */
    private startBatch(): void {
        this.sendResult.set(null);
        this.status.set(null);
        this.pollErrors = 0;
        this.isSending.set(true);

        this.searchService.sendBatchEmail({
            registrationIds: this.recipientIds(),
            subject: this.subject(),
            bodyTemplate: this.body()
        }).subscribe({
            next: handle => {
                this.batchJobId = handle.jobId;
                this.status.set({
                    jobId: handle.jobId, totalRecipients: handle.totalRecipients,
                    sent: 0, failed: 0, optedOut: 0, done: false, failedAddresses: [], processed: 0
                });
                this.pollStatus(handle.jobId);
            },
            error: err => {
                this.isSending.set(false);
                this.toast.show(`Email send failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
            }
        });
    }

    private pollStatus(jobId: string): void {
        this.searchService.getBatchEmailStatus(jobId).subscribe({
            next: s => {
                this.pollErrors = 0;
                if (this.batchJobId !== jobId) return; // superseded, or the page went away
                this.status.set(s);
                if (s.done) {
                    this.isSending.set(false);
                    this.batchJobId = null;
                    this.sendResult.set(s);
                    const optedOutNote = s.optedOut > 0 ? `, ${s.optedOut} opted out` : '';
                    const msg = `Emails sent: ${s.sent} of ${s.totalRecipients}${optedOutNote}`;
                    if (s.failedAddresses.length > 0) this.toast.show(`${msg}. ${s.failedAddresses.length} failed.`, 'warning', 5000);
                    else this.toast.show(msg, 'success', 3000);
                } else {
                    this.pollTimer = setTimeout(() => this.pollStatus(jobId), 1000);
                }
            },
            error: () => {
                if (this.batchJobId !== jobId) return;
                // Transient blips tolerated; a recycled server loses the ephemeral job for good.
                if (++this.pollErrors >= 5) {
                    this.isSending.set(false);
                    this.batchJobId = null;
                    this.toast.show('Lost track of the batch (server may have restarted). The send may still be running.', 'warning', 6000);
                    return;
                }
                this.pollTimer = setTimeout(() => this.pollStatus(jobId), 2000);
            }
        });
    }
}
