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
	UpdateFamilyContactRequest,
	UpdateUserDemographicsRequest,
	CreateAccountingRecordRequest,
	AccountingRecordDto,
	RefundRequest,
	RefundResponse,
	PaymentMethodOptionDto,
	BatchEmailRequest,
	BatchEmailResponse,
	EmailPreviewRequest,
	EmailPreviewResponse,
	LadtTreeRootDto,
	JobOptionDto,
	ChangeJobRequest,
	ChangeJobResponse,
	DeleteRegistrationResponse,
	RegistrationCcChargeRequest,
	RegistrationCcChargeResponse,
	RegistrationCheckOrCorrectionRequest,
	RegistrationCheckOrCorrectionResponse,
	EditAccountingRecordRequest,
	SubscriptionDetailDto
} from '@core/api';

// Re-export for consumers
export type {
	RegistrationSearchRequest,
	RegistrationSearchResponse,
	RegistrationSearchResultDto,
	RegistrationFilterOptionsDto,
	RegistrationDetailDto,
	UpdateRegistrationProfileRequest,
	UpdateFamilyContactRequest,
	UpdateUserDemographicsRequest,
	FamilyContactDto,
	UserDemographicsDto,
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
	FilterOption,
	LadtTreeRootDto,
	LadtTreeNodeDto,
	JobOptionDto,
	ChangeJobRequest,
	ChangeJobResponse,
	DeleteRegistrationResponse,
	RegistrationCcChargeRequest,
	RegistrationCcChargeResponse,
	RegistrationCheckOrCorrectionRequest,
	RegistrationCheckOrCorrectionResponse,
	EditAccountingRecordRequest,
	SubscriptionDetailDto,
	CreditCardInfo
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

	updateFamilyContact(registrationId: string, request: UpdateFamilyContactRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/${registrationId}/family`, request);
	}

	updateDemographics(registrationId: string, request: UpdateUserDemographicsRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/${registrationId}/demographics`, request);
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

	getLadtTree(): Observable<LadtTreeRootDto> {
		return this.http.get<LadtTreeRootDto>(`${environment.apiUrl}/ladt/tree`);
	}

	getChangeJobOptions(): Observable<JobOptionDto[]> {
		return this.http.get<JobOptionDto[]>(`${this.apiUrl}/change-job-options`);
	}

	changeJob(registrationId: string, request: ChangeJobRequest): Observable<ChangeJobResponse> {
		return this.http.post<ChangeJobResponse>(`${this.apiUrl}/${registrationId}/change-job`, request);
	}

	deleteRegistration(registrationId: string): Observable<DeleteRegistrationResponse> {
		return this.http.delete<DeleteRegistrationResponse>(`${this.apiUrl}/${registrationId}`);
	}

	chargeCc(registrationId: string, request: RegistrationCcChargeRequest): Observable<RegistrationCcChargeResponse> {
		return this.http.post<RegistrationCcChargeResponse>(`${this.apiUrl}/${registrationId}/charge-cc`, request);
	}

	recordPayment(registrationId: string, request: RegistrationCheckOrCorrectionRequest): Observable<RegistrationCheckOrCorrectionResponse> {
		return this.http.post<RegistrationCheckOrCorrectionResponse>(`${this.apiUrl}/${registrationId}/record-payment`, request);
	}

	editAccountingRecord(aId: number, request: EditAccountingRecordRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/accounting/${aId}`, request);
	}

	getSubscription(registrationId: string): Observable<SubscriptionDetailDto> {
		return this.http.get<SubscriptionDetailDto>(`${this.apiUrl}/${registrationId}/subscription`);
	}

	cancelSubscription(registrationId: string): Observable<void> {
		return this.http.post<void>(`${this.apiUrl}/${registrationId}/cancel-subscription`, {});
	}
}
