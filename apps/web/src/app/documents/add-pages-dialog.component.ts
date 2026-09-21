import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
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
  @Input({ required: true }) documentId = '';
  @Output() readonly closed = new EventEmitter<void>();
  @Output() readonly completed = new EventEmitter<void>();
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
      if (results.some((item) => item.stage === 'accepted')) this.completed.emit();
    } finally {
      this.running.set(false);
    }
  }

  protected label(item: UploadItemProgress): string {
    if (item.errorCode) return `${item.stage}: ${item.errorCode.replaceAll('_', ' ')}`;
    if (item.stage === 'expanding' && item.discoveredPageCount) {
      return `Creating pages ${item.createdPageCount ?? 0}/${item.discoveredPageCount}`;
    }
    return item.stage;
  }
}
