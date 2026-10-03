import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { AdminApiService } from './admin-api.service';
import { AdminDashboard } from './admin.models';

@Component({
  selector: 'app-admin-dashboard',
  imports: [DatePipe, DecimalPipe],
  styleUrl: './admin.scss',
  template: `
    <h1>Dashboard</h1>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (data(); as d) {
      @if (d.effectivePhase === 'Test') {
        <p class="banner" data-phase-banner>
          Test phase — everyone gets Pro features (the Arks Scanner stamp stays).
          @if (d.enforceFromUtc) { Plans are enforced from {{ d.enforceFromUtc | date: 'mediumDate' }}. }
        </p>
      } @else {
        <p class="banner banner--enforced" data-phase-banner>Plans are enforced — Free limits apply.</p>
      }
      <div class="cards">
        <div class="card" data-metric="users">
          <span class="card__label">Users</span><span class="card__value">{{ d.users.total | number }}</span>
          <span class="card__hint">{{ d.users.signedIn }} signed in · {{ d.users.guests }} guests</span>
        </div>
        <div class="card" data-metric="new-users">
          <span class="card__label">New users</span><span class="card__value">{{ d.users.new.today }}</span>
          <span class="card__hint">today · {{ d.users.new.d7 }} in 7 days · {{ d.users.new.d30 }} in 30</span>
        </div>
        <div class="card" data-metric="active">
          <span class="card__label">Active (7 days)</span><span class="card__value">{{ d.users.active7d | number }}</span>
        </div>
        <div class="card" data-metric="subscribers">
          <span class="card__label">Pro subscribers</span><span class="card__value">{{ d.subscribers.total }}</span>
          <span class="card__hint">{{ d.subscribers.manual }} given · {{ d.subscribers.paid }} paid</span>
        </div>
        <div class="card" data-metric="documents">
          <span class="card__label">Documents</span><span class="card__value">{{ d.documents.total | number }}</span>
          <span class="card__hint">{{ d.documents.today }} today</span>
        </div>
        <div class="card" data-metric="ocr">
          <span class="card__label">OCR pages</span><span class="card__value">{{ d.ocr.today | number }}</span>
          <span class="card__hint">today · {{ d.ocr.month | number }} this month</span>
        </div>
        <div class="card" data-metric="ocr-cost">
          <span class="card__label">OCR cost (month)</span><span class="card__value">US$ {{ d.ocr.estimatedCostMonth | number: '1.2-2' }}</span>
          <span class="card__hint">estimate</span>
        </div>
        <div class="card" data-metric="watermarks">
          <span class="card__label">Watermark exports</span><span class="card__value">{{ d.watermarkExports.today }}</span>
          <span class="card__hint">today · {{ d.watermarkExports.month }} this month</span>
        </div>
      </div>

      <h2>OCR pages — last 30 days</h2>
      <div class="panel">
        <svg class="chart" viewBox="0 0 300 100" preserveAspectRatio="none" role="img" aria-label="OCR pages per day for the last 30 days">
          @for (bar of bars(); track bar.day) {
            <rect data-bar [attr.x]="bar.x" [attr.y]="100 - bar.height" width="8" [attr.height]="bar.height">
              <title>{{ bar.day }}: {{ bar.value }} pages, {{ bar.newUsers }} new users</title>
            </rect>
          }
        </svg>
      </div>
    } @else if (!error()) {
      <p class="muted" aria-live="polite">Loading…</p>
    }
  `,
})
export class DashboardComponent implements OnInit {
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
      this.error.set('Could not load the dashboard. Reload to try again.');
    }
  }
}
