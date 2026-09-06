import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import type { UsageQuery, UsageTabDef } from '../usage-analysis.models';
import { USAGE_SCOPE_OPTIONS } from '../usage-analysis.models';

/**
 * An unclaimed tab slot. States what the slot is reserved for and the exact query a
 * real tab would run with, so the scope/window/bot controls can be exercised before
 * any chart exists. Replace the `<app-usage-tab-placeholder>` for a slot in the shell
 * template with the real tab component to claim it.
 */
@Component({
	selector: 'app-usage-tab-placeholder',
	standalone: true,
	changeDetection: ChangeDetectionStrategy.OnPush,
	template: `
		<div class="list-empty-state">
			<i class="bi" [class]="'bi ' + tab().icon"></i>
			<div class="slot-title">{{ tab().label }} — nothing here yet</div>
			<div class="slot-hint">{{ tab().hint }}</div>
			<div class="slot-query">
				Would run as: <strong>{{ scopeLabel() }}</strong>
				· {{ jobCount() }} live {{ jobCount() === 1 ? 'event' : 'events' }}
				· {{ windowLabel() }}
				· {{ query().excludeBots ? 'bots hidden' : 'bots included' }}
			</div>
		</div>
	`,
	styles: `
		.slot-title {
			font-weight: 600;
			color: var(--brand-text);
		}
		.slot-hint {
			color: var(--brand-text-muted);
		}
		.slot-query {
			margin-top: var(--space-2);
			font-size: var(--font-size-sm);
			color: var(--brand-text-muted);
		}
	`,
})
export class UsageTabPlaceholderComponent {
	readonly tab = input.required<UsageTabDef>();
	readonly query = input.required<UsageQuery>();
	readonly jobCount = input.required<number>();

	readonly scopeLabel = computed(() =>
		USAGE_SCOPE_OPTIONS.find(o => o.scope === this.query().scope)?.label ?? this.query().scope);

	readonly windowLabel = computed(() =>
		this.query().windowDays === 1 ? '24h' : `${this.query().windowDays}d`);
}
