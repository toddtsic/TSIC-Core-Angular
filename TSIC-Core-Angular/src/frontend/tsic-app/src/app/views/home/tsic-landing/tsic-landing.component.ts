import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';

import { WizardThemeDirective } from '@shared-ui/directives/wizard-theme.directive';
import { LastLocationService } from '@infrastructure/services/last-location.service';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [WizardThemeDirective],
  templateUrl: './tsic-landing.component.html',
  styleUrl: './tsic-landing.component.scss'
})
export class TsicLandingComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly lastLocationService = inject(LastLocationService);

  ngOnInit(): void {
    // Check if user has a saved job path and redirect there
    const lastJobPath = this.lastLocationService.getLastJobPath();
    if (lastJobPath) {
      this.router.navigate([`/${lastJobPath}`]);
    }
  }

  navigateToLogin(): void {
    this.router.navigate(['/tsic/login'], { queryParams: { force: 1 } });
  }
}