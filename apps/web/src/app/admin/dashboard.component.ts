import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { AdminApiService } from './admin-api.service';
import { AdminDashboard } from './admin.models';

@Component({
  selector: 'app-admin-dashboard',
  imports: [DatePipe, DecimalPipe],
  styleUrl: './admin.scss',
  template: `
    <h1>{{ i18n.t('admin.dash.title') }}</h1>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (data(); as d) {
      @if (d.effectivePhase === 'Test') {
        <p class="banner" data-phase-banner>
          {{ i18n.t('admin.dash.testBanner') }}
          @if (d.enforceFromUtc) { {{ i18n.t('admin.dash.enforcedFrom', { date: (d.enforceFromUtc | date: 'mediumDate') ?? '' }) }} }
        </p>
      } @else {
        <p class="banner banner--enforced" data-phase-banner>{{ i18n.t('admin.dash.enforced') }}</p>
      }
      <div class="cards">
        <div class="card" data-metric="users">
          <span class="card__label">{{ i18n.t('admin.dash.users') }}</span><span class="card__value">{{ d.users.total | number }}</span>
          <span class="card__hint">{{ i18n.t('admin.dash.usersHint', { signedIn: d.users.signedIn, guests: d.users.guests }) }}</span>
        </div>
        <div class="card" data-metric="new-users">
          <span class="card__label">{{ i18n.t('admin.dash.newUsers') }}</span><span class="card__value">{{ d.users.new.today }}</span>
          <span class="card__hint">{{ i18n.t('admin.dash.newHint', { d7: d.users.new.d7, d30: d.users.new.d30 }) }}</span>
        </div>
        <div class="card" data-metric="active">
          <span class="card__label">{{ i18n.t('admin.dash.active7') }}</span><span class="card__value">{{ d.users.active7d | number }}</span>
        </div>
        <div class="card" data-metric="subscribers">
          <span class="card__label">{{ i18n.t('admin.dash.pro') }}</span><span class="card__value">{{ d.subscribers.total }}</span>
          <span class="card__hint">{{ i18n.t('admin.dash.proHint', { manual: d.subscribers.manual, paid: d.subscribers.paid }) }}</span>
        </div>
        <div class="card" data-metric="documents">
          <span class="card__label">{{ i18n.t('admin.dash.documents') }}</span><span class="card__value">{{ d.documents.total | number }}</span>
          <span class="card__hint">{{ i18n.t('admin.dash.documentsHint', { today: d.documents.today }) }}</span>
        </div>
        <div class="card" data-metric="ocr">
          <span class="card__label">{{ i18n.t('admin.dash.ocrPages') }}</span><span class="card__value">{{ d.ocr.today | number }}</span>
          <span class="card__hint">{{ i18n.t('admin.dash.todayMonth', { month: (d.ocr.month | number) ?? '' }) }}</span>
        </div>
        <div class="card" data-metric="ocr-cost">
          <span class="card__label">{{ i18n.t('admin.dash.ocrCost') }}</span><span class="card__value">US$ {{ d.ocr.estimatedCostMonth | number: '1.2-2' }}</span>
          <span class="card__hint">{{ i18n.t('admin.dash.estimate') }}</span>
        </div>
        <div class="card" data-metric="watermarks">
          <span class="card__label">{{ i18n.t('admin.dash.watermarks') }}</span><span class="card__value">{{ d.watermarkExports.today }}</span>
          <span class="card__hint">{{ i18n.t('admin.dash.todayMonth', { month: d.watermarkExports.month }) }}</span>
        </div>
      </div>

      <h2>{{ i18n.t('admin.dash.chartTitle') }}</h2>
      <div class="panel">
        <svg class="chart" viewBox="0 0 300 100" preserveAspectRatio="none" role="img" [attr.aria-label]="i18n.t('admin.dash.chartLabel')">
          @for (bar of bars(); track bar.day) {
            <rect data-bar [attr.x]="bar.x" [attr.y]="100 - bar.height" width="8" [attr.height]="bar.height">
              <title>{{ i18n.t('admin.dash.barTitle', { day: bar.day, value: bar.value, newUsers: bar.newUsers }) }}</title>
            </rect>
          }
        </svg>
      </div>
    } @else if (!error()) {
      <p class="muted" aria-live="polite">{{ i18n.t('admin.loading') }}</p>
    }
  `,
})
export class DashboardComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AdminApiService);
  protected readonly data = signal<AdminDashboard | null>(null);
  protected readonly error = signal('');
  protected readonly bars = computed(() => {
    const series = this.data()?.series ?? [];
    const max = Math.max(1, ...series.map((s) => s.ocrPages));
    return series.map((s, i) => ({
      day: s.day, value: s.ocrPages, newUsers: s.newUsers, x: i * 10 + 1, height: Math.max(1, (s.ocrPages / max) * 96),
    }));
  });

  async ngOnInit(): Promise<void> {
    try {
      this.data.set(await this.api.dashboard());
    } catch {
      this.error.set(this.i18n.t('admin.dash.loadFailed'));
    }
  }
}
