import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { environment } from '@environments/environment';
import { ToastService } from '@shared-ui/toast.service';
import type { JobDdlOptionsDto } from '@core/api';

// ── Category metadata (data-driven rendering) ──

interface DdlCategory {
	key: keyof JobDdlOptionsDto;
	label: string;
	group: 'clothing' | 'player' | 'team';
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
];

const GROUP_LABELS: Record<string, string> = {
	clothing: 'Clothing Sizes',
	player:   'Player Data',
	team:     'Team & Context',
};

@Component({
	selector: 'app-ddl-options',
	standalone: true,
	imports: [FormsModule],
	templateUrl: './ddl-options.component.html',
	styleUrl: './ddl-options.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DdlOptionsComponent {
	private readonly http = inject(HttpClient);
	private readonly toast = inject(ToastService);
	private readonly apiUrl = `${environment.apiUrl}/job-ddl-options`;

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
	}

	removeValue(key: keyof JobDdlOptionsDto, index: number): void {
		const current = this.options();
		if (!current) return;

		const values = [...current[key]];
		values.splice(index, 1);
		this.options.set({ ...current, [key]: values });
	}

	// ── Load / Save / Reset ──

	private loadOptions(): void {
		this.isLoading.set(true);
		this.http.get<JobDdlOptionsDto>(this.apiUrl).subscribe({
			next: dto => {
				this.options.set(dto);
				this.originalJson.set(JSON.stringify(dto));
				this.isLoading.set(false);
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
	}

	// ── Helpers ──

	private buildGroups(): DdlGroup[] {
		const groupOrder = ['clothing', 'player', 'team'];
		return groupOrder.map(key => ({
			key,
			label: GROUP_LABELS[key],
			categories: CATEGORIES.filter(c => c.group === key),
		}));
	}
}
