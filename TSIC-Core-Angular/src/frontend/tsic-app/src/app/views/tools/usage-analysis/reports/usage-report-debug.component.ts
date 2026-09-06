import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import { environment } from '@environments/environment';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import { USAGE_SCOPE_OPTIONS, type UsageReportDef } from '../usage-analysis.models';

/**
 * DEBUG surface standing in for every report while the role/scope plumbing is being
 * trusted. Prints what the shell would hand a real report: who is asking, what was
 * picked, what the server resolved the scope to, and the URL a real report would call.
 *
 * Replace with the real report component when a slot is claimed.
 */
@Component({
	selector: 'app-usage-report-debug',
	standalone: true,
	changeDetection: ChangeDetectionStrategy.OnPush,
	template: `
		<div class="dbg">
			<div class="dbg-title">
				<i class="bi bi-bug" aria-hidden="true"></i>
				{{ report().label }} — debug
			</div>

			<dl class="dbg-grid">
				<dt>Role</dt>
				<dd>{{ state.role() || '(none)' }}</dd>

				<dt>Ceiling</dt>
				<dd>{{ state.ceiling() }}</dd>

				<dt>Reports offered</dt>
				<dd>{{ reportsOffered() }}</dd>

				<dt>Report</dt>
				<dd><code>{{ report().key }}</code> — {{ report().label }}</dd>

				<dt>Scope (picked)</dt>
				<dd><code>{{ state.scope() }}</code> — {{ scopeLabel() }}</dd>

				<dt>Window</dt>
				<dd>{{ state.windowDays() }} {{ state.windowDays() === 1 ? 'day' : 'days' }}</dd>

				<dt>Bots</dt>
				<dd>{{ state.excludeBots() ? 'hidden (excludeBots=true)' : 'included (excludeBots=false)' }}</dd>

				<dt>Would call</dt>
				<dd><code>{{ url() }}</code></dd>
			</dl>

			@if (state.scopeInfo(); as info) {
				<div class="dbg-subtitle">Server resolved</div>
				<dl class="dbg-grid">
					<dt>Scope (server)</dt>
					<dd><code>{{ info.scope }}</code></dd>

					<dt>Max scope</dt>
					<dd><code>{{ info.maxScope }}</code></dd>

					<dt>Logging available</dt>
					<dd>{{ info.usageLoggingAvailable }}</dd>

					<dt>Current job live</dt>
					<dd>{{ info.currentJobIsLive }}</dd>

					<dt>Live events</dt>
					<dd>
						{{ info.jobs.length }}
						@if (info.jobs.length > 0) {
							<ol class="dbg-jobs">
								@for (j of info.jobs; track j.jobId) {
									<li>{{ j.jobName }} <span class="dbg-id">{{ j.jobId }}</span></li>
								}
							</ol>
						}
					</dd>
				</dl>
			}
		</div>
	`,
	styles: `
		.dbg {
			padding: var(--space-4);
			font-family: var(--font-family-mono, ui-monospace, monospace);
			font-size: var(--font-size-sm);
			color: var(--brand-text);
			background: var(--brand-surface);
			border: 1px dashed var(--brand-border);
			border-radius: var(--radius-md, 0.375rem);
		}
		.dbg-title {
			font-weight: 600;
			margin-bottom: var(--space-3);

			i {
				color: var(--bs-warning);
				margin-right: var(--space-1);
			}
		}
		.dbg-subtitle {
			font-weight: 600;
			margin: var(--space-4) 0 var(--space-2);
			color: var(--brand-text-muted);
		}
		.dbg-grid {
			display: grid;
			grid-template-columns: max-content 1fr;
			gap: var(--space-1) var(--space-4);
			margin: 0;

			dt {
				color: var(--brand-text-muted);
			}
			dd {
				margin: 0;
				overflow-wrap: anywhere;
			}
		}
		.dbg-jobs {
			margin: var(--space-1) 0 0;
			padding-left: var(--space-5);
		}
		.dbg-id {
			color: var(--brand-text-muted);
			font-size: var(--font-size-xs);
		}
	`,
})
export class UsageReportDebugComponent {
	readonly state = inject(UsageAnalysisStateService);

	readonly report = input.required<UsageReportDef>();

	readonly scopeLabel = computed(() =>
		USAGE_SCOPE_OPTIONS.find(o => o.scope === this.state.scope())?.label ?? this.state.scope());

	readonly reportsOffered = computed(() =>
		this.state.reports().map(r => r.label).join(', '));

	/** The request a real report would make: report key as the path, the query as params. */
	readonly url = computed(() => {
		const q = this.state.query();
		return `GET ${environment.apiUrl}/usage-analysis/${this.report().key}`
			+ `?scope=${q.scope}&windowDays=${q.windowDays}&excludeBots=${q.excludeBots}`;
	});
}
