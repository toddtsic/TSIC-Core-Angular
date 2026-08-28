import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of, catchError } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { StoreService } from '../../../infrastructure/services/store.service';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { StoreAdminRosterRowDto, UserSearchResultDto } from '@core/api';

/**
 * Store Administrators — port of legacy `StoreAdminAdd/Index`.
 *
 * Legacy's jqGrid columns, in order: Active · Username · LastName · FirstName · Email · Cell
 * Phone. Its navGrid passed `del: false`, so delete was never on the screen — a store admin is
 * retired by clearing Active, and the same holds here.
 *
 * Two deliberate divergences from legacy, both documented on the server DTOs:
 *
 *  1. **Adding names an existing account instead of minting one.** Legacy's add branch created
 *     a fresh AspNetUsers row whose PASSWORD WAS THE USERNAME, with a placeholder gender and
 *     date of birth, then registered it. The AM-004 ruling replaced that across every admin
 *     role: grants now go to an account that already exists and passes the lane check, which
 *     is what the typeahead below searches.
 *  2. **Username, first name and last name are read-only on edit.** Legacy marked them
 *     editable in the grid, but its Edit action never read them — only Active, Email and
 *     Cellphone were written. The form now says what it actually does.
 */
@Component({
	selector: 'app-store-staff-tab',
	standalone: true,
	imports: [CommonModule, FormsModule, TsicDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-staff-tab.component.html',
	styleUrl: './store-staff-tab.component.scss',
})
export class StoreStaffTabComponent {
	private readonly store = inject(StoreService);
	private readonly auth = inject(AuthService);
	private readonly toast = inject(ToastService);

	/**
	 * Mirrors the backend split exactly: the roster READS under the "StoreAdmin" policy (which
	 * anyone already on this screen satisfies) and every WRITE narrows to "AdminOnly". A store
	 * admin therefore sees the table and no buttons — they cannot appoint a colleague.
	 */
	readonly canManage = this.auth.isAdmin;

	readonly rows = signal<StoreAdminRosterRowDto[]>([]);
	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);

	readonly activeCount = computed(() => this.rows().filter(r => r.isActive).length);

	// ── Add modal ──

	readonly showAddModal = signal(false);
	readonly searchTerm = signal('');
	readonly searchResults = signal<UserSearchResultDto[]>([]);
	readonly searching = signal(false);
	readonly searchFailed = signal(false);
	/** 'notFound' | 'familyOrPlayer' | 'alreadyAdmin' | 'multiplePending' | 'outsideLane' | null */
	readonly emptyReason = signal<string | null>(null);
	readonly selectedUser = signal<UserSearchResultDto | null>(null);

	// Debounce at the KEYSTROKE, never on the per-request http.get — that observable emits once
	// and completes, so a debounce there would only delay the answer.
	private readonly searchInput$ = new Subject<string>();

	// ── Edit modal ──

	readonly editingRow = signal<StoreAdminRosterRowDto | null>(null);
	readonly formIsActive = signal(true);
	readonly formEmail = signal('');
	readonly formCellphone = signal('');

	constructor() {
		this.searchInput$
			.pipe(
				debounceTime(300),
				distinctUntilChanged(),
				switchMap(term => {
					if (term.trim().length < 2) {
						this.searching.set(false);
						return of(null);
					}
					this.searching.set(true);
					return this.store.searchStoreAdminCandidates(term.trim()).pipe(
						catchError(() => {
							this.searchFailed.set(true);
							return of(null);
						}),
					);
				}),
				takeUntilDestroyed(),
			)
			.subscribe(response => {
				this.searching.set(false);
				this.searchResults.set(response?.results ?? []);
				this.emptyReason.set(response?.emptyReason ?? null);
			});

		this.load();
	}

	load(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.store.getStoreAdmins().subscribe({
			next: rows => {
				this.rows.set(rows);
				this.isLoading.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message ?? 'Could not load the store administrators.');
				this.isLoading.set(false);
			},
		});
	}

	// ═══════════════════════════════════════
	//  ADD
	// ═══════════════════════════════════════

	openAdd(): void {
		this.searchTerm.set('');
		this.searchResults.set([]);
		this.selectedUser.set(null);
		this.emptyReason.set(null);
		this.searchFailed.set(false);
		this.showAddModal.set(true);
	}

	onSearchChange(term: string): void {
		this.searchTerm.set(term);
		this.selectedUser.set(null);
		this.searchFailed.set(false);
		this.emptyReason.set(null);
		this.searchInput$.next(term);
	}

	selectCandidate(user: UserSearchResultDto): void {
		this.selectedUser.set(user);
		this.searchResults.set([]);
		this.searchTerm.set(user.userName);
	}

	/**
	 * 'NoRegistrations' is a known account with an empty history — it must not read as
	 * "Pending adult", which would imply a coach row exists and gets consumed by the grant.
	 */
	accountTypeLabel(accountType: string | null | undefined): string {
		switch (accountType) {
			case 'Admin': return 'Admin account';
			case 'PendingAdult': return 'Pending adult';
			default: return 'No registrations';
		}
	}

	emptyReasonMessage(reason: string): string {
		switch (reason) {
			case 'familyOrPlayer':
				return 'That is a family or player login. Household credentials are shared, so they cannot hold admin roles — have the person register as a coach/staff adult with their own account first.';
			case 'alreadyAdmin':
				return 'That person already holds a role on this event. If they are a store admin who was switched off, turn Active back on in the table instead of adding them again.';
			case 'outsideLane':
				return 'That account holds a different kind of admin role elsewhere. Store Admin is granted only to accounts whose history is store work or a pending coach registration.';
			case 'multiplePending':
				return 'That account has more than one pending coach registration, so it is ambiguous which one the grant would consume. Clear the extras in Search Registrations first.';
			default:
				return 'No account matches that username or name.';
		}
	}

	confirmAdd(): void {
		const user = this.selectedUser();
		if (!user || this.isSaving()) return;

		this.isSaving.set(true);
		this.store.addStoreAdmin({ userName: user.userName }).subscribe({
			next: rows => {
				this.rows.set(rows);
				this.isSaving.set(false);
				this.showAddModal.set(false);
				this.toast.show(`${user.displayName} is now a store administrator.`, 'success', 4000);
			},
			error: err => {
				this.isSaving.set(false);
				this.toast.show(err?.error?.message ?? 'Could not add that administrator.', 'danger', 7000);
			},
		});
	}

	// ═══════════════════════════════════════
	//  EDIT
	// ═══════════════════════════════════════

	openEdit(row: StoreAdminRosterRowDto): void {
		this.editingRow.set(row);
		this.formIsActive.set(row.isActive);
		this.formEmail.set(row.email);
		this.formCellphone.set(row.cellphone ?? '');
	}

	saveEdit(): void {
		const row = this.editingRow();
		if (!row || this.isSaving()) return;

		this.isSaving.set(true);
		this.store
			.updateStoreAdmin(row.registrationId, {
				isActive: this.formIsActive(),
				email: this.formEmail().trim(),
				// An emptied field clears the column rather than storing "" — the DTO is nullable.
				cellphone: this.formCellphone().trim() || null,
			})
			.subscribe({
				next: rows => {
					this.rows.set(rows);
					this.isSaving.set(false);
					this.editingRow.set(null);
					this.toast.show('Store administrator updated.', 'success', 3000);
				},
				error: err => {
					this.isSaving.set(false);
					this.toast.show(err?.error?.message ?? 'Could not save those changes.', 'danger', 7000);
				},
			});
	}
}
