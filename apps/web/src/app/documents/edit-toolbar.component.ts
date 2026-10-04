import { DatePipe } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, computed, inject, input, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { EDIT_TOOLS, EditTool, EditToolId, opensInWorkspace } from './edit-tools';
import { PageEditHistory } from './text-edit.models';
import { TextEditService } from './text-edit.service';
import { IconComponent, IconName } from './ui-icon.component';

const TOOL_ICONS: Record<EditToolId, IconName> = {
  crop: 'crop', clean: 'clean', text: 'text', add: 'addText', sign: 'signature', mark: 'tick', ocr: 'recognize',
};
/** Toolbar groups, in display order. Search text joins the Text group (it needs recognized text). */
const TOOL_GROUPS: readonly { label: string; ids: readonly EditToolId[] }[] = [
  { label: 'Page', ids: ['crop', 'clean'] },
  { label: 'Text', ids: ['ocr', 'text', 'add'] },
  { label: 'Sign & mark', ids: ['sign', 'mark'] },
];

/** Navigates to the editor that implements a tool, for the selected page. */
export function openEditTool(router: Router, documentId: string, pageId: string, tool: EditTool): Promise<boolean> {
  const commands = ['/documents', documentId, 'pages', pageId, tool.path];
  return tool.query
    ? router.navigate(commands, { queryParams: { tool: tool.query } })
    : router.navigate(commands, undefined);
}

@Component({
  selector: 'app-edit-toolbar',
  standalone: true,
  imports: [IconComponent, DatePipe],
  templateUrl: './edit-toolbar.component.html',
  styleUrl: './edit-toolbar.component.scss',
})
export class EditToolbarComponent implements OnChanges {
  private readonly router = inject(Router);
  private readonly textEdits = inject(TextEditService);

  @Input({ required: true }) documentId = '';
  @Input() pageId: string | null = null;
  @Input() tool: EditToolId = 'crop';
  /** Whether the search bar is open (Search text lives in the Text group). */
  @Input() searchOpen = false;
  @Output() readonly searchToggle = new EventEmitter<void>();
  @Output() readonly toolChange = new EventEmitter<EditToolId>();
  /** A tool that opens inside the workspace was chosen; the workspace starts it (no navigation). */
  @Output() readonly startTool = new EventEmitter<EditTool>();
  /** Emitted after an undo or redo so the workspace reloads the page previews. */
  @Output() readonly pageChanged = new EventEmitter<void>();

  protected readonly tools = EDIT_TOOLS;
  protected readonly icons = TOOL_ICONS;
  protected readonly groups = TOOL_GROUPS.map((group) => ({
    label: group.label,
    tools: group.ids.map((id) => EDIT_TOOLS.find((tool) => tool.id === id)!),
  }));
  protected readonly history = signal<PageEditHistory | null>(null);
  protected readonly historyOpen = signal(false);
  protected readonly historyBusy = signal(false);
  protected readonly historyError = signal('');
  protected readonly announcement = signal('');
  private generation = 0;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['pageId'] || changes['documentId']) {
      this.historyOpen.set(false);
      void this.loadHistory(false);
    }
  }

  protected choose(tool: EditTool): void {
    if (!this.pageId) return;
    this.toolChange.emit(tool.id);
    if (opensInWorkspace(tool)) this.startTool.emit(tool);
    else void openEditTool(this.router, this.documentId, this.pageId, tool);
  }

  protected async toggleHistory(): Promise<void> {
    if (this.historyOpen()) { this.historyOpen.set(false); return; }
    this.historyOpen.set(true);
    await this.loadHistory(true);
  }

  private async loadHistory(report: boolean): Promise<void> {
    const pageId = this.pageId;
    const run = ++this.generation;
    this.history.set(null);
    this.historyError.set('');
    if (!pageId) return;
    try {
      const history = await this.textEdits.history(this.documentId, pageId);
      if (run === this.generation) this.history.set(history);
    } catch {
      if (run === this.generation && report) this.historyError.set('Edit history is not available right now.');
    }
  }

  /** Every saved version, newest first, ending with the original page. */
  protected readonly versions = computed(() => {
    const h = this.history();
    if (!h) return [];
    const quote = (text?: string) => `“${(text ?? '').trim()}”`;
    const edits = h.entries
      .filter((e) => e.state === 'Succeeded' && e.resultRevisionId)
      .map((e) => ({
        revisionId: e.resultRevisionId!,
        label: !e.originalText?.trim()
          ? `Added ${quote(e.replacementText)}`
          : e.replacementText?.trim()
            ? `Replaced ${quote(e.originalText)} with ${quote(e.replacementText)}`
            : `Deleted ${quote(e.originalText)}`,
        at: e.completedAt ?? e.queuedAt ?? null,
      }))
      .reverse();
    const original = h.originalRevisionId ? [{ revisionId: h.originalRevisionId, label: 'Original page', at: null }] : [];
    return [...edits, ...original].map((v) => ({ ...v, current: v.revisionId === h.activeRevisionId }));
  });

  protected async jumpTo(revisionId: string): Promise<void> {
    const history = this.history();
    const pageId = this.pageId;
    if (!history || !pageId || this.historyBusy() || revisionId === history.activeRevisionId) return;
    this.historyBusy.set(true);
    this.historyError.set('');
    try {
      this.history.set(await this.textEdits.jumpTo(this.documentId, pageId, revisionId, history.activeRevisionId));
      this.announcement.set('Page version restored.');
      this.pageChanged.emit();
    } catch {
      this.historyError.set('The page changed or the action failed. Reload history and try again.');
    } finally {
      this.historyBusy.set(false);
    }
  }

  protected async switchRevision(direction: 'undo' | 'redo'): Promise<void> {
    const history = this.history();
    const pageId = this.pageId;
    if (!history || !pageId || this.historyBusy() ||
      !(direction === 'undo' ? history.canUndo : history.canRedo)) return;
    this.historyBusy.set(true);
    this.historyError.set('');
    try {
      this.history.set(await this.textEdits.switchRevision(this.documentId, pageId, direction, history.activeRevisionId));
      this.announcement.set(direction === 'undo' ? 'Text change undone.' : 'Text change restored.');
      this.pageChanged.emit();
    } catch {
      this.historyError.set('The page changed or the action failed. Reload history and try again.');
    } finally {
      this.historyBusy.set(false);
    }
  }
}

@Component({
  selector: 'app-edit-panel',
  standalone: true,
  template: `
    <p class="kicker">Edit{{ pageNumber() ? ' · page ' + pageNumber() : '' }}</p>
    <h2>{{ current().title }}</h2>
    <p class="body">{{ current().body }}</p>
    <ol>
      @for (step of current().steps; track $index) { <li>{{ step }}</li> }
    </ol>
    <button type="button" class="action" [disabled]="!pageId()" (click)="start.emit(current())">{{ current().action }}</button>
  `,
  styles: [`
    :host { display: block; }
    .kicker { margin: 0 0 4px; font-size: 12px; font-weight: 600; letter-spacing: .04em; text-transform: uppercase; color: #5b6660; }
    h2 { margin: 0 0 8px; font-family: 'Source Serif 4', Georgia, serif; font-size: 20px; font-weight: 600; }
    .body { margin: 0 0 16px; font-size: 14px; line-height: 1.5; color: #3f4a44; }
    ol { margin: 0 0 20px; padding-left: 22px; font-size: 14px; line-height: 1.6; }
    li { margin-bottom: 6px; }
    .action { min-height: 44px; padding: 0 18px; border: none; border-radius: 8px; background: #1e6b50; color: #fff; font-size: 14px; font-weight: 600; cursor: pointer; }
    .action:hover:not(:disabled) { background: #14503b; }
    .action:disabled { opacity: .5; cursor: default; }
  `],
})
export class EditPanelComponent {
  readonly tool = input<EditToolId>('crop');
  readonly pageId = input<string | null>(null);
  readonly pageNumber = input<number | null>(null);
  readonly start = output<EditTool>();
  protected readonly current = computed(() => EDIT_TOOLS.find((t) => t.id === this.tool()) ?? EDIT_TOOLS[0]);
}
