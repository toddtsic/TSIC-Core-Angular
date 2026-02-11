import {
  Component, Input, Output, EventEmitter, signal, computed,
  ChangeDetectionStrategy, SimpleChanges, OnChanges
} from '@angular/core';
import type { LadtTreeNodeDto } from '@core/api';

/** Flat node used for rendering */
interface TreeFlatNode {
  id: string;
  parentId: string | null;
  name: string;
  level: number;
  isLeaf: boolean;
  teamCount: number;
  playerCount: number;
  expandable: boolean;
  isSpecial: boolean;
  /** All descendant IDs (for cascade check/uncheck) */
  descendantIds: string[];
}

@Component({
  selector: 'app-ladt-tree-filter',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ladt-tree-filter">
      <div class="tree-toolbar">
        <button type="button" class="tree-btn" (click)="expandAll()" title="Expand All">
          <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24"
               fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </button>
        <button type="button" class="tree-btn" (click)="collapseAll()" title="Collapse All">
          <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24"
               fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="18 15 12 9 6 15"></polyline>
          </svg>
        </button>
        <span class="tree-badges tree-toolbar-badges">
          <span class="tree-badge-label">Teams</span>
          <span class="tree-badge-label">Players</span>
        </span>
      </div>

      @for (node of visibleNodes(); track node.id) {
        <div class="tree-node"
             [class.tree-special]="node.isSpecial"
             [style.padding-left.rem]="node.level * 1.25">
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
                 [checked]="checkedIds.has(node.id)"
                 [indeterminate]="checkState().get(node.id) === 'some'"
                 (change)="onCheck(node, $event)" />

          <!-- Name -->
          <span class="tree-name" (click)="toggleExpand(node)">{{ node.name }}</span>

          <!-- Count badges -->
          <span class="tree-badges">
            <span class="tree-badge badge-teams">{{ node.teamCount }}</span>
            <span class="tree-badge badge-players">{{ node.playerCount }}</span>
          </span>
        </div>
      }

      <!-- Totals row -->
      @if (flatNodes().length > 0) {
        <div class="tree-totals">
          <span>Totals</span>
          <span class="tree-badges">
            <span class="tree-badge badge-teams">{{ totalTeams() }}</span>
            <span class="tree-badge badge-players">{{ totalPlayers() }}</span>
          </span>
        </div>
      }
    </div>
  `,
  styles: [`
    .ladt-tree-filter {
      font-size: 0.8125rem;
      line-height: 1.4;
    }

    .tree-toolbar {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      padding: var(--space-1) 0;
      border-bottom: 1px solid var(--bs-border-color);
      margin-bottom: var(--space-1);
    }

    .tree-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 24px;
      height: 24px;
      border: 1px solid var(--bs-border-color);
      border-radius: var(--bs-border-radius);
      background: var(--bs-body-bg);
      color: var(--bs-secondary-color);
      cursor: pointer;
      padding: 0;
    }

    .tree-btn:hover {
      background: var(--bs-tertiary-bg);
      color: var(--bs-body-color);
    }

    .tree-badges {
      display: flex;
      gap: var(--space-1);
      margin-left: auto;
      flex-shrink: 0;
    }

    .tree-toolbar-badges {
      gap: var(--space-2);
    }

    .tree-badge-label {
      font-size: 0.625rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.02em;
      color: var(--bs-secondary-color);
      min-width: 32px;
      text-align: center;
    }

    .tree-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: 24px;
      height: 18px;
      padding: 0 var(--space-1);
      border-radius: 9px;
      font-size: 0.625rem;
      font-weight: 700;
      font-variant-numeric: tabular-nums;
    }

    .badge-teams {
      background: var(--bs-info);
      color: var(--bs-white, #fff);
    }

    .badge-players {
      background: var(--bs-primary);
      color: var(--bs-white, #fff);
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

    .tree-special {
      opacity: 0.6;
      font-style: italic;
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

    .tree-name {
      flex: 1;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      cursor: pointer;
    }

    .tree-totals {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: var(--space-1) 4px;
      border-top: 1px solid var(--bs-border-color);
      margin-top: var(--space-1);
      font-weight: 600;
      font-size: 0.8125rem;
      color: var(--bs-body-color);
    }

    .tree-totals .tree-badges {
      margin-left: auto;
    }

    .tree-totals .tree-badge {
      font-size: 0.6875rem;
      min-width: 28px;
      height: 20px;
    }
  `]
})
export class LadtTreeFilterComponent implements OnChanges {
  @Input() treeData: LadtTreeNodeDto[] = [];
  @Input() checkedIds = new Set<string>();
  @Output() checkedIdsChange = new EventEmitter<Set<string>>();

  // Internal state
  flatNodes = signal<TreeFlatNode[]>([]);
  expandedIds = signal<Set<string>>(new Set());
  private parentMap = new Map<string, string | null>(); // childId → parentId

  // Totals
  totalTeams = signal(0);
  totalPlayers = signal(0);

  // Visible nodes (respects expansion)
  visibleNodes = computed(() => {
    const nodes = this.flatNodes();
    const expanded = this.expandedIds();
    const visible: TreeFlatNode[] = [];

    for (const node of nodes) {
      // A node is visible if all its ancestors are expanded
      if (this.isNodeVisible(node, expanded)) {
        visible.push(node);
      }
    }
    return visible;
  });

  // Check state per node: 'all' | 'some' | 'none'
  checkState = computed(() => {
    const checked = this.checkedIds;
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
    if (changes['treeData'] && this.treeData?.length) {
      this.buildFlatNodes();
    }
  }

  private buildFlatNodes(): void {
    const result: TreeFlatNode[] = [];
    this.parentMap.clear();
    let totalTeams = 0;
    let totalPlayers = 0;

    const collectDescendantIds = (node: LadtTreeNodeDto): string[] => {
      const children = (node.children ?? []) as LadtTreeNodeDto[];
      const ids: string[] = [];
      for (const child of children) {
        ids.push(child.id);
        ids.push(...collectDescendantIds(child));
      }
      return ids;
    };

    const recurse = (items: LadtTreeNodeDto[]) => {
      for (const node of items) {
        let children = (node.children ?? []) as LadtTreeNodeDto[];

        // Sort agegroups: regular alpha first, then specials (Dropped Teams, WAITLIST*)
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
            const aU = a.name.toUpperCase() === 'UNASSIGNED';
            const bU = b.name.toUpperCase() === 'UNASSIGNED';
            if (aU !== bU) return aU ? -1 : 1;
            return a.name.localeCompare(b.name);
          });
        }

        // Track parent relationships
        for (const child of children) {
          this.parentMap.set(child.id, node.id);
        }
        if (!this.parentMap.has(node.id)) {
          this.parentMap.set(node.id, null);
        }

        // Aggregate totals at league level
        if (node.level === 0) {
          totalTeams += node.teamCount;
          totalPlayers += node.playerCount;
        }

        const descendantIds = collectDescendantIds(node);

        result.push({
          id: node.id,
          parentId: node.parentId ?? null,
          name: node.name,
          level: node.level,
          isLeaf: node.isLeaf,
          teamCount: node.teamCount,
          playerCount: node.playerCount,
          expandable: children.length > 0,
          isSpecial: (node.level === 1 && this.isSpecialAgegroup(node.name)) ||
                     (node.level === 2 && node.name.toUpperCase() === 'UNASSIGNED'),
          descendantIds
        });

        if (children.length > 0) {
          recurse(children);
        }
      }
    };

    recurse(this.treeData);
    this.flatNodes.set(result);
    this.totalTeams.set(totalTeams);
    this.totalPlayers.set(totalPlayers);

    // Default: expand league level
    const leagueIds = result.filter(n => n.level === 0).map(n => n.id);
    this.expandedIds.set(new Set(leagueIds));
  }

  private isSpecialAgegroup(name: string): boolean {
    const upper = name.toUpperCase();
    return upper === 'DROPPED TEAMS' || upper.startsWith('WAITLIST');
  }

  private isNodeVisible(node: TreeFlatNode, expanded: Set<string>): boolean {
    // Walk up parent chain - all ancestors must be expanded
    let currentId = node.parentId;
    while (currentId) {
      if (!expanded.has(currentId)) return false;
      currentId = this.parentMap.get(currentId) ?? null;
    }
    return true;
  }

  toggleExpand(node: TreeFlatNode): void {
    if (!node.expandable) return;
    this.expandedIds.update(ids => {
      const next = new Set(ids);
      if (next.has(node.id)) next.delete(node.id);
      else next.add(node.id);
      return next;
    });
  }

  expandAll(): void {
    const allExpandable = this.flatNodes().filter(n => n.expandable).map(n => n.id);
    this.expandedIds.set(new Set(allExpandable));
  }

  collapseAll(): void {
    // Keep only league level expanded
    const leagueIds = this.flatNodes().filter(n => n.level === 0).map(n => n.id);
    this.expandedIds.set(new Set(leagueIds));
  }

  onCheck(node: TreeFlatNode, event: Event): void {
    const isChecked = (event.target as HTMLInputElement).checked;
    const next = new Set(this.checkedIds);

    if (isChecked) {
      // Check this node + all descendants
      next.add(node.id);
      for (const id of node.descendantIds) next.add(id);
      // Also check ancestors if all their children are now checked
      this.bubbleCheckUp(node.id, next);
    } else {
      // Uncheck this node + all descendants
      next.delete(node.id);
      for (const id of node.descendantIds) next.delete(id);
      // Uncheck ancestors (they can't be fully checked anymore)
      this.bubbleUncheckUp(node.id, next);
    }

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

  /** After unchecking children, uncheck all ancestors */
  private bubbleUncheckUp(nodeId: string, checked: Set<string>): void {
    let currentId = this.parentMap.get(nodeId);
    while (currentId) {
      checked.delete(currentId);
      currentId = this.parentMap.get(currentId) ?? null;
    }
  }
}
