import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkTreeModule } from '@angular/cdk/tree';
import { LadtService } from './services/ladt.service';
import { LeagueDetailComponent } from './components/league-detail.component';
import { AgegroupDetailComponent } from './components/agegroup-detail.component';
import { DivisionDetailComponent } from './components/division-detail.component';
import { TeamDetailComponent } from './components/team-detail.component';
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
    TeamDetailComponent
  ],
  templateUrl: './ladt-editor.component.html',
  styleUrl: './ladt-editor.component.scss'
})
export class LadtEditorComponent implements OnInit {
  private readonly ladtService = inject(LadtService);

  // ── State ──
  isLoading = signal(false);
  errorMessage = signal<string | null>(null);
  totalTeams = signal(0);
  totalPlayers = signal(0);

  // Tree data (all nodes, flat)
  flatNodes = signal<LadtFlatNode[]>([]);
  private rawTree = signal<LadtTreeNodeDto[]>([]);

  // Expansion state (reactive)
  expandedIds = signal(new Set<string>());

  // Visible nodes: only nodes whose ancestors are all expanded
  visibleNodes = computed(() => {
    const all = this.flatNodes();
    const expanded = this.expandedIds();
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
    return result;
  });

  // Selection
  selectedNode = signal<LadtFlatNode | null>(null);
  selectedLevel = computed(() => this.selectedNode()?.level ?? -1);

  // Mobile drawer
  drawerOpen = signal(false);

  // CdkTree accessors
  readonly levelAccessor = (node: LadtFlatNode) => node.level;
  readonly trackById = (_: number, node: LadtFlatNode) => node.id;

  hasChild = (_: number, node: LadtFlatNode) => node.expandable;

  ngOnInit(): void {
    this.loadTree();
  }

  loadTree(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.ladtService.getTree().subscribe({
      next: (root) => {
        this.rawTree.set(root.leagues as LadtTreeNodeDto[]);
        this.totalTeams.set(root.totalTeams);
        this.totalPlayers.set(root.totalPlayers);

        const flat = this.flattenTree(root.leagues as LadtTreeNodeDto[]);
        this.flatNodes.set(flat);

        // First load: expand all; otherwise preserve existing expansion
        if (this.expandedIds().size === 0) {
          this.expandAll();
        }

        this.isLoading.set(false);
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
        const children = (node.children ?? []) as LadtTreeNodeDto[];
        result.push({
          id: node.id,
          parentId: node.parentId ?? null,
          name: node.name,
          level: node.level,
          isLeaf: node.isLeaf,
          teamCount: node.teamCount,
          playerCount: node.playerCount,
          expandable: children.length > 0,
          active: node.active
        });
        if (children.length > 0) {
          recurse(children);
        }
      }
    };
    recurse(nodes);
    return result;
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
    this.expandedIds.set(new Set());
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

  // ── Add Stubs ──

  addStubAgegroup(leagueId: string): void {
    this.ladtService.addStubAgegroup(leagueId).subscribe({
      next: () => this.loadTree(),
      error: (err) => this.errorMessage.set(err.error?.message || 'Failed to add age group')
    });
  }

  addStubDivision(agegroupId: string): void {
    this.ladtService.addStubDivision(agegroupId).subscribe({
      next: () => this.loadTree(),
      error: (err) => this.errorMessage.set(err.error?.message || 'Failed to add division')
    });
  }

  addStubTeam(divId: string): void {
    this.ladtService.addStubTeam(divId).subscribe({
      next: () => this.loadTree(),
      error: (err) => this.errorMessage.set(err.error?.message || 'Failed to add team')
    });
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

  // ── Detail panel callbacks ──

  onDetailSaved(): void {
    this.loadTree();
  }

  onDetailDeleted(): void {
    this.selectedNode.set(null);
    this.loadTree();
  }

  // ── Mobile ──

  toggleDrawer(): void {
    this.drawerOpen.set(!this.drawerOpen());
  }
}
