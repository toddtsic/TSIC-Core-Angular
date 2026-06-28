import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { QuickLinksService } from './services/quick-links.service';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { isTournament, isLeague } from '@infrastructure/constants/job-type.constants';
import type { JobVisibilityDto, UpdateJobVisibilityRequest } from '@core/api';

/** The flag keys shared by JobVisibilityDto (read) and UpdateJobVisibilityRequest (write). */
type FlagKey = keyof UpdateJobVisibilityRequest;

interface ToggleDef {
	key: FlagKey;
	label: string;
	icon: string;
	onTip: string;
	offTip: string;
	/** Non-null = this toggle can't be configured for this job (wrong job type, or its
	 *  fees aren't set up). We DISABLE it and show this reason rather than hide it, so the
	 *  control is always discoverable AND its stored flag value stays visible for debugging. */
	unavailable: string | null;
	/** SuperUser-only flag (insurance/store). Rendered disabled + "SuperUser only"
	 *  for non-super admins; the backend also ignores it on write for them. */
	superUserOnly?: boolean;
	/** Optional fact-derived caution (e.g. releasing coach reg with no teams). Shown
	 *  when the toggle is ON. Non-forcing — the director can still leave it on. */
	warn?: string | null;
}

/**
 * SuperUser "Quick Links" editor — a focused, job-scoped editor for the public
 * landing-hero CTA toggles of the CURRENT job. Each switch saves on change
 * (one partial PUT per toggle). These flags also live in their logical Configure
 * Job tabs; this is a one-stop convenience surface, not a separate source of truth.
 */
@Component({
	selector: 'app-quick-links',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './quick-links.component.html',
	styleUrl: './quick-links.component.scss',
})
export class QuickLinksComponent {
	private readonly svc = inject(QuickLinksService);
	private readonly toast = inject(ToastService);
	private readonly jobService = inject(JobService);
	private readonly auth = inject(AuthService);

	readonly isSuperUser = computed(() => this.auth.isSuperuser());

	readonly flags = signal<JobVisibilityDto | null>(null);
	readonly isLoading = signal(true);
	readonly loadError = signal(false);
	/** The flag currently being saved (drives the per-row spinner); null when idle. */
	readonly savingKey = signal<FlagKey | null>(null);

	/** Accordion: not-applicable (disabled) toggles are collapsed by default so the
	 *  default view shows only the toggles that apply to this job — the eventual go-live
	 *  view, where the applicable/enabled toggles stay together. Expand to inspect the rest. */
	readonly showUnavailable = signal(false);
	toggleUnavailable(): void { this.showUnavailable.update(v => !v); }

	readonly ready = computed(() => !this.isLoading() && !this.loadError() && this.flags() !== null);

	// Tournament jobs reframe player registration as "self-rostering" (the team is
	// the registering entity; a player joins one). Centralized via isTournament so
	// this never drifts from the public hero card.
	//
	// Toggles are NEVER hidden — a toggle that can't be configured for this job (wrong
	// job type, or its fees aren't set up) is shown DISABLED with a reason (`unavailable`).
	// Rationale: hiding a control while its underlying flag may be ON is a debugging
	// blind spot (e.g. a player site with BRegistrationAllowPlayer=1 but no Player fees
	// would show no Player toggle at all). Disable-not-hide keeps every control and its
	// stored value visible. Relevance gates: player ↔ Player fees, team ↔ ClubRep fees.
	readonly toggles = computed<ToggleDef[]>(() => {
		const f = this.flags();
		if (!f) return [];
		const jobTypeId = this.jobService.currentJob()?.jobTypeId;
		const tournament = isTournament(jobTypeId);
		const league = isLeague(jobTypeId);
		const competitive = tournament || league;
		// Shared "unavailable" reasons for the job-type gates.
		const competitiveOnly = competitive ? null : 'Only applies to tournament or league events.';
		const tournamentOnly = tournament ? null : 'Only applies to tournament events.';
		// The toggle DEFINITIONS (copy + the `unavailable` reason, null when configurable).
		// Physical order here is irrelevant — the display order is applied by ORDER below.
		const defs = ([
		// Tournaments register teams; LEAGUES DO NOT (players self-roster onto a league's
		// pre-built teams). Team reg also needs ClubRep fees so it can be priced.
		{ key: 'allowTeamRegistration', label: 'Register Team', icon: 'bi-people',
			unavailable: league
				? 'Leagues don\'t register teams — players self-roster onto pre-built teams.'
				: !f.teamFeesConfigured
					? 'Team (Club Rep) fees aren\'t configured — set them in Job Fees to enable.'
					: null,
			onTip: 'Teams can register — the "Register Team" card shows on the landing page.',
			offTip: 'Team registration is closed — the card is hidden.' },
		{ key: 'allowPlayerRegistration',
			label: tournament ? 'Allow Player Self-Rostering' : 'Allow Player Registration',
			icon: 'bi-person-plus',
			unavailable: f.playerFeesConfigured
				? null
				: 'Player fees aren\'t configured — set them in Job Fees to enable.',
			onTip: tournament
				? 'Players can self-roster onto a team — the "Self-Roster Player" card shows on the landing page.'
				: 'Players can register — the "Register Player" card shows on the landing page.',
			offTip: tournament
				? 'Self-rostering is closed — the card is hidden.'
				: 'Player registration is closed — the card is hidden.' },
		// Adult registration releases. Coach is team-relevant: a coach requests a team,
		// so releasing it with no teams configured surfaces a non-forcing caution (the
		// hero card also stays hidden until teams exist — pulse gates on teams-exist).
		{ key: 'allowStaffRegistration',
			label: tournament ? 'Allow Coach Registration' : 'Allow Coach/Staff Registration',
			icon: 'bi-person-badge',
			unavailable: null,
			warn: f.teamsConfigured
				? null
				: 'No teams exist yet — coaches will have nothing to request, and the "Register Coach" card stays hidden until teams are added.',
			onTip: 'Coaches can register and request teams — the "Register Coach" card shows on the landing page.',
			offTip: 'Coach registration is closed — the card is hidden.' },
		// Public rosters are a tournament-only concept (self-rostering).
		{ key: 'showPublicRosters', label: 'Rosters', icon: 'bi-list-ul',
			unavailable: tournamentOnly,
			onTip: 'Public player rosters are visible — the "Rosters" card shows.',
			offTip: 'Public rosters are hidden — the card is hidden.' },
		// Schedule-relevant: publishing access is a legitimate pre-arm action, so the
		// toggle stays usable, but with no games entered the public "View Schedule"
		// card stays hidden (pulse gates on FirstGameDate) — surface that as a
		// non-forcing caution. A game schedule is a competitive-event concept.
		{ key: 'publishSchedule', label: 'View Schedule', icon: 'bi-calendar-event',
			unavailable: competitiveOnly,
			warn: f.scheduleConfigured
				? null
				: 'No games are scheduled yet — the "View Schedule" card stays hidden until a schedule is added.',
			onTip: 'The public schedule is visible — the "View Schedule" card shows.',
			offTip: 'The schedule is not public — the card is hidden.' },
		{ key: 'offerPlayerInsurance', label: 'Player RegSaver', icon: 'bi-shield-check',
			unavailable: null, superUserOnly: true,
			onTip: 'RegSaver insurance is offered to players — the "Player RegSaver" card shows.',
			offTip: 'Player RegSaver is not offered — the card is hidden.' },
		// Club-rep pathway, mirrors the player toggle above (the card itself suppresses
		// once every team is already covered). Tournament-OR-league only.
		{ key: 'offerTeamInsurance', label: 'Team RegSaver', icon: 'bi-shield-check',
			unavailable: competitiveOnly, superUserOnly: true,
			onTip: 'RegSaver insurance is offered to club reps — the "Team RegSaver" card shows.',
			offTip: 'Team RegSaver is not offered — the card is hidden.' },
		// College recruiters scout at competitive events — tournament OR league only.
		{ key: 'allowRecruiterRegistration', label: 'Allow College Recruiter Registration', icon: 'bi-mortarboard',
			unavailable: competitiveOnly,
			onTip: 'College recruiters can register — the "Register College Recruiter" card shows.',
			offTip: 'Recruiter registration is closed — the card is hidden.' },
		// Referees officiate at competitive events — tournament OR league only.
		{ key: 'allowRefereeRegistration', label: 'Allow Referee Registration', icon: 'bi-flag',
			unavailable: competitiveOnly,
			onTip: 'Referees can register — the "Register Referee" card shows on the landing page.',
			offTip: 'Referee registration is closed — the card is hidden.' },
		{ key: 'enableStore', label: 'Store', icon: 'bi-bag',
			unavailable: null, superUserOnly: true,
			onTip: 'The store is enabled — the "Store" card shows once it has active items.',
			offTip: 'The store is disabled — the card is hidden.' },
		] as ToggleDef[]);

		// Display order is JOB-TYPE-SCOPED. Competitive events (tournament/league) use
		// Ann's combined ordering. Every OTHER job type (club/camp/sales) keeps its prior
		// ordering. Both arrays list ALL keys, so there is no indexOf -1. Nothing is
		// filtered out anymore — unavailable toggles sort in place and render disabled.
		const COMPETITIVE_ORDER: FlagKey[] = [
			'allowTeamRegistration', 'allowPlayerRegistration', 'allowStaffRegistration',
			'showPublicRosters', 'publishSchedule', 'offerPlayerInsurance',
			'offerTeamInsurance', 'allowRecruiterRegistration', 'allowRefereeRegistration',
			'enableStore',
		];
		const DEFAULT_ORDER: FlagKey[] = [
			'allowPlayerRegistration', 'allowTeamRegistration', 'allowStaffRegistration',
			'allowRefereeRegistration', 'allowRecruiterRegistration', 'offerPlayerInsurance',
			'offerTeamInsurance', 'enableStore', 'publishSchedule', 'showPublicRosters',
		];
		const order = competitive ? COMPETITIVE_ORDER : DEFAULT_ORDER;

		return defs.sort((a, b) => order.indexOf(a.key) - order.indexOf(b.key));
	});

	// Split for the template: applicable toggles render at the top (the go-live view);
	// not-applicable ones (disabled, with a reason) collapse into an accordion below.
	readonly availableToggles = computed(() => this.toggles().filter(t => !t.unavailable));
	readonly unavailableToggles = computed(() => this.toggles().filter(t => !!t.unavailable));

	constructor() {
		this.svc.get().subscribe({
			next: f => { this.flags.set(f); this.isLoading.set(false); },
			error: () => { this.loadError.set(true); this.isLoading.set(false); },
		});
	}

	isOn(key: FlagKey): boolean {
		return !!this.flags()?.[key as keyof JobVisibilityDto];
	}

	/** Optimistically flip the flag, persist just that one, revert on failure. */
	onToggle(key: FlagKey, value: boolean): void {
		const prev = this.flags();
		if (!prev) return;

		this.flags.set({ ...prev, [key]: value });
		this.savingKey.set(key);

		const patch: UpdateJobVisibilityRequest = {};
		patch[key] = value;          // partial — only the toggled flag is sent
		this.svc.save(patch).subscribe({
			next: () => this.savingKey.set(null),
			error: () => {
				this.flags.set({ ...prev });           // revert to the pre-toggle truth
				this.savingKey.set(null);
				this.toast.show('Could not save — change reverted', 'danger');
			},
		});
	}
}
