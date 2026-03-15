import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, OnInit } from '@angular/core';
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
export class CommunicationsTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  displayName = linkedSignal(() => this.svc.communications()?.displayName ?? null);
  regFormFrom = linkedSignal(() => this.svc.communications()?.regFormFrom ?? null);
  regFormCcs = linkedSignal(() => this.svc.communications()?.regFormCcs ?? null);
  regFormBccs = linkedSignal(() => this.svc.communications()?.regFormBccs ?? null);
  rescheduleemaillist = linkedSignal(() => this.svc.communications()?.rescheduleemaillist ?? null);
  alwayscopyemaillist = linkedSignal(() => this.svc.communications()?.alwayscopyemaillist ?? null);
  bDisallowCcplayerConfirmations = linkedSignal(() => this.svc.communications()?.bDisallowCcplayerConfirmations ?? null);

  private readonly cleanSnapshot = computed(() => {
    const c = this.svc.communications();
    if (!c) return '';
    return JSON.stringify({
      displayName: c.displayName,
      regFormFrom: c.regFormFrom,
      regFormCcs: c.regFormCcs,
      regFormBccs: c.regFormBccs,
      rescheduleemaillist: c.rescheduleemaillist,
      alwayscopyemaillist: c.alwayscopyemaillist,
      bDisallowCcplayerConfirmations: c.bDisallowCcplayerConfirmations,
    } satisfies UpdateJobConfigCommunicationsRequest);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
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
