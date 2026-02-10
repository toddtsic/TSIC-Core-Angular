import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	RegistrationSearchRequest,
	RegistrationSearchResponse,
	RegistrationFilterOptionsDto,
	RegistrationDetailDto,
	UpdateRegistrationProfileRequest,
	CreateAccountingRecordRequest,
	AccountingRecordDto,
	RefundRequest,
	RefundResponse,
	PaymentMethodOptionDto,
	BatchEmailRequest,
	BatchEmailResponse,
	EmailPreviewRequest,
	EmailPreviewResponse
} from '@core/api';

// Re-export for consumers
export type {
	RegistrationSearchRequest,
	RegistrationSearchResponse,
	RegistrationSearchResultDto,
	RegistrationFilterOptionsDto,
	RegistrationDetailDto,
	UpdateRegistrationProfileRequest,
	CreateAccountingRecordRequest,
	AccountingRecordDto,
	RefundRequest,
	RefundResponse,
	PaymentMethodOptionDto,
	BatchEmailRequest,
	BatchEmailResponse,
	EmailPreviewRequest,
	EmailPreviewResponse,
	RenderedEmailPreview,
	FilterOption
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class RegistrationSearchService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/registration-search`;

	search(request: RegistrationSearchRequest): Observable<RegistrationSearchResponse> {
		return this.http.post<RegistrationSearchResponse>(`${this.apiUrl}/search`, request);
	}

	getFilterOptions(): Observable<RegistrationFilterOptionsDto> {
		return this.http.get<RegistrationFilterOptionsDto>(`${this.apiUrl}/filter-options`);
	}

	getRegistrationDetail(registrationId: string): Observable<RegistrationDetailDto> {
		return this.http.get<RegistrationDetailDto>(`${this.apiUrl}/${registrationId}`);
	}

	updateProfile(registrationId: string, request: UpdateRegistrationProfileRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/${registrationId}/profile`, request);
	}

	createAccountingRecord(registrationId: string, request: CreateAccountingRecordRequest): Observable<AccountingRecordDto> {
		return this.http.post<AccountingRecordDto>(`${this.apiUrl}/${registrationId}/accounting`, request);
	}

	processRefund(request: RefundRequest): Observable<RefundResponse> {
		return this.http.post<RefundResponse>(`${this.apiUrl}/refund`, request);
	}

	getPaymentMethods(): Observable<PaymentMethodOptionDto[]> {
		return this.http.get<PaymentMethodOptionDto[]>(`${this.apiUrl}/payment-methods`);
	}

	sendBatchEmail(request: BatchEmailRequest): Observable<BatchEmailResponse> {
		return this.http.post<BatchEmailResponse>(`${this.apiUrl}/batch-email`, request);
	}

	previewEmail(request: EmailPreviewRequest): Observable<EmailPreviewResponse> {
		return this.http.post<EmailPreviewResponse>(`${this.apiUrl}/email-preview`, request);
	}
}
