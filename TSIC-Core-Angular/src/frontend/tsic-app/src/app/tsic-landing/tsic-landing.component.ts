import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [],
  templateUrl: './tsic-landing.component.html',
  styleUrl: './tsic-landing.component.scss'
})
export class TsicLandingComponent {
    private readonly router = inject(Router);

    navigateToLogin(): void {
        this.router.navigate(['/tsic/login']);
    }
}