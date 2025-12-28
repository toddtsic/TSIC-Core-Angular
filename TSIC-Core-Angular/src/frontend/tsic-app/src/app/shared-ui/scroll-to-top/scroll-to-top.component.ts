import { Component, HostListener, signal } from '@angular/core';
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
    styleUrls: ['./scroll-to-top.component.scss']
})
export class ScrollToTopComponent {
    showScrollTop = signal(false);

    @HostListener('window:scroll', [])
    onWindowScroll() {
        const scrollPosition = window.scrollY || document.documentElement.scrollTop;
        const windowHeight = window.innerHeight;
        this.showScrollTop.set(scrollPosition > windowHeight);
    }

    scrollToTop() {
        window.scrollTo({
            top: 0,
            behavior: 'smooth'
        });
    }
}