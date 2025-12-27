import { Injectable } from '@angular/core';

/**
 * Encapsulates persistence of idempotency keys for payment submissions.
 * Key format: tsic:payidem:<jobId>:<familyUserId>
 */
@Injectable({ providedIn: 'root' })
export class IdempotencyService {
    private buildKey(jobId: string | undefined | null, familyUserId: string | undefined | null): string | null {
        if (!jobId || !familyUserId) return null;
        return `tsic:payidem:${jobId}:${familyUserId}`;
    }

    load(jobId: string | undefined | null, familyUserId: string | undefined | null): string | null {
        const k = this.buildKey(jobId, familyUserId);
        if (!k) return null;
        try { return localStorage.getItem(k); } catch { return null; }
    }

    persist(jobId: string | undefined | null, familyUserId: string | undefined | null, value: string): void {
        const k = this.buildKey(jobId, familyUserId);
        if (!k) return;
        try { localStorage.setItem(k, value); } catch { /* ignore */ }
    }

    clear(jobId: string | undefined | null, familyUserId: string | undefined | null): void {
        const k = this.buildKey(jobId, familyUserId);
        if (!k) return;
        try { localStorage.removeItem(k); } catch { /* ignore */ }
    }
}
