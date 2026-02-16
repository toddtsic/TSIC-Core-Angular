import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { AuthService } from '@infrastructure/services/auth.service';
import { TsicLandingComponent } from '../tsic-landing/tsic-landing.component';
import { WidgetDashboardComponent } from '../widget-dashboard/widget-dashboard.component';

/**
 * Wrapper component that conditionally loads the appropriate landing page
 * based on whether the jobPath is 'tsic' or an actual job slug.
 *
 * - TSIC path → TsicLandingComponent (marketing page)
 * - Job path + authenticated with role → redirect to /dashboard
 * - Job path + unauthenticated/no role → WidgetDashboardComponent in public mode
 */
@Component({
    selector: 'app-landing-router',
    standalone: true,
    template: `
		@if (isTsic()) {
			<app-tsic-landing />
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
    isTsic = computed(() => {
        const path = this.jobPath();
        return path === 'tsic' || path === '';
    });

    // Redirect authenticated users with a selected role to the widget dashboard route
    private readonly dashboardRedirect = effect(() => {
        const path = this.jobPath();
        if (!path || this.isTsic()) return;

        if (this.auth.hasSelectedRole()) {
            this.router.navigate(['/', path, 'dashboard'], { replaceUrl: true });
        }
    });
}
