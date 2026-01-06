import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [],
  template: `
    <div class="container-fluid d-flex align-items-center justify-content-center min-vh-100">
      <div class="text-center">
        <h1 class="display-1 fw-bold">404</h1>
        <p class="fs-3 mb-4">Page Not Found</p>
        <p class="text-muted mb-4">
          The page you're looking for doesn't exist or the job path is invalid.
        </p>
        <button (click)="goHome()" class="btn btn-primary">
          <i class="bi bi-house-door me-2"></i>Go to Home
        </button>
      </div>
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }
    
    .min-vh-100 {
      min-height: 100vh;
    }
    
    .display-1 {
      color: var(--bs-danger);
    }
  `]
})
export class NotFoundComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  goHome(): void {
    const user = this.authService.getCurrentUser();

    if (user?.jobPath && user.jobPath !== 'tsic') {
      this.router.navigate([user.jobPath]);
    } else {
      this.router.navigate(['tsic']);
    }
  }
}

