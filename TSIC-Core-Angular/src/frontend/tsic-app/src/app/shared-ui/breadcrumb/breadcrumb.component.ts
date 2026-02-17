import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BreadcrumbService } from '@infrastructure/services/breadcrumb.service';

@Component({
	selector: 'app-breadcrumb',
	standalone: true,
	imports: [RouterLink],
	template: `
		@if (svc.visible()) {
			<nav class="breadcrumb-bar" aria-label="Breadcrumb">
				<ol class="breadcrumb-trail">
					@for (crumb of svc.trail(); track crumb.label; let last = $last) {
						<li class="breadcrumb-item" [class.active]="last">
							@if (crumb.url && !last) {
								<a [routerLink]="crumb.url" class="breadcrumb-link">
									@if (crumb.icon) {
										<i class="bi {{crumb.icon}}"></i>
									}
									<span>{{ crumb.label }}</span>
								</a>
							} @else {
								<span class="breadcrumb-current">{{ crumb.label }}</span>
							}
							@if (!last) {
								<i class="bi bi-chevron-right breadcrumb-separator"></i>
							}
						</li>
					}
				</ol>
			</nav>
		}
	`,
	styleUrl: './breadcrumb.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BreadcrumbComponent {
	readonly svc = inject(BreadcrumbService);
}
