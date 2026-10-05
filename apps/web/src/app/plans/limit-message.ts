import { HttpErrorResponse } from '@angular/common/http';
import { PlanLimitProblem } from './plan.models';

/** The plan-limit problem behind a failed request, or null for any other failure. */
export function planLimitOf(error: unknown): PlanLimitProblem | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 429) return null;
  const body = error.error as Partial<PlanLimitProblem> | null;
  return body?.code === 'plan_limit_reached' && typeof body.limit === 'number' ? (body as PlanLimitProblem) : null;
}

/** A translate function such as I18nService.t; without one, messages are in English. */
export type Translate = (key: string, params?: Record<string, string | number>) => string;

export function limitMessage(problem: PlanLimitProblem, t?: Translate): string {
  switch (problem.kind) {
    case 'ocr':
      if (t) return t('ws.limit.ocr', { limit: problem.limit });
      return `You've used today's ${problem.limit} free OCR pages — upgrade to Pro or come back tomorrow.`;
    case 'watermark':
      if (t) return t('ws.limit.watermark', { limit: problem.limit });
      return `You've used today's ${problem.limit} free watermark exports — upgrade to Pro or come back tomorrow.`;
    default:
      if (t) return t('ws.limit.documents', { limit: problem.limit });
      return `The Free plan keeps up to ${problem.limit} documents. Delete some or upgrade to Pro.`;
  }
}

/** True when today's watermark exports are used up (only on plans with a limit). */
export function watermarksUsedUp(meter: { used: number; limit: number } | null): boolean {
  return !!meter && meter.used >= meter.limit;
}

/** The limit message for a plan-limit failure, otherwise the fallback. */
export function errorMessage(error: unknown, fallback: string, t?: Translate): string {
  const problem = planLimitOf(error);
  return problem ? limitMessage(problem, t) : fallback;
}
