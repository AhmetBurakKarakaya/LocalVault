// Alan sınıflandırma kuralları (DOM gerektirmeyen kısım).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const code = readFileSync(new URL('../src/content/forms.js', import.meta.url), 'utf8');
const sandbox = {};
vm.runInNewContext(code, sandbox);
const { classify } = sandbox.LVForms;

const field = (overrides) => ({
  type: 'text', name: '', id: '', autocomplete: '', placeholder: '', ariaLabel: '',
  maxLength: null, inputMode: '', label: '', ...overrides,
});

const cases = [
  // [beklenen, alan, açıklama]
  ['password', { type: 'password' }, 'parola'],
  ['password', { type: 'password', autocomplete: 'current-password' }, 'mevcut parola'],
  ['new-password', { type: 'password', autocomplete: 'new-password' }, 'yeni parola'],
  ['username', { autocomplete: 'username' }, 'autocomplete=username'],
  ['username', { type: 'email' }, 'type=email'],
  ['username', { autocomplete: 'section-login email' }, 'autocomplete içinde email'],
  ['username', { name: 'login' }, 'name=login'],
  ['username', { id: 'kullaniciAdi' }, 'Türkçe kullanıcı adı'],
  ['username', { placeholder: 'E-posta adresiniz' }, 'Türkçe e-posta'],
  ['username', { name: 'tckn', type: 'tel' }, 'TC kimlik no'],
  ['username', { name: 'musteriNo' }, 'müşteri numarası'],
  ['username', { label: 'Kullanıcı adı' }, 'label metni'],
  ['otp', { autocomplete: 'one-time-code' }, 'one-time-code'],
  ['otp', { name: 'otp', inputMode: 'numeric', maxLength: 6 }, 'otp alanı'],
  ['otp', { placeholder: 'Doğrulama kodu' }, 'Türkçe doğrulama kodu'],
  ['otp', { name: 'totpPin', ariaLabel: '2FA code' }, '2FA'],
  [null, { name: 'q', placeholder: 'Search' }, 'arama kutusu'],
  [null, { name: 'firstName' }, 'ad alanı'],
  [null, { type: 'checkbox', name: 'remember' }, 'onay kutusu'],
  [null, { type: 'hidden', name: 'username' }, 'gizli alan'],
  [null, { name: 'email_newsletter', placeholder: 'Bültene abone olun' }, 'bülten'],
  [null, { type: 'number', name: 'quantity' }, 'sayı'],
  // Doğrulama kodu (Faz 4)
  ['otp', { placeholder: 'SMS kodu' }, 'SMS kodu'],
  ['otp', { label: 'Güvenlik kodu' }, 'güvenlik kodu (boşluklu)'],
  ['otp', { name: 'code', inputMode: 'numeric', maxLength: 6 }, 'name=code'],
  ['otp', { ariaLabel: 'Authenticator code' }, 'authenticator'],
  ['otp', { type: 'tel', name: 'verifyCode' }, 'verifyCode'],
  ['otp', { type: 'number', name: 'otp' }, 'number otp'],
  [null, { name: 'couponCode', placeholder: 'Kupon kodu' }, 'kupon kodu'],
  [null, { label: 'Posta kodu' }, 'posta kodu'],
  [null, { name: 'zipCode' }, 'zip code'],
  [null, { name: 'captcha_code', placeholder: 'Resimdeki kodu girin' }, 'captcha'],
  [null, { name: 'referralCode' }, 'davet kodu'],
  [null, { label: 'Ürün kodu' }, 'ürün kodu'],
  [null, { type: 'tel', name: 'areaCode' }, 'alan kodu'],
];

for (const [expected, overrides, description] of cases) {
  test(`${description} → ${expected}`, () => {
    assert.equal(classify(field(overrides)), expected);
  });
}
