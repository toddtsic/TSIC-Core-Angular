/**
 * Scroll the registration wizard back to the top.
 *
 * When the VerticalInsure embedded-offer iframe finishes mounting it autofocuses
 * an element inside itself, which makes the browser scroll the nearest scrollable
 * ancestor — the client-layout `<main>` (see layout.component.scss, `overflow-y: auto`)
 * — down to the widget. On first arrival at the Payment step that leaves the user
 * partway down the page, having to scroll back up. Call this once the widget reports
 * ready to undo that jump.
 *
 * Mirrors the scroll-container lookup used by ScrollToTopComponent (`document.querySelector('main')`).
 */
export function scrollWizardToTop(): void {
    const main = document.querySelector('main');
    if (main) {
        main.scrollTo({ top: 0, behavior: 'auto' });
    } else {
        globalThis.window.scrollTo({ top: 0, behavior: 'auto' });
    }
    // Drop focus off the embed iframe so it can't re-anchor the scroll position.
    const active = document.activeElement as HTMLElement | null;
    if (active?.tagName === 'IFRAME') active.blur();
}
