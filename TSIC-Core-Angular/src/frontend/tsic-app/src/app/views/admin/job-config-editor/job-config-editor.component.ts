import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from './services/job-config.service';
import { ToastService } from '@shared-ui/toast.service';
import type { JobConfigDto, JobConfigLookupsDto, UpdateJobConfigRequest } from '@core/api';

type TabId = 'general' | 'registration' | 'payment' | 'email' | 'features' | 'ddl';

@Component({
	selector: 'app-job-config-editor',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './job-config-editor.component.html',
	styleUrl: './job-config-editor.component.scss',
})
export class JobConfigEditorComponent {
	private readonly configService = inject(JobConfigService);
	private readonly toast = inject(ToastService);

	// ── Data signals ──
	readonly config = signal<JobConfigDto | null>(null);
	readonly lookups = signal<JobConfigLookupsDto | null>(null);
	private readonly originalJson = signal('');

	// ── UI state ──
	readonly activeTab = signal<TabId>('general');
	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);

	// ── Dirty tracking ──
	readonly isDirty = computed(() => {
		const current = this.config();
		if (!current) return false;
		return JSON.stringify(current) !== this.originalJson();
	});

	// ── Tab definitions ──
	readonly tabs: { id: TabId; label: string; icon: string }[] = [
		{ id: 'general', label: 'General', icon: 'bi-gear' },
		{ id: 'registration', label: 'Registration', icon: 'bi-person-plus' },
		{ id: 'payment', label: 'Payment', icon: 'bi-credit-card' },
		{ id: 'email', label: 'Email & Templates', icon: 'bi-envelope' },
		{ id: 'features', label: 'Features & Store', icon: 'bi-toggles' },
		{ id: 'ddl', label: 'DDL Options', icon: 'bi-list-ul' },
	];

	constructor() {
		this.loadData();
	}

	// ── Data loading ──

	private loadData(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.configService.getLookups().subscribe({
			next: (lookups) => this.lookups.set(lookups),
			error: (err) => {
				this.errorMessage.set(err.error?.message || 'Failed to load lookups.');
				this.isLoading.set(false);
			},
		});

		this.configService.getConfig().subscribe({
			next: (config) => {
				this.config.set(config);
				this.originalJson.set(JSON.stringify(config));
				this.isLoading.set(false);
			},
			error: (err) => {
				this.errorMessage.set(err.error?.message || 'Failed to load job configuration.');
				this.isLoading.set(false);
			},
		});
	}

	// ── Field update helper ──

	updateField<K extends keyof JobConfigDto>(key: K, value: JobConfigDto[K]): void {
		const current = this.config();
		if (!current) return;
		this.config.set({ ...current, [key]: value });
	}

	updateFieldFromEvent<K extends keyof JobConfigDto>(key: K, event: Event): void {
		const el = event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
		const current = this.config();
		if (!current) return;

		// Determine value type from current config
		const currentVal = current[key];
		let value: unknown;

		if (typeof currentVal === 'boolean' || currentVal === true || currentVal === false) {
			value = (el as HTMLInputElement).checked;
		} else if (typeof currentVal === 'number') {
			value = el.value === '' ? null : Number(el.value);
		} else {
			value = el.value === '' ? null : el.value;
		}

		this.config.set({ ...current, [key]: value as JobConfigDto[K] });
	}

	updateCheckbox<K extends keyof JobConfigDto>(key: K, event: Event): void {
		const checked = (event.target as HTMLInputElement).checked;
		const current = this.config();
		if (!current) return;
		this.config.set({ ...current, [key]: checked as JobConfigDto[K] });
	}

	updateNumber<K extends keyof JobConfigDto>(key: K, event: Event): void {
		const el = event.target as HTMLInputElement;
		const current = this.config();
		if (!current) return;
		const value = el.value === '' ? null : Number(el.value);
		this.config.set({ ...current, [key]: value as JobConfigDto[K] });
	}

	// ── Save ──

	save(): void {
		const current = this.config();
		if (!current || !this.isDirty() || this.isSaving()) return;

		this.isSaving.set(true);
		this.errorMessage.set(null);

		const request: UpdateJobConfigRequest = {
			jobId: current.jobId,
			updatedOn: current.updatedOn,
			// General
			jobName: current.jobName,
			displayName: current.displayName,
			jobDescription: current.jobDescription,
			jobTagline: current.jobTagline,
			jobCode: current.jobCode,
			year: current.year,
			season: current.season,
			jobTypeId: current.jobTypeId,
			sportId: current.sportId,
			billingTypeId: current.billingTypeId,
			expiryAdmin: current.expiryAdmin,
			expiryUsers: current.expiryUsers,
			eventStartDate: current.eventStartDate,
			eventEndDate: current.eventEndDate,
			searchenginKeywords: current.searchenginKeywords,
			searchengineDescription: current.searchengineDescription,
			bBannerIsCustom: current.bBannerIsCustom,
			bannerFile: current.bannerFile,
			mobileJobName: current.mobileJobName,
			jobNameQbp: current.jobNameQbp,
			momLabel: current.momLabel,
			dadLabel: current.dadLabel,
			bSuspendPublic: current.bSuspendPublic,
			// Registration
			bRegistrationAllowPlayer: current.bRegistrationAllowPlayer,
			bRegistrationAllowTeam: current.bRegistrationAllowTeam,
			bAllowMobileRegn: current.bAllowMobileRegn,
			bUseWaitlists: current.bUseWaitlists,
			bRestrictPlayerTeamsToAgerange: current.bRestrictPlayerTeamsToAgerange,
			bOfferPlayerRegsaverInsurance: current.bOfferPlayerRegsaverInsurance,
			bOfferTeamRegsaverInsurance: current.bOfferTeamRegsaverInsurance,
			playerRegMultiPlayerDiscountMin: current.playerRegMultiPlayerDiscountMin,
			playerRegMultiPlayerDiscountPercent: current.playerRegMultiPlayerDiscountPercent,
			coreRegformPlayer: current.coreRegformPlayer,
			regformNamePlayer: current.regformNamePlayer,
			regformNameTeam: current.regformNameTeam,
			regformNameCoach: current.regformNameCoach,
			regformNameClubRep: current.regformNameClubRep,
			playerProfileMetadataJson: current.playerProfileMetadataJson,
			uslaxNumberValidThroughDate: current.uslaxNumberValidThroughDate,
			// Payment
			paymentMethodsAllowedCode: current.paymentMethodsAllowedCode,
			bAddProcessingFees: current.bAddProcessingFees,
			processingFeePercent: current.processingFeePercent,
			bApplyProcessingFeesToTeamDeposit: current.bApplyProcessingFeesToTeamDeposit,
			bTeamsFullPaymentRequired: current.bTeamsFullPaymentRequired,
			balancedueaspercent: current.balancedueaspercent,
			bAllowRefundsInPriorMonths: current.bAllowRefundsInPriorMonths,
			bAllowCreditAll: current.bAllowCreditAll,
			payTo: current.payTo,
			mailTo: current.mailTo,
			mailinPaymentWarning: current.mailinPaymentWarning,
			adnArb: current.adnArb,
			adnArbbillingOccurences: current.adnArbbillingOccurences,
			adnArbintervalLength: current.adnArbintervalLength,
			adnArbstartDate: current.adnArbstartDate,
			adnArbMinimunTotalCharge: current.adnArbMinimunTotalCharge,
			// Email & Templates
			regFormFrom: current.regFormFrom,
			regFormCcs: current.regFormCcs,
			regFormBccs: current.regFormBccs,
			rescheduleemaillist: current.rescheduleemaillist,
			alwayscopyemaillist: current.alwayscopyemaillist,
			bDisallowCcplayerConfirmations: current.bDisallowCcplayerConfirmations,
			playerRegConfirmationEmail: current.playerRegConfirmationEmail,
			playerRegConfirmationOnScreen: current.playerRegConfirmationOnScreen,
			playerRegRefundPolicy: current.playerRegRefundPolicy,
			playerRegReleaseOfLiability: current.playerRegReleaseOfLiability,
			playerRegCodeOfConduct: current.playerRegCodeOfConduct,
			playerRegCovid19Waiver: current.playerRegCovid19Waiver,
			adultRegConfirmationEmail: current.adultRegConfirmationEmail,
			adultRegConfirmationOnScreen: current.adultRegConfirmationOnScreen,
			adultRegRefundPolicy: current.adultRegRefundPolicy,
			adultRegReleaseOfLiability: current.adultRegReleaseOfLiability,
			adultRegCodeOfConduct: current.adultRegCodeOfConduct,
			refereeRegConfirmationEmail: current.refereeRegConfirmationEmail,
			refereeRegConfirmationOnScreen: current.refereeRegConfirmationOnScreen,
			recruiterRegConfirmationEmail: current.recruiterRegConfirmationEmail,
			recruiterRegConfirmationOnScreen: current.recruiterRegConfirmationOnScreen,
			// Features & Store
			bClubRepAllowEdit: current.bClubRepAllowEdit,
			bClubRepAllowDelete: current.bClubRepAllowDelete,
			bClubRepAllowAdd: current.bClubRepAllowAdd,
			bAllowMobileLogin: current.bAllowMobileLogin,
			bAllowRosterViewAdult: current.bAllowRosterViewAdult,
			bAllowRosterViewPlayer: current.bAllowRosterViewPlayer,
			bShowTeamNameOnlyInSchedules: current.bShowTeamNameOnlyInSchedules,
			bScheduleAllowPublicAccess: current.bScheduleAllowPublicAccess,
			bTeamPushDirectors: current.bTeamPushDirectors,
			bEnableTsicteams: current.bEnableTsicteams,
			bEnableMobileRsvp: current.bEnableMobileRsvp,
			bEnableMobileTeamChat: current.bEnableMobileTeamChat,
			benableStp: current.benableStp,
			bEnableStore: current.bEnableStore,
			bSignalRschedule: current.bSignalRschedule,
			mobileScoreHoursPastGameEligible: current.mobileScoreHoursPastGameEligible,
			storeSalesTax: current.storeSalesTax,
			storeRefundPolicy: current.storeRefundPolicy,
			storePickupDetails: current.storePickupDetails,
			storeContactEmail: current.storeContactEmail,
			storeTsicrate: current.storeTsicrate,
		};

		this.configService.updateConfig(request).subscribe({
			next: (updated) => {
				this.config.set(updated);
				this.originalJson.set(JSON.stringify(updated));
				this.isSaving.set(false);
				this.toast.show('Configuration saved successfully.', 'success');
			},
			error: (err) => {
				this.isSaving.set(false);
				if (err.status === 409) {
					this.errorMessage.set('Configuration was modified by another user. Please reload the page.');
					this.toast.show('Concurrency conflict — please reload.', 'danger');
				} else {
					this.errorMessage.set(err.error?.message || 'Failed to save configuration.');
					this.toast.show('Save failed.', 'danger');
				}
			},
		});
	}

	reload(): void {
		this.loadData();
	}

	discard(): void {
		const json = this.originalJson();
		if (!json) return;
		this.config.set(JSON.parse(json));
	}

	// ── Date formatting helper for date inputs ──

	toDateInputValue(dateStr: string | null | undefined): string {
		if (!dateStr) return '';
		const d = new Date(dateStr);
		if (isNaN(d.getTime())) return '';
		return d.toISOString().split('T')[0];
	}
}
