import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { AdminApiService } from './admin-api.service';
import { AdminPage, AdminPayment } from './admin.models';

/** Read-only payment history (filled once HitPay is connected). */
@Component({
  selector: 'app-admin-payments',
  imports: [CurrencyPipe, DatePipe],
  styleUrl: './admin.scss',
  template: `
    <h1>{{ i18n.t('admin.pay.title') }}</h1>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (result(); as r) {
      @if (r.total === 0) {
        <p class="panel empty">{{ i18n.t('admin.pay.empty') }}</p>
      } @else {
        <div class="table-wrap panel">
          <table>
            <thead><tr><th>{{ i18n.t('admin.pay.date') }}</th><th>{{ i18n.t('admin.pay.account') }}</th><th>{{ i18n.t('admin.pay.provider') }}</th><th>{{ i18n.t('admin.pay.reference') }}</th><th>{{ i18n.t('admin.pay.amount') }}</th><th>{{ i18n.t('admin.col.status') }}</th></tr></thead>
            <tbody>
              @for (p of r.items; track p.id) {
                <tr>
                  <td>{{ p.createdAt | date: 'short' }}</td>
                  <td>{{ p.accountUid }}</td>
                  <td>{{ p.provider }}</td>
                  <td>{{ p.providerReference }}</td>
                  <td>{{ p.amount | currency: p.currency }}</td>
                  <td>{{ p.status }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="row pager">
          <button type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">{{ i18n.t('admin.prev') }}</button>
          <span class="muted">{{ i18n.t('admin.page', { page: page() }) }}</span>
          <button type="button" [disabled]="page() * r.pageSize >= r.total" (click)="go(page() + 1)">{{ i18n.t('admin.next') }}</button>
        </div>
      }
    }
  `,
})
export class PaymentsComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AdminApiService);
  protected readonly result = signal<AdminPage<AdminPayment> | null>(null);
  protected readonly page = signal(1);
  protected readonly error = signal('');

  ngOnInit(): void { void this.load(); }

  protected go(page: number): void { this.page.set(page); void this.load(); }

  private async load(): Promise<void> {
    try {
      this.result.set(await this.api.payments(this.page()));
    } catch {
      this.error.set(this.i18n.t('admin.pay.loadFailed'));
    }
  }
}
