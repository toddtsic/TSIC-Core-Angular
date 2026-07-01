import { Component, ChangeDetectionStrategy, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { environment } from '@environments/environment';
import { BracketDevToolsService } from './services/bracket-dev-tools.service';
import type { BracketDevActionResult } from '@core/api';

/**
 * Dev/sandbox-only strip that exercises the bracket pipeline (seeding + advancement)
 * against real tournament data for the selected division. Renders ONLY outside live
 * Production; the backend independently rejects the calls there too.
 */
@Component({
	selector: 'app-bracket-dev-tools',
	standalone: true,
	imports: [CommonModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './bracket-dev-tools.component.html',
	styleUrl: './bracket-dev-tools.component.scss',
})
export class BracketDevToolsComponent {
	private readonly svc = inject(BracketDevToolsService);

	readonly agegroupId = input.required<string>();
	readonly divId = input.required<string>();
	readonly divisionName = input<string>('');

	/** Emitted after any action mutates data, so the host can refresh its grid. */
	readonly changed = output<void>();

	// Only ever visible in development/staging — the `production` flag is true for
	// staging too, so gate on the explicit env name (mirrors backend IsSandbox()).
	readonly isSandbox = environment.envName !== 'production';

	readonly busy = signal<string | null>(null);
	readonly result = signal<BracketDevActionResult | null>(null);
	readonly errorMessage = signal('');

	clearScores(): void {
		if (!this.canRun()) return;
		if (!confirm(`Clear ALL scores in "${this.divisionName()}" and blank its bracket slots?`)) return;
		this.run('clear', () => this.svc.clearScores(this.request()));
	}

	autoScorePool(): void {
		if (!this.canRun()) return;
		this.run('pool', () => this.svc.autoScorePool(this.request()));
	}

	autoScoreRound(): void {
		if (!this.canRun()) return;
		this.run('round', () => this.svc.autoScoreRound(this.request()));
	}

	private canRun(): boolean {
		return this.isSandbox && !!this.divId() && !this.busy();
	}

	private request() {
		return { agegroupId: this.agegroupId(), divId: this.divId() };
	}

	private run(key: string, call: () => ReturnType<BracketDevToolsService['clearScores']>): void {
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
