import { PageMarkDto } from './page-mark.models';

type Change = { id: string; before: PageMarkDto | null; after: PageMarkDto | null };
export type ApplyMarkChange = (target: PageMarkDto | null, currentId: string) => Promise<PageMarkDto | null>;

/** Session-only, mark-scoped history. The server remains canonical after every compensation. */
export class PageMarkHistory {
  private changes: Change[] = [];
  private cursor = 0;

  get canUndo(): boolean { return this.cursor > 0; }
  get canRedo(): boolean { return this.cursor < this.changes.length; }
  clear(): void { this.changes = []; this.cursor = 0; }
  record(before: PageMarkDto | null, after: PageMarkDto | null): void {
    if (!before && !after) return;
    this.changes = this.changes.slice(0, this.cursor);
    this.changes.push({ id: (after ?? before)!.id, before: this.copy(before), after: this.copy(after) });
    this.cursor = this.changes.length;
  }
  async undo(apply: ApplyMarkChange): Promise<void> {
    if (!this.canUndo) return;
    const change = this.changes[this.cursor - 1];
    const result = await apply(this.copy(change.before), change.id);
    if (result && result.id !== change.id) this.remap(change.id, result.id);
    this.cursor--;
  }
  async redo(apply: ApplyMarkChange): Promise<void> {
    if (!this.canRedo) return;
    const change = this.changes[this.cursor];
    const result = await apply(this.copy(change.after), change.id);
    if (result && result.id !== change.id) this.remap(change.id, result.id);
    this.cursor++;
  }
  private remap(oldId: string, newId: string): void {
    for (const change of this.changes) {
      if (change.id === oldId) change.id = newId;
      if (change.before?.id === oldId) change.before = { ...change.before, id: newId };
      if (change.after?.id === oldId) change.after = { ...change.after, id: newId };
    }
  }
  private copy(mark: PageMarkDto | null): PageMarkDto | null {
    return mark ? { ...mark, box: { ...mark.box } } : null;
  }
}
