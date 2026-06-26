import { ChangeDetectionStrategy, Component, computed, inject, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobLandingComponent } from '../job-landing/job-landing.component';

/**
 * Renders the job landing. EVERYONE — anonymous, non-admin, AND admin — lands on
 * the public bulletin/Smart-Bulletins page; admins reach their widget dashboard
 * via the header "Dashboard" menu item, not an automatic redirect. (Admins viewing
 * the public landing see the Smart Bulletins band computed as the public sees it —
 * see `publicView` in SmartBulletinsComponent.)
 *
 * The one viewer-specific case: a Phase-1 session (logged in but no role selected)
 * on the landing route is a stale session — clear local auth so the user gets the
 * clean public landing, matching refresh behavior.
 */
@Component({
    selector: 'app-landing-router',
    standalone: true,
    template: `<app-job-landing [jobPath]="currentJobPath()" />`,
    imports: [JobLandingComponent],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingRouterComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly auth = inject(AuthService);

    private readonly jobPath = toSignal(
        this.route.parent!.paramMap.pipe(
            map(params => params.get('jobPath') || '')
        ),
        { initialValue: '' }
    );

    readonly currentJobPath = computed(() => this.jobPath());

    ngOnInit(): void {
        if (!this.jobPath()) return;
        const user = this.auth.currentUser();
        if (!user) return;

        // Stale Phase-1 session (no role selected) → clear local auth for a clean public landing.
        if (!this.auth.hasSelectedRole()) {
            this.auth.logoutLocal();
        }
    }
}
