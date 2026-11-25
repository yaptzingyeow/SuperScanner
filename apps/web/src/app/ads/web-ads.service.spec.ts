import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, RouterStateSnapshot } from '@angular/router';
import { PlanService } from '../plans/plan.service';
import { MePlan } from '../plans/plan.models';
import { adFreeNavigationGuard, ADSENSE_SCRIPT, FULL_PAGE_LOAD, isAdSafeUrl, WebAdsService } from './web-ads.service';

describe('WebAdsService', () => {
  const plan = signal<MePlan | null>(null);
  const free = (ads: MePlan['ads']): MePlan => ({
    plan: 'Free', phase: 'Enforced', proUntil: null, proForever: false, brandStamp: true,
    limits: { ocrPagesPerDay: 5, watermarkExportsPerDay: 3, maxDocuments: 30, retentionDays: 7 },
    usage: { ocrPages: 0, bonusOcrPages: 0, watermarkExports: 0, resetsAt: '' }, documentCount: 0, ads,
  });
  let assigned: string[];

  beforeEach(() => {
    plan.set(null);
    assigned = [];
    document.head.querySelectorAll(`script[src^="${ADSENSE_SCRIPT}"]`).forEach((s) => s.remove());
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: PlanService, useValue: { plan } },
        { provide: FULL_PAGE_LOAD, useValue: (url: string) => assigned.push(url) },
      ],
    });
  });

  const scripts = () => document.head.querySelectorAll(`script[src^="${ADSENSE_SCRIPT}"]`);

  it('only treats the upload page as safe for the ad script', () => {
    expect(isAdSafeUrl('/')).toBe(true);
    expect(isAdSafeUrl('/?lang=ms')).toBe(true);
    expect(isAdSafeUrl('/documents')).toBe(false);
    expect(isAdSafeUrl('/documents/abc/pages/p/text')).toBe(false);
    expect(isAdSafeUrl('/privacy')).toBe(false);
  });

  it('loads AdSense once for a Free account with web ads on, and never when they are off', () => {
    const service = TestBed.inject(WebAdsService);
    plan.set(free({ webAnchor: false, adSenseClientId: null }));
    TestBed.tick();
    expect(scripts().length).toBe(0);

    plan.set(free({ webAnchor: true, adSenseClientId: 'ca-pub-1234567890123456' }));
    TestBed.tick();
    expect(scripts().length).toBe(1);
    expect(scripts()[0].getAttribute('src')).toBe(`${ADSENSE_SCRIPT}?client=ca-pub-1234567890123456`);
    expect(service.loaded).toBe(true);
  });

  it('ignores a malformed publisher id instead of loading it', () => {
    TestBed.inject(WebAdsService);
    plan.set(free({ webAnchor: true, adSenseClientId: 'ca-pub-1"><script>' }));
    TestBed.tick();
    expect(scripts().length).toBe(0);
  });

  it('reloads the whole app before a document page opens once the ad script is loaded', () => {
    const service = TestBed.inject(WebAdsService);
    const guard = (url: string) => TestBed.runInInjectionContext(
      () => adFreeNavigationGuard(null!, { url } as RouterStateSnapshot));

    expect(guard('/documents/abc')).toBe(true);
    service.loaded = true;
    expect(guard('/')).toBe(true);
    expect(guard('/documents/abc')).toBe(false);
    expect(assigned).toEqual(['/documents/abc']);
    expect(TestBed.inject(Router)).toBeTruthy();
  });
});
