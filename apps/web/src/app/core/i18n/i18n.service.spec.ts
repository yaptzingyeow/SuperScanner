import { TestBed } from '@angular/core/testing';
import { I18nService, pickLanguage } from './i18n.service';

describe('I18nService', () => {
  afterEach(() => {
    try { localStorage.removeItem('arks:language'); } catch { /* ignore */ }
    document.documentElement.removeAttribute('dir');
  });

  it('picks the saved choice, then the browser language, then English', () => {
    expect(pickLanguage('ms', ['zh-CN'])).toBe('ms');
    expect(pickLanguage(null, ['zh-TW', 'en'])).toBe('zh');
    expect(pickLanguage(null, ['ar-EG'])).toBe('ar');
    expect(pickLanguage(null, ['xx-YY', 'ms-MY'])).toBe('ms');
    expect(pickLanguage(null, ['fr-FR'])).toBe('en');
    expect(pickLanguage('klingon', [])).toBe('en');
  });

  it('translates with placeholders and falls back to English for missing keys', () => {
    const i18n = TestBed.inject(I18nService);
    i18n.use('ms', false);
    expect(i18n.t('nav.documents')).toBe('Dokumen saya');
    expect(i18n.t('list.pages', { count: 3 })).toBe('3 halaman');
    expect(i18n.t('test.onlyInEnglish')).toBe('English only');
    expect(i18n.t('no.such.key')).toBe('no.such.key');
  });

  it('switches the page direction to right-to-left for Arabic', () => {
    const i18n = TestBed.inject(I18nService);
    i18n.use('ar', false);
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
    i18n.use('en', false);
    expect(document.documentElement.dir).toBe('ltr');
  });

  it('every language has every English key', () => {
    const i18n = TestBed.inject(I18nService);
    const english = Object.keys(i18n.dictionary('en')).filter((key) => !key.startsWith('test.'));
    for (const language of i18n.languages.map((l) => l.code)) {
      const keys = Object.keys(i18n.dictionary(language));
      expect(english.filter((key) => !keys.includes(key)), language).toEqual([]);
    }
  });
});
