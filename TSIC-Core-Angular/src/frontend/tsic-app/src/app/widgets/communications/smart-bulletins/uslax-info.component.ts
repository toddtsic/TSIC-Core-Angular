import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';

/**
 * USA Lacrosse Membership notice — the proactive smart bulletin for lacrosse jobs
 * whose player profile REQUIRES a USA Lacrosse membership number
 * (pulse.playerRegRequiresUsLax). It front-runs the most common registration
 * support calls by spelling out, BEFORE a family starts, exactly what makes USA
 * Lacrosse validation fail: a mismatched DOB / last name, a membership that lapses
 * before the event's required date, and the (frequently-missed) one-time age
 * verification. Purely informational — the only links point OUT to USA Lacrosse,
 * there is no in-app CTA. The parent band gates display on reg-open AND this flag.
 */
@Component({
	selector: 'app-uslax-info',
	standalone: true,
	templateUrl: './uslax-info.component.html',
	styleUrl: './uslax-info.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsLaxInfoComponent {
	private readonly pulseService = inject(JobPulseService);
	private readonly pulse = computed(() => this.pulseService.pulse());

	/** "August 15, 2026" — null when the director set no explicit through-date. */
	protected readonly validThrough = computed<string | null>(() => {
		const iso = this.pulse()?.usLaxMembershipValidThrough;
		if (!iso) return null;
		const d = new Date(iso);
		return Number.isNaN(d.getTime())
			? null
			: new Intl.DateTimeFormat(undefined, { month: 'long', day: 'numeric', year: 'numeric' }).format(d);
	});
}
