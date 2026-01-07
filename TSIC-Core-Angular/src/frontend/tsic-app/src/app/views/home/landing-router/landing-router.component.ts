import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { TsicLandingComponent } from '../tsic-landing/tsic-landing.component';
import { JobLandingComponent } from '../job-landing/job-landing.component';

/**
 * Wrapper component that conditionally loads the appropriate landing page
 * based on whether the jobPath is 'tsic' or an actual job slug.
 */
@Component({
    selector: 'app-landing-router',
    standalone: true,
    template: `
		@if (isTsic()) {
			<app-tsic-landing />
		} @else {
			<app-job-landing />
		}
	`,
    imports: [TsicLandingComponent, JobLandingComponent]
})
export class LandingRouterComponent {
    private readonly route = inject(ActivatedRoute);

    // Get jobPath from parent route params
    private readonly jobPath = toSignal(
        this.route.parent!.paramMap.pipe(
            map(params => params.get('jobPath') || '')
        ),
        { initialValue: '' }
    );

    // Check if we're in TSIC context (empty string treated as tsic during initialization)
    isTsic = computed(() => {
        const path = this.jobPath();
        return path === 'tsic' || path === '';
    });
}
