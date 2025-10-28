import { Component, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { DropDownListModule, FilteringEventArgs, ChangeEventArgs, FieldSettingsModel, DropDownListComponent } from '@syncfusion/ej2-angular-dropdowns';
import { ButtonModule } from '@syncfusion/ej2-angular-buttons';
import { Query } from '@syncfusion/ej2-data';

@Component({
  selector: 'app-role-selection',
  standalone: true,
  imports: [CommonModule, DropDownListModule, ButtonModule],
  templateUrl: './role-selection.component.html',
  styleUrls: ['./role-selection.component.scss']
})
export class RoleSelectionComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  @ViewChild('firstDropdown') firstDropdown!: DropDownListComponent;

  registrations: any[] = []; // Change to any[] temporarily
  isLoading = false;
  errorMessage: string | null = null;

  // Syncfusion DropdownList field mappings
  public fields: FieldSettingsModel = { text: 'displayText', value: 'regId' };

  ngOnInit(): void {
    this.isLoading = true;
    this.errorMessage = null;

    // Call API to get available registrations for the authenticated user
    this.authService.getAvailableRegistrations().subscribe({
      next: (registrations) => {
        this.registrations = registrations;
        this.isLoading = false;

        // Open the first dropdown after data is loaded
        setTimeout(() => {
          if (this.firstDropdown) {
            this.firstDropdown.showPopup();
          }
        }, 300);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || 'Failed to load registrations. Please try again.';
        console.error('Error loading registrations:', error);
      }
    });
  }

  // Handle typeahead filtering
  public onFiltering(e: FilteringEventArgs, roleGroup: any): void { // Change parameter type to any
    let query = new Query();
    // Filter based on the search text
    query = (e.text === '') ? query : query.where('displayText', 'contains', e.text, true);
    // Update the dropdown with filtered data
    e.updateData(roleGroup.roleRegistrations, query);
  }

  // Handle dropdown selection
  public onDropdownChange(e: ChangeEventArgs): void {
    if (e.itemData) {
      this.selectRole(e.itemData as any);
    }
  }

  selectRole(registration: any): void {
    if (this.isLoading) {
      return;
    }

    this.isLoading = true;
    this.errorMessage = null;

    // Call API to select registration and get full JWT token
    this.authService.selectRegistration(registration.regId).subscribe({
      next: (response) => {
        // Token with regId and jobPath claims is now stored
        const jobPath = this.authService.getJobPath();

        if (jobPath) {
          // Navigate to the job-specific path
          // Ensure path starts with / for router navigation
          const routePath = jobPath.startsWith('/') ? jobPath.substring(1) : jobPath;
          console.log('Navigating to jobPath:', routePath);
          this.router.navigate([routePath]);
        } else {
          console.warn('No jobPath found in token, redirecting to root');
          // Fallback to root if no jobPath in token
          this.router.navigate(['/']);
        }

        this.isLoading = false;
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.message || 'Role selection failed. Please try again.';
        console.error('Role selection error:', error);
      }
    });
  }

  logout(): void {
    this.authService.logout();
    this.router.navigate(['/']);
  }
}