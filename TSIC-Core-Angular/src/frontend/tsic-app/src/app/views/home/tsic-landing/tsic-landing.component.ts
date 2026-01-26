import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { LastLocationService } from '../../../infrastructure/services/last-location.service';

import { WizardThemeDirective } from '@shared-ui/directives/wizard-theme.directive';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [WizardThemeDirective],
  templateUrl: './tsic-landing.component.html',
  styleUrl: './tsic-landing.component.scss'
})
export class TsicLandingComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly lastLocation = inject(LastLocationService);
  private static hasInitialized = false;

  ngOnInit(): void {
    // Only redirect on first app load (not on subsequent navigations to /tsic)
    if (!TsicLandingComponent.hasInitialized) {
      TsicLandingComponent.hasInitialized = true;

      const lastJob = this.lastLocation.getLastJobPath();
      if (lastJob && lastJob !== 'tsic') {
        this.router.navigate([`/${lastJob}`]);
      }
    }
  }

  navigateToLogin(): void {
    this.router.navigate(['/tsic/login'], { queryParams: { force: 1 } });
  }
}