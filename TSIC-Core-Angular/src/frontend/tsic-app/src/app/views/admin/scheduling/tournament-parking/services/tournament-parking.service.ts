import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';

// ── Types (mirrors backend DTOs — replace with @core/api imports after model regen) ──

export interface TournamentParkingRequest {
	arrivalBufferMinutes: number;
	departureBufferMinutes: number;
	carMultiplier: number;
}

export interface TournamentParkingResponse {
	rollup: ParkingTimeslotDto[];
	complexDays: ParkingComplexDayDto[];
	summary: ParkingSummaryDto;
}

export interface ParkingTimeslotDto {
	fieldComplex: string;
	day: string;
	time: string;
	teamsArriving: number | null;
	teamsDeparting: number | null;
	teamsNet: number;
	teamsOnSite: number;
	carsArriving: number | null;
	carsDeparting: number | null;
	carsNet: number;
	carsOnSite: number;
}

export interface ParkingComplexDayDto {
	fieldComplex: string;
	day: string;
	label: string;
	timeslots: ParkingTimeslotDto[];
}

export interface ParkingSummaryDto {
	totalComplexes: number;
	totalDays: number;
	peakTeamsOnSite: number;
	peakTeamsComplex: string;
	peakCarsOnSite: number;
	peakCarsComplex: string;
}

@Injectable({ providedIn: 'root' })
export class TournamentParkingService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/tournament-parking`;

	getReport(request: TournamentParkingRequest): Observable<TournamentParkingResponse> {
		return this.http.post<TournamentParkingResponse>(`${this.apiUrl}/report`, request);
	}
}
