import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { MePlan } from './plan.models';
import { PlanService } from './plan.service';

export function mePlan(overrides: Partial<MePlan> = {}): MePlan {
  return {
    plan: 'Free', phase: 'Enforced', proUntil: null, proForever: false, brandStamp: true,
    limits: { ocrPagesPerDay: 5, watermarkExportsPerDay: 3, maxDocuments: 30, retentionDays: 7 },
    usage: { ocrPages: 2, bonusOcrPages: 0, watermarkExports: 1, resetsAt: '2026-10-04T16:00:00Z' },
    documentCount: 4,
    ...overrides,
  };
}

describe('PlanService', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: '/api' }],
    });
    return { service: TestBed.inject(PlanService), http: TestBed.inject(HttpTestingController) };
  }

  it('exposes usage meters while limits exist', async () => {
    const { service, http } = setup();
    const done = service.refresh();
    http.expectOne('/api/me/plan').flush(mePlan());
    await done;

    expect(service.ocr()).toEqual({ used: 2, limit: 5 });
    expect(service.watermarks()).toEqual({ used: 1, limit: 3 });
    expect(service.label()).toBe('Free');
  });

  it('hides meters when unlimited and labels Pro', async () => {
    const { service, http } = setup();
    const done = service.refresh();
    http.expectOne('/api/me/plan').flush(mePlan({
      plan: 'Pro', proUntil: '2026-12-31T00:00:00Z',
      limits: { ocrPagesPerDay: null, watermarkExportsPerDay: null, maxDocuments: null, retentionDays: null },
    }));
    await done;

    expect(service.ocr()).toBeNull();
    expect(service.watermarks()).toBeNull();
    expect(service.label()).toMatch(/^Pro until /);
  });

  it('labels a forever grant and survives a failed refresh', async () => {
    const { service, http } = setup();
    const first = service.refresh();
    http.expectOne('/api/me/plan').flush(mePlan({ plan: 'Pro', proForever: true }));
    await first;
    expect(service.label()).toBe('Pro (forever)');

    const second = service.refresh();
    http.expectOne('/api/me/plan').flush('no', { status: 500, statusText: 'err' });
    await second;
    expect(service.label()).toBe('Pro (forever)');
  });
});
