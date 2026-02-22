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
import { PaletteService } from '../../../infrastructure/services/palette.service';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [RouterLink],
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
    { icon: 'bi-people-fill', title: 'Real Human Support', description: 'Not a chatbot. Our team works alongside yours to grow your organization.' }
  ];

  readonly aiFeatures = [
    {
      icon: 'bi-people-fill',
      title: 'Simple Registration Tools',
      description: 'Custom online registration forms for players, teams, and families. Collect the information you need, manage waitlists, and give participants a smooth sign-up experience from any device.'
    },
    {
      icon: 'bi-credit-card-fill',
      title: 'Reliable Payment Systems',
      description: 'Secure online payment processing with support for installment plans, discount codes, and automated invoicing. Track balances, send reminders, and reconcile accounts with ease.'
    },
    {
      icon: 'bi-calendar2-check-fill',
      title: 'Smart Scheduling',
      description: 'Resolve field conflicts and balance team schedules automatically. Our engine handles round-robin, pool play, and bracket generation \u2014 what used to take days now takes seconds.'
    },
    {
      icon: 'bi-phone-vibrate-fill',
      title: 'Automated Communications',
      description: 'Targeted text and email blasts triggered by events \u2014 not manual effort. Schedule reminders, weather alerts, and payment notices that reach the right people at the right time.'
    }
  ];

  readonly services = [
    { icon: 'bi-trophy-fill', title: 'Tournaments', description: 'Bracket management, team registration, and recruiting tools for any event.', image: 'images/svc-camps.jpg' },
    { icon: 'bi-flag-fill', title: 'Leagues', description: 'Standings, schedules, and championship brackets \u2014 fully automated.', image: 'images/svc-basketball.jpg' },
    { icon: 'bi-shield-fill', title: 'Clubs', description: 'Registration, payments, rosters, and reporting \u2014 everything your rec or travel club needs.', image: 'images/svc-softball.jpg' },
    { icon: 'bi-sun-fill', title: 'Camps & Clinics', description: 'Scheduling, roommate rostering, and skills tracking for camps of every size.', image: 'images/svc-soccer.jpg' }
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
    { title: 'Configure', description: 'We set up your season together \u2014 registration, fields, divisions, and fees.' },
    { title: 'Review', description: 'We walk you through everything before going live \u2014 your approval, your confidence.' },
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
    },
    {
      quote: 'We believe every athlete \u2014 youth or adult \u2014 deserves an organization that runs smoothly, communicates clearly, and puts the game first. That\'s why we built TeamSportsInfo: to give organizers the tools so every participant stays connected on and off the field.',
      name: 'The TeamSportsInfo Team',
      title: 'est. 2000',
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
      this.initScrollAnimations();
      this.startTestimonialRotation();
    });
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
