import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Shared loading / error / empty state component for wizard steps.
 *
 * Usage:
 * ```html
 * <!-- Loading -->
 * <app-wizard-loading message="Loading teams..." />
 *
 * <!-- Error with retry -->
 * <app-wizard-loading state="error" message="Failed to load data." [showRetry]="true" (retry)="reload()" />
 *
 * <!-- Empty state with CTA -->
 * <app-wizard-loading state="empty" message="No teams registered yet." />
 * ```
 */
@Component({
    selector: 'app-wizard-loading',
    standalone: true,
    template: `
        <div class="text-center py-4">
            @switch (state()) {
                @case ('loading') {
                    <div class="spinner-border text-primary mb-3" [class.spinner-border-sm]="size() === 'sm'"
                         role="status" aria-live="polite">
                        <span class="visually-hidden">{{ message() }}</span>
                    </div>
                    <p class="text-muted mb-0">{{ message() }}</p>
                }
                @case ('error') {
                    <div class="alert alert-danger d-flex align-items-start text-start" role="alert">
                        <i class="bi bi-exclamation-triangle-fill me-2 mt-1 fs-5 shrink-0"></i>
                        <div class="grow">
                            <strong>Error</strong>
                            <p class="mb-0 mt-1">{{ message() }}</p>
                            @if (showRetry()) {
                                <button type="button" class="btn btn-sm btn-outline-danger mt-2"
                                        (click)="retry.emit()">
                                    <i class="bi bi-arrow-clockwise me-1"></i>Retry
                                </button>
                            }
                        </div>
                    </div>
                }
                @case ('empty') {
                    <i class="bi bi-inbox fs-1 d-block mb-3 text-muted"></i>
                    <p class="text-muted mb-0">{{ message() }}</p>
                }
            }
        </div>
    `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WizardLoadingComponent {
    readonly state = input<'loading' | 'error' | 'empty'>('loading');
    readonly message = input('Loading...');
    readonly size = input<'sm' | 'md'>('md');
    readonly showRetry = input(false);
    readonly retry = output<void>();
}
