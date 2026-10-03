import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { AdminApiService } from './admin-api.service';
import { AdminPage, AdminPayment } from './admin.models';

/** Read-only payment history (filled once HitPay is connected). */
@Component({
  selector: 'app-admin-payments',
  imports: [CurrencyPipe, DatePipe],
  styleUrl: './admin.scss',
  template: `
    <h1>Payments</h1>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (result(); as r) {
      @if (r.total === 0) {
        <p class="panel empty">Payments appear here once HitPay is connected.</p>
      } @else {
        <div class="table-wrap panel">
          <table>
            <thead><tr><th>Date</th><th>Account</th><th>Provider</th><th>Reference</th><th>Amount</th><th>Status</th></tr></thead>
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
          <button type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">Previous</button>
          <span class="muted">Page {{ page() }}</span>
          <button type="button" [disabled]="page() * r.pageSize >= r.total" (click)="go(page() + 1)">Next</button>
        </div>
      }
    }
  `,
})
export class PaymentsComponent implements OnInit {
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
      this.error.set('Could not load payments.');
    }
  }
}
