import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { JobCloneService } from './services/job-clone.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { ToastService } from '@shared-ui/toast.service';
import { environment } from '@environments/environment';
import type {
	DevUndoStatusResponse,
	JobVerifyChecklistDto,
	RegistrationFlagsDto,
	ReleasableAdminDto,
} from '@core/api';

type PersonaKey = 'player' | 'team' | 'staff' | 'referee' | 'recruiter';

interface PersonaRow {
	readonly key: PersonaKey;
	readonly label: string;
}

/**
 * Verify-then-release (T9-C, the modern JobCloneQA). Works from the URL alone
 * (release/:jobId) so it stays reachable from the landing's Unreleased list. Four
 * panels, in order:
 *   1. Verify settings — the job's LIVE values, type-ordered sections, configure deep-links
 *   2. Release site   — flip BSuspendPublic off
 *   3. Release admins — activate director registrations
 *   4. Open registration — flip the chosen BRegistrationAllow* flags ON (all five start
 *      false on every clone; this is the deliberate opening)
 * Plus the sandbox-only "delete this clone" undo.
 */
@Component({
	selector: 'app-job-release',
	standalone: true,
	imports: [CommonModule, FormsModule, RouterLink],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './job-release.component.html',
	styleUrls: ['./job-clone-shared.scss', './job-release.component.scss'],
})
export class JobReleaseComponent implements OnInit {
	private readonly cloneService = inject(JobCloneService);
	private readonly route = inject(ActivatedRoute);
	private readonly router = inject(Router);
	private readonly authService = inject(AuthService);
	private readonly toast = inject(ToastService);
	private readonly jobContext = inject(JobContextService);
	private readonly destroyRef = inject(DestroyRef);

	readonly jobId = this.route.snapshot.paramMap.get('jobId') ?? '';

	// Post-clone extras carried via navigation state — celebration flair and the dev-undo
	// return trip. Optional by design: the page is fully functional without them.
	readonly celebrate = signal(false);
	private sourceJobPathForReturn: string | null = null;
	private sourceRegIdForReturn: string | null = null;

	readonly checklist = signal<JobVerifyChecklistDto | null>(null);
	readonly isLoadingChecklist = signal(true);

	readonly admins = signal<ReleasableAdminDto[]>([]);
	readonly selectedRegIds = signal<Set<string>>(new Set());
	readonly isLoadingAdmins = signal(false);
	readonly isReleasingSite = signal(false);
	readonly isActivatingAdmins = signal(false);
	readonly isOpeningRegistration = signal(false);

	readonly flags = signal<RegistrationFlagsDto | null>(null);
	readonly personaChecks = signal<Record<PersonaKey, boolean>>({
		player: false, team: false, staff: false, referee: false, recruiter: false,
	});

	// ── Sandbox-only undo ──
	// Mirrors the server's IHostEnvironment.IsSandbox() — everything that is NOT
	// Production. It must NOT key off environment.production: that flag is an Angular
	// build-optimization switch and is true for staging, which would have hidden this
	// panel on the one deployed box where the undo is meant to be used. The server
	// 404s both dev-undo endpoints in Production regardless of what the UI shows.
	readonly isSandbox = environment.envName !== 'production';
	readonly devUndoStatus = signal<DevUndoStatusResponse | null>(null);
	readonly devUndoConfirmOpen = signal(false);
	readonly isDeletingClone = signal(false);

	readonly inactiveAdmins = computed(() => this.admins().filter(a => !a.bActive));
	readonly activeAdmins = computed(() => this.admins().filter(a => a.bActive));
	readonly selectedCount = computed(() => this.selectedRegIds().size);

	/** Persona emphasis is type-aware: team-first for tournament/showcase types. */
	readonly personas = computed<PersonaRow[]>(() => {
		const teamFirst = [2, 6].includes(this.checklist()?.jobTypeId ?? 0);
		const rows: PersonaRow[] = [
			{ key: 'player', label: 'Players' },
			{ key: 'team', label: 'Teams (club reps)' },
			{ key: 'staff', label: 'Staff' },
			{ key: 'referee', label: 'Referees' },
			{ key: 'recruiter', label: 'Recruiters' },
		];
		return teamFirst ? [rows[1], rows[0], ...rows.slice(2)] : rows;
	});

	readonly anyPersonaToOpen = computed(() => {
		const f = this.flags();
		const checks = this.personaChecks();
		if (!f) return Object.values(checks).some(Boolean);
		return (checks.player && !f.allowPlayer) || (checks.team && !f.allowTeam)
			|| (checks.staff && !f.allowStaff) || (checks.referee && !f.allowReferee)
			|| (checks.recruiter && !f.allowRecruiter);
	});

	ngOnInit(): void {
		const navState = (typeof history !== 'undefined' ? history.state : null) as
			| { celebrate?: boolean; sourceJobPath?: string; sourceRegId?: string }
			| null;
		if (navState?.celebrate) this.celebrate.set(true);
		this.sourceJobPathForReturn = navState?.sourceJobPath ?? null;
		this.sourceRegIdForReturn = navState?.sourceRegId ?? null;

		if (!this.jobId) {
			this.toast.show('Missing job id', 'danger', 4000);
			return;
		}
		// This page is about the job that was just created; the session is still signed
		// into the job it was cloned FROM, so the chrome would name — and link to — the
		// wrong one. Blank it while we're here; the page header carries the identity.
		this.jobContext.suppressChromeIdentity(true);
		this.destroyRef.onDestroy(() => this.jobContext.suppressChromeIdentity(false));

		this.loadChecklist();
		this.loadAdmins();
		if (this.isSandbox) this.loadDevUndoStatus();
	}

	// ══════════════════════════════════════════════════════════
	// Loads
	// ══════════════════════════════════════════════════════════

	private loadChecklist(): void {
		this.isLoadingChecklist.set(true);
		this.cloneService.getVerifyChecklist(this.jobId).subscribe({
			next: checklist => {
				this.checklist.set(checklist);
				this.flags.set(checklist.registrationFlags);
				// Seed the persona checkboxes with what's already open — the button only
				// fires for newly-checked personas (open-registration is additive).
				this.personaChecks.set({
					player: checklist.registrationFlags.allowPlayer,
					team: checklist.registrationFlags.allowTeam,
					staff: checklist.registrationFlags.allowStaff,
					referee: checklist.registrationFlags.allowReferee,
					recruiter: checklist.registrationFlags.allowRecruiter,
				});
				this.isLoadingChecklist.set(false);
			},
			error: err => {
				this.isLoadingChecklist.set(false);
				this.toast.show(err.error?.message ?? 'Failed to load verify checklist', 'danger', 5000);
			},
		});
	}

	private loadAdmins(): void {
		this.isLoadingAdmins.set(true);
		this.cloneService.getAdmins(this.jobId).subscribe({
			next: admins => {
				this.admins.set(admins);
				this.isLoadingAdmins.set(false);
			},
			error: () => {
				this.isLoadingAdmins.set(false);
				this.toast.show('Failed to load admins', 'danger', 4000);
			},
		});
	}

	private loadDevUndoStatus(): void {
		this.cloneService.getDevUndoStatus(this.jobId).subscribe({
			next: status => this.devUndoStatus.set(status),
			error: () => { /* sandbox-only endpoint; silent when unavailable */ },
		});
	}

	// ══════════════════════════════════════════════════════════
	// Panel 1 — verify deep-links
	// ══════════════════════════════════════════════════════════

	configureLink(routePath: string): string[] {
		const jobPath = this.checklist()?.jobPath ?? '';
		return ['/', jobPath, ...routePath.split('/')];
	}

	// ══════════════════════════════════════════════════════════
	// Panel 2 — release site
	// ══════════════════════════════════════════════════════════

	onReleaseSite(): void {
		this.isReleasingSite.set(true);
		this.cloneService.releaseSite(this.jobId).subscribe({
			next: () => {
				this.isReleasingSite.set(false);
				this.toast.show('Job is now visible to the public', 'success');
				this.loadChecklist();
			},
			error: err => {
				this.isReleasingSite.set(false);
				this.toast.show(err.error?.message ?? 'Release failed', 'danger', 4000);
			},
		});
	}

	// ══════════════════════════════════════════════════════════
	// Panel 3 — admins
	// ══════════════════════════════════════════════════════════

	toggleAdminSelect(regId: string): void {
		const current = new Set(this.selectedRegIds());
		if (current.has(regId)) current.delete(regId);
		else current.add(regId);
		this.selectedRegIds.set(current);
	}

	selectAllInactive(): void {
		this.selectedRegIds.set(new Set(this.inactiveAdmins().map(a => a.registrationId)));
	}

	clearAdminSelection(): void {
		this.selectedRegIds.set(new Set());
	}

	onActivateSelected(): void {
		const ids = [...this.selectedRegIds()];
		if (ids.length === 0) return;
		this.isActivatingAdmins.set(true);
		this.cloneService.releaseAdmins(this.jobId, { registrationIds: ids }).subscribe({
			next: response => {
				this.isActivatingAdmins.set(false);
				const n = response.adminsActivated;
				this.toast.show(`${n} director${n === 1 ? '' : 's'} can now log in`, 'success');
				this.selectedRegIds.set(new Set());
				this.loadAdmins();
				this.loadChecklist();
			},
			error: err => {
				this.isActivatingAdmins.set(false);
				this.toast.show(err.error?.message ?? 'Activate failed', 'danger', 4000);
			},
		});
	}

	// ══════════════════════════════════════════════════════════
	// Panel 4 — open registration
	// ══════════════════════════════════════════════════════════

	setPersona(key: PersonaKey, value: boolean): void {
		// Already-open personas stay checked — closing lives in job settings, not here.
		const f = this.flags();
		const alreadyOpen = f && {
			player: f.allowPlayer, team: f.allowTeam, staff: f.allowStaff,
			referee: f.allowReferee, recruiter: f.allowRecruiter,
		}[key];
		if (alreadyOpen && !value) return;
		this.personaChecks.set({ ...this.personaChecks(), [key]: value });
	}

	isPersonaOpen(key: PersonaKey): boolean {
		const f = this.flags();
		if (!f) return false;
		return { player: f.allowPlayer, team: f.allowTeam, staff: f.allowStaff,
			referee: f.allowReferee, recruiter: f.allowRecruiter }[key];
	}

	onOpenRegistration(): void {
		if (!this.anyPersonaToOpen()) return;
		const checks = this.personaChecks();
		this.isOpeningRegistration.set(true);
		this.cloneService.openRegistration(this.jobId, {
			openPlayer: checks.player,
			openTeam: checks.team,
			openStaff: checks.staff,
			openReferee: checks.referee,
			openRecruiter: checks.recruiter,
		}).subscribe({
			next: flags => {
				this.isOpeningRegistration.set(false);
				this.flags.set(flags);
				this.toast.show('Registration opened', 'success');
				this.loadChecklist();
			},
			error: err => {
				this.isOpeningRegistration.set(false);
				this.toast.show(err.error?.message ?? 'Open registration failed', 'danger', 4000);
			},
		});
	}

	// ══════════════════════════════════════════════════════════
	// Sandbox-only undo
	// ══════════════════════════════════════════════════════════

	openDevUndoConfirm(): void { this.devUndoConfirmOpen.set(true); }
	cancelDevUndoConfirm(): void { this.devUndoConfirmOpen.set(false); }

	confirmDevUndo(): void {
		this.isDeletingClone.set(true);
		this.cloneService.deleteClonedJob(this.jobId).subscribe({
			next: () => {
				this.isDeletingClone.set(false);
				this.devUndoConfirmOpen.set(false);
				const sourcePath = this.sourceJobPathForReturn;
				const sourceRegId = this.sourceRegIdForReturn;
				if (sourcePath && sourceRegId) {
					// Return trip: re-mint the JWT back into the source job's registration.
					this.authService.selectRegistration(sourceRegId).subscribe({
						next: () => {
							this.toast.show('Cloned job deleted; back in source.', 'success');
							this.router.navigate(['/', sourcePath, 'configure', 'job-clone']);
						},
						error: () => {
							this.toast.show('Cloned job deleted; could not switch back automatically.', 'warning', 5000);
							this.router.navigate(['/']);
						},
					});
				} else {
					// No return context (page opened from URL alone) — the current JWT may be
					// scoped to the job that just vanished; send the user to re-select.
					this.toast.show('Cloned job deleted. Select a job to continue.', 'success', 5000);
					this.router.navigate(['/']);
				}
			},
			error: err => {
				this.isDeletingClone.set(false);
				this.toast.show(err.error?.message ?? 'Delete failed.', 'danger', 5000);
			},
		});
	}

	// ══════════════════════════════════════════════════════════
	// Template helpers
	// ══════════════════════════════════════════════════════════

	trackAdmin = (_: number, a: ReleasableAdminDto) => a.registrationId;
}
