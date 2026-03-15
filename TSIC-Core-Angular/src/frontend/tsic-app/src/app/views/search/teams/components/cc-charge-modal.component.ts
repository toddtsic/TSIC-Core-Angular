import { Component, ChangeDetectionStrategy, input, output, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { TeamSearchDetailDto, CreditCardInfo } from '@core/api';
import { TeamSearchService } from '../services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';

@Component({
	selector: 'app-cc-charge-modal',
	standalone: true,
	imports: [CommonModule, FormsModule],
	template: `
		@if (isOpen()) {
			<div class="modal-backdrop" (click)="close()"></div>
			<div class="modal-container">
				<div class="modal-header">
					<h4 class="modal-title">
						@if (scope() === 'team') {
							Charge CC for {{ teamDetail()?.teamName }}
						} @else {
							Charge CC for all {{ teamDetail()?.clubName }} teams ({{ clubTotal | currency }})
						}
					</h4>
					<button type="button" class="btn-close-modal" (click)="close()">&times;</button>
				</div>

				@if (scope() === 'club' && teamDetail()?.clubTeamSummaries?.length) {
					<div class="club-breakdown">
						<h5>Teams to be charged:</h5>
						@for (ct of teamDetail()!.clubTeamSummaries; track ct.teamId) {
							@if (ct.owedTotal > 0) {
								<div class="breakdown-row">
									<span>{{ ct.teamName }}</span>
									<span class="amount">{{ ct.owedTotal | currency }}</span>
								</div>
							}
						}
					</div>
				}

				<form class="cc-form" (ngSubmit)="submit()">
					<div class="form-row">
						<div class="form-group">
							<label for="ccNumber">Card Number</label>
							<input type="text" id="ccNumber" class="form-control" maxlength="19"
								[ngModel]="ccNumber()" (ngModelChange)="ccNumber.set($event)" name="ccNumber" required />
						</div>
					</div>
					<div class="form-row form-row-3">
						<div class="form-group">
							<label for="ccExpiry">Exp (MMYY)</label>
							<input type="text" id="ccExpiry" class="form-control" maxlength="4" placeholder="0125"
								[ngModel]="ccExpiry()" (ngModelChange)="ccExpiry.set($event)" name="ccExpiry" required />
						</div>
						<div class="form-group">
							<label for="ccCode">CVV</label>
							<input type="text" id="ccCode" class="form-control" maxlength="4"
								[ngModel]="ccCode()" (ngModelChange)="ccCode.set($event)" name="ccCode" required />
						</div>
					</div>
					<div class="form-row form-row-2">
						<div class="form-group">
							<label for="ccFirst">First Name</label>
							<input type="text" id="ccFirst" class="form-control"
								[ngModel]="ccFirst()" (ngModelChange)="ccFirst.set($event)" name="ccFirst" required />
						</div>
						<div class="form-group">
							<label for="ccLast">Last Name</label>
							<input type="text" id="ccLast" class="form-control"
								[ngModel]="ccLast()" (ngModelChange)="ccLast.set($event)" name="ccLast" required />
						</div>
					</div>
					<div class="form-group">
						<label for="ccAddress">Street Address</label>
						<input type="text" id="ccAddress" class="form-control"
							[ngModel]="ccAddress()" (ngModelChange)="ccAddress.set($event)" name="ccAddress" />
					</div>
					<div class="form-row form-row-2">
						<div class="form-group">
							<label for="ccZip">Zip Code</label>
							<input type="text" id="ccZip" class="form-control" maxlength="10"
								[ngModel]="ccZip()" (ngModelChange)="ccZip.set($event)" name="ccZip" required />
						</div>
						<div class="form-group">
							<label for="ccEmail">Email</label>
							<input type="email" id="ccEmail" class="form-control"
								[ngModel]="ccEmail()" (ngModelChange)="ccEmail.set($event)" name="ccEmail" />
						</div>
					</div>
					<div class="form-group">
						<label for="ccPhone">Phone</label>
						<input type="text" id="ccPhone" class="form-control" maxlength="15"
							[ngModel]="ccPhone()" (ngModelChange)="ccPhone.set($event)" name="ccPhone" />
					</div>

					<div class="modal-footer">
						<button type="button" class="btn btn-secondary" (click)="close()">Cancel</button>
						<button type="submit" class="btn btn-primary" [disabled]="isProcessing()">
							@if (isProcessing()) { <span class="spinner-border spinner-border-sm me-1"></span> }
							Charge {{ scope() === 'team' ? (teamDetail()?.owedTotal | currency) : (clubTotal | currency) }}
						</button>
					</div>
				</form>
			</div>
		}
	`,
	styleUrl: './cc-charge-modal.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcChargeModalComponent {
	teamDetail = input<TeamSearchDetailDto | null>(null);
	scope = input<'team' | 'club'>('team');
	isOpen = input<boolean>(false);

	closed = output<void>();
	charged = output<void>();

	private readonly searchService = inject(TeamSearchService);
	private readonly toast = inject(ToastService);

	// Form state
	ccNumber = signal('');
	ccExpiry = signal('');
	ccCode = signal('');
	ccFirst = signal('');
	ccLast = signal('');
	ccAddress = signal('');
	ccZip = signal('');
	ccEmail = signal('');
	ccPhone = signal('');
	isProcessing = signal(false);

	get clubTotal(): number {
		return this.teamDetail()?.clubTeamSummaries?.filter(t => t.owedTotal > 0).reduce((s, t) => s + t.owedTotal, 0) ?? 0;
	}

	close(): void {
		this.closed.emit();
	}

	submit(): void {
		const d = this.teamDetail();
		if (!d || !d.clubRepRegistrationId) return;

		this.isProcessing.set(true);

		const cc: CreditCardInfo = {
			number: this.ccNumber(),
			expiry: this.ccExpiry(),
			code: this.ccCode(),
			firstName: this.ccFirst(),
			lastName: this.ccLast(),
			address: this.ccAddress(),
			zip: this.ccZip(),
			email: this.ccEmail(),
			phone: this.ccPhone()
		};

		const request = {
			teamId: this.scope() === 'team' ? d.teamId : undefined,
			clubRepRegistrationId: d.clubRepRegistrationId,
			creditCard: cc
		};

		const call = this.scope() === 'team'
			? this.searchService.chargeCcForTeam(d.teamId, request)
			: this.searchService.chargeCcForClub(d.clubRepRegistrationId, request);

		call.subscribe({
			next: (result) => {
				this.isProcessing.set(false);
				if (result.success) {
					this.toast.show('CC charge successful', 'success', 4000);
					this.charged.emit();
				} else {
					this.toast.show(result.error ?? 'CC charge failed', 'danger', 6000);
				}
			},
			error: (err) => {
				this.isProcessing.set(false);
				this.toast.show('CC charge failed', 'danger', 4000);
				console.error('CC charge error:', err);
			}
		});
	}
}
