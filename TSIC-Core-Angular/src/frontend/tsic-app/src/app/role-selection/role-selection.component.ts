import { Component, OnInit, OnDestroy, AfterViewInit, ViewChild, inject, signal, effect } from '@angular/core';
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
export class RoleSelectionComponent implements OnInit, AfterViewInit, OnDestroy {
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
    // Trigger fetch
    this.authService.loadAvailableRegistrations();

    // Reflect service signals into local state used by the template
    effect(() => {
      this.registrations.set(this.authService.registrations());
      this.isLoading.set(this.authService.registrationsLoading());
      this.errorMessage.set(this.authService.registrationsError());
    });
  }

  // Keep autofocus behavior once registrations load
  // (This uses a timeout intentionally for visual polish on first render)
  // No subscription to HTTP here; we rely on signals above
  // and only react to the local state
  // eslint-disable-next-line @typescript-eslint/member-ordering
  private _autofocusTimer: any;
  ngAfterViewInit(): void {
    this._autofocusTimer = setTimeout(() => {
      if (this.firstDropdown && (this.registrations().length > 0)) {
        this.firstDropdown.showPopup();
      }
    }, 150);
  }
  ngOnDestroy(): void {
    if (this._autofocusTimer) clearTimeout(this._autofocusTimer);
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

    this.errorMessage.set(null);
    this.authService.selectRegistrationCommand(registration.regId);

    // Navigate when jobPath is available (token updated)
    const jobPath = this.authService.getJobPath();
    if (jobPath) {
      const routePath = jobPath.startsWith('/') ? jobPath.substring(1) : jobPath;
      this.router.navigate([routePath]);
    }
  }

  logout(): void {
    this.authService.logout();
  }
}