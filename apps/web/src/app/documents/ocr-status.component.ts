import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, Input, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { PageOcr } from './document.models';
import { DocumentsApiService } from './documents-api.service';

@Component({
  selector: 'app-ocr-status',
  standalone: true,
  imports: [DatePipe, DecimalPipe],
  templateUrl: './ocr-status.component.html',
  styleUrl: './ocr-status.component.scss',
})
export class OcrStatusComponent implements OnInit, OnDestroy {
  private readonly api = inject(DocumentsApiService);
  @Input({ required: true }) documentId = '';
  @Input({ required: true }) pageId = '';
  protected readonly status = signal<PageOcr | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;

  ngOnInit(): void {
    void this.load();
  }

  protected async recognize(retryFailed: boolean): Promise<void> {
    if (this.busy()) return;
    clearTimeout(this.timer);
    this.busy.set(true);
    this.error.set('');
    try {
      const next = await this.api.requestPageOcr(this.documentId, this.pageId, retryFailed);
      if (this.destroyed) return;
      this.status.set(next);
      this.schedule(next);
    } catch {
      if (!this.destroyed)
        this.error.set('Text recognition is not available right now. Please try again.');
    } finally {
      if (!this.destroyed) this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    clearTimeout(this.timer);
    try {
      const next = await this.api.getPageOcr(this.documentId, this.pageId);
      if (this.destroyed) return;
      this.status.set(next);
      this.error.set('');
      this.schedule(next);
    } catch {
      if (!this.destroyed) {
        this.error.set('We could not refresh text recognition. Please try again.');
        const current = this.status();
        if (current) this.schedule(current);
      }
    }
  }

  private schedule(status: PageOcr): void {
    clearTimeout(this.timer);
    if (!this.destroyed && (status.state === 'Queued' || status.state === 'Processing'))
      this.timer = setTimeout(() => void this.load(), 3000);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
  }
}
