import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { ApiService } from './core/services/api.service';

@Component({
  selector: 'tsic-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  private apiService = inject(ApiService);
  
  loading = signal(false);
  apiResponse = signal<string | null>(null);
  error = signal<string | null>(null);
  healthData = signal<any>(null);

  ngOnInit() {
    this.testApi();
  }

  testApi() {
    this.loading.set(true);
    this.error.set(null);
    this.apiResponse.set(null);
    this.healthData.set(null);

    this.apiService.testConnection().subscribe({
      next: (response) => {
        this.apiResponse.set(response);
        this.loading.set(false);
        
        this.apiService.getHealth().subscribe({
          next: (health) => this.healthData.set(health),
          error: (err) => console.error('Health check failed:', err)
        });
      },
      error: (err) => {
        this.error.set(`Failed to connect to API: `);
        this.loading.set(false);
        console.error('API Error:', err);
      }
    });
  }
}
