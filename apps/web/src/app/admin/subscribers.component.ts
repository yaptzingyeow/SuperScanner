import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../core/i18n/i18n.service';
import { AdminApiService, DURATIONS } from './admin-api.service';
import { AdminPage, AdminSubscription, GrantDuration } from './admin.models';

@Component({
  selector: 'app-admin-subscribers',
  imports: [DatePipe, RouterLink],
  styleUrl: './admin.scss',
  template: `
    <h1>{{ i18n.t('admin.subs.title') }}</h1>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (result(); as r) {
      <div class="table-wrap panel">
        <table>
          <thead><tr><th>{{ i18n.t('admin.subs.account') }}</th><th>{{ i18n.t('admin.col.source') }}</th><th>{{ i18n.t('admin.col.status') }}</th><th>{{ i18n.t('admin.subs.started') }}</th><th>{{ i18n.t('admin.col.ends') }}</th><th>{{ i18n.t('admin.col.note') }}</th><th>{{ i18n.t('admin.subs.extend') }}</th><th></th></tr></thead>
          <tbody>
            @for (s of r.items; track s.id) {
              <tr>
                <td><a [routerLink]="['/admin/users', s.accountUid]">{{ s.email ?? s.accountUid }}</a></td>
                <td>{{ s.source }}</td>
                <td>{{ s.active ? i18n.t('admin.active') : s.status }}</td>
                <td>{{ s.startsAt | date: 'mediumDate' }}</td>
                <td>{{ s.endsAt ? (s.endsAt | date: 'mediumDate') : i18n.t('admin.forever') }}</td>
                <td>{{ s.note }}</td>
                <td>
                  @if (s.active && s.endsAt) {
                    <div class="row">
                      <select [attr.aria-label]="i18n.t('admin.subs.extendBy')" (change)="extendBy.set($any($event.target).value)">
                        @for (d of durations; track d.value) { <option [value]="d.value">{{ i18n.t('admin.duration.' + d.value) }}</option> }
                      </select>
                      <button type="button" data-extend [disabled]="busy()" (click)="extend(s)">{{ i18n.t('admin.subs.extend') }}</button>
                    </div>
                  }
                </td>
                <td>@if (s.active) { <button type="button" class="danger" data-revoke [disabled]="busy()" (click)="revoke(s)">{{ i18n.t('admin.revoke') }}</button> }</td>
              </tr>
            } @empty {
              <tr><td colspan="8" class="empty">{{ i18n.t('admin.subs.none') }}</td></tr>
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
  `,
})
export class SubscribersComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
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
    return this.run(() => this.api.extend(subscription.id, this.extendBy()), this.i18n.t('admin.subs.extendFailed'));
  }

  protected async revoke(subscription: AdminSubscription): Promise<void> {
    if (!window.confirm(this.i18n.t('admin.revokeConfirm'))) return;
    await this.run(() => this.api.revoke(subscription.id), this.i18n.t('admin.revokeFailed'));
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
      this.error.set(this.i18n.t('admin.subs.loadFailed'));
    }
  }
}
