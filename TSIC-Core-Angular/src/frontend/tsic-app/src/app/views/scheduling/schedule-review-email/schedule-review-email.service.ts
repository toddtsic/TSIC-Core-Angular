import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { ScheduleReviewRecipientDto } from '@core/api';

/**
 * Reads the FIXED POPULATION for the schedule-review letter. The send itself rides the
 * existing batch-email endpoint (RegistrationSearchService) — this only answers "who".
 */
@Injectable({ providedIn: 'root' })
export class ScheduleReviewEmailService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/schedule-review`;

    /** Active club reps on the current job holding at least one scheduled team. */
    getRecipients(): Observable<ScheduleReviewRecipientDto[]> {
        return this.http.get<ScheduleReviewRecipientDto[]>(`${this.apiUrl}/recipients`);
    }
}
