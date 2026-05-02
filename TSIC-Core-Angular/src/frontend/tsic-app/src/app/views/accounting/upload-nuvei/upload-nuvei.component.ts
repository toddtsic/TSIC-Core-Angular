import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import { NuveiUploadResultDto } from '@core/api';

type UploadKind = 'funding' | 'batches';

@Component({
    selector: 'app-upload-nuvei',
    standalone: true,
    imports: [CommonModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './upload-nuvei.component.html',
    styleUrls: ['./upload-nuvei.component.scss'],
})
export class UploadNuveiComponent {
    private readonly http = inject(HttpClient);

    private readonly endpoints: Record<UploadKind, string> = {
        funding: `${environment.apiUrl}/nuvei-upload/upload-funding`,
        batches: `${environment.apiUrl}/nuvei-upload/upload-batches`,
    };

    readonly fundingState = this.makeState();
    readonly batchesState = this.makeState();

    onDragOver(kind: UploadKind, event: DragEvent): void {
        event.preventDefault();
        event.stopPropagation();
        this.state(kind).isDragOver.set(true);
    }

    onDragLeave(kind: UploadKind): void {
        this.state(kind).isDragOver.set(false);
    }

    onDrop(kind: UploadKind, event: DragEvent): void {
        event.preventDefault();
        event.stopPropagation();
        this.state(kind).isDragOver.set(false);
        const file = event.dataTransfer?.files[0];
        if (file) this.selectFile(kind, file);
    }

    onFileSelected(kind: UploadKind, event: Event): void {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0];
        if (file) this.selectFile(kind, file);
        input.value = '';
    }

    upload(kind: UploadKind): void {
        const s = this.state(kind);
        const file = s.selectedFile();
        if (!file) return;

        s.isUploading.set(true);
        s.errorMessage.set('');
        s.uploadResult.set(null);

        const formData = new FormData();
        formData.append('file', file);

        this.http.post<NuveiUploadResultDto>(this.endpoints[kind], formData).subscribe({
            next: result => {
                s.isUploading.set(false);
                s.uploadResult.set(result);
                s.selectedFile.set(null);
            },
            error: err => {
                s.isUploading.set(false);
                const msg = err.error?.message || 'Upload failed. Please try again.';
                s.errorMessage.set(msg);
            },
        });
    }

    clearFile(kind: UploadKind): void {
        const s = this.state(kind);
        s.selectedFile.set(null);
        s.errorMessage.set('');
    }

    private selectFile(kind: UploadKind, file: File): void {
        const s = this.state(kind);
        s.errorMessage.set('');
        s.uploadResult.set(null);

        const ext = file.name.split('.').pop()?.toLowerCase();
        if (ext !== 'csv') {
            s.errorMessage.set('Only .csv files are accepted.');
            return;
        }

        if (file.size > 10 * 1024 * 1024) {
            s.errorMessage.set('File exceeds 10MB limit.');
            return;
        }

        s.selectedFile.set(file);
    }

    private state(kind: UploadKind) {
        return kind === 'funding' ? this.fundingState : this.batchesState;
    }

    private makeState() {
        return {
            isUploading: signal(false),
            selectedFile: signal<File | null>(null),
            uploadResult: signal<NuveiUploadResultDto | null>(null),
            errorMessage: signal(''),
            isDragOver: signal(false),
        };
    }
}
