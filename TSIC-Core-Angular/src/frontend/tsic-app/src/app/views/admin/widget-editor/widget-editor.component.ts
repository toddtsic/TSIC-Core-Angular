import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WidgetEditorService } from './services/widget-editor.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import type {
	JobTypeRefDto,
	RoleRefDto,
	WidgetCategoryRefDto,
	WidgetDefinitionDto,
	WidgetDefaultEntryDto,
	WidgetAssignmentDto,
} from '@core/api';

// ── Local view-model types ──

interface CategoryGroup {
	categoryId: number;
	name: string;
	widgets: WidgetDefinitionDto[];
}

interface WorkspaceGroup {
	workspace: string;
	label: string;
	icon: string | null;
	widgetCount: number;
	categories: CategoryGroup[];
}

// ── Role abbreviations ──

const ROLE_ABBREVIATIONS: Record<string, string> = {
	'Superuser': 'SU',
	'SuperDirector': 'SD',
	'Director': 'Dir',
	'ClubRep': 'CR',
	'Player': 'Pl',
	'Staff': 'St',
	'Guest': 'Gu',
	'Anonymous': 'Anon',
};

const WORKSPACE_LABELS: Record<string, string> = {
	'dashboard': 'Dashboard',
	'player-reg': 'Player Registration',
	'team-reg': 'Team Registration',
	'scheduling': 'Scheduling',
	'job-config': 'Event Setup',
	'fin-per-job': 'Customer Finances',
	'fin-per-customer': 'Job Finances',
};

@Component({
	selector: 'app-widget-editor',
	standalone: true,
	imports: [CommonModule, TsicDialogComponent, ConfirmDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './widget-editor.component.html',
	styleUrl: './widget-editor.component.scss',
})
export class WidgetEditorComponent {
	private readonly editorService = inject(WidgetEditorService);
	private readonly toast = inject(ToastService);

	// ── Reference data ──
	readonly jobTypes = signal<JobTypeRefDto[]>([]);
	readonly roles = signal<RoleRefDto[]>([]);
	readonly categories = signal<WidgetCategoryRefDto[]>([]);
	readonly widgets = signal<WidgetDefinitionDto[]>([]);

	// ── UI state ──
	readonly activeTab = signal<'matrix' | 'definitions'>('matrix');
	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);

	// ── Matrix state ──
	readonly selectedJobTypeId = signal<number>(0);
	readonly matrixEntries = signal<WidgetDefaultEntryDto[]>([]);
	readonly originalEntries = signal<WidgetDefaultEntryDto[]>([]);
	readonly expandedWorkspaces = signal<Set<string>>(new Set());
	readonly showCopyMenu = signal(false);

	// ── Widget CRUD modal state ──
	readonly showWidgetModal = signal(false);
	readonly editingWidget = signal<WidgetDefinitionDto | null>(null);
	readonly formName = signal('');
	readonly formWidgetType = signal('');
	readonly formComponentKey = signal('');
	readonly formCategoryId = signal(0);
	readonly formDescription = signal('');

	// ── Delete confirm state ──
	readonly showDeleteConfirm = signal(false);
	readonly deleteTarget = signal<WidgetDefinitionDto | null>(null);

	// ── Assign Roles modal state ──
	readonly showAssignModal = signal(false);
	readonly assignTarget = signal<WidgetDefinitionDto | null>(null);
	readonly assignSelectedRoles = signal<Set<string>>(new Set());
	readonly assignSelectedJobTypes = signal<Set<number>>(new Set());
	readonly assignOriginalPairs = signal<Set<string>>(new Set());
	readonly isAssignSaving = signal(false);

	// ── Allowed widget types ──
	readonly widgetTypes = ['content', 'chart', 'status-card', 'quick-action', 'workflow-pipeline', 'link-group'];

	// ── Computed: workspace groups for matrix ──
	readonly workspaceGroups = computed<WorkspaceGroup[]>(() => {
		const cats = this.categories();
		const widgetList = this.widgets();

		// Group categories by workspace
		const wsMap = new Map<string, WidgetCategoryRefDto[]>();
		for (const c of cats) {
			const existing = wsMap.get(c.workspace) || [];
			existing.push(c);
			wsMap.set(c.workspace, existing);
		}

		const groups: WorkspaceGroup[] = [];
		for (const [workspace, wsCats] of wsMap) {
			const categoryGroups: CategoryGroup[] = wsCats
				.sort((a, b) => a.defaultOrder - b.defaultOrder)
				.map(c => ({
					categoryId: c.categoryId,
					name: c.name,
					widgets: widgetList.filter(w => w.categoryId === c.categoryId),
				}))
				.filter(cg => cg.widgets.length > 0);

			if (categoryGroups.length === 0) continue;

			const totalWidgets = categoryGroups.reduce((sum, cg) => sum + cg.widgets.length, 0);
			groups.push({
				workspace,
				label: WORKSPACE_LABELS[workspace] || workspace,
				icon: wsCats[0]?.icon || null,
				widgetCount: totalWidgets,
				categories: categoryGroups,
			});
		}

		return groups;
	});

	// ── Computed: dirty detection ──
	readonly isDirty = computed(() => {
		const current = this.matrixEntries();
		const original = this.originalEntries();
		if (current.length !== original.length) return true;
		const key = (e: WidgetDefaultEntryDto) => `${e.widgetId}|${e.roleId}`;
		const currentKeys = new Set(current.map(key));
		const originalKeys = new Set(original.map(key));
		if (currentKeys.size !== originalKeys.size) return true;
		for (const k of currentKeys) {
			if (!originalKeys.has(k)) return true;
		}
		return false;
	});

	readonly changeCount = computed(() => {
		const current = this.matrixEntries();
		const original = this.originalEntries();
		const key = (e: WidgetDefaultEntryDto) => `${e.widgetId}|${e.roleId}`;
		const currentKeys = new Set(current.map(key));
		const originalKeys = new Set(original.map(key));
		let count = 0;
		for (const k of currentKeys) {
			if (!originalKeys.has(k)) count++;
		}
		for (const k of originalKeys) {
			if (!currentKeys.has(k)) count++;
		}
		return count;
	});

	// ── Form validation ──
	readonly isFormValid = computed(() =>
		this.formName().trim().length > 0 &&
		this.formWidgetType().length > 0 &&
		this.formComponentKey().trim().length > 0 &&
		this.formCategoryId() > 0
	);

	constructor() {
		this.loadReferenceData();
	}

	// ═══════════════════════════════════
	// Data loading
	// ═══════════════════════════════════

	loadReferenceData(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);

		// Load all reference data sequentially via forkJoin-like pattern
		// Using subscribe chains since these are lightweight
		this.editorService.getJobTypes().subscribe({
			next: data => this.jobTypes.set(data),
			error: err => this.handleError('Failed to load job types', err),
		});

		this.editorService.getRoles().subscribe({
			next: data => this.roles.set(data),
			error: err => this.handleError('Failed to load roles', err),
		});

		this.editorService.getCategories().subscribe({
			next: data => {
				this.categories.set(data);
				// Expand all workspaces by default
				const workspaces = new Set(data.map(c => c.workspace));
				this.expandedWorkspaces.set(workspaces);
			},
			error: err => this.handleError('Failed to load categories', err),
		});

		this.editorService.getWidgets().subscribe({
			next: data => {
				this.widgets.set(data);
				this.isLoading.set(false);
			},
			error: err => {
				this.handleError('Failed to load widgets', err);
				this.isLoading.set(false);
			},
		});
	}

	refresh(): void {
		this.loadReferenceData();
		if (this.selectedJobTypeId()) {
			this.loadMatrix(this.selectedJobTypeId());
		}
	}

	// ═══════════════════════════════════
	// Matrix operations
	// ═══════════════════════════════════

	onJobTypeChange(event: Event): void {
		const select = event.target as HTMLSelectElement;
		const jobTypeId = +select.value;
		this.selectedJobTypeId.set(jobTypeId);
		if (jobTypeId > 0) {
			this.loadMatrix(jobTypeId);
		}
	}

	private loadMatrix(jobTypeId: number): void {
		this.editorService.getDefaultsMatrix(jobTypeId).subscribe({
			next: response => {
				this.matrixEntries.set([...response.entries]);
				this.originalEntries.set([...response.entries]);
			},
			error: err => this.handleError('Failed to load defaults matrix', err),
		});
	}

	isDefaultEnabled(widgetId: number, roleId: string): boolean {
		return this.matrixEntries().some(e => e.widgetId === widgetId && e.roleId === roleId);
	}

	toggleDefault(widgetId: number, roleId: string, categoryId: number): void {
		const entries = [...this.matrixEntries()];
		const idx = entries.findIndex(e => e.widgetId === widgetId && e.roleId === roleId);

		if (idx >= 0) {
			entries.splice(idx, 1);
		} else {
			entries.push({
				widgetId,
				roleId,
				categoryId,
				displayOrder: 0,
				config: undefined,
			} as WidgetDefaultEntryDto);
		}

		this.matrixEntries.set(entries);
	}

	resetMatrix(): void {
		this.matrixEntries.set([...this.originalEntries()]);
	}

	saveMatrix(): void {
		const jobTypeId = this.selectedJobTypeId();
		if (!jobTypeId) return;

		this.isSaving.set(true);
		this.editorService.saveDefaultsMatrix({
			jobTypeId,
			entries: this.matrixEntries(),
		} as any).subscribe({
			next: () => {
				this.originalEntries.set([...this.matrixEntries()]);
				this.isSaving.set(false);
				this.toast.show('Widget defaults saved successfully.', 'success');
			},
			error: err => {
				this.isSaving.set(false);
				this.toast.show(err?.error?.message || 'Failed to save defaults.', 'danger', 4000);
			},
		});
	}

	copyFromJobType(sourceJobTypeId: number): void {
		this.showCopyMenu.set(false);
		this.editorService.getDefaultsMatrix(sourceJobTypeId).subscribe({
			next: response => {
				this.matrixEntries.set([...response.entries]);
				this.toast.show('Defaults copied. Review and save when ready.', 'info');
			},
			error: err => this.handleError('Failed to copy defaults', err),
		});
	}

	// ═══════════════════════════════════
	// Workspace accordion
	// ═══════════════════════════════════

	toggleWorkspace(workspace: string): void {
		const current = new Set(this.expandedWorkspaces());
		if (current.has(workspace)) {
			current.delete(workspace);
		} else {
			current.add(workspace);
		}
		this.expandedWorkspaces.set(current);
	}

	isWorkspaceExpanded(workspace: string): boolean {
		return this.expandedWorkspaces().has(workspace);
	}

	// ═══════════════════════════════════
	// Widget definition CRUD
	// ═══════════════════════════════════

	openAddWidget(): void {
		this.editingWidget.set(null);
		this.formName.set('');
		this.formWidgetType.set('');
		this.formComponentKey.set('');
		this.formCategoryId.set(0);
		this.formDescription.set('');
		this.showWidgetModal.set(true);
	}

	openEditWidget(widget: WidgetDefinitionDto): void {
		this.editingWidget.set(widget);
		this.formName.set(widget.name);
		this.formWidgetType.set(widget.widgetType);
		this.formComponentKey.set(widget.componentKey);
		this.formCategoryId.set(widget.categoryId);
		this.formDescription.set(widget.description || '');
		this.showWidgetModal.set(true);
	}

	closeWidgetModal(): void {
		this.showWidgetModal.set(false);
		this.editingWidget.set(null);
	}

	saveWidget(): void {
		const request = {
			name: this.formName().trim(),
			widgetType: this.formWidgetType(),
			componentKey: this.formComponentKey().trim(),
			categoryId: this.formCategoryId(),
			description: this.formDescription().trim() || undefined,
		};

		const editing = this.editingWidget();
		if (editing) {
			this.editorService.updateWidget(editing.widgetId, request as any).subscribe({
				next: () => {
					this.toast.show('Widget updated successfully.', 'success');
					this.closeWidgetModal();
					this.reloadWidgets();
				},
				error: err => this.toast.show(err?.error?.message || 'Failed to update widget.', 'danger', 4000),
			});
		} else {
			this.editorService.createWidget(request as any).subscribe({
				next: () => {
					this.toast.show('Widget created successfully.', 'success');
					this.closeWidgetModal();
					this.reloadWidgets();
				},
				error: err => this.toast.show(err?.error?.message || 'Failed to create widget.', 'danger', 4000),
			});
		}
	}

	confirmDeleteWidget(widget: WidgetDefinitionDto): void {
		this.deleteTarget.set(widget);
		this.showDeleteConfirm.set(true);
	}

	onDeleteConfirmed(): void {
		const target = this.deleteTarget();
		if (!target) return;
		this.showDeleteConfirm.set(false);

		this.editorService.deleteWidget(target.widgetId).subscribe({
			next: () => {
				this.toast.show('Widget deleted.', 'success');
				this.deleteTarget.set(null);
				this.reloadWidgets();
			},
			error: err => this.toast.show(err?.error?.message || 'Failed to delete widget.', 'danger', 4000),
		});
	}

	private reloadWidgets(): void {
		this.editorService.getWidgets().subscribe({
			next: data => this.widgets.set(data),
			error: err => this.handleError('Failed to reload widgets', err),
		});
	}

	// ═══════════════════════════════════
	// Assign Roles modal
	// ═══════════════════════════════════

	openAssignRoles(widget: WidgetDefinitionDto): void {
		this.assignTarget.set(widget);
		this.assignSelectedRoles.set(new Set());
		this.assignSelectedJobTypes.set(new Set());
		this.assignOriginalPairs.set(new Set());
		this.showAssignModal.set(true);

		// Load current assignments for this widget
		this.editorService.getWidgetAssignments(widget.widgetId).subscribe({
			next: response => {
				// Pre-populate: derive selected roles and job types from assignments
				const roles = new Set<string>();
				const jobTypeIds = new Set<number>();
				const pairs = new Set<string>();
				for (const a of response.assignments) {
					roles.add(a.roleId);
					jobTypeIds.add(a.jobTypeId);
					pairs.add(`${a.jobTypeId}|${a.roleId}`);
				}
				this.assignSelectedRoles.set(roles);
				this.assignSelectedJobTypes.set(jobTypeIds);
				this.assignOriginalPairs.set(pairs);
			},
			error: err => this.toast.show(err?.error?.message || 'Failed to load assignments.', 'danger', 4000),
		});
	}

	closeAssignModal(): void {
		this.showAssignModal.set(false);
		this.assignTarget.set(null);
	}

	toggleAssignRole(roleId: string): void {
		const current = new Set(this.assignSelectedRoles());
		if (current.has(roleId)) {
			current.delete(roleId);
		} else {
			current.add(roleId);
		}
		this.assignSelectedRoles.set(current);
	}

	toggleAssignJobType(jobTypeId: number): void {
		const current = new Set(this.assignSelectedJobTypes());
		if (current.has(jobTypeId)) {
			current.delete(jobTypeId);
		} else {
			current.add(jobTypeId);
		}
		this.assignSelectedJobTypes.set(current);
	}

	toggleAllJobTypes(): void {
		const all = this.jobTypes();
		const current = this.assignSelectedJobTypes();
		if (current.size === all.length) {
			this.assignSelectedJobTypes.set(new Set());
		} else {
			this.assignSelectedJobTypes.set(new Set(all.map(jt => jt.jobTypeId)));
		}
	}

	allJobTypesSelected(): boolean {
		return this.assignSelectedJobTypes().size === this.jobTypes().length && this.jobTypes().length > 0;
	}

	assignmentCount(): number {
		return this.assignSelectedRoles().size * this.assignSelectedJobTypes().size;
	}

	saveAssignments(): void {
		const widget = this.assignTarget();
		if (!widget) return;

		const roles = this.assignSelectedRoles();
		const jobTypeIds = this.assignSelectedJobTypes();

		// Build the cross-product: every selected role × every selected job type
		const assignments: WidgetAssignmentDto[] = [];
		for (const jobTypeId of jobTypeIds) {
			for (const roleId of roles) {
				assignments.push({ jobTypeId, roleId } as WidgetAssignmentDto);
			}
		}

		this.isAssignSaving.set(true);
		this.editorService.saveWidgetAssignments({
			widgetId: widget.widgetId,
			categoryId: widget.categoryId,
			assignments,
		} as any).subscribe({
			next: () => {
				this.isAssignSaving.set(false);
				this.toast.show(
					`Assigned ${widget.name} to ${roles.size} role(s) across ${jobTypeIds.size} job type(s).`,
					'success',
				);
				this.closeAssignModal();
				// Refresh matrix if viewing one of the affected job types
				if (this.selectedJobTypeId() && jobTypeIds.has(this.selectedJobTypeId())) {
					this.loadMatrix(this.selectedJobTypeId());
				}
			},
			error: err => {
				this.isAssignSaving.set(false);
				this.toast.show(err?.error?.message || 'Failed to save assignments.', 'danger', 4000);
			},
		});
	}

	// ═══════════════════════════════════
	// Helpers
	// ═══════════════════════════════════

	abbreviateRole(roleName: string): string {
		return ROLE_ABBREVIATIONS[roleName] || roleName.slice(0, 3);
	}

	asInputValue(event: Event): string {
		return (event.target as HTMLInputElement).value;
	}

	asSelectValue(event: Event): string {
		return (event.target as HTMLSelectElement).value;
	}

	private handleError(context: string, err: any): void {
		const message = err?.error?.message || err?.message || context;
		this.errorMessage.set(message);
	}
}
