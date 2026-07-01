import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
	EmailTroubleshooterService,
	type SuppressionEntryDto,
	type EmailInvestigateResultDto
} from './services/e-mail-troubleshooter.service';

type TabKey = 'suppression' | 'investigate';

@Component({
	selector: 'app-email-troubleshooter',
	standalone: true,
	imports: [FormsModule, DatePipe],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './e-mail-troubleshooter.component.html',
	styleUrl: './e-mail-troubleshooter.component.scss'
})
export class EmailTroubleshooterComponent {
	private readonly service = inject(EmailTroubleshooterService);

	readonly activeTab = signal<TabKey>('suppression');

	// ── Tab 1: Suppression List ──
	readonly suppressionInput = signal('');
	readonly suppressionResults = signal<SuppressionEntryDto[]>([]);
	readonly suppressionLoading = signal(false);
	readonly suppressionSearched = signal(false);
	readonly suppressionError = signal<string | null>(null);
	readonly removing = signal<string[]>([]);

	// ── Tab 2: Investigate E-Mail Failure ──
	readonly investigateInput = signal('');
	readonly investigateResults = signal<EmailInvestigateResultDto[]>([]);
	readonly investigateLoading = signal(false);
	readonly investigateSearched = signal(false);
	readonly investigateError = signal<string | null>(null);

	setTab(tab: TabKey): void {
		this.activeTab.set(tab);
	}

	// ── Tab 1 actions ──

	checkSuppression(): void {
		const emails = this.parseEmails(this.suppressionInput());
		if (emails.length === 0) {
			this.suppressionError.set('Enter at least one email address.');
			return;
		}
		this.suppressionError.set(null);
		this.suppressionLoading.set(true);
		this.suppressionSearched.set(true);
		this.suppressionResults.set([]);

		this.service.checkSuppression(emails).subscribe({
			next: rows => {
				this.suppressionResults.set(rows);
				this.suppressionLoading.set(false);
			},
			error: () => {
				this.suppressionError.set('Suppression check failed. Please try again.');
				this.suppressionLoading.set(false);
			}
		});
	}

	removeFromSuppression(email: string): void {
		const ok = window.confirm(
			`Remove ${email} from the suppression list?\n\n` +
			`This is account-wide. If the address was suppressed for a hard bounce or complaint, ` +
			`removing it can let mail re-bounce and may affect sender reputation.`
		);
		if (!ok) return;

		this.removing.set([...this.removing(), email]);
		this.service.removeSuppression([email]).subscribe({
			next: results => {
				const removed = results.length > 0 && results[0].removed;
				if (removed) {
					// Reflect the new state in place: this address is no longer suppressed.
					this.suppressionResults.set(
						this.suppressionResults().map(r =>
							r.email === email ? { ...r, status: 'NotSuppressed', reason: null, lastUpdate: null } : r
						)
					);
				} else {
					this.suppressionError.set(`Could not remove ${email}: ${results[0]?.error ?? 'unknown error'}`);
				}
				this.removing.set(this.removing().filter(e => e !== email));
			},
			error: () => {
				this.suppressionError.set(`Removal request failed for ${email}.`);
				this.removing.set(this.removing().filter(e => e !== email));
			}
		});
	}

	isRemoving(email: string): boolean {
		return this.removing().includes(email);
	}

	clearSuppression(): void {
		this.suppressionInput.set('');
		this.suppressionResults.set([]);
		this.suppressionSearched.set(false);
		this.suppressionError.set(null);
	}

	// ── Tab 2 actions ──

	investigate(): void {
		const emails = this.parseEmails(this.investigateInput());
		if (emails.length === 0) {
			this.investigateError.set('Enter at least one email address.');
			return;
		}
		this.investigateError.set(null);
		this.investigateLoading.set(true);
		this.investigateSearched.set(true);
		this.investigateResults.set([]);

		this.service.investigate(emails).subscribe({
			next: rows => {
				this.investigateResults.set(rows);
				this.investigateLoading.set(false);
			},
			error: () => {
				this.investigateError.set('Investigation failed. Please try again.');
				this.investigateLoading.set(false);
			}
		});
	}

	clearInvestigate(): void {
		this.investigateInput.set('');
		this.investigateResults.set([]);
		this.investigateSearched.set(false);
		this.investigateError.set(null);
	}

	/** Split semicolon/comma/newline-separated input into trimmed, de-duplicated addresses. */
	private parseEmails(raw: string): string[] {
		const seen = new Set<string>();
		const out: string[] = [];
		for (const part of raw.split(/[;,\n]/)) {
			const email = part.trim();
			if (!email) continue;
			const key = email.toLowerCase();
			if (seen.has(key)) continue;
			seen.add(key);
			out.push(email);
		}
		return out;
	}
}
