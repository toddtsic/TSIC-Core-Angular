import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';

import { WizardThemeDirective } from '@shared-ui/directives/wizard-theme.directive';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [WizardThemeDirective],
  templateUrl: './tsic-landing.component.html',
  styleUrl: './tsic-landing.component.scss'
})
export class TsicLandingComponent {
  private readonly router = inject(Router);

  navigateToLogin(): void {
    this.router.navigate(['/tsic/login'], { queryParams: { force: 1 } });
  }
}