import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import { skipErrorToast } from '@infrastructure/interceptors/http-error-context';

const MAX_BYTES = 10 * 1024 * 1024;

@Component({
    selector: 'app-medform-upload',
    standalone: true,
    imports: [CommonModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
      <div class="medform">
        @if (isChecking()) {
          <div class="medform-row medform-row--muted">
            <span class="spinner-border spinner-border-sm"></span>
            <span>Checking for med form…</span>
          </div>
        } @else if (hasFile()) {
          <div class="medform-row medform-row--present">
            <i class="bi bi-file-earmark-pdf-fill"></i>
            <span class="medform-status">Med form on file</span>
            <button type="button" class="btn btn-sm btn-outline-primary"
                    (click)="view()" [disabled]="isViewing()">
              @if (isViewing()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              } @else {
                <i class="bi bi-eye me-1"></i>
              }
              View
            </button>
            <button type="button" class="btn btn-sm btn-outline-danger"
                    (click)="remove()" [disabled]="isDeleting()">
              @if (isDeleting()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              } @else {
                <i class="bi bi-trash me-1"></i>
              }
              Delete
            </button>
          </div>
        } @else {
          <label class="medform-drop"
                 [class.is-drag]="isDragOver()"
                 (dragover)="onDragOver($event)"
                 (dragleave)="onDragLeave()"
                 (drop)="onDrop($event)">
            <input type="file" accept="application/pdf"
                   class="medform-file-input"
                   (change)="onFileSelected($event)"
                   [disabled]="isUploading()">
            @if (isUploading()) {
              <span class="spinner-border spinner-border-sm me-2"></span>
              Uploading…
            } @else {
              <i class="bi bi-cloud-arrow-up medform-drop-icon"></i>
              <span class="medform-drop-label">
                Drop PDF here or click to choose
              </span>
              <span class="medform-drop-hint">PDF only, 10 MB max</span>
            }
          </label>
        }

        @if (errorMessage(); as msg) {
          <div class="medform-error"><i class="bi bi-exclamation-triangle-fill me-1"></i>{{ msg }}</div>
        }
      </div>
    `,
    styles: [`
      .medform {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
      }

      .medform-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-sm);
        background: var(--neutral-0);
        font-size: var(--font-size-sm);
      }

      .medform-row--present {
        border-color: rgba(var(--bs-success-rgb), 0.4);
        background: rgba(var(--bs-success-rgb), 0.05);

        .bi-file-earmark-pdf-fill { color: var(--bs-danger); font-size: var(--font-size-lg); }
      }

      .medform-row--muted { color: var(--brand-text-muted); }

      .medform-status {
        flex: 1;
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
      }

      .medform-drop {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 2px;
        padding: var(--space-3);
        border: 1px dashed var(--border-color);
        border-radius: var(--radius-sm);
        background: var(--neutral-0);
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        cursor: pointer;
        transition: border-color 120ms, background 120ms;

        &:hover, &.is-drag {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.04);
          color: var(--bs-primary);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .medform-drop { transition: none; }
      }

      .medform-file-input {
        position: absolute;
        width: 1px;
        height: 1px;
        opacity: 0;
        pointer-events: none;
      }

      .medform-drop-icon {
        font-size: var(--font-size-xl);
        color: var(--bs-primary);
      }

      .medform-drop-label {
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
      }

      .medform-drop-hint {
        font-size: var(--font-size-xs);
      }

      .medform-error {
        font-size: var(--font-size-xs);
        color: var(--bs-danger);
        font-weight: var(--font-weight-medium);
      }
    `],
})
export class MedFormUploadComponent implements OnInit {
    /** Identity userId of the player whose med form is uploaded/viewed/deleted. */
    readonly playerUserId = input.required<string>();

    private readonly http = inject(HttpClient);
    private readonly baseUrl = computed(() => `${environment.apiUrl}/files/medform/${encodeURIComponent(this.playerUserId())}`);

    readonly isChecking = signal(true);
    readonly hasFile = signal(false);
    readonly isUploading = signal(false);
    readonly isDeleting = signal(false);
    readonly isViewing = signal(false);
    readonly isDragOver = signal(false);
    readonly errorMessage = signal<string | null>(null);

    ngOnInit(): void {
        this.refresh();
    }

    private refresh(): void {
        this.isChecking.set(true);
        // 404 is the expected "no file yet" state — suppress the global toast.
        this.http.head(this.baseUrl(), { observe: 'response', context: skipErrorToast() }).subscribe({
            next: () => {
                this.hasFile.set(true);
                this.isChecking.set(false);
            },
            error: (err: HttpErrorResponse) => {
                this.hasFile.set(err.status !== 404 ? this.hasFile() : false);
                this.isChecking.set(false);
            },
        });
    }

    onDragOver(event: DragEvent): void {
        event.preventDefault();
        event.stopPropagation();
        this.isDragOver.set(true);
    }

    onDragLeave(): void {
        this.isDragOver.set(false);
    }

    onDrop(event: DragEvent): void {
        event.preventDefault();
        event.stopPropagation();
        this.isDragOver.set(false);
        const file = event.dataTransfer?.files[0];
        if (file) this.uploadFile(file);
    }

    onFileSelected(event: Event): void {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0];
        if (file) this.uploadFile(file);
        input.value = '';
    }

    private uploadFile(file: File): void {
        this.errorMessage.set(null);

        const ext = file.name.split('.').pop()?.toLowerCase();
        if (ext !== 'pdf' && file.type !== 'application/pdf') {
            this.errorMessage.set('Only PDF files are accepted.');
            return;
        }
        if (file.size > MAX_BYTES) {
            this.errorMessage.set('File exceeds 10 MB limit.');
            return;
        }

        const formData = new FormData();
        formData.append('file', file);

        this.isUploading.set(true);
        this.http.post(this.baseUrl(), formData).subscribe({
            next: () => {
                this.isUploading.set(false);
                this.hasFile.set(true);
            },
            error: (err: HttpErrorResponse) => {
                this.isUploading.set(false);
                this.errorMessage.set(err.error?.error || 'Upload failed. Please try again.');
            },
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

    remove(): void {
        if (!confirm('Delete the uploaded medical form?')) return;
        this.errorMessage.set(null);
        this.isDeleting.set(true);
        this.http.delete(this.baseUrl()).subscribe({
            next: () => {
                this.isDeleting.set(false);
                this.hasFile.set(false);
            },
            error: (err: HttpErrorResponse) => {
                this.isDeleting.set(false);
                this.errorMessage.set(err.error?.error || 'Delete failed.');
            },
        });
    }
}
