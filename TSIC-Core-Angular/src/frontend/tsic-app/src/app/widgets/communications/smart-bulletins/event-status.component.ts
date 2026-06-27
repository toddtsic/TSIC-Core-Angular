import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { derivePhase, startOfDay } from '@shared/landing/landing-phase';

interface StatusView {
	icon: string;
	headline: string;
	sub: string;
}

/**
 * Event Status — the smart bulletin for the lifecycle "dead zones" the action
 * panels leave bare: registration not open yet (`planned`), nothing/closed
 * (`preview`), and finished (`concluded`). It speaks the current phase in plain
 * language so a job between states (e.g. registration closed but the schedule not
 * yet published) never shows an empty page.
 *
 * Self-hides in the action phases (inSeason/preEvent/registrationOpen) — there the
 * Game-Day / Registration panels own the page. `superseded` is handled separately
 * (the landing redirects to the live later-year event).
 */
@Component({
	selector: 'app-event-status',
	standalone: true,
	templateUrl: './event-status.component.html',
	styleUrl: './event-status.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EventStatusComponent {
	private readonly pulseService = inject(JobPulseService);
	private readonly pulse = computed(() => this.pulseService.pulse());

	protected readonly view = computed<StatusView | null>(() => {
		const p = this.pulse();
		if (!p) return null;
		switch (derivePhase(p, new Date())) {
			case 'planned': {
				const when = this.formatOpens(p.playerRegOpensSoonest);
				return {
					icon: 'bi-hourglass-split',
					headline: when ? `Registration opens ${when}` : 'Registration is coming soon',
					sub: 'Check back to sign up — this page updates the moment it opens.',
				};
			}
			case 'preview': {
				// "Closed" (a registration window that has elapsed, or a suspended page)
				// reads differently from "not configured yet".
				const closed = p.publicSuspended || this.isPast(p.playerRegClosesSoonest) || this.isPast(p.registrationExpiry);
				return closed
					? { icon: 'bi-lock', headline: 'Registration is closed', sub: 'The game schedule will be posted here once it’s set.' }
					: { icon: 'bi-hourglass', headline: 'This event is coming soon', sub: 'Details will appear here as they’re announced.' };
			}
			case 'concluded':
				return {
					icon: 'bi-flag-fill',
					headline: 'This event has concluded',
					sub: 'Thanks for participating — hope to see you back next season!',
				};
			default:
				return null; // action phases — the panels own the page
		}
	});

	/** Day-granularity phrasing ("today" / "tomorrow" / "in N days" / "on Mon D, YYYY"). */
	private formatOpens(iso?: string | null): string | null {
		if (!iso) return null;
		const t = new Date(iso);
		if (Number.isNaN(t.getTime())) return null;
		const days = Math.round((startOfDay(t).getTime() - startOfDay(new Date()).getTime()) / 86_400_000);
		if (days <= 0) return 'today';
		if (days === 1) return 'tomorrow';
		if (days <= 21) return `in ${days} days`;
		return `on ${new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' }).format(t)}`;
	}

	private isPast(iso?: string | null): boolean {
		if (!iso) return false;
		const t = new Date(iso);
		return !Number.isNaN(t.getTime()) && startOfDay(t).getTime() < startOfDay(new Date()).getTime();
	}
}
