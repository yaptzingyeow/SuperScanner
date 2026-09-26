import { Component, input, output } from '@angular/core';
import { PageMarkDraft, PageMarkKind } from './page-mark.models';

@Component({ selector: 'app-page-mark-tools', standalone: true,
  templateUrl: './page-mark-tools.component.html', styleUrl: './page-mark-tools.component.scss' })
export class PageMarkToolsComponent {
  readonly draft = input<PageMarkDraft | null>(null);
  readonly selected = input(false);
  readonly placing = input(false);
  readonly busy = input(false);
  readonly canUndo = input(false);
  readonly canRedo = input(false);
  readonly kind = output<PageMarkKind>();
  readonly color = output<string>();
  readonly strokeWidth = output<number>();
  readonly sizePercent = output<number>();
  readonly save = output<void>();
  readonly cancel = output<void>();
  readonly remove = output<void>();
  readonly edit = output<void>();
  readonly undo = output<void>();
  readonly redo = output<void>();
  readonly place = output<void>();
  protected colorChanged(event: Event): void { this.color.emit((event.target as HTMLInputElement).value); }
  protected strokeChanged(event: Event): void { this.strokeWidth.emit(Number((event.target as HTMLInputElement).value) / 100); }
  protected sizeChanged(event: Event): void { this.sizePercent.emit(Number((event.target as HTMLInputElement).value)); }
  protected size(): number { return Math.round((this.draft()?.box.width ?? .025) / .025 * 100); }
}
