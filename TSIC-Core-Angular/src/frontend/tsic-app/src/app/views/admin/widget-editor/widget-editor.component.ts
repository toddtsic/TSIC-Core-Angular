import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
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
	JobRefDto,
	JobWidgetEntryDto,
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
	imports: [CommonModule, DragDropModule, TsicDialogComponent, ConfirmDialogComponent],
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
	readonly activeTab = signal<'matrix' | 'definitions' | 'overrides'>('matrix');
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
	readonly formConfigIcon = signal('');
	readonly formConfigRoute = signal('');
	readonly formDisplayStyle = signal('');

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

	// ── Job Override state ──
	readonly overrideSelectedJobTypeId = signal<number>(0);
	readonly overrideJobs = signal<JobRefDto[]>([]);
	readonly overrideSelectedJobId = signal<string>('');
	readonly overrideEntries = signal<JobWidgetEntryDto[]>([]);
	readonly overrideOriginalEntries = signal<JobWidgetEntryDto[]>([]);
	readonly isOverrideLoading = signal(false);
	readonly isOverrideSaving = signal(false);

	// ── Definitions sort state ──
	readonly defSortColumn = signal<keyof WidgetDefinitionDto>('name');
	readonly defSortDirection = signal<'asc' | 'desc'>('asc');

	// ── Allowed widget types ──
	readonly widgetTypes = ['content', 'chart-tile', 'status-tile', 'link-tile'];

	/** Valid displayStyle options per WidgetType */
	readonly displayStyleOptions: Record<string, string[]> = {
		'content':      ['banner', 'feed', 'block'],
		'chart-tile':   ['standard', 'wide', 'spark'],
		'status-tile':  ['standard', 'hero', 'compact'],
		'link-tile':    ['standard', 'hero', 'compact'],
	};

	// ── Computed: workspace groups for matrix ──
	readonly workspaceGroups = computed<WorkspaceGroup[]>(() => {
		const cats = this.categories();
		const widgetList = this.widgets();
		const entries = this.matrixEntries();

		// Build displayOrder lookup: widgetId → min displayOrder from matrixEntries
		const orderMap = new Map<number, number>();
		for (const e of entries) {
			const existing = orderMap.get(e.widgetId);
			if (existing === undefined || e.displayOrder < existing) {
				orderMap.set(e.widgetId, e.displayOrder);
			}
		}

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
					widgets: widgetList
						.filter(w => w.categoryId === c.categoryId)
						.sort((a, b) => {
							const oa = orderMap.get(a.widgetId) ?? Number.MAX_SAFE_INTEGER;
							const ob = orderMap.get(b.widgetId) ?? Number.MAX_SAFE_INTEGER;
							return oa !== ob ? oa - ob : a.name.localeCompare(b.name);
						}),
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
		const currentMap = new Map(current.map(e => [key(e), e]));
		const originalMap = new Map(original.map(e => [key(e), e]));
		if (currentMap.size !== originalMap.size) return true;
		for (const [k, ce] of currentMap) {
			const oe = originalMap.get(k);
			if (!oe) return true;
			if (ce.displayOrder !== oe.displayOrder) return true;
		}
		return false;
	});

	readonly changeCount = computed(() => {
		const current = this.matrixEntries();
		const original = this.originalEntries();
		const key = (e: WidgetDefaultEntryDto) => `${e.widgetId}|${e.roleId}`;
		const currentMap = new Map(current.map(e => [key(e), e]));
		const originalMap = new Map(original.map(e => [key(e), e]));
		let count = 0;
		// Count added entries
		for (const k of currentMap.keys()) {
			if (!originalMap.has(k)) count++;
		}
		// Count removed entries
		for (const k of originalMap.keys()) {
			if (!currentMap.has(k)) count++;
		}
		// Count reordered entries
		for (const [k, ce] of currentMap) {
			const oe = originalMap.get(k);
			if (oe && ce.displayOrder !== oe.displayOrder) count++;
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

	// ── Computed: sorted widget definitions ──
	readonly sortedWidgets = computed(() => {
		const col = this.defSortColumn();
		const dir = this.defSortDirection();
		return this.widgets().slice().sort((a, b) => {
			const va = String(a[col] ?? '').toLowerCase();
			const vb = String(b[col] ?? '').toLowerCase();
			const cmp = va.localeCompare(vb);
			return dir === 'asc' ? cmp : -cmp;
		});
	});

	// ── Computed: workspace groups for override matrix ──
	readonly overrideWorkspaceGroups = computed<WorkspaceGroup[]>(() => {
		const cats = this.categories();
		const widgetList = this.widgets();
		const entries = this.overrideEntries();

		// Build displayOrder lookup from override entries
		const orderMap = new Map<number, number>();
		for (const e of entries) {
			const existing = orderMap.get(e.widgetId);
			if (existing === undefined || e.displayOrder < existing) {
				orderMap.set(e.widgetId, e.displayOrder);
			}
		}

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
					widgets: widgetList
						.filter(w => w.categoryId === c.categoryId)
						.sort((a, b) => {
							const oa = orderMap.get(a.widgetId) ?? Number.MAX_SAFE_INTEGER;
							const ob = orderMap.get(b.widgetId) ?? Number.MAX_SAFE_INTEGER;
							return oa !== ob ? oa - ob : a.name.localeCompare(b.name);
						}),
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

	// ── Computed: override dirty detection ──
	readonly isOverrideDirty = computed(() => {
		const current = this.overrideEntries();
		const original = this.overrideOriginalEntries();
		if (current.length !== original.length) return true;
		const key = (e: JobWidgetEntryDto) => `${e.widgetId}|${e.roleId}`;
		const currentMap = new Map(current.map(e => [key(e), e]));
		const originalMap = new Map(original.map(e => [key(e), e]));
		if (currentMap.size !== originalMap.size) return true;
		for (const [k, ce] of currentMap) {
			const oe = originalMap.get(k);
			if (!oe) return true;
			if (ce.isEnabled !== oe.isEnabled) return true;
			if (ce.isOverridden !== oe.isOverridden) return true;
			if (ce.displayOrder !== oe.displayOrder) return true;
		}
		return false;
	});

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
			// Append at end: max displayOrder in this category + 1
			const maxOrder = entries
				.filter(e => e.categoryId === categoryId)
				.reduce((max, e) => Math.max(max, e.displayOrder), -1);
			// Inherit DefaultConfig from the widget definition
			const widget = this.widgets().find(w => w.widgetId === widgetId);
			entries.push({
				widgetId,
				roleId,
				categoryId,
				displayOrder: maxOrder + 1,
				config: widget?.defaultConfig ?? undefined,
			} as WidgetDefaultEntryDto);
		}

		this.matrixEntries.set(entries);
	}

	resetMatrix(): void {
		this.matrixEntries.set([...this.originalEntries()]);
	}

	onWidgetDrop(event: CdkDragDrop<CategoryGroup>, categoryId: number): void {
		if (event.previousIndex === event.currentIndex) return;

		// Get current category's widgets (already sorted by displayOrder via computed)
		const group = this.workspaceGroups()
			.flatMap(ws => ws.categories)
			.find(c => c.categoryId === categoryId);
		if (!group) return;

		const reorderedWidgets = group.widgets.slice();
		moveItemInArray(reorderedWidgets, event.previousIndex, event.currentIndex);

		// Reassign displayOrder for ALL matrixEntries matching these widgets in this category
		const entries = this.matrixEntries().map(entry => {
			if (entry.categoryId !== categoryId) return entry;
			const newOrder = reorderedWidgets.findIndex(w => w.widgetId === entry.widgetId);
			if (newOrder < 0) return entry;
			return { ...entry, displayOrder: newOrder };
		});

		this.matrixEntries.set(entries);
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
		this.formConfigIcon.set('');
		this.formConfigRoute.set('');
		this.formDisplayStyle.set('');
		this.showWidgetModal.set(true);
	}

	openEditWidget(widget: WidgetDefinitionDto): void {
		this.editingWidget.set(widget);
		this.formName.set(widget.name);
		this.formWidgetType.set(widget.widgetType);
		this.formComponentKey.set(widget.componentKey);
		this.formCategoryId.set(widget.categoryId);
		this.formDescription.set(widget.description || '');
		this.parseConfigToFields(widget.defaultConfig);
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
			defaultConfig: this.serializeConfigFromFields() || undefined,
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
	// Job Overrides
	// ═══════════════════════════════════

	onOverrideJobTypeChange(event: Event): void {
		const jobTypeId = +(event.target as HTMLSelectElement).value;
		this.overrideSelectedJobTypeId.set(jobTypeId);
		this.overrideSelectedJobId.set('');
		this.overrideEntries.set([]);
		this.overrideOriginalEntries.set([]);
		if (jobTypeId > 0) {
			this.editorService.getJobsByJobType(jobTypeId).subscribe({
				next: jobs => this.overrideJobs.set(jobs),
				error: err => this.handleError('Failed to load jobs', err),
			});
		} else {
			this.overrideJobs.set([]);
		}
	}

	onOverrideJobChange(event: Event): void {
		const jobId = (event.target as HTMLSelectElement).value;
		this.overrideSelectedJobId.set(jobId);
		if (jobId) {
			this.loadJobOverrides(jobId);
		}
	}

	private loadJobOverrides(jobId: string): void {
		this.isOverrideLoading.set(true);
		this.editorService.getJobOverrides(jobId).subscribe({
			next: response => {
				this.overrideEntries.set([...response.entries]);
				this.overrideOriginalEntries.set([...response.entries]);
				this.isOverrideLoading.set(false);
			},
			error: err => {
				this.handleError('Failed to load job overrides', err);
				this.isOverrideLoading.set(false);
			},
		});
	}

	isOverrideEnabled(widgetId: number, roleId: string): boolean {
		const entry = this.overrideEntries().find(
			e => e.widgetId === widgetId && e.roleId === roleId);
		return entry ? entry.isEnabled : false;
	}

	isOverrideEntry(widgetId: number, roleId: string): boolean {
		const entry = this.overrideEntries().find(
			e => e.widgetId === widgetId && e.roleId === roleId);
		return entry?.isOverridden ?? false;
	}

	isAdditionEntry(widgetId: number, roleId: string): boolean {
		const entry = this.overrideEntries().find(
			e => e.widgetId === widgetId && e.roleId === roleId);
		if (!entry?.isOverridden || !entry.isEnabled) return false;
		// It's an addition if there was no inherited entry for this widget+role
		const original = this.overrideOriginalEntries().find(
			e => e.widgetId === widgetId && e.roleId === roleId);
		return !original || original.isOverridden;
	}

	isWidgetOverridden(widgetId: number): boolean {
		return this.overrideEntries().some(
			e => e.widgetId === widgetId && e.isOverridden);
	}

	hasAnyOverrides(): boolean {
		return this.overrideEntries().some(e => e.isOverridden);
	}

	toggleOverride(widgetId: number, roleId: string, categoryId: number): void {
		const entries = this.overrideEntries().slice();
		const idx = entries.findIndex(
			e => e.widgetId === widgetId && e.roleId === roleId);

		if (idx >= 0) {
			const entry = entries[idx];
			if (entry.isOverridden) {
				// Already an override — toggle isEnabled
				entries[idx] = { ...entry, isEnabled: !entry.isEnabled };
			} else {
				// Inherited default — create override to disable it
				entries[idx] = { ...entry, isOverridden: true, isEnabled: false };
			}
		} else {
			// Not in defaults — add as job-specific addition
			const maxOrder = entries
				.filter(e => e.categoryId === categoryId)
				.reduce((max, e) => Math.max(max, e.displayOrder), -1);
			// Inherit DefaultConfig from the widget definition
			const widget = this.widgets().find(w => w.widgetId === widgetId);
			entries.push({
				widgetId, roleId, categoryId,
				displayOrder: maxOrder + 1,
				config: widget?.defaultConfig ?? undefined,
				isEnabled: true,
				isOverridden: true,
			} as JobWidgetEntryDto);
		}

		this.overrideEntries.set(entries);
	}

	revertOverride(event: MouseEvent, widgetId: number, roleId: string): void {
		event.preventDefault(); // suppress context menu
		const entries = this.overrideEntries().slice();
		const idx = entries.findIndex(
			e => e.widgetId === widgetId && e.roleId === roleId);
		if (idx < 0) return;

		const entry = entries[idx];
		if (!entry.isOverridden) return; // already inherited — nothing to revert

		// Check if there was an inherited original entry
		const original = this.overrideOriginalEntries().find(
			e => e.widgetId === widgetId && e.roleId === roleId && !e.isOverridden);
		if (original) {
			// Revert to inherited state
			entries[idx] = { ...original };
		} else {
			// Was a job-specific addition — remove entirely
			entries.splice(idx, 1);
		}

		this.overrideEntries.set(entries);
	}

	resetOverrides(): void {
		this.overrideEntries.set([...this.overrideOriginalEntries()]);
	}

	resetAllOverrides(): void {
		// Remove all overrides — revert everything to inherited defaults
		const entries = this.overrideEntries().map(e =>
			e.isOverridden ? { ...e, isOverridden: false, isEnabled: true } : e
		);
		this.overrideEntries.set(entries);
	}

	saveOverrides(): void {
		const jobId = this.overrideSelectedJobId();
		if (!jobId) return;

		this.isOverrideSaving.set(true);
		this.editorService.saveJobOverrides({
			jobId,
			entries: this.overrideEntries(),
		} as any).subscribe({
			next: () => {
				this.overrideOriginalEntries.set([...this.overrideEntries()]);
				this.isOverrideSaving.set(false);
				this.toast.show('Job overrides saved successfully.', 'success');
			},
			error: err => {
				this.isOverrideSaving.set(false);
				this.toast.show(err?.error?.message || 'Failed to save overrides.', 'danger', 4000);
			},
		});
	}

	onOverrideWidgetDrop(event: CdkDragDrop<CategoryGroup>, categoryId: number): void {
		if (event.previousIndex === event.currentIndex) return;

		const group = this.overrideWorkspaceGroups()
			.flatMap(ws => ws.categories)
			.find(c => c.categoryId === categoryId);
		if (!group) return;

		const reorderedWidgets = group.widgets.slice();
		moveItemInArray(reorderedWidgets, event.previousIndex, event.currentIndex);

		// Reassign displayOrder and mark as overridden
		const entries = this.overrideEntries().map(entry => {
			if (entry.categoryId !== categoryId) return entry;
			const newOrder = reorderedWidgets.findIndex(w => w.widgetId === entry.widgetId);
			if (newOrder < 0) return entry;
			return { ...entry, displayOrder: newOrder, isOverridden: true };
		});

		this.overrideEntries.set(entries);
	}

	// ═══════════════════════════════════
	// Helpers
	// ═══════════════════════════════════

	toggleDefSort(column: keyof WidgetDefinitionDto): void {
		if (this.defSortColumn() === column) {
			this.defSortDirection.set(this.defSortDirection() === 'asc' ? 'desc' : 'asc');
		} else {
			this.defSortColumn.set(column);
			this.defSortDirection.set('asc');
		}
	}

	abbreviateRole(roleName: string): string {
		return ROLE_ABBREVIATIONS[roleName] || roleName.slice(0, 3);
	}

	asInputValue(event: Event): string {
		return (event.target as HTMLInputElement).value;
	}

	asSelectValue(event: Event): string {
		return (event.target as HTMLSelectElement).value;
	}

	private parseConfigToFields(json: string | null | undefined): void {
		if (!json) {
			this.formConfigIcon.set('');
			this.formConfigRoute.set('');
			this.formDisplayStyle.set('');
			return;
		}
		try {
			const obj = JSON.parse(json);
			this.formConfigIcon.set(obj.icon || '');
			this.formConfigRoute.set(obj.route || '');
			this.formDisplayStyle.set(obj.displayStyle || '');
		} catch {
			this.formConfigIcon.set('');
			this.formConfigRoute.set('');
			this.formDisplayStyle.set('');
		}
	}

	private serializeConfigFromFields(): string | null {
		const icon = this.formConfigIcon().trim();
		const route = this.formConfigRoute().trim();
		const displayStyle = this.formDisplayStyle().trim();
		if (!icon && !route && !displayStyle) return null;
		const obj: Record<string, string> = {};
		obj['label'] = this.formName().trim();
		if (icon) obj['icon'] = icon;
		if (route) obj['route'] = route;
		if (displayStyle) obj['displayStyle'] = displayStyle;
		return JSON.stringify(obj);
	}

	private handleError(context: string, err: any): void {
		const message = err?.error?.message || err?.message || context;
		this.errorMessage.set(message);
	}
}
