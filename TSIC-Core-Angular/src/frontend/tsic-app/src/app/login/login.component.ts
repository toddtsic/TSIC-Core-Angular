import { Component, AfterViewInit, ElementRef, ViewChild } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { LoginRequest } from '../core/models/auth.models';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  standalone: true,
  imports: [ReactiveFormsModule, CommonModule],
  styleUrls: ['./login.component.scss'],
})
export class LoginComponent implements AfterViewInit {
  @ViewChild('usernameInput', { static: false }) usernameInput!: ElementRef<HTMLInputElement>;
  @ViewChild('passwordInput', { static: false }) passwordInput!: ElementRef<HTMLInputElement>;

  form!: FormGroup;
  isLoading = false;
  errorMessage = '';
  submitted = false;

  constructor(
    private readonly authService: AuthService,
    private readonly router: Router,
    private readonly fb: FormBuilder
  ) {
    this.form = this.fb.group({
      username: ['', [Validators.required]],
      password: ['', [Validators.required]],
    });
  }

  ngAfterViewInit() {
    // One-time sync in case the browser autofilled without firing input events
    setTimeout(() => this.syncAutofillOnce(), 250);
  }

  private syncAutofillOnce() {
    const u = this.usernameInput?.nativeElement.value;
    const p = this.passwordInput?.nativeElement.value;
    if (u && !this.form.get('username')?.value) {
      this.form.get('username')?.setValue(u);
    }
    if (p && !this.form.get('password')?.value) {
      this.form.get('password')?.setValue(p);
    }
  }

  onSubmit(event?: Event) {
    // Prevent default browser submission
    if (event) event.preventDefault();

    this.submitted = true;
    if (this.form.invalid) return;

    this.isLoading = true;
    this.errorMessage = '';

    const credentials: LoginRequest = {
      username: this.form.get('username')?.value ?? '',
      password: this.form.get('password')?.value ?? ''
    };

    this.authService.login(credentials).subscribe({
      next: (response) => {
        this.isLoading = false;

        // Store registrations and userId in sessionStorage for role selection
        sessionStorage.setItem('pendingUserId', response.userId);
        sessionStorage.setItem('pendingRegistrations', JSON.stringify(response.registrations));

        // Navigate immediately to role selection
        this.router.navigate(['/role-selection']);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || 'Login failed. Please check your credentials.';
      }
    });
  }
}
