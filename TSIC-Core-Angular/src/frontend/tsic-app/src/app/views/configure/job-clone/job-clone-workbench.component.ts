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
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';
import { plainTextToHtml } from '@infrastructure/utils/banner-text.utils';
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

/**
 * Steps the plan still performs but the pane does not list.
 *
 * OwlImages — the legacy home-page carousel (JobOwlImages). Confirmed dead 08-02: the Angular
 * app never renders it, no screen edits it, and BrandingImageConventions has no upload for it,
 * so a cloned row is invisible and unfixable. The clone step is slated for REMOVAL (step order,
 * reset rule, repo add/delete, D1 snapshot, and a NotCloned entry with a reason) — deferred so
 * it doesn't ride along with unrelated work. Hidden here in the meantime because listing it
 * implies something useful happened. Delete this set once the step is gone.
 */
const HIDDEN_STEPS = new Set<string>(['OwlImages']);

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
 * once on the left, the server-computed plan rendered on the right. The pane refetches on a
 * debounced stream of edits to the EIGHT inputs it actually depends on (planInputsKey), not
 * on every keystroke in the form; the Clone button is disabled only while those are out of
 * step with the displayed plan. Everything else typed here rides the submit unchanged and
 * costs no round trip.
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
	/**
	 * The new job's event window. Seeded from the source + 1 year like the expiries above, but
	 * OPTIONAL: blank means "use the year-shifted source date", which is what the clone did
	 * silently before these fields existed. Blank does NOT clear a date the source has.
	 *
	 * On screen because the end date decides whether the event reads as over. A summer event
	 * cloned in late summer shifts to an end date that has already passed, and a job born
	 * concluded shows no registration links at all however many toggles get turned on — the
	 * failure Ann hit. The warning below fires before the clone runs, not after.
	 */
	readonly eventStartDate = signal('');
	readonly eventEndDate = signal('');

	/** Entered end date already in the past — the born-concluded trap. */
	readonly eventEndInPast = computed(() => {
		const v = this.eventEndDate();
		if (!v) return false;
		const d = new Date(`${v}T00:00:00`);
		if (Number.isNaN(d.getTime())) return false;
		const today = new Date();
		today.setHours(0, 0, 0, 0);
		return d.getTime() < today.getTime();
	});

	/** End before start — nonsense window, worth catching while it is cheap. */
	readonly eventDatesInverted = computed(() => {
		const s = this.eventStartDate();
		const e = this.eventEndDate();
		return !!s && !!e && e < s;   // ISO yyyy-MM-dd compares lexicographically
	});

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
	/** Banner headline / caption, plain text. Seeded ONCE — see bannerTextSeeded. */
	readonly bannerText1Target = signal('');
	readonly bannerText2Target = signal('');
	/**
	 * Jobs.PaymentMethodsAllowedCode — 1 CC only, 2 CC or Check, 3 Check only. Seeded from the
	 * source job (which is what the clone did silently before this control existed). Also gates
	 * eCheck: the registration UI only offers it when this is not CC-only.
	 */
	readonly paymentMethodsAllowedCode = signal(1);
	readonly enableEcheckChoice = signal<'off' | 'source'>('off');
	readonly storeChoice = signal<'keep' | 'disable'>('disable');

	/** True when the chosen payment methods make the eCheck choice inert. */
	readonly echeckBlockedByCcOnly = computed(() => this.paymentMethodsAllowedCode() === 1);

	// ── Commit controls ──
	readonly affirmationChecked = signal(false);
	readonly isSubmitting = signal(false);
	readonly error = signal<string | null>(null);

	// ── Identity collision (inline blur check) ──
	readonly pathCollision = signal(false);
	readonly nameCollision = signal(false);

	// ── Live plan ──
	readonly plan = signal<ClonePlanDto | null>(null);
	readonly planRefreshing = signal(false);
	/** planInputsKey snapshot the displayed plan was computed FROM. */
	private readonly planKey = signal<string | null>(null);
	private readonly planTrigger$ = new Subject<void>();
	/**
	 * Decides whether the comm parameters ride the request as null ("keep source") or as
	 * strings (empty string CLEARS the field on the new job) — see buildCloneRequest.
	 *
	 * Kept a signal rather than a plain field. It no longer feeds the plan freshness key
	 * (that is planInputsKey now, and comm parameters cannot change a plan), but every other
	 * field buildCloneRequest reads is a signal and a lone plain field here is a trap waiting
	 * for the next person who widens the key.
	 */
	private readonly commSeeded = signal(false);
	private typeDefaultsSeeded = false;
	/**
	 * Banner wording is seeded from the FIRST plan and never re-seeded. Deliberate: re-seeding
	 * on a later advance-flag toggle would silently overwrite wording the author typed — the
	 * same class of write-what-you-read bug the effect() ban exists to prevent. The preview
	 * shows the author what will actually ship, so the trade-off is visible rather than silent.
	 */
	private bannerTextSeeded = false;

	/** True only once the author has actually typed in a banner box — see setBannerText. */
	private readonly bannerTextDirty = signal(false);

	/**
	 * The ONLY inputs that can change the plan. Deliberately NOT the whole request.
	 *
	 * JobClonePlanner reads 13 request fields. Six change the step counts, the team split, or
	 * the fingerprint (JobClonePlanner.ComputeFingerprint: SourceJobId, TargetCustomerId,
	 * yearDelta, steps, excluded*). Two more change what the pane RENDERS without changing any
	 * count, and belong here for that reason alone: NoParallaxSlide1 drives the branding
	 * preview, and UpAgegroupNamesByOne drives the agegroup newName and bulletin rows. Both are
	 * discrete toggles, so a refetch per click is fine — it is per-KEYSTROKE refetching this
	 * exists to stop.
	 *
	 * The rest — event dates, job path/name, league renames, the eCheck choice, payment code —
	 * produce no pane content. They feed warnings and validation that already have a local
	 * equivalent rendering with no round trip (eventEndInPast()/eventDatesInverted(),
	 * checkIdentity() on blur), and all of them are re-validated for real by the
	 * in-transaction re-plan at execute.
	 *
	 * Keying on the whole request meant one character of a job name invalidated the plan,
	 * disabled Clone, and triggered a full walk of the source graph — leagues, agegroups,
	 * divisions, teams, fees, modifiers, bulletins, reports, menus, navs, admin regs — to
	 * recompute an answer that could not have changed. Getting this set WRONG is safe: a
	 * missed input means a stale fingerprint, which the execute-time re-plan turns into a 409
	 * with the fresh plan. It cannot produce a bad clone.
	 */
	readonly planInputsKey = computed(() => JSON.stringify({
		sourceJobId: this.source()?.jobId ?? '',
		targetCustomerId: this.customerId(),
		yearTarget: this.yearTarget(),
		ladtScope: this.ladtScope(),
		copyDivisions: this.copyDivisions(),
		storeChoice: this.storeChoice(),
		noParallaxSlide1: this.noParallaxSlide1(),
		upAgegroupNamesByOne: this.upAgegroupNamesByOne(),
	}));
	readonly planFresh = computed(() => this.planKey() !== null && this.planKey() === this.planInputsKey());

	// ── Branding preview (banner + header logo) ──
	// The planner runs the real reset rule, so these are the values the released site will
	// carry — already year-bumped, overridden or cleared per the options above. Resolved
	// through the SAME buildAssetUrl the public chrome uses, so what shows here is what the
	// public sees. Legacy bit us repeatedly by hiding this until after release.
	readonly brandingPreview = computed(() => this.plan()?.brandingPreview ?? null);
	readonly bannerBackgroundUrl = computed(() => buildAssetUrl(this.brandingPreview()?.backgroundImage));
	readonly bannerOverlayUrl = computed(() => buildAssetUrl(this.brandingPreview()?.overlayImage));
	readonly logoUrl = computed(() => buildAssetUrl(this.brandingPreview()?.logoImage));

	// Rendered from the LOCAL editors, not the plan — the plan refetch is debounced 400ms and
	// a preview that trails your typing by a beat reads as broken.
	readonly bannerText1Html = computed(() => plainTextToHtml(this.bannerText1Target()));
	readonly bannerText2Html = computed(() => plainTextToHtml(this.bannerText2Target()));

	/**
	 * Which of client-banner's three branches the new job will land on. Mirrors
	 * client-banner.component.html exactly — 'hero' needs the background, 'image' is the
	 * bare overlay, 'plain' is the job name alone.
	 */
	readonly bannerMode = computed<'hero' | 'image' | 'plain'>(() => {
		const p = this.brandingPreview();
		if (!p?.isCustom) return 'plain';
		if (this.bannerBackgroundUrl()) return 'hero';
		return this.bannerOverlayUrl() ? 'image' : 'plain';
	});

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
					const key = this.planInputsKey();
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

	/**
	 * Template helper for a field the plan does NOT depend on: write it and stop. No round
	 * trip, and the Clone button does not go dead while you type. Everything typed goes to the
	 * server at submit exactly as before — the plan simply has no opinion about it.
	 */
	set<T>(sig: WritableSignal<T>, value: T): void {
		sig.set(value);
	}

	/**
	 * Template helper for one of the eight inputs in planInputsKey: write it and refetch the
	 * plan. Use this ONLY for those — see planInputsKey for why the list is what it is.
	 */
	setPlanInput<T>(sig: WritableSignal<T>, value: T): void {
		sig.set(value);
		this.requestPlan();
	}

	/**
	 * Banner wording writer. Flips bannerTextDirty, which is what decides whether the
	 * request carries an override at all — seeding must NOT count as an edit. The boxes
	 * hold PLAIN text (OverlayText.ToPlainText strips tags to keep the editor readable),
	 * so sending them unconditionally flattened source markup — a source headline stored
	 * as <i>…</i> came out unstyled on a clone nobody had typed into. Untouched now sends
	 * null and the server's year-bumped original ships with its markup intact.
	 */
	setBannerText(sig: WritableSignal<string>, value: string): void {
		this.bannerTextDirty.set(true);
		this.set(sig, value);
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
		// AR-036 — carry the SOURCE's Display name forward. This used to seed the new job name,
		// so every clone silently replaced it with the Customer:Job string and, because DisplayName
		// is the From display name on outbound mail (DisplayName ?? JobName), changed the sender name
		// on every email the new job sends. Nobody sees that until they read their inbox.
		// Fallback to the new job name when the source has none: the field is required to submit
		// (see canSubmit), so seeding blank would block the operator on a field they never chose.
		this.displayName.set(source.displayName?.trim() || this.jobNameTarget());

		// Expiry advances the SOURCE's doors by a year — the same +1 as every other token
		// on this screen. Seeding from "today + 1 year" made the new season's doors close on
		// the day the clone happened to be built, which is not a date anyone chose.
		this.expiryAdmin.set(this.shiftYear(source.expiryAdmin, 1));
		this.expiryUsers.set(this.shiftYear(source.expiryUsers, 1));

		// Event window: same +1 rule, but absent stays absent — shiftYear's today+1y fallback
		// is right for a required expiry and wrong here, where a source with no event dates
		// must seed blank fields and land nulls.
		this.eventStartDate.set(this.shiftYearOrBlank(source.eventStartDate, 1));
		this.eventEndDate.set(this.shiftYearOrBlank(source.eventEndDate, 1));

		// Payment methods follow the source — the clone's long-standing behaviour, now on screen.
		this.paymentMethodsAllowedCode.set(source.paymentMethodsAllowedCode);
	}

	private onPlanArrived(planDto: ClonePlanDto, key: string): void {
		this.plan.set(planDto);
		this.planRefreshing.set(false);

		// One-time seeds from the FIRST plan: comm defaults, advance-flag default,
		// type-aware scope/store defaults. Each seed changes the request → the stream
		// refetches → the next plan lands fresh against the seeded inputs.
		if (!this.commSeeded()) {
			this.commSeeded.set(true);
			this.regFormFrom.set(planDto.regFormFrom ?? '');
			this.regFormCcs.set(planDto.regFormCcs ?? '');
			this.regFormBccs.set(planDto.regFormBccs ?? '');
			this.rescheduleemaillist.set(planDto.rescheduleemaillist ?? '');
			this.alwayscopyemaillist.set(planDto.alwayscopyemaillist ?? '');
			this.mailTo.set(planDto.mailTo ?? '');
			this.payTo.set(planDto.payTo ?? '');
			this.storeContactEmail.set(planDto.storeContactEmail ?? '');
			this.upAgegroupNamesByOne.set(planDto.advanceFlagDefault);
		}
		if (!this.bannerTextSeeded && planDto.brandingPreview) {
			this.bannerTextSeeded = true;
			this.bannerText1Target.set(planDto.brandingPreview.text1 ?? '');
			this.bannerText2Target.set(planDto.brandingPreview.text2 ?? '');
		}
		if (!this.typeDefaultsSeeded) {
			this.typeDefaultsSeeded = true;
			const scope = JobCloneWorkbenchComponent.ScopeByJobType[planDto.sourceJobTypeId];
			if (scope) this.ladtScope.set(scope);
			if (planDto.sourceJobTypeId === 5) this.storeChoice.set('keep'); // Sales Venue lives on its store
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
		}

		// Of the seeds above, ONLY the type-aware one can touch a plan input (ladtScope,
		// storeChoice). Comm parameters, banner wording and league rename rows cannot. So
		// compare the key as it stands NOW against the one this plan was computed from and
		// refetch only on a real difference — a seed that changes nothing the plan depends on
		// no longer costs a second walk of the source graph, and there is no `reseeded` flag
		// to keep in step with the seed list.
		const currentKey = this.planInputsKey();
		this.planKey.set(currentKey);
		if (currentKey !== key) this.requestPlan();
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
				// The clone minted this actor a registration on a job that did not exist when
				// role-selection's list was fetched (once, at login). Without this the new job
				// is missing from "Open a Registration" for the rest of the session — the same
				// staleness that makes a deleted job linger there, in the other direction.
				this.authService.invalidateRegistrationsCache();
				// Land IN the new job, on its home page. Everything a new job needs is a
				// normal settings screen — registration flags, administrators, branding,
				// TSIC-Events visibility — so there is no release sequence to walk, and
				// arriving anywhere else means working on one job while signed into another.
				// The clone already minted this actor's Superuser registration on the new
				// job; re-minting the JWT against it is what makes the landing real.
				this.authService.selectRegistration(response.newSuperUserRegistrationId).subscribe({
					next: () => {
						this.isSubmitting.set(false);
						this.router.navigate(['/', response.newJobPath]);
					},
					error: () => {
						// Clone succeeded; the JWT switch didn't. Say so plainly and stay put —
						// the toast above carries the new job path to log into.
						this.isSubmitting.set(false);
						this.toast.show(
							`Cloned, but could not switch into ${response.newJobPath} — log into it directly.`,
							'warning', 6000);
					},
				});
			},
			error: err => {
				this.isSubmitting.set(false);
				if (err.status === 409 && err.error?.freshPlan) {
					// Data-moved guard: source data changed between plan and submit. Render
					// the fresh plan (it matches the current inputs) and ask for re-approval.
					this.plan.set(err.error.freshPlan as ClonePlanDto);
					this.planKey.set(this.planInputsKey());
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
			// Empty → null → the server falls back to the year-shifted source date. These are
			// deliberately NOT in planInputsKey: the plan reads them only to warn about a past
			// end date, and eventEndInPast()/eventDatesInverted() already say that locally with
			// no round trip. Editing them therefore costs nothing and never disables Clone.
			eventStartDate: this.eventStartDate() || null,
			eventEndDate: this.eventEndDate() || null,
			// null before the comm seed = "keep source"; after seeding these are always
			// strings (empty string deliberately CLEARS the field on the new job).
			regFormFrom: this.commSeeded() ? this.regFormFrom() : null,
			regFormCcs: this.commSeeded() ? this.regFormCcs() : null,
			regFormBccs: this.commSeeded() ? this.regFormBccs() : null,
			rescheduleemaillist: this.commSeeded() ? this.rescheduleemaillist() : null,
			alwayscopyemaillist: this.commSeeded() ? this.alwayscopyemaillist() : null,
			mailTo: this.commSeeded() ? this.mailTo() : null,
			payTo: this.commSeeded() ? this.payTo() : null,
			storeContactEmail: this.commSeeded() ? this.storeContactEmail() : null,
			upAgegroupNamesByOne: this.upAgegroupNamesByOne(),
			noParallaxSlide1: this.noParallaxSlide1(),
			// Null unless the author typed. Sending the seeded value back would round-trip
			// the source's wording through a plain-text editor and strip its markup for no
			// reason; null lets the server's own year bump write the original verbatim.
			bannerText1Target: this.bannerTextDirty() ? this.bannerText1Target() : null,
			bannerText2Target: this.bannerTextDirty() ? this.bannerText2Target() : null,
			ladtScope: this.ladtScope(),
			copyDivisions: this.copyDivisions(),
			paymentMethodsAllowedCode: this.paymentMethodsAllowedCode(),
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

	/** Plan steps worth showing — see HIDDEN_STEPS for what is suppressed and why. */
	visibleSteps(steps: ClonePlanDto['steps']): ClonePlanDto['steps'] {
		return steps.filter(s => !HIDDEN_STEPS.has(s.stepKey));
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

	/**
	 * Source date (ISO from the API) advanced by `years`, as a yyyy-MM-dd value for
	 * <input type="date">. Falls back to today + `years` when the source date is absent
	 * or unparseable, so the field is never left empty (formValid requires it).
	 */
	/**
	 * Same +1 shift as <see cref="shiftYear"/>, but an absent source stays absent — no
	 * today-based fallback. For an optional date, inventing one is worse than leaving it blank.
	 */
	private shiftYearOrBlank(iso: string | null | undefined, years: number): string {
		return iso ? this.shiftYear(iso, years) : '';
	}

	private shiftYear(iso: string | null | undefined, years: number): string {
		const d = iso ? new Date(iso) : new Date();
		const base = Number.isNaN(d.getTime()) ? new Date() : d;
		const shifted = new Date(base.getTime());
		shifted.setFullYear(shifted.getFullYear() + years);
		return this.toDateInput(shifted);
	}

	/** Local calendar date, not UTC — toISOString() would roll the day in +offset zones. */
	private toDateInput(d: Date): string {
		const pad = (n: number) => String(n).padStart(2, '0');
		return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
	}
}
