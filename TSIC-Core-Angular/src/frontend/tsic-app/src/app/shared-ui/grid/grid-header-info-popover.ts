/**
 * Hover/focus popover for an info "i" rendered inside a Syncfusion column HEADER.
 *
 * Why this is imperative rather than a template tooltip: Syncfusion renders header
 * templates as static HTML, so Angular `(mouseenter)`/`(click)` bindings never fire
 * there, and the header clips with `overflow: hidden` while scrolling horizontally —
 * so an in-header `position: absolute` panel is invisible. We attach NATIVE listeners
 * (which do fire from component code) to the rendered icon and show a `position: fixed`
 * panel mounted on `<body>` that escapes the clip. The panel reuses the global
 * `.hover-popover-panel` styles so it matches the ledger card exactly.
 *
 * Shared by the club-rep teams grid and the family players grid: the popover is the
 * same affordance on both, and duplicating it is how the two would drift.
 *
 * Usage: construct one per grid component, call {@link wire} from the grid's
 * `dataBound`, and {@link destroy} from `DestroyRef.onDestroy`.
 */
export class GridHeaderInfoPopover {
    private panel: HTMLElement | null = null;
    private reposition: (() => void) | null = null;

    /**
     * @param iconSelector CSS selector for the header icon, e.g. `.fee-adj-info`.
     * @param title Popover heading. Body text is read off the icon's `data-help`.
     */
    constructor(
        private readonly iconSelector: string,
        private readonly title: string,
    ) { }

    /**
     * Bind the rendered icon. Idempotent: the icon element is recreated whenever the
     * header rebuilds (refreshColumns), so callers re-wire on every dataBound and the
     * dataset flag makes repeat calls free.
     */
    wire(host: Element | null | undefined): void {
        const icon = host?.querySelector<HTMLElement>(this.iconSelector);
        if (!icon || icon.dataset['popoverWired'] === '1') return;
        icon.dataset['popoverWired'] = '1';

        const place = () => {
            const panel = this.panel;
            if (!panel) return;
            const r = icon.getBoundingClientRect();
            panel.style.top = `${r.bottom + 6}px`;
            const left = Math.min(r.left, window.innerWidth - panel.offsetWidth - 8);
            panel.style.left = `${Math.max(8, left)}px`;
        };

        const show = () => {
            const panel = this.ensurePanel();
            const text = panel.querySelector('.hover-popover-text');
            if (text) text.textContent = icon.dataset['help'] ?? '';
            panel.style.display = 'block';
            place();
            this.reposition = place;
            window.addEventListener('scroll', place, true);
            window.addEventListener('resize', place);
        };

        const hide = () => this.hide();

        icon.addEventListener('mouseenter', show);
        icon.addEventListener('mouseleave', hide);
        icon.addEventListener('focus', show);
        icon.addEventListener('blur', hide);
    }

    private hide(): void {
        if (this.panel) this.panel.style.display = 'none';
        if (this.reposition) {
            window.removeEventListener('scroll', this.reposition, true);
            window.removeEventListener('resize', this.reposition);
            this.reposition = null;
        }
    }

    private ensurePanel(): HTMLElement {
        if (this.panel) return this.panel;
        const el = document.createElement('div');
        el.className = 'hover-popover-panel';
        el.style.position = 'fixed';
        el.style.right = 'auto';
        el.style.zIndex = '2000';
        el.style.display = 'none';
        const header = document.createElement('div');
        header.className = 'hover-popover-header';
        const titleEl = document.createElement('span');
        titleEl.className = 'hover-popover-title';
        titleEl.textContent = this.title;
        header.appendChild(titleEl);
        const body = document.createElement('div');
        body.className = 'hover-popover-body';
        const text = document.createElement('span');
        text.className = 'hover-popover-text';
        body.appendChild(text);
        el.appendChild(header);
        el.appendChild(body);
        document.body.appendChild(el);
        this.panel = el;
        return el;
    }

    destroy(): void {
        this.hide();
        this.panel?.remove();
        this.panel = null;
    }
}
