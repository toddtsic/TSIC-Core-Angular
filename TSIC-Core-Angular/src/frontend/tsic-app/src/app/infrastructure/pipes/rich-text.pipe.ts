import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { sanitizeRichText } from '@infrastructure/utils/rich-text-sanitizer';

/**
 * Renders director-authored rich text with its formatting intact.
 *
 *     <div class="bulletin-body" [innerHTML]="bulletin.text | richText"></div>
 *
 * Use this on **every** surface that renders RTE-authored HTML. A plain `[innerHTML]`
 * binding runs Angular's built-in sanitizer, which has no `style` in its attribute
 * allowlist and therefore deletes the font size, text colour and highlight colour that
 * the editor writes as inline styles. The content looks correct to the author and
 * arrives unformatted for the reader.
 *
 * The two halves are deliberately welded together in one pipe rather than exposed
 * separately: `bypassSecurityTrustHtml` is only ever safe on the output of
 * `sanitizeRichText`, and a codebase that offers the bypass on its own eventually gets
 * one call site that skips the sanitizer.
 *
 * Pure by default, so the sanitize pass is memoized per input string and re-runs only
 * when the underlying HTML actually changes — not on every change-detection cycle.
 *
 * Not for app-authored strings (confirm-dialog copy, validation messages). Those are
 * ours, contain no author input, and want Angular's default handling.
 */
@Pipe({
  name: 'richText',
  standalone: true,
})
export class RichTextPipe implements PipeTransform {
  private readonly sanitizer = inject(DomSanitizer);

  transform(html: string | null | undefined): SafeHtml {
    return this.sanitizer.bypassSecurityTrustHtml(sanitizeRichText(html));
  }
}
