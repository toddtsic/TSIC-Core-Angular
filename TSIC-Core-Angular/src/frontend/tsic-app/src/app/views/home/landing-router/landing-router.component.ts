import { ChangeDetectionStrategy, Component, computed, inject, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobLandingComponent } from '../job-landing/job-landing.component';

/**
 * Renders the unified job landing for both authenticated and public visitors.
 * Phase 1 (logged-in but no role selected) on the landing route is treated
 * as a stale session — clear local auth so the user gets the public landing,
 * matching refresh behavior.
 */
@Component({
    selector: 'app-landing-router',
    standalone: true,
    template: `
		<app-job-landing [jobPath]="currentJobPath()" />
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

    ngOnInit(): void {
        const path = this.jobPath();
        if (!path) return;

        const user = this.auth.currentUser();
        if (!user) return;

        if (!this.auth.hasSelectedRole()) {
            this.auth.logoutLocal();
        }
    }
}
