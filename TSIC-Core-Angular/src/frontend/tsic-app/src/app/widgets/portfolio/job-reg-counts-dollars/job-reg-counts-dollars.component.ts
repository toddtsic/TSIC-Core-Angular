import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { AuthService } from '@infrastructure/services/auth.service';
import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import { dateKey, tableSort, textKey } from '@shared-ui/table-sort';
import type { JobRegCountsAndDollarsDto, JobRegCountsAndDollarsRowDto } from '@core/api';

/** Sortable columns. */
type SortCol = 'event' | 'type' | 'starts' | 'players' | 'teams' | 'fees' | 'paid' | 'owed';

/**
 * JobRegCountsAndDollars — live-jobs portfolio.
 *
 * ONE summary line, ONE accordion. Collapsed, the widget is a single rollup row; opening
 * it reveals the per-job rows. (Not an accordion per job — that made the reader open N
 * drawers to answer a question the table already answers in its columns.)
 *
 * Scope is LIVE jobs (ExpiryUsers > now) of the customer the caller is standing in,
 * resolved server-side from the token.
 *
 * BOTH count units ride every row alongside the job type name. The registration-allow
 * flags are an open/closed door switch, NOT a billable-unit classification — 30% of live
 * jobs have both flags off while still holding players, teams and money — so column
 * visibility is never driven off them. A blank count is information, not a gap.
 */
@Component({
	selector: 'app-job-reg-counts-dollars',
	standalone: true,
	imports: [CurrencyPipe, DatePipe, DecimalPipe, RouterLink],
	templateUrl: './job-reg-counts-dollars.component.html',
	styleUrl: './job-reg-counts-dollars.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobRegCountsAndDollarsComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);
	private readonly auth = inject(AuthService);

	readonly data = signal<JobRegCountsAndDollarsDto | null>(null);
	readonly isLoading = signal(true);
	readonly hasError = signal(false);

	/** The single accordion: closed shows the rollup line only. */
	readonly isOpen = signal(false);

	readonly rows = computed(() => this.data()?.rows ?? []);
	readonly isEmpty = computed(() => !this.isLoading() && !this.hasError() && this.rows().length === 0);

	/**
	 * Shared heading-sort state (shared-ui/table-sort). Opens on event date ascending —
	 * soonest first is the working order for "what needs me next". Money and counts open
	 * DESCENDING because someone clicking Owed or Teams is hunting the biggest, not the
	 * smallest; text columns open A–Z.
	 */
	readonly sort = tableSort<SortCol>('starts', {
		players: 'desc', teams: 'desc', fees: 'desc', paid: 'desc', owed: 'desc',
	});

	/**
	 * Comparators are written ASCENDING; table-sort applies direction. Event and Type sort
	 * on what is DISPLAYED (stripped name, abbreviated type), not the stored value — a
	 * column that sorts by text the reader cannot see reads as broken.
	 */
	readonly sortedRows = this.sort.applyTo(this.rows, (col, a, b) => {
		switch (col) {
			case 'event':   return textKey(this.eventName(a), this.eventName(b));
			case 'type':    return textKey(this.typeLabel(a), this.typeLabel(b));
			case 'starts':  return dateKey(a.eventStartDate) - dateKey(b.eventStartDate);
			case 'players': return a.playerCount - b.playerCount;
			case 'teams':   return a.teamCount - b.teamCount;
			case 'fees':    return a.fees - b.fees;
			case 'paid':    return a.paid - b.paid;
			case 'owed':    return a.owed - b.owed;
		}
	});

	/**
	 * Deep detail lives on Customer Job Revenue — already gated to the same audience
	 * (Superuser + SuperDirector). Segment array naming the jobPath explicitly, the
	 * same shape admin-nav-pill uses: a relative link would resolve under the
	 * dashboard route instead of the job root.
	 */
	readonly detailLink = computed(() => {
		const jobPath = this.auth.currentUser()?.jobPath ?? '';
		return ['/', jobPath, 'tools', 'customer-job-revenue'];
	});

	ngOnInit(): void {
		this.load();
	}

	load(): void {
		this.isLoading.set(true);
		this.hasError.set(false);

		this.svc.getJobRegCountsAndDollars().subscribe({
			next: d => {
				this.data.set(d);
				this.isLoading.set(false);
			},
			error: () => {
				this.hasError.set(true);
				this.isLoading.set(false);
			},
		});
	}

	toggle(): void {
		this.isOpen.set(!this.isOpen());
	}

	/** Days until the event starts; null when the job carries no event date. */
	daysOut(row: JobRegCountsAndDollarsRowDto): number | null {
		if (!row.eventStartDate) return null;
		const start = new Date(row.eventStartDate).getTime();
		const today = new Date().setHours(0, 0, 0, 0);
		return Math.round((start - today) / 86_400_000);
	}

	/**
	 * Short labels for reference.JobTypes. The stored names carry a trailing noun that
	 * is pure column width here ("Tournament Scheduling", "Showcase Registration") —
	 * the distinguishing word is the first one.
	 *
	 * An explicit map, NOT string surgery on the suffix: a new or renamed job type
	 * falls through to its full stored name, which is merely wide. Trimming blindly
	 * would silently mangle it instead.
	 */
	private static readonly TYPE_LABELS: Readonly<Record<string, string>> = {
		'Tournament Scheduling': 'Tournament',
		'League Scheduling': 'League',
		'Club Sport Registration': 'Club Sport',
		'Camp Registration': 'Camp',
		'Showcase Registration': 'Showcase',
		'Sales Venue': 'Sales',
		'Customer Root': 'Root',
	};

	/** Abbreviated job type, falling back to the stored name when unmapped. */
	typeLabel(row: JobRegCountsAndDollarsRowDto): string {
		return JobRegCountsAndDollarsComponent.TYPE_LABELS[row.jobTypeName] ?? row.jobTypeName;
	}

	/**
	 * Job names are stored "Customer:Event" ("Top Threat Tournaments:Fall Draw 2026").
	 * The customer half is identical on every row — the table is one customer's live
	 * jobs by definition — so it is dropped from the primary line and the event kept.
	 * Split on the FIRST colon only: event names may contain their own.
	 */
	eventName(row: JobRegCountsAndDollarsRowDto): string {
		const i = row.jobName.indexOf(':');
		return i < 0 ? row.jobName : row.jobName.slice(i + 1).trim();
	}

	/** The dropped customer half, or '' when the name carries no prefix. */
	customerName(row: JobRegCountsAndDollarsRowDto): string {
		const i = row.jobName.indexOf(':');
		return i < 0 ? '' : row.jobName.slice(0, i).trim();
	}

	/** Stable identity for @for. */
	trackRow(_i: number, row: JobRegCountsAndDollarsRowDto): string {
		return row.jobId;
	}
}
