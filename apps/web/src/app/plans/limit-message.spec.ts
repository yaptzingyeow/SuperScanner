import { HttpErrorResponse } from '@angular/common/http';
import { limitMessage, planLimitOf } from './limit-message';

describe('limitMessage', () => {
  it('uses the agreed copy for each limit', () => {
    expect(limitMessage({ code: 'plan_limit_reached', kind: 'ocr', limit: 5, used: 5, resetsAt: null }))
      .toBe("You've used today's 5 free OCR pages — upgrade to Pro or come back tomorrow.");
    expect(limitMessage({ code: 'plan_limit_reached', kind: 'watermark', limit: 3, used: 3, resetsAt: null }))
      .toBe("You've used today's 3 free watermark exports — upgrade to Pro or come back tomorrow.");
    expect(limitMessage({ code: 'plan_limit_reached', kind: 'documents', limit: 30, used: 30, resetsAt: null }))
      .toBe('The Free plan keeps up to 30 documents. Delete some or upgrade to Pro.');
  });

  it('recognises only plan-limit 429 responses', () => {
    const body = { code: 'plan_limit_reached', kind: 'ocr', limit: 5, used: 5, resetsAt: null };
    expect(planLimitOf(new HttpErrorResponse({ status: 429, error: body }))).toEqual(body);
    expect(planLimitOf(new HttpErrorResponse({ status: 429, error: {} }))).toBeNull();
    expect(planLimitOf(new HttpErrorResponse({ status: 500, error: body }))).toBeNull();
    expect(planLimitOf(new Error('x'))).toBeNull();
  });
});
