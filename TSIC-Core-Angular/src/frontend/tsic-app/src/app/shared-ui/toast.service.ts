import { Injectable, signal } from '@angular/core';

export type ToastType = 'success' | 'info' | 'warning' | 'danger';

export interface Toast {
    id: number;
    message: string;
    type: ToastType;
    title?: string;
    timeout?: number;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
    private readonly _toasts = signal<Toast[]>([]);
    private _nextId = 1;

    toasts = this._toasts.asReadonly();

    /** Default auto-dismiss by severity: success/info auto-close; warning/danger stick until user closes. */
    private readonly DEFAULT_TIMEOUTS: Record<ToastType, number> = {
        success: 5000,
        info: 5000,
        warning: 0,
        danger: 0,
    };

    show(message: string, type: ToastType = 'success', timeout?: number, title?: string) {
        const id = this._nextId++;
        const resolvedTimeout = timeout ?? this.DEFAULT_TIMEOUTS[type];
        const toast: Toast = { id, message, type, title, timeout: resolvedTimeout };
        this._toasts.update(list => [...list, toast]);

        if (resolvedTimeout > 0) {
            setTimeout(() => this.dismiss(id), resolvedTimeout);
        }
    }

    dismiss(id: number) {
        this._toasts.update(list => list.filter(t => t.id !== id));
    }

    clear() {
        this._toasts.set([]);
    }
}
