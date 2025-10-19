import { Component } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { LoginService } from './login.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  standalone: true,
  imports: [ReactiveFormsModule],
  styleUrls: ['./login.component.scss'],
})
export class LoginComponent {
  loginForm: FormGroup;
  registrations: Array<{ regId: string, displayText: string }> = [];
  dropdownDisabled = true;
  submitDisabled = true;

  constructor(private fb: FormBuilder, private loginService: LoginService, private router: Router) {
    this.loginForm = this.fb.group({
      username: ['', Validators.required],
      password: ['', Validators.required],
      registration: [{ value: '', disabled: true }, Validators.required]
    });
  }

  onCredentialsChange() {
    const username = this.loginForm.get('username')?.value;
    const password = this.loginForm.get('password')?.value;
    if (username && password) {
      this.loginService.validateCredentials(username, password).subscribe({
        next: (result) => {
          if (result && Array.isArray(result.registrations) && result.registrations.length > 0) {
            // result.registrations is an array of { regId, displayText }
            this.registrations = result.registrations;
            this.dropdownDisabled = false;
            this.loginForm.get('registration')?.enable();
          } else {
            this.registrations = [];
            this.dropdownDisabled = true;
            this.loginForm.get('registration')?.disable();
          }
        },
        error: () => {
          this.registrations = [];
          this.dropdownDisabled = true;
          this.loginForm.get('registration')?.disable();
        }
      });
    } else {
      this.registrations = [];
      this.dropdownDisabled = true;
      this.loginForm.get('registration')?.disable();
    }
    this.submitDisabled = true;
  }

  onRegistrationSelect() {
    const regId = this.loginForm.get('registration')?.value;
    this.submitDisabled = !regId;
  }

  onSubmit() {
    const username = this.loginForm.get('username')?.value;
    const regId = this.loginForm.get('registration')?.value;
    if (username && regId) {
      this.loginService.getJwtToken(username, regId).subscribe({
        next: (result) => {
          if (result && result.token) {
            localStorage.setItem('jwt', result.token);
            this.router.navigate(['/job', regId]);
          }
        },
        error: () => {
          // Handle error (e.g., show message)
        }
      });
    }
  }
}
