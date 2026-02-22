import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  OnDestroy,
  signal
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { PaletteService } from '../../../infrastructure/services/palette.service';

@Component({
  selector: 'app-tsic-landing-v3',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './tsic-landing-v3.component.html',
  styleUrl: './tsic-landing-v3.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TsicLandingV3Component implements OnDestroy {
  private readonly elRef = inject(ElementRef);
  readonly paletteService = inject(PaletteService);
  private observer: IntersectionObserver | null = null;
  private navObserver: IntersectionObserver | null = null;
  private testimonialInterval: ReturnType<typeof setInterval> | null = null;
  private statsAnimated = false;

  readonly navSolid = signal(false);
  readonly activeTestimonial = signal(0);
  readonly currentYear = new Date().getFullYear();

  readonly capabilities = [
    {
      icon: 'bi-calendar2-check-fill',
      title: 'Smart Scheduling',
      description: 'Resolve field conflicts and balance team schedules automatically. Our AI engine handles round-robin, pool play, and bracket generation \u2014 what used to take days now takes seconds.',
      size: 'hero' as const
    },
    {
      icon: 'bi-people-fill',
      title: 'Intelligent Rostering',
      description: 'AI-assisted player placement based on age, skill, and availability. Balance teams and manage waitlists without spreadsheets.',
      size: 'standard' as const
    },
    {
      icon: 'bi-send-fill',
      title: 'Automated Comms',
      description: 'Targeted text and email blasts triggered by events \u2014 not manual effort. The right message, to the right people, at the right time.',
      size: 'standard' as const
    },
    {
      icon: 'bi-graph-up-arrow',
      title: 'Predictive Analytics',
      description: 'Enrollment trends, revenue forecasts, and participation insights at a glance. Data-driven decisions before the season starts.',
      size: 'standard' as const
    }
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
    { title: 'Sign Up', description: 'Tell us about your organization and your season goals.' },
    { title: 'Configure', description: 'We set up your season together \u2014 registration, fields, divisions, and fees.' },
    { title: 'Launch', description: 'Go live and start registering. We\'re with you every step of the way.' }
  ];

  readonly testimonials = [
    {
      quote: 'TeamSportsInfo transformed how we run our club. Registration that used to take weeks now takes a weekend.',
      name: 'Jane D.',
      title: 'Club Director',
      org: 'Eastside Youth Soccer'
    },
    {
      quote: 'The scheduling engine alone saved us 40 hours a season. And the parents love the text notifications.',
      name: 'Mike R.',
      title: 'League Commissioner',
      org: 'Metro Basketball League'
    },
    {
      quote: 'We went from spreadsheets to a fully digital operation in one season. Support was incredible throughout.',
      name: 'Sarah K.',
      title: 'Tournament Director',
      org: 'Atlantic Lacrosse Invitational'
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
    this.navObserver?.disconnect();
    this.navObserver = null;
    if (this.testimonialInterval) {
      clearInterval(this.testimonialInterval);
      this.testimonialInterval = null;
    }
  }

  private startTestimonialRotation(): void {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    this.testimonialInterval = setInterval(() => {
      const next = (this.activeTestimonial() + 1) % this.testimonials.length;
      this.activeTestimonial.set(next);
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
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

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

    const hero = this.elRef.nativeElement.querySelector('.hero');
    if (hero) {
      this.navObserver = new IntersectionObserver(
        ([entry]) => this.navSolid.set(!entry.isIntersecting),
        { root: null, threshold: 0, rootMargin: '-80px 0px 0px 0px' }
      );
      this.navObserver.observe(hero);
    }
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
