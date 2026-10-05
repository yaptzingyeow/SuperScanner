import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18nService } from '../core/i18n/i18n.service';
import { AdminApiService, DURATIONS } from './admin-api.service';
import { AdminSubscription, AdminUserDetail, GrantDuration } from './admin.models';

@Component({
  selector: 'app-admin-user-detail',
  imports: [DatePipe, RouterLink],
  styleUrl: './admin.scss',
  template: `
    <p><a routerLink="/admin/users">{{ i18n.t('admin.detail.back') }}</a></p>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (user(); as u) {
      <h1>{{ u.email ?? (u.isGuest ? i18n.t('admin.guest') : u.uid) }}</h1>
      <div class="cards">
        <div class="card"><span class="card__label">{{ i18n.t('admin.detail.plan') }}</span><span class="card__value" data-user-plan>{{ u.plan }}</span></div>
        <div class="card"><span class="card__label">{{ i18n.t('admin.detail.documents') }}</span><span class="card__value">{{ u.documentCount }}</span></div>
        <div class="card"><span class="card__label">{{ i18n.t('admin.detail.ocrToday') }}</span><span class="card__value">{{ u.usage.ocrPages }}</span></div>
        <div class="card"><span class="card__label">{{ i18n.t('admin.detail.wmToday') }}</span><span class="card__value">{{ u.usage.watermarkExports }}</span></div>
      </div>
      <p class="muted">{{ i18n.t('admin.detail.meta', { uid: u.uid, provider: u.provider, admin: u.isAdmin ? i18n.t('admin.detail.adminTag') : '', joined: (u.createdAt | date: 'mediumDate') ?? '', seen: (u.lastSeenAt | date: 'short') ?? '' }) }}</p>

      @if (!u.isGuest) {
        <h2>{{ i18n.t('admin.detail.giveTitle') }}</h2>
        <form class="row panel" (submit)="$event.preventDefault(); grant()">
          <label>{{ i18n.t('admin.detail.duration') }}
            <select data-grant-duration [value]="duration()" (change)="duration.set($any($event.target).value)">
              @for (d of durations; track d.value) { <option [value]="d.value">{{ i18n.t('admin.duration.' + d.value) }}</option> }
            </select>
          </label>
          <label>{{ i18n.t('admin.col.note') }}
            <input data-grant-note maxlength="200" [placeholder]="i18n.t('admin.detail.notePlaceholder')" [value]="note()" (input)="note.set($any($event.target).value)" />
          </label>
          <button data-grant class="primary" type="submit" [disabled]="busy()">{{ i18n.t('admin.detail.give') }}</button>
        </form>
      }

      <h2>{{ i18n.t('admin.detail.subs') }}</h2>
      <div class="table-wrap panel">
        <table>
          <thead><tr><th>{{ i18n.t('admin.col.source') }}</th><th>{{ i18n.t('admin.col.status') }}</th><th>{{ i18n.t('admin.col.ends') }}</th><th>{{ i18n.t('admin.col.note') }}</th><th></th></tr></thead>
          <tbody>
            @for (s of u.subscriptions; track s.id) {
              <tr>
                <td>{{ s.source }}</td>
                <td>{{ s.active ? i18n.t('admin.active') : s.status }}</td>
                <td>{{ s.endsAt ? (s.endsAt | date: 'mediumDate') ?? '' : i18n.t('admin.forever') }}</td>
                <td>{{ s.note }}</td>
                <td>@if (s.active) { <button type="button" class="danger" data-revoke [disabled]="busy()" (click)="revoke(s)">{{ i18n.t('admin.revoke') }}</button> }</td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="empty">{{ i18n.t('admin.detail.noSubs') }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class UserDetailComponent implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AdminApiService);
  private readonly uid = inject(ActivatedRoute).snapshot.paramMap.get('uid') ?? '';
  protected readonly durations = DURATIONS;
  protected readonly user = signal<AdminUserDetail | null>(null);
  protected readonly duration = signal<GrantDuration>('1m');
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  ngOnInit(): void { void this.load(); }

  protected async grant(): Promise<void> {
    await this.run(async () => {
      await this.api.grant(this.uid, this.duration(), this.note());
      this.note.set('');
    }, this.i18n.t('admin.detail.giveFailed'));
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
      this.user.set(await this.api.user(this.uid));
    } catch {
      this.error.set(this.i18n.t('admin.detail.loadFailed'));
    }
  }
}
