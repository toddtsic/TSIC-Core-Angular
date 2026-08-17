import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';

/**
 * 404 page.
 *
 * Reached two ways, and both must look deliberate rather than like a crash:
 *   - the `**` wildcard, for a URL that matches no route at all
 *   - JobService.requestJobMetadata, which redirects here when the server 404s a job. That
 *     redirect uses skipLocationChange, so the address bar still shows what the user typed
 *     — which is why this page can echo the attempted path back to them.
 *
 * Styled off the TSIC marketing landing (views/home/tsic-landing): same cross-fading hero,
 * same white veil, same wordmark and pill buttons. A dead end is still a brand surface.
 */
@Component({
    selector: 'app-not-found',
    standalone: true,
    imports: [],
    templateUrl: './not-found.component.html',
    styleUrl: './not-found.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class NotFoundComponent {
    private readonly authService = inject(AuthService);
    private readonly router = inject(Router);

    /**
     * The address the user actually asked for. Interpolated, never bound as HTML — Angular
     * escapes it, and it is attacker-supplied by definition.
     */
    readonly attemptedPath = signal<string>(this.readPath());

    /** Suppress the chip when there is nothing informative to show. */
    readonly showAttemptedPath = computed(() => {
        const p = this.attemptedPath();
        return !!p && p !== '/' && p !== '/not-found';
    });

    /** Where "home" is depends on whether they have a job. Same rule as the old page. */
    readonly homeLabel = computed(() =>
        this.authService.getCurrentUser()?.jobPath ? 'Back to your event' : 'Go to TeamSportsInfo'
    );

    /** Only offer "Go Back" when there is somewhere to go back TO. */
    readonly canGoBack = signal<boolean>(this.readHistoryDepth() > 1);

    goHome(): void {
        const user = this.authService.getCurrentUser();
        this.router.navigate([user?.jobPath ? user.jobPath : 'tsic']);
    }

    goBack(): void {
        globalThis.history?.back();
    }

    private readPath(): string {
        try { return globalThis.location?.pathname ?? ''; } catch { return ''; }
    }

    private readHistoryDepth(): number {
        try { return globalThis.history?.length ?? 0; } catch { return 0; }
    }
}
