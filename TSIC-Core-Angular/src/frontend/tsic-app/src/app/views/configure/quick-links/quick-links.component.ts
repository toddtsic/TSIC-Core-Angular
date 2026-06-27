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
	/** When false the toggle is irrelevant to this job and is omitted entirely. */
	relevant: boolean;
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

	readonly ready = computed(() => !this.isLoading() && !this.loadError() && this.flags() !== null);

	// Tournament jobs reframe player registration as "self-rostering" (the team is
	// the registering entity; a player joins one). Centralized via isTournament so
	// this never drifts from the public hero card.
	//
	// Registration toggles are fee-relevance-gated: player ↔ Player fees,
	// team ↔ ClubRep fees. A reg type with no fees configured can't be priced, so
	// it's irrelevant to this job and omitted — mirrors the public hero's gating.
	readonly toggles = computed<ToggleDef[]>(() => {
		const f = this.flags();
		if (!f) return [];
		const jobTypeId = this.jobService.currentJob()?.jobTypeId;
		const tournament = isTournament(jobTypeId);
		const league = isLeague(jobTypeId);
		const competitive = tournament || league;
		// The toggle DEFINITIONS (relevance + copy). Physical order here is irrelevant —
		// the display order is applied by ORDER below, chosen per job type, then the
		// non-relevant ones are filtered out.
		const defs = ([
		// Tournaments register teams; LEAGUES DO NOT (players self-roster onto a league's
		// pre-built teams). So exclude leagues even when team fees happen to be configured.
		{ key: 'allowTeamRegistration', label: 'Register Team', icon: 'bi-people',
			relevant: f.teamFeesConfigured && !league,
			onTip: 'Teams can register — the "Register Team" card shows on the landing page.',
			offTip: 'Team registration is closed — the card is hidden.' },
		{ key: 'allowPlayerRegistration',
			label: tournament ? 'Allow Player Self-Rostering' : 'Allow Player Registration',
			icon: 'bi-person-plus',
			relevant: f.playerFeesConfigured,
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
			relevant: true,
			warn: f.teamsConfigured
				? null
				: 'No teams exist yet — coaches will have nothing to request, and the "Register Coach" card stays hidden until teams are added.',
			onTip: 'Coaches can register and request teams — the "Register Coach" card shows on the landing page.',
			offTip: 'Coach registration is closed — the card is hidden.' },
		// Public rosters are a tournament-only concept (self-rostering) — omit the toggle
		// on leagues/clubs/camps entirely, mirroring the public panel's tournament gate.
		{ key: 'showPublicRosters', label: 'Rosters', icon: 'bi-list-ul',
			relevant: tournament,
			onTip: 'Public player rosters are visible — the "Rosters" card shows.',
			offTip: 'Public rosters are hidden — the card is hidden.' },
		// Schedule-relevant: publishing access is a legitimate pre-arm action, so the
		// toggle stays usable, but with no games entered the public "View Schedule"
		// card stays hidden (pulse gates on FirstGameDate) — surface that as a
		// non-forcing caution, mirroring the coach/no-teams pattern above.
		// A game schedule is a competitive-event concept — tournament OR league only.
		{ key: 'publishSchedule', label: 'View Schedule', icon: 'bi-calendar-event',
			relevant: tournament || league,
			warn: f.scheduleConfigured
				? null
				: 'No games are scheduled yet — the "View Schedule" card stays hidden until a schedule is added.',
			onTip: 'The public schedule is visible — the "View Schedule" card shows.',
			offTip: 'The schedule is not public — the card is hidden.' },
		{ key: 'offerPlayerInsurance', label: 'Player RegSaver', icon: 'bi-shield-check',
			relevant: true, superUserOnly: true,
			onTip: 'RegSaver insurance is offered to players — the "Player RegSaver" card shows.',
			offTip: 'Player RegSaver is not offered — the card is hidden.' },
		// Club-rep pathway, mirrors the player toggle above (the card itself suppresses
		// once every team is already covered). Tournament-OR-league only — the
		// competitive settings where club reps register teams.
		{ key: 'offerTeamInsurance', label: 'Team RegSaver', icon: 'bi-shield-check',
			relevant: tournament || league, superUserOnly: true,
			onTip: 'RegSaver insurance is offered to club reps — the "Team RegSaver" card shows.',
			offTip: 'Team RegSaver is not offered — the card is hidden.' },
		// College recruiters scout at competitive events — tournament OR league only.
		{ key: 'allowRecruiterRegistration', label: 'Allow College Recruiter Registration', icon: 'bi-mortarboard',
			relevant: tournament || league,
			onTip: 'College recruiters can register — the "Register College Recruiter" card shows.',
			offTip: 'Recruiter registration is closed — the card is hidden.' },
		// Referees officiate at competitive events — tournament OR league only.
		{ key: 'allowRefereeRegistration', label: 'Allow Referee Registration', icon: 'bi-flag',
			relevant: tournament || league,
			onTip: 'Referees can register — the "Register Referee" card shows on the landing page.',
			offTip: 'Referee registration is closed — the card is hidden.' },
		{ key: 'enableStore', label: 'Store', icon: 'bi-bag',
			relevant: true, superUserOnly: true,
			onTip: 'The store is enabled — the "Store" card shows once it has active items.',
			offTip: 'The store is disabled — the card is hidden.' },
		] as ToggleDef[]);

		// Display order is JOB-TYPE-SCOPED. Competitive events (tournament/league) use
		// Ann's combined ordering. Every OTHER job type (club/camp/sales) keeps its prior
		// ordering — this combined order must NOT perturb non-competitive layout. Keys not
		// present in a list sort to the front (indexOf -1), but the relevant-filter has
		// already dropped the competitive-only toggles for those types, so it never bites.
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

		return defs.filter(t => t.relevant)
			.sort((a, b) => order.indexOf(a.key) - order.indexOf(b.key));
	});

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
