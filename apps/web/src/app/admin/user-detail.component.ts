import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AdminApiService, DURATIONS } from './admin-api.service';
import { AdminSubscription, AdminUserDetail, GrantDuration } from './admin.models';

@Component({
  selector: 'app-admin-user-detail',
  imports: [DatePipe, RouterLink],
  styleUrl: './admin.scss',
  template: `
    <p><a routerLink="/admin/users">← All users</a></p>
    @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
    @if (user(); as u) {
      <h1>{{ u.email ?? (u.isGuest ? 'Guest' : u.uid) }}</h1>
      <div class="cards">
        <div class="card"><span class="card__label">Plan</span><span class="card__value" data-user-plan>{{ u.plan }}</span></div>
        <div class="card"><span class="card__label">Documents</span><span class="card__value">{{ u.documentCount }}</span></div>
        <div class="card"><span class="card__label">OCR today</span><span class="card__value">{{ u.usage.ocrPages }}</span></div>
        <div class="card"><span class="card__label">Watermarks today</span><span class="card__value">{{ u.usage.watermarkExports }}</span></div>
      </div>
      <p class="muted">UID {{ u.uid }} · {{ u.provider }}{{ u.isAdmin ? ' · admin' : '' }} · joined {{ u.createdAt | date: 'mediumDate' }} · last seen {{ u.lastSeenAt | date: 'short' }}</p>

      @if (!u.isGuest) {
        <h2>Give Pro (free)</h2>
        <form class="row panel" (submit)="$event.preventDefault(); grant()">
          <label>Duration
            <select data-grant-duration [value]="duration()" (change)="duration.set($any($event.target).value)">
              @for (d of durations; track d.value) { <option [value]="d.value">{{ d.label }}</option> }
            </select>
          </label>
          <label>Note
            <input data-grant-note maxlength="200" placeholder="e.g. friend, tester" [value]="note()" (input)="note.set($any($event.target).value)" />
          </label>
          <button data-grant class="primary" type="submit" [disabled]="busy()">Give Pro</button>
        </form>
      }

      <h2>Subscriptions</h2>
      <div class="table-wrap panel">
        <table>
          <thead><tr><th>Source</th><th>Status</th><th>Ends</th><th>Note</th><th></th></tr></thead>
          <tbody>
            @for (s of u.subscriptions; track s.id) {
              <tr>
                <td>{{ s.source }}</td>
                <td>{{ s.active ? 'Active' : s.status }}</td>
                <td>{{ s.endsAt ? (s.endsAt | date: 'mediumDate') : 'Forever' }}</td>
                <td>{{ s.note }}</td>
                <td>@if (s.active) { <button type="button" class="danger" data-revoke [disabled]="busy()" (click)="revoke(s)">Revoke</button> }</td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="empty">No subscriptions yet.</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class UserDetailComponent implements OnInit {
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
    }, 'Could not give Pro.');
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
      this.user.set(await this.api.user(this.uid));
    } catch {
      this.error.set('Could not load this user.');
    }
  }
}
