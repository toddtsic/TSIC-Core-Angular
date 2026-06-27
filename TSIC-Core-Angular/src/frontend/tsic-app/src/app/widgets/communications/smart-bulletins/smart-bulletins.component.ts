import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { CTAS_BY_PHASE, derivePhase } from '@shared/landing/landing-phase';
import { RegistrationPanelComponent } from '@views/home/job-landing/registration-panel/registration-panel.component';
import { GameDayPanelComponent } from '@views/home/job-landing/game-day-panel/game-day-panel.component';
import { InlineGameClockComponent } from '@views/scheduling/view-schedule/components/inline-game-clock.component';
import { EventStatusComponent } from './event-status.component';

/**
 * Smart Bulletins band — the self-assembling, always-current "bulletins" the
 * system writes from the live pulse, as opposed to the director's hand-authored
 * ones. Composes the Game-Day + Registration compound viewers (and a compact
 * Store card) into one ✨ SMART band.
 *
 * It lives INSIDE the `app-bulletins` widget, so every consumer of that widget —
 * the public job-landing AND the admin widget-dashboard — renders the same band.
 * That's the point: the dashboard shows the director exactly what the public sees.
 *
 * Unlike the old landing hero, the band is NOT admin-suppressed: it IS the public
 * preview, so it renders for everyone. The hand-authored bulletins keep their own
 * admin edit/inactivate overlay; the smart panels are read-only for all.
 *
 * Each section is gated on BOTH the lifecycle phase (CTAS_BY_PHASE) AND its pulse
 * flag, so a stale director toggle can't resurrect a CTA. Self-hides when empty.
 */
@Component({
	selector: 'app-smart-bulletins',
	standalone: true,
	imports: [RouterLink, RegistrationPanelComponent, GameDayPanelComponent, InlineGameClockComponent, EventStatusComponent],
	templateUrl: './smart-bulletins.component.html',
	styleUrl: './smart-bulletins.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SmartBulletinsComponent {
	/** Absolute, jobPath-prefixed base (the jobPath is in the path, so it's preserved). */
	readonly jobPath = input.required<string>();

	private readonly pulseService = inject(JobPulseService);
	private readonly auth = inject(AuthService);
	private readonly jobService = inject(JobService);

	private readonly pulse = computed(() => this.pulseService.pulse());
	private readonly base = computed(() => `/${this.jobPath()}`);

	// Director/SuperDirector/SuperUser preview the band as an anonymous visitor would
	// see it — the whole point of folding the smart band into the widget is that the
	// dashboard shows the director EXACTLY what the public sees. Without this the
	// per-user overlay (their regId/role/my* pulse fields) personalizes the band and
	// they'd see the inverse of the public (e.g. an empty Registration panel). Scoped
	// to isAdmin() = those three event-runner roles ONLY; a Club Rep / Staff keeps
	// their personalized hub. Flows into the Registration panel so it matches.
	protected readonly publicView = computed(() => this.auth.isAdmin());

	// Lifecycle phase from FACTS (shared with the landing's status line so the two
	// can't drift). allowedKeys is the phase's eligible-CTA set, passed to the
	// Registration panel so its sections gate on phase as well as pulse.
	private readonly phase = computed(() => derivePhase(this.pulse(), new Date()));
	protected readonly allowedKeys = computed<ReadonlySet<string>>(() => CTAS_BY_PHASE[this.phase()]);

	// Game-Day panel inputs, derived from pulse/phase.
	protected readonly jobId = computed(() => this.jobService.currentJob()?.jobId ?? '');
	protected readonly live = computed(() => this.phase() !== 'concluded');
	protected readonly storeLink = computed(() => `${this.base()}/store`);

	// ── Section gates (moved out of job-landing, MINUS the isAdmin early-returns) ──

	// THE isolated "schedule is live" signal — schedule published AND games actually
	// exist (firstGameDate non-null), in a phase where View Schedule belongs.
	protected readonly showGameDay = computed(() => {
		const p = this.pulse();
		if (!p || !this.jobPath()) return false;
		if (!(p.schedulePublished && p.firstGameDate)) return false;
		return this.allowedKeys().has('view-schedule');
	});

	// The inline game clock self-fetches and self-hides when nothing is active (and
	// on phones), so we just mount it whenever a live schedule is showing.
	protected readonly showClock = computed(() => this.showGameDay() && this.live());

	// Mount the Registration panel only on SUBSTANTIVE content. Mirrors the panel's
	// own selfRosterLinks + manageItems + rosters exactly, so the gate and the rendered
	// sections stay in lockstep — INCLUDING rosters, so when an event is over but rosters
	// are still public the panel mounts just to show that row (it can't orphan).
	protected readonly showRegistration = computed(() => {
		const p = this.pulse();
		if (!p || !this.jobPath()) return false;
		const allowed = this.allowedKeys();
		const user = this.auth.currentUser();
		// In public-preview mode the viewer is treated as anonymous: no regId, no
		// player/family role, and the my* overlay branches collapse — so this gate
		// stays in lockstep with the panel's own publicView short-circuit below.
		const pub = this.publicView();
		const registered = !pub && !!user?.regId;
		const isPlayerOrFamily = !pub && (user?.role === Roles.Player || user?.role === Roles.Family);

		const hasSelfRoster = !registered && (
			(allowed.has('register-player') && p.playerRegistrationOpen) ||
			(allowed.has('register-team') && p.teamRegistrationOpen) ||
			(allowed.has('register-coach') && p.staffRegistrationOpen) ||
			(allowed.has('register-referee') && p.refereeRegistrationOpen) ||
			(allowed.has('register-recruiter') && p.recruiterRegistrationOpen));
		const hasManage =
			(allowed.has('register-player') && p.playerRegistrationOpen) ||  // self-roster-update
			(registered && isPlayerOrFamily && (
				allowed.has('my-registration') ||
				(allowed.has('pay-balance') && (p.myRegistrationOwedTotal ?? 0) > 0) ||
				(allowed.has('player-insurance') && p.offerPlayerRegsaverInsurance && p.myHasPurchasedPlayerRegsaver !== true))) ||
			(!pub && (p.myClubRepTeamCount ?? 0) > 0 && (
				allowed.has('my-teams') ||
				(p.myClubRepTotalOwed ?? 0) > 0 ||
				(allowed.has('team-insurance') && p.offerTeamRegsaverInsurance && p.myClubRepHasTeamWithoutRegsaver === true)));
		const hasRosters = !!p.publicRostersAvailable && allowed.has('rosters');
		return hasSelfRoster || hasManage || hasRosters;
	});

	protected readonly showStore = computed(() => {
		const p = this.pulse();
		return !!p?.storeHasActiveItems && this.allowedKeys().has('store');
	});

	// NB: Public Rosters is no longer a standalone band card — it's a section INSIDE the
	// Registration panel again (showRosters there). The showRegistration gate above counts
	// rosters, so the panel still mounts (and rosters still shows) when nothing else does.

	// Event Status fills the lifecycle "dead zones" the action panels leave bare —
	// registration not open yet, nothing/closed, or finished. The component self-hides
	// in the action phases; this gate mirrors it for hasContent.
	protected readonly showEventStatus = computed(() => {
		const phase = this.phase();
		return phase === 'planned' || phase === 'preview' || phase === 'concluded';
	});

	/** The band self-hides when no smart section has content. */
	protected readonly hasContent = computed(() =>
		this.showEventStatus() || this.showRegistration() || this.showGameDay() || this.showStore());
}
