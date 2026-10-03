import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminApiService, DURATIONS } from './admin-api.service';
import { AdminPage, AdminSubscription, GrantDuration } from './admin.models';

@Component({
  selector: 'app-admin-subscribers',
  imports: [DatePipe, RouterLink],
  styleUrl: './admin.scss',
  template: `
    <h1>Subscribers</h1>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (result(); as r) {
      <div class="table-wrap panel">
        <table>
          <thead><tr><th>Account</th><th>Source</th><th>Status</th><th>Started</th><th>Ends</th><th>Note</th><th>Extend</th><th></th></tr></thead>
          <tbody>
            @for (s of r.items; track s.id) {
              <tr>
                <td><a [routerLink]="['/admin/users', s.accountUid]">{{ s.email ?? s.accountUid }}</a></td>
                <td>{{ s.source }}</td>
                <td>{{ s.active ? 'Active' : s.status }}</td>
                <td>{{ s.startsAt | date: 'mediumDate' }}</td>
                <td>{{ s.endsAt ? (s.endsAt | date: 'mediumDate') : 'Forever' }}</td>
                <td>{{ s.note }}</td>
                <td>
                  @if (s.active && s.endsAt) {
                    <div class="row">
                      <select [attr.aria-label]="'Extend by'" (change)="extendBy.set($any($event.target).value)">
                        @for (d of durations; track d.value) { <option [value]="d.value">{{ d.label }}</option> }
                      </select>
                      <button type="button" data-extend [disabled]="busy()" (click)="extend(s)">Extend</button>
                    </div>
                  }
                </td>
                <td>@if (s.active) { <button type="button" class="danger" data-revoke [disabled]="busy()" (click)="revoke(s)">Revoke</button> }</td>
              </tr>
            } @empty {
              <tr><td colspan="8" class="empty">No Pro subscriptions yet. Give Pro from a user's page.</td></tr>
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
  `,
})
export class SubscribersComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  protected readonly durations = DURATIONS;
  protected readonly result = signal<AdminPage<AdminSubscription> | null>(null);
  protected readonly page = signal(1);
  protected readonly extendBy = signal<GrantDuration>('1m');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  ngOnInit(): void { void this.load(); }

  protected go(page: number): void { this.page.set(page); void this.load(); }

  protected extend(subscription: AdminSubscription): Promise<void> {
    return this.run(() => this.api.extend(subscription.id, this.extendBy()), 'Could not extend the subscription.');
  }

  protected async revoke(subscription: AdminSubscription): Promise<void> {
    if (!window.confirm('Revoke this Pro subscription now?')) return;
    await this.run(() => this.api.revoke(subscription.id), 'Could not revoke the subscription.');
  }

  private async run(action: () => Promise<unknown>, failure: string): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
      await this.load();
    } catch {
      this.error.set(failure);
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.result.set(await this.api.subscriptions(this.page()));
    } catch {
      this.error.set('Could not load subscriptions.');
    }
  }
}
