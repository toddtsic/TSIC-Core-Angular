import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto, RegisterTeamResponse } from '@core/api';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';
import { LOP_CHOICES, formatLop, normalizeLop } from '@shared/teams/lop-choices';
import { clubNameInTeamName } from '@shared/teams/team-name-hints';
import { libraryGradYearOptions } from '@shared/teams/library-team-form';
import { ageGroupLabel, isWaitlistAgeGroup } from './library-segment.types';
import { ageGroupWaitlists, byGradYearThenName } from './library-register-plan';

/** What the step toasts once its reload lands. */
export interface TeamAddedEvent { message: string; tone: 'success' | 'warning'; }

let nextId = 0;

const norm = (s: string | null | undefined): string => (s ?? '').trim().toLowerCase();

/**
 * The age group a team NAME names, or '' (Todd 2026-09-28: "preselect if you can from team name,
 * otherwise must be selected"). An age group matches when its name appears in the team name as a
 * whole token ("2029 Blue" → 2029, never 20291). Several matches resolve only when the longest
 * contains every other ("2029/2030 Blue" → 2029/2030); anything else is ambiguous → ''. A full
 * match lands on its WAITLIST twin, as the library's register editor does.
 */
export function ageGroupFromTeamName(ageGroups: readonly AgeGroupDto[], teamName: string): string {
    const name = teamName.trim();
    if (!name) return '';
    const hits = ageGroups.filter(ag => {
        if (isWaitlistAgeGroup(ag)) return false;
        const label = ag.ageGroupName.trim();
        if (!label) return false;
        const escaped = label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        return new RegExp(`(^|[^A-Za-z0-9])${escaped}($|[^A-Za-z0-9])`, 'i').test(name);
    });
    if (hits.length === 0) return '';
    const longest = hits.reduce((a, b) => (b.ageGroupName.length > a.ageGroupName.length ? b : a));
    if (hits.some(h => h !== longest && !norm(longest.ageGroupName).includes(norm(h.ageGroupName)))) return '';
    if (longest.registeredCount >= longest.maxTeams) {
        const twin = ageGroups.find(a => norm(a.ageGroupName) === norm(`WAITLIST - ${longest.ageGroupName}`));
        if (twin) return twin.ageGroupId;
    }
    return longest.ageGroupId;
}

/**
 * The ONE way a team gets onto this event (Todd 2026-09-28): a compound input on top of Registered
 * Teams. The team name is a combobox over the club's library:
 *
 *   pick a library team  → its grad year and level fill in; register it.
 *   type a new name      → grad year and level are the rep's to fill; the team is ADDED to the
 *                          library and registered, one press.
 *
 * Typing a library team's exact name (any case/spacing) IS picking it, so a near-duplicate never
 * mints a second library row. The age group preselects from the name, else must be chosen.
 * Enter adds; the row clears and the cursor is back in the name for the next team.
 *
 * Owns its own writes, like the row editors; the step reloads on `added` / `libraryChanged`.
 */
@Component({
    selector: 'app-team-add-row',
    standalone: true,
    template: `
    <div class="add" role="group" aria-label="Add a team to this event" (keydown.escape)="closeList()">
      <div class="add-fields">
        <!-- Team: the combobox -->
        <div class="f f--team">
          <label class="f-label" [attr.for]="inputId">Team</label>
          <div class="combo">
            <input class="f-input f-name" type="text" autocomplete="off" spellcheck="false"
                   [id]="inputId"
                   role="combobox"
                   aria-autocomplete="list"
                   [attr.aria-expanded]="listOpen()"
                   [attr.aria-controls]="listId"
                   [attr.aria-activedescendant]="listOpen() && activeIndex() >= 0 ? optionId(activeIndex()) : null"
                   [placeholder]="available().length ? 'Pick from your library or type a new team' : 'Type your team name'"
                   [disabled]="busy()"
                   [value]="text()"
                   [class.is-invalid]="!!nameProblem()"
                   (input)="onType($any($event.target).value)"
                   (focus)="openList()"
                   (click)="openList()"
                   (blur)="closeList()"
                   (keydown)="onNameKey($event)" />
            <i class="bi combo-caret" [class.bi-chevron-down]="!listOpen()" [class.bi-chevron-up]="listOpen()" aria-hidden="true"></i>
            @if (listOpen() && options().length > 0) {
              <!-- mousedown kept off the input's blur, so a click lands on the option. -->
              <ul class="combo-list" role="listbox" [id]="listId" aria-label="Club Team Library"
                  (mousedown)="$event.preventDefault()">
                @for (t of options(); track t.clubTeamId; let i = $index) {
                  <li class="combo-opt" role="option" [id]="optionId(i)"
                      [class.is-active]="i === activeIndex()"
                      [attr.aria-selected]="i === activeIndex()"
                      (mouseenter)="activeIndex.set(i)"
                      (click)="pick(t)">
                    <span class="opt-name">{{ t.clubTeamName }}</span>
                    <span class="opt-meta">
                      <span class="meta-pair"><span class="meta-key">Grad</span>{{ t.clubTeamGradYear || '—' }}</span>
                      <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(t.clubTeamLevelOfPlay) || '—' }}</span>
                    </span>
                  </li>
                }
              </ul>
            }
          </div>
        </div>

        <!-- Grad year: the library's when picked (read-only — it is the library team's), else typed in. -->
        <label class="f f--grad">
          <span class="f-label">Grad</span>
          <select class="f-input" [disabled]="busy() || !!picked()"
                  [class.is-blank]="!picked() && !gradYear()"
                  (change)="gradYear.set($any($event.target).value)">
            @if (picked(); as p) {
              <option selected>{{ p.clubTeamGradYear || '—' }}</option>
            } @else {
              <option value="" [selected]="!gradYear()">Pick…</option>
              @for (yr of gradYearOptions; track yr) {
                <option [value]="yr" [selected]="yr === gradYear()">{{ yr }}</option>
              }
            }
          </select>
        </label>

        <!-- Level: this event's. Seeded from the library team, the rep may change it. -->
        <label class="f f--lop">
          <span class="f-label">LOP</span>
          <select class="f-input" [disabled]="busy()" [class.is-blank]="!lop()"
                  (change)="lopPick.set($any($event.target).value)">
            <option value="" [selected]="!lop()">Pick…</option>
            @for (c of lopChoices; track c.value) {
              <option [value]="c.value" [selected]="c.value === lop()">{{ c.label }}</option>
            }
          </select>
        </label>

        <label class="f f--ag">
          <span class="f-label">Age group</span>
          <select class="f-input" [disabled]="busy()" [class.is-blank]="!ageGroupId()"
                  (change)="onAgeGroup($any($event.target).value)">
            <option value="" [selected]="!ageGroupId()">Pick…</option>
            @for (o of ageGroupOptions(); track o.id) {
              <option [value]="o.id" [selected]="o.id === ageGroupId()">{{ o.text }}</option>
            }
          </select>
        </label>

        <button type="button" class="btn-add-team" [class.btn-add-team--wl]="waitlists()"
                [disabled]="busy() || !canAdd()"
                [attr.title]="missing() ?? null"
                (click)="add()">
          @if (saving()) {
            <span class="spinner-border spinner-border-sm" aria-hidden="true"></span>Adding…
          } @else {
            <i class="bi" [class.bi-plus-circle-fill]="!waitlists()" [class.bi-pause-circle-fill]="waitlists()" aria-hidden="true"></i>
            {{ waitlists() ? 'Join waitlist' : 'Add' }}
          }
        </button>
      </div>

      <!-- One line under the fields: what the press will do, or what's in the way. -->
      <p class="add-note" [class.add-note--err]="!!nameProblem() || !!errorMsg()" role="status">
        @if (errorMsg()) {
          <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>{{ errorMsg() }}
        } @else if (nameProblem()) {
          <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>{{ nameProblem() }}
        } @else if (picked()) {
          <i class="bi bi-card-list" aria-hidden="true"></i>From your Club Team Library.
        } @else if (text().trim()) {
          <i class="bi bi-plus-circle" aria-hidden="true"></i>New team &mdash; it will be saved to your Club Team Library too.
        } @else if (available().length) {
          {{ available().length }} {{ available().length === 1 ? 'team' : 'teams' }} in your Club Team Library not registered yet.
        }
      </p>
    </div>
    `,
    styles: [`
      :host { display: block; }

      .add {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
        padding: var(--space-2) var(--space-3);
        border: 1px solid color-mix(in srgb, var(--bs-success) 35%, var(--bs-border-color));
        border-radius: var(--radius-md);
        background: color-mix(in srgb, var(--bs-success) 5%, var(--brand-surface));
        box-shadow: var(--shadow-xs);
      }

      /* Team | Grad | LOP | Age group | Add — wraps to Team over the rest on a narrow screen. */
      .add-fields { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-2); }

      .f { display: flex; flex-direction: column; gap: 1px; min-width: 0; margin: 0; }
      .f--team { flex: 1 1 14rem; }
      .f--grad { flex: 0 0 5.5rem; }
      .f--lop { flex: 0 0 6.5rem; }
      .f--ag { flex: 1 1 8rem; }

      .f-label {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--brand-text-muted);
      }

      .f-input {
        width: 100%;
        min-width: 0;
        height: 30px;
        padding: 3px var(--space-2);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text);
        font-size: var(--font-size-sm);

        &:focus-visible { outline: none; border-color: var(--bs-primary); box-shadow: var(--shadow-focus); }
        &:disabled { background: color-mix(in srgb, var(--bs-body-color) 4%, var(--brand-surface)); cursor: default; }
        &.is-invalid { border-color: var(--bs-danger); }
        &.is-blank { border-style: dashed; color: var(--brand-text-muted); }
      }
      select.f-input { padding: 3px var(--space-1); font-size: var(--font-size-xs); cursor: pointer; }
      .f-name { padding-right: var(--space-6); font-weight: var(--font-weight-semibold); }

      /* ── The combobox ── */
      .combo { position: relative; }
      .combo-caret {
        position: absolute;
        top: 50%;
        right: var(--space-2);
        transform: translateY(-50%);
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        pointer-events: none;
      }

      .combo-list {
        position: absolute;
        top: calc(100% + 2px);
        left: 0;
        right: 0;
        z-index: 20;
        max-height: 18rem;
        margin: 0;
        padding: var(--space-1) 0;
        overflow-y: auto;
        list-style: none;
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        box-shadow: var(--shadow-lg);
        min-width: 16rem;
      }

      .combo-opt {
        display: flex;
        align-items: baseline;
        justify-content: space-between;
        gap: var(--space-3);
        padding: var(--space-1) var(--space-3);
        cursor: pointer;

        &.is-active { background: color-mix(in srgb, var(--bs-primary) 12%, var(--brand-surface)); }
      }
      .opt-name {
        min-width: 0;
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .opt-meta {
        display: inline-flex;
        flex-shrink: 0;
        gap: var(--space-3);
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }
      .meta-pair { display: inline-flex; align-items: baseline; gap: var(--space-1); }
      .meta-key { text-transform: uppercase; letter-spacing: 0.06em; font-weight: var(--font-weight-semibold); opacity: 0.7; }

      .btn-add-team {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-1);
        height: 30px;
        padding: 0 var(--space-3);
        border: 1px solid var(--bs-success);
        border-radius: var(--radius-sm);
        background: var(--bs-success);
        color: var(--neutral-0);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;

        &:hover:not(:disabled) { filter: brightness(0.93); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.4; cursor: default; }
        &--wl { border-color: var(--bs-warning); background: var(--bs-warning); color: var(--bs-dark); }
      }

      .add-note {
        display: flex;
        align-items: baseline;
        gap: var(--space-1);
        min-height: 1.2em;
        margin: 0;
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);

        .bi { flex-shrink: 0; color: var(--bs-success); }
        &--err { color: var(--bs-danger); .bi { color: var(--bs-danger); } }
      }

      @media (max-width: 575.98px) {
        .f--team { flex-basis: 100%; }
        .f--ag { flex-basis: 100%; }
        .btn-add-team { flex: 1 1 100%; height: 36px; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamAddRowComponent {
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly destroyRef = inject(DestroyRef);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly injector = inject(Injector);

    readonly clubTeams = input.required<readonly ClubTeamDto[]>();
    readonly registeredTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly clubName = input('');
    readonly eventName = input('this event');
    /** Another write on the step is in flight. */
    readonly actionInProgress = input(false);

    /** A team was registered (or waitlisted): the step reloads, then toasts this. */
    readonly added = output<TeamAddedEvent>();
    /** The library gained a row but the registration was refused: the step reloads the list. */
    readonly libraryChanged = output<void>();

    readonly inputId = `team-add-${++nextId}`;
    readonly listId = `${this.inputId}-list`;
    optionId(i: number): string { return `${this.inputId}-opt-${i}`; }

    readonly formatLop = formatLop;
    readonly lopChoices = LOP_CHOICES;
    readonly gradYearOptions = libraryGradYearOptions();

    readonly text = signal('');
    /** Typed grad year — a new team only; a picked team's is its own. */
    readonly gradYear = signal('');
    /** The rep's level pick; '' = the picked library team's level. */
    readonly lopPick = signal('');
    /** The rep's age-group pick; null = untouched, follow the name. */
    private readonly agPick = signal<string | null>(null);
    readonly listOpen = signal(false);
    readonly activeIndex = signal(-1);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);
    /** Registered this visit, until the step's reload says so — keeps them out of the list meanwhile. */
    private readonly justAdded = signal<ReadonlySet<number>>(new Set());

    readonly busy = computed(() => this.saving() || this.actionInProgress());

    private readonly registeredClubTeamIds = computed(() => {
        const ids = new Set<number>(this.justAdded());
        for (const r of this.registeredTeams()) if (r.clubTeamId != null) ids.add(r.clubTeamId);
        return ids;
    });

    /** Active library teams not registered here, club order. */
    readonly available = computed(() => {
        const reg = this.registeredClubTeamIds();
        return this.clubTeams().filter(t => !t.bArchived && !reg.has(t.clubTeamId)).sort(byGradYearThenName);
    });

    /** The dropdown: every available team until the rep types, then the ones whose name or grad year contain it. */
    readonly options = computed(() => {
        const q = norm(this.text());
        if (!q || this.picked()) return this.available();
        return this.available().filter(t => norm(t.clubTeamName).includes(q) || norm(t.clubTeamGradYear).includes(q));
    });

    /** The library team the name IS — exact, case- and spacing-insensitive. */
    readonly picked = computed<ClubTeamDto | null>(() => {
        const n = norm(this.text());
        return n ? this.available().find(t => norm(t.clubTeamName) === n) ?? null : null;
    });

    readonly lop = computed(() => this.lopPick() || (this.picked() ? normalizeLop(this.picked()!.clubTeamLevelOfPlay) : ''));
    readonly effectiveGradYear = computed(() => this.picked()?.clubTeamGradYear ?? this.gradYear());
    readonly ageGroupId = computed(() => this.agPick() ?? ageGroupFromTeamName(this.ageGroups(), this.text()));

    readonly ageGroupOptions = computed(() => this.ageGroups().map(ag => {
        const label = ageGroupLabel(ag);
        return { id: ag.ageGroupId, text: ageGroupWaitlists(ag) ? `${label} · waitlist` : label };
    }));

    readonly waitlists = computed(() => {
        const ag = this.ageGroups().find(a => a.ageGroupId === this.ageGroupId());
        return !!ag && ageGroupWaitlists(ag);
    });

    /** Why the typed name can't be used — only for a name that is not a pickable library team. */
    readonly nameProblem = computed<string | null>(() => {
        const name = this.text().trim();
        if (!name || this.picked()) return null;
        const n = norm(name);
        if (this.registeredTeams().some(r => norm(r.teamName) === n)) return `${name} is already registered for ${this.eventName()}.`;
        const inLibrary = this.clubTeams().find(t => norm(t.clubTeamName) === n);
        if (inLibrary?.bArchived) return `${inLibrary.clubTeamName} is archived in your Club Team Library — restore it there to use it again.`;
        if (inLibrary) return `${inLibrary.clubTeamName} is already registered for ${this.eventName()}.`;
        if (clubNameInTeamName(this.clubName(), name) === 'full') {
            return `Leave "${this.clubName()}" out of the team name — schedules print it in front: ${this.clubName()}:${name}.`;
        }
        return null;
    });

    /** What's still needed, in order — the disabled Add's tooltip. null = ready. */
    readonly missing = computed<string | null>(() => {
        if (!this.text().trim()) return 'Pick or type a team';
        if (this.nameProblem()) return this.nameProblem();
        if (!this.picked() && !this.gradYear()) return 'Pick a grad year';
        if (!this.lop()) return 'Pick a level of play';
        if (!this.ageGroupId()) return 'Pick an age group';
        return null;
    });
    readonly canAdd = computed(() => this.missing() === null);

    // ── Combobox ──
    openList(): void {
        if (this.busy()) return;
        this.listOpen.set(true);
    }
    closeList(): void {
        this.listOpen.set(false);
        this.activeIndex.set(-1);
    }

    onType(value: string): void {
        this.text.set(value);
        this.errorMsg.set(null);
        // A different team: its level and grad year are not the last one's.
        this.lopPick.set('');
        this.activeIndex.set(-1);
        this.listOpen.set(true);
    }

    onNameKey(e: KeyboardEvent): void {
        const opts = this.options();
        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                this.listOpen.set(true);
                this.activeIndex.set(opts.length ? (this.activeIndex() + 1) % opts.length : -1);
                break;
            case 'ArrowUp':
                e.preventDefault();
                this.listOpen.set(true);
                this.activeIndex.set(opts.length ? (this.activeIndex() <= 0 ? opts.length - 1 : this.activeIndex() - 1) : -1);
                break;
            case 'Enter': {
                e.preventDefault();
                const active = this.listOpen() ? opts[this.activeIndex()] : undefined;
                if (active) this.pick(active);
                else if (this.canAdd()) this.add();
                else this.closeList();
                break;
            }
            case 'Escape':
                if (this.listOpen()) { e.stopPropagation(); this.closeList(); }
                break;
        }
    }

    pick(team: ClubTeamDto): void {
        this.text.set(team.clubTeamName);
        this.lopPick.set('');
        this.agPick.set(null);
        this.errorMsg.set(null);
        this.closeList();
        // Everything filled → straight to Add, so Enter registers; otherwise the first blank.
        afterNextRender(() => {
            const root = this.host.nativeElement;
            const target = this.canAdd()
                ? root.querySelector<HTMLElement>('.btn-add-team')
                : root.querySelector<HTMLElement>('select.is-blank');
            target?.focus();
        }, { injector: this.injector });
    }

    onAgeGroup(id: string): void {
        this.agPick.set(id);
    }

    // ── Add ──
    add(): void {
        if (this.busy() || !this.canAdd()) return;
        this.closeList();
        this.errorMsg.set(null);
        this.saving.set(true);

        const picked = this.picked();
        const ageGroupId = this.ageGroupId();
        const lop = this.lop();

        if (picked) {
            this.register(picked, ageGroupId, lop, false);
            return;
        }

        const name = this.text().trim();
        this.teamReg.createClubTeam({ clubTeamName: name, clubTeamGradYear: this.gradYear(), levelOfPlay: lop })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: team => this.register(team, ageGroupId, lop, true),
                error: (err: unknown) => {
                    this.saving.set(false);
                    this.errorMsg.set(extractHttpErrorMessage(err, 'Failed to add the team.'));
                },
            });
    }

    private register(team: ClubTeamDto, ageGroupId: string, lop: string, createdNow: boolean): void {
        this.teamReg.registerTeamForEvent({
            clubTeamId: team.clubTeamId,
            ageGroupId,
            teamName: team.clubTeamName,
            clubTeamGradYear: team.clubTeamGradYear,
            levelOfPlay: lop || team.clubTeamLevelOfPlay || undefined,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp: RegisterTeamResponse) => {
                    this.saving.set(false);
                    this.justAdded.set(new Set([...this.justAdded(), team.clubTeamId]));
                    const where = (resp.waitlistAgegroupName ?? '').replace(/^\s*WAITLIST\s*-\s*/i, '').trim() || 'the waitlist';
                    this.added.emit(resp.isWaitlisted
                        ? { message: `${team.clubTeamName} waitlisted for ${where}.`, tone: 'warning' }
                        : { message: `${team.clubTeamName} registered for ${this.eventName()}${createdNow ? ' and saved to your Club Team Library' : ''}.`, tone: 'success' });
                    this.reset();
                },
                error: (err: unknown) => {
                    // The row keeps what the rep entered. A new team is in the library now, so
                    // the reload turns the typed name into a pick and a retry registers it.
                    this.saving.set(false);
                    const why = extractHttpErrorMessage(err, 'The registration failed.');
                    this.errorMsg.set(createdNow ? `${why} ${team.clubTeamName} is saved to your Club Team Library.` : why);
                    if (createdNow) this.libraryChanged.emit();
                },
            });
    }

    /** Clear for the next team; the cursor goes back to the name. */
    private reset(): void {
        this.text.set('');
        this.gradYear.set('');
        this.lopPick.set('');
        this.agPick.set(null);
        this.errorMsg.set(null);
        this.closeList();
        afterNextRender(() => this.host.nativeElement.querySelector<HTMLInputElement>('.f-name')?.focus(),
            { injector: this.injector });
    }
}
