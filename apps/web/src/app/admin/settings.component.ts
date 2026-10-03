import { Component, inject, OnInit, signal } from '@angular/core';
import { AdminApiService } from './admin-api.service';
import { AdminSettings } from './admin.models';

type NumberSetting = 'freeOcrPagesPerDay' | 'freeWatermarkExportsPerDay' | 'freeMaxDocuments' | 'freeRetentionDays' | 'ocrCostPerThousandPages';

@Component({
  selector: 'app-admin-settings',
  styleUrl: './admin.scss',
  template: `
    <h1>Plan settings</h1>
    @if (loadError()) { <p class="error" role="alert">{{ loadError() }}</p> }
    @if (draft(); as s) {
      <form class="stack panel" (submit)="$event.preventDefault(); save()">
        <label>Phase
          <select data-setting="phase" [value]="s.phase" (change)="set('phase', $any($event.target).value)">
            <option value="Test">Test — everyone gets Pro features (stamp stays, nothing deleted)</option>
            <option value="Enforced">Enforced — Free limits apply</option>
          </select>
        </label>
        <label>Switch to Enforced automatically on (optional)
          <input data-setting="enforceFromUtc" type="datetime-local" [value]="localDate(s.enforceFromUtc)"
            (input)="set('enforceFromUtc', utcDate($any($event.target).value))" />
        </label>
        @for (field of numberFields; track field.key) {
          <label>{{ field.label }}
            <input type="number" [attr.data-setting]="field.key" [attr.min]="field.min" [attr.step]="field.step"
              [value]="s[field.key]" (input)="setNumber(field.key, $any($event.target).value)" />
          </label>
        }
        <label>Usage day time zone
          <input data-setting="usageTimeZone" [value]="s.usageTimeZone" (input)="set('usageTimeZone', $any($event.target).value)" />
        </label>
        <div class="row">
          <button class="primary" type="submit" data-save-settings [disabled]="busy()">Save</button>
          @if (saved()) { <span class="ok" role="status">Saved</span> }
        </div>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      </form>
    }
  `,
})
export class SettingsComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  protected readonly draft = signal<AdminSettings | null>(null);
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal('');
  protected readonly loadError = signal('');
  protected readonly numberFields: { key: NumberSetting; label: string; min: number; step: number }[] = [
    { key: 'freeOcrPagesPerDay', label: 'Free OCR pages per day', min: 0, step: 1 },
    { key: 'freeWatermarkExportsPerDay', label: 'Free watermark exports per day', min: 0, step: 1 },
    { key: 'freeMaxDocuments', label: 'Free documents kept (maximum)', min: 1, step: 1 },
    { key: 'freeRetentionDays', label: 'Free documents deleted after (days)', min: 1, step: 1 },
    { key: 'ocrCostPerThousandPages', label: 'OCR cost per 1,000 pages (US$, for the dashboard estimate)', min: 0, step: 0.01 },
  ];

  async ngOnInit(): Promise<void> {
    try {
      this.draft.set(await this.api.settings());
    } catch {
      this.loadError.set('Could not load the plan settings.');
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
    if (!draft || this.busy() || !window.confirm('Apply to all users now?')) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.draft.set(await this.api.saveSettings(draft));
      this.saved.set(true);
    } catch {
      this.error.set('These settings were not saved. Check the values and try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
