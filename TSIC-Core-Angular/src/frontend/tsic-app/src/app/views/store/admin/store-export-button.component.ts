import { Component, ChangeDetectionStrategy, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ReportingService } from '../../../infrastructure/services/reporting.service';

/**
 * The Excel Export button that sits on every store admin grid — port of the EJ2 toolbar's
 * `ExcelExport` item.
 *
 * Four grids need the identical behaviour (fetch a blob, disable while it is in flight, save it,
 * say so if it fails), so it lives here once. The caller supplies a FACTORY rather than an
 * observable: an observable input would fire the export on render instead of on click.
 *
 * The blob becomes a file through `ReportingService.triggerDownload`, the one place in the app
 * that does so — filename comes from the server's Content-Disposition.
 */
@Component({
	selector: 'app-store-export-button',
	standalone: true,
	imports: [CommonModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	template: `
		<button
			type="button"
			class="btn btn-outline-secondary btn-sm store-export-btn"
			[disabled]="isBusy()"
			[title]="hint()"
			(click)="run()">
			@if (isBusy()) {
				<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>
			} @else {
				<i class="bi bi-file-earmark-spreadsheet me-1"></i>
			}
			{{ label() }}
		</button>
		@if (errorMessage(); as message) {
			<span class="store-export-error" role="alert">{{ message }}</span>
		}
	`,
	styles: [`
		.store-export-btn { white-space: nowrap; }

		.store-export-btn:focus-visible {
			outline: none;
			box-shadow: var(--shadow-focus);
		}

		.store-export-error {
			margin-left: var(--space-2);
			font-size: var(--font-size-sm);
			color: var(--bs-danger);
		}
	`],
})
export class StoreExportButtonComponent {
	private readonly reporting = inject(ReportingService);

	/** Called on click. A factory, not an observable — see the class comment. */
	readonly fetch = input.required<() => Observable<HttpResponse<Blob>>>();

	readonly label = input('Export');
	readonly hint = input('Download this grid as an Excel workbook');

	readonly isBusy = signal(false);
	readonly errorMessage = signal<string | null>(null);

	run(): void {
		if (this.isBusy()) return;
		this.isBusy.set(true);
		this.errorMessage.set(null);

		this.fetch()().subscribe({
			next: response => {
				this.reporting.triggerDownload(response);
				this.isBusy.set(false);
			},
			error: () => {
				this.errorMessage.set('Export failed.');
				this.isBusy.set(false);
			},
		});
	}
}
