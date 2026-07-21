import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { GridAllModule, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { JobService } from '@infrastructure/services/job.service';
import {
	EmailDeliverabilityService,
	type SuppressionEntryDto,
	type EmailInvestigateResultDto,
	type PlayerSentEmailDto
} from './services/email-deliverability.service';

type TabKey = 'status' | 'history';

// Seconds a player must wait between test sends to the same address (client-side throttle).
const TEST_COOLDOWN_MS = 60_000;

type StatusTone = 'ok' | 'suppressed' | 'unknown';
interface StatusMeta {
	/** Pill label. */
	pill: string;
	/** Drives the row accent + pill colour. */
	tone: StatusTone;
}

// Single source of the per-status pill + tone, keyed by the server's SuppressionEntryDto.status.
// No prose lives here: the "is my email OK?" verdict is stated once in the adaptive summary and the
// "accepted ≠ received" caveat once in the disclaimer — rows never repeat a shared explanation.
const STATUS_META: Record<string, StatusMeta> = {
	NotSuppressed: { pill: 'Not suppressed', tone: 'ok' },
	Suppressed: { pill: 'Suppressed', tone: 'suppressed' }
};
const STATUS_META_FALLBACK: StatusMeta = { pill: 'Couldn’t check', tone: 'unknown' };

@Component({
	selector: 'app-email-deliverability',
	standalone: true,
	imports: [DatePipe, GridAllModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './email-deliverability.component.html',
	styleUrl: './email-deliverability.component.scss'
})
export class EmailDeliverabilityComponent implements OnInit {
	private readonly service = inject(EmailDeliverabilityService);
	private readonly jobService = inject(JobService);

	/** Current program name for "this program" labeling; falls back to a generic phrase. */
	readonly programName = computed(() => this.jobService.currentJob()?.jobName || 'this program');

	readonly activeTab = signal<TabKey>('status');

	// ── Tab 1: Current Status ──
	readonly entries = signal<SuppressionEntryDto[]>([]);
	readonly loading = signal(false);
	readonly loaded = signal(false);
	readonly error = signal<string | null>(null);
	readonly removing = signal<string[]>([]);
	// Per-address test-send state.
	readonly testing = signal<string[]>([]);
	readonly cooling = signal<string[]>([]);
	readonly testResults = signal<Record<string, EmailInvestigateResultDto>>({});

	/** How many of the checked addresses are suppressed — drives the summary verdict. */
	readonly suppressedCount = computed(() => this.entries().filter(r => r.status === 'Suppressed').length);

	/**
	 * The single at-a-glance verdict shown above the rows, so the "is my email OK?" answer and the
	 * shared explanation are stated ONCE instead of repeated on every same-status row. Null until
	 * there is something to summarize.
	 */
	readonly summary = computed<{ tone: 'ok' | 'warn'; text: string } | null>(() => {
		const total = this.entries().length;
		if (total === 0) return null;
		const suppressed = this.suppressedCount();
		if (suppressed === 0) {
			return {
				tone: 'ok',
				text: total === 1
					? 'Good news — your email address is not suppressed. Amazon AWS Email Services isn’t stopping delivery to it.'
					: `Good news — none of your ${total} email addresses is suppressed. Amazon AWS Email Services isn’t stopping delivery to any of them.`
			};
		}
		return {
			tone: 'warn',
			text: `${suppressed} of your ${total} email addresses ${suppressed === 1 ? 'is' : 'are'} suppressed — restore delivery below to start receiving our email again.`
		};
	});

	// ── Tab 2: E-Mail History (this job only) ──
	readonly sentEmails = signal<PlayerSentEmailDto[]>([]);
	readonly sentLoaded = signal(false);
	// Default sort: newest first.
	readonly sortSettings: SortSettingsModel = {
		columns: [{ field: 'sentAt', direction: 'Descending' }]
	};

	// ── Template preview modal ──
	readonly templateOpen = signal(false);
	readonly templateLoading = signal(false);
	readonly templateError = signal<string | null>(null);
	readonly templateSubject = signal<string>('');
	// Raw HTML string; the template binds it via [innerHTML], which Angular sanitizes.
	readonly templateHtml = signal<string>('');

	ngOnInit(): void {
		this.load();
		this.loadSentHistory();
	}

	setTab(tab: TabKey): void {
		this.activeTab.set(tab);
	}

	// ── Tab 1 ──

	load(): void {
		this.loading.set(true);
		this.error.set(null);

		this.service.getStatus().subscribe({
			next: rows => {
				this.entries.set(rows);
				this.loading.set(false);
				this.loaded.set(true);
			},
			error: () => {
				this.error.set('We could not check your email right now. Please try again in a moment.');
				this.loading.set(false);
				this.loaded.set(true);
			}
		});
	}

	unsuppress(email: string): void {
		const ok = window.confirm(
			`Restore delivery to ${email}?\n\n` +
			`This address was suppressed after mail to it bounced or was marked as spam. ` +
			`Make sure the mailbox exists and can receive mail before you restore it — ` +
			`otherwise it will just be suppressed again.`
		);
		if (!ok) return;

		this.removing.set([...this.removing(), email]);
		this.error.set(null);

		this.service.unsuppress(email).subscribe({
			next: result => {
				if (result.removed) {
					this.entries.set(
						this.entries().map(r =>
							r.email === email ? { ...r, status: 'NotSuppressed', reason: null, lastUpdate: null } : r
						)
					);
				} else {
					this.error.set(`We could not restore ${email}: ${result.error ?? 'unknown error'}`);
				}
				this.removing.set(this.removing().filter(e => e !== email));
			},
			error: () => {
				this.error.set(`The request to restore ${email} failed. Please try again.`);
				this.removing.set(this.removing().filter(e => e !== email));
			}
		});
	}

	sendTest(email: string): void {
		if (this.isTesting(email) || this.isCoolingDown(email)) return;

		this.testing.set([...this.testing(), email]);

		this.service.testSend(email).subscribe({
			next: result => {
				this.testResults.set({ ...this.testResults(), [email]: result });
				// If the test surfaced a suppression we didn't know about, reflect it in the row.
				if (result.suppressionStatus === 'Suppressed') {
					this.entries.set(
						this.entries().map(r =>
							r.email === email
								? { ...r, status: 'Suppressed', reason: result.suppressionReason ?? r.reason }
								: r
						)
					);
				}
				this.finishTest(email);
			},
			error: () => {
				this.testResults.set({
					...this.testResults(),
					[email]: {
						email,
						suppressionStatus: 'Unknown',
						sendAccepted: false,
						side: 'Inconclusive',
						conclusion: ''
					}
				});
				this.finishTest(email);
			}
		});
	}

	/** Clear the in-flight flag and start the per-address cooldown. */
	private finishTest(email: string): void {
		this.testing.set(this.testing().filter(e => e !== email));
		this.cooling.set([...this.cooling(), email]);
		setTimeout(() => this.cooling.set(this.cooling().filter(e => e !== email)), TEST_COOLDOWN_MS);
	}

	isRemoving(email: string): boolean {
		return this.removing().includes(email);
	}

	isTesting(email: string): boolean {
		return this.testing().includes(email);
	}

	isCoolingDown(email: string): boolean {
		return this.cooling().includes(email);
	}

	testResultFor(email: string): EmailInvestigateResultDto | undefined {
		return this.testResults()[email];
	}

	/** Copy + tone for a row's status, from the STATUS_META dictionary (unknown → fallback). */
	metaFor(status: string): StatusMeta {
		return STATUS_META[status] ?? STATUS_META_FALLBACK;
	}

	// ── Tab 2 ──

	loadSentHistory(): void {
		this.service.getSentHistory().subscribe({
			next: rows => {
				this.sentEmails.set(rows);
				this.sentLoaded.set(true);
			},
			// A failed history load is non-critical — the status tab is the primary answer.
			error: () => {
				this.sentEmails.set([]);
				this.sentLoaded.set(true);
			}
		});
	}

	/** Open the modal and fetch this send's raw template (tokens unresolved). */
	openTemplate(row: PlayerSentEmailDto): void {
		this.templateOpen.set(true);
		this.templateLoading.set(true);
		this.templateError.set(null);
		this.templateSubject.set(row.subject || '(no subject)');
		this.templateHtml.set('');

		this.service.getSentTemplate(row.emailId).subscribe({
			next: html => {
				this.templateHtml.set(html || '');
				this.templateLoading.set(false);
			},
			error: () => {
				this.templateError.set('We could not load this template. Please try again.');
				this.templateLoading.set(false);
			}
		});
	}

	closeTemplate(): void {
		this.templateOpen.set(false);
		this.templateHtml.set('');
		this.templateError.set(null);
	}
}
