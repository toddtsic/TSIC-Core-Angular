import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	TeamSearchRequest,
	TeamSearchResponse,
	TeamFilterOptionsDto,
	TeamSearchDetailDto,
	EditTeamRequest,
	TeamCcChargeRequest,
	TeamCcChargeResponse,
	TeamCheckOrCorrectionRequest,
	TeamCheckOrCorrectionResponse,
	RefundRequest,
	RefundResponse,
	PaymentMethodOptionDto,
	LadtTreeRootDto
} from '@core/api';

// Re-export for consumers
export type {
	TeamSearchRequest,
	TeamSearchResponse,
	TeamSearchResultDto,
	TeamFilterOptionsDto,
	TeamSearchDetailDto,
	ClubTeamSummaryDto,
	EditTeamRequest,
	TeamCcChargeRequest,
	TeamCcChargeResponse,
	TeamCheckOrCorrectionRequest,
	TeamCheckOrCorrectionResponse,
	TeamPaymentAllocation,
	RefundRequest,
	RefundResponse,
	PaymentMethodOptionDto,
	AccountingRecordDto,
	FilterOption,
	LadtTreeRootDto,
	LadtTreeNodeDto,
	CreditCardInfo
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class TeamSearchService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/team-search`;

	search(request: TeamSearchRequest): Observable<TeamSearchResponse> {
		return this.http.post<TeamSearchResponse>(`${this.apiUrl}/search`, request);
	}

	getFilterOptions(): Observable<TeamFilterOptionsDto> {
		return this.http.get<TeamFilterOptionsDto>(`${this.apiUrl}/filter-options`);
	}

	getTeamDetail(teamId: string): Observable<TeamSearchDetailDto> {
		return this.http.get<TeamSearchDetailDto>(`${this.apiUrl}/${teamId}`);
	}

	editTeam(teamId: string, request: EditTeamRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/${teamId}`, request);
	}

	chargeCcForTeam(teamId: string, request: TeamCcChargeRequest): Observable<TeamCcChargeResponse> {
		return this.http.post<TeamCcChargeResponse>(`${this.apiUrl}/${teamId}/cc-charge`, request);
	}

	chargeCcForClub(clubRepRegId: string, request: TeamCcChargeRequest): Observable<TeamCcChargeResponse> {
		return this.http.post<TeamCcChargeResponse>(`${this.apiUrl}/club/${clubRepRegId}/cc-charge`, request);
	}

	recordCheckForTeam(teamId: string, request: TeamCheckOrCorrectionRequest): Observable<TeamCheckOrCorrectionResponse> {
		return this.http.post<TeamCheckOrCorrectionResponse>(`${this.apiUrl}/${teamId}/check`, request);
	}

	recordCheckForClub(clubRepRegId: string, request: TeamCheckOrCorrectionRequest): Observable<TeamCheckOrCorrectionResponse> {
		return this.http.post<TeamCheckOrCorrectionResponse>(`${this.apiUrl}/club/${clubRepRegId}/check`, request);
	}

	processRefund(request: RefundRequest): Observable<RefundResponse> {
		return this.http.post<RefundResponse>(`${this.apiUrl}/refund`, request);
	}

	getPaymentMethods(): Observable<PaymentMethodOptionDto[]> {
		return this.http.get<PaymentMethodOptionDto[]>(`${this.apiUrl}/payment-methods`);
	}

	getLadtTree(): Observable<LadtTreeRootDto> {
		return this.http.get<LadtTreeRootDto>(`${environment.apiUrl}/ladt/tree`);
	}
}
