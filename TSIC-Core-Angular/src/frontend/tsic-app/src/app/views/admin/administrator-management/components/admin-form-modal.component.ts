import { Component, EventEmitter, Input, Output, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { AdministratorService } from '../services/administrator.service';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of, takeUntil } from 'rxjs';
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
                    <h5 class="modal-title">{{ mode === 'add' ? 'Add Administrator' : 'Edit Administrator' }}</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    @if (mode === 'add') {
                        <div class="mb-3">
                            <label for="userSearch" class="form-label fw-semibold">Username</label>
                            <input
                                id="userSearch"
                                type="text"
                                class="form-control"
                                placeholder="Search by name or username..."
                                [value]="searchInput()"
                                (input)="onSearchInput($event)"
                                autocomplete="off" />
                            @if (searchResults().length > 0 && !selectedUser()) {
                                <ul class="list-group mt-1 shadow-sm typeahead-dropdown">
                                    @for (user of searchResults(); track user.userId) {
                                        <li class="list-group-item list-group-item-action"
                                            role="button"
                                            (click)="selectUser(user)">
                                            <span class="fw-semibold">{{ user.displayName }}</span>
                                            <small class="text-body-secondary ms-2">({{ user.userName }})</small>
                                        </li>
                                    }
                                </ul>
                            }
                            @if (selectedUser()) {
                                <div class="mt-2 d-flex align-items-center gap-2">
                                    <span class="badge bg-primary-subtle text-primary-emphasis">
                                        {{ selectedUser()!.displayName }} ({{ selectedUser()!.userName }})
                                    </span>
                                    <button type="button" class="btn-close btn-close-sm" (click)="clearUser()" aria-label="Clear"></button>
                                </div>
                            }
                            @if (searchInput().length >= 2 && searchResults().length === 0 && !selectedUser() && !searching()) {
                                <small class="text-body-secondary mt-1 d-block">No users found.</small>
                            }
                        </div>
                    } @else {
                        <div class="mb-3">
                            <label class="form-label fw-semibold">Administrator</label>
                            <p class="form-control-plaintext">{{ editAdmin()?.administratorName }}</p>
                        </div>
                        <div class="mb-3">
                            <label for="activeToggle" class="form-label fw-semibold">Active</label>
                            <div class="form-check form-switch">
                                <input id="activeToggle" type="checkbox" class="form-check-input" role="switch"
                                    [checked]="isActive()"
                                    (change)="isActive.set($any($event.target).checked)" />
                                <label class="form-check-label" for="activeToggle">
                                    {{ isActive() ? 'Active' : 'Inactive' }}
                                </label>
                            </div>
                        </div>
                    }

                    <div class="mb-3">
                        <label for="roleSelect" class="form-label fw-semibold">Role</label>
                        <select id="roleSelect" class="form-select"
                            [ngModel]="selectedRole()"
                            (ngModelChange)="selectedRole.set($event)">
                            <option value="" disabled>Select a role...</option>
                            @for (role of availableRoles; track role) {
                                <option [value]="role">{{ role }}</option>
                            }
                        </select>
                    </div>

                    @if (errorMessage()) {
                        <div class="alert alert-danger py-2 mb-0">{{ errorMessage() }}</div>
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
                        {{ mode === 'add' ? 'Add' : 'Save' }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    styles: [`
        .typeahead-dropdown {
            position: absolute;
            z-index: 10;
            max-height: 200px;
            overflow-y: auto;
            width: calc(100% - 2rem);
        }
        .btn-close-sm {
            font-size: 0.6rem;
        }
    `]
})
export class AdminFormModalComponent implements OnInit, OnDestroy {
    @Input() mode: ModalMode = 'add';
    @Input() admin: AdministratorDto | null = null;

    @Output() close = new EventEmitter<void>();
    @Output() saved = new EventEmitter<AdminFormResult>();

    private readonly adminService = inject(AdministratorService);
    private readonly destroy$ = new Subject<void>();
    private readonly searchSubject = new Subject<string>();

    readonly availableRoles = ['Director', 'SuperDirector', 'ApiAuthorized', 'Ref Assignor', 'Store Admin', 'STPAdmin'];

    // State
    readonly searchInput = signal('');
    readonly searchResults = signal<UserSearchResultDto[]>([]);
    readonly selectedUser = signal<UserSearchResultDto | null>(null);
    readonly selectedRole = signal('');
    readonly isActive = signal(true);
    readonly errorMessage = signal<string | null>(null);
    readonly saving = signal(false);
    readonly searching = signal(false);
    readonly editAdmin = signal<AdministratorDto | null>(null);

    readonly isValid = signal(false);

    ngOnInit() {
        if (this.mode === 'edit' && this.admin) {
            this.editAdmin.set(this.admin);
            this.selectedRole.set(this.admin.roleName ?? '');
            this.isActive.set(this.admin.isActive);
        }

        // Typeahead debounce
        this.searchSubject.pipe(
            debounceTime(300),
            distinctUntilChanged(),
            switchMap(query => {
                if (query.length < 2) {
                    this.searching.set(false);
                    return of([]);
                }
                this.searching.set(true);
                return this.adminService.searchUsers(query);
            }),
            takeUntil(this.destroy$)
        ).subscribe({
            next: results => {
                this.searchResults.set(results);
                this.searching.set(false);
            },
            error: () => {
                this.searching.set(false);
            }
        });
    }

    ngOnDestroy() {
        this.destroy$.next();
        this.destroy$.complete();
    }

    onSearchInput(event: Event) {
        const value = (event.target as HTMLInputElement).value;
        this.searchInput.set(value);
        this.selectedUser.set(null);
        this.searchSubject.next(value);
        this.updateValidity();
    }

    selectUser(user: UserSearchResultDto) {
        this.selectedUser.set(user);
        this.searchInput.set(user.userName);
        this.searchResults.set([]);
        this.updateValidity();
    }

    clearUser() {
        this.selectedUser.set(null);
        this.searchInput.set('');
        this.searchResults.set([]);
        this.updateValidity();
    }

    updateValidity() {
        if (this.mode === 'add') {
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

        const result: AdminFormResult = { mode: this.mode };

        if (this.mode === 'add') {
            result.addRequest = {
                userName: this.selectedUser()!.userName,
                roleName: this.selectedRole()
            };
        } else {
            result.registrationId = this.admin?.registrationId;
            result.updateRequest = {
                isActive: this.isActive(),
                roleName: this.selectedRole()
            };
        }

        this.saved.emit(result);
    }
}
