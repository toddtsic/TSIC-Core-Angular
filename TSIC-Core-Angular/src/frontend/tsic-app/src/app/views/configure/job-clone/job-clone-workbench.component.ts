import {
	ChangeDetectionStrategy, Component, DestroyRef, OnInit, WritableSignal,
	computed, inject, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, catchError, debounceTime, switchMap, tap } from 'rxjs';
import { JobCloneService } from './services/job-clone.service';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import type {
	ClonePlanDto,
	JobCloneRequest,
	JobCloneSourceDto,
	JobConfigReferenceDataDto,
} from '@core/api';

interface LeagueRenameRow {
	readonly sourceLeagueId: string;
	readonly sourceName: string;
	readonly nameTarget: string;
}

/** Friendly labels for the plan pane's step list (keys = JobCloneStepOrder). */
const STEP_LABELS: Record<string, string> = {
	Job: 'Job',
	DisplayOptions: 'Display options',
	OwlImages: 'Carousel images',
	Bulletins: 'Bulletins',
	AgeRanges: 'Age ranges',
	Menus: 'Menus',
	JobReports: 'Reports',
	Nav: 'Nav overrides',
	AdminRegistrations: 'Admin registrations',
	Leagues: 'Leagues',
	JobLeagues: 'League links',
	Agegroups: 'Agegroups',
	Divisions: 'Divisions',
	Teams: 'Teams',
	JobFees: 'Fee rows',
	FeeModifiers: 'Fee modifiers',
};

/**
 * Single-screen clone WORKBENCH (replaces the 7-step wizard): every input visible at
 * once on the left, the server-computed plan rendered live on the right. The plan pane
 * refetches on a debounced stream of form edits; the Clone button is enabled only when
 * the displayed plan matches the current inputs (freshness is a computed over the same
 * request snapshot) — the stale-preview and step-bypass defect classes are structurally
 * impossible here.
 *
 * The owning customer is a first-class input, seeded from the source. Pointing it at a
 * different customer is the new-customer onboarding path (it replaced a separate blank-job
 * flow): the plan comes back flagged cross-customer with its own warnings, and the server
 * withholds the source's admin registrations.
 */
@Component({
	selector: 'app-job-clone-workbench',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './job-clone-workbench.component.html',
	styleUrls: ['./job-clone-shared.scss', './job-clone-workbench.component.scss'],
})
export class JobCloneWorkbenchComponent implements OnInit {
	private readonly cloneService = inject(JobCloneService);
	private readonly jobContext = inject(JobContextService);
	private readonly route = inject(ActivatedRoute);
	private readonly router = inject(Router);
	private readonly authService = inject(AuthService);
	private readonly toast = inject(ToastService);
	private readonly destroyRef = inject(DestroyRef);

	// ── Source ──
	readonly source = signal<JobCloneSourceDto | null>(null);
	readonly isLoadingSource = signal(true);

	// ── Reference data (customer picker) ──
	readonly referenceData = signal<JobConfigReferenceDataDto | null>(null);

	// ── Identity ──
	readonly jobPathTarget = signal('');
	readonly jobNameTarget = signal('');
	readonly yearTarget = signal('');
	readonly seasonTarget = signal('');
	readonly displayName = signal('');
	readonly leagueRenames = signal<LeagueRenameRow[]>([]);

	/**
	 * Owning customer of the NEW job — seeded from the source, changeable. This is the
	 * merchant account that will collect on the job, so it is a form field rather than an
	 * inherited value.
	 */
	readonly customerId = signal('');

	// ── Dates ──
	readonly expiryAdmin = signal('');
	readonly expiryUsers = signal('');

	// ── Communications (8 params, seeded from the first plan response) ──
	readonly regFormFrom = signal('');
	readonly regFormCcs = signal('');
	readonly regFormBccs = signal('');
	readonly rescheduleemaillist = signal('');
	readonly alwayscopyemaillist = signal('');
	readonly mailTo = signal('');
	readonly payTo = signal('');
	readonly storeContactEmail = signal('');

	// ── Scope & options ──
	readonly ladtScope = signal<'none' | 'lad' | 'ladt'>('lad');
	/** false = clone the shape but not last year's pools (every agegroup still gets Unassigned). */
	readonly copyDivisions = signal(true);
	readonly upAgegroupNamesByOne = signal(true);
	readonly noParallaxSlide1 = signal(false);
	readonly enableEcheckChoice = signal<'off' | 'source'>('off');
	readonly storeChoice = signal<'keep' | 'disable'>('disable');

	// ── Commit controls ──
	readonly affirmationChecked = signal(false);
	readonly enterNewJob = signal(true);
	readonly isSubmitting = signal(false);
	readonly error = signal<string | null>(null);

	// ── Identity collision (inline blur check) ──
	readonly pathCollision = signal(false);
	readonly nameCollision = signal(false);

	// ── Live plan ──
	readonly plan = signal<ClonePlanDto | null>(null);
	readonly planRefreshing = signal(false);
	/** Request snapshot the displayed plan was computed FROM. */
	private readonly planKey = signal<string | null>(null);
	private readonly planTrigger$ = new Subject<void>();
	private commSeeded = false;
	private typeDefaultsSeeded = false;

	/** Snapshot of the current inputs — recomputes on any form-signal change. */
	readonly requestKey = computed(() => JSON.stringify(this.buildCloneRequest(null)));
	readonly planFresh = computed(() => this.planKey() !== null && this.planKey() === this.requestKey());

	private static readonly SlugPattern = /^[A-Za-z0-9][A-Za-z0-9-]*$/;

	/** Type-aware LadtScope default (T9-A): tournament/showcase carry structure teams. */
	private static readonly ScopeByJobType: Record<number, 'none' | 'lad' | 'ladt'> = {
		1: 'lad',   // Club Sport Registration
		2: 'ladt',  // Tournament Scheduling
		3: 'lad',   // League Scheduling
		4: 'none',  // Camp Registration
		5: 'lad',   // Sales Venue
		6: 'ladt',  // Showcase Registration
	};

	readonly slugValid = computed(() =>
		JobCloneWorkbenchComponent.SlugPattern.test(this.jobPathTarget()) && this.jobPathTarget().length <= 80);

	/** True once the operator points the job at a customer other than the source's. */
	readonly isCrossCustomer = computed(() => {
		const src = this.source();
		return !!src && !!this.customerId() && this.customerId() !== src.customerId;
	});

	readonly formValid = computed(() => {
		if (!this.jobPathTarget() || !this.slugValid()) return false;
		if (!this.jobNameTarget() || !this.yearTarget() || !this.seasonTarget() || !this.displayName()) return false;
		if (!this.expiryAdmin() || !this.expiryUsers()) return false;
		if (this.pathCollision() || this.nameCollision()) return false;
		if (!this.source() || !this.customerId()) return false;
		if (this.ladtScope() !== 'none' && this.leagueRenames().some(r => !r.nameTarget.trim())) return false;
		return true;
	});

	readonly canClone = computed(() =>
		this.formValid() && this.affirmationChecked() && !this.isSubmitting()
		&& this.plan() !== null && this.planFresh() && !this.planRefreshing());

	constructor() {
		// Debounce at the keystroke source; one in-flight plan request at a time.
		this.planTrigger$
			.pipe(
				debounceTime(400),
				tap(() => this.planRefreshing.set(true)),
				switchMap(() => {
					const req = this.buildCloneRequest(null);
					const key = JSON.stringify(req);
					return this.cloneService.previewClone(req).pipe(
						tap(planDto => this.onPlanArrived(planDto, key)),
						catchError(err => {
							this.planRefreshing.set(false);
							this.toast.show(err.error?.message ?? 'Plan refresh failed', 'danger', 4000);
							return EMPTY;
						}),
					);
				}),
				takeUntilDestroyed(this.destroyRef),
			)
			.subscribe();
	}

	ngOnInit(): void {
		this.loadSourceFromRoute();
		// Customer list for the owner picker — the only reference data the workbench needs.
		this.cloneService.getReferenceData().subscribe({
			next: data => this.referenceData.set(data),
			error: () => this.toast.show('Failed to load customers', 'danger', 4000),
		});
	}

	// ══════════════════════════════════════════════════════════
	// Form plumbing
	// ══════════════════════════════════════════════════════════

	/** Template helper: write a form signal and mark the plan stale (debounced refetch). */
	set<T>(sig: WritableSignal<T>, value: T): void {
		sig.set(value);
		this.requestPlan();
	}

	updateLeagueName(sourceLeagueId: string, nameTarget: string): void {
		this.leagueRenames.set(this.leagueRenames().map(r =>
			r.sourceLeagueId === sourceLeagueId ? { ...r, nameTarget } : r));
		this.requestPlan();
	}

	private requestPlan(): void {
		if (!this.source()) return;
		this.planTrigger$.next();
	}

	// ══════════════════════════════════════════════════════════
	// Seeding
	// ══════════════════════════════════════════════════════════

	private loadSourceFromRoute(): void {
		this.cloneService.getSources().subscribe({
			next: sources => {
				const currentPath =
					this.jobContext.resolveFromRoute(this.route)
					|| this.jobContext.jobPath()
					|| (globalThis.location?.pathname ?? '').split('/').filter(Boolean)[0]
					|| '';
				const match = sources.find(j => j.jobPath.toLowerCase() === currentPath.toLowerCase()) ?? null;
				this.isLoadingSource.set(false);
				if (!match) {
					this.toast.show(`Current job "${currentPath}" not found in cloneable sources`, 'danger', 5000);
					return;
				}
				this.source.set(match);
				this.seedFromSource(match);
				this.requestPlan();
			},
			error: () => {
				this.isLoadingSource.set(false);
				this.toast.show('Failed to load source jobs', 'danger', 4000);
			},
		});
	}

	private seedFromSource(source: JobCloneSourceDto): void {
		// Next-season defaults: +1 year everywhere a 20xx token appears (mirrors the
		// server's IncrementYearsInName — full century, not a hardcoded decade).
		// Same owner by default — retargeting is a deliberate act, never a default.
		this.customerId.set(source.customerId);

		const sourceYear = source.year ?? '';
		const targetYear = sourceYear && /^\d{4}$/.test(sourceYear) ? String(Number(sourceYear) + 1) : sourceYear;

		const bumpedPath = this.bumpYearTokens(source.jobPath);
		const bumpedName = this.bumpYearTokens(source.jobName ?? '');
		this.jobPathTarget.set(bumpedPath !== source.jobPath ? bumpedPath : `${source.jobPath}-copy`);
		this.jobNameTarget.set(bumpedName !== (source.jobName ?? '') ? bumpedName : `${source.jobName ?? ''} (Copy)`);
		this.yearTarget.set(targetYear);
		this.seasonTarget.set(source.season ?? '');
		this.displayName.set(this.jobNameTarget());

		const oneYearOut = new Date();
		oneYearOut.setFullYear(oneYearOut.getFullYear() + 1);
		this.expiryAdmin.set(this.toDateInput(oneYearOut));
		this.expiryUsers.set(this.toDateInput(oneYearOut));
	}

	private onPlanArrived(planDto: ClonePlanDto, key: string): void {
		this.plan.set(planDto);
		this.planRefreshing.set(false);

		// One-time seeds from the FIRST plan: comm defaults, advance-flag default,
		// type-aware scope/store defaults. Each seed changes the request → the stream
		// refetches → the next plan lands fresh against the seeded inputs.
		let reseeded = false;
		if (!this.commSeeded) {
			this.commSeeded = true;
			this.regFormFrom.set(planDto.regFormFrom ?? '');
			this.regFormCcs.set(planDto.regFormCcs ?? '');
			this.regFormBccs.set(planDto.regFormBccs ?? '');
			this.rescheduleemaillist.set(planDto.rescheduleemaillist ?? '');
			this.alwayscopyemaillist.set(planDto.alwayscopyemaillist ?? '');
			this.mailTo.set(planDto.mailTo ?? '');
			this.payTo.set(planDto.payTo ?? '');
			this.storeContactEmail.set(planDto.storeContactEmail ?? '');
			this.upAgegroupNamesByOne.set(planDto.advanceFlagDefault);
			reseeded = true;
		}
		if (!this.typeDefaultsSeeded) {
			this.typeDefaultsSeeded = true;
			const scope = JobCloneWorkbenchComponent.ScopeByJobType[planDto.sourceJobTypeId];
			if (scope) this.ladtScope.set(scope);
			if (planDto.sourceJobTypeId === 5) this.storeChoice.set('keep'); // Sales Venue lives on its store
			reseeded = true;
		}

		// League rename rows: one per source league, seeded with the year-bumped default;
		// keyed merge preserves the operator's edits across refetches.
		const existing = new Map(this.leagueRenames().map(r => [r.sourceLeagueId, r]));
		const rows = (planDto.leagues ?? []).map(l => existing.get(l.sourceLeagueId) ?? {
			sourceLeagueId: l.sourceLeagueId,
			sourceName: l.sourceName ?? '',
			nameTarget: l.defaultNameTarget ?? '',
		});
		if (rows.length !== this.leagueRenames().length
			|| rows.some((r, i) => r !== this.leagueRenames()[i])) {
			this.leagueRenames.set(rows);
			reseeded = true;
		}

		this.planKey.set(reseeded ? null : key);
		if (reseeded) this.requestPlan();
	}

	private bumpYearTokens(s: string): string {
		return s.replace(/\b(20\d{2})\b/g, y => String(Number(y) + 1));
	}

	// ══════════════════════════════════════════════════════════
	// Identity uniqueness (inline, on blur)
	// ══════════════════════════════════════════════════════════

	checkIdentity(): void {
		if (!this.jobPathTarget() || !this.jobNameTarget()) return;
		this.cloneService.jobIdentityExists(this.jobPathTarget(), this.jobNameTarget()).subscribe({
			next: res => {
				this.pathCollision.set(res.pathExists);
				this.nameCollision.set(res.nameExists);
			},
			error: () => { /* non-blocking — submit re-validates server-side */ },
		});
	}

	// ══════════════════════════════════════════════════════════
	// Submit
	// ══════════════════════════════════════════════════════════

	onSubmit(): void {
		if (!this.canClone()) return;
		this.isSubmitting.set(true);
		this.error.set(null);

		const request = this.buildCloneRequest(this.plan()?.planFingerprint ?? null);
		this.cloneService.cloneJob(request).subscribe({
			next: response => {
				this.toast.show(`Cloned to ${response.newJobPath}`, 'success');
				if (this.enterNewJob()) {
					// Re-mint the JWT scoped to the new job, then land on ITS release page.
					this.authService.selectRegistration(response.newSuperUserRegistrationId).subscribe({
						next: () => {
							this.isSubmitting.set(false);
							this.router.navigate(
								['/', response.newJobPath, 'configure', 'job-clone', 'release', response.newJobId],
								{ state: { celebrate: true } });
						},
						error: () => {
							// Clone succeeded; JWT switch didn't. Stay in the source job — the
							// release page works from the URL alone (SuperUser is jobPath-exempt).
							this.isSubmitting.set(false);
							this.toast.show('Could not enter the new job; releasing from here.', 'warning', 5000);
							this.router.navigate(['release', response.newJobId],
								{ relativeTo: this.route, state: { celebrate: true } });
						},
					});
				} else {
					// Stay in the source job. The release page keeps working from its URL —
					// the toast above carries the new job path.
					this.isSubmitting.set(false);
					this.router.navigate(['..'], { relativeTo: this.route });
				}
			},
			error: err => {
				this.isSubmitting.set(false);
				if (err.status === 409 && err.error?.freshPlan) {
					// Data-moved guard: source data changed between plan and submit. Render
					// the fresh plan (it matches the current inputs) and ask for re-approval.
					this.plan.set(err.error.freshPlan as ClonePlanDto);
					this.planKey.set(this.requestKey());
					this.affirmationChecked.set(false);
					this.error.set('Source data changed since the plan was computed — review the refreshed plan and clone again.');
					this.toast.show('Plan refreshed — source data moved', 'warning', 5000);
					return;
				}
				const message = err.error?.message ?? 'Clone failed. Please check the parameters.';
				this.error.set(message);
				this.toast.show(message, 'danger', 4000);
			},
		});
	}

	private buildCloneRequest(planFingerprint: string | null): JobCloneRequest {
		return {
			sourceJobId: this.source()?.jobId ?? '',
			targetCustomerId: this.customerId(),
			jobPathTarget: this.jobPathTarget(),
			jobNameTarget: this.jobNameTarget(),
			yearTarget: this.yearTarget(),
			seasonTarget: this.seasonTarget(),
			displayName: this.displayName(),
			leagues: this.leagueRenames().map(r => ({
				sourceLeagueId: r.sourceLeagueId,
				nameTarget: r.nameTarget,
			})),
			expiryAdmin: this.expiryAdmin(),
			expiryUsers: this.expiryUsers(),
			// null before the comm seed = "keep source"; after seeding these are always
			// strings (empty string deliberately CLEARS the field on the new job).
			regFormFrom: this.commSeeded ? this.regFormFrom() : null,
			regFormCcs: this.commSeeded ? this.regFormCcs() : null,
			regFormBccs: this.commSeeded ? this.regFormBccs() : null,
			rescheduleemaillist: this.commSeeded ? this.rescheduleemaillist() : null,
			alwayscopyemaillist: this.commSeeded ? this.alwayscopyemaillist() : null,
			mailTo: this.commSeeded ? this.mailTo() : null,
			payTo: this.commSeeded ? this.payTo() : null,
			storeContactEmail: this.commSeeded ? this.storeContactEmail() : null,
			upAgegroupNamesByOne: this.upAgegroupNamesByOne(),
			noParallaxSlide1: this.noParallaxSlide1(),
			ladtScope: this.ladtScope(),
			copyDivisions: this.copyDivisions(),
			enableEcheckChoice: this.enableEcheckChoice(),
			storeChoice: this.storeChoice(),
			planFingerprint,
		};
	}

	// ══════════════════════════════════════════════════════════
	// Template helpers
	// ══════════════════════════════════════════════════════════

	stepLabel(key: string): string {
		return STEP_LABELS[key] ?? key;
	}

	formatDate(value: string | null | undefined): string {
		if (!value) return '—';
		const d = new Date(value);
		return isNaN(d.getTime()) ? '—' : d.toISOString().slice(0, 10);
	}

	formatShift(shift: { from?: string | null; to?: string | null } | null | undefined): string {
		if (!shift) return '—';
		return `${this.formatDate(shift.from)} → ${this.formatDate(shift.to)}`;
	}

	private toDateInput(d: Date): string {
		return d.toISOString().slice(0, 10);
	}
}
