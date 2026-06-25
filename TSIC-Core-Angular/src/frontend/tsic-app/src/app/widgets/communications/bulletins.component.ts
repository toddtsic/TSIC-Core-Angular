import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, ActivatedRouteSnapshot, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { TranslateLegacyUrlsPipe } from '@infrastructure/pipes/translate-legacy-urls.pipe';
import { InternalLinkDirective } from '@infrastructure/directives/internal-link.directive';
import { JobService } from '@infrastructure/services/job.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { BulletinAdminService } from '@views/communications/bulletins/services/bulletin-admin.service';
import type { BulletinDto } from '@core/api';

/**
 * Bulletins Display Component
 *
 * Self-sufficient: injects JobService for bulletin data and resolves jobPath
 * from auth state or route tree. Zero inputs required.
 *
 * Admins viewing the public job-home see a "quick inactivate" control on each
 * bulletin (set Active=false) — the manual counterpart to the editor, so a
 * stale/redundant bulletin can be hidden in one click without leaving the page.
 */
@Component({
    selector: 'app-bulletins',
    standalone: true,
    imports: [CommonModule, TranslateLegacyUrlsPipe, InternalLinkDirective],
    templateUrl: './bulletins.component.html',
    styleUrl: './bulletins.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BulletinsComponent {
    private readonly jobService = inject(JobService);
    private readonly auth = inject(AuthService);
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly toast = inject(ToastService);
    private readonly bulletinAdmin = inject(BulletinAdminService);

    readonly bulletins = computed(() => this.jobService.bulletins());
    readonly loading = computed(() => this.jobService.bulletinsLoading());
    readonly error = computed(() => this.jobService.bulletinsError());

    /** Admins get the per-bulletin quick-inactivate control. */
    readonly isAdmin = computed(() => this.auth.isAdmin());

    /** Bulletin id currently being inactivated (disables its button). */
    readonly deactivatingId = signal<string | null>(null);

    readonly jobPath = computed(() => {
        const user = this.auth.currentUser();
        if (user?.jobPath) return user.jobPath;
        let r: ActivatedRouteSnapshot | null = this.route.snapshot;
        while (r) {
            const jp = r.paramMap.get('jobPath');
            if (jp) return jp;
            r = r.parent;
        }
        return '';
    });

    /** Deep-link to the bulletin editor with this bulletin's edit modal open. */
    edit(bulletin: BulletinDto, event: Event): void {
        event.stopPropagation();
        const jp = this.jobPath();
        if (!jp) return;
        this.router.navigate(['/', jp, 'communications', 'bulletins'], {
            // from=home so the editor returns here (not to the admin grid) once done.
            queryParams: { edit: bulletin.bulletinId, from: 'home' }
        });
    }

    /** Quick-inactivate: persist Active=false, then drop it from the view. */
    deactivate(bulletin: BulletinDto, event: Event): void {
        event.stopPropagation();
        if (this.deactivatingId()) return;
        this.deactivatingId.set(bulletin.bulletinId);
        this.bulletinAdmin.deactivateBulletin(bulletin.bulletinId).subscribe({
            next: () => {
                this.jobService.bulletins.set(
                    this.jobService.bulletins().filter(b => b.bulletinId !== bulletin.bulletinId)
                );
                this.deactivatingId.set(null);
                this.toast.show('Bulletin hidden', 'success');
            },
            error: (err) => {
                this.deactivatingId.set(null);
                this.toast.show(err?.error?.message || 'Could not hide bulletin', 'danger');
            }
        });
    }
}
