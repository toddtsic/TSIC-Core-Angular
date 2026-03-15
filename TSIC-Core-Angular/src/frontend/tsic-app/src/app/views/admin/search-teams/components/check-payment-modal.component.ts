import { Component, ChangeDetectionStrategy, input, output, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { TeamSearchDetailDto } from '@core/api';
import { TeamSearchService } from '../services/team-search.service';
import { ToastService } from '@shared-ui/toast.service';

@Component({
	selector: 'app-check-payment-modal',
	standalone: true,
	imports: [CommonModule, FormsModule],
	template: `
		@if (isOpen()) {
			<div class="modal-backdrop" (click)="close()"></div>
			<div class="modal-container">
				<div class="modal-header">
					<h4 class="modal-title">
						{{ paymentType() }} for
						@if (scope() === 'team') {
							{{ teamDetail()?.teamName }}
						} @else {
							all {{ teamDetail()?.clubName }} teams
						}
					</h4>
					<button type="button" class="btn-close-modal" (click)="close()">&times;</button>
				</div>

				<form class="payment-form" (ngSubmit)="submit()">
					<div class="form-group">
						<label for="amount">Amount</label>
						<div class="input-group">
							<span class="input-group-text">$</span>
							<input type="number" id="amount" class="form-control" step="0.01" min="0.01"
								[ngModel]="amount()" (ngModelChange)="amount.set($event)" name="amount" required />
						</div>
						<small class="form-hint">
							Max: {{ scope() === 'team' ? (teamDetail()?.owedTotal | currency) : (clubOwed | currency) }}
						</small>
					</div>

					@if (paymentType() === 'Check') {
						<div class="form-group">
							<label for="checkNo">Check Number</label>
							<input type="text" id="checkNo" class="form-control"
								[ngModel]="checkNo()" (ngModelChange)="checkNo.set($event)" name="checkNo" />
						</div>
					}

					<div class="form-group">
						<label for="comment">Comment</label>
						<textarea id="comment" class="form-control" rows="2"
							[ngModel]="comment()" (ngModelChange)="comment.set($event)" name="comment">
						</textarea>
					</div>

					<!-- Club-wide allocation preview -->
					@if (scope() === 'club' && amount() > 0 && teamDetail()?.clubTeamSummaries?.length) {
						<div class="allocation-preview">
							<h5>Allocation Preview</h5>
							<p class="preview-note">Payment will be distributed starting with highest balance:</p>
							@for (ct of teamDetail()!.clubTeamSummaries; track ct.teamId) {
								@if (ct.owedTotal > 0) {
									<div class="allocation-row">
										<span>{{ ct.teamName }}</span>
										<span class="owed-label">owes {{ ct.owedTotal | currency }}</span>
									</div>
								}
							}
						</div>
					}

					<div class="modal-footer">
						<button type="button" class="btn btn-secondary" (click)="close()">Cancel</button>
						<button type="submit" class="btn" [class.btn-success]="paymentType() === 'Check'" [class.btn-warning]="paymentType() === 'Correction'" [disabled]="isProcessing() || !amount()">
							@if (isProcessing()) { <span class="spinner-border spinner-border-sm me-1"></span> }
							Record {{ paymentType() }}
						</button>
					</div>
				</form>
			</div>
		}
	`,
	styleUrl: './check-payment-modal.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class CheckPaymentModalComponent {
	teamDetail = input<TeamSearchDetailDto | null>(null);
	scope = input<'team' | 'club'>('team');
	paymentType = input<'Check' | 'Correction'>('Check');
	isOpen = input<boolean>(false);

	closed = output<void>();
	completed = output<void>();

	private readonly searchService = inject(TeamSearchService);
	private readonly toast = inject(ToastService);

	amount = signal<number>(0);
	checkNo = signal('');
	comment = signal('');
	isProcessing = signal(false);

	get clubOwed(): number {
		return this.teamDetail()?.clubTeamSummaries?.reduce((s, t) => s + t.owedTotal, 0) ?? 0;
	}

	close(): void {
		this.closed.emit();
	}

	submit(): void {
		const d = this.teamDetail();
		if (!d || !d.clubRepRegistrationId || !this.amount()) return;

		this.isProcessing.set(true);

		const request = {
			teamId: this.scope() === 'team' ? d.teamId : undefined,
			clubRepRegistrationId: d.clubRepRegistrationId,
			amount: this.amount(),
			checkNo: this.paymentType() === 'Check' ? this.checkNo() : undefined,
			comment: this.comment() || undefined,
			paymentType: this.paymentType()
		};

		const call = this.scope() === 'team'
			? this.searchService.recordCheckForTeam(d.teamId, request)
			: this.searchService.recordCheckForClub(d.clubRepRegistrationId, request);

		call.subscribe({
			next: (result) => {
				this.isProcessing.set(false);
				if (result.success) {
					this.toast.show(`${this.paymentType()} recorded successfully`, 'success', 4000);
					this.completed.emit();
				} else {
					this.toast.show(result.error ?? `${this.paymentType()} failed`, 'danger', 6000);
				}
			},
			error: (err) => {
				this.isProcessing.set(false);
				this.toast.show(`${this.paymentType()} failed`, 'danger', 4000);
				console.error('Payment error:', err);
			}
		});
	}
}
