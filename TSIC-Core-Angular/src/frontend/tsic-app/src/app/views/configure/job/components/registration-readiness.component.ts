import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { JobConfigService, type TabKey } from '../job-config.service';
import type { ReadinessClauseDto } from '@core/api';

/** Which registration channel this panel is answering for. */
export type ReadinessAudience = 'player' | 'team';

/**
 * "Why isn't my registration link showing?" — answered on the screen where the director
 * turns registration on.
 *
 * The public "Register Player" / "Register a Team" cards are governed by five clauses, of
 * which the toggle below is ONE. The rest — the event isn't over, no later-year event has
 * replaced this one, fees exist, a team is open for players to join — were enforced silently
 * on the server. A director could turn registration on, watch the save succeed, and get no
 * card, with nothing anywhere saying why. That is what this panel ends.
 *
 * It renders the server's clause list verbatim: every verdict and every sentence of evidence
 * comes from RegistrationReadiness, the same type the public pulse composes its registration
 * flags through. Nothing is re-evaluated here, because a second opinion computed in the
 * browser would eventually explain a site that no longer behaves that way.
 */
@Component({
	selector: 'app-registration-readiness',
	standalone: true,
	imports: [RouterLink],
	templateUrl: './registration-readiness.component.html',
	styleUrl: './registration-readiness.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistrationReadinessComponent {
	readonly audience = input.required<ReadinessAudience>();

	protected readonly svc = inject(JobConfigService);

	protected readonly clauses = computed<ReadinessClauseDto[]>(() => {
		const r = this.svc.readiness();
		if (!r) return [];
		return (this.audience() === 'player' ? r.playerClauses : r.teamClauses) ?? [];
	});

	protected readonly visible = computed(() => {
		const r = this.svc.readiness();
		if (!r) return false;
		return this.audience() === 'player' ? r.playerCardVisible : r.teamCardVisible;
	});

	protected readonly failedCount = computed(() => this.clauses().filter(c => !c.passed).length);

	/**
	 * This tab has edits that haven't been saved yet.
	 *
	 * The clause list answers for what is IN THE DATABASE — it is the server's evaluation, and
	 * re-deriving any of it in the browser to track an unsaved checkbox would fork the predicate
	 * this whole panel exists to keep singular. So instead of silently contradicting the toggle
	 * beside it ("registration is off" next to a checkbox the director just ticked), it says the
	 * answer is stale and what to do about it.
	 */
	protected readonly pending = computed(() =>
		this.svc.dirtyTabs().has(this.audience() === 'player' ? 'player' : 'teams'));

	protected readonly channelLabel = computed(() =>
		this.audience() === 'player' ? 'Player registration' : 'Team registration');

	protected readonly headline = computed(() => {
		if (this.pending()) return `${this.channelLabel()} — save to re-check.`;
		if (this.visible()) return `${this.channelLabel()} is LIVE on the public site.`;
		const n = this.failedCount();
		return n === 1
			? `${this.channelLabel()} is NOT showing — 1 thing to fix.`
			: `${this.channelLabel()} is NOT showing — ${n} things to fix.`;
	});

	/**
	 * A failing clause is only worth a link if it lands on the control that fixes it.
	 *   scheduling → the Scheduling tab, where the event start/end dates live (same screen,
	 *                so it switches tabs rather than navigating).
	 *   fees/teams → the LADT editor, which owns both the fee cards and the teams whose
	 *                registration windows decide player availability.
	 *   toggle     → the switch immediately below this panel; no link needed.
	 *   superseded / league → explanatory, nothing to open. A link to nowhere is worse
	 *                than no link.
	 * Relative route — the :jobPath prefix has to survive.
	 */
	protected fixTab(clause: ReadinessClauseDto): TabKey | null {
		return clause.fixTarget === 'scheduling' ? 'scheduling' : null;
	}

	protected isLadtFix(clause: ReadinessClauseDto): boolean {
		return clause.fixTarget === 'fees' || clause.fixTarget === 'teams';
	}

	protected fixLabel(clause: ReadinessClauseDto): string | null {
		switch (clause.fixTarget) {
			case 'scheduling': return 'Open the Scheduling tab';
			case 'fees': return 'Set up fees in the League editor';
			case 'teams': return 'Open team registration windows in the League editor';
			default: return null;
		}
	}

	protected goToTab(tab: TabKey): void {
		this.svc.activeTab.set(tab);
	}
}
