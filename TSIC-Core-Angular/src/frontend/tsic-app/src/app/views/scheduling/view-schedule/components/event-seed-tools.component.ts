import { Component, ChangeDetectionStrategy, inject, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { environment } from '@environments/environment';
import { BracketDevToolsService } from '../../bracket-dev-tools/services/bracket-dev-tools.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { BracketDevActionResult } from '@core/api';

/**
 * Dev/sandbox-only strip in View Schedule that exercises a RESEEDING tournament's full
 * pipeline against real data. Event-scoped, because a reseed tournament keeps its pools
 * in a shared round-robin agegroup and reseeds its championship flights (separate
 * agegroups) cross-agegroup: scoring the pools fires job-wide seed resolution which
 * reseeds the bracket placeholders automatically, then bracket rounds advance winners.
 *
 * Rendered only outside live Production (env-gated below) and only when the job is a
 * reseed tournament (host gates on capabilities.isReseedTournament); the backend
 * independently rejects the calls in Production too.
 */
@Component({
	selector: 'app-event-seed-tools',
	standalone: true,
	imports: [CommonModule, ConfirmDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './event-seed-tools.component.html',
	styleUrl: './event-seed-tools.component.scss',
})
export class EventSeedToolsComponent {
	private readonly svc = inject(BracketDevToolsService);

	/** Emitted after any action mutates data, so the host can refresh its tabs. */
	readonly changed = output<void>();

	// Only ever visible in development/staging — the `production` flag is true for
	// staging too, so gate on the explicit env name (mirrors backend IsSandbox()).
	readonly isSandbox = environment.envName !== 'production';

	readonly busy = signal<string | null>(null);
	readonly result = signal<BracketDevActionResult | null>(null);
	readonly errorMessage = signal('');
	readonly showClearConfirm = signal(false);

	seedPools(): void {
		if (!this.canRun()) return;
		this.run('pool', () => this.svc.autoScorePoolJob());
	}

	seedBracketRound(): void {
		if (!this.canRun()) return;
		this.run('round', () => this.svc.autoScoreRoundJob());
	}

	clearAll(): void {
		if (!this.canRun()) return;
		this.showClearConfirm.set(true);
	}

	onClearConfirmed(): void {
		this.showClearConfirm.set(false);
		this.run('clear', () => this.svc.revertLeague());
	}

	onClearCancelled(): void {
		this.showClearConfirm.set(false);
	}

	private canRun(): boolean {
		return this.isSandbox && !this.busy();
	}

	private run(key: string, call: () => ReturnType<BracketDevToolsService['revertLeague']>): void {
		this.busy.set(key);
		this.result.set(null);
		this.errorMessage.set('');
		call().subscribe({
			next: (res) => {
				this.result.set(res);
				this.busy.set(null);
				this.changed.emit();
			},
			error: (err) => {
				this.errorMessage.set(err?.error?.message ?? 'Action failed.');
				this.busy.set(null);
			},
		});
	}
}
