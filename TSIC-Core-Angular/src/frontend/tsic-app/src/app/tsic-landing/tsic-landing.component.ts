import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from '@syncfusion/ej2-angular-buttons';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [ButtonModule],
  templateUrl: './tsic-landing.component.html',
  styleUrl: './tsic-landing.component.scss'
})
export class TsicLandingComponent {
  private readonly router = inject(Router);

  navigateToLogin(): void {
    this.router.navigate(['/tsic/login']);
  }
}