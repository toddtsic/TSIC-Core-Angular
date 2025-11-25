import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { WizardThemeDirective } from '../shared/directives/wizard-theme.directive';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [CommonModule, WizardThemeDirective, MatButtonModule, MatIconModule],
  templateUrl: './tsic-landing.component.html',
  styleUrl: './tsic-landing.component.scss'
})
export class TsicLandingComponent {
  private readonly router = inject(Router);

  navigateToLogin(): void {
    this.router.navigate(['/tsic/login'], { queryParams: { force: 1 } });
  }
}