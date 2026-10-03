import { Injectable, computed, signal } from '@angular/core';
import { DEFAULT_WATERMARK, ExportWatermarkSettings, normalizeWatermark } from './watermark';

const STORAGE_KEY = 'arksscanner:export-watermark';

/** The watermark chosen in the Export tab, shared by PDF and Print and remembered on this browser. */
@Injectable({ providedIn: 'root' })
export class WatermarkStore {
  readonly enabled = signal(false);
  readonly settings = signal<ExportWatermarkSettings>({ ...DEFAULT_WATERMARK });
  /** What the next export should carry: the settings when switched on with some text, otherwise none. */
  readonly active = computed<ExportWatermarkSettings | null>(() =>
    this.enabled() && this.settings().text.trim() ? normalizeWatermark(this.settings()) : null);

  constructor() {
    try {
      const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null') as
        { enabled?: boolean; settings?: Partial<ExportWatermarkSettings> } | null;
      if (saved) {
        this.enabled.set(!!saved.enabled);
        this.settings.set({ ...DEFAULT_WATERMARK, ...saved.settings });
      }
    } catch { /* storage unavailable or corrupt: start from the defaults */ }
  }

  setEnabled(enabled: boolean): void {
    this.enabled.set(enabled);
    this.save();
  }

  update(change: Partial<ExportWatermarkSettings>): void {
    this.settings.update((current) => ({ ...current, ...change }));
    this.save();
  }

  reset(): void {
    this.settings.set({ ...DEFAULT_WATERMARK });
    this.save();
  }

  private save(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ enabled: this.enabled(), settings: this.settings() }));
    } catch { /* storage unavailable: keep the in-memory settings */ }
  }
}
