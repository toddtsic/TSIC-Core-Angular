import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  OnDestroy,
  OnInit,
  signal
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { LastLocationService } from '../../../infrastructure/services/last-location.service';
import { PaletteService } from '../../../infrastructure/services/palette.service';

@Component({
  selector: 'app-tsic-landing',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './tsic-landing.component.html',
  styleUrl: './tsic-landing.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TsicLandingComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly lastLocation = inject(LastLocationService);
  private readonly elRef = inject(ElementRef);
  readonly paletteService = inject(PaletteService);
  private static hasInitialized = false;
  private observer: IntersectionObserver | null = null;
  private navObserver: IntersectionObserver | null = null;
  private statsAnimated = false;

  readonly navSolid = signal(false);
  readonly activeFeature = signal(0);
  readonly currentYear = new Date().getFullYear();

  readonly aiFeatures = [
    {
      icon: 'bi-calendar2-check-fill',
      title: 'Smart Scheduling',
      description: 'Resolve field conflicts and balance team schedules automatically. Our AI engine handles round-robin, pool play, and bracket generation — what used to take days now takes seconds.'
    },
    {
      icon: 'bi-people-fill',
      title: 'Intelligent Rostering',
      description: 'AI-assisted player placement based on age, skill, and availability. Automatically balance teams, flag registration issues, and manage waitlists without spreadsheets.'
    },
    {
      icon: 'bi-send-fill',
      title: 'Automated Communications',
      description: 'Targeted text and email blasts triggered by events — not manual effort. Schedule reminders, weather alerts, and payment notices that reach the right people at the right time.'
    },
    {
      icon: 'bi-graph-up-arrow',
      title: 'Predictive Analytics',
      description: 'Enrollment trends, revenue forecasts, and participation insights at a glance. See where your organization is headed and make data-driven decisions before the season starts.'
    }
  ];

  readonly services = [
    { icon: 'bi-shield-fill', title: 'Clubs', description: 'Registration, payments, rosters, and reporting — everything your rec or travel club needs.', image: 'images/svc-clubs.jpg' },
    { icon: 'bi-sun-fill', title: 'Camps', description: 'Scheduling, roommate rostering, and skills tracking for camps of every size.', image: 'images/svc-camps.jpg' },
    { icon: 'bi-trophy-fill', title: 'Leagues', description: 'Standings, schedules, and championship brackets — fully automated.', image: 'images/svc-leagues.jpg' },
    { icon: 'bi-flag-fill', title: 'Tournaments', description: 'Bracket management, team registration, and recruiting tools for any event.', image: 'images/svc-tournaments.jpg' }
  ];

  readonly howItWorks = [
    { icon: 'bi-person-plus-fill', title: 'Sign Up', description: 'Tell us about your organization and your season goals.' },
    { icon: 'bi-gear-fill', title: 'Configure', description: 'We set up your season together — registration, fields, divisions, and fees.' },
    { icon: 'bi-rocket-takeoff-fill', title: 'Launch', description: 'Go live and start registering. We\'re with you every step of the way.' }
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
    afterNextRender(() => this.initScrollAnimations());
  }

  ngOnInit(): void {
    if (!TsicLandingComponent.hasInitialized) {
      TsicLandingComponent.hasInitialized = true;
      const lastJob = this.lastLocation.getLastJobPath();
      if (lastJob) {
        this.router.navigate([`/${lastJob}`]);
      }
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

  selectFeature(index: number): void {
    this.activeFeature.set(index);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.observer = null;
    this.navObserver?.disconnect();
    this.navObserver = null;
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
