import { TestBed } from '@angular/core/testing';
import { I18nService, pickLanguage } from './i18n.service';
import en from './lang/en';
import ms from './lang/ms';
import zh from './lang/zh';
import ar from './lang/ar';

describe('I18nService', () => {
  afterEach(() => document.documentElement.removeAttribute('dir'));

  it('picks the saved choice, then the browser language, then English', () => {
    expect(pickLanguage('ms', ['zh-CN'])).toBe('ms');
    expect(pickLanguage(null, ['zh-TW', 'en'])).toBe('zh');
    expect(pickLanguage(null, ['ar-EG'])).toBe('ar');
    expect(pickLanguage(null, ['xx-YY', 'ms-MY'])).toBe('ms');
    expect(pickLanguage(null, ['fr-FR'])).toBe('en');
    expect(pickLanguage('klingon', [])).toBe('en');
  });

  it('translates with placeholders and falls back to English for missing keys', async () => {
    const i18n = TestBed.inject(I18nService);
    await i18n.load('ms');
    expect(i18n.t('nav.documents')).toBe('Dokumen saya');
    expect(i18n.t('list.pages', { count: 3 })).toBe('3 halaman');
    expect(i18n.t('test.onlyInEnglish')).toBe('English only');
    expect(i18n.t('no.such.key')).toBe('no.such.key');
  });

  it('switches the page direction to right-to-left for Arabic', async () => {
    const i18n = TestBed.inject(I18nService);
    await i18n.load('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
    await i18n.load('en');
    expect(document.documentElement.dir).toBe('ltr');
  });

  it('every language has every English key with the same placeholders', () => {
    const english = Object.keys(en).filter((key) => !key.startsWith('test.'));
    for (const [code, dictionary] of Object.entries({ ms, zh, ar })) {
      expect(english.filter((key) => !(key in dictionary)), code).toEqual([]);
      const placeholders = (text: string) => (text.match(/\{\w+\}/g) ?? []).sort().join();
      expect(english.filter((key) => placeholders(en[key]) !== placeholders(dictionary[key])), code).toEqual([]);
    }
  });
});
