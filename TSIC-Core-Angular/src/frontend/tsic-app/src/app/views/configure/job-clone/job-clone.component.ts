import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { JobCloneService } from './services/job-clone.service';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { ToastService } from '@shared-ui/toast.service';
import type {
	AgegroupPreviewDto,
	BlankJobRequest,
	BulletinShiftDto,
	DateShiftDto,
	FeeModifierShiftDto,
	JobClonePreviewResponse,
	JobCloneSourceDto,
	ReleasableAdminDto,
} from '@core/api';

type Mode = 'wizard' | 'release';
type Flavor = 'clone' | 'blank';

/**
 * Minimal shape used to seed the Release view. Accepts either a suspended-job
 * pick from Landing or the response from a just-submitted wizard.
 */
interface ReleaseContext {
	readonly jobId: string;
	readonly jobPath: string;
	readonly jobName: string;
}

@Component({
	selector: 'app-job-clone',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './job-clone.component.html',
	styleUrl: './job-clone.component.scss',
})
export class JobCloneComponent implements OnInit {
	private readonly cloneService = inject(JobCloneService);
	private readonly jobContext = inject(JobContextService);
	private readonly route = inject(ActivatedRoute);
	private readonly toast = inject(ToastService);

	readonly totalSteps = 7;

	// ── Mode state ──
	readonly mode = signal<Mode>('wizard');
	readonly flavor = signal<Flavor>('clone');
	readonly step = signal<number>(1);

	// ── Source data (clone flavor) ──
	readonly sourceJobs = signal<JobCloneSourceDto[]>([]);
	readonly isLoadingSources = signal(false);
	readonly selectedSource = signal<JobCloneSourceDto | null>(null);

	// ── Wizard form state (shared between clone + blank) ──
	// Step 1 blank flavor
	customerId = '';
	billingTypeId: number | null = null;
	jobTypeId: number | null = null;
	sportId = '';

	// Step 2 identity
	jobPathTarget = '';
	jobNameTarget = '';
	yearTarget = '';
	seasonTarget = '';
	displayName = '';
	leagueNameTarget = '';

	// Step 3 dates
	expiryAdmin = '';
	expiryUsers = '';

	// Step 4 LADT scope
	ladtScope: 'none' | 'lad' | 'ladt' = 'lad';

	// Step 5 fee defaults (prompt-if-valued)
	processingFeeChoice: 'source' | 'current' | 'custom' = 'current';
	customProcessingFee = 3.5;
	storeChoice: 'keep' | 'disable' = 'disable';

	// Step 1 toggle — controls whether the year-bump logic runs when advancing to Step 2.
	autoAdvanceYear = true;

	// Step 6 people / options
	upAgegroupNamesByOne = true;
	noParallaxSlide1 = false;

	// Step 7 review
	affirmationChecked = false;
	regFormFrom = '';

	// ── Preview state (loaded at Step 3) ──
	readonly preview = signal<JobClonePreviewResponse | null>(null);
	readonly isLoadingPreview = signal(false);

	// ── Release-mode state ──
	readonly releaseJobId = signal<string | null>(null);
	readonly releaseJobContext = signal<ReleaseContext | null>(null);
	readonly releaseAdmins = signal<ReleasableAdminDto[]>([]);
	readonly releaseSelectedRegIds = signal<Set<string>>(new Set());
	readonly isLoadingAdmins = signal(false);
	readonly isReleasingSite = signal(false);
	readonly isActivatingAdmins = signal(false);
	readonly sitePublic = signal<boolean | null>(null);

	// ── Submit state ──
	readonly isSubmitting = signal(false);
	readonly error = signal<string | null>(null);

	// ── Computed ──
	readonly canAdvance = computed(() => this.validateCurrentStep());
	readonly inactiveAdmins = computed(() =>
		this.releaseAdmins().filter(a => !a.bActive));
	readonly activeAdmins = computed(() =>
		this.releaseAdmins().filter(a => a.bActive));
	readonly selectedCount = computed(() => this.releaseSelectedRegIds().size);

	ngOnInit(): void {
		// Component opens directly to the wizard — SuperUser-only tooling,
		// source picker lists all cloneable jobs.
		this.loadSources();
	}

	// ══════════════════════════════════════════════════════════
	// Wizard entry points
	// ══════════════════════════════════════════════════════════

	switchFlavor(f: Flavor): void {
		this.flavor.set(f);
		this.step.set(1);
		this.resetWizard();
		if (f === 'clone') {
			// Re-run auto-select so the source stays wired to the current job.
			this.tryAutoSelectCurrentJob();
		} else {
			// Blank defaults: next year, today-based dates
			const today = new Date();
			const nextYear = String(today.getFullYear() + 1);
			this.yearTarget = nextYear;
			const oneYearOut = new Date();
			oneYearOut.setFullYear(oneYearOut.getFullYear() + 1);
			this.expiryAdmin = this.toDateInput(oneYearOut);
			this.expiryUsers = this.toDateInput(oneYearOut);
		}
	}

	cancelWizard(): void {
		// Wipe fields, return to step 1, preserve current flavor. For clone flavor
		// re-run auto-select so the current job stays the source.
		this.resetWizard();
		if (this.flavor() === 'clone') this.tryAutoSelectCurrentJob();
	}

	private loadSources(): void {
		this.isLoadingSources.set(true);
		this.cloneService.getSources().subscribe({
			next: sources => {
				this.sourceJobs.set(sources);
				this.isLoadingSources.set(false);
				this.tryAutoSelectCurrentJob();
			},
			error: () => {
				this.isLoadingSources.set(false);
				this.toast.show('Failed to load source jobs', 'danger', 4000);
			},
		});
	}

	private tryAutoSelectCurrentJob(): void {
		// Source = the current job (no dropdown). Match by jobPath taken from the
		// ActivatedRoute (authoritative) with the singleton JobContextService as fallback.
		if (this.selectedSource()) return;
		const currentPath =
			this.jobContext.resolveFromRoute(this.route)
			|| this.jobContext.jobPath()
			|| this.jobPathFromWindow();
		if (!currentPath) {
			console.warn('[job-clone] could not resolve current jobPath from route');
			return;
		}
		const match = this.sourceJobs().find(
			j => j.jobPath.toLowerCase() === currentPath.toLowerCase(),
		);
		if (match) {
			this.onSourceSelected(match.jobId);
		} else {
			console.warn(
				`[job-clone] jobPath "${currentPath}" not found in sources list (${this.sourceJobs().length} entries)`,
			);
		}
	}

	private jobPathFromWindow(): string | null {
		// Last-resort fallback: parse the first URL segment. Works even when
		// JobContextService hasn't been initialized for this route.
		const path = globalThis.location?.pathname ?? '';
		const first = path.split('/').filter(Boolean)[0] ?? '';
		return first || null;
	}

	onSourceSelected(sourceId: string): void {
		// Only registers the current job as the source — no form population. Fields
		// on Step 2 are populated when the user hits Next, based on the autoAdvanceYear
		// toggle so the user can opt out of the +1-year smart defaults.
		if (!sourceId) {
			this.selectedSource.set(null);
			return;
		}
		const source = this.sourceJobs().find(j => j.jobId === sourceId) ?? null;
		this.selectedSource.set(source);
	}

	private populateStep2FromSource(): void {
		const source = this.selectedSource();
		if (!source) return;

		const advance = this.autoAdvanceYear;
		const sourceYear = source.year ?? '';
		const targetYear = advance && sourceYear ? String(Number(sourceYear) + 1) : sourceYear;

		if (advance) {
			// Bump every 4-digit year token (20XX) in path + name. Mirrors server-side
			// IncrementYearsInName so "...-2025-2026" becomes "...-2026-2027".
			const bumpedPath = this.bumpYearTokens(source.jobPath);
			const bumpedName = this.bumpYearTokens(source.jobName ?? '');
			this.jobPathTarget = bumpedPath !== source.jobPath ? bumpedPath : `${source.jobPath}-copy`;
			this.jobNameTarget = bumpedName !== (source.jobName ?? '') ? bumpedName : `${source.jobName ?? ''} (Copy)`;
		} else {
			// Same year — user has to pick a different path/name. Seed with "-copy" variants.
			this.jobPathTarget = `${source.jobPath}-copy`;
			this.jobNameTarget = `${source.jobName ?? ''} (Copy)`;
		}

		this.yearTarget = targetYear;
		this.seasonTarget = source.season ?? '';
		this.displayName = this.jobNameTarget;

		// League name defaults to the source league name (joined via JobLeagues on the
		// server). Year-bump when auto-advance is on, verbatim otherwise.
		const srcLeagueName = source.leagueName ?? '';
		this.leagueNameTarget = advance ? this.bumpYearTokens(srcLeagueName) : srcLeagueName;

		const expiryTarget = new Date();
		expiryTarget.setFullYear(expiryTarget.getFullYear() + 1);
		this.expiryAdmin = this.toDateInput(expiryTarget);
		this.expiryUsers = this.toDateInput(expiryTarget);
	}

	private bumpYearTokens(s: string): string {
		// Matches any 4-digit year in 2020–2039 as a standalone token; increments by 1.
		return s.replace(/\b(20[2-3]\d)\b/g, y => String(Number(y) + 1));
	}

	// ══════════════════════════════════════════════════════════
	// Wizard navigation
	// ══════════════════════════════════════════════════════════

	wizardNext(): void {
		if (!this.canAdvance()) return;
		const next = this.step() + 1;
		if (next > this.totalSteps) return;

		// Transitioning Step 1 → 2 in Clone flavor: populate target fields from source,
		// honoring the autoAdvanceYear toggle. Fresh each time so flipping the toggle
		// on Step 1 and moving forward re-seeds Step 2 cleanly.
		if (this.step() === 1 && next === 2 && this.flavor() === 'clone') {
			this.populateStep2FromSource();
		}

		// Entering Step 3 (dates) — load preview for clone flavor.
		if (next === 3 && this.flavor() === 'clone' && !this.preview()) {
			this.loadPreview();
		}
		this.step.set(next);
	}

	wizardBack(): void {
		const prev = this.step() - 1;
		if (prev < 1) return;
		this.step.set(prev);
		this.ensureSourceOnStep1();
	}

	goToStep(target: number): void {
		if (target < 1 || target > this.totalSteps) return;
		if (target > this.step() && !this.canAdvance()) return;
		this.step.set(target);
		this.ensureSourceOnStep1();
	}

	private ensureSourceOnStep1(): void {
		// When landing on Step 1 in Clone flavor, guarantee selectedSource is still
		// the current job (guards against any path that clears it mid-wizard).
		if (this.step() === 1 && this.flavor() === 'clone' && !this.selectedSource()) {
			this.tryAutoSelectCurrentJob();
		}
	}

	private validateCurrentStep(): boolean {
		const s = this.step();
		const f = this.flavor();

		if (s === 1) {
			if (f === 'clone') return !!this.selectedSource();
			return !!this.customerId && this.billingTypeId !== null && this.jobTypeId !== null && !!this.sportId;
		}
		if (s === 2) {
			return !!this.jobPathTarget && !!this.jobNameTarget && !!this.yearTarget
				&& !!this.seasonTarget && !!this.displayName
				&& (f === 'blank' || !!this.leagueNameTarget);
		}
		if (s === 3) return !!this.expiryAdmin && !!this.expiryUsers;
		if (s === 7) return this.affirmationChecked;
		return true;
	}

	private loadPreview(): void {
		const source = this.selectedSource();
		if (!source) return;

		this.isLoadingPreview.set(true);
		this.cloneService.previewClone(this.buildCloneRequest()).subscribe({
			next: preview => {
				this.preview.set(preview);
				this.isLoadingPreview.set(false);
			},
			error: err => {
				this.isLoadingPreview.set(false);
				this.toast.show(err.error?.message ?? 'Preview failed', 'danger', 4000);
			},
		});
	}

	refreshPreview(): void {
		this.preview.set(null);
		this.loadPreview();
	}

	// ══════════════════════════════════════════════════════════
	// Submit
	// ══════════════════════════════════════════════════════════

	onSubmit(): void {
		if (!this.canAdvance()) return;
		this.isSubmitting.set(true);
		this.error.set(null);

		if (this.flavor() === 'clone') {
			this.cloneService.cloneJob(this.buildCloneRequest()).subscribe({
				next: response => this.onCreationSuccess(response.newJobId, response.newJobPath, response.newJobName),
				error: err => this.onCreationFailure(err),
			});
		} else {
			this.cloneService.createBlank(this.buildBlankRequest()).subscribe({
				next: response => this.onCreationSuccess(response.newJobId, response.newJobPath, response.newJobName),
				error: err => this.onCreationFailure(err),
			});
		}
	}

	private onCreationSuccess(newJobId: string, newJobPath: string, newJobName: string): void {
		this.isSubmitting.set(false);
		this.toast.show(`Job created: ${newJobPath}`, 'success');
		// Transition directly into Release mode for the new job.
		this.openRelease({ jobId: newJobId, jobPath: newJobPath, jobName: newJobName });
	}

	private onCreationFailure(err: { error?: { message?: string } }): void {
		const message = err.error?.message ?? 'Submit failed. Please check the parameters.';
		this.error.set(message);
		this.isSubmitting.set(false);
		this.toast.show(message, 'danger', 4000);
	}

	private buildCloneRequest() {
		const src = this.selectedSource();
		return {
			sourceJobId: src?.jobId ?? '',
			jobPathTarget: this.jobPathTarget,
			jobNameTarget: this.jobNameTarget,
			yearTarget: this.yearTarget,
			seasonTarget: this.seasonTarget,
			displayName: this.displayName,
			leagueNameTarget: this.leagueNameTarget,
			expiryAdmin: this.expiryAdmin,
			expiryUsers: this.expiryUsers,
			regFormFrom: this.regFormFrom || undefined,
			upAgegroupNamesByOne: this.upAgegroupNamesByOne,
			setDirectorsToInactive: true, // dead field — server ignores
			noParallaxSlide1: this.noParallaxSlide1,
		};
	}

	private buildBlankRequest(): BlankJobRequest {
		return {
			customerId: this.customerId,
			jobPathTarget: this.jobPathTarget,
			jobNameTarget: this.jobNameTarget,
			yearTarget: this.yearTarget,
			seasonTarget: this.seasonTarget,
			displayName: this.displayName,
			expiryAdmin: this.expiryAdmin,
			expiryUsers: this.expiryUsers,
			billingTypeId: this.billingTypeId!,
			jobTypeId: this.jobTypeId!,
			sportId: this.sportId,
			regFormFrom: this.regFormFrom || undefined,
		};
	}

	// ══════════════════════════════════════════════════════════
	// Release mode
	// ══════════════════════════════════════════════════════════

	openRelease(ctx: ReleaseContext): void {
		this.releaseJobId.set(ctx.jobId);
		this.releaseJobContext.set(ctx);
		this.releaseSelectedRegIds.set(new Set());
		this.sitePublic.set(null);
		this.loadReleaseAdmins(ctx.jobId);
		this.mode.set('release');
	}

	private loadReleaseAdmins(jobId: string): void {
		this.isLoadingAdmins.set(true);
		this.cloneService.getAdmins(jobId).subscribe({
			next: admins => {
				this.releaseAdmins.set(admins);
				this.isLoadingAdmins.set(false);
			},
			error: () => {
				this.isLoadingAdmins.set(false);
				this.toast.show('Failed to load admins', 'danger', 4000);
			},
		});
	}

	toggleAdminSelect(regId: string): void {
		const current = new Set(this.releaseSelectedRegIds());
		if (current.has(regId)) current.delete(regId);
		else current.add(regId);
		this.releaseSelectedRegIds.set(current);
	}

	selectAllInactive(): void {
		const ids = new Set(this.inactiveAdmins().map(a => a.registrationId));
		this.releaseSelectedRegIds.set(ids);
	}

	clearAdminSelection(): void {
		this.releaseSelectedRegIds.set(new Set());
	}

	onReleaseSite(): void {
		const jobId = this.releaseJobId();
		if (!jobId) return;
		this.isReleasingSite.set(true);
		this.cloneService.releaseSite(jobId).subscribe({
			next: response => {
				this.isReleasingSite.set(false);
				this.sitePublic.set(!response.bSuspendPublic);
				this.toast.show('Job is now visible to the public', 'success');
			},
			error: err => {
				this.isReleasingSite.set(false);
				this.toast.show(err.error?.message ?? 'Release failed', 'danger', 4000);
			},
		});
	}

	onActivateSelected(): void {
		const jobId = this.releaseJobId();
		const ids = [...this.releaseSelectedRegIds()];
		if (!jobId || ids.length === 0) return;

		this.isActivatingAdmins.set(true);
		this.cloneService.releaseAdmins(jobId, { registrationIds: ids }).subscribe({
			next: response => {
				this.isActivatingAdmins.set(false);
				const n = response.adminsActivated;
				this.toast.show(`${n} director${n === 1 ? '' : 's'} can now log in`, 'success');
				this.releaseSelectedRegIds.set(new Set());
				this.loadReleaseAdmins(jobId);
			},
			error: err => {
				this.isActivatingAdmins.set(false);
				this.toast.show(err.error?.message ?? 'Activate failed', 'danger', 4000);
			},
		});
	}

	// ══════════════════════════════════════════════════════════
	// Helpers exposed to template
	// ══════════════════════════════════════════════════════════

	formatDate(value: string | null | undefined): string {
		if (!value) return '—';
		const d = new Date(value);
		return isNaN(d.getTime()) ? '—' : d.toISOString().slice(0, 10);
	}

	formatShift(shift: DateShiftDto | null | undefined): string {
		if (!shift) return '—';
		return `${this.formatDate(shift.from)} → ${this.formatDate(shift.to)}`;
	}

	trackBulletin = (_: number, b: BulletinShiftDto) => b.sourceBulletinId;
	trackAgegroup = (_: number, a: AgegroupPreviewDto) => a.sourceAgegroupId;
	trackModifier = (_: number, m: FeeModifierShiftDto) => m.sourceFeeModifierId;
	trackAdmin = (_: number, a: ReleasableAdminDto) => a.registrationId;

	private resetWizard(): void {
		this.step.set(1);
		this.preview.set(null);
		this.selectedSource.set(null);
		this.error.set(null);
		this.affirmationChecked = false;
		this.jobPathTarget = '';
		this.jobNameTarget = '';
		this.yearTarget = '';
		this.seasonTarget = '';
		this.displayName = '';
		this.leagueNameTarget = '';
		this.expiryAdmin = '';
		this.expiryUsers = '';
		this.regFormFrom = '';
		this.customerId = '';
		this.billingTypeId = null;
		this.jobTypeId = null;
		this.sportId = '';
	}

	private toDateInput(d: Date): string {
		return d.toISOString().slice(0, 10);
	}
}
