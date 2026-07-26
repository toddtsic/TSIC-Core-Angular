import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import { skipErrorToast } from '@infrastructure/interceptors/http-error-context';

/**
 * Admin/director read-only view of a player's medical form, keyed by REGISTRATION id (not userId).
 * The backend endpoint validates the registration belongs to the caller's job before streaming, so
 * this control can only ever surface a form for a registrant in the director's own job.
 *
 * Renders NOTHING unless a form is actually on file (HEAD → 204), so it stays invisible for the
 * jobs — the vast majority — that don't collect med forms. Read-only by design: upload/delete live
 * on the self/family registration flow, not here.
 */
@Component({
    selector: 'app-medform-view',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
      @if (hasFile()) {
        <div class="medform-view">
          <div class="medform-view-row">
            <i class="bi bi-file-earmark-pdf-fill"></i>
            <span class="medform-view-label">Medical form on file</span>
            <button type="button" class="btn btn-sm btn-outline-primary"
                    (click)="view()" [disabled]="isViewing()">
              @if (isViewing()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              } @else {
                <i class="bi bi-eye me-1"></i>
              }
              View
            </button>
          </div>
          @if (errorMessage(); as msg) {
            <div class="medform-view-error"><i class="bi bi-exclamation-triangle-fill me-1"></i>{{ msg }}</div>
          }
        </div>
      }
    `,
    styles: [`
      .medform-view {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
      }

      .medform-view-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border: 1px solid rgba(var(--bs-success-rgb), 0.4);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-success-rgb), 0.05);
        font-size: var(--font-size-sm);

        .bi-file-earmark-pdf-fill { color: var(--bs-danger); font-size: var(--font-size-lg); }
      }

      .medform-view-label {
        flex: 1;
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
      }

      .medform-view-error {
        font-size: var(--font-size-xs);
        color: var(--bs-danger);
        font-weight: var(--font-weight-medium);
      }
    `],
})
export class MedFormViewComponent implements OnInit {
    /** RegistrationId of the registrant whose med form is viewed. Job-validated server-side. */
    readonly registrationId = input.required<string>();

    private readonly http = inject(HttpClient);
    private readonly baseUrl = computed(() =>
        `${environment.apiUrl}/files/medform/by-registration/${encodeURIComponent(this.registrationId())}`);

    readonly hasFile = signal(false);
    readonly isViewing = signal(false);
    readonly errorMessage = signal<string | null>(null);

    ngOnInit(): void {
        // 404 is the expected "no file" state (and 403 shouldn't happen for an in-job director) —
        // suppress the global toast either way; the control simply stays hidden.
        this.http.head(this.baseUrl(), { observe: 'response', context: skipErrorToast() }).subscribe({
            next: () => this.hasFile.set(true),
            error: () => this.hasFile.set(false),
        });
    }

    view(): void {
        this.errorMessage.set(null);
        this.isViewing.set(true);
        this.http.get(this.baseUrl(), { responseType: 'blob' }).subscribe({
            next: (blob) => {
                this.isViewing.set(false);
                const url = URL.createObjectURL(blob);
                window.open(url, '_blank');
                setTimeout(() => URL.revokeObjectURL(url), 60_000);
            },
            error: (err: HttpErrorResponse) => {
                this.isViewing.set(false);
                this.errorMessage.set(err.status === 403 ? 'Not authorized to view this file.' : 'Could not load file.');
            },
        });
    }
}
