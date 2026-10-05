import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { UploadItemProgress } from './document.models';
import { UploadService } from './upload.service';

@Component({
  selector: 'app-add-pages-dialog',
  standalone: true,
  templateUrl: './add-pages-dialog.component.html',
  styleUrl: './add-pages-dialog.component.scss',
})
export class AddPagesDialogComponent {
  private readonly uploads = inject(UploadService);
  protected readonly i18n = inject(I18nService);
  @Input({ required: true }) documentId = '';
  @Output() readonly closed = new EventEmitter<void>();
  @Output() readonly completed = new EventEmitter<string[]>();
  protected readonly selected = signal<File[]>([]);
  protected readonly running = signal(false);
  protected readonly items = this.uploads.items;

  protected choose(event: Event): void {
    this.selected.set(Array.from((event.target as HTMLInputElement).files ?? []));
  }

  protected async add(): Promise<void> {
    if (!this.selected().length || this.running()) return;
    this.running.set(true);
    try {
      const results = await this.uploads.addFiles(this.documentId, this.selected());
      if (results.every((item) => item.stage === 'accepted')) {
        this.completed.emit(results.flatMap((item) => (item.uploadId ? [item.uploadId] : [])));
      }
    } finally {
      this.running.set(false);
    }
  }

  protected label(item: UploadItemProgress): string {
    if (item.errorCode) return `${this.stage(item.stage)}: ${item.errorCode.replaceAll('_', ' ')}`;
    if (item.stage === 'expanding' && item.discoveredPageCount) {
      return this.i18n.t('pages.add.creating', { created: item.createdPageCount ?? 0, total: item.discoveredPageCount });
    }
    return this.stage(item.stage);
  }

  private stage(stage: string): string {
    const key = 'pages.add.stage.' + stage;
    const text = this.i18n.t(key);
    return text === key ? stage : text;
  }
}
