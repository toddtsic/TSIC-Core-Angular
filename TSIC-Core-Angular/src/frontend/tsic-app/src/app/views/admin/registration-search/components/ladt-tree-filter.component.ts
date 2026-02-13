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
    </div>
  `,
  styles: [`
    .ladt-tree-filter {
      font-size: 0.8125rem;
      line-height: 1.4;
    }

    .tree-badges {
      display: flex;
      gap: var(--space-1);
      margin-left: auto;
      flex-shrink: 0;
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
      color: var(--bs-white);
    }

    .badge-players {
      background: var(--bs-primary);
      color: var(--bs-white);
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

    // Default: start fully collapsed — user expands as needed
    this.expandedIds.set(new Set());
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
