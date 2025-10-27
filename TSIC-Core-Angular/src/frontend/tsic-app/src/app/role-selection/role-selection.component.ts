import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { RegistrationRoleDto, RegistrationDto, RoleSelectionRequest } from '../core/models/auth.models';

@Component({
  selector: 'app-role-selection',
  templateUrl: './role-selection.component.html',
  standalone: true,
  imports: [CommonModule],
  styleUrls: ['./role-selection.component.scss']
})
export class RoleSelectionComponent implements OnInit {
  userId: string = '';
  registrations: RegistrationRoleDto[] = [];
  isLoading = false;
  errorMessage = '';

  constructor(
    private readonly authService: AuthService,
    private readonly router: Router
  ) { }

  ngOnInit() {
    // Get data from sessionStorage
    const userId = sessionStorage.getItem('pendingUserId');
    const registrationsJson = sessionStorage.getItem('pendingRegistrations');

    if (userId && registrationsJson) {
      this.userId = userId;
      const parsed = JSON.parse(registrationsJson);
      this.registrations = this.normalizeRegistrations(parsed);

      // Clear the pending data
      sessionStorage.removeItem('pendingUserId');
      sessionStorage.removeItem('pendingRegistrations');
    } else {
      // No data available, redirect to login
      this.router.navigate(['/']);
    }
  }

  /**
   * Normalize registrations to grouped form expected by the template.
   * Accepts either:
   * - RegistrationRoleDto[] (already grouped)
   * - RegistrationDto[] (flat list) -> wrapped into a single group
   */
  private normalizeRegistrations(input: any): RegistrationRoleDto[] {
    if (!Array.isArray(input)) {
      return [];
    }
    // Already grouped
    if (input.length === 0) return [];
    const first = input[0];
    if (first && typeof first === 'object' && 'roleRegistrations' in first) {
      return input as RegistrationRoleDto[];
    }
    // Flat list -> wrap
    return [{
      roleName: 'Registrations',
      roleRegistrations: input as RegistrationDto[]
    }];
  }

  selectRole(registration: RegistrationDto) {
    this.isLoading = true;
    this.errorMessage = '';

    const request: RoleSelectionRequest = {
      userId: this.userId,
      regId: registration.regId
    };

    this.authService.selectRole(request).subscribe({
      next: (response) => {
        this.isLoading = false;
        // Navigate to job view with the selected registration
        this.router.navigate(['/job', registration.regId]);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || 'Role selection failed. Please try again.';
      }
    });
  }

  logout() {
    this.authService.logout();
    this.router.navigate(['/']);
  }
}
