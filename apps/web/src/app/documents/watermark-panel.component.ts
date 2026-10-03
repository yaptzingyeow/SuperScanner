import { Component, computed, inject } from '@angular/core';
import {
  ExportWatermarkSettings, WATERMARK_COLORS, WATERMARK_FONTS, WATERMARK_PRESETS, watermarkFont,
} from './watermark';
import { WatermarkStore } from './watermark.store';

/** Export-tab controls for the user's own watermark, with a live page preview. */
@Component({
  selector: 'app-watermark-panel',
  standalone: true,
  templateUrl: './watermark-panel.component.html',
  styleUrl: './watermark-panel.component.scss',
})
export class WatermarkPanelComponent {
  protected readonly store = inject(WatermarkStore);
  protected readonly fonts = WATERMARK_FONTS;
  protected readonly presets = WATERMARK_PRESETS;
  protected readonly colors = WATERMARK_COLORS;
  protected readonly angles = [0, 35, 45, -45, 90];
  protected readonly s = this.store.settings;
  protected readonly font = computed(() => watermarkFont(this.s().fontId));
  /** Rows of repeated copies for the preview (the PDF fills the page the same way). */
  protected readonly tileRows = Array.from({ length: 18 }, (_, i) => i);
  protected readonly tileColumns = Array.from({ length: 8 }, (_, i) => i);

  protected set<K extends keyof ExportWatermarkSettings>(key: K, value: ExportWatermarkSettings[K]): void {
    this.store.update({ [key]: value } as Partial<ExportWatermarkSettings>);
  }

  protected number(key: 'opacity' | 'sizePercent' | 'angleDegrees' | 'spacing', event: Event, scale = 1): void {
    const value = Number((event.target as HTMLInputElement).value) / scale;
    if (Number.isFinite(value)) this.set(key, value);
  }

  protected text(event: Event): void {
    this.set('text', (event.target as HTMLInputElement).value.slice(0, 120));
  }

  protected chooseFont(event: Event): void {
    this.set('fontId', (event.target as HTMLSelectElement).value);
  }

  protected chooseColor(event: Event): void {
    this.set('color', (event.target as HTMLInputElement).value.toUpperCase());
  }

  protected toggle(event: Event): void {
    this.store.setEnabled((event.target as HTMLInputElement).checked);
  }
}
