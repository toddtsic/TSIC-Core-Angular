import { Component, inject, signal, computed, ChangeDetectionStrategy, viewChild } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { EmailBodyEditorComponent } from '@shared-ui/components/email-body-editor/email-body-editor.component';
import { TestSendButtonComponent, type TestSendOptions } from '@shared-ui/components/test-send-button/test-send-button.component';
import { GridAllModule, GridComponent, type ToolbarItems, type SelectionSettingsModel, type RowSelectEventArgs, type RowDeselectEventArgs } from '@syncfusion/ej2-angular-grids';
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
    imports: [DecimalPipe, DatePipe, FormsModule, GridAllModule, TsicDialogComponent, EmailBodyEditorComponent, TestSendButtonComponent],
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

    // Selection. The grid owns the checkboxes; the authoritative recipient set is accumulated
    // here from (de)selection deltas, so it survives a re-sort and the header select-all alike.
    // checkboxOnly means clicking a mailto/tel link in the contact card can never toggle a row -
    // which is what the old hand-rolled table needed a stopPropagation on every link for.
    readonly selectedIds = signal<Set<string>>(new Set());
    // persistSelection + the hidden registrationId primary key keep the ticks attached to the
    // ROW rather than to its position, so a re-sort cannot leave selectedIds holding families
    // whose checkboxes the grid has quietly cleared.
    readonly selectionSettings: SelectionSettingsModel = { type: 'Multiple', checkboxOnly: true, persistSelection: true };
    readonly toolbar: ToolbarItems[] = ['ExcelExport'];
    private readonly grid = viewChild<GridComponent>('grid');

    // Email dialog
    readonly showEmailDialog = signal(false);
    readonly emailSubject = signal('');
    readonly emailBody = signal('');
    readonly notifyDirectors = signal(true);
    readonly substitutionVars = signal<ArbSubstitutionVariableDto[]>([]);
    readonly isSending = signal(false);
    readonly sendResult = signal<{ sent: number; failed: number; failedAddresses: string[] } | null>(null);

    // A send failure is reported INSIDE the composer, not on the page behind it: the dialog
    // stays open so the director's typed message survives, and a page-level alert they cannot
    // see is no way to tell them the send did not happen.
    readonly sendError = signal<string | null>(null);

    // Expiring Cards has no selection to clear, so its one-click blast is latched instead:
    // once it has gone out for the loaded list, re-running the lookup is the only way to arm
    // it again. Without this, a second click sends the same warning to the same families.
    readonly warningsSent = signal(false);

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
                    this.clearSelection();
                    this.warningsSent.set(false);
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
        this.clearSelection();
        this.showEmailDialog.set(false);
        this.sendResult.set(null);
        this.sendError.set(null);
        this.warningsSent.set(false);
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

    onRowSelected(args: RowSelectEventArgs): void {
        this.applySelectionDelta(args?.data, true);
    }

    onRowDeselected(args: RowDeselectEventArgs): void {
        this.applySelectionDelta(args?.data, false);
    }

    // Header select-all and single-row clicks both arrive here; the event carries an array in
    // the first case and a single record in the second.
    private applySelectionDelta(data: unknown, add: boolean): void {
        const rows = (Array.isArray(data) ? data : data ? [data] : []) as ArbFlaggedRegistrantDto[];
        if (rows.length === 0) return;
        const next = new Set(this.selectedIds());
        for (const r of rows) {
            if (!r?.registrationId) continue;
            if (add) next.add(r.registrationId); else next.delete(r.registrationId);
        }
        this.selectedIds.set(next);
    }

    private clearSelection(): void {
        this.selectedIds.set(new Set());
        this.grid()?.clearRowSelection();
    }

    onToolbarClick(args: { item?: { id?: string } }): void {
        if (args.item?.id?.includes('excelexport')) {
            // includeHiddenColumn carries the per-address contact columns into the sheet as
            // plain text; on screen those same values are the link card (PL-056).
            this.grid()?.excelExport({
                includeHiddenColumn: true,
                fileName: this.activeTab() === FLAG_TYPE.ExpiringCard
                    ? 'arb-expiring-cards.xlsx'
                    : 'arb-behind-in-payment.xlsx'
            });
        }
    }

    openEmailDialog(): void {
        this.emailSubject.set('');
        this.emailBody.set('');
        this.notifyDirectors.set(true);
        this.sendResult.set(null);
        this.sendError.set(null);
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

    // AR-039 part 4. This is dunning email to families who owe money, so a completed send must
    // leave nothing armed behind it: the composer closes and the picks are dropped, and the
    // outcome is reported on the page. It used to do the opposite - success left the dialog open
    // with the same recipients still ticked and Send live again, while FAILURE was the case that
    // closed it and threw away the typed message.
    sendEmails(): void {
        const ids = Array.from(this.selectedIds());
        if (ids.length === 0 || this.isSending()) return;

        this.isSending.set(true);
        this.sendResult.set(null);
        this.sendError.set(null);

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
                this.showEmailDialog.set(false);
                this.clearSelection();
            },
            error: err => {
                this.sendError.set(err?.error?.message || 'Failed to send emails.');
                this.isSending.set(false);
            }
        });
    }

    /** One-click send for Expiring Cards tab (like legacy). Same duplicate-send hazard as the
     *  composer (AR-039 part 4), minus a selection to clear: the latch is what stops a second
     *  click re-warning every family on the loaded list. */
    sendExpiringCardWarnings(): void {
        const allIds = this.registrants().map(r => r.registrationId);
        if (allIds.length === 0 || this.isSending() || this.warningsSent()) return;

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
                this.warningsSent.set(true);
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
