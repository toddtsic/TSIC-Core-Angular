import { Component, OnInit, inject, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { DropDownListModule, FilteringEventArgs, ChangeEventArgs, FieldSettingsModel } from '@syncfusion/ej2-angular-dropdowns';
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


  // Signals instead of observables
  registrations = signal<any[]>([]);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  // Syncfusion DropdownList field mappings
  public fields: FieldSettingsModel = { text: 'displayText', value: 'regId' };

  // Reflect service signals into local state used by the template
  // Define as a field initializer so it runs within the component's injection context
  private readonly _mirrorServiceState = effect(() => {
    this.registrations.set(this.authService.registrations());
    this.isLoading.set(this.authService.registrationsLoading());
    this.errorMessage.set(this.authService.registrationsError());
  }, { allowSignalWrites: true });

  ngOnInit(): void {
    // Trigger fetch
    this.authService.loadAvailableRegistrations();
  }

  // No extra lifecycle hooks needed; popup opens from (created)/(dataBound) in the template
  // Auto-open support for the first dropdown: call showPopup once it is created/bound.
  // We defensively queue the call to ensure the popup can calculate sizes.
  public onFirstDropdownCreated(dd: any): void {
    try {
      // Microtask first
      queueMicrotask(() => dd?.showPopup?.());
      // Fallback in case microtask runs too early
      setTimeout(() => dd?.showPopup?.(), 0);
    } catch { /* no-op */ }
  }

  public onFirstDropdownDataBound(dd: any): void {
    try {
      dd?.showPopup?.();
    } catch { /* no-op */ }
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
  }

  // Navigate to job path only after a selection action completes
  // Track a local flag to ensure we only navigate on a true selection lifecycle
  private _wasSelecting = false;
  private readonly _navEffect = effect(() => {
    const loading = this.authService.selectLoading();
    const user = this.authService.getCurrentUser();

    // Mark when a selection is in-flight
    if (loading) {
      this._wasSelecting = true;
      return;
    }

    // Only navigate when a selection just finished successfully
    if (!loading && this._wasSelecting && user?.jobPath) {
      const routePath = user.jobPath.startsWith('/') ? user.jobPath.substring(1) : user.jobPath;
      this._wasSelecting = false;
      this.router.navigate([routePath]);
    }
  });

  logout(): void {
    this.authService.logout();
  }
}