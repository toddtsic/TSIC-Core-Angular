import { TestBed } from '@angular/core/testing';
import { TranslateLegacyUrlsPipe } from './translate-legacy-urls.pipe';
import { JobService } from '@infrastructure/services/job.service';
import { signal } from '@angular/core';

/**
 * Stub JobService — the pipe reads `currentJob()` for JobTypeId only.
 * Returning null is safe (pipe defaults to 'unassigned').
 */
class JobServiceStub {
    currentJob = signal<{ jobTypeId: number } | null>(null);
}

describe('TranslateLegacyUrlsPipe - inline style → safe form', () => {
    let pipe: TranslateLegacyUrlsPipe;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [
                TranslateLegacyUrlsPipe,
                { provide: JobService, useClass: JobServiceStub },
            ],
        });
        pipe = TestBed.inject(TranslateLegacyUrlsPipe);
    });

    const run = (html: string) => pipe.transform(html, 'JOB123');

    // ── font-size ──────────────────────────────────────────────────────────
    it('maps font-size:22px to bl-fs-22 class', () => {
        expect(run('<span style="font-size:22px;">x</span>'))
            .toBe('<span class="bl-fs-22">x</span>');
    });
    it('maps font-size:18px and 14px to their classes', () => {
        expect(run('<span style="font-size:18px">x</span>')).toBe('<span class="bl-fs-18">x</span>');
        expect(run('<span style="font-size:14px">x</span>')).toBe('<span class="bl-fs-14">x</span>');
    });
    it('leaves unknown font-size in style (will be stripped by sanitizer)', () => {
        expect(run('<span style="font-size:16px">x</span>'))
            .toBe('<span style="font-size:16px">x</span>');
    });

    // ── color ──────────────────────────────────────────────────────────────
    it('maps #0000FF and #0000CD both to bl-c-blue', () => {
        expect(run('<span style="color:#0000FF">x</span>')).toBe('<span class="bl-c-blue">x</span>');
        expect(run('<span style="color:#0000CD">x</span>')).toBe('<span class="bl-c-blue">x</span>');
    });
    it('maps #FF0000 to bl-c-red and #000000 to bl-c-black', () => {
        expect(run('<span style="color:#FF0000">x</span>')).toBe('<span class="bl-c-red">x</span>');
        expect(run('<span style="color:#000000">x</span>')).toBe('<span class="bl-c-black">x</span>');
    });
    it('is case-insensitive on hex values', () => {
        expect(run('<span style="color:#ff0000">x</span>')).toBe('<span class="bl-c-red">x</span>');
    });

    // ── combined properties ────────────────────────────────────────────────
    it('emits two classes for combined color + font-size', () => {
        expect(run('<span style="color:#0000FF;font-size:22px;">x</span>'))
            .toBe('<span class="bl-c-blue bl-fs-22">x</span>');
    });
    it('drops background-color:transparent noise while preserving other properties', () => {
        expect(run('<span style="background-color:transparent;color:#000000;font-size:22px;">x</span>'))
            .toBe('<span class="bl-c-black bl-fs-22">x</span>');
    });

    // ── img width/height promotion ─────────────────────────────────────────
    it('promotes img style width to HTML width attribute', () => {
        expect(run('<img src="a.png" style="width:80px;">'))
            .toBe('<img src="a.png" width="80">');
    });
    it('promotes img style width+height together', () => {
        expect(run('<img style="width:80px;height:80px;" src="a.png">'))
            .toBe('<img src="a.png" width="80" height="80">');
    });
    it('merges img class with existing class attribute', () => {
        expect(run('<img class="image_resized" style="width:70px;" src="a.png">'))
            .toBe('<img class="image_resized" src="a.png" width="70">');
    });

    // ── class merge ────────────────────────────────────────────────────────
    it('appends new classes to existing class attribute without duplicates', () => {
        expect(run('<span class="foo" style="color:#0000FF">x</span>'))
            .toBe('<span class="foo bl-c-blue">x</span>');
    });

    // ── no-op cases ────────────────────────────────────────────────────────
    it('leaves tags without style attribute untouched', () => {
        expect(run('<p>hello <strong>world</strong></p>'))
            .toBe('<p>hello <strong>world</strong></p>');
    });
    it('handles null/empty input', () => {
        expect(pipe.transform(null, 'J')).toBe('');
        expect(pipe.transform('', 'J')).toBe('');
    });

    // ── preserves existing URL translation behavior ────────────────────────
    it('still rewrites ClubRep registration links', () => {
        const out = run('<a href="/foo/StartARegistration?bClubRep=true">Register</a>');
        expect(out).toContain('/JOB123/registration/team');
    });
});
