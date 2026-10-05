import { Injectable, signal } from '@angular/core';
import english from './lang/en';

export type LanguageCode = 'en' | 'ms' | 'zh' | 'ar';
type Dictionary = Record<string, string>;

/** Non-English dictionaries are separate chunks, downloaded only when that language is chosen. */
const LOADERS: Record<LanguageCode, () => Promise<Dictionary>> = {
  en: async () => english,
  ms: async () => (await import('./lang/ms')).default,
  zh: async () => (await import('./lang/zh')).default,
  ar: async () => (await import('./lang/ar')).default,
};
const RIGHT_TO_LEFT = new Set<LanguageCode>(['ar']);
const STORAGE_KEY = 'arks:language';

/** Saved choice first, then the first supported browser language, then English. */
export function pickLanguage(saved: string | null, browser: readonly string[]): LanguageCode {
  const supported = (tag: string | null | undefined): LanguageCode | null => {
    const base = tag?.toLowerCase().split('-')[0];
    return base && base in LOADERS ? (base as LanguageCode) : null;
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
  private readonly active = signal<Dictionary>(english);

  constructor() {
    this.applyDocument(this.language());
  }

  /** Loads the current language's text; the app waits for this before showing anything. */
  async ready(): Promise<void> {
    await this.load(this.language());
  }

  /** Switches language now (used by tests and on start); text falls back to English until loaded. */
  async load(code: LanguageCode): Promise<void> {
    this.language.set(code);
    this.applyDocument(code);
    const dictionary = await LOADERS[code]();
    if (this.language() === code) this.active.set(dictionary);
  }

  /** The person picked a language: remember it and reload so dates and numbers re-format too. */
  use(code: LanguageCode): void {
    try { localStorage.setItem(STORAGE_KEY, code); } catch { /* private mode: this visit only */ }
    if (typeof location !== 'undefined') location.reload();
  }

  t(key: string, params: Record<string, string | number> = {}): string {
    const text = this.active()[key] ?? english[key] ?? key;
    return text.replace(/\{(\w+)\}/g, (match, name: string) => name in params ? String(params[name]) : match);
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
