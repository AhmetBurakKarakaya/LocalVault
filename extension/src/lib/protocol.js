// LocalVault köprü protokolü: istek imzalama (HMAC-SHA256), yanıt doğrulama ve doğrulama kodu.
// Kanonik metinler Vault.Bridge/MessageAuth.cs ile birebir aynıdır; uyum ortak test vektörleriyle sınanır.
(function (root) {
  'use strict';

  const encoder = new TextEncoder();

  function toBase64(bytes) {
    let binary = '';
    for (const b of bytes) binary += String.fromCharCode(b);
    return btoa(binary);
  }

  function fromBase64(text) {
    const binary = atob(text);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
  }

  function randomBytes(length) {
    const bytes = new Uint8Array(length);
    crypto.getRandomValues(bytes);
    return bytes;
  }

  function randomId() {
    return Array.from(randomBytes(16), (b) => b.toString(16).padStart(2, '0')).join('');
  }

  async function hmac(keyBytes, message) {
    const key = await crypto.subtle.importKey('raw', keyBytes, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
    const signature = await crypto.subtle.sign('HMAC', key, encoder.encode(message));
    return toBase64(new Uint8Array(signature));
  }

  /** type \n clientId \n nonce \n ts \n payload */
  function requestMac(keyBytes, request) {
    return hmac(keyBytes,
      `${request.type}\n${request.clientId}\n${request.nonce}\n${request.ts}\n${request.payload ?? ''}`);
  }

  /** id \n ok(1/0) \n payload */
  function responseMac(keyBytes, response) {
    return hmac(keyBytes, `${response.id}\n${response.ok ? '1' : '0'}\n${response.payload ?? ''}`);
  }

  async function verifyResponse(keyBytes, response) {
    if (typeof response.mac !== 'string') return false;
    const expected = await responseMac(keyBytes, response);
    // Sabit zamanlı karşılaştırma
    if (expected.length !== response.mac.length) return false;
    let diff = 0;
    for (let i = 0; i < expected.length; i++) diff |= expected.charCodeAt(i) ^ response.mac.charCodeAt(i);
    return diff === 0;
  }

  /** Eşleştirmede iki tarafta gösterilen kısa kod, ör. "630-DCD". */
  async function verificationCode(keyBytes) {
    const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', keyBytes));
    const hex = Array.from(hash.slice(0, 3), (b) => b.toString(16).padStart(2, '0')).join('').toUpperCase();
    return `${hex.slice(0, 3)}-${hex.slice(3)}`;
  }

  const api = { toBase64, fromBase64, randomBytes, randomId, requestMac, responseMac, verifyResponse, verificationCode };
  root.LVProtocol = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(globalThis);
