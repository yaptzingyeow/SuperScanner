import { Component, inject, OnInit, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { AdminApiService } from './admin-api.service';
import { AdminSettings } from './admin.models';

type NumberSetting = 'freeOcrPagesPerDay' | 'freeWatermarkExportsPerDay' | 'freeMaxDocuments' | 'freeRetentionDays' | 'ocrCostPerThousandPages';

@Component({
  selector: 'app-admin-settings',
  styleUrl: './admin.scss',
  template: `
    <h1>{{ i18n.t('admin.set.title') }}</h1>
    @if (loadError()) { <p class="error" role="alert">{{ loadError() }}</p> }
    @if (draft(); as s) {
      <form class="stack panel" (submit)="$event.preventDefault(); save()">
        <label>{{ i18n.t('admin.set.phase') }}
          <select data-setting="phase" [value]="s.phase" (change)="set('phase', $any($event.target).value)">
            <option value="Test">{{ i18n.t('admin.set.phaseTest') }}</option>
            <option value="Enforced">{{ i18n.t('admin.set.phaseEnforced') }}</option>
          </select>
        </label>
        <label>{{ i18n.t('admin.set.autoEnforce') }}
          <input data-setting="enforceFromUtc" type="datetime-local" [value]="localDate(s.enforceFromUtc)"
            (input)="set('enforceFromUtc', utcDate($any($event.target).value))" />
        </label>
        @for (field of numberFields; track field.key) {
          <label>{{ i18n.t(field.label) }}
            <input type="number" [attr.data-setting]="field.key" [attr.min]="field.min" [attr.step]="field.step"
              [value]="s[field.key]" (input)="setNumber(field.key, $any($event.target).value)" />
          </label>
        }
        <label>{{ i18n.t('admin.set.timeZone') }}
          <input data-setting="usageTimeZone" [value]="s.usageTimeZone" (input)="set('usageTimeZone', $any($event.target).value)" />
        </label>
        <div class="row">
          <button class="primary" type="submit" data-save-settings [disabled]="busy()">{{ i18n.t('admin.set.save') }}</button>
          @if (saved()) { <span class="ok" role="status">{{ i18n.t('admin.set.saved') }}</span> }
        </div>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      </form>
    }
  `,
})
export class SettingsComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AdminApiService);
  protected readonly draft = signal<AdminSettings | null>(null);
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal('');
  protected readonly loadError = signal('');
  protected readonly numberFields: { key: NumberSetting; label: string; min: number; step: number }[] = [
    { key: 'freeOcrPagesPerDay', label: 'admin.set.ocrPerDay', min: 0, step: 1 },
    { key: 'freeWatermarkExportsPerDay', label: 'admin.set.wmPerDay', min: 0, step: 1 },
    { key: 'freeMaxDocuments', label: 'admin.set.maxDocs', min: 1, step: 1 },
    { key: 'freeRetentionDays', label: 'admin.set.retention', min: 1, step: 1 },
    { key: 'ocrCostPerThousandPages', label: 'admin.set.ocrCost', min: 0, step: 0.01 },
  ];

  async ngOnInit(): Promise<void> {
    try {
      this.draft.set(await this.api.settings());
    } catch {
      this.loadError.set(this.i18n.t('admin.set.loadFailed'));
    }
  }

  protected set<K extends keyof AdminSettings>(key: K, value: AdminSettings[K]): void {
    this.saved.set(false);
    this.draft.update((current) => current && { ...current, [key]: value });
  }

  protected setNumber(key: NumberSetting, value: string): void {
    const number = Number(value);
    if (value !== '' && Number.isFinite(number)) this.set(key, number);
  }

  protected localDate(utc: string | null): string {
    if (!utc) return '';
    const date = new Date(utc);
    return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
  }

  protected utcDate(local: string): string | null {
    return local ? new Date(local).toISOString() : null;
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!draft || this.busy() || !window.confirm(this.i18n.t('admin.set.confirm'))) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.draft.set(await this.api.saveSettings(draft));
      this.saved.set(true);
    } catch {
      this.error.set(this.i18n.t('admin.set.saveFailed'));
    } finally {
      this.busy.set(false);
    }
  }
}
