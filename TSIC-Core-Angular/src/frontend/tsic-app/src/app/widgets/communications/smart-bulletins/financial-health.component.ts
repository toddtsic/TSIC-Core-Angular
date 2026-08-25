import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AuthService } from '@infrastructure/services/auth.service';

/** One placeholder figure. LAYER 1 — shape only; the real numbers arrive from the sweep cache. */
interface DemoMetric {
	readonly key: string;
	readonly icon: string;
	readonly label: string;
	readonly value: string;
	/** Drives the status dot AND a text label — never colour alone (WCAG). */
	readonly tone: 'attention' | 'watch' | 'clear';
	readonly toneLabel: string;
}

/**
 * Financial Health — the DIRECTOR-ONLY smart bulletin, first in the band.
 *
 * Deliberately named "Financial Health", not "ARB Health": expiring cards and ARB
 * subscription drift are the first tenants, not the whole scope. Anything that makes
 * a director's money picture wrong belongs here.
 *
 * **This is the one card in the band the public never sees**, so it carries its own
 * unmistakable treatment — inverted surface, amber private accent, a lock ribbon, and
 * the director's own name — rather than blending into the public-preview cards around
 * it. The rest of the band is a faithful mirror of the public page; this is not, and it
 * must never be mistaken for it.
 *
 * LAYER 1: content is placeholder. No live data, no links, no sends.
 */
@Component({
	selector: 'app-financial-health',
	standalone: true,
	templateUrl: './financial-health.component.html',
	styleUrl: './financial-health.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FinancialHealthComponent {
	private readonly auth = inject(AuthService);

	/**
	 * Name for the "private to you" line. The client auth model carries only `username`
	 * (`AuthenticatedUser`) — there is no first/last name on it, so a friendlier greeting
	 * would need a backend field before it can be shown.
	 */
	protected readonly directorName = computed(() => this.auth.currentUser()?.username ?? '');

	/** LAYER 1 placeholder figures — replaced by the 1st/15th sweep cache. */
	protected readonly metrics: readonly DemoMetric[] = [
		{ key: 'expiring', icon: 'bi-credit-card-2-front', label: 'Cards expiring this month', value: '7', tone: 'attention', toneLabel: 'Needs action' },
		{ key: 'drift', icon: 'bi-arrow-repeat', label: 'Subscriptions needing attention', value: '3', tone: 'watch', toneLabel: 'Watch' },
		{ key: 'behind', icon: 'bi-exclamation-triangle', label: 'Registrations behind in payment', value: '12', tone: 'attention', toneLabel: 'Needs action' },
	];
}
