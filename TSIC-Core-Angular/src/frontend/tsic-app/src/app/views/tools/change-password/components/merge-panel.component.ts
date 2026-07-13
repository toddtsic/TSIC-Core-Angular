import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import { displayEmail, dobLabel, childKey } from '../services/change-password.service';
import type { MergeCandidateDto } from '@core/api';

/**
 * ONE candidate in the merge dialog. Rendered IDENTICALLY on both sides — that sameness is the whole
 * point, because putting the two side by side is what turns an irreversible write into a comparison
 * the admin can actually make.
 *
 * Two dropdowns of usernames would not be enough. Half the family usernames in this system are raw
 * GUIDs (`76da3519-7842-400e-84ed-4ea6005e974c`), so the username tells the admin nothing. What tells
 * them something is:
 *
 *   1. THE IDENTITY BLOCK — the mother, or the adult themselves. This *is* the security key, shown.
 *      If the two panels are not the same person, stop.
 *   2. THE CHILDREN, with the ones that will actually be fused marked.
 *   3. EVERY REGISTRATION THAT WILL MOVE — the rows, not a count. A merge moves registrations, so the
 *      admin looks at the registrations before they move.
 */
@Component({
  selector: 'cp-merge-panel',
  standalone: true,
  imports: [CommonModule, PhonePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cp-card" [class.cp-card-retire]="retiring()">

      <!-- The identity block. Recorded as typed — mom and dad are routinely in swapped slots, so it is
           shown as it is stored rather than tidied into a shape that would hide the mismatch. -->
      @if (account().momName || account().momEmail || account().momPhone) {
        <dl class="cp-parent">
          <dt>Mom</dt>
          <dd>
            <strong>{{ account().momName || '—' }}</strong>
            <span>{{ email(account().momEmail) }}</span>
            <span>{{ account().momPhone | phone }}</span>
          </dd>
          @if (account().dadName || account().dadEmail) {
            <dt>Dad</dt>
            <dd>
              <strong>{{ account().dadName || '—' }}</strong>
              <span>{{ email(account().dadEmail) }}</span>
            </dd>
          }
        </dl>
      } @else if (account().personName || account().email || account().phone) {
        <dl class="cp-parent">
          <dt>Person</dt>
          <dd>
            <strong>{{ account().personName || '—' }}</strong>
            <span>{{ email(account().email) }}</span>
            <span>{{ account().phone | phone }}</span>
          </dd>
        </dl>
      }

      @if (account().children.length) {
        <h4 class="cp-card-head">Children</h4>
        <ul class="cp-children">
          @for (child of account().children; track child.userId) {
            <li [class.is-match]="merges(child.name, child.dob)">
              <span class="cp-child-name">{{ child.name }}</span>
              <span class="cp-child-dob">{{ dob(child.dob) }}</span>
              @if (merges(child.name, child.dob)) {
                <span class="cp-match" title="The same child on both logins — these two records become one">
                  <i class="bi bi-check-lg"></i> merges
                </span>
              }
            </li>
          }
        </ul>
      }

      <h4 class="cp-card-head">
        {{ account().registrations.length }}
        registration{{ account().registrations.length === 1 ? '' : 's' }}
        @if (retiring()) { <span class="cp-all-move">— ALL MOVE</span> }
      </h4>
      <ul class="cp-regs">
        @for (reg of account().registrations; track reg.registrationId) {
          <li>
            <span class="cp-reg-job">{{ reg.jobName }}</span>
            <span class="cp-reg-who">{{ reg.personName || reg.roleName }}</span>
            <span class="cp-reg-cust">{{ reg.customerName }}</span>
          </li>
        }
      </ul>
    </div>
  `,
  styles: [`
    :host { display: block; min-width: 0; }

    .cp-card {
        margin-top: var(--space-2);
        padding: var(--space-3);
        border: 1px solid var(--brand-border);
        border-radius: var(--radius-md);
        background: var(--brand-bg-secondary);
        min-width: 0;
    }

    .cp-card-retire {
        border-color: color-mix(in srgb, var(--brand-danger) 35%, transparent);
        background: color-mix(in srgb, var(--brand-danger) 5%, var(--brand-bg-secondary));
    }

    .cp-parent {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--space-1) var(--space-3);
        margin: 0 0 var(--space-3);
    }
    .cp-parent dt {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--brand-text-muted);
    }
    .cp-parent dd {
        margin: 0;
        min-width: 0;
        display: flex;
        flex-direction: column;
        font-size: var(--font-size-xs);
        color: var(--brand-text);
    }
    .cp-parent dd span {
        color: var(--brand-text-muted);
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .cp-card-head {
        margin: var(--space-3) 0 var(--space-1);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--brand-text-muted);
    }

    .cp-all-move {
        color: var(--brand-danger);
        font-weight: var(--font-weight-bold);
    }

    .cp-children, .cp-regs {
        list-style: none;
        margin: 0;
        padding: 0;
        font-size: var(--font-size-xs);
    }

    .cp-children li {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        padding: 0.15rem 0;
        border-bottom: 1px dashed var(--brand-border);
    }
    .cp-children li:last-child { border-bottom: none; }

    .cp-child-name {
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
    }

    .cp-child-dob {
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
    }

    /* Marked only when the two records will actually become one — the same rule the server applies.
       Text as well as colour: never rely on colour alone to carry a fact. */
    .cp-match {
        margin-left: auto;
        color: var(--brand-success);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
    }

    .cp-regs li {
        display: grid;
        grid-template-columns: 1fr auto;
        gap: 0 var(--space-2);
        padding: var(--space-1) 0;
        border-bottom: 1px dashed var(--brand-border);
    }
    .cp-regs li:last-child { border-bottom: none; }

    .cp-reg-job {
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .cp-reg-who {
        color: var(--brand-text-muted);
        white-space: nowrap;
    }

    .cp-reg-cust {
        grid-column: 1 / -1;
        color: var(--brand-text-muted);
        font-size: var(--font-size-2xs);
    }

    @media (prefers-reduced-motion: reduce) {
        * { animation: none !important; transition: none !important; }
    }
  `]
})
export class MergePanelComponent {
  readonly account = input.required<MergeCandidateDto>();
  readonly retiring = input(false);

  /**
   * The children this merge will FUSE, keyed exactly as the server keys them. Computed by the parent,
   * because it depends on BOTH panels — a child collapses only when each side holds exactly ONE row
   * for that (name, DOB). Two rows on either side is a deliberate double-registration, which the
   * server refuses to touch, so marking it here would be a lie about what the button does.
   */
  readonly collapsingKeys = input<ReadonlySet<string>>(new Set<string>());

  merges(name: string, dob: string | null | undefined): boolean {
    return this.collapsingKeys().has(childKey(name, dob));
  }

  email(value: string | null | undefined): string {
    return displayEmail(value);
  }

  dob(value: string | null | undefined): string {
    return dobLabel(value);
  }
}
