import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../core/services/auth.service';
import { ToastService } from '../shared/toast.service';

@Component({
    selector: 'app-terms-of-service',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './terms-of-service.component.html',
    styleUrls: ['./terms-of-service.component.scss']
})
export class TermsOfServiceComponent {
    private readonly authService = inject(AuthService);
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly toastService = inject(ToastService);

    accepted = signal(false);
    submitting = signal(false);
    error = signal<string | null>(null);

    onAccept(): void {
        if (!this.accepted()) {
            return;
        }

        this.submitting.set(true);
        this.error.set(null);

        this.authService.acceptTos().subscribe({
            next: () => {
                this.submitting.set(false);
                this.toastService.show('Terms of Service accepted successfully', 'success');
                const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') || '/tsic/role-selection';
                this.router.navigateByUrl(returnUrl);
            },
            error: (err) => {
                this.submitting.set(false);
                this.error.set(err?.error?.message || 'Failed to accept Terms of Service. Please try again.');
            }
        });
    }
}
