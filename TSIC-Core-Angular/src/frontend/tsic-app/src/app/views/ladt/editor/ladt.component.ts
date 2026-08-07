import { ChangeDetectionStrategy, Component, OnInit, AfterViewChecked, HostListener, signal, computed, inject, DestroyRef } from '@angular/core';
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
import { ResizablePanelDirective } from '@shared-ui/directives/resizable-panel.directive';
import { FormsModule } from '@angular/forms';
import {
  COLUMNS_BY_LEVEL, MOBILE_COLUMNS_BY_LEVEL, ID_FIELD_BY_LEVEL,
  type LadtColumnDef
} from './configs/ladt-grid-columns';
import type { ParentBreadcrumb } from './components/ladt-sibling-grid.component';
import type {
  LadtTreeNodeDto, DivisionNameSyncPreview, JobFeeDto,
  LadtFeeResolutionMapDto, LadtFeeNodeResolutionDto, LadtFeeRoleResolutionDto
} from '../../../core/api';
import type { DescendantOverrideInfo, PhaseContext } from './components/fee-card.component';
import { RoleIds } from '@infrastructure/constants/roles.constants';
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
    TsicDialogComponent,
    ResizablePanelDirective
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

  // Raw JobFees rows, retained for ONE purpose: feeRolesPresent (fee-card disclosure).
  // Job-tier-only rows are in no cascade chain, so the resolution map cannot see them —
  // but a role with such rows must still show its fee card. Everything else (grids AND
  // fly-in context) resolves from the server map below.
  private jobFees = signal<JobFeeDto[]>([]);

  // Canonical fee-resolution map for the sibling grids (server-resolved: amounts/phase
  // + source tiers + below-summaries). null = NOT LOADED — that null is load-bearing:
  // an unloaded map must render as the "—" placeholder, never as a verified all-clear.
  private feeMap = signal<LadtFeeResolutionMapDto | null>(null);
  private feeMapIndex = computed<Map<string, LadtFeeNodeResolutionDto> | null>(() => {
    const m = this.feeMap();
    return m ? new Map((m.nodes ?? []).map(n => [n.nodeId, n])) : null;
  });

  // Role IDs (mirror ROLE_LABELS) for feeRolesPresent.
  private static readonly PLAYER_ROLE_ID = RoleIds.Player;
  private static readonly CLUBREP_ROLE_ID = RoleIds.ClubRep;

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

  /**
   * True below the 768px breakpoint — the sibling grid then uses the mobile column sets.
   *
   * Live, not read once: a director who rotates a phone mid-edit crosses the breakpoint,
   * and a stale column set would leave the grid panning horizontally with no way back.
   * `matchMedia` rather than a resize listener because the browser only fires it when the
   * breakpoint is actually CROSSED, not on every pixel of a resize drag.
   *
   * This is the whole viewport dependency for the grid — `ladt-sibling-grid` itself has
   * none. Above 768px `isNarrow()` is false and every downstream value is byte-identical
   * to what it was before this existed.
   */
  readonly isNarrow = signal(false);

  constructor() {
    const mql = window.matchMedia('(max-width: 767.98px)');
    this.isNarrow.set(mql.matches);
    const onChange = (e: MediaQueryListEvent) => this.isNarrow.set(e.matches);
    mql.addEventListener('change', onChange);
    inject(DestroyRef).onDestroy(() => mql.removeEventListener('change', onChange));
  }

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
  /** Level whose siblings the grid is showing; -1 before the first selection. */
  siblingLevel = signal(-1);

  /**
   * Derived, not set by `loadSiblings()`, so that crossing the 768px breakpoint (rotating a
   * phone) re-picks the column set instead of leaving whatever was chosen at load time.
   */
  siblingColumns = computed<LadtColumnDef[]>(() => {
    const level = this.siblingLevel();
    if (level < 0) return [];

    const narrow = this.isNarrow();
    let cols = narrow ? MOBILE_COLUMNS_BY_LEVEL[level] : COLUMNS_BY_LEVEL[level];

    // Hide the club column at team level when the job has 0-1 distinct clubs — a column
    // repeating one value, or none, is dead width. Desktop only: the mobile set's club
    // field IS its identity column, and that cell already promotes the team name when
    // there is no club, so filtering it here would leave teams with no name at all.
    if (level === 3 && !narrow && !this.showClubColumn()) {
      cols = cols.filter(c => c.field !== 'clubName');
    }
    return cols;
  });
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
    // Columns are derived from this (see the siblingColumns computed) so the set tracks
    // the viewport, not just the moment of selection.
    this.siblingLevel.set(level);
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

    // For levels that show fees, load in parallel whatever fee caches are cold:
    // jobFees (fee-card role disclosure only) and the resolution map (grid pills
    // + all fly-in context).
    const needsFees = level === 0 || level === 1 || level === 2 || level === 3;
    const sources: Record<string, Observable<any>> = { data: fetch$ };
    if (needsFees && this.jobFees().length === 0) sources['fees'] = this.ladtService.getJobFees();
    if (needsFees && this.feeMap() === null) sources['map'] = this.ladtService.getFeeResolutionMap();

    const combined$ = Object.keys(sources).length > 1 ? forkJoin(sources) : fetch$;

    (combined$ as Observable<any>).subscribe({
      next: (result: any) => {
        const data: any[] = result.data ?? result;
        if (result.fees) {
          this.jobFees.set(result.fees);
        }
        if (result.map) {
          this.feeMap.set(result.map);
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
    [RoleIds.Player]: 'Player',
    [RoleIds.ClubRep]: 'ClubRep',
  };

  /**
   * Phase resolved from the tiers ABOVE a detail node's own scope — what governs the node when
   * its "Use league/age group setting" radio is chosen. Team → its age group's effective phase
   * (agegroup → league → job); age group → its league's. Read from the same server map as the
   * grid pills, and deliberately EXCLUDING the node's own stamp so the fly-in's
   * "Currently: …" line stays honest mid-edit (while an unsaved radio change is pending).
   */
  /** Memoized for the open fly-in: a computed keeps the object identity stable across change
   *  detection (a template method call would mint a fresh object every CD cycle, re-firing the
   *  detail components' input change handling forever). Recomputes only when the detail node
   *  or the cached fees actually change. */
  readonly detailAncestorPhase = computed(() => {
    const node = this.detailNode();
    return node ? this.ancestorPhaseFor(node) : null;
  });

  /**
   * Card-level disclosure: which fee roles have ANY JobFees row in this job (amounts,
   * modifiers, or phase stamps — a row is a row). A role with no rows collapses its fee
   * card to an "Add … fees" link in the detail fly-ins; a role with rows always shows.
   * Memoized for input-binding identity stability (same reason as detailAncestorPhase).
   */
  readonly feeRolesPresent = computed(() => {
    const fees = this.jobFees();
    return {
      player: fees.some(f => f.roleId === LadtEditorComponent.PLAYER_ROLE_ID),
      clubRep: fees.some(f => f.roleId === LadtEditorComponent.CLUBREP_ROLE_ID)
    };
  });

  /**
   * Phase-relevance context for the open fly-in's fee cards, per role: can the payment
   * phase engage anywhere this card's setting reaches? Deposit search covers the resolved
   * cascade AT the scope (own row ?? governing tiers) plus every tier BELOW it (a league/
   * age-group phase stamp governs lower scopes whose deposits live elsewhere). Also carries
   * the resolved amounts so the collapsed single-payment note can quote the real charge.
   * Memoized — see detailAncestorPhase.
   */
  readonly detailPhaseContext = computed(() => {
    const node = this.detailNode();
    if (!node || node.level === 2) return null; // divisions carry no fee cards
    return {
      player: this.phaseContextFor(node, 'player'),
      clubRep: this.phaseContextFor(node, 'clubRep')
    };
  });

  /**
   * Reverse-cascade disclosure for the open fly-in's fee cards, per role: which scopes
   * ONE TIER BELOW set their own phase stamp, amounts, or modifiers (league card → age
   * groups, age-group card → teams). Field-aware — a bare row is not an override (player
   * rows exist structurally at age-group scope); only locally-set values count. WAITLIST/
   * Dropped holding buckets (and their mirror teams' minted $0 rows) are not overrides a
   * director set — excluded, like every other fee surface. null until the map is loaded:
   * an unloaded map must read as "unknown", never as an all-clear. Memoized for
   * input-binding identity stability — see detailAncestorPhase.
   */
  readonly detailDescendantOverrides = computed(() => {
    const node = this.detailNode();
    if (!node || node.level > 1) return null; // divisions carry no fee cards; teams are leaves
    if (!this.feeMapIndex()) return null;     // not loaded yet — no notes, no all-clear
    return {
      player: this.descendantOverridesFor(node, 'player'),
      clubRep: this.descendantOverridesFor(node, 'clubRep')
    };
  });

  /**
   * TWO tiers below a league card: teams (under the league's real age groups) with their
   * own settings. Count-only in the cards' copy, but carried as full infos so each note
   * filters by its own field — and so the all-clear line can honestly claim the whole
   * subtree, not just the tier the named notes cover. null off league scope / before load.
   */
  readonly detailDeeperOverrides = computed(() => {
    const node = this.detailNode();
    if (!node || node.level !== 0) return null;
    if (!this.feeMapIndex()) return null;
    return {
      player: this.teamOverridesUnderLeague(node, 'player'),
      clubRep: this.teamOverridesUnderLeague(node, 'clubRep')
    };
  });

  /** One tier below `node`, the scopes whose own values set a phase stamp, an amount, or
   *  a modifier for the role — name + what they set, in name order. Locality is read off
   *  the map: a field whose source equals the child's own tier is set THERE, and its
   *  resolved value IS the local row value. */
  private descendantOverridesFor(node: LadtFlatNode, roleKey: 'player' | 'clubRep'): DescendantOverrideInfo[] {
    const index = this.feeMapIndex();
    if (!index) return [];
    const flat = this.flatNodes();
    let children: LadtFlatNode[];
    let tier: 'agegroup' | 'team';
    if (node.level === 0) {
      tier = 'agegroup';
      children = flat.filter(n => n.level === 1 && n.parentId === node.id && !this.isSpecialAgegroup(n.name));
    } else {
      tier = 'team';
      const divIds = new Set(flat.filter(n => n.level === 2 && n.parentId === node.id).map(n => n.id));
      children = flat.filter(n => n.level === 3 && n.parentId && (n.parentId === node.id || divIds.has(n.parentId)));
    }
    const out: DescendantOverrideInfo[] = [];
    for (const child of children) {
      const info = this.overrideInfoFromMap(child.name, index.get(child.id)?.[roleKey], tier);
      if (info) out.push(info);
    }
    return out.sort((a, b) => a.name.localeCompare(b.name));
  }

  /** Teams under a league's non-bucket age groups (direct or via a division) whose own
   *  values set something for the role. */
  private teamOverridesUnderLeague(node: LadtFlatNode, roleKey: 'player' | 'clubRep'): DescendantOverrideInfo[] {
    const index = this.feeMapIndex();
    if (!index) return [];
    const byId = new Map(this.flatNodes().map(n => [n.id, n] as const));
    const out: DescendantOverrideInfo[] = [];
    for (const teamId of this.teamsUnderContainers(0).get(node.id) ?? []) {
      const info = this.overrideInfoFromMap(byId.get(teamId)?.name ?? '', index.get(teamId)?.[roleKey], 'team');
      if (info) out.push(info);
    }
    return out;
  }

  /** Field-aware map entry → override info; null when the scope sets nothing locally.
   *  Stale team rows (mismatched agegroup pair) never surface here — the map already
   *  excludes them, exactly as the charge path does. */
  private overrideInfoFromMap(
    name: string,
    entry: LadtFeeRoleResolutionDto | undefined,
    tier: 'agegroup' | 'team',
  ): DescendantOverrideInfo | null {
    if (!entry) return null;
    const info: DescendantOverrideInfo = {
      name,
      phase: entry.phaseSource === tier ? entry.fullPayment : null,
      deposit: entry.depositSource === tier ? entry.deposit ?? null : null,
      balanceDue: entry.balanceDueSource === tier ? entry.balanceDue ?? null : null,
      earlyBird: entry.earlyBird?.source === tier,
      lateFee: entry.lateFee?.source === tier
    };
    return (info.phase !== null || info.deposit != null || info.balanceDue != null || info.earlyBird || info.lateFee)
      ? info : null;
  }

  private phaseContextFor(node: LadtFlatNode, roleKey: 'player' | 'clubRep'): PhaseContext {
    const index = this.feeMapIndex();
    const own = index?.get(node.id)?.[roleKey];
    const resolvedDeposit = own?.deposit ?? null;
    const resolvedBalance = own?.balanceDue ?? null;
    // Deposit-ONLY reach — deliberately distinct from the map's `twoPhase` (deposit AND
    // balance): the phase control is relevant wherever ANY deposit exists at or below
    // this scope, even when the matching balance lives at another tier.
    const depositInScope = (resolvedDeposit ?? 0) > 0
      || this.scopesBelow(node).some(id => ((index?.get(id)?.[roleKey]?.deposit) ?? 0) > 0);
    return { depositInScope, resolvedDeposit, resolvedBalance };
  }

  /** Scope ids below a node that can carry fee rows: non-bucket age groups + their teams
   *  under a league; teams under an age group; nothing under a team (leaf). */
  private scopesBelow(node: LadtFlatNode): string[] {
    if (node.level === 3) return [];
    const teams = this.teamsUnderContainers(node.level === 0 ? 0 : 1).get(node.id) ?? [];
    if (node.level !== 0) return teams;
    const ags = this.flatNodes()
      .filter(n => n.level === 1 && n.parentId === node.id && !this.isSpecialAgegroup(n.name))
      .map(n => n.id);
    return [...ags, ...teams];
  }

  /** The governing ancestor's map entry — which by construction excludes the node's own
   *  stamp (phase resolves at-or-above, and we read the tier ABOVE the node). Team → its
   *  age group; age group → its league; league → null (nothing above it in this UI). */
  private ancestorPhaseFor(node: LadtFlatNode): { player: { full: boolean; source: string }; clubRep: { full: boolean; source: string } } | null {
    let scopeId: string | undefined;
    if (node.level === 3) {
      const parent = this.flatNodes().find(n => n.id === node.parentId);
      scopeId = parent?.level === 2 ? parent.parentId ?? undefined : parent?.id;
    } else if (node.level === 1 && node.parentId) {
      scopeId = node.parentId;
    }
    const entry = scopeId ? this.feeMapIndex()?.get(scopeId) : undefined;
    if (!entry) return null;
    const pick = (e: LadtFeeRoleResolutionDto) => ({ full: e.fullPayment, source: e.phaseSource ?? 'job' });
    return { player: pick(entry.player), clubRep: pick(entry.clubRep) };
  }

  /** Tier specificity for source comparison: league < agegroup < team. */
  private static readonly TIER_RANK: Record<string, number> = { league: 1, agegroup: 2, team: 3 };

  /**
   * Enrich grid rows with _fees / _earlyBird / _lateFee / _phase column data from the
   * SERVER resolution map (canonical — mirrors the charging path; see
   * LadtFeeResolutionMapBuilder). The fly-in detail panels still resolve locally from
   * jobFees; the grids read only this map.
   *
   * Map not loaded (index null) → rows stay unenriched and the cells render the "—"
   * placeholder. That is the loading state; a loaded map with zero below-overrides is
   * the verified-clean state — the two must never look alike in data.
   *
   * Pill flags: `inherited` = the value came from a LESS specific tier (dim style +
   * "Inherited from X level"); `fromBelow` = the row's pill shows a value carried by MORE
   * specific scopes — either nothing resolves at/above this row (the AM-090 case) or the
   * row's resolved value operates on no team (`inert` — every team below sets its own, so
   * claiming the value is "set here" would be false; Todd's ruling 08-07). `inherited` and
   * `fromBelow` are mutually exclusive and must never share a flag.
   *
   * `dist` (container rows only) is the team-weighted distribution of the row's resolved
   * value over the non-bucket teams below it: `covered` teams are charged what the pill
   * shows; `own` teams resolve from a deeper tier. Tooltips speak in these terms — a
   * level is only ever said to "set" what it actually operates on.
   */
  private enrichWithFees(data: any[], level: number): void {
    if (level === 2) return; // divisions aren't a scope in fees.JobFees
    const index = this.feeMapIndex();
    if (!index) return;

    const idField = ID_FIELD_BY_LEVEL[level];
    const ownTier = level === 0 ? 'league' : level === 1 ? 'agegroup' : 'team';
    const ownRank = LadtEditorComponent.TIER_RANK[ownTier];
    const containment = level === 3 ? null : this.teamsUnderContainers(level);

    for (const row of data) {
      const node = index.get(row[idField]);
      if (!node) continue;

      const fees: any[] = [];
      const earlyBird: any[] = [];
      const lateFee: any[] = [];
      const phase: any[] = [];
      const teamIds = containment?.get(row[idField]) ?? [];

      for (const [roleKey, entry] of [
        ['player', node.player], ['clubRep', node.clubRep],
      ] as ReadonlyArray<readonly ['player' | 'clubRep', LadtFeeRoleResolutionDto]>) {
        const roleLabel = LadtEditorComponent.ROLE_LABELS[entry.roleId] ?? entry.roleId.substring(0, 6);
        const below = entry.below ?? null;
        const dist = (srcOf: (e: LadtFeeRoleResolutionDto) => string | null | undefined,
                      nullMeans: 'skip' | 'covered') =>
          containment
            ? LadtEditorComponent.distributionOf(teamIds, index, roleKey, ownRank, srcOf, nullMeans)
            : null;

        // ── Base-fee pill: shown when configured at/above OR overridden below ──
        if (entry.feeConfigured || (below?.amounts.overrideCount ?? 0) > 0) {
          // Per-field sources can split (deposit from agegroup, balance from team-…);
          // the pill's single ⓘ names the more specific of the two.
          const source = LadtEditorComponent.moreSpecific(entry.depositSource, entry.balanceDueSource);
          const feeDist = dist(
            e => e.feeConfigured ? LadtEditorComponent.moreSpecific(e.depositSource, e.balanceDueSource) : null,
            'skip');
          const inert = entry.feeConfigured && feeDist != null && feeDist.own > 0 && feeDist.covered === 0;
          fees.push({
            roleId: entry.roleId, roleLabel,
            deposit: entry.deposit ?? null,
            balanceDue: entry.balanceDue ?? null,
            source,
            inherited: !inert && source != null && LadtEditorComponent.TIER_RANK[source] < ownRank,
            fromBelow: !entry.feeConfigured || inert,
            inert,
            dist: feeDist,
            below: below?.amounts ?? null,
          });
        }

        // ── Modifier pills ──
        for (const [list, win, belowMod, srcOf] of [
          [earlyBird, entry.earlyBird ?? null, below?.earlyBird ?? null,
            (e: LadtFeeRoleResolutionDto) => e.earlyBird?.source],
          [lateFee, entry.lateFee ?? null, below?.lateFee ?? null,
            (e: LadtFeeRoleResolutionDto) => e.lateFee?.source],
        ] as const) {
          if (win != null || (belowMod?.overrideCount ?? 0) > 0) {
            const modDist = dist(srcOf, 'skip');
            const inert = win != null && modDist != null && modDist.own > 0 && modDist.covered === 0;
            (list as any[]).push({
              roleId: entry.roleId, roleLabel,
              amount: win?.amount ?? null,
              source: win?.source ?? null,
              active: win?.active ?? false,
              inherited: !inert && win != null && LadtEditorComponent.TIER_RANK[win.source] < ownRank,
              fromBelow: win == null || inert,
              inert,
              dist: modDist,
              below: belowMod,
            });
          }
        }

        // ── Phase pill: null phaseSource = the job baseline (silence = deposit) ──
        if (entry.feeConfigured || entry.phaseSource != null || entry.twoPhase
          || (below?.phase.overrideCount ?? 0) > 0) {
          const source = entry.phaseSource ?? 'job';
          // Teams with no stamp inherit through this row — they count as covered.
          const phaseDist = dist(e => e.phaseSource, 'covered');
          const inert = phaseDist != null && phaseDist.own > 0 && phaseDist.covered === 0;
          phase.push({
            roleId: entry.roleId, roleLabel,
            fullPayment: entry.fullPayment,
            twoPhase: entry.twoPhase,
            source,
            inherited: !inert && source !== ownTier,
            fromBelow: false, // face flips via `inert`, not this flag (phase resolves at-or-above)
            inert,
            dist: phaseDist,
            below: below?.phase ?? null,
          });
        }
      }

      row._fees = fees;
      row._earlyBird = earlyBird;
      row._lateFee = lateFee;
      row._phase = phase;
    }
  }

  /** The more specific of two source tiers (null-tolerant). */
  private static moreSpecific(a: string | null | undefined, b: string | null | undefined): string | null {
    if (a == null) return b ?? null;
    if (b == null) return a;
    return LadtEditorComponent.TIER_RANK[a] >= LadtEditorComponent.TIER_RANK[b] ? a : b;
  }

  /** Non-bucket team ids under each container (league when level 0, agegroup when level 1),
   *  from the tree. WAITLIST/Dropped buckets and their teams are no part of any fee
   *  conclusion — mirrors AgegroupConstants.IsSystemBucket on the server, whose below-
   *  summaries exclude them the same way. */
  private teamsUnderContainers(level: number): Map<string, string[]> {
    const nodes = this.flatNodes();
    const byId = new Map(nodes.map(n => [n.id, n] as const));
    const out = new Map<string, string[]>();
    for (const team of nodes) {
      if (team.level !== 3) continue;
      let ag: LadtFlatNode | null = null;
      let league: LadtFlatNode | null = null;
      for (let p = team.parentId ? byId.get(team.parentId) : undefined;
           p; p = p.parentId ? byId.get(p.parentId) : undefined) {
        if (p.level === 1) ag = p;
        else if (p.level === 0) league = p;
      }
      if (!ag || ag.isSpecial) continue;
      const container = level === 0 ? league : ag;
      if (!container) continue;
      const list = out.get(container.id);
      if (list) list.push(team.id); else out.set(container.id, [team.id]);
    }
    return out;
  }

  /** How a container row's resolved value distributes over the teams below it, per family.
   *  `srcOf` extracts a team's source tier for the family; a null return means the family
   *  doesn't exist for that team and is skipped — except phase, where null is the job
   *  baseline and the team counts as covered. `covered` teams are charged what the row
   *  shows (their source is at-or-above the row's tier); `own` teams resolve deeper, at
   *  the tiers listed in `ownTiers` (broadest first). */
  private static distributionOf(
    teamIds: string[],
    index: Map<string, LadtFeeNodeResolutionDto>,
    roleKey: 'player' | 'clubRep',
    ownRank: number,
    srcOf: (entry: LadtFeeRoleResolutionDto) => string | null | undefined,
    nullMeans: 'skip' | 'covered',
  ): { teams: number; covered: number; own: number; ownTiers: string[] } {
    let covered = 0;
    let own = 0;
    const ownTiers = new Set<string>();
    for (const id of teamIds) {
      const entry = index.get(id)?.[roleKey];
      if (!entry) continue;
      const src = srcOf(entry);
      if (src == null) {
        if (nullMeans === 'covered') covered++;
        continue;
      }
      if (LadtEditorComponent.TIER_RANK[src] > ownRank) {
        own++;
        ownTiers.add(src);
      } else {
        covered++;
      }
    }
    return {
      teams: teamIds.length, covered, own,
      ownTiers: [...ownTiers].sort((a, b) => LadtEditorComponent.TIER_RANK[a] - LadtEditorComponent.TIER_RANK[b]),
    };
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

  /** Invalidate BOTH fee caches in lockstep — the role-disclosure rows (jobFees) and
   *  the resolution map (feeMap, feeding grids AND fly-in context). Structural
   *  invariant: any save/clone/drop that stales one stales the other; never null one
   *  without the other. */
  private invalidateFeeCaches(): void {
    this.jobFees.set([]);
    this.feeMap.set(null);
  }

  onDetailSaved(): void {
    const node = this.selectedNode();
    this.invalidateFeeCaches(); // grid + fly-in reload fresh
    this.forceCloseDetail();
    this.loadTree();
    if (node) this.loadSiblings(node);
  }

  onDetailCloned(newTeamId: string): void {
    // Reload tree and refocus on the clone — fly-in stays open on the new team.
    this.invalidateFeeCaches();
    this.forceCloseDetail();
    this.loadTree(newTeamId, /* openDetailAfter */ true);
  }

  onDetailDropped(): void {
    // Team moved to Dropped Teams. Mutate locally — no endpoint refresh.
    // Mirrors onDetailDeleted (SP-005 pattern). A natural refresh later
    // surfaces the team under the Dropped Teams agegroup.
    const dropped = this.selectedNode();
    this.invalidateFeeCaches();
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
