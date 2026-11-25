import { DOCUMENT } from '@angular/common';
import { effect, inject, Injectable, InjectionToken } from '@angular/core';
import { CanActivateChildFn, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { PlanService } from '../plans/plan.service';

export const ADSENSE_SCRIPT = 'https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js';

/**
 * Pages where the AdSense script may run: they show no document content. AdSense can read the page it is
 * on, and a loaded script keeps running in a single-page app, so it is only ever loaded here and the app
 * reloads fully (dropping the script) before opening anything else.
 */
export function isAdSafeUrl(url: string): boolean {
  const path = url.split(/[?#]/)[0];
  return path === '/' || path === '';
}

/** Google AdSense anchor bar for Free accounts on ad-safe pages (switch: Ads__WebAnchorEnabled on the API). */
@Injectable({ providedIn: 'root' })
export class WebAdsService {
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);
  private readonly plans = inject(PlanService);
  private currentUrl = '/';

  /** True once the AdSense script is on this page; only a full reload removes it. */
  loaded = false;

  constructor() {
    this.currentUrl = this.router.url;
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe((event) => {
      this.currentUrl = (event as NavigationEnd).urlAfterRedirects;
      this.maybeLoad();
    });
    effect(() => {
      this.plans.plan();
      this.maybeLoad();
    });
  }

  private maybeLoad(): void {
    const ads = this.plans.plan()?.ads;
    if (this.loaded || !ads?.webAnchor || !ads.adSenseClientId || !isAdSafeUrl(this.currentUrl)) return;
    if (!/^ca-pub-\d{10,20}$/.test(ads.adSenseClientId)) return;
    this.loaded = true;
    const script = this.document.createElement('script');
    script.async = true;
    script.crossOrigin = 'anonymous';
    script.src = `${ADSENSE_SCRIPT}?client=${ads.adSenseClientId}`;
    this.document.head.appendChild(script);
    // Anchor (bottom bar) only; AdSense shows its own close button and consent message where required.
    const queue = ((this.document.defaultView as unknown as { adsbygoogle?: unknown[] }).adsbygoogle ??= []);
    queue.push({ google_ad_client: ads.adSenseClientId, enable_page_level_ads: true, overlays: { bottom: true } });
  }
}

/** Opens a URL with a full page load (replaceable in tests). */
export const FULL_PAGE_LOAD = new InjectionToken<(url: string) => void>('FULL_PAGE_LOAD', {
  providedIn: 'root',
  factory: () => {
    const document = inject(DOCUMENT);
    return (url: string) => document.location.assign(url);
  },
});

/** Before leaving an ad-safe page for one with documents, reload the app so the ad script is gone. */
export const adFreeNavigationGuard: CanActivateChildFn = (_route, state) => {
  const ads = inject(WebAdsService);
  if (!ads.loaded || isAdSafeUrl(state.url)) return true;
  inject(FULL_PAGE_LOAD)(state.url);
  return false;
};
