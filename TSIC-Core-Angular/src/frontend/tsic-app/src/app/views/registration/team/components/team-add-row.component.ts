import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, OnChanges, SimpleChanges, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto, RegisterTeamResponse, SameNameEventTeamDto } from '@core/api';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';
import { LOP_CHOICES, formatLop, normalizeLop } from '@shared/teams/lop-choices';
import { clubNameInTeamName } from '@shared/teams/team-name-hints';
import { GRAD_YEAR_NA, libraryGradYearOptions, sameLibraryText } from '@shared/teams/library-team-form';
import { ageGroupLabel, isWaitlistAgeGroup } from './library-segment.types';
import { ageGroupWaitlists, byGradYearThenName } from './library-register-plan';
import { resolveRecommendedAgeGroupId } from './event-age-group.util';

/** What the step toasts once its reload lands. */
export interface TeamAddedEvent { message: string; tone: 'success' | 'warning'; }

let nextId = 0;

const norm = (s: string | null | undefined): string => (s ?? '').trim().toLowerCase();

/**
 * The age group a typed-in team's NAME names, or '' (Todd 2026-09-28: from the name for a type-in,
 * from the grad year for a library pick; otherwise must be selected). An age group matches when its name appears in the team name as a
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

/** Trimmed, case- and inner-space-insensitive. */
const teamKey = (s: string | null | undefined): string => (s ?? '').trim().replace(/\s+/g, ' ').toLowerCase();
/** An age group's identity across its WAITLIST twin: WAITLIST - 2029 is 2029. */
const ageGroupKey = (name: string | null | undefined): string => teamKey(ageGroupLabel({ ageGroupName: name ?? '' }));

/** One other same-name rep and the teams they registered in one age group. */
export interface RepAgeGroupTeams { repName: string; teams: string[]; }

/**
 * The other same-name reps who already registered teams in this age group here (Todd 2026-10-06),
 * each rep once, with their teams. Compared by AGE GROUP, never by team name: the same team carries
 * different names on different lists ("Top Tier National 2029" vs "2029"). A WAITLIST twin counts
 * as its age group.
 */
export function otherRepsInAgeGroup(eventTeams: readonly SameNameEventTeamDto[], ageGroupName: string | null | undefined): RepAgeGroupTeams[] {
    const ag = ageGroupKey(ageGroupName);
    if (!ag) return [];
    const byRep = new Map<string, string[]>();
    for (const e of eventTeams) {
        if (ageGroupKey(e.ageGroupName) !== ag) continue;
        const rep = e.repName.trim() || 'another rep';
        byRep.set(rep, [...(byRep.get(rep) ?? []), e.teamName.trim()]);
    }
    return [...byRep].map(([repName, teams]) => ({ repName, teams }));
}

/**
 * The ONE way a team gets onto this event (Todd 2026-09-28): a compound input on top of Registered
 * Teams. The team name is a combobox over the club's library:
 *
 *   pick a library team  → its grad year and level preselect; register it. Change the grad year
 *                          and it is a different team: Add makes a NEW library team (same name,
 *                          new grad year) and registers that; the picked one is untouched.
 *   type a new name      → grad year defaults to N/A, level is the rep's; the team is ADDED to
 *                          the library and registered, one press.
 *
 * Name + grad year is the library's identity (Todd 2026-09-28, the server's own rule). Typing a
 * library team's exact name (any case/spacing) IS picking it, so a near-duplicate never mints a
 * second library row. The age group preselects from the grad year when the row started from a
 * library team, from the typed name when new, else must be chosen.
 * Enter adds; the row clears and the cursor is back in the name for the next team.
 *
 * Same-name clubs (Todd 2026-10-06): the list also offers other same-name clubs' saved teams — a
 * rep taking over from a predecessor registers the predecessor's teams by click. A pick fills name,
 * grad year and level and goes the new-team way (saved to the rep's library, then registered). A
 * team another same-name rep already registered here gets a loud warning, and Add asks first.
 *
 * Owns its own writes, like the row editors; the step locks on `started` and reloads on `added` / `failed`.
 */
@Component({
    selector: 'app-team-add-row',
    standalone: true,
    template: `
    <section class="add" [attr.aria-labelledby]="inputId + '-title'" (keydown.escape)="onEscape()">
      <!-- The zone says what it is for, loudly (Todd 2026-09-28): this is where a team comes in. -->
      <header class="zone-head">
        <span class="zone-icon zone-icon--add" aria-hidden="true"><i class="bi bi-plus-lg"></i></span>
        <div class="zone-text">
          <h3 class="zone-title" [id]="inputId + '-title'">Add a Team</h3>
          <!-- Said only when there is something to pick (Todd 2026-09-28): an empty library, or one
               whose teams are all registered here already, gets the type-in line alone. -->
          <p class="zone-sub">
            @if (available().length > 0) {
              Pick one from your Club Team Library, or type a new team name &mdash; a new team is saved to your library too.
            } @else {
              <!-- Nothing to pick (empty library, or all registered here): the instruction alone. -->
              Fill in the team name, grad year, level of play and age group, then click <b>Add</b>.
            }
          </p>
        </div>
      </header>
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
                   (focus)="onFocus()"
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

        <!-- Grad year: preselected from the library team, N/A for a new one; always the rep's to
             change. A changed grad year on a library team makes a NEW library team (name + grad year
             is its identity) — the note under the row says so. -->
        <label class="f f--grad">
          <span class="f-label">Grad</span>
          <select class="f-input" [disabled]="busy()"
                  (change)="gradPick.set($any($event.target).value)">
            @for (yr of gradOptions(); track yr) {
              <option [value]="yr" [selected]="yr === gradYear()">{{ yr }}</option>
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
        @if (dirty() && !busy()) {
          <!-- A started row holds Back / Proceed shut — this is the way out without adding. -->
          <button type="button" class="btn-clear" (click)="clear()" title="Clear this row">
            <i class="bi bi-x-lg" aria-hidden="true"></i><span class="visually-hidden">Clear</span>
          </button>
        }
      </div>

      <!-- Another rep of this club already registered teams in this age group here (Todd 2026-10-06):
           said loudly, with their teams, and Add asks first — a second entry is a second fee and a
           headache for the tournament. Never a dead end: a genuinely different team still goes in. -->
      @if (dupGroups().length > 0) {
        <div class="dup-warn" role="alert">
          <i class="bi bi-exclamation-triangle-fill dup-icon" aria-hidden="true"></i>
          <div class="dup-body">
            @if (dupGroups().length === 1) {
              <p class="dup-text">
                Another {{ clubName() }} rep, <b>{{ dupGroups()[0].repName }}</b>, already registered
                {{ teamCount(dupGroups()[0].teams.length) }} in <b>{{ dupAgeGroup() }}</b>: {{ dupGroups()[0].teams.join(', ') }}.
              </p>
            } @else {
              <p class="dup-text">Other {{ clubName() }} reps already registered teams in <b>{{ dupAgeGroup() }}</b>:</p>
              <ul class="dup-list">
                @for (g of dupGroups(); track g.repName) {
                  <li><b>{{ g.repName }}</b>, {{ teamCount(g.teams.length) }}: {{ g.teams.join(', ') }}</li>
                }
              </ul>
            }
            <p class="dup-text">Adding another creates a separate entry and a separate fee.</p>
            @if (confirmOpen()) {
              <div class="dup-confirm">
                <p class="dup-q">{{ confirmQuestion() }}</p>
                <div class="dup-actions">
                  <button type="button" class="btn-dont" (click)="dontAdd()">Don't add</button>
                  <button type="button" class="btn-yes" [disabled]="busy()" (click)="add(true)">Yes &mdash; it's a different team, add it</button>
                </div>
              </div>
            }
          </div>
        </div>
      }

      <!-- One line under the fields: what the press will do, or what's in the way. -->
      <p class="add-note" [class.add-note--err]="!!nameProblem() || !!errorMsg()" role="status">
        @if (errorMsg()) {
          <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>{{ errorMsg() }}
        } @else if (nameProblem()) {
          <i class="bi bi-exclamation-triangle" aria-hidden="true"></i>{{ nameProblem() }}
        } @else if (target()) {
          <i class="bi bi-card-list" aria-hidden="true"></i>From your Club Team Library.
        } @else if (newFromBase()) {
          <i class="bi bi-plus-circle" aria-hidden="true"></i>
          <span>New library team <b>{{ text().trim() }} &middot; {{ gradYear() }}</b> &mdash;
            {{ base()!.clubTeamName }} &middot; {{ base()!.clubTeamGradYear || '—' }} stays as it is.</span>
        } @else if (text().trim()) {
          <i class="bi bi-plus-circle" aria-hidden="true"></i>New team &mdash; it will be saved to your Club Team Library too.
        } @else if (available().length) {
          {{ available().length }} {{ available().length === 1 ? 'team' : 'teams' }} in your Club Team Library not registered yet.
        }
      </p>
    </section>
    `,
    styleUrl: './zone-heading.scss',
    styles: [`
      :host { display: block; }

      /* The Add zone wears the library's color (primary): it is where library teams are picked and
         new ones made. Registered Teams below wears the event's (success). */
      .add {
        --zone: var(--bs-primary);
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        padding: var(--space-3) var(--space-4);
        border: 1px solid color-mix(in srgb, var(--zone) 40%, var(--bs-border-color));
        border-top: 4px solid var(--zone);
        border-radius: var(--radius-md);
        background: linear-gradient(180deg,
          color-mix(in srgb, var(--zone) 9%, var(--brand-surface)) 0%,
          color-mix(in srgb, var(--zone) 3%, var(--brand-surface)) 100%);
        box-shadow: var(--shadow-md);
      }

      .btn-clear {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 30px;
        height: 30px;
        padding: 0;
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        cursor: pointer;

        &:hover { color: var(--bs-danger); border-color: var(--bs-danger); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      /* Team | Grad | LOP | Age group | Add — wraps to Team over the rest on a narrow screen. */
      .add-fields { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-2); }

      .f { display: flex; flex-direction: column; gap: 1px; min-width: 0; margin: 0; }
      /* Team on its own line, full width, so its placeholder reads in full (Todd 2026-09-28);
         Grad | LOP | Age group | Add below it. */
      .f--team { flex: 1 1 100%; }
      .f--grad { flex: 0 0 6rem; }
      .f--lop { flex: 0 0 7.5rem; }
      .f--ag { flex: 1 1 10rem; }

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

      /* ── Same-name duplicate warning: amber, loud, never a dead end ── */
      .dup-warn {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border: 1px solid var(--bs-warning);
        border-left: 4px solid var(--bs-warning);
        border-radius: var(--radius-sm);
        background: color-mix(in srgb, var(--bs-warning) 14%, var(--brand-surface));
        color: var(--brand-text);
      }
      .dup-icon { flex-shrink: 0; margin-top: 2px; color: var(--bs-warning-text-emphasis); font-size: var(--font-size-base); }
      .dup-body { flex: 1; min-width: 0; }
      .dup-text, .dup-q { margin: 0; font-size: var(--font-size-sm); line-height: var(--line-height-normal); }
      .dup-text + .dup-text { margin-top: var(--space-1); }
      .dup-list {
        margin: var(--space-1) 0;
        padding-left: var(--space-4);
        font-size: var(--font-size-sm);
        line-height: var(--line-height-normal);
      }
      .dup-confirm {
        margin-top: var(--space-2);
        padding-top: var(--space-2);
        border-top: 1px solid color-mix(in srgb, var(--bs-warning) 45%, transparent);
      }
      .dup-q { font-weight: var(--font-weight-semibold); }
      .dup-actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-2); }
      .btn-dont, .btn-yes {
        height: 30px;
        padding: 0 var(--space-3);
        border-radius: var(--radius-sm);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.4; cursor: default; }
      }
      .btn-dont { border: 1px solid var(--bs-primary); background: var(--bs-primary); color: var(--neutral-0); }
      .btn-dont:hover { filter: brightness(0.93); }
      .btn-yes { border: 1px solid var(--bs-border-color); background: var(--brand-surface); color: var(--brand-text); }
      .btn-yes:hover:not(:disabled) { border-color: var(--brand-text-muted); }

      @media (max-width: 575.98px) {
        .f--ag { flex-basis: 100%; }
        .combo-opt { flex-wrap: wrap; }
        .btn-add-team { flex: 1 1 100%; height: 36px; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamAddRowComponent implements OnChanges {
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly destroyRef = inject(DestroyRef);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly injector = inject(Injector);

    readonly clubTeams = input.required<readonly ClubTeamDto[]>();
    readonly registeredTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly clubName = input('');
    readonly eventName = input('this event');
    /** Teams other same-name reps already registered here — the duplicate warning. */
    readonly sameNameEventTeams = input<readonly SameNameEventTeamDto[]>([]);
    /** Another write on the step is in flight. */
    readonly actionInProgress = input(false);

    /** A team was registered (or waitlisted): the step reloads, then toasts this. */
    readonly added = output<TeamAddedEvent>();
    /** Add pressed: the step locks the whole screen until its reload lands. */
    readonly started = output<void>();
    /** Refused. reload = the library gained a row first (a new team), so the step reloads to show it. */
    readonly failed = output<{ reload: boolean }>();

    readonly inputId = `team-add-${++nextId}`;
    readonly listId = `${this.inputId}-list`;
    optionId(i: number): string { return `${this.inputId}-opt-${i}`; }

    readonly formatLop = formatLop;
    readonly lopChoices = LOP_CHOICES;
    readonly gradYearOptions = libraryGradYearOptions();

    readonly text = signal('');
    /** The library team picked from the list — which one, when several share a name. */
    private readonly chosen = signal<ClubTeamDto | null>(null);
    /** Add was pressed on a team another same-name rep registered here: asking first. */
    readonly confirmOpen = signal(false);
    /** The rep's grad-year pick; '' = the library team's, or N/A for a new one. */
    readonly gradPick = signal('');
    /** The rep's level pick; '' = the library team's level. */
    readonly lopPick = signal('');
    /** The rep's age-group pick; null = untouched, follow the preselect. */
    private readonly agPick = signal<string | null>(null);
    readonly listOpen = signal(false);
    readonly activeIndex = signal(-1);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);
    /** Registered this visit, until the step's reload says so — keeps them out of the list meanwhile. */
    private readonly justAdded = signal<ReadonlySet<number>>(new Set());

    readonly busy = computed(() => this.saving() || this.actionInProgress());

    /** Anything entered or in flight — the wizard's Back / Proceed stay shut until it's added or cleared. */
    readonly dirty = computed(() => this.saving() || !!this.text().trim()
        || !!this.gradPick() || !!this.lopPick() || this.agPick() !== null);

    /** The row's ✕: drop what was entered, back to the name. */
    clear(): void {
        if (this.busy()) return;
        this.reset();
    }

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

    /** The dropdown: every available team until the rep types, then the ones whose name or grad
     *  year contain it; a picked team shows them all again. */
    readonly options = computed(() => {
        const q = norm(this.text());
        if (!q || this.base()) return this.available();
        return this.available().filter(t => norm(t.clubTeamName).includes(q) || norm(t.clubTeamGradYear).includes(q));
    });

    /**
     * The library team the row started from — it seeds grad year and level. The one picked from
     * the list while the name still reads as its name; else the one available team with exactly
     * this name (case/spacing-insensitive), so typing a library name IS picking it.
     */
    readonly base = computed<ClubTeamDto | null>(() => {
        // An empty row started from nothing — even if a library row's name is blank (one exists in prod data).
        if (!this.text().trim()) return null;
        const c = this.chosen();
        if (c && sameLibraryText(c.clubTeamName, this.text())) return c;
        const same = this.available().filter(t => sameLibraryText(t.clubTeamName, this.text()));
        return same.length === 1 ? same[0] : null;
    });

    /** Preselected from the library team, editable; a new team defaults to N/A (Todd 2026-09-28). */
    readonly gradYear = computed(() => this.gradPick() || this.base()?.clubTeamGradYear?.trim() || GRAD_YEAR_NA);

    /** The list, plus a library team's off-list value (a legacy year) so its own reads selected. */
    readonly gradOptions = computed(() =>
        this.gradYearOptions.includes(this.gradYear()) ? this.gradYearOptions : [this.gradYear(), ...this.gradYearOptions]);

    /**
     * The library team being REGISTERED: name + grad year, the library's identity. Leave the grad
     * year and it is the base; change it and there is none — Add makes a new library team with this
     * name and grad year (Todd 2026-09-28), the base untouched.
     */
    readonly target = computed<ClubTeamDto | null>(() => {
        if (!this.text().trim()) return null;
        return this.available().find(t =>
            sameLibraryText(t.clubTeamName, this.text()) && sameLibraryText(t.clubTeamGradYear, this.gradYear())) ?? null;
    });

    /** Started from a library team, grad year changed: a new library team, not that one. */
    readonly newFromBase = computed(() => !this.target() && !!this.base());

    readonly lop = computed(() => this.lopPick() || normalizeLop((this.target() ?? this.base())?.clubTeamLevelOfPlay));

    /** The rep's pick, else the preselect: from a library team, its grad year (as the library's
     *  own register did), falling back to the name; a typed-in team, its name (Todd 2026-09-28). */
    readonly ageGroupId = computed(() => {
        const pick = this.agPick();
        if (pick !== null) return pick;
        const fromName = ageGroupFromTeamName(this.ageGroups(), this.text());
        return this.target() ?? this.base()
            ? resolveRecommendedAgeGroupId(this.ageGroups(), this.gradYear()) || fromName
            : fromName;
    });

    readonly ageGroupOptions = computed(() => this.ageGroups().map(ag => {
        const label = ageGroupLabel(ag);
        return { id: ag.ageGroupId, text: ageGroupWaitlists(ag) ? `${label} · waitlist` : label };
    }));

    readonly waitlists = computed(() => {
        const ag = this.ageGroups().find(a => a.ageGroupId === this.ageGroupId());
        return !!ag && ageGroupWaitlists(ag);
    });

    /** Why this name + grad year can't be added. */
    readonly nameProblem = computed<string | null>(() => {
        const name = this.text().trim();
        if (!name) return null;
        // The server's rule: no two of this club's teams share a name within one age group.
        const agId = this.ageGroupId();
        const clash = agId ? this.registeredTeams().find(r => r.ageGroupId === agId && sameLibraryText(r.teamName, name)) : undefined;
        if (clash) return `${clash.teamName} is already registered in ${clash.ageGroupName} — pick another age group or name.`;
        if (this.target()) return null;
        const grad = this.gradYear();
        const inLibrary = this.clubTeams().find(t => sameLibraryText(t.clubTeamName, name) && sameLibraryText(t.clubTeamGradYear, grad));
        if (inLibrary?.bArchived) return `${inLibrary.clubTeamName} · ${grad} is archived in your Club Team Library — restore it there to use it again.`;
        if (inLibrary) return `${inLibrary.clubTeamName} · ${grad} is already registered for ${this.eventName()}.`;
        if (clubNameInTeamName(this.clubName(), name) === 'full') {
            return `Leave "${this.clubName()}" out of the team name — schedules print it in front: ${this.clubName()}:${name}.`;
        }
        return null;
    });

    /** The picked age group as the rep reads it (a WAITLIST twin shows its parent's name). */
    readonly dupAgeGroup = computed(() => {
        const ag = this.ageGroups().find(a => a.ageGroupId === this.ageGroupId());
        return ag ? ageGroupLabel(ag) : '';
    });

    /** Other same-name reps' teams in the picked age group — once the row has a name and an age group. */
    readonly dupGroups = computed(() =>
        this.text().trim() ? otherRepsInAgeGroup(this.sameNameEventTeams(), this.dupAgeGroup()) : []);

    readonly confirmQuestion = computed(() => {
        const n = this.dupGroups().reduce((sum, g) => sum + g.teams.length, 0);
        return `Is ${this.text().trim()} a different team from ${n === 1 ? 'that one' : 'those'}?`;
    });

    teamCount(n: number): string { return n === 1 ? '1 team' : `${n} teams`; }

    /** What's still needed, in order — the disabled Add's tooltip. null = ready. */
    readonly missing = computed<string | null>(() => {
        if (!this.text().trim()) return 'Pick or type a team';
        if (this.nameProblem()) return this.nameProblem();
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
        const before = this.base();
        this.text.set(value);
        if (!sameLibraryText(this.chosen()?.clubTeamName, value)) this.chosen.set(null);
        // A different library team: its grad year, level and age group are not the last one's.
        if (this.base() !== before) this.clearPicks();
        this.confirmOpen.set(false);
        this.errorMsg.set(null);
        this.activeIndex.set(-1);
        this.listOpen.set(true);
    }

    private clearPicks(): void {
        this.gradPick.set('');
        this.lopPick.set('');
        this.agPick.set(null);
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
        this.chosen.set(team);
        this.text.set(team.clubTeamName);
        this.clearPicks();
        this.confirmOpen.set(false);
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
    /** confirmed = the rep answered "Yes — it's a different team" to the duplicate question. */
    add(confirmed = false): void {
        if (this.busy() || !this.canAdd()) return;
        this.closeList();
        if (this.dupGroups().length > 0 && !confirmed) {
            // Ask first; "Don't add" is the default and takes the focus.
            this.confirmOpen.set(true);
            afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('.btn-dont')?.focus(),
                { injector: this.injector });
            return;
        }
        this.confirmOpen.set(false);
        this.errorMsg.set(null);
        this.saving.set(true);
        // The step locks everything (this row, the registered list) until its reload lands.
        this.started.emit();

        const target = this.target();
        const ageGroupId = this.ageGroupId();
        const lop = this.lop();

        if (target) {
            this.register(target, ageGroupId, lop, false);
            return;
        }

        // A new name, or a library team's name with a new grad year: a new library team.
        const name = this.text().trim();
        this.teamReg.createClubTeam({ clubTeamName: name, clubTeamGradYear: this.gradYear(), levelOfPlay: lop })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: team => this.register(team, ageGroupId, lop, true),
                error: (err: unknown) => {
                    this.saving.set(false);
                    this.errorMsg.set(extractHttpErrorMessage(err, 'Failed to add the team.'));
                    this.failed.emit({ reload: false });
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
                    this.failed.emit({ reload: createdNow });
                },
            });
    }

    /** "Don't add": the question closes and the row stays as entered, cursor back in the name. */
    dontAdd(): void {
        this.confirmOpen.set(false);
        this.focusName();
    }

    /** Escape closes the open list, else the open question. */
    onEscape(): void {
        if (this.listOpen()) this.closeList();
        else if (this.confirmOpen()) this.dontAdd();
    }

    /** Clear for the next team. The cursor goes back to the name once the step's reload lands. */
    private reset(): void {
        this.text.set('');
        this.chosen.set(null);
        this.confirmOpen.set(false);
        this.clearPicks();
        this.errorMsg.set(null);
        this.closeList();
        this.focusWhenFree = true;
        if (!this.actionInProgress()) this.focusName();
    }

    /** Set by a successful add; drained when the step releases its lock. */
    private focusWhenFree = false;

    ngOnChanges(changes: SimpleChanges): void {
        if (changes['actionInProgress'] && !this.actionInProgress() && this.focusWhenFree) this.focusName();
    }

    /** Back to the name for the next team — without popping the list over the team just added. */
    private focusName(): void {
        this.focusWhenFree = false;
        afterNextRender(() => {
            const input = this.host.nativeElement.querySelector<HTMLInputElement>('.f-name');
            if (!input) return;
            this.quietFocus = true;
            input.focus();
            this.quietFocus = false;
        }, { injector: this.injector });
    }

    /** A programmatic focus: the list stays shut until the rep types, clicks or arrows. */
    private quietFocus = false;
    onFocus(): void {
        if (!this.quietFocus) this.openList();
    }
}
