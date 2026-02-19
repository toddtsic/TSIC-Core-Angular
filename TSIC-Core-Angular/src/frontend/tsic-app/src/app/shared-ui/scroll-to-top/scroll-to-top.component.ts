import { ChangeDetectionStrategy, Component, HostListener, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-scroll-to-top',
    standalone: true,
    imports: [CommonModule],
    template: `
    @if (showScrollTop()) {
      <button 
        class="scroll-to-top-btn"
        (click)="scrollToTop()"
        aria-label="Scroll to top"
        title="Back to top">
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M12 4L12 20M12 4L18 10M12 4L6 10" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </button>
    }
  `,
    styleUrls: ['./scroll-to-top.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScrollToTopComponent {
    showScrollTop = signal(false);

    private get scrollContainer(): Element | null {
        return document.querySelector('main');
    }

    @HostListener('window:scroll', [])
    onWindowScroll() {
        // Check both window and main scroll container
        const main = this.scrollContainer;
        const scrollPosition = main
            ? main.scrollTop
            : (window.scrollY || document.documentElement.scrollTop);
        const windowHeight = window.innerHeight;
        this.showScrollTop.set(scrollPosition > windowHeight);
    }

    ngAfterViewInit() {
        // Listen to main's scroll since it's the actual scroll container
        this.scrollContainer?.addEventListener('scroll', () => this.onWindowScroll());
    }

    scrollToTop() {
        const main = this.scrollContainer;
        if (main) {
            main.scrollTo({ top: 0, behavior: 'smooth' });
        } else {
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
    }
}