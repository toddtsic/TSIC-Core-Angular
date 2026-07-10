import { Component, ChangeDetectionStrategy, computed, inject, input, output, signal } from '@angular/core';
import { environment } from '@environments/environment';
import { BracketDevToolsService } from './services/bracket-dev-tools.service';
import { DevSeedStripComponent, type DevStripAction } from '../shared/components/dev-seed-strip/dev-seed-strip.component';
import type { BracketDevActionResult } from '@core/api';

/**
 * Dev/sandbox-only strip that exercises the bracket pipeline (seeding + advancement)
 * against real tournament data for the selected division. Renders ONLY outside live
 * Production; the backend independently rejects the calls there too. Chrome + spinner
 * live in the shared DevSeedStrip; this component owns the division-scoped wiring.
 */
@Component({
	selector: 'app-bracket-dev-tools',
	standalone: true,
	imports: [DevSeedStripComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './bracket-dev-tools.component.html',
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

	readonly actions: DevStripAction[] = [
		{
			key: 'pool',
			label: '1 · Auto-Score RR Games',
			busyLabel: 'Scoring RR games…',
			hint: 'Scores every pool game with random scores (ties allowed) → standings lock → bracket seeds resolve.',
		},
		{
			key: 'round',
			label: '2 · Auto-Score Bracket Round (1 by 1)',
			busyLabel: 'Advancing…',
			hint: 'Scores every ready bracket game → winners advance one round. Click again for the next.',
		},
		{
			key: 'clear',
			label: '↺ Clear scores & reset bracket',
			busyLabel: 'Clearing…',
			hint: 'Wipes all scores and blanks bracket slots so seeding can re-run from scratch.',
			danger: true,
		},
	];

	readonly clearMessage = computed(() => {
		const div = this.divisionName();
		const where = div ? `<strong>${div}</strong>` : 'this division';
		return `Every score in ${where} will be cleared and its bracket slots blanked back to unseeded.`;
	});

	onAction(key: string): void {
		switch (key) {
			case 'pool':
				this.run('pool', () => this.svc.autoScorePool(this.request()));
				break;
			case 'round':
				this.run('round', () => this.svc.autoScoreRound(this.request()));
				break;
			case 'clear':
				this.run('clear', () => this.svc.clearScores(this.request()));
				break;
		}
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
