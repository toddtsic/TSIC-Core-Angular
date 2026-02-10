import { Component, Input, Output, EventEmitter, OnChanges, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LadtService } from '../services/ladt.service';
import { ConfirmDialogComponent } from '../../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { TeamDetailDto, UpdateTeamRequest, ClubRegistrationDto, MoveTeamToClubRequest } from '../../../../core/api';

@Component({
  selector: 'app-team-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, ConfirmDialogComponent],
  template: `
    <div class="detail-header d-flex align-items-center justify-content-between">
      <div class="d-flex align-items-center gap-2">
        <i class="bi bi-person-badge text-info"></i>
        <h5 class="mb-0">Team Details</h5>
        @if (team()?.playerCount) {
          <span class="badge bg-info-subtle text-info-emphasis">{{ team()!.playerCount }} players</span>
        }
      </div>
      <div class="d-flex gap-2">
        <button class="btn btn-sm btn-outline-secondary" (click)="openCloneDialog()" [disabled]="isSaving()" title="Clone team">
          <i class="bi bi-copy me-1"></i>Clone
        </button>
        <button class="btn btn-sm btn-outline-danger" (click)="confirmDrop()" [disabled]="isSaving()">
          <i class="bi bi-box-arrow-down me-1"></i>Drop
        </button>
        <div style="position: relative;">
          <button class="btn btn-sm btn-outline-secondary" (click)="moreOpen.set(!moreOpen())" title="More actions">
            <i class="bi bi-three-dots-vertical"></i>
          </button>
          @if (moreOpen()) {
            <ul class="dropdown-menu dropdown-menu-end show" style="position: absolute; right: 0; top: 100%;">
              @if (team()?.clubRepRegistrationId) {
                <li><button class="dropdown-item" (click)="confirmChangeClub(); moreOpen.set(false)">
                  <i class="bi bi-arrow-left-right me-2"></i>Change Club
                </button></li>
              }
            </ul>
          }
        </div>
      </div>
    </div>

    @if (isLoading()) {
      <div class="text-center py-4">
        <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
      </div>
    } @else if (team()) {
      @if (showDropConfirm()) {
        <div class="alert alert-danger d-flex align-items-center justify-content-between" role="alert">
          <span>
            <i class="bi bi-exclamation-triangle me-2"></i>
            Drop this team? It will be moved to Dropped Teams and deactivated.
          </span>
          <div class="d-flex gap-2">
            <button class="btn btn-sm btn-outline-secondary" (click)="showDropConfirm.set(false)">Cancel</button>
            <button class="btn btn-sm btn-danger" (click)="doDrop()">Drop</button>
          </div>
        </div>
      }

      @if (showCloneDialog()) {
        <div class="alert alert-secondary" role="alert">
          <h6 class="alert-heading mb-2">
            <i class="bi bi-copy me-1"></i>Clone Team
          </h6>
          <div class="mb-2">
            <label class="form-label small mb-1">New Team Name</label>
            <input class="form-control form-control-sm"
                   [ngModel]="cloneName()"
                   (ngModelChange)="cloneName.set($event)"
                   [ngModelOptions]="{standalone: true}">
          </div>
          @if (team()?.clubRepRegistrationId) {
            <div class="form-check mb-2">
              <input class="form-check-input" type="checkbox" id="cloneAddToClub"
                     [ngModel]="cloneAddToClub()"
                     (ngModelChange)="cloneAddToClub.set($event)"
                     [ngModelOptions]="{standalone: true}">
              <label class="form-check-label small" for="cloneAddToClub">
                Add to {{ team()?.clubName }}'s team library
              </label>
            </div>
          }
          <div class="d-flex gap-2">
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="showCloneDialog.set(false)">Cancel</button>
            <button type="button" class="btn btn-sm btn-primary" (click)="doClone()"
                    [disabled]="!cloneName().trim() || isSaving()">
              @if (isSaving()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              }
              Clone
            </button>
          </div>
        </div>
      }

      @if (showChangeClub()) {
        <div class="alert alert-info" role="alert">
          <h6 class="alert-heading mb-2">
            <i class="bi bi-arrow-left-right me-1"></i>Move to Different Club
          </h6>
          <p class="small mb-2">Current club: <strong>{{ team()?.clubName }}</strong></p>

          <div class="btn-group btn-group-sm mb-2">
            <button type="button" class="btn"
                    [class.btn-primary]="moveScope() === 'single'"
                    [class.btn-outline-primary]="moveScope() !== 'single'"
                    (click)="moveScope.set('single')">Just this team</button>
            <button type="button" class="btn"
                    [class.btn-primary]="moveScope() === 'all'"
                    [class.btn-outline-primary]="moveScope() !== 'all'"
                    (click)="moveScope.set('all')">All teams from this club</button>
          </div>

          <select class="form-select form-select-sm mb-2"
                  [ngModel]="selectedTargetRegistrationId()"
                  (ngModelChange)="selectedTargetRegistrationId.set($event)"
                  [ngModelOptions]="{standalone: true}">
            <option value="">Select target club...</option>
            @for (club of clubRegistrations(); track club.registrationId) {
              <option [value]="club.registrationId">{{ club.clubName }}</option>
            }
          </select>

          <div class="d-flex gap-2">
            <button type="button" class="btn btn-sm btn-outline-secondary" (click)="cancelChangeClub()">Cancel</button>
            <button type="button" class="btn btn-sm btn-primary" (click)="doChangeClub()"
                    [disabled]="!selectedTargetRegistrationId() || isMoving()">
              @if (isMoving()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              }
              Move
            </button>
          </div>
        </div>
      }

      <form (ngSubmit)="save()">
        <div class="row g-3">
          <div class="col-md-6">
            <label class="form-label">Team Name</label>
            <input class="form-control" [(ngModel)]="form.teamName" name="teamName">
          </div>
          <div class="col-md-3">
            <div class="form-check form-switch mt-4">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.active" name="active">
              <label class="form-check-label">Active</label>
            </div>
          </div>
        </div>

        <h6 class="section-label mt-4">Roster</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Max Roster Count</label>
            <input class="form-control" type="number" [(ngModel)]="form.maxCount" name="maxCount">
          </div>
          <div class="col-md-4">
            <div class="form-check form-switch mt-4">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bAllowSelfRostering" name="bAllowSelfRostering">
              <label class="form-check-label">Allow Self Rostering</label>
            </div>
          </div>
          <div class="col-md-4">
            <div class="form-check form-switch mt-4">
              <input class="form-check-input" type="checkbox" [(ngModel)]="form.bHideRoster" name="bHideRoster">
              <label class="form-check-label">Hide Roster</label>
            </div>
          </div>
          <div class="col-md-6">
            <label class="form-label">Division Requested</label>
            <input class="form-control" [(ngModel)]="form.divisionRequested" name="divisionRequested">
          </div>
          <div class="col-md-6">
            <label class="form-label">Last League Record</label>
            <input class="form-control" [(ngModel)]="form.lastLeagueRecord" name="lastLeagueRecord">
          </div>
        </div>

        <h6 class="section-label mt-4">Fees</h6>
        <div class="row g-3">
          <div class="col-md-4">
            <label class="form-label">Base Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.feeBase" name="feeBase">
          </div>
          <div class="col-md-4">
            <label class="form-label">Per Registrant Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.perRegistrantFee" name="perRegistrantFee">
          </div>
          <div class="col-md-4">
            <label class="form-label">Per Registrant Deposit</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.perRegistrantDeposit" name="perRegistrantDeposit">
          </div>
          <div class="col-md-4">
            <label class="form-label">Discount Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.discountFee" name="discountFee">
          </div>
          <div class="col-md-4">
            <label class="form-label">Late Fee</label>
            <input class="form-control" type="number" step="0.01" [(ngModel)]="form.lateFee" name="lateFee">
          </div>
        </div>

        <h6 class="section-label mt-4">Dates</h6>
        <div class="row g-3">
          <div class="col-md-3">
            <label class="form-label">Start Date</label>
            <input class="form-control" type="date" [(ngModel)]="form.startdate" name="startdate">
          </div>
          <div class="col-md-3">
            <label class="form-label">End Date</label>
            <input class="form-control" type="date" [(ngModel)]="form.enddate" name="enddate">
          </div>
          <div class="col-md-3">
            <label class="form-label">Effective As Of</label>
            <input class="form-control" type="date" [(ngModel)]="form.effectiveasofdate" name="effectiveasofdate">
          </div>
          <div class="col-md-3">
            <label class="form-label">Expire On</label>
            <input class="form-control" type="date" [(ngModel)]="form.expireondate" name="expireondate">
          </div>
        </div>

        <h6 class="section-label mt-4">Eligibility</h6>
        <div class="row g-3">
          <div class="col-md-3">
            <label class="form-label">Gender</label>
            <select class="form-select" [(ngModel)]="form.gender" name="gender">
              <option [ngValue]="null">Any</option>
              <option value="M">Male</option>
              <option value="F">Female</option>
              <option value="C">Co-Ed</option>
            </select>
          </div>
          <div class="col-md-3">
            <label class="form-label">Level of Play</label>
            <input class="form-control" [(ngModel)]="form.levelOfPlay" name="levelOfPlay">
          </div>
        </div>

        <h6 class="section-label mt-4">Notes</h6>
        <div class="row g-3">
          <div class="col-md-6">
            <label class="form-label">Requests</label>
            <textarea class="form-control" rows="2" [(ngModel)]="form.requests" name="requests"></textarea>
          </div>
          <div class="col-md-6">
            <label class="form-label">Team Comments</label>
            <textarea class="form-control" rows="2" [(ngModel)]="form.teamComments" name="teamComments"></textarea>
          </div>
          <div class="col-md-12">
            <label class="form-label">Keyword Pairs</label>
            <input class="form-control" [(ngModel)]="form.keywordPairs" name="keywordPairs">
          </div>
        </div>

        <div class="d-flex gap-2 mt-4">
          <button type="submit" class="btn btn-primary" [disabled]="isSaving()">
            @if (isSaving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>
            }
            Save Changes
          </button>
        </div>

        @if (saveMessage()) {
          <div class="alert mt-3 py-2" [class.alert-success]="!isError()" [class.alert-danger]="isError()" role="alert">
            <i class="bi me-1" [class.bi-check-circle]="!isError()" [class.bi-exclamation-triangle]="isError()"></i>
            {{ saveMessage() }}
          </div>
        }
      </form>
    }

    @if (showChangeClubWarning()) {
      <confirm-dialog
        title="Change Club — Are You Sure?"
        message="This operation will reassign team ownership to a different club. It affects club rep financials, schedule data, and team assignments. Only proceed if you are certain this is correct."
        confirmLabel="I Understand, Proceed"
        confirmVariant="warning"
        (confirmed)="onChangeClubWarningConfirmed()"
        (cancelled)="showChangeClubWarning.set(false)"
      />
    }
  `,
  styles: [`
    :host { display: block; }
    .detail-header { margin-bottom: var(--space-4); }
    .section-label {
      font-size: 0.8rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.03em;
      color: var(--bs-secondary-color);
      border-bottom: 1px solid var(--bs-border-color);
      padding-bottom: var(--space-1);
    }
  `]
})
export class TeamDetailComponent implements OnChanges {
  @Input({ required: true }) teamId!: string;
  @Output() saved = new EventEmitter<void>();
  @Output() cloned = new EventEmitter<void>();
  @Output() clubChanged = new EventEmitter<void>();

  private readonly ladtService = inject(LadtService);

  team = signal<TeamDetailDto | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);
  saveMessage = signal<string | null>(null);
  isError = signal(false);
  showDropConfirm = signal(false);
  showChangeClub = signal(false);
  clubRegistrations = signal<ClubRegistrationDto[]>([]);
  selectedTargetRegistrationId = signal('');
  moveScope = signal<'single' | 'all'>('single');
  isMoving = signal(false);
  moreOpen = signal(false);
  showChangeClubWarning = signal(false);
  showCloneDialog = signal(false);
  cloneName = signal('');
  cloneAddToClub = signal(false);

  form: any = {};

  ngOnChanges(): void {
    this.loadDetail();
  }

  private loadDetail(): void {
    this.isLoading.set(true);
    this.saveMessage.set(null);
    this.showDropConfirm.set(false);
    this.showChangeClub.set(false);

    this.ladtService.getTeam(this.teamId).subscribe({
      next: (detail) => {
        this.team.set(detail);
        this.form = { ...detail };
        // Normalize DateTime strings to YYYY-MM-DD for <input type="date">
        for (const key of ['startdate', 'enddate', 'effectiveasofdate', 'expireondate',
                           'discountFeeStart', 'discountFeeEnd', 'lateFeeStart', 'lateFeeEnd']) {
          if (this.form[key]) {
            this.form[key] = String(this.form[key]).substring(0, 10);
          }
        }
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  save(): void {
    this.isSaving.set(true);
    this.saveMessage.set(null);

    const request: UpdateTeamRequest = {
      teamName: this.form.teamName,
      active: this.form.active,
      divisionRequested: this.form.divisionRequested,
      lastLeagueRecord: this.form.lastLeagueRecord,
      color: this.form.color,
      maxCount: this.form.maxCount,
      bAllowSelfRostering: this.form.bAllowSelfRostering,
      bHideRoster: this.form.bHideRoster,
      feeBase: this.form.feeBase,
      perRegistrantFee: this.form.perRegistrantFee,
      perRegistrantDeposit: this.form.perRegistrantDeposit,
      discountFee: this.form.discountFee,
      discountFeeStart: this.form.discountFeeStart,
      discountFeeEnd: this.form.discountFeeEnd,
      lateFee: this.form.lateFee,
      lateFeeStart: this.form.lateFeeStart,
      lateFeeEnd: this.form.lateFeeEnd,
      startdate: this.form.startdate,
      enddate: this.form.enddate,
      effectiveasofdate: this.form.effectiveasofdate,
      expireondate: this.form.expireondate,
      dobMin: this.form.dobMin,
      dobMax: this.form.dobMax,
      gradYearMin: this.form.gradYearMin,
      gradYearMax: this.form.gradYearMax,
      schoolGradeMin: this.form.schoolGradeMin,
      schoolGradeMax: this.form.schoolGradeMax,
      gender: this.form.gender,
      dow: this.form.dow,
      dow2: this.form.dow2,
      fieldId1: this.form.fieldId1,
      fieldId2: this.form.fieldId2,
      fieldId3: this.form.fieldId3,
      levelOfPlay: this.form.levelOfPlay,
      requests: this.form.requests,
      keywordPairs: this.form.keywordPairs,
      teamComments: this.form.teamComments
    };

    this.ladtService.updateTeam(this.teamId, request).subscribe({
      next: (updated) => {
        this.team.set(updated);
        this.form = { ...updated };
        for (const key of ['startdate', 'enddate', 'effectiveasofdate', 'expireondate',
                           'discountFeeStart', 'discountFeeEnd', 'lateFeeStart', 'lateFeeEnd']) {
          if (this.form[key]) {
            this.form[key] = String(this.form[key]).substring(0, 10);
          }
        }
        this.isSaving.set(false);
        this.isError.set(false);
        this.saveMessage.set('Team saved successfully.');
        this.saved.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to save team.');
      }
    });
  }

  confirmDrop(): void {
    this.showDropConfirm.set(true);
  }

  doDrop(): void {
    this.isSaving.set(true);
    this.ladtService.dropTeam(this.teamId).subscribe({
      next: (result) => {
        this.isSaving.set(false);
        this.showDropConfirm.set(false);
        this.isError.set(false);
        this.saveMessage.set(result.message);
        this.loadDetail();
        this.saved.emit(); // refresh tree to show moved/inactive state
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to drop team.');
        this.showDropConfirm.set(false);
      }
    });
  }

  openCloneDialog(): void {
    this.cloneName.set(`${this.team()?.teamName ?? ''} (Copy)`);
    this.cloneAddToClub.set(!!this.team()?.clubRepRegistrationId);
    this.showCloneDialog.set(true);
  }

  doClone(): void {
    const name = this.cloneName()?.trim();
    if (!name) return;

    this.isSaving.set(true);
    this.ladtService.cloneTeam(this.teamId, { teamName: name, addToClubLibrary: this.cloneAddToClub() }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.isError.set(false);
        this.showCloneDialog.set(false);
        this.saveMessage.set('Team cloned successfully.');
        this.cloned.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to clone team.');
      }
    });
  }

  confirmChangeClub(): void {
    this.showChangeClubWarning.set(true);
  }

  onChangeClubWarningConfirmed(): void {
    this.showChangeClubWarning.set(false);
    this.saveMessage.set(null);
    this.selectedTargetRegistrationId.set('');
    this.moveScope.set('single');
    this.ladtService.getClubRegistrationsForJob().subscribe({
      next: (clubs) => {
        // Filter out the current team's club
        const currentRegId = this.team()?.clubRepRegistrationId;
        this.clubRegistrations.set(clubs.filter(c => c.registrationId !== currentRegId));
        this.showChangeClub.set(true);
      },
      error: (err) => {
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to load club list.');
      }
    });
  }

  doChangeClub(): void {
    const targetId = this.selectedTargetRegistrationId();
    if (!targetId) return;

    this.isMoving.set(true);
    this.saveMessage.set(null);

    const request: MoveTeamToClubRequest = {
      targetRegistrationId: targetId,
      moveAllFromClub: this.moveScope() === 'all'
    };

    this.ladtService.moveTeamToClub(this.teamId, request).subscribe({
      next: (result) => {
        this.isMoving.set(false);
        this.showChangeClub.set(false);
        this.isError.set(false);
        this.saveMessage.set(result.message);
        this.loadDetail();
        this.clubChanged.emit();
      },
      error: (err) => {
        this.isMoving.set(false);
        this.isError.set(true);
        this.saveMessage.set(err.error?.message || 'Failed to move team.');
      }
    });
  }

  cancelChangeClub(): void {
    this.showChangeClub.set(false);
    this.selectedTargetRegistrationId.set('');
    this.clubRegistrations.set([]);
  }
}
