import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';
import { AdultRegistrationService } from '@infrastructure/services/adult-registration.service';
import { TestSendButtonComponent, type TestSendOptions } from '@shared-ui/components/test-send-button/test-send-button.component';
import { environment } from '@environments/environment';
import { RichTextPipe } from '@infrastructure/pipes/rich-text.pipe';

@Component({
    selector: 'app-adult-confirmation-step',
    standalone: true,
    imports: [TestSendButtonComponent, RichTextPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <!-- Centered hero -->
        <div class="welcome-hero">
            <h4 class="welcome-title">
                <i class="bi bi-check-circle-fill welcome-icon" style="color: var(--bs-success)"></i>
                Registration Complete!
            </h4>
            <p class="welcome-desc">
                <i class="bi bi-person-check me-1"></i>You're registered as <strong>{{ state.roleDisplayName() }}</strong>
                <span class="desc-dot"></span>
                <i class="bi bi-envelope me-1"></i>A confirmation email is on its way
            </p>
        </div>

        <div class="card shadow border-0 card-rounded">
            <div class="card-body">
                @if (state.confirmationLoading()) {
                    <div class="d-flex justify-content-center my-4">
                        <div class="spinner-border text-primary" role="status">
                            <span class="visually-hidden">Loading confirmation...</span>
                        </div>
                    </div>
                } @else if (state.confirmationHtml()) {
                    <div class="confirmation-html" [innerHTML]="state.confirmationHtml() | richText"></div>
                } @else {
                    <div class="alert alert-info" role="status">
                        Your registration has been recorded.
                    </div>
                }

                @if (emailMessage()) {
                    <div class="alert mt-3 mb-0"
                        [class.alert-success]="!emailError()"
                        [class.alert-danger]="emailError()" role="status">
                        {{ emailMessage() }}
                    </div>
                }

                <!-- ONE resend button, two behaviors: off-prod the resend couldn't deliver anyway
                     (sandbox email gate), so it IS the test-send popover; on prod it really resends. -->
                <div class="d-flex justify-content-center gap-3 mt-4">
                    @if (isNonProd) {
                        <app-test-send-button
                            label="Resend Confirmation Email"
                            variant="primary"
                            [busy]="testSending()"
                            (send)="onTestSend($event)" />
                    } @else {
                        <button class="btn btn-outline-primary"
                            [disabled]="emailSending()"
                            (click)="onResendEmail()">
                            @if (emailSending()) {
                                <span class="spinner-border spinner-border-sm me-1" role="status"></span>
                            } @else {
                                <i class="bi bi-envelope me-1"></i>
                            }
                            Resend Confirmation Email
                        </button>
                    }

                    <!-- AR-035: the registration is already complete here; this button only
                         navigates to the job home page (see onFinishConfirmation). "Finish" plus a
                         forward arrow named an act that had already happened. -->
                    <button class="btn btn-primary" (click)="finished.emit()">
                        <i class="bi bi-house me-1"></i> Return Home
                    </button>
                </div>
            </div>
        </div>
    `,
    styles: [`
        .confirmation-html {
            padding: var(--space-4);
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
            background: var(--brand-surface);
        }
    `],
})
export class ConfirmationStepComponent {
    readonly state = inject(AdultWizardStateService);
    private readonly api = inject(AdultRegistrationService);
    readonly finished = output<void>();

    readonly emailSending = signal(false);
    readonly emailMessage = signal<string | null>(null);
    readonly emailError = signal(false);

    /** Test send is a non-prod affordance; the backend refuses in Production regardless. */
    readonly isNonProd = !environment.production;
    readonly testSending = signal(false);

    /** Renders THIS confirmation and delivers it to the tester's inbox instead of the registrant. */
    onTestSend(options: TestSendOptions): void {
        const regId = this.state.registrationId();
        if (!regId || this.testSending()) return;

        this.testSending.set(true);
        this.emailMessage.set(null);

        this.api.testSendConfirmationEmail(regId, options.recipient).subscribe({
            next: (result) => {
                this.testSending.set(false);
                this.emailError.set(!result.sent);
                this.emailMessage.set(result.sent
                    ? `Test confirmation (rendered for ${result.renderedFor}) sent to ${result.recipient}.`
                    : result.message || 'Test send failed.');
            },
            error: () => {
                this.testSending.set(false);
                this.emailError.set(true);
                this.emailMessage.set('Test send failed. Please try again.');
            },
        });
    }

    onResendEmail(): void {
        const regId = this.state.registrationId();
        if (!regId) return;

        this.emailSending.set(true);
        this.emailMessage.set(null);

        this.api.resendConfirmationEmail(regId).subscribe({
            // A 200 does not mean mail went out — the server reports `sent` so a send that
            // reached nobody is not dressed up as a success.
            next: (resp) => {
                this.emailSending.set(false);
                this.emailError.set(!resp.sent);
                this.emailMessage.set(
                    resp.message ?? (resp.sent
                        ? 'Confirmation email sent successfully.'
                        : 'No confirmation email could be sent.'),
                );
            },
            error: () => {
                this.emailSending.set(false);
                this.emailError.set(true);
                this.emailMessage.set('Failed to send confirmation email. Please try again.');
            },
        });
    }
}
