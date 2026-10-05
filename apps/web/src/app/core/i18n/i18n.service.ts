import { Injectable, signal } from '@angular/core';
import { AR, Dictionary, EN, MS, ZH } from './translations';

export type LanguageCode = 'en' | 'ms' | 'zh' | 'ar';

const DICTIONARIES: Record<LanguageCode, Dictionary> = { en: EN, ms: MS, zh: ZH, ar: AR };
const RIGHT_TO_LEFT = new Set<LanguageCode>(['ar']);
const STORAGE_KEY = 'arks:language';

/** Saved choice first, then the first supported browser language, then English. */
export function pickLanguage(saved: string | null, browser: readonly string[]): LanguageCode {
  const supported = (tag: string | null | undefined): LanguageCode | null => {
    const base = tag?.toLowerCase().split('-')[0];
    return base && base in DICTIONARIES ? (base as LanguageCode) : null;
  };
  return supported(saved) ?? browser.map(supported).find((code) => code !== null) ?? 'en';
}

function savedLanguage(): string | null {
  try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
}

/** Runtime UI translation: t('key', { name: value }), with English fallback and RTL support. */
@Injectable({ providedIn: 'root' })
export class I18nService {
  readonly languages: { code: LanguageCode; name: string }[] = [
    { code: 'en', name: 'English' },
    { code: 'ms', name: 'Bahasa Melayu' },
    { code: 'zh', name: '中文 (简体)' },
    { code: 'ar', name: 'العربية' },
  ];
  readonly language = signal<LanguageCode>(
    pickLanguage(savedLanguage(), typeof navigator === 'undefined' ? [] : navigator.languages ?? [navigator.language]));

  constructor() {
    this.applyDocument(this.language());
  }

  /** Switches language; saving reloads so dates and numbers re-format in the new locale. */
  use(code: LanguageCode, save = true): void {
    this.language.set(code);
    this.applyDocument(code);
    if (!save) return;
    try { localStorage.setItem(STORAGE_KEY, code); } catch { /* private mode: this visit only */ }
    if (typeof location !== 'undefined') location.reload();
  }

  t(key: string, params: Record<string, string | number> = {}): string {
    const text = DICTIONARIES[this.language()][key] ?? EN[key] ?? key;
    return text.replace(/\{(\w+)\}/g, (match, name: string) => name in params ? String(params[name]) : match);
  }

  dictionary(code: LanguageCode): Dictionary {
    return DICTIONARIES[code];
  }

  private applyDocument(code: LanguageCode): void {
    if (typeof document === 'undefined') return;
    document.documentElement.lang = code;
    document.documentElement.dir = RIGHT_TO_LEFT.has(code) ? 'rtl' : 'ltr';
  }
}

/** Locale for Angular's date/number pipes, matching the chosen language. */
export function localeFor(code: LanguageCode): string {
  return { en: 'en', ms: 'ms', zh: 'zh', ar: 'ar' }[code];
}
