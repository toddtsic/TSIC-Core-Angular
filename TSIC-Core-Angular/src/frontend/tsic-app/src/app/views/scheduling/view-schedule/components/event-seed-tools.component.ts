import { Component, ChangeDetectionStrategy, computed, inject, input, output, signal } from '@angular/core';
import { environment } from '@environments/environment';
import { BracketDevToolsService } from '../../bracket-dev-tools/services/bracket-dev-tools.service';
import { DevSeedStripComponent, type DevStripAction } from '../../shared/components/dev-seed-strip/dev-seed-strip.component';
import type { BracketDevActionResult } from '@core/api';

/**
 * Dev/sandbox-only strip in View Schedule that exercises the bracket pipeline (seeding +
 * auto-advancement) against real data for a SINGLE age group — the one the "By Age" filter
 * currently resolves to. The host gates visibility on exactly one age group being selected
 * and passes it in; every action is scoped to that age group, never the whole job.
 *
 * A reseeding tournament keeps its pools in a dedicated agegroup and reseeds its championship
 * flights (separate agegroups) cross-agegroup: scoring one agegroup's pools still fires
 * job-wide seed resolution, so the flight placeholders reseed automatically; the operator then
 * selects the flight agegroup and advances its bracket rounds.
 *
 * Rendered only outside live Production (env-gated below); the backend independently rejects
 * the calls in Production too. Chrome + spinner live in the shared DevSeedStrip; this component
 * owns the agegroup-scoped wiring.
 */
@Component({
	selector: 'app-event-seed-tools',
	standalone: true,
	imports: [DevSeedStripComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './event-seed-tools.component.html',
})
export class EventSeedToolsComponent {
	private readonly svc = inject(BracketDevToolsService);

	/** The single age group these tools act on (resolved + passed by the host). */
	readonly agegroupId = input.required<string>();
	/** Display name of that age group, for the strip copy. */
	readonly agegroupName = input<string>('');
	/** Host's active schedule tab — the bracket-round action only applies on 'brackets'. */
	readonly activeTab = input<string>('games');

	/** Emitted after any action mutates data, so the host can refresh its tabs. */
	readonly changed = output<void>();

	// Only ever visible in development/staging — the `production` flag is true for
	// staging too, so gate on the explicit env name (mirrors backend IsSandbox()).
	readonly isSandbox = environment.envName !== 'production';

	readonly busy = signal<string | null>(null);
	readonly result = signal<BracketDevActionResult | null>(null);
	readonly errorMessage = signal('');

	// Bracket-round scoring only makes sense while the Brackets tab is showing, so it's
	// disabled elsewhere (and its hint says why). Recomputes when the active tab changes.
	readonly actions = computed<DevStripAction[]>(() => {
		const onBrackets = this.activeTab() === 'brackets';
		return [
			{
				key: 'pool',
				label: '1 · Auto-Score RR Games',
				busyLabel: 'Scoring RR games…',
				hint: 'Scores every round-robin pool game in this age group with random scores (ties allowed) → standings lock → bracket seeds resolve.',
			},
			{
				key: 'round',
				label: '2 · Auto-Score Bracket Round (1 by 1)',
				busyLabel: 'Advancing…',
				hint: onBrackets
					? 'Scores every ready bracket game in this age group → winners advance one round. Click again for the next.'
					: 'Switch to the Brackets tab to advance bracket rounds.',
				disabled: !onBrackets,
			},
			{
				key: 'clear',
				label: '↺ Clear this age group\'s scores',
				busyLabel: 'Clearing…',
				hint: 'Wipes every score in this age group so seeding can re-run from scratch.',
				danger: true,
			},
		];
	});

	readonly clearMessage = computed(() => {
		const name = this.agegroupName();
		const where = name ? `the ${name} age group` : 'this age group';
		return `Every score in ${where} will be wiped and its bracket slots reset to unseeded. Seeding can then re-run from scratch.`;
	});

	onAction(key: string): void {
		const agegroupId = this.agegroupId();
		switch (key) {
			case 'pool':
				this.run('pool', () => this.svc.autoScorePoolAgegroup(agegroupId));
				break;
			case 'round':
				this.run('round', () => this.svc.autoScoreRoundAgegroup(agegroupId));
				break;
			case 'clear':
				this.run('clear', () => this.svc.revertAgegroup(agegroupId));
				break;
		}
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
