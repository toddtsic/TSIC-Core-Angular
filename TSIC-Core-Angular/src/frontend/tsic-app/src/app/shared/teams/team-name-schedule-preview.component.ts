import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { clubNameInTeamName, scheduleTeamLabel } from './team-name-hints';

/**
 * The club-name hammer (Todd 2026-09-24: "we have to HAMMER the concept of NOT INCLUDING YOUR
 * CLUB NAME in your team name — this causes schedules to read Long Club Name:Long Club Name
 * 2028 Blue").
 *
 * A quiet italic tip was ignored. This shows the rep the actual schedule label, live, as they
 * type — ScheduleRepository.ComposeTeamLabel is "{club}:{team}", so a club name in the team name
 * doubles up in front of their eyes. Full club name = red, and the host form blocks the save.
 * A word of the club name = amber warning, never a block (a club called "Elite" can't lose the
 * word "Elite"). Shared by team-form-modal and add-and-register-team-modal so the two forms say
 * one thing.
 */
@Component({
    selector: 'team-name-schedule-preview',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
    <div class="preview" [class.is-full]="hit() === 'full'" [class.is-partial]="hit() === 'partial'" role="status" aria-live="polite">
      <div class="preview-row">
        <span class="preview-key">On schedules this team reads</span>
        <span class="preview-label" [class.is-placeholder]="!teamName().trim()">{{ label() }}</span>
      </div>
      @if (hit() === 'full') {
        <div class="preview-msg">
          <i class="bi bi-x-octagon-fill" aria-hidden="true"></i>
          <span><strong>Your club name is added automatically.</strong> Take <strong>{{ clubName().trim() }}</strong> out of the
            team name — enter just <strong>{{ suggestion() }}</strong>.</span>
        </div>
      } @else if (hit() === 'partial') {
        <div class="preview-msg">
          <i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i>
          <span><strong>That looks like part of your club name.</strong> Schedules already show
            <strong>{{ clubName().trim() }}</strong> in front of every team — the team name is only what comes after it.</span>
        </div>
      } @else {
        <div class="preview-hint">
          Don't put <strong>{{ clubName().trim() }}</strong> in the team name — it is already there.
        </div>
      }
    </div>
  `,
    styles: [`
      .preview {
        margin-top: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border: 1px solid var(--bs-border-color);
        border-left: 4px solid var(--bs-primary);
        border-radius: var(--radius-md);
        background: color-mix(in srgb, var(--bs-primary) 5%, var(--bs-body-bg));
        font-size: var(--font-size-xs);
        line-height: var(--line-height-normal);
        color: var(--brand-text);
        transition: background-color 0.15s ease, border-color 0.15s ease;

        &.is-partial {
          border-color: var(--bs-warning);
          background: var(--bs-warning-bg-subtle);
        }
        &.is-full {
          border-color: var(--bs-danger);
          background: var(--bs-danger-bg-subtle);
        }
      }
      .preview-row {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--space-1) var(--space-2);
      }
      .preview-key {
        text-transform: uppercase;
        letter-spacing: 0.04em;
        font-size: 0.7rem;
        font-weight: var(--font-weight-semibold);
        color: var(--bs-secondary-color);
      }
      .preview-label {
        font-weight: var(--font-weight-bold);
        font-size: var(--font-size-sm);
        overflow-wrap: anywhere;

        &.is-placeholder { opacity: 0.55; font-weight: var(--font-weight-medium); }
        .is-full & { color: var(--bs-danger-text-emphasis); }
        .is-partial & { color: var(--bs-warning-text-emphasis); }
      }
      .preview-msg {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        margin-top: var(--space-1);

        i { flex-shrink: 0; margin-top: 2px; }
        .is-full & { color: var(--bs-danger-text-emphasis); }
        .is-partial & { color: var(--bs-warning-text-emphasis); }
      }
      .preview-hint {
        margin-top: var(--space-1);
        color: var(--bs-secondary-color);
      }
      strong { font-weight: var(--font-weight-bold); }

      @media (prefers-reduced-motion: reduce) {
        .preview { transition: none !important; }
      }
    `],
})
export class TeamNameSchedulePreviewComponent {
    readonly clubName = input('');
    readonly teamName = input('');

    /** Full club name in the team name, a word of it, or clean. */
    readonly hit = computed(() => clubNameInTeamName(this.clubName(), this.teamName()));

    /** The label ScheduleRepository would print — with "2028 Blue" standing in while the name is empty. */
    readonly label = computed(() =>
        scheduleTeamLabel(this.clubName(), this.teamName().trim() || '2028 Blue'));

    /** The typed name with the club name cut out, as the thing to enter instead. */
    readonly suggestion = computed(() => {
        const club = this.clubName().trim();
        if (!club) return this.teamName().trim();
        // Cut the club name out, then any separator the rep put between it and the name (":", "-").
        const stripped = this.teamName().replace(new RegExp(escapeRegExp(club), 'ig'), ' ')
            .replace(/\s+/g, ' ').trim()
            .replace(/^[^A-Za-z0-9]+|[^A-Za-z0-9]+$/g, '').trim();
        return stripped || '2028 Blue';
    });
}

function escapeRegExp(s: string): string {
    return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
