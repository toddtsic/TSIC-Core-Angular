import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, filter, map, switchMap } from 'rxjs/operators';
import { catchError, of } from 'rxjs';

import { environment } from '@environments/environment';
import type { UsersByRoleDto, UsersByRoleRowDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import type { UsageQuery } from '../usage-analysis.models';

/**
 * Report 01 — Users by Role. Distinct people who used the scoped live events in the
 * window, counted by registration and grouped by role.
 *
 * People only. Anonymous traffic is requests, not people — nothing in the log can turn
 * an anonymous request into a visitor, and a registered user browsing before sign-in is
 * anonymous too — so it is deliberately absent rather than shown as a different unit
 * beside a people count.
 *
 * The admin tier (the director's own staff doing setup, and TSIC) is listed apart so it
 * never pads the customer-facing count.
 */
@Component({
	selector: 'app-usage-users-by-role',
	standalone: true,
	changeDetection: ChangeDetectionStrategy.OnPush,
	template: `
		@if (isLoading()) {
			<div class="ubr-state" role="status">
				<div class="spinner-border spinner-border-sm"></div>
				<span>Counting users…</span>
			</div>
		} @else if (error()) {
			<div class="ubr-state ubr-state--error" role="alert">
				<i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
				<span>{{ error() }}</span>
			</div>
		} @else if (data(); as d) {
			@if (d.rows.length === 0) {
				<div class="ubr-state">
					<i class="bi bi-person-slash" aria-hidden="true"></i>
					<span>No signed-in users in this window.</span>
				</div>
			} @else {
				<div class="ubr-tiles">
					<div class="ubr-tile ubr-tile--primary">
						<div class="ubr-tile-value">{{ d.customerUsers.toLocaleString() }}</div>
						<div class="ubr-tile-label">People using {{ eventWord() }}</div>
					</div>
					<div class="ubr-tile">
						<div class="ubr-tile-value">{{ d.adminUsers.toLocaleString() }}</div>
						<div class="ubr-tile-label">Admin &amp; staff</div>
					</div>
				</div>

				<table class="ubr-table">
					<thead>
						<tr>
							<th scope="col">Role</th>
							<th scope="col" class="ubr-num">Users</th>
							<th scope="col" class="ubr-bar-col"><span class="visually-hidden">Share</span></th>
						</tr>
					</thead>
					<tbody>
						@for (r of customerRows(); track r.roleName) {
							<tr>
								<td>{{ r.roleName }}</td>
								<td class="ubr-num">{{ r.users.toLocaleString() }}</td>
								<td class="ubr-bar-col">
									<div class="ubr-bar" [style.width.%]="share(r)" [title]="share(r).toFixed(0) + '%'"></div>
								</td>
							</tr>
						}
						@if (adminRows().length > 0) {
							<tr class="ubr-divider">
								<th scope="rowgroup" colspan="3">Admin &amp; staff</th>
							</tr>
							@for (r of adminRows(); track r.roleName) {
								<tr class="ubr-admin">
									<td>{{ r.roleName }}</td>
									<td class="ubr-num">{{ r.users.toLocaleString() }}</td>
									<td class="ubr-bar-col">
										<div class="ubr-bar ubr-bar--admin" [style.width.%]="share(r)" [title]="share(r).toFixed(0) + '%'"></div>
									</td>
								</tr>
							}
						}
					</tbody>
				</table>

				<p class="ubr-note">
					A user is a registration that made at least one request in the window. Someone holding
					two roles counts once under each. Public visitors who never sign in are not people this
					data can count and are not included.
				</p>
			}
		}
	`,
	styles: `
		:host {
			display: block;
		}

		.ubr-tiles {
			display: flex;
			flex-wrap: wrap;
			gap: var(--space-3);
			margin-bottom: var(--space-4);
		}

		.ubr-tile {
			min-width: 11rem;
			padding: var(--space-3) var(--space-4);
			background: var(--brand-surface);
			border: 1px solid var(--brand-border);
			border-radius: var(--radius-md, 0.375rem);
			box-shadow: var(--shadow-sm, 0 1px 2px rgba(0, 0, 0, 0.05));

			&.ubr-tile--primary {
				border-color: color-mix(in srgb, var(--bs-primary) 45%, var(--brand-border));
				background: var(--bs-primary-bg-subtle, var(--brand-surface));
			}
		}

		.ubr-tile-value {
			font-size: var(--font-size-2xl, 1.75rem);
			font-weight: 700;
			line-height: 1.1;
			color: var(--brand-text);
		}

		.ubr-tile-label {
			margin-top: var(--space-1);
			font-size: var(--font-size-sm);
			color: var(--brand-text-muted);
		}

		.ubr-table {
			width: 100%;
			max-width: 44rem;
			border-collapse: collapse;
			font-size: var(--font-size-sm);
			color: var(--brand-text);

			th, td {
				padding: var(--space-2) var(--space-3);
				border-bottom: 1px solid var(--brand-border);
				text-align: left;
				vertical-align: middle;
			}

			thead th {
				font-size: var(--font-size-xs);
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: 0.04em;
				color: var(--brand-text-muted);
			}
		}

		.ubr-num {
			text-align: right !important;
			font-variant-numeric: tabular-nums;
			white-space: nowrap;
		}

		.ubr-bar-col {
			width: 40%;
		}

		.ubr-bar {
			height: 0.6rem;
			min-width: 2px;
			border-radius: var(--radius-sm);
			background: var(--bs-primary);

			&.ubr-bar--admin {
				background: var(--brand-text-muted);
			}
		}

		.ubr-divider th {
			padding-top: var(--space-3);
			font-size: var(--font-size-xs);
			font-weight: 600;
			text-transform: uppercase;
			letter-spacing: 0.04em;
			color: var(--brand-text-muted);
			border-bottom: none;
		}

		.ubr-admin td {
			color: var(--brand-text-muted);
		}

		.ubr-note {
			max-width: 44rem;
			margin: var(--space-3) 0 0;
			font-size: var(--font-size-sm);
			color: var(--brand-text-muted);
		}

		.ubr-state {
			display: flex;
			align-items: center;
			justify-content: center;
			gap: var(--space-2);
			padding: var(--space-6) var(--space-4);
			font-size: var(--font-size-sm);
			color: var(--brand-text-muted);

			i {
				font-size: 1.25rem;
			}

			&.ubr-state--error {
				color: var(--bs-danger-text-emphasis);
			}
		}
	`,
})
export class UsersByRoleComponent implements OnInit {
	private readonly http = inject(HttpClient);
	private readonly destroyRef = inject(DestroyRef);
	private readonly state = inject(UsageAnalysisStateService);

	readonly data = signal<UsersByRoleDto | null>(null);
	readonly isLoading = signal(false);
	readonly error = signal<string | null>(null);

	readonly customerRows = computed(() => (this.data()?.rows ?? []).filter(r => !r.isAdmin));
	readonly adminRows = computed(() => (this.data()?.rows ?? []).filter(r => r.isAdmin));

	/** Bars are proportional to the largest row on the page, admin rows included. */
	private readonly maxUsers = computed(() =>
		Math.max(1, ...(this.data()?.rows ?? []).map(r => r.users)));

	readonly eventWord = computed(() => {
		const n = this.state.jobCount();
		return n === 1 ? 'this event' : `${n} live events`;
	});

	/**
	 * The query to run: the shell's query whenever the scope is resolved and non-empty,
	 * else null. Emitting null clears the report, so nothing lingers under a scope it was
	 * not fetched with.
	 */
	private readonly runnable = computed<UsageQuery | null>(() =>
		this.state.canQuery() ? this.state.query() : null);

	// Created here, in the injection context; subscribed in ngOnInit.
	private readonly runnable$ = toObservable(this.runnable);

	ngOnInit(): void {
		this.runnable$
			.pipe(
				map(q => q ? JSON.stringify(q) : null),
				distinctUntilChanged(),
				map(key => key ? (JSON.parse(key) as UsageQuery) : null),
				filter((q): q is UsageQuery => {
					if (q) return true;
					this.data.set(null);
					return false;
				}),
				switchMap(q => {
					this.isLoading.set(true);
					this.error.set(null);
					return this.http.get<UsersByRoleDto>(`${environment.apiUrl}/usage-analysis/users-by-role`, {
						params: { scope: q.scope, windowDays: q.windowDays, excludeBots: q.excludeBots },
					}).pipe(
						catchError(err => {
							this.error.set(err?.status === 403
								? 'That scope is not available to your role.'
								: 'Unable to load users by role.');
							return of(null);
						}),
					);
				}),
				takeUntilDestroyed(this.destroyRef),
			)
			.subscribe(d => {
				this.data.set(d);
				this.isLoading.set(false);
			});
	}

	share(r: UsersByRoleRowDto): number {
		return (r.users / this.maxUsers()) * 100;
	}
}
