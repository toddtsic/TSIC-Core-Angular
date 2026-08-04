import { Component, inject, signal, computed, ChangeDetectionStrategy, output } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { environment } from '@environments/environment';
import { ToastService } from '@shared-ui/toast.service';
import { JobConfigService } from '../job/job-config.service';
import { HasUnsavedChanges } from '../../../infrastructure/guards/unsaved-changes.guard';
import type { JobDdlOptionsDto } from '@core/api';

// ── Category metadata (data-driven rendering) ──

interface DdlCategory {
	key: keyof JobDdlOptionsDto;
	label: string;
	group: 'clothing' | 'coach' | 'player' | 'team' | 'camp';
}

interface DdlGroup {
	key: string;
	label: string;
	categories: DdlCategory[];
}

const CATEGORIES: DdlCategory[] = [
	// Clothing Sizes
	{ key: 'jerseySizes',      label: 'Jersey Sizes',      group: 'clothing' },
	{ key: 'shortsSizes',      label: 'Shorts Sizes',      group: 'clothing' },
	{ key: 'reversibleSizes',  label: 'Reversible Sizes',  group: 'clothing' },
	{ key: 'kiltSizes',        label: 'Kilt Sizes',        group: 'clothing' },
	{ key: 'tShirtSizes',      label: 'T-Shirt Sizes',     group: 'clothing' },
	{ key: 'glovesSizes',      label: 'Gloves Sizes',      group: 'clothing' },
	{ key: 'sweatshirtSizes',  label: 'Sweatshirt Sizes',  group: 'clothing' },
	{ key: 'shoesSizes',       label: 'Shoes Sizes',       group: 'clothing' },

	// Clothing Sizes (Adult / Coach) — namespaced apart from the player sizes above
	{ key: 'coachJerseySizes', label: 'Coach Shirt Sizes', group: 'coach' },
	{ key: 'coachShortsSizes', label: 'Coach Shorts Sizes', group: 'coach' },
	{ key: 'coachWaistSizes',  label: 'Coach Waist Sizes',  group: 'coach' },
	{ key: 'coachShoesSizes',  label: 'Coach Shoe Sizes',   group: 'coach' },

	// Player Data
	{ key: 'yearsExperience',      label: 'Years Experience',      group: 'player' },
	{ key: 'positions',            label: 'Positions',              group: 'player' },
	{ key: 'gradYears',            label: 'Grad Years',             group: 'player' },
	{ key: 'recruitingGradYears',  label: 'Recruiting Grad Years',  group: 'player' },
	{ key: 'schoolGrades',         label: 'School Grades',          group: 'player' },
	{ key: 'strongHand',           label: 'Strong Hand',            group: 'player' },
	{ key: 'whoReferred',          label: 'Who Referred',           group: 'player' },
	{ key: 'heightInches',         label: 'Height (Inches)',        group: 'player' },
	{ key: 'skillLevels',          label: 'Skill Levels',           group: 'player' },

	// Team & Context
	{ key: 'lops',              label: 'LOPs (Team Reg Form)',   group: 'team' },
	{ key: 'clubNames',         label: 'Club Names',              group: 'team' },
	{ key: 'priorSeasonYears',  label: 'Prior Season Years',      group: 'team' },

	// Camp Roster
	{ key: 'dayGroups',         label: 'Day Groups',              group: 'camp' },
	{ key: 'nightGroups',       label: 'Night Groups',            group: 'camp' },
];

const GROUP_LABELS: Record<string, string> = {
	clothing: 'Clothing Sizes',
	coach:    'Clothing Sizes (Adult / Coach)',
	player:   'Player Data',
	team:     'Team & Context',
	camp:     'Camp Roster',
};

@Component({
	selector: 'app-ddl-options',
	standalone: true,
	imports: [FormsModule, DragDropModule],
	templateUrl: './ddl-options.component.html',
	styleUrl: './ddl-options.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DdlOptionsComponent implements HasUnsavedChanges {
	private readonly http = inject(HttpClient);
	private readonly toast = inject(ToastService);
	private readonly apiUrl = `${environment.apiUrl}/job-ddl-options`;

	// Provided by the Job Settings shell. Every tab must register its save with the
	// shell's FAB — otherwise the FAB runs the PREVIOUS tab's save (and its
	// loadConfig() teardown wipes this component's unsaved edits). Optional so the
	// component can still mount outside the shell.
	private readonly jobConfigSvc = inject(JobConfigService, { optional: true });

	// AM-051: in-shell the FAB is the ONE save affordance (matches every sibling tab);
	// the local sticky bar renders only on the standalone /configure/ddl-options mount,
	// where there is no shell and therefore no FAB.
	readonly inShell = !!this.jobConfigSvc;

	// ── Grouped categories for template ──
	readonly groups: DdlGroup[] = this.buildGroups();

	// ── Data signals ──
	readonly options = signal<JobDdlOptionsDto | null>(null);
	readonly originalJson = signal('');

	// ── UI state ──
	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly addInputs = signal<Record<string, string>>({});

	// ── Dirty detection ──
	readonly isDirty = computed(() => {
		const current = this.options();
		if (!current) return false;
		return JSON.stringify(current) !== this.originalJson();
	});

	/** Emits true when dirty, false when clean — lets a parent track this component's dirty state. */
	readonly dirtyChange = output<boolean>();

	/**
	 * AM-079: route guard hook. Only bites on the STANDALONE /configure/ddl-options route —
	 * mounted as a Job Settings tab this component isn't the routed component, and the shell's
	 * own guard covers it via its dirtyTabs set. Same guard and same wording as Job Settings,
	 * which was the only screen in the app that had this protection.
	 */
	hasUnsavedChanges(): boolean {
		return this.isDirty();
	}

	readonly changeCount = computed(() => {
		const current = this.options();
		if (!current || !this.isDirty()) return 0;

		let original: JobDdlOptionsDto;
		try {
			original = JSON.parse(this.originalJson());
		} catch {
			return 0;
		}

		let count = 0;
		for (const cat of CATEGORIES) {
			if (JSON.stringify(current[cat.key]) !== JSON.stringify(original[cat.key])) {
				count++;
			}
		}
		return count;
	});

	constructor() {
		this.loadOptions();
		this.jobConfigSvc?.saveHandler.set(() => this.save());
	}

	/** Notify the parent of our dirty state. Called from every site that can change it. */
	private emitDirty(): void {
		this.dirtyChange.emit(this.isDirty());
	}

	// ── Data access ──

	getValues(key: keyof JobDdlOptionsDto): string[] {
		return this.options()?.[key] ?? [];
	}

	getAddInput(key: string): string {
		return this.addInputs()[key] ?? '';
	}

	setAddInput(key: string, event: Event): void {
		const input = event.target as HTMLInputElement;
		this.addInputs.update(inputs => ({ ...inputs, [key]: input.value }));
	}

	// ── Mutations ──

	addValues(key: keyof JobDdlOptionsDto): void {
		const raw = this.getAddInput(key).trim();
		if (!raw) return;

		const newValues = raw.split(';').map(v => v.trim()).filter(v => v.length > 0);
		if (newValues.length === 0) return;

		const current = this.options();
		if (!current) return;

		const existing = [...current[key]];
		const existingLower = new Set(existing.map(v => v.toLowerCase()));

		for (const val of newValues) {
			if (!existingLower.has(val.toLowerCase())) {
				existing.push(val);
				existingLower.add(val.toLowerCase());
			}
		}

		this.options.set({ ...current, [key]: existing });
		this.addInputs.update(inputs => ({ ...inputs, [key]: '' }));
		this.emitDirty();
	}

	removeValue(key: keyof JobDdlOptionsDto, index: number): void {
		const current = this.options();
		if (!current) return;

		const values = [...current[key]];
		values.splice(index, 1);
		this.options.set({ ...current, [key]: values });
		this.emitDirty();
	}

	/** Move a value one position left (-1) or right (+1). Registrants see this order. */
	moveValue(key: keyof JobDdlOptionsDto, index: number, delta: -1 | 1): void {
		const current = this.options();
		if (!current) return;

		const values = [...current[key]];
		const target = index + delta;
		if (target < 0 || target >= values.length) return;

		[values[index], values[target]] = [values[target], values[index]];
		this.options.set({ ...current, [key]: values });
		this.emitDirty();
	}

	onChipDrop(key: keyof JobDdlOptionsDto, event: CdkDragDrop<string[]>): void {
		if (event.previousIndex === event.currentIndex) return;
		const current = this.options();
		if (!current) return;

		const values = [...current[key]];
		moveItemInArray(values, event.previousIndex, event.currentIndex);
		this.options.set({ ...current, [key]: values });
		this.emitDirty();
	}

	// ── Load / Save / Reset ──

	private loadOptions(): void {
		this.isLoading.set(true);
		this.http.get<JobDdlOptionsDto>(this.apiUrl).subscribe({
			next: dto => {
				this.options.set(dto);
				this.originalJson.set(JSON.stringify(dto));
				this.isLoading.set(false);
				this.emitDirty();
			},
			error: (err: unknown) => {
				const msg = (err as { error?: { message?: string } })?.error?.message || 'Failed to load dropdown options.';
				this.toast.show(msg, 'danger', 4000);
				this.isLoading.set(false);
			},
		});
	}

	save(): void {
		const current = this.options();
		if (!current || !this.isDirty()) return;

		this.isSaving.set(true);
		this.http.put(this.apiUrl, current).subscribe({
			next: () => {
				this.originalJson.set(JSON.stringify(current));
				this.isSaving.set(false);
				this.toast.show('Dropdown options saved.', 'success');
				this.emitDirty();
			},
			error: (err: unknown) => {
				const msg = (err as { error?: { message?: string } })?.error?.message || 'Failed to save.';
				this.toast.show(msg, 'danger', 4000);
				this.isSaving.set(false);
			},
		});
	}

	reset(): void {
		try {
			this.options.set(JSON.parse(this.originalJson()));
		} catch {
			// no-op — original is always valid JSON
		}
		this.emitDirty();
	}

	// ── Helpers ──

	private buildGroups(): DdlGroup[] {
		const groupOrder = ['player', 'team', 'clothing', 'coach'];
		return groupOrder.map(key => ({
			key,
			label: GROUP_LABELS[key],
			categories: CATEGORIES.filter(c => c.group === key),
		}));
	}
}
