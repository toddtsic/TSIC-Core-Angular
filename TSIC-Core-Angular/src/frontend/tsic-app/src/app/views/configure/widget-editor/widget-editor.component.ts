import { Component, inject, signal, computed, ChangeDetectionStrategy, isDevMode } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { WidgetEditorService } from './services/widget-editor.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { WIDGET_MANIFEST } from '@widgets/widget-registry';
import { Workspaces } from '@widgets/workspace.constants';
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
	[Workspaces.Public]: 'Public',
	[Workspaces.Dashboard]: 'Dashboard',
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
	readonly activeTab = signal<'definitions' | 'overrides' | 'matrix'>('definitions');
	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly isDevMode = isDevMode();

	// ── Matrix state ──
	readonly selectedJobTypeId = signal<number>(0);
	readonly matrixEntries = signal<WidgetDefaultEntryDto[]>([]);
	readonly originalEntries = signal<WidgetDefaultEntryDto[]>([]);
	readonly expandedWorkspaces = signal<Set<string>>(new Set());
	readonly showCopyMenu = signal(false);

	// ── Category order state ──
	readonly categoryOrderDirty = signal(false);
	readonly isSavingCategoryOrder = signal(false);

	// ── Widget CRUD modal state ──
	readonly showWidgetModal = signal(false);
	readonly editingWidget = signal<WidgetDefinitionDto | null>(null);
	readonly formName = signal('');
	readonly formWidgetType = signal('');
	readonly formComponentKey = signal('');
	readonly formCategoryId = signal(0);
	readonly formDescription = signal('');
	readonly formConfigIcon = signal('');
	readonly formDisplayStyle = signal('');
	/** True when user selected "Other (custom key)" in the dropdown */
	readonly useCustomKey = signal(false);

	// ── Manifest-driven intelligence ──
	readonly manifestKeys = Object.keys(WIDGET_MANIFEST);

	/** Manifest keys that have no matching Widget definition in the database */
	readonly uncoveredKeys = computed(() => {
		const dbKeys = new Set(this.widgets().map(w => w.componentKey));
		return this.manifestKeys.filter(k => !dbKeys.has(k));
	});

	// ── Delete confirm state ──
	readonly showDeleteConfirm = signal(false);
	readonly deleteTarget = signal<WidgetDefinitionDto | null>(null);

	// ── Role assignment state (embedded in Edit Widget modal) ──
	readonly assignSelectedRoles = signal<Set<string>>(new Set());
	readonly assignSelectedJobTypes = signal<Set<number>>(new Set());
	readonly assignSectionExpanded = signal(true);
	readonly isLoadingAssignments = signal(false);

	/**
	 * True when the currently-selected form category belongs to the 'public' workspace.
	 * Public widgets apply to every user regardless of role, so the Roles picker is
	 * hidden and assignments are stored with RoleId = null.
	 */
	readonly isPublicCategory = computed(() => {
		const catId = this.formCategoryId();
		const cat = this.categories().find(c => c.categoryId === catId);
		return cat?.workspace === Workspaces.Public;
	});

	// ── Job Override state ──
	readonly overrideSelectedJobTypeId = signal<number>(0);
	readonly overrideJobs = signal<JobRefDto[]>([]);
	readonly overrideSelectedJobId = signal<string>('');
	readonly overrideEntries = signal<JobWidgetEntryDto[]>([]);
	readonly overrideOriginalEntries = signal<JobWidgetEntryDto[]>([]);
	readonly isOverrideLoading = signal(false);
	readonly isOverrideSaving = signal(false);

	// ── Export SQL dialog state ──
	readonly exportDialogOpen = signal(false);
	readonly exportedSql = signal('');
	readonly exportLoading = signal(false);
	readonly copySuccess = signal(false);

	// ── Definitions sort state ──
	readonly defSortColumn = signal<keyof WidgetDefinitionDto>('name');
	readonly defSortDirection = signal<'asc' | 'desc'>('asc');

	// ── Allowed widget types ──
	readonly widgetTypes = ['content', 'chart-tile', 'status-tile'];

	/** Valid displayStyle options per WidgetType */
	readonly displayStyleOptions: Record<string, string[]> = {
		'content':      ['banner', 'feed', 'block'],
		'chart-tile':   ['standard', 'wide', 'spark'],
		'status-tile':  ['standard', 'hero', 'compact'],
	};

	// ── Computed: workspace groups for matrix ──
	// Public-workspace widgets are rendered in a dedicated panel above the accordion
	// (see publicWidgetRows); they no longer appear here.
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

		// Group categories by workspace (exclude public — rendered separately)
		const wsMap = new Map<string, WidgetCategoryRefDto[]>();
		for (const c of cats) {
			if (c.workspace === Workspaces.Public) continue;
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

	/**
	 * Public-workspace widgets shown in the dedicated panel above the role × jobtype
	 * accordion. The matrix is scoped to one JobType, so each widget is a single
	 * on/off toggle (presence of a null-role WidgetDefault row).
	 */
	readonly publicWidgetRows = computed(() => {
		const publicCategoryIds = new Set(
			this.categories().filter(c => c.workspace === Workspaces.Public).map(c => c.categoryId)
		);
		return this.widgets()
			.filter(w => publicCategoryIds.has(w.categoryId))
			.sort((a, b) => a.name.localeCompare(b.name));
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

	/** True when a null-role (public) default exists for this widget in the loaded jobtype. */
	isPublicDefaultEnabled(widgetId: number): boolean {
		return this.matrixEntries().some(e => e.widgetId === widgetId && e.roleId == null);
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

	/**
	 * Toggle a public widget on/off for the loaded jobtype. Adds or removes a
	 * WidgetDefault row with RoleId = null (single row per jobtype for public widgets).
	 */
	togglePublicDefault(widgetId: number, categoryId: number): void {
		const entries = [...this.matrixEntries()];
		const idx = entries.findIndex(e => e.widgetId === widgetId && e.roleId == null);

		if (idx >= 0) {
			entries.splice(idx, 1);
		} else {
			const maxOrder = entries
				.filter(e => e.categoryId === categoryId)
				.reduce((max, e) => Math.max(max, e.displayOrder), -1);
			const widget = this.widgets().find(w => w.widgetId === widgetId);
			entries.push({
				widgetId,
				roleId: null,
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
	// Category ordering
	// ═══════════════════════════════════

	onCategoryDrop(event: CdkDragDrop<CategoryGroup[]>, workspace: string): void {
		if (event.previousIndex === event.currentIndex) return;

		// Get this workspace's categories from the current categories signal
		const allCats = this.categories().slice();
		const wsCats = allCats
			.filter(c => c.workspace === workspace)
			.sort((a, b) => a.defaultOrder - b.defaultOrder);

		// Reorder
		moveItemInArray(wsCats, event.previousIndex, event.currentIndex);

		// Reassign defaultOrder by position index
		const updatedCats = allCats.map(c => {
			if (c.workspace !== workspace) return c;
			const newIdx = wsCats.findIndex(wc => wc.categoryId === c.categoryId);
			return newIdx >= 0 ? { ...c, defaultOrder: newIdx } : c;
		});

		this.categories.set(updatedCats);
		this.categoryOrderDirty.set(true);
	}

	saveCategoryOrder(): void {
		const entries = this.categories().map(c => ({
			categoryId: c.categoryId,
			defaultOrder: c.defaultOrder,
		}));

		this.isSavingCategoryOrder.set(true);
		this.editorService.saveCategoryOrder(entries).subscribe({
			next: () => {
				this.isSavingCategoryOrder.set(false);
				this.categoryOrderDirty.set(false);
				this.toast.show('Category order saved successfully.', 'success');
			},
			error: err => {
				this.isSavingCategoryOrder.set(false);
				this.toast.show(err?.error?.message || 'Failed to save category order.', 'danger', 4000);
			},
		});
	}

	resetCategoryOrder(): void {
		this.editorService.getCategories().subscribe({
			next: data => {
				this.categories.set(data);
				this.categoryOrderDirty.set(false);
			},
			error: err => this.handleError('Failed to reload categories', err),
		});
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
		this.formDisplayStyle.set('');
		this.useCustomKey.set(false);
		// Reset assignment state for new widget
		this.assignSelectedRoles.set(new Set());
		this.assignSelectedJobTypes.set(new Set());
		this.assignSectionExpanded.set(true);
		this.isLoadingAssignments.set(false);
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
		this.useCustomKey.set(!WIDGET_MANIFEST[widget.componentKey]);
		// Load existing assignments
		this.assignSelectedRoles.set(new Set());
		this.assignSelectedJobTypes.set(new Set());
		this.assignSectionExpanded.set(true);
		this.isLoadingAssignments.set(true);
		this.showWidgetModal.set(true);

		this.editorService.getWidgetAssignments(widget.widgetId).subscribe({
			next: response => {
				const roles = new Set<string>();
				const jobTypeIds = new Set<number>();
				for (const a of response.assignments) {
					// Public widgets store roleId = null; skip role chips for them.
					if (a.roleId != null) roles.add(a.roleId);
					jobTypeIds.add(a.jobTypeId);
				}
				this.assignSelectedRoles.set(roles);
				this.assignSelectedJobTypes.set(jobTypeIds);
				this.isLoadingAssignments.set(false);
			},
			error: () => {
				this.isLoadingAssignments.set(false);
				this.toast.show('Failed to load role assignments.', 'warning', 3000);
			},
		});
	}

	/** Handle Component Key dropdown selection — auto-fills form from manifest */
	onComponentKeySelect(value: string): void {
		if (value === '__custom__') {
			this.useCustomKey.set(true);
			this.formComponentKey.set('');
			return;
		}
		this.useCustomKey.set(false);
		this.formComponentKey.set(value);
		const entry = WIDGET_MANIFEST[value];
		if (!entry) return;

		this.formName.set(entry.label);
		this.formWidgetType.set(entry.widgetType);
		this.formDescription.set(entry.description || '');
		this.formConfigIcon.set(entry.icon);
		this.formDisplayStyle.set(entry.displayStyle || '');

		// Best-effort category match by workspace
		const cat = this.categories().find(c => c.workspace === entry.workspace);
		if (cat) this.formCategoryId.set(cat.categoryId);
	}

	/** One-click: open Add Widget modal pre-filled from manifest */
	createFromManifest(key: string): void {
		this.openAddWidget();
		this.onComponentKeySelect(key);
	}

	/** Get the manifest icon for a component key (used by uncovered panel) */
	getManifestIcon(key: string): string {
		return WIDGET_MANIFEST[key]?.icon || 'bi-puzzle';
	}


	// ═══════════════════════════════════
	// Export SQL
	// ═══════════════════════════════════

	exportSql(): void {
		this.exportLoading.set(true);
		this.exportedSql.set('');
		this.copySuccess.set(false);
		this.exportDialogOpen.set(true);

		this.editorService.exportSql().subscribe({
			next: (sql) => {
				this.exportedSql.set(sql);
				this.exportLoading.set(false);
			},
			error: () => {
				this.exportedSql.set('-- Error generating SQL export');
				this.exportLoading.set(false);
			},
		});
	}

	copyExportToClipboard(): void {
		navigator.clipboard.writeText(this.exportedSql()).then(() => {
			this.copySuccess.set(true);
			setTimeout(() => this.copySuccess.set(false), 2000);
		});
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
				next: () => this.saveAssignmentsForWidget(editing.widgetId, editing.categoryId),
				error: err => this.toast.show(err?.error?.message || 'Failed to update widget.', 'danger', 4000),
			});
		} else {
			this.editorService.createWidget(request as any).subscribe({
				next: (created: WidgetDefinitionDto) => this.saveAssignmentsForWidget(created.widgetId, created.categoryId),
				error: err => this.toast.show(err?.error?.message || 'Failed to create widget.', 'danger', 4000),
			});
		}
	}

	/** Saves role assignments after widget create/update succeeds. */
	private saveAssignmentsForWidget(widgetId: number, categoryId: number): void {
		const roles = this.assignSelectedRoles();
		const jobTypeIds = this.assignSelectedJobTypes();
		const isPublic = this.isPublicCategory();
		const verb = this.editingWidget() ? 'updated' : 'created';

		// Need at least one job type. Public widgets don't need roles (one row per
		// jobType with RoleId = null); role-scoped widgets need both.
		const missing = jobTypeIds.size === 0 || (!isPublic && roles.size === 0);
		if (missing) {
			this.toast.show(`Widget ${verb} successfully.`, 'success');
			this.closeWidgetModal();
			this.reloadWidgets();
			return;
		}

		// Public: one assignment per jobType, RoleId = null.
		// Role-scoped: cross-product of roles × jobTypes.
		const assignments: WidgetAssignmentDto[] = [];
		for (const jobTypeId of jobTypeIds) {
			if (isPublic) {
				assignments.push({ jobTypeId, roleId: null });
			} else {
				for (const roleId of roles) {
					assignments.push({ jobTypeId, roleId });
				}
			}
		}

		const successMsg = isPublic
			? `Widget ${verb} and assigned to ${jobTypeIds.size} job type(s) (public — all roles).`
			: `Widget ${verb} and assigned to ${roles.size} role(s) across ${jobTypeIds.size} job type(s).`;

		this.editorService.saveWidgetAssignments({
			widgetId,
			categoryId,
			assignments,
		} as any).subscribe({
			next: () => {
				this.toast.show(successMsg, 'success');
				this.closeWidgetModal();
				this.reloadWidgets();
				// Refresh matrix if viewing an affected job type
				if (this.selectedJobTypeId() && jobTypeIds.has(this.selectedJobTypeId())) {
					this.loadMatrix(this.selectedJobTypeId());
				}
			},
			error: () => {
				this.toast.show(
					`Widget ${verb}, but role assignments failed. Edit the widget to retry.`,
					'warning', 5000,
				);
				this.closeWidgetModal();
				this.reloadWidgets();
			},
		});
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
		if (this.isPublicCategory()) return this.assignSelectedJobTypes().size;
		return this.assignSelectedRoles().size * this.assignSelectedJobTypes().size;
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
			this.formDisplayStyle.set('');
			return;
		}
		try {
			const obj = JSON.parse(json);
			this.formConfigIcon.set(obj.icon || '');
			this.formDisplayStyle.set(obj.displayStyle || '');
		} catch {
			this.formConfigIcon.set('');
			this.formDisplayStyle.set('');
		}
	}

	private serializeConfigFromFields(): string | null {
		const icon = this.formConfigIcon().trim();
		const displayStyle = this.formDisplayStyle().trim();
		if (!icon && !displayStyle) return null;
		const obj: Record<string, string> = {};
		obj['label'] = this.formName().trim();
		if (icon) obj['icon'] = icon;
		if (displayStyle) obj['displayStyle'] = displayStyle;
		return JSON.stringify(obj);
	}

	private handleError(context: string, err: any): void {
		const message = err?.error?.message || err?.message || context;
		this.errorMessage.set(message);
	}
}
