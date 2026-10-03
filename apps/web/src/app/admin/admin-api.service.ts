import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import {
  AdminDashboard, AdminMember, AdminPage, AdminPayment, AdminSettings, AdminSubscription, AdminUser,
  AdminUserDetail, GrantDuration,
} from './admin.models';

/** The admin portal's calls; every route 404s for anyone who is not an admin. */
@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(API_BASE_URL).replace(/\/+$/, '')}/admin`;

  dashboard(): Promise<AdminDashboard> { return this.get('dashboard'); }

  users(query: string, page: number): Promise<AdminPage<AdminUser>> {
    const params = new HttpParams().set('query', query).set('page', page);
    return firstValueFrom(this.http.get<AdminPage<AdminUser>>(`${this.base}/users`, { params }));
  }

  user(uid: string): Promise<AdminUserDetail> { return this.get(`users/${encodeURIComponent(uid)}`); }

  grant(uid: string, duration: GrantDuration, note: string): Promise<AdminSubscription> {
    return firstValueFrom(this.http.post<AdminSubscription>(`${this.base}/users/${encodeURIComponent(uid)}/subscriptions`,
      { duration, note: note.trim() || null }));
  }

  subscriptions(page: number): Promise<AdminPage<AdminSubscription>> { return this.get(`subscriptions?page=${page}`); }

  revoke(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.base}/subscriptions/${id}/revoke`, null));
  }

  extend(id: string, duration: GrantDuration): Promise<AdminSubscription> {
    return firstValueFrom(this.http.post<AdminSubscription>(`${this.base}/subscriptions/${id}/extend`, { duration }));
  }

  payments(page: number): Promise<AdminPage<AdminPayment>> { return this.get(`payments?page=${page}`); }

  settings(): Promise<AdminSettings> { return this.get('settings'); }

  saveSettings(settings: AdminSettings): Promise<AdminSettings> {
    return firstValueFrom(this.http.put<AdminSettings>(`${this.base}/settings`, settings));
  }

  admins(): Promise<AdminPage<AdminMember>> { return this.get('admins'); }

  addAdmin(email: string): Promise<AdminMember> {
    return firstValueFrom(this.http.post<AdminMember>(`${this.base}/admins`, { email: email.trim() }));
  }

  removeAdmin(uid: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/admins/${encodeURIComponent(uid)}`));
  }

  private get<T>(path: string): Promise<T> { return firstValueFrom(this.http.get<T>(`${this.base}/${path}`)); }
}

export const DURATIONS: { value: GrantDuration; label: string }[] = [
  { value: '1m', label: '1 month' },
  { value: '3m', label: '3 months' },
  { value: '1y', label: '1 year' },
  { value: 'forever', label: 'Forever' },
];
