import { Component, inject, Input, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ToastService } from '@shared-ui/toast.service';
import { TeamRegistrationService } from '../services/team-registration.service';

@Component({
  selector: 'app-review-step',
  templateUrl: './review-step.component.html',
  styleUrls: ['./review-step.component.scss'],
  standalone: true,
  imports: [CommonModule]
})
export class ReviewStepComponent implements OnInit {
  @Input() registrationId!: string;

  private readonly teamRegService = inject(TeamRegistrationService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly toast = inject(ToastService);

  // State signals
  readonly isLoading = signal(false);
  readonly isResending = signal(false);
  readonly confirmationHtml = signal<SafeHtml | null>(null);
  readonly error = signal<string | null>(null);

  // Computed
  readonly canResend = computed(() => !this.isResending() && !!this.confirmationHtml());

  ngOnInit(): void {
    this.loadConfirmationText();
    // Auto-send confirmation email when reaching Review step
    this.sendConfirmationEmail(false);
  }

  loadConfirmationText(): void {
    if (!this.registrationId) {
      this.error.set('Registration ID not provided');
      return;
    }

    this.isLoading.set(true);
    this.error.set(null);

    this.teamRegService.getConfirmationText(this.registrationId).subscribe({
      next: (html: string) => {
        if (!html) {
          this.error.set('No confirmation template found for this event');
        } else {
          // Sanitize HTML from backend (it's trusted because it's from our substitution service)
          this.confirmationHtml.set(this.sanitizer.bypassSecurityTrustHtml(html));
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Error loading confirmation text:', err);
        this.error.set(err?.error?.Message || 'Failed to load confirmation text');
        this.isLoading.set(false);
      }
    });
  }

  sendConfirmationEmail(forceResend: boolean): void {
    if (!this.registrationId) return;

    if (forceResend) {
      this.isResending.set(true);
    }

    this.teamRegService.sendConfirmationEmail(this.registrationId, forceResend).subscribe({
      next: () => {
        if (forceResend) {
          this.toast.show('✓ Confirmation email sent successfully', 'success', 5000);
          this.isResending.set(false);
        }
      },
      error: (err) => {
        console.error('Error sending confirmation email:', err);
        if (forceResend) {
          const message = err?.error?.Message || 'Failed to send confirmation email';
          this.toast.show(`✗ ${message}`, 'danger', 7000);
          this.isResending.set(false);
        }
      }
    });
  }

  resendConfirmationEmail(): void {
    if (!this.registrationId || this.isResending()) return;
    this.sendConfirmationEmail(true);
  }

  printConfirmation(): void {
    if (!this.confirmationHtml()) return;
    
    // Open print dialog for the confirmation content
    const printWindow = window.open('', '_blank');
    if (printWindow) {
      printWindow.document.write(`
        <!DOCTYPE html>
        <html>
        <head>
          <title>Registration Confirmation</title>
          <style>
            body { font-family: Arial, sans-serif; padding: 20px; }
            table { border-collapse: collapse; width: 100%; }
            th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
            th { background-color: #f2f2f2; }
            @media print {
              button { display: none; }
            }
          </style>
        </head>
        <body>
          ${this.confirmationHtml()}
        </body>
        </html>
      `);
      printWindow.document.close();
      printWindow.focus();
      setTimeout(() => {
        printWindow.print();
        printWindow.close();
      }, 250);
    }
  }
}
