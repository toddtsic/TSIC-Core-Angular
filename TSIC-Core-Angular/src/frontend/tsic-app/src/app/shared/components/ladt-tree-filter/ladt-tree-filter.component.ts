import {
  Component, Input, Output, EventEmitter, signal, computed,
  ChangeDetectionStrategy, SimpleChanges, OnChanges
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { LadtAgegroupNode } from '@core/api';

/**
 * Flat node for rendering the 3-level LADT filter tree: Agegroup \u2192 Division \u2192 Team.
 * Levels: 0=agegroup, 1=division, 2=team. Metadata flags (isScheduled/hasClubRep on teams,
 * isWaitlist/isDropped on agegroups) drive the [requireScheduled] / [requireClubRep] /
 * [excludeWaitlistDropped] filter flags.
 */
interface LadtFlatNode {
  id: string;
  parentId: string | null;
  name: string;
  level: number;
  isLeaf: boolean;
  expandable: boolean;
  color: string | null;
  teamCount: number;
  playerCount: number;
  descendantIds: string[];
  isScheduled?: boolean;
  hasClubRep?: boolean;
  isWaitlist?: boolean;
  isDropped?: boolean;
}

export interface LadtSelectionEvent {
  agegroupIds: string[];
  divisionIds: string[];
  teamIds: string[];
}

@Component({
  selector: 'app-ladt-tree-filter',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ladt-tree-filter">
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

      <div class="tree-col-headers">
        <span class="tree-col-header">Teams</span>
        <span class="tree-col-header">Players</span>
      </div>

      <div class="tree-container">
        @for (node of visibleNodes(); track node.id) {
          @if (node.id === 'root:job') {
            <div class="tree-node tree-root" (click)="toggleExpand(node)">
              <svg class="tree-root-chevron" [class.expanded]="expandedIds().has(node.id)"
                   xmlns="http://www.w3.org/2000/svg" width="10" height="10" viewBox="0 0 24 24"
                   fill="none" stroke="currentColor" stroke-width="2.5">
                <polyline points="9 6 15 12 9 18"></polyline>
              </svg>
              <span class="tree-name">All</span>
              <span class="tree-root-actions">
                <button class="tree-toggle-btn" title="Expand all" (click)="expandAll(); $event.stopPropagation()">
                  <svg xmlns="http://www.w3.org/2000/svg" width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="7 8 12 13 17 8"></polyline><polyline points="7 14 12 19 17 14"></polyline></svg>
                </button>
                <button class="tree-toggle-btn" title="Collapse all" (click)="collapseAll(); $event.stopPropagation()">
                  <svg xmlns="http://www.w3.org/2000/svg" width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="7 13 12 8 17 13"></polyline><polyline points="7 19 12 14 17 19"></polyline></svg>
                </button>
              </span>
              <span class="tree-badges">
                <span class="tree-badge" title="Teams">{{ node.teamCount }}</span>
                <span class="tree-badge" title="Players">{{ node.playerCount }}</span>
              </span>
            </div>
          } @else {
            <div class="tree-node"
                 [style.padding-left.rem]="(node.level < 1 ? 0 : node.level - 1) * 1.25">
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

              <input type="checkbox"
                     class="tree-checkbox"
                     [checked]="checkedIdsSignal().has(node.id)"
                     [indeterminate]="checkState().get(node.id) === 'some'"
                     (change)="onCheck(node, $event)" />

              @if (node.color) {
                <span class="tree-color-dot" [style.background]="node.color"></span>
              }

              <span class="tree-name" (click)="toggleExpand(node)">{{ node.name }}</span>

              <span class="tree-badges">
                @if (!node.isLeaf) {
                  <span class="tree-badge"
                        [class.is-zero]="node.teamCount === 0"
                        title="Teams">{{ node.teamCount }}</span>
                }
                <span class="tree-badge"
                      [class.is-zero]="node.playerCount === 0"
                      title="Players">{{ node.playerCount }}</span>
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
    .ladt-tree-filter {
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

    .tree-search { position: relative; margin-bottom: var(--space-2, 8px); flex-shrink: 0; }
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
    .tree-search-input::placeholder { color: var(--bs-secondary-color); }
    .tree-search-clear {
      position: absolute; right: var(--space-1, 4px); top: 50%; transform: translateY(-50%);
      background: none; border: none; color: var(--bs-secondary-color);
      font-size: 1rem; cursor: pointer; padding: 0 var(--space-1, 4px); line-height: 1;
    }
    .tree-search-clear:hover { color: var(--bs-body-color); }

    .tree-col-headers {
      display: flex; justify-content: flex-end; gap: 6px;
      padding: 0 4px 2px;
      color: var(--bs-secondary-color);
      font-size: 0.55rem; font-weight: 600;
      text-transform: uppercase; letter-spacing: 0.5px;
    }
    .tree-col-header { min-width: 24px; text-align: center; }

    .tree-container { overflow-x: hidden; }
    .tree-node {
      display: flex; align-items: center; gap: 4px;
      padding: 2px 4px; border-radius: var(--bs-border-radius); cursor: default;
    }
    .tree-node:hover { background: var(--bs-tertiary-bg); }

    .tree-expand {
      display: inline-flex; align-items: center; justify-content: center;
      width: 16px; height: 16px; flex-shrink: 0; cursor: pointer;
      color: var(--bs-secondary-color); transition: transform 0.15s;
    }
    .tree-expand.expanded { transform: rotate(90deg); }
    .tree-expand-spacer { display: inline-block; width: 16px; flex-shrink: 0; }

    .tree-checkbox {
      accent-color: var(--bs-primary);
      width: 15px; height: 15px; flex-shrink: 0; cursor: pointer;
    }

    .tree-color-dot {
      display: inline-block; width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0;
    }

    .tree-name {
      flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer;
    }

    .tree-root {
      border-bottom: 1px solid var(--bs-border-color-translucent);
      margin-bottom: 2px; padding-bottom: 2px;
      font-weight: 600; color: var(--bs-secondary-color);
      font-size: 0.6875rem; cursor: pointer;
    }
    .tree-root-actions { display: inline-flex; gap: 2px; }
    .tree-toggle-btn {
      display: inline-flex; align-items: center; justify-content: center;
      width: 16px; height: 16px; padding: 0; border: none;
      border-radius: var(--bs-border-radius); background: transparent;
      color: var(--bs-secondary-color); cursor: pointer;
    }
    .tree-toggle-btn:hover { background: var(--bs-tertiary-bg); color: var(--bs-body-color); }
    .tree-root-chevron {
      flex-shrink: 0; color: var(--bs-secondary-color); opacity: 0.6;
      transition: transform 0.15s; cursor: pointer;
    }
    .tree-root-chevron.expanded { transform: rotate(90deg); }

    .tree-badges { margin-left: auto; display: flex; gap: 6px; flex-shrink: 0; }
    .tree-badge {
      flex-shrink: 0; display: inline-flex; align-items: center; justify-content: center;
      min-width: 24px; height: 18px; padding: 0 var(--space-1, 4px);
      border-radius: 9px;
      background: var(--bs-tertiary-bg); color: var(--bs-body-color);
      font-size: 0.625rem; font-weight: 700; font-variant-numeric: tabular-nums;
    }
    .tree-badge.is-zero { opacity: 0.4; }

    .badge-muted {
      background: color-mix(in srgb, var(--bs-secondary) 40%, var(--bs-card-bg));
      color: var(--bs-body-color);
    }
    .badge-outline {
      background: transparent; border: 1.5px solid var(--bs-body-color); color: var(--bs-body-color);
    }
    .tree-empty {
      padding: var(--space-2, 8px) var(--space-3, 12px);
      color: var(--bs-secondary-color); font-style: italic;
    }
  `]
})
export class LadtTreeFilterComponent implements OnChanges {
  /** LADT data: Agegroup \u2192 Division \u2192 Team. */
  @Input() treeData: LadtAgegroupNode[] = [];
  @Input() hideRootLevel = false;
  @Input() searchPlaceholder = 'Filter agegroups...';
  @Input() showSearch = true;
  /** When set, wraps all agegroups under a synthetic root node (e.g. job name). */
  @Input() rootLabel = '';
  @Input() headerLabel = '';
  @Output() checkedIdsChange = new EventEmitter<Set<string>>();

  readonly checkedIdsSignal = signal(new Set<string>());

  @Input() set checkedIds(value: Set<string>) {
    this.checkedIdsSignal.set(value);
  }

  // Filter flag inputs
  @Input() set requireScheduled(value: boolean) { this.requireScheduledSig.set(value); }
  @Input() set requireClubRep(value: boolean) { this.requireClubRepSig.set(value); }
  @Input() set excludeWaitlistDropped(value: boolean) { this.excludeWaitlistDroppedSig.set(value); }

  private readonly requireScheduledSig = signal(false);
  private readonly requireClubRepSig = signal(false);
  private readonly excludeWaitlistDroppedSig = signal(false);

  flatNodes = signal<LadtFlatNode[]>([]);
  expandedIds = signal<Set<string>>(new Set());
  searchTerm = signal('');
  private parentMap = new Map<string, string | null>();

  visibleNodes = computed(() => {
    const nodes = this.flatNodes();
    const expanded = this.expandedIds();
    const term = this.searchTerm().trim().toLowerCase();
    const requireScheduled = this.requireScheduledSig();
    const requireClubRep = this.requireClubRepSig();
    const excludeWaitlistDropped = this.excludeWaitlistDroppedSig();

    // Pass 1: flag-filter prune, cascading upward. No-op when all flags false.
    let flagFiltered = nodes;
    if (requireScheduled || requireClubRep || excludeWaitlistDropped) {
      const excludedAgIds = new Set<string>();
      if (excludeWaitlistDropped) {
        for (const n of nodes) {
          if (n.isWaitlist || n.isDropped) excludedAgIds.add(n.id);
        }
      }
      const keptIds = new Set<string>();
      for (const n of nodes) {
        if (!n.isLeaf) continue;
        if (requireScheduled && !n.isScheduled) continue;
        if (requireClubRep && !n.hasClubRep) continue;
        let blocked = false;
        let cur: string | null = n.parentId;
        while (cur) {
          if (excludedAgIds.has(cur)) { blocked = true; break; }
          cur = this.parentMap.get(cur) ?? null;
        }
        if (blocked) continue;
        keptIds.add(n.id);
        cur = n.parentId;
        while (cur) {
          keptIds.add(cur);
          cur = this.parentMap.get(cur) ?? null;
        }
      }
      flagFiltered = nodes.filter(n => keptIds.has(n.id));
    }

    // Pass 2: search by agegroup name.
    let filteredNodes = flagFiltered;
    if (term) {
      const matchingRootIds = new Set<string>();
      for (const node of flagFiltered) {
        if (node.id.startsWith('ag:') && node.name.toLowerCase().includes(term)) {
          matchingRootIds.add(node.id);
        }
      }
      filteredNodes = flagFiltered.filter(node => {
        if (node.id === 'root:job') return true;
        if (node.id.startsWith('ag:') && (node.parentId === 'root:job' || !node.parentId)) {
          return matchingRootIds.has(node.id);
        }
        let ancestorId: string | null = node.parentId;
        while (ancestorId) {
          if (matchingRootIds.has(ancestorId)) return true;
          ancestorId = this.parentMap.get(ancestorId) ?? null;
        }
        return false;
      });
    }

    // Pass 3: expansion visibility
    const visible: LadtFlatNode[] = [];
    for (const node of filteredNodes) {
      if (this.hideRootLevel && node.level === 0) continue;
      if (this.isNodeVisible(node, expanded, filteredNodes)) {
        visible.push(node);
      }
    }
    return visible;
  });

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
    if (changes['treeData'] || changes['rootLabel']) {
      if (this.treeData?.length) {
        this.buildFlatNodes();
      } else {
        this.flatNodes.set([]);
        this.parentMap.clear();
      }
    }
  }

  private buildFlatNodes(): void {
    const result: LadtFlatNode[] = [];
    this.parentMap.clear();
    const levelOffset = this.rootLabel ? 1 : 0;

    if (this.rootLabel) {
      const rootId = 'root:job';
      this.parentMap.set(rootId, null);

      const allDescendants: string[] = [];
      let totalTeams = 0;
      let totalPlayers = 0;
      for (const ag of this.treeData) {
        allDescendants.push(`ag:${ag.agegroupId}`);
        totalTeams += ag.teamCount ?? 0;
        totalPlayers += ag.playerCount ?? 0;
        for (const div of ag.divisions ?? []) {
          allDescendants.push(`div:${div.divId}`);
          for (const team of div.teams ?? []) {
            allDescendants.push(`team:${team.teamId}`);
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

    for (const ag of this.treeData) {
      const agId = `ag:${ag.agegroupId}`;
      const agParent = this.rootLabel ? 'root:job' : null;
      this.parentMap.set(agId, agParent);

      const agDescendants: string[] = [];
      for (const div of ag.divisions ?? []) {
        const divId = `div:${div.divId}`;
        agDescendants.push(divId);
        for (const team of div.teams ?? []) {
          agDescendants.push(`team:${team.teamId}`);
        }
      }

      const agColor = ag.color ?? null;

      result.push({
        id: agId,
        parentId: agParent,
        name: ag.agegroupName,
        level: 0 + levelOffset,
        isLeaf: (ag.divisions ?? []).length === 0,
        expandable: (ag.divisions ?? []).length > 0,
        color: agColor,
        teamCount: ag.teamCount ?? 0,
        playerCount: ag.playerCount ?? 0,
        descendantIds: agDescendants,
        isWaitlist: ag.isWaitlist ?? false,
        isDropped: ag.isDropped ?? false
      });

      for (const div of ag.divisions ?? []) {
        const divId = `div:${div.divId}`;
        this.parentMap.set(divId, agId);

        const divDescendants = (div.teams ?? []).map(t => `team:${t.teamId}`);

        result.push({
          id: divId,
          parentId: agId,
          name: div.divName,
          level: 1 + levelOffset,
          isLeaf: (div.teams ?? []).length === 0,
          expandable: (div.teams ?? []).length > 0,
          color: agColor,
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
            level: 2 + levelOffset,
            isLeaf: true,
            expandable: false,
            color: agColor,
            teamCount: 0,
            playerCount: team.playerCount ?? 0,
            descendantIds: [],
            isScheduled: team.isScheduled ?? false,
            hasClubRep: team.hasClubRep ?? false
          });
        }
      }
    }

    this.flatNodes.set(result);
    this.expandedIds.set(new Set());
  }

  private isNodeVisible(
    node: LadtFlatNode,
    expanded: Set<string>,
    filteredNodes: LadtFlatNode[]
  ): boolean {
    if (!node.parentId) return true;
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

  toggleExpand(node: LadtFlatNode): void {
    if (!node.expandable) return;
    this.expandedIds.update(ids => {
      const next = new Set(ids);
      if (next.has(node.id)) next.delete(node.id);
      else next.add(node.id);
      return next;
    });
  }

  onCheck(node: LadtFlatNode, event: Event): void {
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

  private bubbleUncheckUp(nodeId: string, checked: Set<string>): void {
    let currentId = this.parentMap.get(nodeId);
    while (currentId) {
      checked.delete(currentId);
      currentId = this.parentMap.get(currentId) ?? null;
    }
  }

  contrastText(hex: string | null): string {
    if (!hex) return 'var(--bs-white)';
    const c = hex.replace('#', '');
    const r = parseInt(c.substring(0, 2), 16);
    const g = parseInt(c.substring(2, 4), 16);
    const b = parseInt(c.substring(4, 6), 16);
    const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    return lum > 0.55 ? 'var(--bs-dark)' : 'var(--bs-white)';
  }
}
