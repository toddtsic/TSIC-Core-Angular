import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { QuickLinksService } from './services/quick-links.service';
import { ToastService } from '@shared-ui/toast.service';
import type { JobVisibilityDto, UpdateJobVisibilityRequest } from '@core/api';

/** The flag keys shared by JobVisibilityDto (read) and UpdateJobVisibilityRequest (write). */
type FlagKey = keyof UpdateJobVisibilityRequest;

interface ToggleDef {
	key: FlagKey;
	label: string;
	icon: string;
	onTip: string;
	offTip: string;
}

/**
 * SuperUser "Quick Links" editor — a focused, job-scoped editor for the public
 * landing-hero CTA toggles of the CURRENT job. Each switch saves on change
 * (one partial PUT per toggle). These flags also live in their logical Configure
 * Job tabs; this is a one-stop convenience surface, not a separate source of truth.
 */
@Component({
	selector: 'app-quick-links',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './quick-links.component.html',
	styleUrl: './quick-links.component.scss',
})
export class QuickLinksComponent {
	private readonly svc = inject(QuickLinksService);
	private readonly toast = inject(ToastService);

	readonly flags = signal<JobVisibilityDto | null>(null);
	readonly isLoading = signal(true);
	readonly loadError = signal(false);
	/** The flag currently being saved (drives the per-row spinner); null when idle. */
	readonly savingKey = signal<FlagKey | null>(null);

	readonly ready = computed(() => !this.isLoading() && !this.loadError() && this.flags() !== null);

	readonly toggles: readonly ToggleDef[] = [
		{ key: 'allowPlayerRegistration', label: 'Register Player', icon: 'bi-person-plus',
			onTip: 'Players can register — the "Register Player" card shows on the landing page.',
			offTip: 'Player registration is closed — the card is hidden.' },
		{ key: 'allowTeamRegistration', label: 'Register Team', icon: 'bi-people',
			onTip: 'Teams can register — the "Register Team" card shows (Tournament/League jobs only).',
			offTip: 'Team registration is closed — the card is hidden.' },
		{ key: 'publishSchedule', label: 'View Schedule', icon: 'bi-calendar-event',
			onTip: 'The public schedule is visible — the "View Schedule" card shows.',
			offTip: 'The schedule is not public — the card is hidden.' },
		{ key: 'showPublicRosters', label: 'Rosters', icon: 'bi-list-ul',
			onTip: 'Public player rosters are visible — the "Rosters" card shows.',
			offTip: 'Public rosters are hidden — the card is hidden.' },
		{ key: 'enableStore', label: 'Store', icon: 'bi-bag',
			onTip: 'The store is enabled — the "Store" card shows once it has active items.',
			offTip: 'The store is disabled — the card is hidden.' },
		{ key: 'offerPlayerInsurance', label: 'Player Insurance', icon: 'bi-shield-check',
			onTip: 'RegSaver insurance is offered — the "Insurance Update" card shows.',
			offTip: 'Insurance is not offered — the card is hidden.' },
	];

	constructor() {
		this.svc.get().subscribe({
			next: f => { this.flags.set(f); this.isLoading.set(false); },
			error: () => { this.loadError.set(true); this.isLoading.set(false); },
		});
	}

	isOn(key: FlagKey): boolean {
		return !!this.flags()?.[key as keyof JobVisibilityDto];
	}

	/** Optimistically flip the flag, persist just that one, revert on failure. */
	onToggle(key: FlagKey, value: boolean): void {
		const prev = this.flags();
		if (!prev) return;

		this.flags.set({ ...prev, [key]: value });
		this.savingKey.set(key);

		const patch: UpdateJobVisibilityRequest = {};
		patch[key] = value;          // partial — only the toggled flag is sent
		this.svc.save(patch).subscribe({
			next: () => this.savingKey.set(null),
			error: () => {
				this.flags.set({ ...prev });           // revert to the pre-toggle truth
				this.savingKey.set(null);
				this.toast.show('Could not save — change reverted', 'danger');
			},
		});
	}
}
