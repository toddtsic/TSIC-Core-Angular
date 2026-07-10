import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { BracketDevActionResult } from '@core/api';

/** One button in a dev seed strip. */
export interface DevStripAction {
	/** Busy key; emitted via (action) when this button should run. */
	readonly key: string;
	/** Idle button label. */
	readonly label: string;
	/** Label shown beside the spinner while this action runs. */
	readonly busyLabel: string;
	/** Sub-label describing what the button does. */
	readonly hint: string;
	/** Destructive — a confirm dialog gates it before (action) fires. */
	readonly danger?: boolean;
	/** Non-interactive (e.g. this action only applies on another tab). A running action
	 *  (busy) disables every button independently of this flag. */
	readonly disabled?: boolean;
}

/**
 * Presentational DEV strip shared by the division panel (bracket-dev-tools) and the
 * reseeding-event panel (event-seed-tools) — the same tool at two scopes. Owns the shell,
 * the busy spinner, and the destructive-action confirm; the parent owns the async calls,
 * passes busy/result/error state down, and receives the action key back through (action).
 * The lead paragraph is projected so each parent supplies its own rich-text copy.
 */
@Component({
	selector: 'app-dev-seed-strip',
	standalone: true,
	imports: [ConfirmDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './dev-seed-strip.component.html',
	styleUrl: './dev-seed-strip.component.scss',
})
export class DevSeedStripComponent {
	readonly title = input.required<string>();
	readonly actions = input.required<DevStripAction[]>();
	readonly busy = input<string | null>(null);
	readonly result = input<BracketDevActionResult | null>(null);
	readonly errorMessage = input<string>('');
	readonly confirmTitle = input<string>('Are you sure?');
	readonly confirmMessage = input<string>('');

	/** Emits an action's key when it should run (after confirm, for danger actions). */
	readonly action = output<string>();

	protected readonly pendingDanger = signal<string | null>(null);

	protected onClick(a: DevStripAction): void {
		if (this.busy() || a.disabled) return;
		if (a.danger) {
			this.pendingDanger.set(a.key);
			return;
		}
		this.action.emit(a.key);
	}

	protected onConfirm(): void {
		const key = this.pendingDanger();
		this.pendingDanger.set(null);
		if (key) this.action.emit(key);
	}

	protected onCancel(): void {
		this.pendingDanger.set(null);
	}
}
