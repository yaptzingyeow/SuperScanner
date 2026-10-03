import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { MePlan, UsageMeter } from './plan.models';

/** The signed-in account's plan, limits and today's usage (refresh after limited actions). */
@Injectable({ providedIn: 'root' })
export class PlanService {
  private readonly http = inject(HttpClient, { optional: true });
  private readonly baseUrl = (inject(API_BASE_URL, { optional: true }) ?? '').replace(/\/+$/, '');
  readonly plan = signal<MePlan | null>(null);

  readonly ocr = computed<UsageMeter | null>(() => {
    const plan = this.plan();
    const limit = plan?.limits.ocrPagesPerDay;
    return plan && limit != null ? { used: plan.usage.ocrPages, limit: limit + plan.usage.bonusOcrPages } : null;
  });

  readonly watermarks = computed<UsageMeter | null>(() => {
    const plan = this.plan();
    const limit = plan?.limits.watermarkExportsPerDay;
    return plan && limit != null ? { used: plan.usage.watermarkExports, limit } : null;
  });

  readonly label = computed(() => {
    const plan = this.plan();
    if (!plan) return '';
    if (plan.plan === 'Free') return 'Free';
    if (plan.proForever) return 'Pro (forever)';
    return plan.proUntil ? `Pro until ${new Date(plan.proUntil).toLocaleDateString()}` : 'Pro';
  });

  readonly isAdmin = computed(() => this.plan()?.isAdmin === true);

  async refresh(): Promise<void> {
    if (!this.http) return;
    try {
      const plan = await firstValueFrom(this.http.get<MePlan>(`${this.baseUrl}/me/plan`));
      if (plan && typeof plan === 'object' && 'limits' in plan) this.plan.set(plan);
    } catch {
      // Keep the last known plan; the server still enforces every limit.
    }
  }
}
