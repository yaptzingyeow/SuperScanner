import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService } from '../core/i18n/i18n.service';
import { AdminApiService } from './admin-api.service';
import { AdminPage, AdminUser } from './admin.models';

@Component({
  selector: 'app-admin-users',
  imports: [DatePipe, RouterLink],
  styleUrl: './admin.scss',
  template: `
    <h1>{{ i18n.t('admin.users.title') }}</h1>
    <form class="row" (submit)="$event.preventDefault(); search()">
      <input data-user-search type="search" [placeholder]="i18n.t('admin.users.searchPlaceholder')" [attr.aria-label]="i18n.t('admin.users.searchLabel')"
        [value]="query()" (input)="query.set($any($event.target).value)" />
      <button data-search type="submit">{{ i18n.t('admin.users.search') }}</button>
    </form>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (result(); as r) {
      <p class="muted">{{ i18n.t(r.total === 1 ? 'admin.users.countOne' : 'admin.users.countOther', { count: r.total }) }}</p>
      <div class="table-wrap panel">
        <table>
          <thead><tr><th>{{ i18n.t('admin.col.email') }}</th><th>{{ i18n.t('admin.users.signIn') }}</th><th>{{ i18n.t('admin.users.plan') }}</th><th>{{ i18n.t('admin.users.joined') }}</th><th>{{ i18n.t('admin.users.lastSeen') }}</th></tr></thead>
          <tbody>
            @for (user of r.items; track user.uid) {
              <tr>
                <td><a [routerLink]="['/admin/users', user.uid]">{{ user.email ?? (user.isGuest ? i18n.t('admin.guest') : user.uid) }}</a></td>
                <td>{{ user.provider }}</td>
                <td><span class="pill" [class.pill--pro]="user.plan === 'Pro'">{{ user.plan }}</span></td>
                <td>{{ user.createdAt | date: 'mediumDate' }}</td>
                <td>{{ user.lastSeenAt | date: 'short' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="empty">{{ i18n.t('admin.users.none') }}</td></tr>
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
export class UsersComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AdminApiService);
  protected readonly query = signal('');
  protected readonly page = signal(1);
  protected readonly result = signal<AdminPage<AdminUser> | null>(null);
  protected readonly error = signal('');

  ngOnInit(): void { void this.load(); }

  protected search(): void { this.page.set(1); void this.load(); }

  protected go(page: number): void { this.page.set(page); void this.load(); }

  private async load(): Promise<void> {
    this.error.set('');
    try {
      this.result.set(await this.api.users(this.query().trim(), this.page()));
    } catch {
      this.error.set(this.i18n.t('admin.users.loadFailed'));
    }
  }
}
