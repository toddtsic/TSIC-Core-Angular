import { Component, OnInit, inject, signal, effect, ViewChildren, AfterViewInit, QueryList, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';

import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { DropDownListModule, FilteringEventArgs, ChangeEventArgs, FieldSettingsModel, DropDownListComponent } from '@syncfusion/ej2-angular-dropdowns';
import { Query } from '@syncfusion/ej2-data';
import { WizardThemeDirective } from '@shared-ui/directives/wizard-theme.directive';

@Component({
  selector: 'app-role-selection',
  standalone: true,
  imports: [DropDownListModule, WizardThemeDirective],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  templateUrl: './role-selection.component.html',
  styleUrls: ['./role-selection.component.scss']
})
export class RoleSelectionComponent implements OnInit, AfterViewInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  registrations = signal<any[]>([]);
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);

  public fields: FieldSettingsModel = { text: 'displayText', value: 'regId' };

  private readonly _mirrorServiceState = effect(() => {
    this.registrations.set(this.authService.registrations());
    this.isLoading.set(this.authService.registrationsLoading());
    this.errorMessage.set(this.authService.registrationsError());
  });

  ngOnInit(): void {
    // Trigger fetch
    this.authService.loadAvailableRegistrations();
  }

  @ViewChildren(DropDownListComponent) readonly dropdowns!: QueryList<DropDownListComponent>;
  private _openedOnce = false;

  ngAfterViewInit(): void {
    this.tryOpenFirstDropdown();
    this.dropdowns.changes.subscribe(() => this.tryOpenFirstDropdown());
  }

  private tryOpenFirstDropdown(): void {
    if (this._openedOnce) return;
    const first = this.dropdowns?.first;
    if (first) {
      this._openedOnce = true;
      setTimeout(() => { try { first.showPopup(); } catch { /* no-op */ } }, 0);
    }
  }

  public onFiltering(e: FilteringEventArgs, roleGroup: any): void {
    const text = (e.text ?? '').trim();
    const query = text ? new Query().where('displayText', 'contains', text, true) : new Query();
    e.updateData(roleGroup.roleRegistrations, query);
  }

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

  private _wasSelecting = false;
  private readonly _navEffect = effect(() => {
    const loading = this.authService.selectLoading();
    const user = this.authService.getCurrentUser();

    if (loading) {
      this._wasSelecting = true;
      return;
    }

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