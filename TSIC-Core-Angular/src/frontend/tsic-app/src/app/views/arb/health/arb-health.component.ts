import { Component, inject, signal, computed, ChangeDetectionStrategy, viewChild } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { EmailBodyEditorComponent } from '@shared-ui/components/email-body-editor/email-body-editor.component';
import { TestSendButtonComponent, type TestSendOptions } from '@shared-ui/components/test-send-button/test-send-button.component';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { environment } from '@environments/environment';
import { ArbDefensiveService } from './services/arb-defensive.service';
import type {
    ArbFlaggedRegistrantDto,
    ArbRefreshStatusesResultDto,
    ArbSubstitutionVariableDto,
    ArbSendEmailsRequest
} from '@core/api';

/** Matches the C# ArbFlagType enum values */
const FLAG_TYPE = { ExpiringCard: 0, BehindInPayment: 1 } as const;

/** Pre-built email templates ported from legacy system */
interface EmailTemplate {
    label: string;
    subject: string;
    body: string;
}

const TEMPLATES: Record<string, EmailTemplate[]> = {
    behindInPayment: [
        {
            label: 'Active/Suspended Subscriptions (Update CC Info)',
            subject: 'Action Required: Update Your Payment Information',
            body:
                '<p>One or more of your automatic payments for !JOBNAME for !PLAYER was declined.</p>' +
                '<p>You can contact your credit card issuer to determine the reason if you need to.</p>' +
                '<p>Then you can update your credit card information and process the current balance due (!OWEDNOW) all in one step.</p>' +
                '<p>To fix this, visit !JOBLINK, then:</p>' +
                '<ol>' +
                '<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>' +
                '<li>Select your Player\'s role</li>' +
                '<li>Under \'Player\' in the upper right, select <b>Update CC Info</b> — this also pays the auto-payment that failed</li>' +
                '<li>Your <b>Balance Due</b> is shown near the top of the page. Enter your credit card information below it.</li>' +
                '<li>Click <b>Update Card &amp; Pay Balance</b> to make the payment and reactivate your future automatic payments.</li>' +
                '</ol>'
        },
        {
            label: 'Expired/Terminated Subscriptions (Pay Balance Due)',
            subject: 'Action Required: Pay Balance Due',
            body:
                '<p>One or more of your automatic payments for !JOBNAME for !PLAYER was declined.</p>' +
                '<p>You can contact your credit card issuer to determine the reason if you need to.</p>' +
                '<p>Then you can update your credit card information and process the current balance due (!OWEDNOW) all in one step.</p>' +
                '<p>To fix this, visit !JOBLINK, then:</p>' +
                '<ol>' +
                '<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>' +
                '<li>Select your Player\'s role</li>' +
                '<li>Under \'Player\' in the upper right, select \'Pay Balance Due\'</li>' +
                '</ol>'
        }
    ],
    expiringCard: [
        {
            label: 'Credit Card Expiration Notice',
            subject: 'TeamSportsInfo.com Credit Card Expiring This Month',
            body:
                '<h2>Credit Card Expiration Notice</h2>' +
                '<p>The credit card on file for <strong>Automatic Recurrent Billing</strong> for !PLAYER is expiring this month.</p>' +
                '<p>Please visit !JOBLINK to update your credit card information TO PREVENT YOUR NEXT PAYMENT FROM FAILING.</p>' +
                '<ol>' +
                '<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>' +
                '<li>Select your Player\'s role</li>' +
                '<li>Under \'Player\' in the upper right, select <b>Update CC Info</b> — this also pays any auto-payment that has failed</li>' +
                '<li>Your <b>Balance Due</b> is shown near the top of the page. Enter your credit card information below it.</li>' +
                '<li>Click <b>Update Card &amp; Pay Balance</b> to save the new card and keep your automatic payments running.</li>' +
                '</ol>'
        }
    ]
};

@Component({
    selector: 'app-arb-health',
    standalone: true,
    imports: [DecimalPipe, DatePipe, FormsModule, TsicDialogComponent, EmailBodyEditorComponent, TestSendButtonComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './arb-health.component.html',
    styleUrl: './arb-health.component.scss'
})
export class ArbHealthComponent {
    private readonly arbService = inject(ArbDefensiveService);
    private readonly toast = inject(ToastService);
    private readonly auth = inject(AuthService);

    readonly isNonProd = environment.envName !== 'production';
    /** Test popovers are SUPERUSER-only on shared environments (AM-060 rule). */
    readonly isSuperuser = this.auth.isSuperuser;

    // Lookup state. activeTab is the flag type the UI is oriented to (drives templates,
    // action bar, table shape); loadedTab is which lookup has actually RUN — null until
    // the director clicks one. Nothing loads on init: Expiring Cards queries live
    // production Authorize.Net, so it must only run on a deliberate click (PL-055).
    readonly activeTab = signal<number>(FLAG_TYPE.BehindInPayment);
    readonly loadedTab = signal<number | null>(null);
    readonly FLAG_TYPE = FLAG_TYPE;

    // Data
    readonly registrants = signal<ArbFlaggedRegistrantDto[]>([]);
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);

    // Job-wide ARB status refresh (the single status-sync chokepoint)
    readonly isRefreshing = signal(false);
    readonly refreshResult = signal<ArbRefreshStatusesResultDto | null>(null);

    // Selection
    readonly selectedIds = signal<Set<string>>(new Set());
    readonly allSelected = computed(() => {
        const list = this.registrants();
        const sel = this.selectedIds();
        return list.length > 0 && list.every(r => sel.has(r.registrationId));
    });

    // Email dialog
    readonly showEmailDialog = signal(false);
    readonly emailSubject = signal('');
    readonly emailBody = signal('');
    readonly notifyDirectors = signal(true);
    readonly substitutionVars = signal<ArbSubstitutionVariableDto[]>([]);
    readonly isSending = signal(false);
    readonly sendResult = signal<{ sent: number; failed: number; failedAddresses: string[] } | null>(null);

    readonly selectedCount = computed(() => this.selectedIds().size);

    private readonly bodyEditor = viewChild.required(EmailBodyEditorComponent);

    /** Templates available for the active tab */
    readonly availableTemplates = computed<EmailTemplate[]>(() => {
        return this.activeTab() === FLAG_TYPE.ExpiringCard
            ? TEMPLATES['expiringCard']
            : TEMPLATES['behindInPayment'];
    });

    constructor() {
        // Deliberately NO lookup here — the page opens neutral; see runLookup.
        this.loadSubstitutionVars();
    }

    /**
     * One click, whole job: syncs stored ARB status from Authorize.Net for every
     * registration in the job with a subscription ID, then reloads the active tab.
     */
    refreshStatuses(): void {
        if (this.isRefreshing()) return;

        this.isRefreshing.set(true);
        this.refreshResult.set(null);
        this.errorMessage.set(null);

        this.arbService.refreshStatuses().subscribe({
            next: result => {
                this.refreshResult.set(result);
                this.isRefreshing.set(false);
                // Statuses may have changed which registrants are flagged — reload,
                // but ONLY a lookup the director already ran. Refresh alone must not
                // trigger the first lookup.
                if (this.loadedTab() !== null) {
                    this.selectedIds.set(new Set());
                    this.loadTab(this.activeTab());
                }
            },
            error: err => {
                this.errorMessage.set(err?.error?.message || 'Failed to refresh ARB statuses.');
                this.isRefreshing.set(false);
            }
        });
    }

    /** Explicit lookup button. Unlike the old tab switch, re-clicking the active
     *  lookup re-runs it (fresh query). */
    runLookup(type: number): void {
        this.activeTab.set(type);
        this.selectedIds.set(new Set());
        this.showEmailDialog.set(false);
        this.sendResult.set(null);
        this.loadTab(type);
    }

    private loadTab(type: number): void {
        this.loadedTab.set(type);
        this.isLoading.set(true);
        this.errorMessage.set(null);

        this.arbService.getFlagged(type).subscribe({
            next: data => {
                this.registrants.set(data);
                this.isLoading.set(false);
            },
            error: err => {
                this.errorMessage.set(err?.error?.message || 'Failed to load flagged registrants.');
                this.isLoading.set(false);
            }
        });
    }

    private loadSubstitutionVars(): void {
        this.arbService.getSubstitutionVariables().subscribe({
            next: vars => this.substitutionVars.set(vars)
        });
    }

    toggleSelect(registrationId: string): void {
        const set = new Set(this.selectedIds());
        if (set.has(registrationId)) {
            set.delete(registrationId);
        } else {
            set.add(registrationId);
        }
        this.selectedIds.set(set);
    }

    toggleSelectAll(): void {
        if (this.allSelected()) {
            this.selectedIds.set(new Set());
        } else {
            const set = new Set(this.registrants().map(r => r.registrationId));
            this.selectedIds.set(set);
        }
    }

    isSelected(registrationId: string): boolean {
        return this.selectedIds().has(registrationId);
    }

    openEmailDialog(): void {
        this.emailSubject.set('');
        this.emailBody.set('');
        this.notifyDirectors.set(true);
        this.sendResult.set(null);
        this.showEmailDialog.set(true);
    }

    closeEmailDialog(): void {
        this.showEmailDialog.set(false);
    }

    applyTemplate(template: EmailTemplate): void {
        this.emailSubject.set(template.subject);
        this.emailBody.set(template.body);
    }

    insertToken(token: string): void {
        this.bodyEditor().insertToken(token);
    }

    readonly isSendingTest = signal(false);

    /** Non-prod: renders ARB tokens against the first selected flagged registrant and delivers
     *  the real email to a single test inbox. */
    sendTestEmail(options: TestSendOptions): void {
        const firstSelected = this.registrants().find(r => this.selectedIds().has(r.registrationId))
            ?? this.registrants()[0];
        if (!firstSelected) return;

        this.isSendingTest.set(true);
        this.arbService.sendTestEmail({
            jobId: '00000000-0000-0000-0000-000000000000', // derived server-side from JWT
            flagType: this.activeTab(),
            registrationId: firstSelected.registrationId,
            emailSubject: this.emailSubject(),
            emailBody: this.emailBody(),
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
                this.toast.show(err?.error?.message || 'Test send failed', 'danger', 5000);
            }
        });
    }

    onSubjectChange(value: string): void {
        this.emailSubject.set(value);
    }

    onNotifyDirectorsChange(value: boolean): void {
        this.notifyDirectors.set(value);
    }

    sendEmails(): void {
        const ids = Array.from(this.selectedIds());
        if (ids.length === 0) return;

        this.isSending.set(true);
        this.sendResult.set(null);

        // jobId + senderUserId are derived server-side from JWT claims
        const request: ArbSendEmailsRequest = {
            jobId: '00000000-0000-0000-0000-000000000000',
            senderUserId: '',
            flagType: this.activeTab(),
            emailSubject: this.emailSubject(),
            emailBody: this.emailBody(),
            registrationIds: ids,
            notifyDirectors: this.notifyDirectors()
        };

        this.arbService.sendEmailsAndAwait(request).subscribe({
            next: status => {
                this.sendResult.set({
                    sent: status.sent ?? 0,
                    failed: status.failed ?? 0,
                    failedAddresses: status.failedAddresses ?? []
                });
                this.isSending.set(false);
            },
            error: err => {
                this.errorMessage.set(err?.error?.message || 'Failed to send emails.');
                this.isSending.set(false);
                this.showEmailDialog.set(false);
            }
        });
    }

    /** One-click send for Expiring Cards tab (like legacy) */
    sendExpiringCardWarnings(): void {
        const allIds = this.registrants().map(r => r.registrationId);
        if (allIds.length === 0) return;

        this.isSending.set(true);
        this.sendResult.set(null);

        const template = TEMPLATES['expiringCard'][0];
        const request: ArbSendEmailsRequest = {
            jobId: '00000000-0000-0000-0000-000000000000',
            senderUserId: '',
            flagType: FLAG_TYPE.ExpiringCard,
            emailSubject: template.subject,
            emailBody: template.body,
            registrationIds: allIds,
            notifyDirectors: true
        };

        this.arbService.sendEmailsAndAwait(request).subscribe({
            next: status => {
                this.sendResult.set({
                    sent: status.sent ?? 0,
                    failed: status.failed ?? 0,
                    failedAddresses: status.failedAddresses ?? []
                });
                this.isSending.set(false);
            },
            error: err => {
                this.errorMessage.set(err?.error?.message || 'Failed to send warning emails.');
                this.isSending.set(false);
            }
        });
    }

    tabLabel(type: number): string {
        return type === FLAG_TYPE.ExpiringCard ? 'Expiring Cards' : 'Behind in Payment';
    }
}
