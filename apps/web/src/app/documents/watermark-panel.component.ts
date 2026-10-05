import { Component, computed, inject } from '@angular/core';
import {
  ExportWatermarkSettings, WATERMARK_COLORS, WATERMARK_FONTS, WATERMARK_PRESETS, WATERMARK_PRESET_IDS, watermarkFont,
} from './watermark';
import { WatermarkStore } from './watermark.store';
import { PlanService } from '../plans/plan.service';
import { limitMessage, watermarksUsedUp } from '../plans/limit-message';
import { I18nService } from '../core/i18n/i18n.service';

/** Export-tab controls for the user's own watermark, with a live page preview. */
@Component({
  selector: 'app-watermark-panel',
  standalone: true,
  templateUrl: './watermark-panel.component.html',
  styleUrl: './watermark-panel.component.scss',
})
export class WatermarkPanelComponent {
  protected readonly store = inject(WatermarkStore);
  private readonly plans = inject(PlanService);
  protected readonly i18n = inject(I18nService);
  /** At the daily limit the switch is off and explains why; plain export still works. */
  protected readonly limitNote = computed(() => {
    const meter = this.plans.watermarks();
    return meter && watermarksUsedUp(meter)
      ? limitMessage({ code: 'plan_limit_reached', kind: 'watermark', limit: meter.limit, used: meter.used, resetsAt: null }, (key, params) => this.i18n.t(key, params))
      : '';
  });
  protected readonly fonts = WATERMARK_FONTS;
  /** Quick texts in the app language first, then the English originals (often required on official copies). */
  protected readonly presets = [...new Set([
    ...WATERMARK_PRESET_IDS.map((id) => this.i18n.t(`ws.wm.preset.${id}`)),
    ...WATERMARK_PRESETS,
  ])];

  /** Descriptive font names ("Handwriting") are translated; brand names ("Lato") are not. */
  protected fontLabel(font: { id: string; label: string }): string {
    const key = `ws.wm.font.${font.id}`;
    const text = this.i18n.t(key);
    return text === key ? font.label : text;
  }

  protected colorLabel(color: { label: string }): string {
    return this.i18n.t(`ws.wm.color.${color.label.toLowerCase()}`);
  }
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
