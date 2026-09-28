import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { FontFaceEntry } from './font-catalogue.models';

@Injectable({ providedIn: 'root' })
export class FontCatalogueService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  private pending?: Promise<FontFaceEntry[]>;
  private readonly loaded = new Map<string, Promise<void>>();

  list(): Promise<FontFaceEntry[]> {
    return this.pending ??= firstValueFrom(this.http.get<FontFaceEntry[]>(`${this.base}/text-edit-fonts`))
      .then((faces) => faces.filter((face) => face.enabled &&
        /^\/assets\/fonts\/[A-Za-z0-9][A-Za-z0-9._-]*\.ttf$/u.test(face.webAssetUrl)))
      .catch((error) => { this.pending = undefined; throw error; });
  }

  loadFace(face: FontFaceEntry): Promise<void> {
    if (!/^\/assets\/fonts\/[A-Za-z0-9][A-Za-z0-9._-]*\.ttf$/u.test(face.webAssetUrl))
      return Promise.reject(new Error('Unsafe font URL'));
    const key = `${face.catalogueId}:${face.version}`;
    if (!this.loaded.has(key)) {
      const loading = new FontFace(face.webFamilyName, `url("${face.webAssetUrl}")`,
        { weight: String(face.weight), style: face.style === 'Italic' ? 'italic' : 'normal' })
        .load().then((loaded) => { document.fonts.add(loaded); })
        .catch((error) => { this.loaded.delete(key); throw error; });
      this.loaded.set(key, loading);
    }
    return this.loaded.get(key)!;
  }
}
