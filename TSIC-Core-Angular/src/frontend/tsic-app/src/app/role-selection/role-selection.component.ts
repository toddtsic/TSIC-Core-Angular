import { Component, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { DropDownListModule, FilteringEventArgs, ChangeEventArgs, FieldSettingsModel, DropDownListComponent } from '@syncfusion/ej2-angular-dropdowns';
import { Query } from '@syncfusion/ej2-data';

@Component({
  selector: 'app-role-selection',
  standalone: true,
  imports: [CommonModule, DropDownListModule],
  templateUrl: './role-selection.component.html',
  styleUrls: ['./role-selection.component.scss']
})
export class RoleSelectionComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  @ViewChild('firstDropdown') firstDropdown!: DropDownListComponent;

  // Signals instead of observables
  registrations = signal<any[]>([]);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  // Syncfusion DropdownList field mappings
  public fields: FieldSettingsModel = { text: 'displayText', value: 'regId' };

  ngOnInit(): void {
    this.loadRegistrations();
  }

  private loadRegistrations(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.authService.getAvailableRegistrations().subscribe({
      next: (registrations) => {
        this.registrations.set(registrations);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Failed to load registrations. Please try again.');
        console.error('Error loading registrations:', error);
      }
    });
  }

  // Handle typeahead filtering
  public onFiltering(e: FilteringEventArgs, roleGroup: any): void {
    let query = new Query();
    query = (e.text === '') ? query : query.where('displayText', 'contains', e.text, true);
    e.updateData(roleGroup.roleRegistrations, query);
  }

  // Handle dropdown selection
  public onDropdownChange(e: ChangeEventArgs): void {
    if (e.itemData) {
      this.selectRole(e.itemData as any);
    }
  }

  selectRole(registration: any): void {
    if (this.isLoading()) {
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.authService.selectRegistration(registration.regId).subscribe({
      next: (response) => {
        const jobPath = this.authService.getJobPath();

        if (jobPath) {
          const routePath = jobPath.startsWith('/') ? jobPath.substring(1) : jobPath;
          console.log('Navigating to jobPath:', routePath);
          this.router.navigate([routePath]);
        } else {
          console.warn('No jobPath found in token, redirecting to root');
          this.router.navigate(['/']);
        }

        this.isLoading.set(false);
      },
      error: (error) => {
        this.isLoading.set(false);
        this.errorMessage.set(error.error?.message || 'Role selection failed. Please try again.');
        console.error('Role selection error:', error);
      }
    });
  }

  logout(): void {
    this.authService.logout();
  }
}