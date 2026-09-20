import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { derivePhase } from '@shared/landing/landing-phase';

interface StatusView {
	icon: string;
	headline: string;
	sub: string;
	/** Optional action rendered under the sub-line. Concluded-only today: the final-standings
	 *  link, absorbed from the Game-Day panel so a finished event shows ONE card instead of a
	 *  status notice stacked on a near-empty "Schedule Links" card wearing the same flag icon. */
	cta?: { label: string; link: string; queryParams?: Record<string, string> };
}

/**
 * Event Status — the smart bulletin for the one lifecycle state no action panel
 * covers: `concluded`. It reports a FACT the server has already decided
 * (pulse.eventConcluded) and carries the final-standings link.
 *
 * It deliberately says NOTHING about an event that has not started yet. The
 * forward-looking copy that used to live here ("Registration is coming soon",
 * "This event is coming soon") was removed 2026-09-20: it reported the system's
 * GUESS at a director's intentions, not a status. Three problems with it —
 *   1. it fired on the player profile merely EXISTING with the switch off, so a
 *      director mid-setup was advertised without ever asking to be;
 *   2. it said the unqualified word "Registration" while firing on player,
 *      coach, referee and recruiter signals alike — and never on club-rep/team
 *      registration, which has no date signal at all;
 *   3. there was no way to turn it off. The only per-job lever was the team
 *      Effectiveasofdate, which drives the wizard's available-teams list and is
 *      per-team — unusable as a display switch.
 * A director who wants to tease an upcoming event authors a bulletin and
 * conditions it on `playerRegistrationPlanned` / `adultRegistrationPlanned`
 * (both are bulletin condition keys already), which self-hides the moment they
 * open registration. Intent belongs to the person who holds it.
 *
 * Self-hides in every other phase — the Game-Day / Registration panels own those,
 * and a site with nothing configured correctly shows no band at all. `superseded`
 * is handled separately (the landing redirects to the live later-year event).
 */
@Component({
	selector: 'app-event-status',
	standalone: true,
	imports: [RouterLink],
	templateUrl: './event-status.component.html',
	styleUrl: './event-status.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EventStatusComponent {
	/** Absolute, jobPath-prefixed schedule link, or null when this event has no public schedule
	 *  to link to. The BAND owns that decision (it already enforces competitive + schedulePublished
	 *  + firstGameDate + the view-schedule key for the Game-Day panel) and hands the resolved link
	 *  down — so a concluded event that never published a schedule shows the notice with no button,
	 *  rather than this component re-deriving the same conditions and drifting from that gate. */
	readonly standingsLink = input<string | null>(null);

	private readonly pulseService = inject(JobPulseService);
	private readonly pulse = computed(() => this.pulseService.pulse());

	protected readonly view = computed<StatusView | null>(() => {
		const p = this.pulse();
		if (!p) return null;
		switch (derivePhase(p, new Date())) {
			case 'concluded': {
				// The standings link rides IN this card — the Game-Day panel is suppressed once
				// concluded (it had already shed its sub-line and both app-store columns there,
				// leaving a header and a lone button under a duplicate flag icon).
				const link = this.standingsLink();
				return {
					icon: 'bi-flag-fill',
					headline: 'This event has concluded',
					sub: 'Thanks for participating — hope to see you back next season!',
					cta: link
						? { label: 'View Final Standings', link, queryParams: { tab: 'standings' } }
						: undefined,
				};
			}
			default:
				return null; // every other phase — the panels own the page, or nothing does
		}
	});
}
