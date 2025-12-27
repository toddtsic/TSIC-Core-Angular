import { Component, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from './toast.service';

@Component({
    selector: 'tsic-toasts',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="tsic-toasts position-fixed top-0 end-0 p-3" style="z-index: 2000;">
      <div *ngFor="let t of toasts()" class="toast show align-items-center text-bg-{{t.type}} border-0 mb-2 shadow" role="status" aria-live="polite" aria-atomic="true">
        <div class="d-flex">
          <div class="toast-body">{{ t.message }}</div>
          <button type="button" class="btn-close btn-close-white me-2 m-auto" aria-label="Close" (click)="dismiss(t.id)"></button>
        </div>
      </div>
    </div>
  `,
    styles: [
        `:host{pointer-events:none}`,
        `.tsic-toasts{pointer-events:none}`,
        `.toast{pointer-events:auto}`
    ]
})
export class ToastsComponent {
    private readonly toastService = inject(ToastService);
    toasts = computed(() => this.toastService.toasts());
    dismiss(id: number) { this.toastService.dismiss(id); }
}
