import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { AuthService } from '@infrastructure/services/auth.service';
import { TsicLandingComponent } from '../tsic-landing/tsic-landing.component';
import { WidgetDashboardComponent } from '../widget-dashboard/widget-dashboard.component';

/**
 * Index route component that conditionally renders the appropriate landing page.
 *
 * - TSIC path → TsicLandingComponent (marketing page)
 * - Job path + authenticated (Phase 2) → WidgetDashboardComponent (hub dashboard)
 * - Job path + authenticated (Phase 1, no role) → redirect to role-selection
 * - Job path + unauthenticated → WidgetDashboardComponent (public mode)
 */
@Component({
    selector: 'app-landing-router',
    standalone: true,
    template: `
		@if (isTsic()) {
			<app-tsic-landing />
		} @else if (isAuthenticated()) {
			<app-widget-dashboard [mode]="'authenticated'" />
		} @else {
			<app-widget-dashboard [mode]="'public'" [jobPath]="currentJobPath()" />
		}
	`,
    imports: [TsicLandingComponent, WidgetDashboardComponent],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingRouterComponent {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly auth = inject(AuthService);

    // Get jobPath from parent route params
    private readonly jobPath = toSignal(
        this.route.parent!.paramMap.pipe(
            map(params => params.get('jobPath') || '')
        ),
        { initialValue: '' }
    );

    // Expose jobPath for template binding
    readonly currentJobPath = computed(() => this.jobPath());

    // Check if we're in TSIC context (empty string treated as tsic during initialization)
    readonly isTsic = computed(() => {
        const path = this.jobPath();
        return path === 'tsic' || path === '';
    });

    // Authenticated Phase 2 user — render hub dashboard inline
    readonly isAuthenticated = computed(() =>
        !this.isTsic() && this.auth.hasSelectedRole()
    );

    // Phase 1 (logged in, no role) → redirect to role selection
    private readonly roleSelectionRedirect = effect(() => {
        const path = this.jobPath();
        if (!path || path === 'tsic') return;

        const user = this.auth.currentUser();
        if (!user) return;

        // Phase 1: logged in but no role yet → force role selection
        if (!this.auth.hasSelectedRole()) {
            this.router.navigate(['/', path, 'role-selection'], { replaceUrl: true });
        }
    });
}
