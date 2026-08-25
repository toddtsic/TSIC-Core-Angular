import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AuthService } from '@infrastructure/services/auth.service';
import { environment } from '@environments/environment';

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
 * Financial Health — the DIRECTOR-ONLY dashboard widget.
 *
 * Deliberately named "Financial Health", not "ARB Health": expiring cards and ARB
 * subscription drift are the first tenants, not the whole scope. Anything that makes
 * a director's money picture wrong belongs here — balances owed, plans that will not
 * finish the balance, payments that never landed.
 *
 * **Reached as a WIDGET, not a bulletin.** It briefly lived in the Smart Bulletins
 * band, which put it on the page every director lands on — but the band renders
 * unconditionally, so there was no way to hold it back per job while it is still being
 * designed. `widgets.JobWidget` supplies that control: `IsEnabled` per job, `RoleId`
 * for the director gate, and `Config` for per-job settings. Attach and detach is
 * SuperUser-only (`WidgetEditorController`), so a director cannot remove their own
 * money warnings.
 *
 * KNOWN GAP, tracked deliberately: the dashboard is a menu click away, so this does
 * NOT yet satisfy the "a director can never say I didn't see it" objective. That
 * needs the `public` widget workspace wired into the job landing page — the taxonomy
 * already exists (`WidgetCategory.Workspace`), the render path does not.
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
	 * SECOND, INDEPENDENT GUARD — the JobWidget row is NOT one.
	 *
	 * Production and Staging run against the SAME database, so a widgets.JobWidget
	 * row is live for prod the moment it is inserted; it controls WHICH JOB, never
	 * which environment. While the content is placeholder, invented money figures
	 * must not be reachable in a production build at all.
	 *
	 * Must be `envName`, NOT `production` — the `production: true` flag is set in the
	 * STAGING overlay too (it drives build optimisation, not identity), so the obvious
	 * version would also hide this from staging. Same idiom as arb-health.component.ts:93.
	 *
	 * DELETE THIS when the panel shows real data; the JobWidget row is the permanent control.
	 */
	protected readonly visible = environment.envName !== 'production';

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
