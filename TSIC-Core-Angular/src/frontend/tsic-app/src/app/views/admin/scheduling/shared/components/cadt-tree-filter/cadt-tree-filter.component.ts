import {
  Component, Input, Output, EventEmitter, signal, computed,
  ChangeDetectionStrategy, SimpleChanges, OnChanges
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { CadtClubNode, CadtAgegroupNode, CadtDivisionNode, CadtTeamNode } from '@core/api';

/** Flat node used for rendering the CADT tree */
interface CadtFlatNode {
  id: string;
  parentId: string | null;
  name: string;
  level: number;       // 0=club, 1=agegroup, 2=division, 3=team
  isLeaf: boolean;
  expandable: boolean;
  color: string | null; // agegroup color dot
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
      <div class="tree-search">
        <input type="text"
               class="tree-search-input"
               placeholder="Filter clubs..."
               [ngModel]="searchTerm()"
               (ngModelChange)="searchTerm.set($event)" />
        @if (searchTerm()) {
          <button class="tree-search-clear" (click)="searchTerm.set('')">&times;</button>
        }
      </div>

      <!-- Tree nodes -->
      <div class="tree-container">
        @for (node of visibleNodes(); track node.id) {
          <div class="tree-node"
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

            <!-- Agegroup color dot -->
            @if (node.color) {
              <span class="tree-color-dot"
                    [style.background]="node.color"></span>
            }

            <!-- Name -->
            <span class="tree-name" (click)="toggleExpand(node)">{{ node.name }}</span>
          </div>
        }

        @if (visibleNodes().length === 0 && searchTerm()) {
          <div class="tree-empty">No clubs match "{{ searchTerm() }}"</div>
        }
      </div>
    </div>
  `,
  styles: [`
    .cadt-tree-filter {
      font-size: var(--font-size-sm, 0.8125rem);
      line-height: 1.4;
      display: flex;
      flex-direction: column;
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
      max-height: 420px;
      overflow-y: auto;
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

    .tree-empty {
      padding: var(--space-2, 8px) var(--space-3, 12px);
      color: var(--bs-secondary-color);
      font-style: italic;
    }
  `]
})
export class CadtTreeFilterComponent implements OnChanges {
  @Input() treeData: CadtClubNode[] = [];
  @Input() checkedIds = new Set<string>();
  @Output() checkedIdsChange = new EventEmitter<CadtSelectionEvent>();

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
        if (node.level === 0 && node.name.toLowerCase().includes(term)) {
          matchingClubIds.add(node.id);
        }
      }
      filteredNodes = nodes.filter(node => {
        if (node.level === 0) return matchingClubIds.has(node.id);
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
      if (this.isNodeVisible(node, expanded, filteredNodes)) {
        visible.push(node);
      }
    }
    return visible;
  });

  /** Check state per node: 'all' | 'some' | 'none' */
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
    const result: CadtFlatNode[] = [];
    this.parentMap.clear();

    for (const club of this.treeData) {
      const clubId = `club:${club.clubName}`;
      this.parentMap.set(clubId, null);

      const clubDescendants: string[] = [];

      // Pre-collect all descendant IDs for the club
      for (const ag of club.agegroups ?? []) {
        const agId = `ag:${ag.agegroupId}`;
        clubDescendants.push(agId);
        for (const div of ag.divisions ?? []) {
          const divId = `div:${div.divId}`;
          clubDescendants.push(divId);
          for (const team of div.teams ?? []) {
            clubDescendants.push(`team:${team.teamId}`);
          }
        }
      }

      result.push({
        id: clubId,
        parentId: null,
        name: club.clubName,
        level: 0,
        isLeaf: (club.agegroups ?? []).length === 0,
        expandable: (club.agegroups ?? []).length > 0,
        color: null,
        descendantIds: clubDescendants
      });

      for (const ag of club.agegroups ?? []) {
        const agId = `ag:${ag.agegroupId}`;
        this.parentMap.set(agId, clubId);

        const agDescendants: string[] = [];
        for (const div of ag.divisions ?? []) {
          const divId = `div:${div.divId}`;
          agDescendants.push(divId);
          for (const team of div.teams ?? []) {
            agDescendants.push(`team:${team.teamId}`);
          }
        }

        result.push({
          id: agId,
          parentId: clubId,
          name: ag.agegroupName,
          level: 1,
          isLeaf: (ag.divisions ?? []).length === 0,
          expandable: (ag.divisions ?? []).length > 0,
          color: ag.color ?? null,
          descendantIds: agDescendants
        });

        for (const div of ag.divisions ?? []) {
          const divId = `div:${div.divId}`;
          this.parentMap.set(divId, agId);

          const divDescendants: string[] = [];
          for (const team of div.teams ?? []) {
            divDescendants.push(`team:${team.teamId}`);
          }

          result.push({
            id: divId,
            parentId: agId,
            name: div.divName,
            level: 2,
            isLeaf: (div.teams ?? []).length === 0,
            expandable: (div.teams ?? []).length > 0,
            color: null,
            descendantIds: divDescendants
          });

          for (const team of div.teams ?? []) {
            const teamId = `team:${team.teamId}`;
            this.parentMap.set(teamId, divId);

            result.push({
              id: teamId,
              parentId: divId,
              name: team.teamName,
              level: 3,
              isLeaf: true,
              expandable: false,
              color: null,
              descendantIds: []
            });
          }
        }
      }
    }

    this.flatNodes.set(result);
    // Start fully collapsed
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
    const next = new Set(this.checkedIds);

    if (isChecked) {
      next.add(node.id);
      for (const id of node.descendantIds) next.add(id);
      this.bubbleCheckUp(node.id, next);
    } else {
      next.delete(node.id);
      for (const id of node.descendantIds) next.delete(id);
      this.bubbleUncheckUp(node.id, next);
    }

    this.emitSelection(next);
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

  /** Resolve the flat set of IDs into the structured selection output */
  private emitSelection(checked: Set<string>): void {
    const clubNames: string[] = [];
    const agegroupIds: string[] = [];
    const divisionIds: string[] = [];
    const teamIds: string[] = [];

    for (const id of checked) {
      if (id.startsWith('club:')) clubNames.push(id.substring(5));
      else if (id.startsWith('ag:')) agegroupIds.push(id.substring(3));
      else if (id.startsWith('div:')) divisionIds.push(id.substring(4));
      else if (id.startsWith('team:')) teamIds.push(id.substring(5));
    }

    this.checkedIdsChange.emit({ clubNames, agegroupIds, divisionIds, teamIds });
  }
}
