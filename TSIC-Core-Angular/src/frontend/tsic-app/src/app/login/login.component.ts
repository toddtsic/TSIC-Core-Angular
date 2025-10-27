import { Component, OnInit, AfterViewInit, ElementRef, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { LoginRequest } from '../core/models/auth.models';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  standalone: true,
  imports: [FormsModule, CommonModule],
  styleUrls: ['./login.component.scss'],
})
export class LoginComponent implements AfterViewInit {
  @ViewChild('usernameInput', { static: false }) usernameInput!: ElementRef<HTMLInputElement>;
  @ViewChild('passwordInput', { static: false }) passwordInput!: ElementRef<HTMLInputElement>;

  username = '';
  password = '';
  isLoading = false;
  successMessage = '';
  errorMessage = '';

  constructor(
    private authService: AuthService,
    private router: Router
  ) { }

  ngAfterViewInit() {
    // Check for browser autofill after view initializes
    setTimeout(() => this.checkAutofill(), 100);
    setTimeout(() => this.checkAutofill(), 500);
    setTimeout(() => this.checkAutofill(), 1000);
  }

  checkAutofill() {
    // Chrome autofills the DOM but Angular doesn't detect it
    // Manually sync the values
    if (this.usernameInput && this.usernameInput.nativeElement.value && !this.username) {
      this.username = this.usernameInput.nativeElement.value;
    }
    if (this.passwordInput && this.passwordInput.nativeElement.value && !this.password) {
      this.password = this.passwordInput.nativeElement.value;
    }
  }

  onSubmit(event?: Event) {
    // Prevent default form submission
    if (event) {
      event.preventDefault();
    }

    if (!this.username || !this.password) {
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    const credentials: LoginRequest = {
      username: this.username,
      password: this.password
    };

    this.authService.login(credentials).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = 'Login successful! Redirecting...';

        // Store registrations and userId in sessionStorage for role selection
        sessionStorage.setItem('pendingUserId', response.userId);
        sessionStorage.setItem('pendingRegistrations', JSON.stringify(response.registrations));

        // Delay navigation to allow browser to detect successful login
        setTimeout(() => {
          this.router.navigate(['/role-selection']);
        }, 1500);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || 'Login failed. Please check your credentials.';
      }
    });
  }
}
