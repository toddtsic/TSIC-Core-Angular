import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';
import { AdultRegistrationService } from '@infrastructure/services/adult-registration.service';

@Component({
    selector: 'app-adult-confirmation-step',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="step-content text-center py-4">
            <i class="bi bi-check-circle-fill text-success" style="font-size: 3rem;"></i>
            <h3 class="mt-3">Registration Complete!</h3>
            <p class="text-muted">
                You have been registered as <strong>{{ state.selectedRole()?.displayName }}</strong>.
            </p>

            @if (state.confirmationLoading()) {
                <div class="d-flex justify-content-center my-4">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Loading confirmation...</span>
                    </div>
                </div>
            } @else if (state.confirmationHtml()) {
                <div class="confirmation-html mt-4 text-start" [innerHTML]="state.confirmationHtml()"></div>
            }

            <div class="d-flex justify-content-center gap-3 mt-4">
                <button class="btn btn-outline-secondary"
                    [disabled]="emailSending()"
                    (click)="onResendEmail()">
                    @if (emailSending()) {
                        <span class="spinner-border spinner-border-sm me-1" role="status"></span>
                    } @else {
                        <i class="bi bi-envelope me-1"></i>
                    }
                    Resend Confirmation Email
                </button>

                <button class="btn btn-primary" (click)="finished.emit()">
                    Finish
                </button>
            </div>

            @if (emailMessage()) {
                <div class="alert mt-3" [class.alert-success]="!emailError()" [class.alert-danger]="emailError()" role="status">
                    {{ emailMessage() }}
                </div>
            }
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

    onResendEmail(): void {
        const regId = this.state.registrationId();
        if (!regId) return;

        this.emailSending.set(true);
        this.emailMessage.set(null);

        this.api.resendConfirmationEmail(regId).subscribe({
            next: () => {
                this.emailSending.set(false);
                this.emailError.set(false);
                this.emailMessage.set('Confirmation email sent successfully.');
            },
            error: () => {
                this.emailSending.set(false);
                this.emailError.set(true);
                this.emailMessage.set('Failed to send confirmation email. Please try again.');
            },
        });
    }
}
