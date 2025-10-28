import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'app-job-home',
  standalone: true,
  imports: [],
  templateUrl: './job-home.component.html',
  styleUrl: './job-home.component.scss'
})
export class JobHomeComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  username: string | null = null;
  jobPath: string | null = null;

  ngOnInit(): void {
    const user = this.authService.getCurrentUser();
    this.username = user?.username || null;
    this.jobPath = user?.jobPath || null;
  }

  changeRole(): void {
    this.router.navigate(['/tsic/role-selection']);
  }

  logout(): void {
    this.authService.logout();
  }
}
