// Ortak test vektörleri: tests/Vault.Bridge.Tests/ProtocolTests.cs ile aynı (bağımsız olarak Python ile hesaplandı).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const P = require('../src/lib/protocol.js');
const KEY = Uint8Array.from({ length: 32 }, (_, i) => i);

test('istek imzası C# ile aynı', async () => {
  const mac = await P.requestMac(KEY, {
    type: 'get-logins',
    clientId: 'client-1',
    nonce: 'nonce-1',
    ts: 1700000000,
    payload: '{"url":"https://github.com/login","interactive":true}',
  });
  assert.equal(mac, 'uCHXrrgV8znWcMX86B+YcwuGXLtHpTobQiyLfZObhJg=');
});

test('yanıt imzası C# ile aynı ve doğrulanıyor', async () => {
  const response = { id: 'req-42', ok: true, payload: '{"logins":[]}', mac: 'XkaDp6Pnc0KKpK6JUXwrqw1t5TKJIxNrMUUUb2Gs+78=' };
  assert.equal(await P.responseMac(KEY, response), response.mac);
  assert.equal(await P.verifyResponse(KEY, response), true);
  assert.equal(await P.verifyResponse(KEY, { ...response, payload: '{"logins":[1]}' }), false);
  assert.equal(await P.verifyResponse(KEY, { ...response, ok: false }), false);
  assert.equal(await P.verifyResponse(KEY, { ...response, mac: undefined }), false);
  assert.equal(await P.verifyResponse(new Uint8Array(32), response), false);
});

test('doğrulama kodu C# ile aynı', async () => {
  assert.equal(await P.verificationCode(KEY), '630-DCD');
});

test('Base64 gidiş-dönüş ve rastgele değerler', () => {
  assert.equal(P.toBase64(KEY), 'AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=');
  assert.deepEqual(P.fromBase64(P.toBase64(KEY)), KEY);
  assert.match(P.randomId(), /^[0-9a-f]{32}$/);
  assert.notEqual(P.randomId(), P.randomId());
  assert.equal(P.randomBytes(32).length, 32);
});
