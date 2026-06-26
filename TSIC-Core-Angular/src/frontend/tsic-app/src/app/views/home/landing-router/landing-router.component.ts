import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobLandingComponent } from '../job-landing/job-landing.component';

/**
 * Routes the job landing by viewer.
 * - Phase 1 (logged-in but no role selected) on the landing route is treated
 *   as a stale session — clear local auth so the user gets the public landing,
 *   matching refresh behavior.
 * - Admins (Superuser/Director/SuperDirector) with a selected role are redirected
 *   to their widget dashboard. Everyone else (anonymous + non-admin) sees the
 *   public bulletin/quicklinks landing.
 * - The `publicView` route data flag suppresses the admin redirect, so admins can
 *   explicitly preview the public landing (the /:jobPath/home route).
 */
@Component({
    selector: 'app-landing-router',
    standalone: true,
    template: `
		@if (!redirecting()) {
			<app-job-landing [jobPath]="currentJobPath()" />
		}
	`,
    imports: [JobLandingComponent],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingRouterComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly auth = inject(AuthService);

    private readonly jobPath = toSignal(
        this.route.parent!.paramMap.pipe(
            map(params => params.get('jobPath') || '')
        ),
        { initialValue: '' }
    );

    readonly currentJobPath = computed(() => this.jobPath());

    /** True once we've decided to redirect an admin to the dashboard — suppresses the landing flash. */
    readonly redirecting = signal(false);

    ngOnInit(): void {
        const path = this.jobPath();
        if (!path) return;

        const user = this.auth.currentUser();
        if (!user) return;

        if (!this.auth.hasSelectedRole()) {
            this.auth.logoutLocal();
            return;
        }

        const publicView = this.route.snapshot.data['publicView'] === true;
        if (!publicView && this.auth.isAdmin()) {
            this.redirecting.set(true);
            this.router.navigateByUrl(`/${path}/dashboard`);
        }
    }
}
