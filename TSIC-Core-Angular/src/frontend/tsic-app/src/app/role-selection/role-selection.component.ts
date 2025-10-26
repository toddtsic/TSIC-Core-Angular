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
    private authService: AuthService,
    private router: Router
  ) {
    // Get navigation state
    const navigation = this.router.getCurrentNavigation();
    if (navigation?.extras.state) {
      this.userId = navigation.extras.state['userId'];
      this.registrations = navigation.extras.state['registrations'];
    }
  }

  ngOnInit() {
    // Redirect to login if no state data
    if (!this.userId || !this.registrations || this.registrations.length === 0) {
      this.router.navigate(['/login']);
    }
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
    this.router.navigate(['/login']);
  }
}
