import { ChangeDetectionStrategy, Component, OnInit, AfterViewChecked, HostListener, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkTreeModule } from '@angular/cdk/tree';
import { Observable, forkJoin } from 'rxjs';
import { LadtService } from './services/ladt.service';
import { LadtEditGuardService } from './services/ladt-edit-guard.service';
import { LeagueDetailComponent } from './components/league-detail.component';
import { AgegroupDetailComponent } from './components/agegroup-detail.component';
import { DivisionDetailComponent } from './components/division-detail.component';
import { TeamDetailComponent } from './components/team-detail.component';
import { LadtSiblingGridComponent } from './components/ladt-sibling-grid.component';
import { CloneTeamDialogComponent } from './components/clone-team-dialog.component';
import { CloneAgegroupDialogComponent } from './components/clone-agegroup-dialog.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';
import { FormsModule } from '@angular/forms';
import {
  COLUMNS_BY_LEVEL, ID_FIELD_BY_LEVEL,
  type LadtColumnDef
} from './configs/ladt-grid-columns';
import type { ParentBreadcrumb } from './components/ladt-sibling-grid.component';
import type { LadtTreeNodeDto, DivisionNameSyncPreview, JobFeeDto } from '../../../core/api';
import { AGEGROUP_COLORS } from '../../scheduling/shared/utils/scheduling-helpers';

/** Flat node for CdkTree display */
export interface LadtFlatNode {
  id: string;
  parentId: string | null;
  name: string;
  level: number;
  isLeaf: boolean;
  teamCount: number;
  playerCount: number;
  expandable: boolean;
  active: boolean;
  clubName: string | null;
  color: string | null;
  parentColor: string | null;
  isSpecial: boolean;
  isPhantom?: boolean;
}

@Component({
  selector: 'app-ladt',
  standalone: true,
  imports: [
    CommonModule,
    CdkTreeModule,
    FormsModule,
    LeagueDetailComponent,
    AgegroupDetailComponent,
    DivisionDetailComponent,
    TeamDetailComponent,
    LadtSiblingGridComponent,
    CloneTeamDialogComponent,
    CloneAgegroupDialogComponent,
    ConfirmDialogComponent,
    TsicDialogComponent
  ],
  templateUrl: './ladt.component.html',
  styleUrl: './ladt.component.scss',
  providers: [LadtEditGuardService],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LadtEditorComponent implements OnInit, AfterViewChecked {
  private readonly ladtService = inject(LadtService);
  private readonly editGuard = inject(LadtEditGuardService);

  // ── State ──
  isLoading = signal(false);
  isTreeBusy = signal(false);
  errorMessage = signal<string | null>(null);

  // Clone dialog state (driven from grid-row clone button)
  cloneSource = signal<{ teamId: string; teamName: string; hasClubRep: boolean; clubName: string | null } | null>(null);
  cloneAgegroupSource = signal<{ agegroupId: string; agegroupName: string } | null>(null);
  totalTeams = signal(0);
  totalPlayers = signal(0);

  // Tree data (all nodes, flat)
  flatNodes = signal<LadtFlatNode[]>([]);
  private rawTree = signal<LadtTreeNodeDto[]>([]);

  // Scheduled team IDs (raw data from backend, used for KPI computation)
  scheduledTeamIds = signal<Set<string>>(new Set());

  // Fee data for grid enrichment
  private jobFees = signal<JobFeeDto[]>([]);

  // Job-level full-payment phase baselines (from the tree root). The fee resolver falls back
  // to these when no per-scope override exists, so the grid's Payment Phase column must too.
  // Players flag → Player role; Teams flag → ClubRep role.
  private jobPlayersFullPayment = signal(false);
  private jobTeamsFullPayment = signal(false);

  // Role IDs (mirror ROLE_LABELS) for the role→baseline split.
  private static readonly PLAYER_ROLE_ID = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
  private static readonly CLUBREP_ROLE_ID = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';

  // ── Team Status KPIs (computed from tree data) ──
  teamStatusKpis = computed(() => {
    const scheduledIds = this.scheduledTeamIds();
    let waitlisted = 0;
    let nonWaitlisted = 0;
    let scheduled = 0;

    for (const league of this.rawTree()) {
      for (const ag of (league.children ?? []) as LadtTreeNodeDto[]) {
        const agName = (ag.name ?? '').toUpperCase();
        const isWaitlist = agName.startsWith('WAITLIST');
        const isDropped = agName === 'DROPPED TEAMS';

        for (const div of (ag.children ?? []) as LadtTreeNodeDto[]) {
          for (const team of (div.children ?? []) as LadtTreeNodeDto[]) {
            if (!team.active) continue;
            if (isWaitlist) waitlisted++;
            else if (!isDropped) nonWaitlisted++;
            if (scheduledIds.has(team.id)) scheduled++;
          }
        }
      }
    }

    return { waitlisted, nonWaitlisted, scheduled };
  });

  // Show club column only when there are 2+ distinct clubs in the job
  showClubColumn = computed(() => {
    const clubs = new Set<string>();
    for (const league of this.rawTree()) {
      for (const ag of (league.children ?? []) as LadtTreeNodeDto[]) {
        for (const div of (ag.children ?? []) as LadtTreeNodeDto[]) {
          for (const team of (div.children ?? []) as LadtTreeNodeDto[]) {
            if (team.clubName) clubs.add(team.clubName);
            if (clubs.size > 1) return true;
          }
        }
      }
    }
    return false;
  });

  // Expansion state (reactive)
  expandedIds = signal(new Set<string>());

  // Visible nodes: only nodes whose ancestors are all expanded
  visibleNodes = computed(() => {
    const all = this.flatNodes();
    const expanded = this.expandedIds();
    const phantomParent = this.phantomParentId();
    const result: LadtFlatNode[] = [];
    let skipLevel = -1;

    for (const node of all) {
      if (skipLevel >= 0 && node.level > skipLevel) continue;
      skipLevel = -1;
      result.push(node);
      if (node.expandable && !expanded.has(node.id)) {
        skipLevel = node.level;
      }
    }

    // Inject phantom node after the parent's last visible descendant
    if (phantomParent) {
      const parentIdx = result.findIndex(n => n.id === phantomParent);
      if (parentIdx >= 0) {
        const parentNode = result[parentIdx];
        const childLevel = parentNode.level + 1;
        let insertIdx = parentIdx + 1;
        while (insertIdx < result.length && result[insertIdx].level > parentNode.level) {
          insertIdx++;
        }
        result.splice(insertIdx, 0, {
          id: '__phantom__',
          parentId: phantomParent,
          name: '',
          level: childLevel,
          isLeaf: true,
          teamCount: 0,
          playerCount: 0,
          expandable: false,
          active: true,
          clubName: null,
          color: null,
          parentColor: parentNode.color,
          isSpecial: false,
          isPhantom: true
        });
      }
    }

    return result;
  });

  // Selection
  selectedNode = signal<LadtFlatNode | null>(null);
  selectedLevel = computed(() => this.selectedNode()?.level ?? -1);

  // Sibling grid state
  siblingData = signal<any[]>([]);
  siblingColumns = signal<LadtColumnDef[]>([]);
  siblingIdField = signal('');
  siblingLevelLabel = signal('');
  siblingLevelIcon = signal('bi-list');
  siblingParentParts = signal<ParentBreadcrumb[]>([]);
  isSiblingsLoading = signal(false);

  // Sibling division names (for duplicate validation in division-detail)
  siblingDivisionNames = computed(() => {
    if (this.selectedLevel() !== 2) return [];
    return this.siblingData()
      .map((d: any) => d.divName as string)
      .filter(Boolean);
  });

  // Delete confirmation dialog
  showDeleteConfirm = signal(false);
  deleteTargetNode = signal<LadtFlatNode | null>(null);
  deleteDialogTitle = computed(() => {
    const node = this.deleteTargetNode();
    if (!node) return 'Confirm';
    return node.level === 3 ? 'Remove Team' : `Delete ${this.getLevelLabel(node.level)}`;
  });
  deleteDialogMessage = computed(() => {
    const node = this.deleteTargetNode();
    if (!node) return '';
    if (node.level === 3) return `Remove team "${node.name}"? If the team has no players, payments, or schedule history it will be permanently deleted. Otherwise it will be moved to Dropped Teams and deactivated.`;
    return `Delete ${this.getLevelLabel(node.level)} "${node.name}"?`;
  });
  deleteDialogConfirmLabel = computed(() => this.deleteTargetNode()?.level === 3 ? 'Remove' : 'Delete');

  // Fly-in detail panel
  isDetailOpen = signal(false);
  detailNode = signal<LadtFlatNode | null>(null);

  // Unsaved-changes guard: a pending close/sibling-jump that's waiting on the
  // "discard changes?" confirm. null = no pending action (dialog hidden).
  pendingNav = signal<{ type: 'close' } | { type: 'sibling'; id: string } | null>(null);

  // ── Fly-in sibling navigation (dropdown + ↑/↓ keys) ──
  // Siblings = same parent + same level, in tree order (matches the left panel).
  flyinSiblings = computed(() => {
    const node = this.detailNode();
    if (!node) return [];
    return this.flatNodes().filter(n => n.level === node.level && n.parentId === node.parentId);
  });
  flyinIndex = computed(() => {
    const node = this.detailNode();
    if (!node) return -1;
    return this.flyinSiblings().findIndex(n => n.id === node.id);
  });
  canFlyinPrev = computed(() => this.flyinIndex() > 0);
  canFlyinNext = computed(() => {
    const i = this.flyinIndex();
    return i >= 0 && i < this.flyinSiblings().length - 1;
  });

  // Actions dropdown
  actionsOpen = signal(false);

  // Age-group color picker (tree dot → popover)
  colorPickerAgId = signal<string | null>(null);
  readonly colorOptions = AGEGROUP_COLORS;

  // Mobile drawer
  drawerOpen = signal(false);

  // Inline creation (phantom node)
  phantomParentId = signal<string | null>(null);
  private shouldFocusPhantom = false;

  // CdkTree accessors
  readonly levelAccessor = (node: LadtFlatNode) => node.level;
  readonly trackById = (_: number, node: LadtFlatNode) => node.id;

  hasChild = (_: number, node: LadtFlatNode) => node.expandable && !node.isPhantom;

  ngOnInit(): void {
    this.loadTree();
  }

  ngAfterViewChecked(): void {
    if (this.shouldFocusPhantom) {
      const input = document.querySelector('.phantom-input') as HTMLInputElement;
      if (input) {
        input.focus();
        this.shouldFocusPhantom = false;
      }
    }
  }

  loadTree(selectId?: string, openDetailAfter = false): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.ladtService.getTree().subscribe({
      next: (root) => {
        this.rawTree.set(root.leagues as LadtTreeNodeDto[]);
        this.totalTeams.set(root.totalTeams);
        this.totalPlayers.set(root.totalPlayers);
        this.scheduledTeamIds.set(new Set(root.scheduledTeamIds ?? []));
        this.jobPlayersFullPayment.set(root.bPlayersFullPaymentRequired);
        this.jobTeamsFullPayment.set(root.bTeamsFullPaymentRequired);

        const flat = this.flattenTree(root.leagues as LadtTreeNodeDto[]);
        this.flatNodes.set(flat);

        // First load: show leagues expanded (age groups visible), auto-select the first one
        if (this.expandedIds().size === 0) {
          this.collapseAll();
          const firstAgeGroup = flat.find(n => n.level === 1);
          if (firstAgeGroup) this.selectNode(firstAgeGroup);
        }

        this.isLoading.set(false);

        // Refresh agegroup badge counts from updated tree data
        this.refreshAgegroupBadges(flat);

        // After adding a child: expand ancestors and select the new node
        if (selectId) {
          const newNode = flat.find(n => n.id === selectId);
          if (newNode) {
            this.expandAncestors(newNode, flat);
            this.selectNode(newNode);
            if (openDetailAfter) {
              this.openDetail(selectId);
            }
          }
        }
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load tree data');
        this.isLoading.set(false);
      }
    });
  }

  // ── Tree operations ──

  private flattenTree(nodes: LadtTreeNodeDto[]): LadtFlatNode[] {
    const result: LadtFlatNode[] = [];
    const recurse = (items: LadtTreeNodeDto[], inheritedColor: string | null = null) => {
      for (const node of items) {
        let children = (node.children ?? []) as LadtTreeNodeDto[];

        // Sort age groups: regular alpha first, then specials (Dropped Teams, WAITLIST*)
        if (node.level === 0 && children.length > 0) {
          children = [...children].sort((a, b) => {
            const aSpecial = this.isSpecialAgegroup(a.name);
            const bSpecial = this.isSpecialAgegroup(b.name);
            if (aSpecial !== bSpecial) return aSpecial ? 1 : -1;
            return a.name.localeCompare(b.name);
          });
        }

        // Sort divisions: "Unassigned" last, then alpha
        if (node.level === 1 && children.length > 0) {
          children = [...children].sort((a, b) => {
            const aUnassigned = a.name.toUpperCase() === 'UNASSIGNED';
            const bUnassigned = b.name.toUpperCase() === 'UNASSIGNED';
            if (aUnassigned !== bUnassigned) return aUnassigned ? 1 : -1;
            return a.name.localeCompare(b.name);
          });
        }

        const nodeColor = node.color ?? null;
        result.push({
          id: node.id,
          parentId: node.parentId ?? null,
          name: node.name,
          level: node.level,
          isLeaf: node.isLeaf,
          teamCount: node.teamCount,
          playerCount: node.playerCount,
          expandable: children.length > 0,
          active: node.active,
          clubName: node.clubName ?? null,
          color: nodeColor,
          parentColor: inheritedColor,
          isSpecial: (node.level === 1 && this.isSpecialAgegroup(node.name)) ||
                     (node.level === 2 && node.name.toUpperCase() === 'UNASSIGNED')
        });
        if (children.length > 0) {
          recurse(children, nodeColor ?? inheritedColor);
        }
      }
    };
    recurse(nodes);
    return result;
  }

  private isSpecialAgegroup(name: string): boolean {
    const upper = name.toUpperCase();
    return upper === 'DROPPED TEAMS' || upper.startsWith('WAITLIST');
  }

  isNodeExpanded(node: LadtFlatNode): boolean {
    return this.expandedIds().has(node.id);
  }

  expandAll(): void {
    this.isTreeBusy.set(true);
    // Yield to the browser so the spinner paints before the expensive expansion
    // re-renders the tree. Double-rAF gives the paint a full frame.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      const next = new Set<string>();
      for (const n of this.flatNodes()) {
        if (n.expandable) next.add(n.id);
      }
      this.expandedIds.set(next);
      this.isTreeBusy.set(false);
    }));
  }

  collapseAll(): void {
    const next = new Set<string>();
    for (const n of this.flatNodes()) {
      if (n.level === 0 && n.expandable) next.add(n.id);
    }
    this.expandedIds.set(next);
  }

  toggleNode(node: LadtFlatNode): void {
    this.expandedIds.update(ids => {
      const next = new Set(ids);
      if (next.has(node.id)) {
        next.delete(node.id);
      } else {
        next.add(node.id);
      }
      return next;
    });
  }

  // ── Age-group color picker ──

  /** Toggle the color popover for an age group. stopPropagation keeps the row
   * click (selectNode) and the document:click closer from firing on the dot. */
  openColorPicker(agId: string, e: MouseEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.colorPickerAgId.set(this.colorPickerAgId() === agId ? null : agId);
  }

  /** Persist the chosen color, then patch the age group's own dot AND its
   * divisions' inherited (parent) color in place so the subtree recolors live. */
  selectAgegroupColor(agId: string, color: string | null): void {
    const value = color?.toUpperCase() ?? null;
    this.colorPickerAgId.set(null);

    this.ladtService.updateAgegroupColor(agId, value).subscribe({
      next: () => {
        this.flatNodes.update(nodes => nodes.map(n => {
          if (n.id === agId) return { ...n, color: value };
          if (n.parentId === agId) return { ...n, parentColor: value };
          return n;
        }));
        // Keep the selected reference consistent if it's the recolored node
        const sel = this.selectedNode();
        if (sel?.id === agId) this.selectedNode.set({ ...sel, color: value });
      },
      error: (err) => this.errorMessage.set(err.error?.message || 'Failed to update color')
    });
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.colorPickerAgId()) this.colorPickerAgId.set(null);
  }

  // ── Selection ──

  selectNode(node: LadtFlatNode): void {
    this.selectedNode.set(node);
    this.drawerOpen.set(false);
    this.loadSiblings(node);
  }

  // ── Level labels ──

  getLevelLabel(level: number): string {
    switch (level) {
      case 0: return 'League';
      case 1: return 'Age Group';
      case 2: return 'Division';
      case 3: return 'Team';
      default: return '';
    }
  }

  getLevelIcon(level: number): string {
    switch (level) {
      case 0: return 'bi-trophy';
      case 1: return 'bi-people';
      case 2: return 'bi-grid-3x3-gap';
      case 3: return 'bi-person-badge';
      default: return 'bi-circle';
    }
  }

  // ── Inline Creation (Phantom Node) ──

  startAdd(parentId: string): void {
    this.ensureExpanded(parentId);
    this.phantomParentId.set(parentId);
    this.shouldFocusPhantom = true;
  }

  commitPhantom(name: string): void {
    const parentId = this.phantomParentId();
    if (!parentId) return;

    const parentNode = this.flatNodes().find(n => n.id === parentId);
    if (!parentNode) return;

    const trimmedName = name.trim();
    const nameArg = trimmedName || undefined;

    let stub$: Observable<string>;
    if (parentNode.level === 0) {
      stub$ = this.ladtService.addStubAgegroup(parentId, nameArg);
    } else if (parentNode.level === 1) {
      stub$ = this.ladtService.addStubDivision(parentId, nameArg);
    } else {
      stub$ = this.ladtService.addStubTeam(parentId, nameArg);
    }

    this.phantomParentId.set(null);

    stub$.subscribe({
      next: (newId) => {
        this.ensureExpanded(parentId);
        this.loadTree(newId);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to create entity');
      }
    });
  }

  cancelPhantom(): void {
    if (this.phantomParentId()) {
      this.phantomParentId.set(null);
    }
  }

  private ensureExpanded(nodeId: string): void {
    this.expandedIds.update(ids => {
      const next = new Set(ids);
      next.add(nodeId);
      return next;
    });
  }

  private expandAncestors(node: LadtFlatNode, flat: LadtFlatNode[]): void {
    this.expandedIds.update(ids => {
      const next = new Set(ids);
      let current: LadtFlatNode | undefined = node;
      while (current?.parentId) {
        next.add(current.parentId);
        current = flat.find(n => n.id === current!.parentId);
      }
      return next;
    });
  }

  // ── Delete ──

  /** Frontend guard: can this node be removed via the tree "-" button? */
  canDelete(node: LadtFlatNode): boolean {
    // Leagues (level 0) are never deletable from the tree
    if (node.level === 0) return false;
    // "Unassigned" divisions are protected
    if (node.level === 2 && node.isSpecial) return false;
    // Agegroups & divisions: blocked if any teams exist underneath
    if (node.level <= 2 && node.teamCount > 0) return false;
    // Teams: always show "-" (backend guards scheduled teams, drop handles players)
    return true;
  }

  confirmDelete(node: LadtFlatNode): void {
    this.deleteTargetNode.set(node);
    this.showDeleteConfirm.set(true);
  }

  onDeleteConfirmed(): void {
    const node = this.deleteTargetNode();
    if (!node) return;
    this.showDeleteConfirm.set(false);
    this.deleteTargetNode.set(null);

    // Teams are "dropped" (moved to Dropped Teams), not deleted
    if (node.level === 3) {
      this.ladtService.dropTeam(node.id).subscribe({
        next: (result) => {
          if (this.selectedNode()?.id === node.id) {
            this.selectedNode.set(null);
            this.siblingData.set([]);
          }
          this.loadTree();
          this.errorMessage.set(result.message);
        },
        error: (err) => this.errorMessage.set(err.error?.message || 'Failed to drop team')
      });
      return;
    }

    const label = this.getLevelLabel(node.level);
    let delete$: Observable<void>;
    if (node.level === 1) delete$ = this.ladtService.deleteAgegroup(node.id);
    else delete$ = this.ladtService.deleteDivision(node.id);

    delete$.subscribe({
      next: () => {
        if (this.selectedNode()?.id === node.id) {
          this.selectedNode.set(null);
          this.siblingData.set([]);
        }
        this.loadTree();
      },
      error: (err) => this.errorMessage.set(err.error?.message || `Failed to delete ${label}`)
    });
  }

  onDeleteCancelled(): void {
    this.showDeleteConfirm.set(false);
    this.deleteTargetNode.set(null);
  }


  // ── Sibling grid ──

  private loadSiblings(node: LadtFlatNode): void {
    const level = node.level;
    let cols = COLUMNS_BY_LEVEL[level];
    // Hide club column at team level when there's only 0-1 distinct clubs
    if (level === 3 && !this.showClubColumn()) {
      cols = cols.filter(c => c.field !== 'clubName');
    }
    this.siblingColumns.set(cols);
    this.siblingIdField.set(ID_FIELD_BY_LEVEL[level]);
    this.siblingLevelLabel.set(this.getLevelLabel(level));
    this.siblingLevelIcon.set(this.getLevelIcon(level));
    this.siblingParentParts.set(this.getParentParts(node));
    this.isSiblingsLoading.set(true);
    this.siblingData.set([]);

    let fetch$: Observable<any[]>;
    if (level === 0) fetch$ = this.ladtService.getLeagueSiblings();
    else if (level === 1) fetch$ = this.ladtService.getAgegroupSiblings(node.parentId!);
    else if (level === 2) fetch$ = this.ladtService.getDivisionSiblings(node.parentId!);
    else fetch$ = this.ladtService.getTeamSiblings(node.parentId!);

    // For levels that show fees, load fees in parallel
    const needsFees = level === 0 || level === 1 || level === 2 || level === 3;
    const feesLoaded = this.jobFees().length > 0;

    const combined$ = needsFees && !feesLoaded
      ? forkJoin({ data: fetch$, fees: this.ladtService.getJobFees() })
      : fetch$;

    (combined$ as Observable<any>).subscribe({
      next: (result: any) => {
        const data: any[] = result.data ?? result;
        if (result.fees) {
          this.jobFees.set(result.fees);
        }

        // Enrich leagues with child agegroup count for drill-down badge
        if (level === 0) {
          const treeNodes = this.flatNodes();
          for (const row of data) {
            row.agegroupCount = treeNodes.filter(n => n.parentId === row.leagueId).length;
          }
        }

        // Enrich agegroups with tree counts + special flag
        if (level === 1) {
          const treeNodes = this.flatNodes();
          for (const row of data) {
            const upper = (row.agegroupName ?? '').toUpperCase();
            // "Special" = holding buckets whose name contains WAITLIST or DROPPED.
            // Styled distinctly, no action menu, and always sorted to the bottom of
            // the age-group grid.
            row._isSpecial = upper.includes('WAITLIST') || upper.includes('DROPPED');
            const treeNode = treeNodes.find(n => n.id === row.agegroupId);
            if (treeNode) {
              row.teamCount = treeNode.teamCount;
              row.playerCount = treeNode.playerCount;
              row.divisionCount = treeNodes.filter(n => n.parentId === treeNode.id).length;
            }
          }
        }

        // Enrich divisions with parent agegroup ID + team count for navigation
        if (level === 2) {
          const treeNodes = this.flatNodes();
          for (const row of data) {
            const tn = treeNodes.find(n => n.id === row.divId);
            if (tn) {
              row._parentAgId = tn.parentId;
              row.teamCount = tn.teamCount;
            }
          }
        }

        // Enrich teams with parent division ID for up-navigation
        if (level === 3) {
          const treeNodes = this.flatNodes();
          for (const row of data) {
            const tn = treeNodes.find(n => n.id === row.teamId);
            if (tn) row._parentDivId = tn.parentId;
          }
        }

        // Enrich with fee pills
        if (needsFees) {
          this.enrichWithFees(data, level);
        }

        this.siblingData.set(data);
        this.isSiblingsLoading.set(false);
      },
      error: (err: any) => {
        this.errorMessage.set(err.error?.message || 'Failed to load siblings');
        this.isSiblingsLoading.set(false);
      }
    });
  }

  /** Re-stamp teamCount/playerCount on agegroup grid rows from fresh tree data */
  private refreshAgegroupBadges(treeNodes: LadtFlatNode[]): void {
    const selected = this.selectedNode();
    if (!selected || selected.level !== 1) return;

    const data = this.siblingData();
    if (data.length === 0) return;

    let changed = false;
    for (const row of data) {
      const tn = treeNodes.find(n => n.id === row.agegroupId);
      if (tn && (row.teamCount !== tn.teamCount || row.playerCount !== tn.playerCount)) {
        row.teamCount = tn.teamCount;
        row.playerCount = tn.playerCount;
        changed = true;
      }
    }
    if (changed) this.siblingData.set([...data]);
  }

  private static readonly ROLE_LABELS: Record<string, string> = {
    'DAC0C570-94AA-4A88-8D73-6034F1F72F3A': 'Player',
    '6A26171F-4D94-4928-94FA-2FEFD42C3C3E': 'ClubRep',
  };

  /**
   * Resolve the Deposit/BalanceDue chip and the Early Bird / Late Fee columns
   * for a grid row from cached jobFees.
   *
   * The base fee (deposit/balanceDue) cascades Job → League → Agegroup → Team,
   * most-specific tier wins as a unit. Each modifier type (EarlyBird, LateFee)
   * runs the SAME cascade INDEPENDENTLY — most-specific tier that actually has a
   * modifier of that type wins, tiers do NOT sum (mirrors the backend's
   * FeeRepository.GetActiveModifiersForCascadeAsync). Job tier is not a modifier
   * source. A modifier's source can therefore differ from the base fee's source.
   */
  /** Job-level full-payment baseline for a role — the resolver's fallback when no override exists. */
  private jobBaselineFor(roleId: string): boolean {
    return roleId === LadtEditorComponent.CLUBREP_ROLE_ID
      ? this.jobTeamsFullPayment()
      : this.jobPlayersFullPayment();
  }

  private buildFeeData(scopeId: string, scopeType: 'league' | 'agegroup' | 'team'): {
    fees: any[]; earlyBird: any[]; lateFee: any[]; phase: any[];
  } {
    const fees = this.jobFees();
    if (!fees.length) return { fees: [], earlyBird: [], lateFee: [], phase: [] };

    interface ModWin { amount: number; source: 'league' | 'agegroup' | 'team'; active: boolean; }
    interface PhaseWin { value: boolean; source: 'league' | 'agegroup' | 'team'; }
    interface FeeEntry {
      deposit: number | null;
      balanceDue: number | null;
      source: 'job' | 'league' | 'agegroup' | 'team';
      earlyBird: ModWin | null;
      lateFee: ModWin | null;
      phase: PhaseWin | null;
    }

    const roleMap = new Map<string, FeeEntry>();
    const now = new Date();

    const upsert = (roleId: string): FeeEntry => {
      let e = roleMap.get(roleId);
      if (!e) {
        e = { deposit: null, balanceDue: null, source: 'job', earlyBird: null, lateFee: null, phase: null };
        roleMap.set(roleId, e);
      }
      return e;
    };

    // Sum a single fee row's modifiers of one type into one winner candidate.
    // Multiple active windows of the same type at one tier stack (mirrors backend).
    const modForType = (f: JobFeeDto, type: string): { amount: number; active: boolean } | null => {
      const mods = (f.modifiers ?? []).filter(m => m.modifierType === type);
      if (!mods.length) return null;
      const amount = mods.reduce((s, m) => s + m.amount, 0);
      const active = mods.some(m =>
        (!m.startDate || new Date(m.startDate) <= now) && (!m.endDate || new Date(m.endDate) >= now));
      return { amount, active };
    };

    // Overwrite a modifier winner only when this tier's row actually carries that
    // type — so a more-specific base-fee row without modifiers can't wipe an
    // inherited modifier from a less-specific tier.
    const applyMods = (e: FeeEntry, f: JobFeeDto, src: 'league' | 'agegroup' | 'team') => {
      const eb = modForType(f, 'EarlyBird');
      if (eb) e.earlyBird = { ...eb, source: src };
      const lf = modForType(f, 'LateFee');
      if (lf) e.lateFee = { ...lf, source: src };
    };

    // Full-payment phase cascades like a modifier — the most-specific tier that
    // sets bFullPaymentRequired wins. Floor is League (the job baseline is not
    // surfaced as a phase source here). null = no override at/above this scope.
    const applyPhase = (e: FeeEntry, f: JobFeeDto, src: 'league' | 'agegroup' | 'team') => {
      if (f.bFullPaymentRequired != null) e.phase = { value: f.bFullPaymentRequired, source: src };
    };

    // Base fee cascades per field, most-specific NON-NULL wins (mirrors backend
    // FeeRepository.GetResolvedFeeAsync's `??=`). Update a field + its source ONLY
    // when this tier supplies a value — otherwise a modifier-only row (e.g. a
    // team/agegroup late fee with no Deposit/BalanceDue) would blank the inherited
    // base and the grid would show $0.
    const applyBase = (e: FeeEntry, f: JobFeeDto, src: 'job' | 'league' | 'agegroup' | 'team') => {
      if (f.deposit != null) { e.deposit = f.deposit; e.source = src; }
      if (f.balanceDue != null) { e.balanceDue = f.balanceDue; e.source = src; }
    };

    // Resolve the agegroup + league in scope. Team scope walks up through its
    // division to the agegroup; agegroup scope IS the agegroup; league scope has
    // no agegroup (only the job→league base + league-tier modifiers apply).
    let agId: string | undefined;
    let leagueId: string | undefined;
    if (scopeType === 'team') {
      const teamNode = this.flatNodes().find(n => n.id === scopeId);
      const agNode = teamNode ? this.flatNodes().find(n => n.id === teamNode.parentId) : null;
      agId = agNode?.level === 2 ? agNode.parentId ?? undefined : agNode?.id;
      leagueId = agId ? this.flatNodes().find(n => n.id === agId)?.parentId ?? undefined : undefined;
    } else if (scopeType === 'agegroup') {
      agId = scopeId;
      leagueId = this.flatNodes().find(n => n.id === agId)?.parentId ?? undefined;
    } else {
      // league scope
      agId = undefined;
      leagueId = scopeId;
    }

    // Layer 1: Job-level base defaults (Player/ClubRep no longer seed here; exclude
    // league-scoped rows so they don't masquerade as job-level). Job tier is NOT a
    // modifier source.
    for (const f of fees) {
      if (!f.agegroupId && !f.teamId && !f.leagueId && f.roleId) {
        applyBase(upsert(f.roleId), f, 'job');
      }
    }

    // Layer 2: League-level (top tier of base cascade AND modifier cascade)
    if (leagueId) {
      for (const f of fees) {
        if (f.leagueId === leagueId && !f.agegroupId && !f.teamId && f.roleId) {
          const e = upsert(f.roleId);
          applyBase(e, f, 'league');
          applyMods(e, f, 'league');
          applyPhase(e, f, 'league');
        }
      }
    }

    // Layer 3: Agegroup-level
    if (agId) {
      for (const f of fees) {
        if (f.agegroupId === agId && !f.teamId && f.roleId) {
          const e = upsert(f.roleId);
          applyBase(e, f, 'agegroup');
          applyMods(e, f, 'agegroup');
          applyPhase(e, f, 'agegroup');
        }
      }
    }

    // Layer 4: Team-level (only for team scope)
    if (scopeType === 'team') {
      for (const f of fees) {
        if (f.teamId === scopeId && f.roleId) {
          const e = upsert(f.roleId);
          applyBase(e, f, 'team');
          applyMods(e, f, 'team');
          applyPhase(e, f, 'team');
        }
      }
    }

    // "inherited" = the winning tier is above this grid's own scope. A value
    // sourced from this grid's own tier is "set" here; anything higher is inherited.
    const isInherited = (src: string) => src !== scopeType;

    const feesOut: any[] = [];
    const earlyBird: any[] = [];
    const lateFee: any[] = [];
    const phase: any[] = [];

    for (const [roleId, e] of roleMap.entries()) {
      const roleLabel = LadtEditorComponent.ROLE_LABELS[roleId] ?? roleId.substring(0, 6);
      feesOut.push({
        roleId, roleLabel,
        deposit: e.deposit, balanceDue: e.balanceDue,
        inherited: isInherited(e.source), source: e.source,
      });
      if (e.earlyBird) {
        earlyBird.push({
          roleId, roleLabel, amount: e.earlyBird.amount,
          source: e.earlyBird.source, active: e.earlyBird.active,
          inherited: isInherited(e.earlyBird.source),
        });
      }
      if (e.lateFee) {
        lateFee.push({
          roleId, roleLabel, amount: e.lateFee.amount,
          source: e.lateFee.source, active: e.lateFee.active,
          inherited: isInherited(e.lateFee.source),
        });
      }
      // The phase pill carries two ORTHOGONAL facts:
      //   value (WHAT) — Single / Deposit / PIF, the effective phase.
      //   source (WHERE) — which tier set the phase. A phase override (applyPhase only ever
      //     sets league/agegroup/team) names its tier; with NO override the phase is the job
      //     baseline, so the source is the JOB — not where the base fee happens to live. The
      //     fee's location is a separate axis (the Fees column); conflating them here misreported
      //     where the phase came from.
      const phaseSource = e.phase?.source ?? 'job';
      phase.push({
        roleId, roleLabel,
        fullPayment: e.phase?.value ?? this.jobBaselineFor(roleId),
        source: phaseSource,
        inherited: isInherited(phaseSource), // job (and any higher-than-scope tier) → "from X"
        // The phase choice only MEANS something when the fee has BOTH a deposit and a balance to
        // split. A balance-only (or deposit-only) fee is a single payment — the phase is inert
        // (both settings stamp the same FeeBase) — so the pill reads "Single", no phase, no badge.
        twoPhase: (e.deposit ?? 0) > 0 && (e.balanceDue ?? 0) > 0,
      });
    }

    return { fees: feesOut, earlyBird, lateFee, phase };
  }

  /** Enrich grid rows with _fees / _earlyBird / _lateFee / _phase column data */
  private enrichWithFees(data: any[], level: number): void {
    const assign = (row: any, scopeId: string, scopeType: 'league' | 'agegroup' | 'team') => {
      const d = this.buildFeeData(scopeId, scopeType);
      row._fees = d.fees;
      row._earlyBird = d.earlyBird;
      row._lateFee = d.lateFee;
      row._phase = d.phase;
    };
    if (level === 0) {
      for (const row of data) assign(row, row.leagueId, 'league');
    } else if (level === 1) {
      for (const row of data) assign(row, row.agegroupId, 'agegroup');
    } else if (level === 3) {
      for (const row of data) assign(row, row.teamId, 'team');
    }
    // level 2 (Divisions): no fee columns — divisions aren't a scope in fees.JobFees.
  }

  private getParentParts(node: LadtFlatNode): ParentBreadcrumb[] {
    const parts: ParentBreadcrumb[] = [];
    let current = node;
    // Walk up the tree collecting ancestors
    while (current.parentId) {
      const parent = this.flatNodes().find(n => n.id === current.parentId);
      if (!parent) break;
      parts.unshift({ name: parent.name, level: parent.level, id: parent.id });
      current = parent;
    }
    return parts;
  }

  // ── Fly-in detail panel ──

  openDetail(id: string): void {
    const node = this.flatNodes().find(n => n.id === id);
    if (node) {
      this.selectedNode.set(node);
      this.detailNode.set(node);
      this.isDetailOpen.set(true);
    }
  }

  /** User-initiated close (X / backdrop / Esc). Guards unsaved edits first. */
  closeDetail(): void {
    if (this.editGuard.isDirty()) {
      this.pendingNav.set({ type: 'close' });
      return;
    }
    this.forceCloseDetail();
  }

  /** Close without the unsaved-changes guard — used after a save/clone/drop has
   *  already persisted (or been confirmed), so there's nothing to discard. */
  private forceCloseDetail(): void {
    this.pendingNav.set(null);
    this.isDetailOpen.set(false);
    this.detailNode.set(null);
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    // While the discard prompt is up, let the dialog own Escape (it cancels).
    if (this.pendingNav()) return;
    if (this.isDetailOpen()) this.closeDetail();
  }

  /** Jump the fly-in to a sibling by id (dropdown selection). */
  flyinNavigateTo(id: string): void {
    this.requestFlyinNav(this.flyinSiblings().find(n => n.id === id));
  }

  /** Step to the previous/next sibling (↑/↓ keys); clamps at the ends. */
  flyinNavigate(delta: number): void {
    this.requestFlyinNav(this.flyinSiblings()[this.flyinIndex() + delta]);
  }

  /** Guard a sibling jump on unsaved edits before swapping the panel. */
  private requestFlyinNav(target: LadtFlatNode | undefined): void {
    if (!target || target.id === this.detailNode()?.id) return;
    if (this.editGuard.isDirty()) {
      this.pendingNav.set({ type: 'sibling', id: target.id });
      return;
    }
    this.setFlyinNode(target);
  }

  /** "Discard" confirmed — carry out the pending close or sibling jump. */
  onDiscardConfirmed(): void {
    const nav = this.pendingNav();
    this.pendingNav.set(null);
    if (!nav) return;
    if (nav.type === 'close') {
      this.forceCloseDetail();
    } else {
      this.setFlyinNode(this.flyinSiblings().find(n => n.id === nav.id));
    }
  }

  /** "Keep editing" — dismiss the prompt, stay where we are. */
  onDiscardCancelled(): void {
    this.pendingNav.set(null);
  }

  /** Swap the fly-in to a sibling — panels reload via ngOnChanges; tree + grid stay in sync. */
  private setFlyinNode(target: LadtFlatNode | undefined): void {
    if (!target) return;
    this.detailNode.set(target);
    this.selectedNode.set(target);
  }

  @HostListener('document:keydown.arrowup', ['$event'])
  onArrowUpKey(e: Event): void {
    if (this.shouldHandleFlyinArrow(e)) this.flyinNavigate(-1);
  }

  @HostListener('document:keydown.arrowdown', ['$event'])
  onArrowDownKey(e: Event): void {
    if (this.shouldHandleFlyinArrow(e)) this.flyinNavigate(1);
  }

  /**
   * Gate keyboard nav: only when the fly-in is open AND focus isn't in an
   * editable field — so ↑/↓ still increment number inputs, move textarea
   * carets, and change selects. Consumes the event only when we handle it.
   */
  private shouldHandleFlyinArrow(e: Event): boolean {
    if (!this.isDetailOpen()) return false;
    const t = e.target as HTMLElement | null;
    const tag = t?.tagName;
    if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA' || t?.isContentEditable) {
      return false;
    }
    e.preventDefault();
    return true;
  }

  // ── Detail panel callbacks ──

  onDetailSaved(): void {
    const node = this.selectedNode();
    this.jobFees.set([]); // invalidate fee cache so grid reloads fresh
    this.forceCloseDetail();
    this.loadTree();
    if (node) this.loadSiblings(node);
  }

  onDetailCloned(newTeamId: string): void {
    // Reload tree and refocus on the clone — fly-in stays open on the new team.
    this.jobFees.set([]);
    this.forceCloseDetail();
    this.loadTree(newTeamId, /* openDetailAfter */ true);
  }

  onDetailDropped(): void {
    // Team moved to Dropped Teams. Mutate locally — no endpoint refresh.
    // Mirrors onDetailDeleted (SP-005 pattern). A natural refresh later
    // surfaces the team under the Dropped Teams agegroup.
    const dropped = this.selectedNode();
    this.jobFees.set([]);
    this.forceCloseDetail();

    if (!dropped) return;

    this.flatNodes.update(nodes => nodes.filter(n => n.id !== dropped.id));

    const idField = this.siblingIdField();
    if (idField) {
      this.siblingData.update(rows => rows.filter((r: any) => r[idField] !== dropped.id));
    }

    const remainingSiblingId = this.siblingIdField()
      ? (this.siblingData()[0] as any)?.[this.siblingIdField()] ?? null
      : null;
    const siblingNode = remainingSiblingId
      ? this.flatNodes().find(n => n.id === remainingSiblingId) ?? null
      : null;
    this.selectedNode.set(siblingNode);
  }

  onDetailDeleted(): void {
    const deleted = this.selectedNode();
    this.forceCloseDetail();

    if (!deleted) return;

    // Local removal — no endpoint refresh. Keeps the right-side grid mounted
    // on the deleted item's level by selecting the parent node.
    this.flatNodes.update(nodes => nodes.filter(n => n.id !== deleted.id));

    const idField = this.siblingIdField();
    if (idField) {
      this.siblingData.update(rows => rows.filter((r: any) => r[idField] !== deleted.id));
    }

    // Point selectedNode at a remaining sibling so the grid stays mounted
    // at the correct level with its existing columns. Falls back to null
    // (empty state) when the deleted item was the last sibling.
    const remainingSiblingId = this.siblingIdField()
      ? (this.siblingData()[0] as any)?.[this.siblingIdField()] ?? null
      : null;
    const siblingNode = remainingSiblingId
      ? this.flatNodes().find(n => n.id === remainingSiblingId) ?? null
      : null;
    this.selectedNode.set(siblingNode);
  }


  // ── Grid action column callbacks ──

  /** Arrow function so it can be passed as [canDeleteFn] without losing `this` */
  canDeleteRow = (row: any): boolean => {
    const selected = this.selectedNode();
    if (!selected) return false;
    const level = selected.level; // grid shows children of selected node's level
    // Agegroups (level 1 grid): can delete if no teams
    if (level === 1) return (row.teamCount ?? 0) === 0 && !row._isSpecial;
    // Divisions (level 2 grid): can delete if no teams and not Unassigned
    if (level === 2) {
      const name = (row.divName ?? '').toUpperCase();
      return (row.teamCount ?? 0) === 0 && name !== 'UNASSIGNED';
    }
    // Teams (level 3 grid): always deletable (backend handles soft delete)
    if (level === 3) return true;
    return false;
  };

  onGridDrillDown(id: string): void {
    // Find the node in the tree and select its first child
    const node = this.flatNodes().find(n => n.id === id);
    if (!node) return;

    // Expand this node in the tree
    this.expandedIds.update(ids => {
      const next = new Set(ids);
      next.add(node.id);
      return next;
    });

    // Find first child node
    const children = this.flatNodes().filter(n => n.parentId === id);
    if (children.length > 0) {
      this.selectNode(children[0]);
    }
  }

  onGridDelete(id: string): void {
    const node = this.flatNodes().find(n => n.id === id);
    if (node) {
      this.confirmDelete(node);
    }
  }

  onGridNavigate(nodeId: string): void {
    const node = this.flatNodes().find(n => n.id === nodeId);
    if (node) {
      this.selectNode(node);
    }
  }

  onGridAdd(): void {
    const selected = this.selectedNode();
    if (!selected) return;
    // The grid shows siblings of `selected`. Adding from the grid header
    // should create another sibling at the same level — so the phantom's
    // parent is the selected node's parent, not the selected node itself.
    if (!selected.parentId) return;
    this.startAdd(selected.parentId);
  }

  onGridCloneRow(row: any): void {
    if (row?.agegroupId && !row?.teamId && !row?.divId) {
      this.cloneAgegroupSource.set({
        agegroupId: row.agegroupId,
        agegroupName: row.agegroupName ?? ''
      });
      return;
    }
    if (!row?.teamId) return;
    this.cloneSource.set({
      teamId: row.teamId,
      teamName: row.teamName ?? '',
      hasClubRep: !!row.clubRepRegistrationId,
      clubName: row.clubName ?? null
    });
  }

  onCloneDialogCancelled(): void {
    this.cloneSource.set(null);
  }

  onCloneDialogCloned(newTeam: { teamId: string }): void {
    this.cloneSource.set(null);
    this.onDetailCloned(newTeam.teamId);
  }

  onCloneAgegroupDialogCancelled(): void {
    this.cloneAgegroupSource.set(null);
  }

  onCloneAgegroupDialogCloned(newAg: { agegroupId: string }): void {
    this.cloneAgegroupSource.set(null);
    this.onDetailCloned(newAg.agegroupId);
  }

  // ── Mobile ──

  toggleDrawer(): void {
    this.drawerOpen.set(!this.drawerOpen());
  }

  // ── Division Name Sync ──

  showSyncDialog = signal(false);
  syncThemeNames = signal<string[]>([]);
  syncPreviews = signal<DivisionNameSyncPreview[]>([]);
  syncLoading = signal(false);
  syncApplying = signal(false);
  syncResult = signal<string | null>(null);

  /** Whether at least one theme name has content */
  syncHasNames = computed(() => this.syncThemeNames().some(n => n.trim().length > 0));

  /** Whether any agegroups exist to theme */
  syncHasAgegroups = computed(() => this.syncPreviews().length > 0);

  openSyncDialog(): void {
    this.actionsOpen.set(false);
    this.syncLoading.set(true);
    this.syncResult.set(null);
    this.syncThemeNames.set(['']);
    this.showSyncDialog.set(true);

    // Fetch current state to show what exists now
    this.ladtService.previewDivisionNameSync([]).subscribe({
      next: (previews) => {
        this.syncPreviews.set(previews);
        this.syncLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load divisions');
        this.showSyncDialog.set(false);
        this.syncLoading.set(false);
      }
    });
  }

  closeSyncDialog(): void {
    this.showSyncDialog.set(false);
    this.syncPreviews.set([]);
    this.syncThemeNames.set([]);
    this.syncResult.set(null);
  }

  addThemeName(): void {
    this.syncThemeNames.update(names => [...names, '']);
  }

  updateThemeName(index: number, value: string): void {
    this.syncThemeNames.update(names => {
      const updated = [...names];
      updated[index] = value;
      return updated;
    });
  }

  removeThemeName(index: number): void {
    if (this.syncThemeNames().length <= 1) return;
    this.syncThemeNames.update(names => names.filter((_, i) => i !== index));
    this.refreshSyncPreview();
  }

  onThemeNameBlur(): void {
    this.refreshSyncPreview();
  }

  private refreshSyncPreview(): void {
    this.syncLoading.set(true);
    this.ladtService.previewDivisionNameSync(this.syncThemeNames()).subscribe({
      next: (previews) => {
        this.syncPreviews.set(previews);
        this.syncLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to refresh preview');
        this.syncLoading.set(false);
      }
    });
  }

  applySyncNames(): void {
    this.syncApplying.set(true);
    this.ladtService.applyDivisionNameSync(this.syncThemeNames()).subscribe({
      next: (result) => {
        this.syncApplying.set(false);
        const parts: string[] = [];
        if (result.divisionsRenamed > 0) parts.push(`${result.divisionsRenamed} renamed`);
        if (result.divisionsCreated > 0) parts.push(`${result.divisionsCreated} created`);
        if (result.divisionsDeleted > 0) parts.push(`${result.divisionsDeleted} removed`);
        const summary = parts.length > 0 ? parts.join(', ') : 'No changes needed';
        if (result.errors.length > 0) {
          this.syncResult.set(`${summary}. Errors: ${result.errors.join(', ')}`);
        } else {
          this.syncResult.set(`Done! ${summary}.`);
        }
        this.loadTree();
      },
      error: (err) => {
        this.syncApplying.set(false);
        this.syncResult.set(err.error?.message || 'Failed to apply division name sync');
      }
    });
  }
}
