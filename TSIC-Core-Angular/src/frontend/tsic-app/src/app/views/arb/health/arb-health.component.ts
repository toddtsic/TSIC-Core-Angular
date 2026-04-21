import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ArbDefensiveService } from './services/arb-defensive.service';
import type {
    ArbFlaggedRegistrantDto,
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
                '<li>Under \'Player\' in the upper right, select \'Update CC Info (will also pay for failed auto-payments)\'</li>' +
                '<li>Enter your credit card information and you will see the amount due at the bottom of the screen.</li>' +
                '<li>Click Submit to make the payment and reactivate your future automatic payments.</li>' +
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
                '<li>Under \'Player\' in the upper right, select \'Update CC Info (will also pay for failed auto-payments)\'</li>' +
                '<li>Enter your credit card information and you will see the amount due at the bottom of the screen</li>' +
                '<li>Click Submit to make the payment and reactivate your future automatic payments</li>' +
                '</ol>'
        }
    ]
};

@Component({
    selector: 'app-arb-health',
    standalone: true,
    imports: [DecimalPipe, DatePipe, FormsModule, TsicDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './arb-health.component.html',
    styleUrl: './arb-health.component.scss'
})
export class ArbHealthComponent {
    private readonly arbService = inject(ArbDefensiveService);

    // Tab state
    readonly activeTab = signal<number>(FLAG_TYPE.ExpiringCard);
    readonly FLAG_TYPE = FLAG_TYPE;

    // Data
    readonly registrants = signal<ArbFlaggedRegistrantDto[]>([]);
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);

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

    /** Templates available for the active tab */
    readonly availableTemplates = computed<EmailTemplate[]>(() => {
        return this.activeTab() === FLAG_TYPE.ExpiringCard
            ? TEMPLATES['expiringCard']
            : TEMPLATES['behindInPayment'];
    });

    constructor() {
        this.loadTab(FLAG_TYPE.ExpiringCard);
        this.loadSubstitutionVars();
    }

    switchTab(type: number): void {
        if (this.activeTab() === type) return;
        this.activeTab.set(type);
        this.selectedIds.set(new Set());
        this.showEmailDialog.set(false);
        this.sendResult.set(null);
        this.loadTab(type);
    }

    private loadTab(type: number): void {
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
        this.emailBody.update(body => body + token);
    }

    onSubjectChange(value: string): void {
        this.emailSubject.set(value);
    }

    onBodyChange(value: string): void {
        this.emailBody.set(value);
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

        this.arbService.sendEmails(request).subscribe({
            next: result => {
                this.sendResult.set({
                    sent: result.emailsSent ?? 0,
                    failed: result.emailsFailed ?? 0,
                    failedAddresses: result.failedAddresses ?? []
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

        this.arbService.sendEmails(request).subscribe({
            next: result => {
                this.sendResult.set({
                    sent: result.emailsSent ?? 0,
                    failed: result.emailsFailed ?? 0,
                    failedAddresses: result.failedAddresses ?? []
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
