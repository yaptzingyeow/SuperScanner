import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { FontFaceEntry } from './font-catalogue.models';

/** Bundled fonts ship with the app; large world-script fonts are served (signed in) by the API. */
const BUNDLED = /^\/assets\/fonts\/[A-Za-z0-9][A-Za-z0-9._-]*\.ttf$/u;
const API_SERVED = /^\/api\/fonts\/[A-Za-z0-9][A-Za-z0-9_-]*\.ttf$/u;

@Injectable({ providedIn: 'root' })
export class FontCatalogueService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  private pending?: Promise<FontFaceEntry[]>;
  private readonly loaded = new Map<string, Promise<void>>();

  list(): Promise<FontFaceEntry[]> {
    return this.pending ??= firstValueFrom(this.http.get<FontFaceEntry[]>(`${this.base}/text-edit-fonts`))
      .then((faces) => faces.filter((face) => face.enabled &&
        (BUNDLED.test(face.webAssetUrl) || API_SERVED.test(face.webAssetUrl))))
      .catch((error) => { this.pending = undefined; throw error; });
  }

  loadFace(face: FontFaceEntry): Promise<void> {
    const bundled = BUNDLED.test(face.webAssetUrl);
    if (!bundled && !API_SERVED.test(face.webAssetUrl)) return Promise.reject(new Error('Unsafe font URL'));
    const key = `${face.catalogueId}:${face.version}`;
    if (!this.loaded.has(key)) {
      const descriptors = { weight: String(face.weight), style: face.style === 'Italic' ? 'italic' : 'normal' };
      const source: Promise<string | ArrayBuffer> = bundled
        ? Promise.resolve(`url("${face.webAssetUrl}")`)
        : firstValueFrom(this.http.get(face.webAssetUrl, { responseType: 'arraybuffer' }));
      const loading = source
        .then((data) => new FontFace(face.webFamilyName, data, descriptors).load())
        .then((loadedFace) => { document.fonts.add(loadedFace); })
        .catch((error) => { this.loaded.delete(key); throw error; });
      this.loaded.set(key, loading);
    }
    return this.loaded.get(key)!;
  }
}
