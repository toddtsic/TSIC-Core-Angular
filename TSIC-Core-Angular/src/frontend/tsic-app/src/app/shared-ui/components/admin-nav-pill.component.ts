import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * The quiet admin pill that links the public landing and the widget dashboard to
 * each other. ONE definition on purpose: the two are a matched pair, and a pair
 * that must look identical should not be two copies of the same markup and CSS.
 *
 * `link` takes a SEGMENT ARRAY that names the jobPath, e.g. ['/', jobPath, 'dashboard'],
 * never a relative string. The landing renders at BOTH `/:jobPath` and `/:jobPath/home`,
 * so a relative 'dashboard' resolves to `/:jobPath/home/dashboard` on the alias route
 * and 404s. Naming jobPath explicitly is correct from either, and still preserves the
 * prefix the routing rules require.
 *
 * Usage:
 *   <app-admin-nav-pill icon="bi-speedometer2" label="Dashboard" [link]="dashboardLink()" />
 *   <app-admin-nav-pill icon="bi-house" label="Home" [link]="homeLink()" />
 */
@Component({
	selector: 'app-admin-nav-pill',
	standalone: true,
	imports: [RouterLink],
	template: `
		<div class="anp">
			<a class="anp__link" [routerLink]="link()">
				<i class="bi {{ icon() }}" aria-hidden="true"></i>
				<span>{{ label() }}</span>
				<i class="bi bi-arrow-right anp__go" aria-hidden="true"></i>
			</a>
		</div>
	`,
	styles: [`
		.anp {
			display: flex;
			justify-content: flex-end;
			max-width: 960px;
			margin: var(--space-3) auto 0;
			padding: 0 var(--space-3);
		}

		.anp__link {
			display: inline-flex;
			align-items: center;
			gap: var(--space-2);
			padding: var(--space-2) var(--space-4);
			border: 1px solid var(--brand-border);
			border-radius: var(--radius-pill, 999px);
			background: var(--brand-surface);
			color: var(--brand-text);
			font-size: var(--font-size-sm);
			font-weight: var(--font-weight-semibold);
			text-decoration: none;
			box-shadow: var(--shadow-sm);
			transition: border-color 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;
		}

		.anp__link i {
			color: var(--bs-primary);
		}

		.anp__link:hover {
			border-color: var(--bs-primary);
			box-shadow: var(--shadow-md);
			transform: translateY(-1px);
		}

		.anp__link:focus-visible {
			outline: none;
			box-shadow: var(--shadow-focus);
		}

		.anp__go {
			font-size: 0.875em;
		}

		@media (prefers-reduced-motion: reduce) {
			.anp__link {
				transition: none !important;
			}

			.anp__link:hover {
				transform: none;
			}
		}
	`],
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminNavPillComponent {
	/** Bootstrap icon class, e.g. 'bi-speedometer2'. */
	readonly icon = input.required<string>();
	readonly label = input.required<string>();
	/** Router segment array INCLUDING the jobPath — see the class comment. */
	readonly link = input.required<unknown[]>();
}
