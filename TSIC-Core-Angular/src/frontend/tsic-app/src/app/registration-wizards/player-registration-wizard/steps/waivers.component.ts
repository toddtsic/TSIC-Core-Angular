import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RegistrationWizardService } from '../registration-wizard.service';

@Component({
    selector: 'app-rw-waivers',
    standalone: true,
    imports: [CommonModule, FormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Waivers & Agreements</h5>
      </div>
      <div class="card-body">
        @if (waivers().length === 0) {
          <div class="alert alert-info">No waivers are required for this registration. You can continue.</div>
        } @else {
          <p class="text-secondary small mb-3">Please review each waiver below and check the box to indicate acceptance.</p>
          <div class="vstack gap-3">
            @for (w of waivers(); track w.id) {
              <section class="border rounded p-3 bg-body-tertiary">
                <header class="d-flex align-items-center justify-content-between mb-2">
                  <h6 class="mb-0 fw-semibold">{{ w.title }}</h6>
                  <span class="badge bg-secondary-subtle text-secondary-emphasis" title="Version">v{{ w.version }}</span>
                </header>
                <div class="small" [innerHTML]="w.html"></div>
                <div class="form-check mt-2">
                  <input class="form-check-input" type="checkbox" [checked]="isAccepted(w.id)" (change)="toggleAccepted(w.id, $event)" [id]="'waiver-' + w.id">
                  <label class="form-check-label" [for]="'waiver-' + w.id">
                    I have read and agree to the {{ w.title.toLowerCase() }}
                    @if (w.required) { <span class="text-danger">(required)</span> }
                  </label>
                </div>
              </section>
            }
          </div>

          @if (requireSignature()) {
            <div class="mt-3">
              <h6 class="fw-semibold mb-2">Signature</h6>
              <div class="row g-3">
                <div class="col-12 col-md-6">
                  <label class="form-label">Full name <span class="text-danger">*</span></label>
                  <input type="text" class="form-control" [ngModel]="signatureName()" (ngModelChange)="setSignatureName($event)" placeholder="Type your full legal name" />
                </div>
                <div class="col-12 col-md-6">
                  <label class="form-label">Signing as <span class="text-danger">*</span></label>
                  <select class="form-select" [ngModel]="signatureRole()" (ngModelChange)="setSignatureRole($event)">
                    <option value="">Select role</option>
                    <option value="Parent/Guardian">Parent/Guardian</option>
                    <option value="Adult Player">Adult Player</option>
                  </select>
                </div>
              </div>
              @if (submitted() && !signatureValid()) {
                <div class="invalid-feedback d-block mt-2">Please enter your full name and select a signing role.</div>
              }
            </div>
          }
        }

        <div class="rw-bottom-nav d-flex gap-2 mt-3">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-primary" [disabled]="disableContinue()" (click)="handleContinue()">Continue</button>
        </div>
      </div>
    </div>
  `
})
export class WaiversComponent {
    @Output() next = new EventEmitter<void>();
    @Output() back = new EventEmitter<void>();

    private readonly state = inject(RegistrationWizardService);

    waivers = () => this.state.waiverDefinitions();
    submitted = signal(false);

    isAccepted = (id: string) => this.state.isWaiverAccepted(id);
    toggleAccepted(id: string, ev: Event) {
        const checked = (ev.target as HTMLInputElement | null)?.checked ?? false;
        this.state.setWaiverAccepted(id, checked);
    }

    requireSignature = () => this.state.requireSignature();
    signatureName = () => this.state.signatureName();
    signatureRole = () => this.state.signatureRole();
    setSignatureName(v: string) { this.state.signatureName.set(v || ''); }
    setSignatureRole(v: 'Parent/Guardian' | 'Adult Player' | '') { this.state.signatureRole.set(v || ''); }

    signatureValid(): boolean {
        if (!this.requireSignature()) return true;
        return !!this.signatureName()?.trim() && !!this.signatureRole();
    }

    disableContinue(): boolean {
        const waivers = this.waivers();
        const allAccepted = this.state.allRequiredWaiversAccepted();
        const sigOk = this.signatureValid();
        if (waivers.length === 0) return false;
        return !(allAccepted && sigOk);
    }

    handleContinue(): void {
        this.submitted.set(true);
        if (this.disableContinue()) return;
        this.next.emit();
    }
}
