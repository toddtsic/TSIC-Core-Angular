import { ChangeDetectionStrategy, Component, computed, inject, signal, OnInit, OnDestroy, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { AdministratorService } from '../services/administrator.service';
import { Subject, takeUntil } from 'rxjs';
import type { AdministratorDto, AddAdministratorRequest, UpdateAdministratorRequest, UserSearchResultDto } from '@core/api';

export type ModalMode = 'add' | 'edit';

export interface AdminFormResult {
    mode: ModalMode;
    addRequest?: AddAdministratorRequest;
    updateRequest?: UpdateAdministratorRequest;
    registrationId?: string;
}

@Component({
    selector: 'admin-form-modal',
    standalone: true,
    imports: [TsicDialogComponent, FormsModule],
    template: `
        <tsic-dialog [open]="true" size="sm" (requestClose)="close.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ mode() === 'add' ? 'Add Administrator' : 'Edit Administrator' }}</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <div class="row g-2">
                        <!-- Lane model (AM-004): eligibility depends on the role being granted,
                             so the role is chosen FIRST and the search is scoped to its lane. -->
                        <div class="col-12">
                            <label for="roleSelect" class="field-label">Role</label>
                            <select id="roleSelect" class="field-input field-select"
                                [ngModel]="selectedRole()"
                                (ngModelChange)="onRoleChange($event)">
                                <option value="" disabled>Select a role...</option>
                                @for (role of roleOptions(); track role) {
                                    <option [value]="role">{{ role }}</option>
                                }
                            </select>
                        </div>

                        @if (mode() === 'add') {
                            <div class="col-12">
                                <label for="userSearch" class="field-label">Username</label>
                                <div class="d-flex gap-2">
                                    <input
                                        id="userSearch"
                                        type="text"
                                        class="field-input"
                                        [placeholder]="selectedRole() ? 'Name or username...' : 'Select a role first...'"
                                        [disabled]="!selectedRole()"
                                        [value]="searchInput()"
                                        (input)="onSearchInput($event)"
                                        (keyup.enter)="onSearchSubmit()"
                                        autocomplete="off" />
                                    <button
                                        type="button"
                                        class="btn btn-primary flex-shrink-0"
                                        [disabled]="!selectedRole() || searchInput().trim().length < 2 || searching()"
                                        (click)="onSearchSubmit()">
                                        @if (searching()) {
                                            <span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>
                                        } @else {
                                            <i class="bi bi-search me-1"></i>
                                        }Search
                                    </button>
                                </div>
                                @if (selectedRole()) {
                                    <small class="text-body-secondary d-block mt-1">
                                        Eligible: accounts whose active registrations are all
                                        {{ selectedRole() === 'Director' || selectedRole() === 'SuperDirector' ? 'Director / SuperDirector' : selectedRole() }},
                                        or accounts holding exactly one pending (unassigned adult) coach
                                        registration — from any event site. Family logins cannot hold
                                        admin roles.
                                    </small>
                                }
                                @if (searchResults().length > 0 && !selectedUser()) {
                                    <ul class="list-group mt-2 search-results">
                                        @for (user of searchResults(); track user.userId) {
                                            <li class="list-group-item list-group-item-action d-flex align-items-center"
                                                role="button"
                                                (click)="selectUser(user)">
                                                <span class="fw-semibold">{{ user.displayName }}</span>
                                                <small class="text-body-secondary ms-2">({{ user.userName }})</small>
                                                <span
                                                    [class]="user.accountType === 'PendingAdult'
                                                        ? 'badge ms-auto bg-warning-subtle text-warning-emphasis'
                                                        : 'badge ms-auto bg-primary-subtle text-primary-emphasis'">
                                                    {{ accountTypeLabel(user.accountType) }}
                                                </span>
                                            </li>
                                        }
                                    </ul>
                                }
                                @if (selectedUser()) {
                                    <div class="mt-1 d-flex align-items-center gap-2">
                                        <span class="badge bg-primary-subtle text-primary-emphasis">
                                            {{ selectedUser()!.displayName }} ({{ selectedUser()!.userName }})
                                        </span>
                                        <button type="button" class="btn-close btn-close-sm" (click)="clearUser()" aria-label="Clear"></button>
                                    </div>
                                    @if (selectedUser()!.accountType === 'PendingAdult') {
                                        <small class="text-warning-emphasis d-block mt-1">
                                            <i class="bi bi-arrow-repeat me-1"></i>Accepting grants {{ selectedRole() }}
                                            on this job and deletes this person's pending coach registration
                                            (they leave the coach-approval queue; any team-staff assignment
                                            is kept).
                                        </small>
                                    }
                                }
                                @if (searchFailed()) {
                                    <div class="alert alert-danger d-flex align-items-start gap-2 mt-2 mb-0 py-2 px-3 small" role="alert">
                                        <i class="bi bi-exclamation-triangle flex-shrink-0"></i>
                                        <span>Search failed — check your connection and try again.</span>
                                    </div>
                                } @else if (emptyReason() !== null) {
                                    <!-- Search ran and found nothing eligible. AM-004 funnel feedback: a
                                         distinct alert (not more gray help prose) that says WHICH dead end. -->
                                    @switch (emptyReason()) {
                                        @case ('familyOrPlayer') {
                                            <div class="alert alert-warning d-flex align-items-start gap-2 mt-2 mb-0 py-2 px-3 small" role="status">
                                                <i class="bi bi-person-x flex-shrink-0"></i>
                                                <span>"{{ searchInput().trim() }}" matches a family login — family
                                                credentials are shared within the household and can never hold
                                                admin roles. The person needs a coach registration under their
                                                own account — then accept them here.</span>
                                            </div>
                                        }
                                        @case ('alreadyAdmin') {
                                            <div class="alert alert-warning d-flex align-items-start gap-2 mt-2 mb-0 py-2 px-3 small" role="status">
                                                <i class="bi bi-person-check flex-shrink-0"></i>
                                                <span>"{{ searchInput().trim() }}" already holds
                                                {{ selectedRole() }} on this job — manage them in the grid (use
                                                the Active toggle to reactivate a deactivated admin).</span>
                                            </div>
                                        }
                                        @case ('multiplePending') {
                                            <div class="alert alert-warning d-flex align-items-start gap-2 mt-2 mb-0 py-2 px-3 small" role="status">
                                                <i class="bi bi-person-x flex-shrink-0"></i>
                                                <span>"{{ searchInput().trim() }}" has more than one pending coach
                                                registration, so it's ambiguous which one the grant consumes.
                                                Delete or deactivate the extras in Search Registrations, then
                                                accept them here.</span>
                                            </div>
                                        }
                                        @case ('outsideLane') {
                                            <div class="alert alert-warning d-flex align-items-start gap-2 mt-2 mb-0 py-2 px-3 small" role="status">
                                                <i class="bi bi-person-x flex-shrink-0"></i>
                                                <span>"{{ searchInput().trim() }}" matches an account with no pending
                                                coach registration and existing roles that aren't eligible for
                                                {{ selectedRole() }}. Register the person as a coach (any event
                                                site), then accept them here.</span>
                                            </div>
                                        }
                                        @default {
                                            <div class="alert alert-warning d-flex align-items-start gap-2 mt-2 mb-0 py-2 px-3 small" role="status">
                                                <i class="bi bi-info-circle flex-shrink-0"></i>
                                                <span>No registered account matches
                                                "{{ searchInput().trim() }}". New admins should first register
                                                as a coach/staff adult (any event site), then be accepted here.</span>
                                            </div>
                                        }
                                    }
                                }
                            </div>
                        } @else {
                            <div class="col-12">
                                <label class="field-label">Administrator</label>
                                <p class="mb-0" style="font-size: var(--font-size-sm)">{{ editAdmin()?.administratorName }}</p>
                            </div>
                            <div class="col-12">
                                <div class="field-check">
                                    <input id="activeToggle" type="checkbox" role="switch"
                                        class="form-check-input"
                                        [checked]="isActive()"
                                        (change)="isActive.set($any($event.target).checked)" />
                                    <label class="field-label" for="activeToggle" style="margin-bottom:0">
                                        {{ isActive() ? 'Active' : 'Inactive' }}
                                    </label>
                                </div>
                            </div>
                        }
                    </div>

                    @if (errorMessage()) {
                        <div class="alert alert-danger py-2 mt-2 mb-0">{{ errorMessage() }}</div>
                    }
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary btn-sm"
                        [disabled]="!isValid() || saving()"
                        (click)="onSave()">
                        @if (saving()) {
                            <span class="spinner-border spinner-border-sm me-1"></span>
                        }
                        {{ mode() === 'add' ? 'Add' : 'Save' }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    styles: [`
        /* Results flow in-document (explicit search, not a typeahead overlay) so the
           dialog footer is pushed down, never covered. */
        .search-results {
            max-height: 200px;
            overflow-y: auto;
        }
        .btn-close-sm {
            font-size: 0.6rem;
        }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdminFormModalComponent implements OnInit, OnDestroy {
    readonly mode = input<ModalMode>('add');
    readonly admin = input<AdministratorDto | null>(null);

    readonly close = output<void>();
    readonly saved = output<AdminFormResult>();

    private readonly adminService = inject(AdministratorService);
    private readonly destroy$ = new Subject<void>();

    readonly availableRoles = [
        'Director',
        'SuperDirector',
        // 'ApiAuthorized' was retired 08-02 with the vendor API, then REVIVED 08-06 with a
        // different job: it is now an export-only login. The role's whole menu is one leaf —
        // Authorized Rosters and Schedule Export — and the export ships only age groups the
        // event enabled for Third-Party Roster Access. It is deliberately NOT in AdminOnly;
        // the single endpoint carries its own narrow CanRunThirdPartyExport policy. The
        // string must stay exactly 'ApiAuthorized': it is the DB role name the backend maps
        // to a role id and mints the JWT role claim from.
        'ApiAuthorized',
        'Ref Assignor',
        'Store Admin',
        'STPAdmin',
    ];

    /**
     * Options for the role select. Add mode = availableRoles only. Edit mode also
     * includes the admin's CURRENT role when it's no longer offered — a role retired
     * after the fact must display, not blank the select (saving an Active toggle must
     * not silently rewrite the role).
     */
    readonly roleOptions = computed(() => {
        const current = this.mode() === 'edit' ? this.admin()?.roleName : null;
        return current && !this.availableRoles.includes(current)
            ? [...this.availableRoles, current]
            : this.availableRoles;
    });

    // State
    readonly searchInput = signal('');
    readonly searchResults = signal<UserSearchResultDto[]>([]);
    readonly selectedUser = signal<UserSearchResultDto | null>(null);
    readonly selectedRole = signal('');
    readonly isActive = signal(true);
    readonly errorMessage = signal<string | null>(null);
    readonly saving = signal(false);
    readonly searching = signal(false);
    /** Why the last search came back empty: 'notFound' | 'familyOrPlayer' | 'alreadyAdmin' | 'multiplePending' | 'outsideLane' | null. */
    readonly emptyReason = signal<string | null>(null);
    /** Last search request errored (distinct from "no matches"). */
    readonly searchFailed = signal(false);
    readonly editAdmin = signal<AdministratorDto | null>(null);

    readonly isValid = signal(false);

    /**
     * Search-result badge text. 'NoRegistrations' is a known account with an empty
     * registration footprint — it must NOT read as "Pending adult", which would imply a
     * pending coach row exists (and gets consumed) when there is none.
     */
    accountTypeLabel(accountType: string | null | undefined): string {
        switch (accountType) {
            case 'Admin': return 'Admin account';
            case 'PendingAdult': return 'Pending adult';
            default: return 'No registrations';
        }
    }

    ngOnInit() {
        const admin = this.admin();
        if (this.mode() === 'edit' && admin) {
            this.editAdmin.set(admin);
            this.selectedRole.set(admin.roleName ?? '');
            this.isActive.set(admin.isActive);
        }

    }

    ngOnDestroy() {
        this.destroy$.next();
        this.destroy$.complete();
    }

    onSearchInput(event: Event) {
        const value = (event.target as HTMLInputElement).value;
        this.searchInput.set(value);
        // Edited input invalidates whatever the last search showed.
        this.selectedUser.set(null);
        this.searchResults.set([]);
        this.emptyReason.set(null);
        this.searchFailed.set(false);
        this.updateValidity();
    }

    /** Explicit search — one request per click/Enter, no typeahead. */
    onSearchSubmit() {
        const q = this.searchInput().trim();
        const role = this.selectedRole();
        if (q.length < 2 || !role || this.searching()) {
            return;
        }
        this.searching.set(true);
        this.searchFailed.set(false);
        this.adminService.searchUsers(q, role)
            .pipe(takeUntil(this.destroy$))
            .subscribe({
                next: response => {
                    this.searching.set(false);
                    this.searchResults.set(response.results);
                    this.emptyReason.set(response.emptyReason ?? null);
                },
                error: () => {
                    this.searching.set(false);
                    this.searchFailed.set(true);
                    this.searchResults.set([]);
                    this.emptyReason.set(null);
                }
            });
    }

    /** Role changed → the eligibility lane changed: prior results/selection are invalid. */
    onRoleChange(role: string) {
        this.selectedRole.set(role);
        if (this.mode() === 'add') {
            // Lane changed → prior results/selection are invalid; user re-searches explicitly.
            this.selectedUser.set(null);
            this.searchResults.set([]);
            this.emptyReason.set(null);
            this.searchFailed.set(false);
        }
        this.updateValidity();
    }

    selectUser(user: UserSearchResultDto) {
        this.selectedUser.set(user);
        this.searchInput.set(user.userName);
        this.searchResults.set([]);
        this.emptyReason.set(null);
        this.updateValidity();
    }

    clearUser() {
        this.selectedUser.set(null);
        this.searchInput.set('');
        this.searchResults.set([]);
        this.emptyReason.set(null);
        this.updateValidity();
    }

    updateValidity() {
        if (this.mode() === 'add') {
            this.isValid.set(!!this.selectedUser() && !!this.selectedRole());
        } else {
            this.isValid.set(!!this.selectedRole());
        }
    }

    onSave() {
        this.updateValidity();
        if (!this.isValid()) return;

        this.saving.set(true);
        this.errorMessage.set(null);

        const result: AdminFormResult = { mode: this.mode() };

        if (this.mode() === 'add') {
            result.addRequest = {
                userName: this.selectedUser()!.userName,
                roleName: this.selectedRole()
            };
        } else {
            result.registrationId = this.admin()?.registrationId;
            result.updateRequest = {
                isActive: this.isActive(),
                roleName: this.selectedRole()
            };
        }

        this.saved.emit(result);
    }

    /** Called by the host page when the save request fails: stop the spinner and show the
     *  error inline (the dialog stays open, so the message belongs inside it, not in a toast). */
    saveFailed(message: string) {
        this.saving.set(false);
        this.errorMessage.set(message);
    }
}
