import { Component, ChangeDetectionStrategy, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { environment } from '@environments/environment';
import { BracketDevToolsService } from '../../bracket-dev-tools/services/bracket-dev-tools.service';
import type { BracketDevActionResult } from '@core/api';

/**
 * Dev/sandbox-only strip in View Schedule that exercises an AGE GROUP's bracket
 * seeding against real tournament data. Age-group scope is the correct granularity:
 * championship games seed cross-pool from the divisions within the age group, so all
 * of its pools must complete before seeds resolve. Rendered only outside live
 * Production (env-gated below; the backend independently rejects the calls too), and
 * the host only mounts it for an age group that actually has bracket games.
 */
@Component({
	selector: 'app-agegroup-seed-tools',
	standalone: true,
	imports: [CommonModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './agegroup-seed-tools.component.html',
	styleUrl: './agegroup-seed-tools.component.scss',
})
export class AgegroupSeedToolsComponent {
	private readonly svc = inject(BracketDevToolsService);

	readonly agegroupId = input.required<string>();
	readonly agegroupName = input<string>('');

	/** Emitted after any action mutates data, so the host can refresh its tabs. */
	readonly changed = output<void>();

	// Only ever visible in development/staging — the `production` flag is true for
	// staging too, so gate on the explicit env name (mirrors backend IsSandbox()).
	readonly isSandbox = environment.envName !== 'production';

	readonly busy = signal<string | null>(null);
	readonly result = signal<BracketDevActionResult | null>(null);
	readonly errorMessage = signal('');

	autoScorePool(): void {
		if (!this.canRun()) return;
		this.run('pool', () => this.svc.autoScorePoolAgegroup(this.request()));
	}

	autoScoreRound(): void {
		if (!this.canRun()) return;
		this.run('round', () => this.svc.autoScoreRoundAgegroup(this.request()));
	}

	clearScores(): void {
		if (!this.canRun()) return;
		if (!confirm(`Clear ALL scores in "${this.agegroupName()}" and blank its bracket slots?`)) return;
		this.run('clear', () => this.svc.revertAgegroup(this.request()));
	}

	private canRun(): boolean {
		return this.isSandbox && !!this.agegroupId() && !this.busy();
	}

	private request() {
		return { agegroupId: this.agegroupId() };
	}

	private run(key: string, call: () => ReturnType<BracketDevToolsService['revertAgegroup']>): void {
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
