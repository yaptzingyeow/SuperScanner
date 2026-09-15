import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';

interface PreviewPage { id: string; pageNumber: number; hasPreview: boolean; hasOriginal: boolean; canCrop: boolean; cropStatus: string | null; previewRevision: number; appliedFilter: string; }
interface DocumentDetail { id: string; title: string; status: string; message: string | null; pages: PreviewPage[]; }

@Component({
  selector: 'app-document-detail', standalone: true, imports: [RouterLink],
  template: `
    <section>
      <a routerLink="/documents">← My documents</a>
      @if (document(); as doc) {
        <header><div><h1>{{ doc.title }}</h1><p>{{ doc.status === 'Ready' ? 'Your document preview is ready.' : doc.status === 'NeedsCrop' ? 'Check your document corners to finish your scan.' : doc.status === 'Failed' ? 'Processing could not be completed. Open the corner editor to retry a crop.' : 'Creating your preview…' }}</p></div><span>{{ doc.status === 'NeedsCrop' ? 'Adjust corners' : doc.status }}</span></header>
        @if (doc.message) { <p role="status">{{ doc.message }}</p> }
        @if (doc.status === 'Failed') { <button (click)="retry()" [disabled]="retrying()">{{ retrying() ? 'Retrying…' : 'Retry preview' }}</button> }
        @for (page of doc.pages; track page.id) {
          <article>
            <div class="page-heading"><h2>Page {{ page.pageNumber }}</h2>@if (page.hasOriginal) { <button (click)="download(page.id)">Download original</button> }</div>
            @if (page.canCrop) { <p><a [routerLink]="['/documents', doc.id, 'pages', page.id, 'crop']">Crop & filters</a> · {{ page.cropStatus === 'NeedsCrop' ? 'Corners ready for review' : page.cropStatus }}</p>
              @if (page.previewRevision > 0) { <p class="note">Saved filter: {{ page.appliedFilter === 'BlackAndWhite' ? 'Black & White' : page.appliedFilter }}</p> }
            }
            @if (images()[page.id]; as url) { <img [src]="url" [alt]="'Preview of page ' + page.pageNumber" /> }
            @else { <p>Preview pending</p> }
          </article>
        }
      } @else if (!error()) { <p role="status">Loading document…</p> }
      @if (error()) { <p role="alert">{{ error() }}</p><button (click)="load()">Reload</button> }
    </section>`,
  styles: [`section{max-width:1000px;margin:auto;padding:32px 24px}a{color:inherit}header,.page-heading{display:flex;align-items:center;justify-content:space-between;gap:16px}header{margin:24px 0}h1{font-size:30px;margin-bottom:8px}header span{padding:8px 14px;border-radius:20px;background:#e4f3e9;color:#23513e}article{background:white;border:1px solid #e1e5e4;border-radius:16px;padding:20px;margin:20px 0}h2{font-size:16px}img{display:block;max-width:100%;max-height:1000px;margin:20px auto;object-fit:contain}button{padding:10px 16px;border:1px solid #ccd8d0;border-radius:9px;background:#f4f8f5;color:#234437;cursor:pointer}.note{color:#66736d;font-size:13px}`]
})
export class DocumentDetailComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  private readonly id = this.route.snapshot.paramMap.get('documentId')!;
  protected readonly document = signal<DocumentDetail | null>(null);
  protected readonly images = signal<Record<string, string>>({});
  protected readonly error = signal('');
  protected readonly retrying = signal(false);
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  private readonly loadedRevisions: Record<string, number> = {};
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    clearTimeout(this.timer);
    this.error.set('');
    try {
      const doc = await firstValueFrom(this.http.get<DocumentDetail>(`${this.base}/documents/${this.id}`));
      if (this.destroyed) return;
      this.document.set(doc);
      for (const page of doc.pages.filter(p => p.hasPreview && (!this.images()[p.id] || this.loadedRevisions[p.id] !== p.previewRevision))) {
        const blob = await firstValueFrom(this.http.get(`${this.base}/documents/${this.id}/pages/${page.id}/preview`, { responseType: 'blob' }));
        if (this.destroyed) return;
        if (this.images()[page.id]) URL.revokeObjectURL(this.images()[page.id]);
        this.loadedRevisions[page.id] = page.previewRevision;
        this.images.update(current => ({ ...current, [page.id]: URL.createObjectURL(blob) }));
      }
      if (!this.destroyed && doc.status !== 'Ready' && doc.status !== 'Failed' && doc.status !== 'NeedsCrop')
        this.timer = setTimeout(() => void this.load(), 3000);
    } catch { if (!this.destroyed) this.error.set('We could not load this document. Please try again.'); }
  }
  async retry(): Promise<void> {
    this.retrying.set(true);
    try { await firstValueFrom(this.http.post(`${this.base}/documents/${this.id}/retry-preview`, {})); await this.load(); }
    catch { this.error.set('Could not restart processing. Please try again.'); }
    finally { this.retrying.set(false); }
  }
  async download(pageId: string): Promise<void> {
    try {
      const blob = await firstValueFrom(this.http.get(`${this.base}/documents/${this.id}/pages/${pageId}/original`, { responseType: 'blob' }));
      const url = URL.createObjectURL(blob);
      const extension = ({ 'application/pdf': '.pdf', 'image/png': '.png', 'image/jpeg': '.jpg', 'image/heic': '.heic' } as Record<string, string>)[blob.type] ?? '.bin';
      const link = window.document.createElement('a'); link.href = url; link.download = 'original' + extension; link.click();
      setTimeout(() => URL.revokeObjectURL(url), 60000);
    } catch { this.error.set('Could not download the original. Please try again.'); }
  }
  ngOnDestroy(): void { this.destroyed = true; clearTimeout(this.timer); Object.values(this.images()).forEach(url => URL.revokeObjectURL(url)); }
}
