import { Component, OnInit, AfterViewChecked, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkTreeModule } from '@angular/cdk/tree';
import { Observable } from 'rxjs';
import { LadtService } from './services/ladt.service';
import { LeagueDetailComponent } from './components/league-detail.component';
import { AgegroupDetailComponent } from './components/agegroup-detail.component';
import { DivisionDetailComponent } from './components/division-detail.component';
import { TeamDetailComponent } from './components/team-detail.component';
import { LadtSiblingGridComponent } from './components/ladt-sibling-grid.component';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import {
  COLUMNS_BY_LEVEL, ID_FIELD_BY_LEVEL,
  type LadtColumnDef
} from './configs/ladt-grid-columns';
import type { ParentBreadcrumb } from './components/ladt-sibling-grid.component';
import type { LadtTreeNodeDto } from '../../../core/api';

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
  isSpecial: boolean;
  isPhantom?: boolean;
}

@Component({
  selector: 'app-ladt-editor',
  standalone: true,
  imports: [
    CommonModule,
    CdkTreeModule,
    LeagueDetailComponent,
    AgegroupDetailComponent,
    DivisionDetailComponent,
    TeamDetailComponent,
    LadtSiblingGridComponent,
    ConfirmDialogComponent
  ],
  templateUrl: './ladt-editor.component.html',
  styleUrl: './ladt-editor.component.scss'
})
export class LadtEditorComponent implements OnInit, AfterViewChecked {
  private readonly ladtService = inject(LadtService);

  // ── State ──
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);
  totalTeams = signal(0);
  totalPlayers = signal(0);

  // Tree data (all nodes, flat)
  flatNodes = signal<LadtFlatNode[]>([]);
  private rawTree = signal<LadtTreeNodeDto[]>([]);

  // Scheduled team IDs (raw data from backend, used for KPI computation)
  scheduledTeamIds = signal<Set<string>>(new Set());

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

  // Actions dropdown
  actionsOpen = signal(false);

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

  loadTree(selectId?: string): void {
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

        // First load: show leagues expanded (age groups visible)
        if (this.expandedIds().size === 0) {
          this.collapseAll();
        }

        this.isLoading.set(false);

        // After adding a child: expand ancestors and select the new node
        if (selectId) {
          const newNode = flat.find(n => n.id === selectId);
          if (newNode) {
            this.expandAncestors(newNode, flat);
            this.selectNode(newNode);
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
    const recurse = (items: LadtTreeNodeDto[]) => {
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

        // Sort divisions: "Unassigned" first, then alpha
        if (node.level === 1 && children.length > 0) {
          children = [...children].sort((a, b) => {
            const aUnassigned = a.name.toUpperCase() === 'UNASSIGNED';
            const bUnassigned = b.name.toUpperCase() === 'UNASSIGNED';
            if (aUnassigned !== bUnassigned) return aUnassigned ? -1 : 1;
            return a.name.localeCompare(b.name);
          });
        }

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
          color: node.color ?? null,
          isSpecial: (node.level === 1 && this.isSpecialAgegroup(node.name)) ||
                     (node.level === 2 && node.name.toUpperCase() === 'UNASSIGNED')
        });
        if (children.length > 0) {
          recurse(children);
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
    const next = new Set<string>();
    for (const n of this.flatNodes()) {
      if (n.expandable) next.add(n.id);
    }
    this.expandedIds.set(next);
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

  // ── Batch ──

  addWaitlistAgegroups(): void {
    this.ladtService.addWaitlistAgegroups().subscribe({
      next: (count) => {
        this.loadTree();
        if (count === 0) {
          this.errorMessage.set('All leagues already have WAITLIST age groups.');
        }
      },
      error: (err) => this.errorMessage.set(err.error?.message || 'Failed to add waitlist groups')
    });
  }

  // ── Sibling grid ──

  private loadSiblings(node: LadtFlatNode): void {
    const level = node.level;
    this.siblingColumns.set(COLUMNS_BY_LEVEL[level]);
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

    fetch$.subscribe({
      next: (data: any[]) => {
        this.siblingData.set(data);
        this.isSiblingsLoading.set(false);
      },
      error: (err: any) => {
        this.errorMessage.set(err.error?.message || 'Failed to load siblings');
        this.isSiblingsLoading.set(false);
      }
    });
  }

  private getParentParts(node: LadtFlatNode): ParentBreadcrumb[] {
    const parts: ParentBreadcrumb[] = [];
    let current = node;
    // Walk up the tree collecting ancestors
    while (current.parentId) {
      const parent = this.flatNodes().find(n => n.id === current.parentId);
      if (!parent) break;
      parts.unshift({ name: parent.name, level: parent.level });
      current = parent;
    }
    return parts;
  }

  onGridRowSelected(id: string): void {
    const node = this.flatNodes().find(n => n.id === id);
    if (node) {
      this.selectedNode.set(node);
    }
  }

  // ── Detail panel callbacks ──

  onDetailSaved(): void {
    const node = this.selectedNode();
    this.loadTree();
    if (node) this.loadSiblings(node);
  }

  onDetailDeleted(): void {
    this.selectedNode.set(null);
    this.siblingData.set([]);
    this.loadTree();
  }

  // ── Mobile ──

  toggleDrawer(): void {
    this.drawerOpen.set(!this.drawerOpen());
  }
}
