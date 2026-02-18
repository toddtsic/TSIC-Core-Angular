import { Component, computed, inject, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, ActivatedRouteSnapshot } from '@angular/router';
import { CommonModule } from '@angular/common';
import { TranslateLegacyUrlsPipe } from '@infrastructure/pipes/translate-legacy-urls.pipe';
import { InternalLinkDirective } from '@infrastructure/directives/internal-link.directive';
import { JobService } from '@infrastructure/services/job.service';
import { AuthService } from '@infrastructure/services/auth.service';

/**
 * Bulletins Display Component
 *
 * Self-sufficient: injects JobService for bulletin data and resolves jobPath
 * from auth state or route tree. Zero inputs required.
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

    readonly bulletins = computed(() => this.jobService.bulletins());
    readonly loading = computed(() => this.jobService.bulletinsLoading());
    readonly error = computed(() => this.jobService.bulletinsError());
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
}
