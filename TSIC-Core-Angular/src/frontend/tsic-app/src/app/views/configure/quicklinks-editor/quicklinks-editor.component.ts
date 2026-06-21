import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { QuicklinksEditorService } from './services/quicklinks-editor.service';
import { ToastService } from '@shared-ui/toast.service';
import { HasUnsavedChanges } from '@infrastructure/guards/unsaved-changes.guard';
import type {
	JobTypeRefDto,
	JobRefDto,
	JobPulseDto,
	QuickLinkEditorModelDto,
	QuickLinkSaveRowDto,
} from '@core/api';

/** Per-row editor view-model. SortOrder is implicit = array index after drag. */
interface QuicklinkRowVm {
	linkKey: string;
	label: string;            // effective, editable
	defaultLabel: string;
	icon: string | null;
	grounded: boolean;
	groundingSetting: string | null;
	groundingPulseFlag: string | null;
	groundingInverted: boolean;
	/** JobQuickLink.Enabled override: null = follow/default, false = force-hide, true = ungrounded show. */
	enabledOverride: boolean | null;
}

@Component({
	selector: 'app-quicklinks-editor',
	standalone: true,
	imports: [CommonModule, DragDropModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './quicklinks-editor.component.html',
	styleUrl: './quicklinks-editor.component.scss',
})
export class QuicklinksEditorComponent implements HasUnsavedChanges {
	private readonly editorService = inject(QuicklinksEditorService);
	private readonly toast = inject(ToastService);

	// ── Picker ──
	readonly jobTypes = signal<JobTypeRefDto[]>([]);
	readonly selectedJobTypeId = signal<number>(0);
	readonly jobs = signal<JobRefDto[]>([]);
	readonly selectedJobId = signal<string>('');
	readonly jobName = signal<string>('');
	readonly jobPath = signal<string>('');

	// ── Editor state ──
	readonly pulse = signal<JobPulseDto | null>(null);
	readonly rows = signal<QuicklinkRowVm[]>([]);
	private readonly originalRows = signal<QuicklinkRowVm[]>([]);

	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);

	constructor() {
		this.editorService.getJobTypes().subscribe({
			next: jt => this.jobTypes.set(jt),
			error: err => this.handleError('Failed to load job types', err),
		});
	}

	// ── Dirty tracking ──
	readonly isDirty = computed(() => {
		const cur = this.rows();
		const orig = this.originalRows();
		if (cur.length !== orig.length) return true;
		const origByKey = new Map(orig.map(r => [r.linkKey, r]));
		for (let i = 0; i < cur.length; i++) {
			const c = cur[i];
			const o = origByKey.get(c.linkKey);
			if (!o) return true;
			if (orig[i].linkKey !== c.linkKey) return true;        // order changed
			if (o.label !== c.label) return true;                  // label changed
			if (o.enabledOverride !== c.enabledOverride) return true; // visibility changed
		}
		return false;
	});

	hasUnsavedChanges(): boolean {
		return this.isDirty();
	}

	// ── Picker handlers ──
	onJobTypeChange(event: Event): void {
		const jobTypeId = +(event.target as HTMLSelectElement).value;
		this.selectedJobTypeId.set(jobTypeId);
		this.clearSelection();
		if (jobTypeId > 0) {
			this.editorService.getJobsByJobType(jobTypeId).subscribe({
				next: jobs => this.jobs.set(jobs),
				error: err => this.handleError('Failed to load jobs', err),
			});
		} else {
			this.jobs.set([]);
		}
	}

	onJobChange(event: Event): void {
		const jobId = (event.target as HTMLSelectElement).value;
		this.selectedJobId.set(jobId);
		const job = this.jobs().find(j => j.jobId === jobId);
		this.jobName.set(job?.jobName ?? '');
		this.jobPath.set(job?.jobPath ?? '');
		if (jobId && job) {
			this.loadEditor(jobId, job.jobPath);
		} else {
			this.rows.set([]);
			this.originalRows.set([]);
		}
	}

	private loadEditor(jobId: string, jobPath: string): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);
		// Pulse drives grounded preview; the editor model is the authoritative config.
		this.editorService.getPulse(jobPath).subscribe({
			next: pulse => this.pulse.set(pulse),
			error: () => this.pulse.set(null), // preview degrades gracefully; not fatal
		});
		this.editorService.getEditorModel(jobId).subscribe({
			next: model => {
				const vms = this.toVms(model);
				this.rows.set(vms);
				this.originalRows.set(vms.map(r => ({ ...r })));
				this.isLoading.set(false);
			},
			error: err => {
				this.handleError('Failed to load quicklinks', err);
				this.isLoading.set(false);
			},
		});
	}

	private toVms(model: QuickLinkEditorModelDto): QuicklinkRowVm[] {
		return model.rows.map(r => ({
			linkKey: r.linkKey,
			label: r.overrideLabel ?? r.defaultLabel,
			defaultLabel: r.defaultLabel,
			icon: r.icon ?? null,
			grounded: r.isGrounded,
			groundingSetting: r.groundingSetting ?? null,
			groundingPulseFlag: r.groundingPulseFlag ?? null,
			groundingInverted: r.groundingInverted,
			enabledOverride: r.enabled ?? null,
		}));
	}

	// ── Grounded preview: derived on/off from the chosen job's pulse ──
	/** True when a grounded link's underlying job condition is currently on. */
	derivedOn(row: QuicklinkRowVm): boolean {
		const p = this.pulse();
		if (!p || !row.groundingPulseFlag) return false;
		const raw = !!(p as unknown as Record<string, unknown>)[row.groundingPulseFlag];
		return row.groundingInverted ? !raw : raw;
	}

	/** Effective visibility shown to the SU (what the hero would render right now). */
	effectiveVisible(row: QuicklinkRowVm): boolean {
		if (row.grounded) return this.derivedOn(row) && row.enabledOverride !== false;
		return row.enabledOverride === true;
	}

	isForceHidden(row: QuicklinkRowVm): boolean {
		return row.grounded && row.enabledOverride === false;
	}

	groundingHint(row: QuicklinkRowVm): string {
		return row.groundingSetting ?? row.groundingPulseFlag ?? '';
	}

	// ── Row edits (all immutable) ──
	onDrop(event: CdkDragDrop<QuicklinkRowVm[]>): void {
		if (event.previousIndex === event.currentIndex) return;
		const next = this.rows().slice();
		moveItemInArray(next, event.previousIndex, event.currentIndex);
		this.rows.set(next);
	}

	/** Grounded: toggle force-hide (false ↔ follow null). Ungrounded: toggle show (true ↔ off null). */
	toggleVisibility(row: QuicklinkRowVm): void {
		const next = row.grounded
			? (row.enabledOverride === false ? null : false)
			: (row.enabledOverride === true ? null : true);
		this.updateRow(row.linkKey, r => ({ ...r, enabledOverride: next }));
	}

	onLabelInput(row: QuicklinkRowVm, event: Event): void {
		const value = (event.target as HTMLInputElement).value;
		this.updateRow(row.linkKey, r => ({ ...r, label: value }));
	}

	private updateRow(linkKey: string, fn: (r: QuicklinkRowVm) => QuicklinkRowVm): void {
		this.rows.set(this.rows().map(r => (r.linkKey === linkKey ? fn(r) : r)));
	}

	resetRows(): void {
		this.rows.set(this.originalRows().map(r => ({ ...r })));
	}

	// ── Save ──
	save(): void {
		const jobId = this.selectedJobId();
		if (!jobId || !this.isDirty()) return;

		const cur = this.rows();
		const orig = this.originalRows();
		const orderDirty = cur.length !== orig.length
			|| cur.some((r, i) => orig[i]?.linkKey !== r.linkKey);

		const saveRows: QuickLinkSaveRowDto[] = cur.map((r, i) => {
			const hasLabel = r.label !== r.defaultLabel;
			const hasEnabled = r.enabledOverride !== null;
			// Pure-default row with untouched order → delete any existing override.
			if (!hasLabel && !hasEnabled && !orderDirty) {
				return { linkKey: r.linkKey, delete: true };
			}
			return {
				linkKey: r.linkKey,
				delete: false,
				enabled: r.enabledOverride,
				label: hasLabel ? r.label : null,
				sortOrder: orderDirty ? i : null,
			};
		});

		this.isSaving.set(true);
		this.editorService.save(jobId, { rows: saveRows }).subscribe({
			next: () => {
				this.originalRows.set(cur.map(r => ({ ...r })));
				this.isSaving.set(false);
				this.toast.show('Quicklinks saved', 'success');
			},
			error: err => {
				this.handleError('Failed to save quicklinks', err);
				this.isSaving.set(false);
			},
		});
	}

	private clearSelection(): void {
		this.selectedJobId.set('');
		this.jobName.set('');
		this.jobPath.set('');
		this.pulse.set(null);
		this.rows.set([]);
		this.originalRows.set([]);
	}

	private handleError(message: string, err: unknown): void {
		this.errorMessage.set(message);
		this.toast.show(message, 'danger');
		console.error(message, err);
	}
}
