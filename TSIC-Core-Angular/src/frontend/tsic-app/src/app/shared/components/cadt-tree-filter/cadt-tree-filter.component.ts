import {
  Component, Input, Output, EventEmitter, signal, computed,
  ChangeDetectionStrategy, SimpleChanges, OnChanges
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { CadtClubNode as BaseCadtClubNode, CadtAgegroupNode as BaseCadtAgegroupNode, CadtDivisionNode as BaseCadtDivisionNode, CadtTeamNode as BaseCadtTeamNode } from '@core/api';

// Extend until API models are regenerated with count fields.
// Once regenerated, replace with direct imports.
type CadtTeamNode = BaseCadtTeamNode & { playerCount?: number };
type CadtDivisionNode = Omit<BaseCadtDivisionNode, 'teams'> & { teamCount?: number; playerCount?: number; teams: CadtTeamNode[] };
type CadtAgegroupNode = Omit<BaseCadtAgegroupNode, 'divisions'> & { teamCount?: number; playerCount?: number; divisions: CadtDivisionNode[] };
type CadtClubNode = Omit<BaseCadtClubNode, 'agegroups'> & { teamCount?: number; playerCount?: number; agegroups: CadtAgegroupNode[] };

/** Flat node used for rendering the CADT tree */
interface CadtFlatNode {
  id: string;
  parentId: string | null;
  name: string;
  level: number;       // 0=club, 1=agegroup, 2=division, 3=team
  isLeaf: boolean;
  expandable: boolean;
  color: string | null; // agegroup color (inherited to div/team)
  teamCount: number;
  playerCount: number;
  /** All descendant IDs (for cascade check/uncheck) */
  descendantIds: string[];
}

export interface CadtSelectionEvent {
  clubNames: string[];
  agegroupIds: string[];
  divisionIds: string[];
  teamIds: string[];
}

@Component({
  selector: 'app-cadt-tree-filter',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cadt-tree-filter">
      <!-- Search box -->
      @if (showSearch) {
        <div class="tree-search">
          <input type="text"
                 class="tree-search-input"
                 [placeholder]="searchPlaceholder"
                 [ngModel]="searchTerm()"
                 (ngModelChange)="searchTerm.set($event)" />
          @if (searchTerm()) {
            <button class="tree-search-clear" (click)="searchTerm.set('')">&times;</button>
          }
        </div>
      }

      <!-- Tree nodes -->
      <div class="tree-container">
        @for (node of visibleNodes(); track node.id) {
          @if (node.level === 0) {
            <!-- Root: totals row, clickable to expand/collapse -->
            <div class="tree-node tree-root" (click)="toggleExpand(node)">
              <svg class="tree-root-chevron" [class.expanded]="expandedIds().has(node.id)"
                   xmlns="http://www.w3.org/2000/svg" width="10" height="10" viewBox="0 0 24 24"
                   fill="none" stroke="currentColor" stroke-width="2.5">
                <polyline points="9 6 15 12 9 18"></polyline>
              </svg>
              <span class="tree-name">All</span>
              <span class="tree-badges">
                <span class="tree-badge badge-muted" title="Teams">{{ node.teamCount }}</span>
                <span class="tree-badge badge-outline" title="Players">{{ node.playerCount }}</span>
              </span>
            </div>
          } @else {
            <div class="tree-node"
                 [style.padding-left.rem]="(node.level - 1) * 1.25">
              <!-- Expand/collapse arrow -->
              @if (node.expandable) {
                <span class="tree-expand"
                      [class.expanded]="expandedIds().has(node.id)"
                      (click)="toggleExpand(node)">
                  <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24"
                       fill="none" stroke="currentColor" stroke-width="2.5">
                    <polyline points="9 6 15 12 9 18"></polyline>
                  </svg>
                </span>
              } @else {
                <span class="tree-expand-spacer"></span>
              }

              <!-- Checkbox -->
              <input type="checkbox"
                     class="tree-checkbox"
                     [checked]="checkedIdsSignal().has(node.id)"
                     [indeterminate]="checkState().get(node.id) === 'some'"
                     (change)="onCheck(node, $event)" />

              <!-- Agegroup color dot -->
              @if (node.color) {
                <span class="tree-color-dot"
                      [style.background]="node.color"></span>
              }

              <!-- Name -->
              <span class="tree-name" (click)="toggleExpand(node)">{{ node.name }}</span>

              <!-- Count badges -->
              <span class="tree-badges">
                @if (node.isLeaf) {
                  @if (node.color) {
                    <span class="tree-badge"
                          [style.background]="node.color"
                          [style.color]="contrastText(node.color)"
                          title="Players">{{ node.playerCount }}</span>
                  } @else {
                    <span class="tree-badge badge-outline" title="Players">{{ node.playerCount }}</span>
                  }
                } @else if (node.color) {
                  <span class="tree-badge badge-muted" title="Teams">{{ node.teamCount }}</span>
                  <span class="tree-badge"
                        [style.background]="node.color"
                        [style.color]="contrastText(node.color)"
                        title="Players">{{ node.playerCount }}</span>
                } @else {
                  <span class="tree-badge badge-muted" title="Teams">{{ node.teamCount }}</span>
                  <span class="tree-badge badge-outline" title="Players">{{ node.playerCount }}</span>
                }
              </span>
            </div>
          }
        }

        @if (visibleNodes().length === 0 && searchTerm()) {
          <div class="tree-empty">No matches for "{{ searchTerm() }}"</div>
        }
      </div>
    </div>
  `,
  styles: [`
    .cadt-tree-filter {
      font-size: var(--font-size-xs, 0.75rem);
      line-height: 1.4;
      display: flex;
      flex-direction: column;
      border: 1px solid var(--bs-border-color);
      border-radius: var(--bs-border-radius);
      background: var(--bs-body-bg);
      padding: var(--space-1);
      max-height: 400px;
      overflow-y: auto;
    }

    .tree-search {
      position: relative;
      margin-bottom: var(--space-2, 8px);
      flex-shrink: 0;
    }

    .tree-search-input {
      width: 100%;
      padding: var(--space-1, 4px) var(--space-2, 8px);
      padding-right: var(--space-6, 24px);
      border: 1px solid var(--bs-border-color);
      border-radius: var(--bs-border-radius);
      background: var(--bs-body-bg);
      color: var(--bs-body-color);
      font-size: inherit;
      outline: none;
      box-sizing: border-box;
    }

    .tree-search-input:focus {
      border-color: var(--bs-primary);
      box-shadow: 0 0 0 2px color-mix(in srgb, var(--bs-primary) 25%, transparent);
    }

    .tree-search-input::placeholder {
      color: var(--bs-secondary-color);
    }

    .tree-search-clear {
      position: absolute;
      right: var(--space-1, 4px);
      top: 50%;
      transform: translateY(-50%);
      background: none;
      border: none;
      color: var(--bs-secondary-color);
      font-size: 1rem;
      cursor: pointer;
      padding: 0 var(--space-1, 4px);
      line-height: 1;
    }

    .tree-search-clear:hover {
      color: var(--bs-body-color);
    }

    .tree-container {
      overflow-x: hidden;
    }

    .tree-node {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 2px 4px;
      border-radius: var(--bs-border-radius);
      cursor: default;
    }

    .tree-node:hover {
      background: var(--bs-tertiary-bg);
    }

    .tree-expand {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 16px;
      height: 16px;
      flex-shrink: 0;
      cursor: pointer;
      color: var(--bs-secondary-color);
      transition: transform 0.15s;
    }

    .tree-expand.expanded {
      transform: rotate(90deg);
    }

    .tree-expand-spacer {
      display: inline-block;
      width: 16px;
      flex-shrink: 0;
    }

    .tree-checkbox {
      accent-color: var(--bs-primary);
      width: 15px;
      height: 15px;
      flex-shrink: 0;
      cursor: pointer;
    }

    .tree-color-dot {
      display: inline-block;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      flex-shrink: 0;
    }

    .tree-name {
      flex: 1;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      cursor: pointer;
    }

    .tree-root {
      border-bottom: 1px solid var(--bs-border-color-translucent);
      margin-bottom: 2px;
      padding-bottom: 2px;
      font-weight: 600;
      color: var(--bs-secondary-color);
      font-size: 0.6875rem;
      cursor: pointer;
    }

    .tree-root-chevron {
      flex-shrink: 0;
      color: var(--bs-secondary-color);
      opacity: 0.6;
      transition: transform 0.15s;
      cursor: pointer;
    }

    .tree-root-chevron.expanded {
      transform: rotate(90deg);
    }

    .tree-badges {
      margin-left: auto;
      display: flex;
      gap: 6px;
      flex-shrink: 0;
    }

    .tree-badge {
      flex-shrink: 0;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: 24px;
      height: 18px;
      padding: 0 var(--space-1, 4px);
      border-radius: 9px;
      font-size: 0.625rem;
      font-weight: 700;
      font-variant-numeric: tabular-nums;
    }

    .badge-default {
      background: var(--bs-info);
      color: var(--bs-white);
    }

    .badge-muted {
      background: color-mix(in srgb, var(--bs-secondary) 40%, var(--bs-card-bg));
      color: var(--bs-body-color);
    }

    .badge-outline {
      background: transparent;
      border: 1.5px solid var(--bs-body-color);
      color: var(--bs-body-color);
    }

    .tree-empty {
      padding: var(--space-2, 8px) var(--space-3, 12px);
      color: var(--bs-secondary-color);
      font-style: italic;
    }
  `]
})
export class CadtTreeFilterComponent implements OnChanges {
  @Input() treeData: CadtClubNode[] = [];
  @Input() hideRootLevel = false;
  @Input() searchPlaceholder = 'Filter clubs...';
  @Input() showSearch = true;
  /** When set, wraps all clubs under a synthetic root node (e.g. job name). */
  @Input() rootLabel = '';
  /** Header label shown flush-left above the tree (e.g. "Club/Agegroup/Division/Team") */
  @Input() headerLabel = '';
  @Output() checkedIdsChange = new EventEmitter<Set<string>>();

  /** Internal signal for checked state — synced from parent @Input, updated on user interaction. */
  readonly checkedIdsSignal = signal(new Set<string>());

  @Input() set checkedIds(value: Set<string>) {
    this.checkedIdsSignal.set(value);
  }

  // Internal state
  flatNodes = signal<CadtFlatNode[]>([]);
  expandedIds = signal<Set<string>>(new Set());
  searchTerm = signal('');
  private parentMap = new Map<string, string | null>(); // childId -> parentId

  /** Flat nodes filtered by search, then pruned to only visible (expanded) rows */
  visibleNodes = computed(() => {
    const nodes = this.flatNodes();
    const expanded = this.expandedIds();
    const term = this.searchTerm().trim().toLowerCase();

    // If searching, find matching club IDs and show only their subtrees
    let filteredNodes = nodes;
    if (term) {
      const matchingClubIds = new Set<string>();
      for (const node of nodes) {
        if (node.id.startsWith('club:') && node.name.toLowerCase().includes(term)) {
          matchingClubIds.add(node.id);
        }
      }
      filteredNodes = nodes.filter(node => {
        // Always show synthetic root
        if (node.id === 'root:job') return true;
        if (node.id.startsWith('club:')) return matchingClubIds.has(node.id);
        // For child nodes, walk up to find their club ancestor
        let ancestorId = node.parentId;
        while (ancestorId) {
          if (matchingClubIds.has(ancestorId)) return true;
          ancestorId = this.parentMap.get(ancestorId) ?? null;
        }
        return false;
      });
    }

    // Then apply expansion visibility
    const visible: CadtFlatNode[] = [];
    for (const node of filteredNodes) {
      // When hideRootLevel, skip rendering root-level nodes (they stay expanded but invisible)
      if (this.hideRootLevel && node.level === 0) continue;
      if (this.isNodeVisible(node, expanded, filteredNodes)) {
        visible.push(node);
      }
    }
    return visible;
  });

  /** Check state per node: 'all' | 'some' | 'none' */
  checkState = computed(() => {
    const checked = this.checkedIdsSignal();
    const nodes = this.flatNodes();
    const stateMap = new Map<string, 'all' | 'some' | 'none'>();

    for (const node of nodes) {
      if (node.isLeaf) {
        stateMap.set(node.id, checked.has(node.id) ? 'all' : 'none');
      } else {
        const descendants = node.descendantIds;
        if (descendants.length === 0) {
          stateMap.set(node.id, checked.has(node.id) ? 'all' : 'none');
          continue;
        }
        const checkedCount = descendants.filter(id => checked.has(id)).length;
        if (checkedCount === 0 && !checked.has(node.id)) stateMap.set(node.id, 'none');
        else if (checkedCount === descendants.length) stateMap.set(node.id, 'all');
        else stateMap.set(node.id, 'some');
      }
    }
    return stateMap;
  });

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['treeData'] || changes['rootLabel']) && this.treeData?.length) {
      this.buildFlatNodes();
    }
  }

  private buildFlatNodes(): void {
    const result: CadtFlatNode[] = [];
    this.parentMap.clear();
    const levelOffset = this.rootLabel ? 1 : 0;

    // Synthetic root node when rootLabel is provided
    if (this.rootLabel) {
      const rootId = 'root:job';
      this.parentMap.set(rootId, null);

      const allDescendants: string[] = [];
      let totalTeams = 0;
      let totalPlayers = 0;
      for (const club of this.treeData) {
        const clubId = `club:${club.clubName}`;
        allDescendants.push(clubId);
        totalTeams += club.teamCount ?? 0;
        totalPlayers += club.playerCount ?? 0;
        for (const ag of club.agegroups ?? []) {
          allDescendants.push(`ag:${club.clubName}|${ag.agegroupId}`);
          for (const div of ag.divisions ?? []) {
            allDescendants.push(`div:${club.clubName}|${div.divId}`);
            for (const team of div.teams ?? []) {
              allDescendants.push(`team:${team.teamId}`);
            }
          }
        }
      }

      result.push({
        id: rootId,
        parentId: null,
        name: this.rootLabel,
        level: 0,
        isLeaf: false,
        expandable: true,
        color: null,
        teamCount: totalTeams,
        playerCount: totalPlayers,
        descendantIds: allDescendants
      });
    }

    for (const club of this.treeData) {
      const clubId = `club:${club.clubName}`;
      const clubParent = this.rootLabel ? 'root:job' : null;
      this.parentMap.set(clubId, clubParent);

      const clubDescendants: string[] = [];

      // Pre-collect all descendant IDs for the club
      // Agegroup/division IDs are scoped by club name to avoid duplicates
      // (same ag/div can appear under multiple clubs)
      for (const ag of club.agegroups ?? []) {
        const agId = `ag:${club.clubName}|${ag.agegroupId}`;
        clubDescendants.push(agId);
        for (const div of ag.divisions ?? []) {
          const divId = `div:${club.clubName}|${div.divId}`;
          clubDescendants.push(divId);
          for (const team of div.teams ?? []) {
            clubDescendants.push(`team:${team.teamId}`);
          }
        }
      }

      result.push({
        id: clubId,
        parentId: clubParent,
        name: club.clubName,
        level: 0 + levelOffset,
        isLeaf: (club.agegroups ?? []).length === 0,
        expandable: (club.agegroups ?? []).length > 0,
        color: null,
        teamCount: club.teamCount ?? 0,
        playerCount: club.playerCount ?? 0,
        descendantIds: clubDescendants
      });

      for (const ag of club.agegroups ?? []) {
        const agId = `ag:${club.clubName}|${ag.agegroupId}`;
        this.parentMap.set(agId, clubId);

        const agDescendants: string[] = [];
        for (const div of ag.divisions ?? []) {
          const divId = `div:${club.clubName}|${div.divId}`;
          agDescendants.push(divId);
          for (const team of div.teams ?? []) {
            agDescendants.push(`team:${team.teamId}`);
          }
        }

        const agColor = ag.color ?? null;

        result.push({
          id: agId,
          parentId: clubId,
          name: ag.agegroupName,
          level: 1 + levelOffset,
          isLeaf: (ag.divisions ?? []).length === 0,
          expandable: (ag.divisions ?? []).length > 0,
          color: agColor,
          teamCount: ag.teamCount ?? 0,
          playerCount: ag.playerCount ?? 0,
          descendantIds: agDescendants
        });

        for (const div of ag.divisions ?? []) {
          const divId = `div:${club.clubName}|${div.divId}`;
          this.parentMap.set(divId, agId);

          const divDescendants: string[] = [];
          for (const team of div.teams ?? []) {
            divDescendants.push(`team:${team.teamId}`);
          }

          result.push({
            id: divId,
            parentId: agId,
            name: div.divName,
            level: 2 + levelOffset,
            isLeaf: (div.teams ?? []).length === 0,
            expandable: (div.teams ?? []).length > 0,
            color: agColor,  // Inherit agegroup color
            teamCount: div.teamCount ?? 0,
            playerCount: div.playerCount ?? 0,
            descendantIds: divDescendants
          });

          for (const team of div.teams ?? []) {
            const teamId = `team:${team.teamId}`;
            this.parentMap.set(teamId, divId);

            result.push({
              id: teamId,
              parentId: divId,
              name: team.teamName,
              level: 3 + levelOffset,
              isLeaf: true,
              expandable: false,
              color: agColor,  // Inherit agegroup color
              teamCount: 0,
              playerCount: team.playerCount ?? 0,
              descendantIds: []
            });
          }
        }
      }
    }

    this.flatNodes.set(result);
    // Start collapsed — root not expanded, children hidden
    this.expandedIds.set(new Set());
  }

  private isNodeVisible(
    node: CadtFlatNode,
    expanded: Set<string>,
    filteredNodes: CadtFlatNode[]
  ): boolean {
    // Root-level nodes are always visible
    if (!node.parentId) return true;

    // Walk up parent chain - all ancestors must be expanded AND present in filtered set
    const filteredIds = new Set(filteredNodes.map(n => n.id));
    let currentId: string | null = node.parentId;
    while (currentId) {
      if (!expanded.has(currentId)) return false;
      if (!filteredIds.has(currentId)) return false;
      currentId = this.parentMap.get(currentId) ?? null;
    }
    return true;
  }

  expandAll(): void {
    const all = new Set(this.flatNodes().filter(n => n.expandable).map(n => n.id));
    this.expandedIds.set(all);
  }

  collapseAll(): void {
    this.expandedIds.set(new Set());
  }

  toggleExpand(node: CadtFlatNode): void {
    if (!node.expandable) return;
    this.expandedIds.update(ids => {
      const next = new Set(ids);
      if (next.has(node.id)) next.delete(node.id);
      else next.add(node.id);
      return next;
    });
  }

  onCheck(node: CadtFlatNode, event: Event): void {
    const isChecked = (event.target as HTMLInputElement).checked;
    const next = new Set(this.checkedIdsSignal());

    if (isChecked) {
      next.add(node.id);
      for (const id of node.descendantIds) next.add(id);
      this.bubbleCheckUp(node.id, next);
    } else {
      next.delete(node.id);
      for (const id of node.descendantIds) next.delete(id);
      this.bubbleUncheckUp(node.id, next);
    }

    this.checkedIdsSignal.set(next);
    this.checkedIdsChange.emit(next);
  }

  /** After checking children, check parent if ALL its children are now checked */
  private bubbleCheckUp(nodeId: string, checked: Set<string>): void {
    const parentId = this.parentMap.get(nodeId);
    if (!parentId) return;

    const parent = this.flatNodes().find(n => n.id === parentId);
    if (!parent) return;

    const allDescendantsChecked = parent.descendantIds.every(id => checked.has(id));
    if (allDescendantsChecked) {
      checked.add(parentId);
      this.bubbleCheckUp(parentId, checked);
    }
  }

  /** Returns white or dark text depending on background luminance */
  contrastText(hex: string | null): string {
    if (!hex) return 'var(--bs-white)';
    const c = hex.replace('#', '');
    const r = parseInt(c.substring(0, 2), 16);
    const g = parseInt(c.substring(2, 4), 16);
    const b = parseInt(c.substring(4, 6), 16);
    const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    return lum > 0.55 ? 'var(--bs-dark)' : 'var(--bs-white)';
  }

  /** After unchecking children, uncheck all ancestors */
  private bubbleUncheckUp(nodeId: string, checked: Set<string>): void {
    let currentId = this.parentMap.get(nodeId);
    while (currentId) {
      checked.delete(currentId);
      currentId = this.parentMap.get(currentId) ?? null;
    }
  }

}
