import { ChangeDetectionStrategy, Component, OnInit, inject, computed, signal, ViewChildren, AfterViewInit, QueryList, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { DropDownListModule, FilteringEventArgs, ChangeEventArgs, FieldSettingsModel, DropDownListComponent } from '@syncfusion/ej2-angular-dropdowns';
import { Query } from '@syncfusion/ej2-data';
@Component({
  selector: 'app-role-selection',
  standalone: true,
  imports: [DropDownListModule],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  templateUrl: './role-selection.component.html',
  styleUrls: ['./role-selection.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RoleSelectionComponent implements OnInit, AfterViewInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly registrations = computed(() => this.authService.registrations());
  readonly isLoading = computed(() => this.authService.registrationsLoading() || this.selectingRole());
  readonly errorMessage = computed(() => this.authService.registrationsError() ?? this.authService.selectError());
  readonly username = computed(() => this.authService.currentUser()?.username ?? '');

  /** Local UI signal for selection in progress */
  readonly selectingRole = signal(false);

  public fields: FieldSettingsModel = { text: 'displayText', value: 'regId' };

  /** Optional returnUrl from query params — honored after role selection (e.g. store flow) */
  private _returnUrl: string | null = null;

  ngOnInit(): void {
    this._returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
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
    // Skip auto-open on mobile — Syncfusion opens a full-screen overlay on touch devices
    if (window.innerWidth < 768) return;
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

    this.selectingRole.set(true);
    this.authService.selectRegistration(registration.regId).subscribe({
      next: () => {
        this.selectingRole.set(false);
        const user = this.authService.getCurrentUser();
        if (this._returnUrl) {
          this.router.navigateByUrl(this._returnUrl);
        } else if (user?.jobPath) {
          const routePath = user.jobPath.startsWith('/') ? user.jobPath.substring(1) : user.jobPath;
          this.router.navigate([routePath]);
        }
      },
      error: () => {
        this.selectingRole.set(false);
      }
    });
  }

  logout(): void {
    this.authService.logout();
  }
}
