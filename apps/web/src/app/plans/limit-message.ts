import { HttpErrorResponse } from '@angular/common/http';
import { PlanLimitProblem } from './plan.models';

/** The plan-limit problem behind a failed request, or null for any other failure. */
export function planLimitOf(error: unknown): PlanLimitProblem | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 429) return null;
  const body = error.error as Partial<PlanLimitProblem> | null;
  return body?.code === 'plan_limit_reached' && typeof body.limit === 'number' ? (body as PlanLimitProblem) : null;
}

export function limitMessage(problem: PlanLimitProblem): string {
  switch (problem.kind) {
    case 'ocr':
      return `You've used today's ${problem.limit} free OCR pages — upgrade to Pro or come back tomorrow.`;
    case 'watermark':
      return `You've used today's ${problem.limit} free watermark exports — upgrade to Pro or come back tomorrow.`;
    default:
      return `The Free plan keeps up to ${problem.limit} documents. Delete some or upgrade to Pro.`;
  }
}

/** True when today's watermark exports are used up (only on plans with a limit). */
export function watermarksUsedUp(meter: { used: number; limit: number } | null): boolean {
  return !!meter && meter.used >= meter.limit;
}

/** The limit message for a plan-limit failure, otherwise the fallback. */
export function errorMessage(error: unknown, fallback: string): string {
  const problem = planLimitOf(error);
  return problem ? limitMessage(problem) : fallback;
}
