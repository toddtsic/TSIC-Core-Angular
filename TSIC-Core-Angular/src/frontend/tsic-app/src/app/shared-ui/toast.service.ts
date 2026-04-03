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

    show(message: string, type: ToastType = 'success', timeout = 2000, title?: string) {
        const id = this._nextId++;
        const toast: Toast = { id, message, type, title, timeout };
        this._toasts.update(list => [...list, toast]);

        if (timeout && timeout > 0) {
            setTimeout(() => this.dismiss(id), timeout);
        }
    }

    dismiss(id: number) {
        this._toasts.update(list => list.filter(t => t.id !== id));
    }

    clear() {
        this._toasts.set([]);
    }
}
