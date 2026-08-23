import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, map } from 'rxjs';
import {
  MultiSelectModule,
  CheckBoxSelectionService,
  FilteringEventArgs,
  FieldSettingsModel
} from '@syncfusion/ej2-angular-dropdowns';
import { Query } from '@syncfusion/ej2-data';
import { GridAllModule, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { PushNotificationService } from './services/push-notification.service';
import type {
  PushNotificationHistoryDto,
  PushNotificationReadinessDto,
  PushTeamOptionDto
} from '../../../core/api';

/** A single unmet delivery condition, rendered as an alert above the send form. */
export interface PushReadinessWarning {
  level: 'danger' | 'warning';
  icon: string;
  text: string;
}

/** reference.JobTypes — showcase runs neither mobile app, so it gets its own explanation. */
const SHOWCASE_JOB_TYPE = 6;

@Component({
  selector: 'app-push-notification',
  standalone: true,
  imports: [FormsModule, GridAllModule, MultiSelectModule],
  // CheckBox mode is a separate ej2 module feature; without this provider the tick boxes
  // never render and the control silently degrades to plain multi-select.
  providers: [CheckBoxSelectionService],
  templateUrl: './push-notification.component.html',
  styleUrl: './push-notification.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PushNotificationComponent implements OnInit {
  private readonly pushService = inject(PushNotificationService);

  // UI state
  isLoading = signal(false);
  isSending = signal(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  // Data
  readiness = signal<PushNotificationReadinessDto | null>(null);
  pushText = signal('');
  history = signal<PushNotificationHistoryDto[]>([]);

  /** Teams in this job. Empty until loaded, and empty is a legitimate state. */
  teams = signal<PushTeamOptionDto[]>([]);

  /**
   * Teams this send is narrowed to. EMPTY MEANS EVERYONE — the event-wide send is what an
   * untouched selector does, which is the common case and stays one click.
   */
  selectedTeamIds = signal<string[]>([]);

  /** Names of the selected teams, in the order the list shows them. */
  selectedTeamNames = computed(() => {
    const picked = new Set(this.selectedTeamIds());
    return this.teams().filter(t => picked.has(t.teamId)).map(t => t.display);
  });

  /**
   * Upper bound on the reach of the current selection. It is a SUM, not a distinct count: a
   * parent following two selected teams is counted twice here but receives one push, so the
   * label says "up to" rather than stating a reach the send will not match.
   */
  selectedDeviceCeiling = computed(() => {
    const picked = new Set(this.selectedTeamIds());
    return this.teams()
      .filter(t => picked.has(t.teamId))
      .reduce((sum, t) => sum + t.deviceCount, 0);
  });

  /**
   * Dropdown rows, one per team, each carrying its subscriber count. Filtering runs over this
   * same list, so typing a club name narrows 500+ teams to the handful that matter — the
   * reason this is a filtering multiselect and not a native select.
   *
   * A team nobody has the app for reads as (0 device(s)) BEFORE the send rather than after.
   */
  audienceOptions = computed(() => this.teams().map(t => ({
    teamId: t.teamId,
    display: `${t.display} (${t.deviceCount} device(s))`
  })));
  readonly audienceFields: FieldSettingsModel = { text: 'display', value: 'teamId' };

  // Computed
  /**
   * The one app this job feeds. A job is never both: TSIC-Events and TSIC-Teams are separate
   * Firebase projects, and a token minted by one is rejected by the other. The backend resolves
   * it from job type plus the TSIC-Teams switch; this screen only reports the result.
   */
  audience = computed(() => this.readiness()?.audience ?? 'None');
  audienceLabel = computed(() => {
    switch (this.audience()) {
      case 'Events': return 'TSIC-Events';
      case 'Teams': return 'TSIC-Teams';
      default: return 'No mobile app';
    }
  });

  /** Devices in the resolved audience's pool — who a send would actually reach. */
  deviceCount = computed(() => this.readiness()?.deviceCount ?? 0);

  hasAudience = computed(() => this.audience() !== 'None');
  canSend = computed(() =>
    this.pushText().trim().length > 0 && !this.isSending() && this.hasAudience());

  /**
   * Unmet delivery conditions. The screen is deliberately always reachable — the nav
   * used to hide it on a flag that had nothing to do with mobile, which made "why is
   * the menu item missing" unanswerable from the UI. Everything that would stop a send
   * from landing is stated here instead.
   */
  warnings = computed<PushReadinessWarning[]>(() => {
    const r = this.readiness();
    if (!r) return [];

    const list: PushReadinessWarning[] = [];

    // No audience at all — nothing else matters, so this is the only thing said.
    if (r.audience === 'None') {
      list.push({
        level: 'danger',
        icon: 'bi-x-octagon-fill',
        text: r.jobTypeId === SHOWCASE_JOB_TYPE
          ? 'Showcase events run neither mobile app, so there is nobody to push to.'
          : 'This event feeds no mobile app. Turn on Enable TSIC Teams under ' +
            'Job Settings → Mobile/Store to give it an audience.'
      });
      return list;
    }

    if (!r.senderConfigured) {
      list.push({
        level: 'danger',
        icon: 'bi-key-fill',
        text: `No Firebase sender is configured for ${this.audienceLabel()}, so a send would ` +
              'fail. The other app\'s credential cannot deliver to these devices.'
      });
    }

    if (r.deviceCount === 0) {
      list.push({
        level: 'warning',
        icon: 'bi-phone-slash',
        text: `No ${this.audienceLabel()} devices have registered for this event yet. ` +
              'A send will reach nobody.'
      });
    }

    // Registered devices outlive the switch that hid the event from the app.
    if (r.audience === 'Events' && !r.eventsEnabled && r.deviceCount > 0) {
      list.push({
        level: 'warning',
        icon: 'bi-eye-slash',
        text: `This event is hidden from the TSIC-Events app, but ${r.deviceCount} ` +
              'device(s) are still registered from before it was hidden. They will receive this push.'
      });
    }

    return list;
  });

  // Grid settings
  sortSettings: SortSettingsModel = { columns: [{ field: 'sentWhen', direction: 'Descending' }] };

  ngOnInit(): void {
    this.loadReadiness();
    this.loadHistory();
    this.loadTeams();
  }

  private loadReadiness(): void {
    this.pushService.getReadiness().subscribe({
      next: (data) => this.readiness.set(data),
      error: () => { /* Readiness is advisory — a failure must not block the screen */ }
    });
  }

  private loadTeams(): void {
    this.pushService.availableTeams().subscribe({
      next: (data) => this.teams.set(data),
      // No team list just means no narrowing; the job-wide send still works.
      error: () => { /* non-fatal */ }
    });
  }

  private loadHistory(): void {
    this.isLoading.set(true);
    this.pushService.getHistory().subscribe({
      next: (data) => {
        this.history.set(data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load notification history.');
        this.isLoading.set(false);
      }
    });
  }

  /** Substring match over the visible label, so "Coppermine" or "2027 Blue" both find it. */
  onAudienceFiltering(e: FilteringEventArgs): void {
    const text = (e.text ?? '').trim();
    const query = text ? new Query().where('display', 'contains', text, true) : new Query();
    e.updateData(this.audienceOptions(), query);
  }

  onAudienceChange(value: unknown): void {
    // ej2 hands back null when the last tick is cleared, which is the "everyone" state.
    this.selectedTeamIds.set(Array.isArray(value) ? (value as string[]) : []);
  }

  sendPush(): void {
    const text = this.pushText().trim();
    if (!text) return;

    this.isSending.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const teamIds = this.selectedTeamIds();

    // Two endpoints, one button. A narrowed send goes to send-teams, which stamps each team
    // onto its own audit row and dedupes across them so a parent following two selected teams
    // gets ONE notification. Both responses carry deviceCount; widening to a common shape here
    // keeps the subscribe monomorphic.
    const send$: Observable<{ deviceCount: number; teamCount: number }> = teamIds.length
      ? this.pushService.sendTeamsPush(teamIds, text)
      : this.pushService.sendPush(text).pipe(map(r => ({ deviceCount: r.deviceCount, teamCount: 0 })));

    send$.subscribe({
      next: (res) => {
        const who = res.teamCount === 0
          ? 'everyone in this event'
          : res.teamCount === 1
            ? this.selectedTeamNames()[0]
            : `${res.teamCount} teams`;
        this.successMessage.set(`Sent to ${who} - delivered to ${res.deviceCount} device(s).`);
        this.pushText.set('');
        this.isSending.set(false);
        this.loadReadiness();
        this.loadHistory();
      },
      // The team endpoint answers a cross-job attempt with ProblemDetails (detail), the
      // job-wide one with a plain message. Read both so neither failure shows as generic.
      error: (err: { error?: { detail?: string; message?: string } }) => {
        this.errorMessage.set(
          err.error?.detail || err.error?.message || 'Failed to send push notification.');
        this.isSending.set(false);
      }
    });
  }
}