import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  OnDestroy,
  signal
} from '@angular/core';
@Component({
  selector: 'app-tsic-landing-v1',
  standalone: true,
  templateUrl: './tsic-landing-v1.component.html',
  styleUrl: './tsic-landing-v1.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TsicLandingV1Component implements OnDestroy {
  private readonly elRef = inject(ElementRef);
  private observer: IntersectionObserver | null = null;
  private navObserver: IntersectionObserver | null = null;
  private statsAnimated = false;

  readonly navSolid = signal(false);

  readonly services = [
    { title: 'Clubs', description: 'Registration, payments, rosters, and reporting — everything your rec or travel club needs.', image: 'images/svc-clubs.jpg' },
    { title: 'Camps', description: 'Scheduling, roommate rostering, and skills tracking for camps of every size.', image: 'images/svc-camps.jpg' },
    { title: 'Leagues', description: 'Standings, schedules, and championship brackets — fully automated.', image: 'images/svc-leagues.jpg' },
    { title: 'Tournaments', description: 'Bracket management, team registration, and recruiting tools for any event.', image: 'images/svc-tournaments.jpg' }
  ];

  readonly pillars = [
    { icon: 'bi-lightning-charge-fill', title: 'Built for Speed', description: 'Set up registration, scheduling, and reporting in minutes — not days.' },
    { icon: 'bi-phone-fill', title: 'Mobile-First Comms', description: 'Targeted text and email blasts keep everyone connected — anytime, anywhere.' },
    { icon: 'bi-people-fill', title: 'Real Human Support', description: 'Not a chatbot. Our team works alongside yours to grow your organization.' }
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

  scrollToTop(event: Event): void {
    event.preventDefault();
    window.scrollTo({ top: 0, behavior: 'smooth' });
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

    // Nav background — solid when hero scrolls past the top 80px
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
