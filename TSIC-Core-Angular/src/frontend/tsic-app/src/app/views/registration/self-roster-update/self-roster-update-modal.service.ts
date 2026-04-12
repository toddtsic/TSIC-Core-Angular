import { Injectable, signal } from '@angular/core';

/**
 * Controls visibility of the self-roster update modal.
 * Triggered by bulletin link clicks via InternalLinkDirective.
 */
@Injectable({ providedIn: 'root' })
export class SelfRosterUpdateModalService {
    private readonly _isOpen = signal(false);
    private readonly _jobPath = signal<string>('');

    readonly isOpen = this._isOpen.asReadonly();
    readonly jobPath = this._jobPath.asReadonly();

    open(jobPath: string): void {
        this._jobPath.set(jobPath);
        this._isOpen.set(true);
    }

    close(): void {
        this._isOpen.set(false);
    }
}
