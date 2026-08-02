/**
 * Shared decoder for job banner overlay text (JobDisplayOptions.parallaxSlide1Text1/2).
 *
 * Stored values are inconsistent by era: legacy rows hold HTML-encoded rich text with inline
 * <span>/<i> markup, while anything saved through Configure -> Job -> Branding is plain text
 * with <br> line joins (JobConfigService.NewlineToBr). Both have to render as clean lines.
 *
 * Used by the public client-banner and by the job-clone workbench's banner preview, so the
 * preview shows exactly what the released home page will show.
 */
export function decodeOverlayText(text?: string | null): string {
    if (!text) return '';

    // Decode HTML entities (legacy data is HTML-encoded in the DB)
    const textarea = document.createElement('textarea');
    textarea.innerHTML = text;
    let clean = textarea.value;

    // Normalize <br> variants to newlines, then strip surviving legacy tags
    clean = clean.replaceAll(/<br\s*\/?>/gi, '\n');
    clean = clean.replaceAll(/<[^>]+>/g, '');
    clean = clean.replaceAll(/\u00A0/g, ' ');

    // Trim lines and drop ALL blanks — including the bare \r that `<br />\r\n` leaves behind
    const lines = clean.split('\n')
        .map(l => l.trim())
        .filter(l => l.length > 0);

    return lines.join('<br>');
}

/**
 * Plain text (newline-separated, as the API's OverlayText.ToPlainText returns and as the
 * textareas hold it) -> the <br>-joined form used for [innerHTML] rendering.
 *
 * Escapes first: this renders author-typed text, and the stored form is markup. Without the
 * escape, typing a tag into the banner headline would inject it into the preview.
 */
export function plainTextToHtml(text?: string | null): string {
    if (!text) return '';

    const escaped = text
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');

    return escaped.split('\n')
        .map(l => l.trim())
        .filter(l => l.length > 0)
        .join('<br>');
}
