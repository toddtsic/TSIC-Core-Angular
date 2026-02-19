import { Component, inject, ChangeDetectionStrategy, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import type { UpdateJobConfigCommunicationsRequest } from '@core/api';

@Component({
  selector: 'app-communications-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './communications-tab.component.html',
})
export class CommunicationsTabComponent {
  protected readonly svc = inject(JobConfigService);

  displayName = signal<string | null>(null);
  regFormFrom = signal<string | null>(null);
  regFormCcs = signal<string | null>(null);
  regFormBccs = signal<string | null>(null);
  rescheduleemaillist = signal<string | null>(null);
  alwayscopyemaillist = signal<string | null>(null);
  bDisallowCcplayerConfirmations = signal<boolean | null>(null);

  private cleanSnapshot = '';

  constructor() {
    effect(() => {
      const c = this.svc.communications();
      if (!c) return;
      this.displayName.set(c.displayName);
      this.regFormFrom.set(c.regFormFrom);
      this.regFormCcs.set(c.regFormCcs);
      this.regFormBccs.set(c.regFormBccs);
      this.rescheduleemaillist.set(c.rescheduleemaillist);
      this.alwayscopyemaillist.set(c.alwayscopyemaillist);
      this.bDisallowCcplayerConfirmations.set(c.bDisallowCcplayerConfirmations);
      this.cleanSnapshot = JSON.stringify(this.buildPayload());
      this.svc.saveHandler.set(() => this.save());
    });
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot) {
      this.svc.markClean('communications');
    } else {
      this.svc.markDirty('communications');
    }
  }

  save(): void {
    this.svc.saveCommunications(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigCommunicationsRequest {
    return {
      displayName: this.displayName(),
      regFormFrom: this.regFormFrom(),
      regFormCcs: this.regFormCcs(),
      regFormBccs: this.regFormBccs(),
      rescheduleemaillist: this.rescheduleemaillist(),
      alwayscopyemaillist: this.alwayscopyemaillist(),
      bDisallowCcplayerConfirmations: this.bDisallowCcplayerConfirmations(),
    };
  }
}
