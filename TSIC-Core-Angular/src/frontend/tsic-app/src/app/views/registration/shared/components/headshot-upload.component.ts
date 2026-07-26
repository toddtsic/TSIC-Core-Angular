import {
    ChangeDetectionStrategy, Component, OnDestroy, OnInit,
    computed, inject, input, output, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import { skipErrorToast } from '@infrastructure/interceptors/http-error-context';

const MAX_BYTES = 5 * 1024 * 1024;
const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
const ACCEPTED_EXTS = ['jpg', 'jpeg', 'png', 'webp'];

/**
 * Self-service registrant headshot control — offered as a form question in every registration flow.
 * Mirrors the native-input + HttpClient pattern of medform-upload; the server re-encodes whatever is
 * accepted to a downscaled {userId}.jpg on the public statics share.
 *
 * Two modes, driven by whether a userId is known at render time:
 *  - IMMEDIATE (userId set — player, returning adult, club rep post-login): pick → upload now; the
 *    control HEAD-probes for an existing headshot on init and supports replace + delete.
 *  - DEFERRED (userId null — a NEW adult whose account is minted at submit): pick → preview locally
 *    and emit (fileSelected); the parent wizard holds the File and uploads it once the account exists.
 *
 * Display prefers the locally-picked object URL over the stored statics URL, so the just-chosen image
 * shows immediately even on dev (where the statics URL resolves to PROD and won't round-trip a fresh
 * dev upload).
 */
@Component({
    selector: 'app-headshot-upload',
    standalone: true,
    imports: [CommonModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
      <div class="headshot">
        <div class="headshot-avatar"
             [class.is-drag]="isDragOver()"
             (dragover)="onDragOver($event)"
             (dragleave)="onDragLeave()"
             (drop)="onDrop($event)">
          @if (displayUrl(); as url) {
            <img [src]="url" alt="Headshot" class="headshot-img" (error)="onImgError()">
          } @else {
            <i class="bi bi-person-circle headshot-placeholder"></i>
          }
          @if (isUploading()) {
            <div class="headshot-overlay"><span class="spinner-border spinner-border-sm"></span></div>
          }
        </div>

        <div class="headshot-actions">
          <label class="btn btn-sm btn-outline-primary headshot-pick">
            <input type="file" [accept]="acceptAttr"
                   class="headshot-file-input"
                   (change)="onFileSelected($event)"
                   [disabled]="isUploading()">
            <i class="bi bi-camera me-1"></i>{{ displayUrl() ? 'Change photo' : 'Add photo' }}
          </label>
          @if (canRemove()) {
            <button type="button" class="btn btn-sm btn-outline-danger"
                    (click)="remove()" [disabled]="isDeleting()">
              @if (isDeleting()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              } @else {
                <i class="bi bi-trash me-1"></i>
              }
              Remove
            </button>
          }
          <span class="headshot-hint">JPG, PNG, or WebP · 5 MB max</span>
        </div>

        @if (errorMessage(); as msg) {
          <div class="headshot-error"><i class="bi bi-exclamation-triangle-fill me-1"></i>{{ msg }}</div>
        }
      </div>
    `,
    styles: [`
      .headshot {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-3);
      }

      .headshot-avatar {
        position: relative;
        width: 96px;
        height: 96px;
        flex: 0 0 auto;
        display: flex;
        align-items: center;
        justify-content: center;
        border-radius: 50%;
        overflow: hidden;
        border: 1px dashed var(--border-color);
        background: var(--neutral-0);
        transition: border-color 120ms, background 120ms;

        &.is-drag {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.04);
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .headshot-avatar { transition: none; }
      }

      .headshot-img {
        width: 100%;
        height: 100%;
        object-fit: cover;
      }

      .headshot-placeholder {
        font-size: 3.5rem;
        color: var(--brand-text-muted);
      }

      .headshot-overlay {
        position: absolute;
        inset: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        background: rgba(0, 0, 0, 0.35);
        color: #fff;
      }

      .headshot-actions {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-2);
      }

      .headshot-pick {
        cursor: pointer;
        margin: 0;

        &:focus-within {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      .headshot-file-input {
        position: absolute;
        width: 1px;
        height: 1px;
        opacity: 0;
        pointer-events: none;
      }

      .headshot-hint {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      .headshot-error {
        flex-basis: 100%;
        font-size: var(--font-size-xs);
        color: var(--bs-danger);
        font-weight: var(--font-weight-medium);
      }
    `],
})
export class HeadshotUploadComponent implements OnInit, OnDestroy {
    /**
     * Identity userId whose headshot is managed. Null puts the control in DEFERRED mode — the picked
     * file is previewed and emitted via (fileSelected) for the parent to upload after the account is
     * created. A stable value is expected per render (the control does not react to it changing).
     */
    readonly userId = input<string | null>(null);

    /** DEFERRED mode only: emits the picked File (or null when cleared) for the parent to hold + upload. */
    readonly fileSelected = output<File | null>();

    readonly acceptAttr = ACCEPTED_TYPES.join(',');

    private readonly http = inject(HttpClient);

    private readonly baseUrl = computed(() => {
        const id = this.userId();
        return id ? `${environment.apiUrl}/files/headshot/${encodeURIComponent(id)}` : null;
    });

    // Bumped after each successful upload/delete to force the stored <img> to reload.
    private readonly version = signal(Date.now());
    private objectUrl: string | null = null;

    readonly hasFile = signal(false);
    readonly previewUrl = signal<string | null>(null);
    readonly imgError = signal(false);
    readonly isUploading = signal(false);
    readonly isDeleting = signal(false);
    readonly isDragOver = signal(false);
    readonly errorMessage = signal<string | null>(null);

    /** Current stored headshot on statics (immediate mode, when a file is on file). */
    private readonly storedUrl = computed(() => {
        const id = this.userId();
        if (!id || !this.hasFile()) return null;
        return `${environment.staticsUrl}/Headshots-AllRegistrants/${encodeURIComponent(id)}.jpg?v=${this.version()}`;
    });

    /** What the avatar shows: a fresh local pick wins; else the stored image (unless it failed to load). */
    readonly displayUrl = computed(() =>
        this.previewUrl() ?? (this.imgError() ? null : this.storedUrl()));

    readonly canRemove = computed(() =>
        (!!this.userId() && this.hasFile()) || (!this.userId() && this.previewUrl() !== null));

    ngOnInit(): void {
        // Immediate mode only: probe for an existing headshot. 404 = none yet (suppress the toast).
        if (!this.baseUrl()) return;
        this.http.head(this.baseUrl()!, { observe: 'response', context: skipErrorToast() }).subscribe({
            next: () => this.hasFile.set(true),
            error: () => this.hasFile.set(false),
        });
    }

    ngOnDestroy(): void {
        this.revokeObjectUrl();
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
        if (file) this.pickFile(file);
    }

    onFileSelected(event: Event): void {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0];
        if (file) this.pickFile(file);
        input.value = '';
    }

    onImgError(): void {
        this.imgError.set(true);
    }

    private pickFile(file: File): void {
        this.errorMessage.set(null);

        const ext = file.name.split('.').pop()?.toLowerCase() ?? '';
        if (!ACCEPTED_TYPES.includes(file.type) && !ACCEPTED_EXTS.includes(ext)) {
            this.errorMessage.set('Only JPG, PNG, or WebP images are accepted.');
            return;
        }
        if (file.size > MAX_BYTES) {
            this.errorMessage.set('Image exceeds the 5 MB limit.');
            return;
        }

        // Show the picked image immediately (own object URL, revoke any prior).
        this.setPreview(file);

        if (this.baseUrl()) {
            this.uploadFile(file);
        } else {
            // Deferred: hand the File to the parent wizard.
            this.fileSelected.emit(file);
        }
    }

    private uploadFile(file: File): void {
        const formData = new FormData();
        formData.append('file', file);

        this.isUploading.set(true);
        this.http.post(this.baseUrl()!, formData).subscribe({
            next: () => {
                this.isUploading.set(false);
                this.hasFile.set(true);
                this.imgError.set(false);
                this.version.set(Date.now());
                // Keep the local preview showing — on dev the statics URL points at PROD and won't
                // round-trip a fresh upload; the object URL is the reliable confirmation.
            },
            error: (err: HttpErrorResponse) => {
                this.isUploading.set(false);
                this.clearPreview();
                this.errorMessage.set(err.error?.error || 'Upload failed. Please try again.');
            },
        });
    }

    remove(): void {
        // Deferred mode: just clear the local pick and tell the parent.
        if (!this.baseUrl()) {
            this.clearPreview();
            this.fileSelected.emit(null);
            return;
        }

        if (!confirm('Remove this headshot?')) return;
        this.errorMessage.set(null);
        this.isDeleting.set(true);
        this.http.delete(this.baseUrl()!).subscribe({
            next: () => {
                this.isDeleting.set(false);
                this.hasFile.set(false);
                this.clearPreview();
                this.version.set(Date.now());
            },
            error: (err: HttpErrorResponse) => {
                this.isDeleting.set(false);
                this.errorMessage.set(err.error?.error || 'Remove failed.');
            },
        });
    }

    private setPreview(file: File): void {
        this.revokeObjectUrl();
        this.objectUrl = URL.createObjectURL(file);
        this.imgError.set(false);
        this.previewUrl.set(this.objectUrl);
    }

    private clearPreview(): void {
        this.revokeObjectUrl();
        this.previewUrl.set(null);
    }

    private revokeObjectUrl(): void {
        if (this.objectUrl) {
            URL.revokeObjectURL(this.objectUrl);
            this.objectUrl = null;
        }
    }
}
