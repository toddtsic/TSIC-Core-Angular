import {
  afterNextRender,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  inject,
  OnDestroy,
  signal
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { PalettePickerComponent } from '../../../layouts/components/palette-picker/palette-picker.component';
import { PaletteService } from '../../../infrastructure/services/palette.service';
import { ScrollToTopComponent } from '../../../shared-ui/scroll-to-top/scroll-to-top.component';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [RouterLink, PalettePickerComponent, ScrollToTopComponent],
  templateUrl: './tsic-landing.component.html',
  styleUrl: './tsic-landing.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TsicLandingComponent implements OnDestroy {
  private readonly elRef = inject(ElementRef);
  private readonly cdRef = inject(ChangeDetectorRef);
  readonly paletteService = inject(PaletteService);
  private observer: IntersectionObserver | null = null;
  private navObserver: IntersectionObserver | null = null;
  private previewedPaletteIndex = -1;
  private readonly navScrollHandler = () => {
    const solid = window.scrollY > 10;
    if (solid !== this.navSolid()) {
      this.navSolid.set(solid);
      this.cdRef.markForCheck();
    }
  };
  private testimonialInterval: ReturnType<typeof setInterval> | null = null;
  private statsAnimated = false;

  readonly navSolid = signal(false);
  readonly activeTestimonial = signal(0);
  readonly currentYear = new Date().getFullYear();

  readonly pillars = [
    { icon: 'bi-lightning-charge-fill', title: 'Built for Speed', description: 'Set up registration, scheduling, and reporting in minutes — not days.' },
    { icon: 'bi-phone-fill', title: 'Mobile-First Comms', description: 'Targeted text and email blasts keep everyone connected — anytime, anywhere.' },
    { icon: 'bi-people-fill', title: 'Real Human Support', description: 'Our team works alongside yours to grow your organization.' },
    { icon: 'bi-tag-fill', title: 'Predictable Pricing', description: 'True fixed-rate pricing \u2014 not percentage-based fees.' }
  ];

  readonly aiFeatures = [
    {
      icon: 'bi-people-fill',
      title: 'Simple Registration Tools',
      description: 'Custom registration forms for players, coaches and teams, with family and club rep information brought forward to streamline the process. Multi-select options per player available for camps & clinics. Collect the information you need, manage waitlists, and give participants a smooth sign-up experience from any device.'
    },
    {
      icon: 'bi-credit-card-fill',
      title: 'Reliable Payment Systems',
      description: 'Secure online payment processing with flexible payment configurations. Support for installment plans, discount codes, flexible payment methods and registration protection insurance. Track balances, send reminders, and reconcile accounts with ease.'
    },
    {
      icon: 'bi-calendar2-check-fill',
      title: 'Smart Scheduling',
      description: 'Resolve field conflicts and balance team schedules automatically. With the rescheduler, directors can make changes or ask for support, invaluable during rain delays or cancellations. Our engine handles round-robin, pool play, and bracket generation \u2014 what used to take days now takes seconds.'
    },
    {
      icon: 'bi-phone-vibrate-fill',
      title: 'Automated Communications',
      description: 'Targeted text and email blasts with filters for all registrants. Mobile app has automated rostering for any TeamSportsInfo club team. Push notifications for game results, schedule reminders, weather alerts that reach the right people at the right time.'
    },
    {
      icon: 'bi-clipboard-data-fill',
      title: 'Comprehensive Reports',
      description: 'Real-time exportable reports and dashboards covering registration data, payments, rosters, check-in forms, uniform numbers, college recruiting profiles, field utilization, game score boards and more. Track balances, per job accounting and season-end summaries — giving directors the data they need to plan smarter and communicate with confidence.'
    }
  ];

  readonly services = [
    { icon: 'bi-trophy-fill', title: 'Tournaments', description: 'Intuitive registration, scheduling & recruiting tools with push notifications for any event.', image: 'images/svc-girls-lax.jpg' },
    { icon: 'bi-flag-fill', title: 'Leagues', description: 'Standings, schedules, and championship brackets \u2014 fully automated.', image: 'images/svc-basketball.jpg' },
    { icon: 'bi-shield-fill', title: 'Clubs', description: 'Registration, payments, rosters, and reporting \u2014 everything your rec or travel club needs.', image: 'images/svc-softball.jpg' },
    { icon: 'bi-sun-fill', title: 'Camps & Clinics', description: 'Multi-select registration, rostering and check-in reports for camps & clinics of every size.', image: 'images/svc-soccer.jpg' }
  ];

  readonly serviceRows = [
    {
      image: 'images/svc-clubs.jpg',
      imageAlt: 'Youth lacrosse club in action',
      items: [
        { icon: 'bi-shield-fill', title: 'Clubs', description: 'Registration, payments, rosters, and reporting \u2014 everything your rec or travel club needs.' },
        { icon: 'bi-sun-fill', title: 'Camps', description: 'Scheduling, roommate rostering, and skills tracking for camps of every size.' }
      ]
    },
    {
      image: 'images/svc-tournaments.jpg',
      imageAlt: 'Tournament lacrosse game',
      reverse: true,
      items: [
        { icon: 'bi-trophy-fill', title: 'Leagues', description: 'Standings, schedules, and championship brackets \u2014 fully automated.' },
        { icon: 'bi-flag-fill', title: 'Tournaments', description: 'Bracket management, team registration, and recruiting tools for any event.' }
      ]
    }
  ];

  readonly howItWorks = [
    { title: 'Contact Us', description: 'Tell us about your organization and your season goals.' },
    { title: 'Configure', description: 'We set up your season together \u2014 registration, fees, agegroups and divisions.' },
    { title: 'Review', description: 'We walk you through everything \u2014 your approval, your confidence.' },
    { title: 'Launch', description: 'Go live and start registering. We\'re with you every step of the way.' }
  ];

  readonly testimonials = [
    {
      quote: 'After running tournaments with other software vendors we switched to TeamSportsInfo and have had a great experience. They always pick up the phone and will take whatever time it takes to answer your questions.',
      name: '',
      title: 'Tournament Director',
      org: ''
    },
    {
      quote: 'This new version of the TSI-Events app is excellent. Fast and easy to navigate. Love the filters and immediate push notifications of results across events.',
      name: '',
      title: 'Parent',
      org: ''
    }
  ];

  readonly animatedStats = signal([
    { target: 1000, suffix: '+', current: 0, label: 'Teams Managed' },
    { target: 50, suffix: '+', current: 0, label: 'Organizations' },
    { target: 20, suffix: '+', current: 0, label: 'Years of Service' },
    { target: 24, suffix: '/7', current: 0, label: 'Support' }
  ]);

  constructor() {
    afterNextRender(() => {
      this.previewedPaletteIndex = 0;
      this.paletteService.selectPalette(4);
      this.initScrollAnimations();
      this.startTestimonialRotation();
      this.loadCalendlyWidget();
    });
  }

  onPaletteSelected(): void {
    // If the user unchecked a palette (reset to 0), snap back to Forest Green
    // as the default for this page rather than going colorless.
    if (this.paletteService.selectedIndex() === 0) {
      this.paletteService.selectPalette(4);
    }
  }

  scrollToTop(event: Event): void {
    event.preventDefault();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  scrollTo(event: Event, selector: string): void {
    event.preventDefault();
    const el = this.elRef.nativeElement.querySelector(selector);
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  selectTestimonial(index: number): void {
    this.activeTestimonial.set(index);
    this.restartTestimonialRotation();
  }

  ngOnDestroy(): void {
    if (this.previewedPaletteIndex !== -1) {
      this.paletteService.selectPalette(this.previewedPaletteIndex);
      this.previewedPaletteIndex = -1;
    }
    this.observer?.disconnect();
    this.observer = null;
    window.removeEventListener('scroll', this.navScrollHandler);
    this.navObserver?.disconnect();
    this.navObserver = null;
    if (this.testimonialInterval) {
      clearInterval(this.testimonialInterval);
      this.testimonialInterval = null;
    }
  }

  private startTestimonialRotation(): void {
    if (globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
    this.testimonialInterval = setInterval(() => {
      const next = (this.activeTestimonial() + 1) % this.testimonials.length;
      this.activeTestimonial.set(next);
      this.cdRef.markForCheck();
    }, 6000);
  }

  private restartTestimonialRotation(): void {
    if (this.testimonialInterval) {
      clearInterval(this.testimonialInterval);
      this.testimonialInterval = null;
    }
    this.startTestimonialRotation();
  }

  private loadCalendlyWidget(): void {
    const id = 'calendly-widget-script';
    if (document.getElementById(id)) return;

    const link = document.createElement('link');
    link.href = 'https://assets.calendly.com/assets/external/widget.css';
    link.rel = 'stylesheet';
    document.head.appendChild(link);

    const script = document.createElement('script');
    script.id = id;
    script.src = 'https://assets.calendly.com/assets/external/widget.js';
    script.async = true;
    document.head.appendChild(script);
  }

  private initScrollAnimations(): void {
    const prefersReducedMotion = globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

    if (prefersReducedMotion) {
      this.elRef.nativeElement.querySelectorAll('.reveal')
        .forEach((el: Element) => el.classList.add('revealed'));
      this.snapStats();
      return;
    }

    this.observer = new IntersectionObserver(
      (entries) => {
        entries.forEach(entry => {
          if (entry.isIntersecting) {
            entry.target.classList.add('revealed');
            if (entry.target.classList.contains('stats-trigger') && !this.statsAnimated) {
              this.statsAnimated = true;
              this.animateCounters();
            }
            this.observer?.unobserve(entry.target);
          }
        });
      },
      { root: null, rootMargin: '0px 0px -40px 0px', threshold: 0.15 }
    );

    this.elRef.nativeElement.querySelectorAll('.reveal')
      .forEach((el: Element) => this.observer!.observe(el));

    window.addEventListener('scroll', this.navScrollHandler, { passive: true });
  }

  private animateCounters(): void {
    const duration = 1800;
    const fps = 30;
    const total = Math.round(duration / (1000 / fps));
    let frame = 0;

    const id = setInterval(() => {
      frame++;
      const eased = 1 - Math.pow(1 - frame / total, 3);
      this.animatedStats.update(s => s.map(x => ({ ...x, current: Math.round(x.target * eased) })));
      if (frame >= total) { clearInterval(id); this.snapStats(); }
    }, 1000 / fps);
  }

  private snapStats(): void {
    this.animatedStats.update(s => s.map(x => ({ ...x, current: x.target })));
  }
}
