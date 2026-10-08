// LocalVault arka plan betiği: native host bağlantısı, eşleştirme ve içerik betiklerine aracılık.
// Chrome'da service worker, Firefox'ta arka plan betiği olarak çalışır.
'use strict';

if (typeof importScripts === 'function' && typeof LVProtocol === 'undefined') {
  importScripts('lib/protocol.js');
}

const ext = globalThis.browser ?? globalThis.chrome;
const NATIVE_HOST = 'com.localvault.bridge';
const DEFAULT_TIMEOUT_MS = 10000;
const PAIR_TIMEOUT_MS = 75000;

// ---------------------------------------------------------------------------
// Native host bağlantısı
// ---------------------------------------------------------------------------

let port = null;
const pending = new Map();

function connect() {
  if (port) return port;
  const current = ext.runtime.connectNative(NATIVE_HOST);
  current.onMessage.addListener((message) => {
    const entry = pending.get(message?.id);
    if (!entry) return;
    pending.delete(message.id);
    clearTimeout(entry.timer);
    entry.resolve(message);
  });
  current.onDisconnect.addListener((p) => {
    const reason = ext.runtime.lastError?.message ?? p?.error?.message ?? 'Bağlantı kapandı.';
    if (port === current) port = null;
    for (const [id, entry] of pending) {
      clearTimeout(entry.timer);
      entry.resolve({ id, ok: false, error: hostError(reason) });
    }
    pending.clear();
  });
  port = current;
  return port;
}

function hostError(reason) {
  // Chrome: "Specified native messaging host not found.", Firefox: "No such native application ..."
  const missing = /not found|no such native application|not registered/i.test(reason);
  return missing
    ? { code: 'host_missing', message: 'LocalVault masaüstü uygulaması bu tarayıcıya kayıtlı değil.' }
    : { code: 'host_unavailable', message: reason };
}

function send(request, timeoutMs = DEFAULT_TIMEOUT_MS) {
  return new Promise((resolve) => {
    let current;
    try {
      current = connect();
    } catch (error) {
      resolve({ id: request.id, ok: false, error: hostError(String(error?.message ?? error)) });
      return;
    }
    const timer = setTimeout(() => {
      pending.delete(request.id);
      resolve({ id: request.id, ok: false, error: { code: 'timeout', message: 'LocalVault yanıt vermedi.' } });
    }, timeoutMs);
    pending.set(request.id, { resolve, timer });
    try {
      current.postMessage(request);
    } catch (error) {
      pending.delete(request.id);
      clearTimeout(timer);
      port = null;
      resolve({ id: request.id, ok: false, error: hostError(String(error?.message ?? error)) });
    }
  });
}

// ---------------------------------------------------------------------------
// İmzalı istekler
// ---------------------------------------------------------------------------

async function getPairing() {
  const { pairing } = await ext.storage.local.get('pairing');
  return pairing ?? null;
}

/** Masaüstü uygulamasına istek gönderir; eşleştirilmişse imzalar ve yanıt imzasını doğrular. */
async function call(type, payload, { signed = true, timeoutMs } = {}) {
  const request = { id: LVProtocol.randomId(), type };
  if (payload !== undefined) request.payload = JSON.stringify(payload);

  let key = null;
  if (signed) {
    const pairing = await getPairing();
    if (!pairing) return { ok: false, error: { code: 'not_paired', message: 'Eklenti LocalVault ile eşleştirilmemiş.' } };
    key = LVProtocol.fromBase64(pairing.key);
    request.clientId = pairing.clientId;
    request.nonce = LVProtocol.randomId();
    request.ts = Math.floor(Date.now() / 1000);
    request.mac = await LVProtocol.requestMac(key, request);
  }

  const response = await send(request, timeoutMs);
  if (response.ok && key && !(await LVProtocol.verifyResponse(key, response))) {
    return { ok: false, error: { code: 'bad_signature', message: 'Yanıt imzası geçersiz.' } };
  }
  return {
    ok: !!response.ok,
    error: response.error ?? null,
    data: response.ok && response.payload ? JSON.parse(response.payload) : null,
  };
}

async function getStatus() {
  const ping = await call('ping', undefined, { signed: false, timeoutMs: 4000 });
  if (!ping.ok) return { state: ping.error?.code ?? 'error', message: ping.error?.message };

  const pairing = await getPairing();
  if (!pairing) return { state: 'not_paired' };
  if (ping.data.locked) return { state: 'locked', pairedAs: pairing.name };

  const test = await call('test-associate');
  if (test.ok) return { state: 'ready', pairedAs: test.data.name };
  if (test.error?.code === 'unauthorized') return { state: 'not_paired', revoked: true };
  if (test.error?.code === 'locked') return { state: 'locked', pairedAs: pairing.name };
  return { state: test.error?.code ?? 'error', message: test.error?.message };
}

// ---------------------------------------------------------------------------
// Eşleştirme: anahtar yalnızca arka planda üretilir ve saklanır.
// ---------------------------------------------------------------------------

let pairAttempt = null;

function browserName() {
  if (ext.runtime.getURL('').startsWith('moz-extension:')) return 'Firefox';
  if (/\bEdg\//.test(navigator.userAgent)) return 'Edge';
  return 'Chrome';
}

async function startPairing() {
  if (pairAttempt) return { code: pairAttempt.code };

  const key = LVProtocol.randomBytes(32);
  const clientId = LVProtocol.randomId();
  const code = await LVProtocol.verificationCode(key);

  const promise = (async () => {
    const request = {
      id: LVProtocol.randomId(),
      type: 'associate',
      clientId,
      payload: JSON.stringify({ key: LVProtocol.toBase64(key), browser: browserName() }),
    };
    const response = await send(request, PAIR_TIMEOUT_MS);
    if (!response.ok) return { ok: false, error: response.error };
    if (!(await LVProtocol.verifyResponse(key, response))) {
      return { ok: false, error: { code: 'bad_signature', message: 'Yanıt imzası geçersiz.' } };
    }
    const { name } = JSON.parse(response.payload);
    await ext.storage.local.set({ pairing: { clientId, key: LVProtocol.toBase64(key), name } });
    return { ok: true, name };
  })();

  pairAttempt = { code, promise };
  promise.finally(() => { pairAttempt = null; });
  return { code };
}

async function waitPairing() {
  return pairAttempt ? pairAttempt.promise : { ok: false, error: { code: 'no_attempt', message: 'Bekleyen eşleştirme yok.' } };
}

// ---------------------------------------------------------------------------
// Çok adımlı giriş akışı
// Kullanıcı bir sekmede LocalVault ile doldurma yaptığında o hesap birkaç dakika hatırlanır; aynı
// sekmede beliren parola adımı veya doğrulama kodu adımı bu hesapla tamamlanır. Akış yalnızca
// kullanıcı eylemiyle başlar ve masaüstü uygulaması her adımda URL eşleşmesini yeniden doğrular.
// ---------------------------------------------------------------------------

const FLOW_TTL_MS = 3 * 60 * 1000;
const flows = new Map();   // tabId → { entryId, at, passwordFilled, otpDone }

function startFlow(tabId, entryId, { passwordFilled = false, otpDone = false } = {}) {
  const current = activeFlow(tabId);
  if (current && current.entryId === entryId) {
    current.passwordFilled ||= passwordFilled;
    current.otpDone ||= otpDone;
    current.at = Date.now();
    return;
  }
  flows.set(tabId, { entryId, at: Date.now(), passwordFilled, otpDone });
}

function activeFlow(tabId) {
  const flow = flows.get(tabId);
  if (!flow) return null;
  if (Date.now() - flow.at > FLOW_TTL_MS) {
    flows.delete(tabId);
    return null;
  }
  return flow;
}

ext.tabs.onRemoved.addListener((tabId) => flows.delete(tabId));

async function continueFlow(tabId, url, need) {
  const flow = activeFlow(tabId);
  const none = { ok: false, error: { code: 'no_flow', message: 'Devam eden akış yok.' } };
  if (!flow) return none;

  if (need === 'password') {
    if (flow.passwordFilled) return none;
    const credentials = await call('get-credentials', { url, entryId: flow.entryId });
    return credentials.ok ? { ...credentials, entryId: flow.entryId } : credentials;
  }
  if (need === 'otp') {
    if (flow.otpDone) return none;
    const totp = await freshTotp(url, flow.entryId);
    if (totp.ok) flow.otpDone = true;
    return totp;
  }
  return none;
}

/** Kod süresi dolmak üzereyse bir sonrakini bekleyip onu döndürür. */
async function freshTotp(url, entryId) {
  let totp = await call('get-totp', { url, entryId });
  if (totp.ok && totp.data.remaining <= 2) {
    await new Promise((resolve) => setTimeout(resolve, totp.data.remaining * 1000 + 300));
    totp = await call('get-totp', { url, entryId });
  }
  return totp;
}

// ---------------------------------------------------------------------------
// Yeni hesap kaydetme önerisi
// Gönderilen parola yalnızca karar verilene kadar (en fazla 2 dakika) bellekte tutulur.
// ---------------------------------------------------------------------------

const SAVE_TTL_MS = 2 * 60 * 1000;
const pendingSaves = new Map();   // tabId → { url, origin, host, username, password, status, entryId, title, at }
const usernameSteps = new Map();  // tabId → { username, at }

const DEFAULT_SETTINGS = { showIcons: true, continueFlow: true, autofillOnLoad: false, offerSave: true, neverSave: [] };

async function getSettings() {
  const { settings } = await ext.storage.local.get('settings');
  return { ...DEFAULT_SETTINGS, ...(settings ?? {}) };
}

function pendingSave(tabId) {
  const pending = pendingSaves.get(tabId);
  if (pending && Date.now() - pending.at > SAVE_TTL_MS) {
    pendingSaves.delete(tabId);
    return null;
  }
  return pending ?? null;
}

/** Sayfaya gösterilecek öneri (parola içermez). */
function publicOffer(pending) {
  return { status: pending.status, host: pending.host, username: pending.username, title: pending.title };
}

ext.tabs.onRemoved.addListener((tabId) => {
  pendingSaves.delete(tabId);
  usernameSteps.delete(tabId);
});

async function onLoginSubmitted(tabId, url, message) {
  const settings = await getSettings();
  const { origin, host } = new URL(url);
  if (!settings.offerSave || settings.neverSave.includes(origin) || !message.password) return { ok: true, data: { offer: false } };

  // Çok adımlı girişlerde kullanıcı adı önceki adımda girilmiş olabilir.
  const step = usernameSteps.get(tabId);
  const username = message.username || (step && Date.now() - step.at < SAVE_TTL_MS ? step.username : '');

  const check = await call('check-login', { url, username, password: message.password });
  if (!check.ok) return check;   // kilitli/eşleşmemiş: sessizce geç
  if (check.data.status === 'exists') {
    pendingSaves.delete(tabId);
    return { ok: true, data: { offer: false } };
  }

  const pending = {
    url, origin, host, username, password: message.password, status: check.data.status,
    entryId: check.data.entryId, title: check.data.title, at: Date.now(),
  };
  pendingSaves.set(tabId, pending);
  await ext.tabs.sendMessage(tabId, { type: 'lv:offerSave', offer: publicOffer(pending) }, { frameId: 0 }).catch(() => {});
  return { ok: true, data: { offer: true } };
}

async function resolveSave(tabId, action) {
  const pending = pendingSave(tabId);
  pendingSaves.delete(tabId);
  if (!pending) return { ok: false, error: { code: 'expired', message: 'Önerinin süresi doldu.' } };

  if (action === 'never') {
    const settings = await getSettings();
    if (!settings.neverSave.includes(pending.origin)) {
      await ext.storage.local.set({ settings: { ...settings, neverSave: [...settings.neverSave, pending.origin] } });
    }
    return { ok: true };
  }
  if (action !== 'save') return { ok: true };

  return pending.status === 'changed'
    ? call('update-password', { url: pending.url, entryId: pending.entryId, password: pending.password })
    : call('save-login', { url: pending.url, username: pending.username, password: pending.password });
}

// ---------------------------------------------------------------------------
// Doldurma
// ---------------------------------------------------------------------------

async function activeTab() {
  const [tab] = await ext.tabs.query({ active: true, currentWindow: true });
  return tab ?? null;
}

function isWebUrl(url) {
  return typeof url === 'string' && /^https?:\/\//i.test(url);
}

async function loginsFor(url, interactive) {
  if (!isWebUrl(url)) return { ok: true, data: { logins: [] } };
  return call('get-logins', { url, interactive });
}

async function pageInfo(tabId) {
  try {
    return (await ext.tabs.sendMessage(tabId, { type: 'lv:pageInfo' }, { frameId: 0 })) ?? {};
  } catch {
    return {};
  }
}

/** Sayfa yalnızca doğrulama kodu istiyorsa (parola alanı yoksa) OTP adımındayız. */
function isOtpStep(info) {
  return !!info.hasOtp && !info.hasPassword;
}

/**
 * Seçilen hesabı sekmenin ana çerçevesine doldurur: OTP adımındaysa doğrulama kodunu, değilse
 * kullanıcı adı/parolayı. Değerler yalnızca aynı origin'de kalan sayfaya gönderilir.
 */
async function fillTab(tabId, url, entryId) {
  const origin = new URL(url).origin;
  const noFields = { ok: false, error: { code: 'no_fields', message: 'Sayfada doldurulacak alan bulunamadı.' } };
  const info = await pageInfo(tabId);

  if (isOtpStep(info)) {
    const totp = await freshTotp(url, entryId);
    if (!totp.ok) return totp;
    const result = await ext.tabs.sendMessage(tabId, { type: 'lv:fillOtp', origin, code: totp.data.code }, { frameId: 0 }).catch(() => null);
    if (!result?.filled) return noFields;
    startFlow(tabId, entryId, { passwordFilled: true, otpDone: true });
    return { ok: true, kind: 'otp' };
  }

  const credentials = await call('get-credentials', { url, entryId });
  if (!credentials.ok) return credentials;
  const result = await ext.tabs.sendMessage(tabId, { type: 'lv:fill', origin, ...credentials.data }, { frameId: 0 }).catch(() => null);
  if (!result?.filled) return noFields;
  startFlow(tabId, entryId, { passwordFilled: !!result.password });
  return { ok: true, kind: 'credentials' };
}

async function fillActiveTabFromShortcut() {
  const tab = await activeTab();
  if (!tab || !isWebUrl(tab.url)) return;

  const result = await loginsFor(tab.url, true);
  const notify = (message) => ext.tabs.sendMessage(tab.id, { type: 'lv:toast', message }, { frameId: 0 }).catch(() => {});

  if (!result.ok) return notify(result.error?.message ?? 'LocalVault kullanılamıyor.');
  const otpStep = isOtpStep(await pageInfo(tab.id));
  const logins = otpStep ? result.data.logins.filter((l) => l.hasTotp) : result.data.logins;
  const preferred = otpStep ? logins.find((l) => l.id === activeFlow(tab.id)?.entryId) : null;

  if (logins.length === 0) return notify(otpStep ? 'Bu site için kayıtlı doğrulama kodu (TOTP) yok.' : 'Bu site için kayıtlı hesap yok.');
  if (preferred || logins.length === 1) {
    const fill = await fillTab(tab.id, tab.url, (preferred ?? logins[0]).id);
    if (!fill.ok) notify(fill.error?.message ?? 'Doldurulamadı.');
    return;
  }
  // Birden fazla hesap: sayfadaki menüyü göster.
  await ext.tabs.sendMessage(tab.id, { type: 'lv:showMenu', logins, otp: otpStep }, { frameId: 0 }).catch(() => {});
}

// ---------------------------------------------------------------------------
// Mesajlar
// ---------------------------------------------------------------------------

const handlers = {
  // Açılır pencere / ayarlar sayfası (sender.tab yok)
  'lv:status': () => getStatus(),
  'lv:pairStart': () => startPairing(),
  'lv:pairWait': () => waitPairing(),
  'lv:unpair': async () => { await ext.storage.local.remove('pairing'); return { ok: true }; },
  'lv:launchApp': () => call('launch-app', undefined, { signed: false }),
  'lv:showApp': () => call('show-app', undefined, { signed: false }),
  'lv:popupLogins': async () => {
    const tab = await activeTab();
    if (!tab || !isWebUrl(tab.url)) return { ok: true, data: { logins: [], url: null } };
    const result = await loginsFor(tab.url, true);
    const info = await pageInfo(tab.id);
    return { ...result, tabId: tab.id, url: tab.url, otpStep: isOtpStep(info) };
  },
  'lv:popupFill': (message) => fillTab(message.tabId, message.url, message.entryId),
  'lv:popupTotp': (message) => call('get-totp', { url: message.url, entryId: message.entryId }),

  // İçerik betikleri: URL her zaman tarayıcının bildirdiği sender.url'den alınır.
  'lv:getLogins': async (message, sender) => {
    const result = await loginsFor(sender.url, !!message.interactive);
    if (result.ok) result.data.flowEntryId = activeFlow(sender.tab.id)?.entryId ?? null;
    return result;
  },
  'lv:getCredentials': async (message, sender) => {
    const result = await call('get-credentials', { url: sender.url, entryId: message.entryId });
    if (result.ok) startFlow(sender.tab.id, message.entryId);
    return result;
  },
  'lv:filled': (message, sender) => {
    if (message.password) startFlow(sender.tab.id, message.entryId, { passwordFilled: true });
    return { ok: true };
  },
  'lv:getTotp': async (message, sender) => {
    const result = await call('get-totp', { url: sender.url, entryId: message.entryId });
    if (result.ok) startFlow(sender.tab.id, message.entryId, { otpDone: true });
    return result;
  },
  'lv:continue': (message, sender) => continueFlow(sender.tab.id, sender.url, message.need),
  'lv:usernameStep': (message, sender) => {
    usernameSteps.set(sender.tab.id, { username: String(message.username ?? '').slice(0, 256), at: Date.now() });
    return { ok: true };
  },
  'lv:loginSubmitted': (message, sender) => onLoginSubmitted(sender.tab.id, sender.url, message),
  'lv:pendingSave': (message, sender) => {
    const pending = pendingSave(sender.tab.id);
    return { ok: true, data: pending ? publicOffer(pending) : null };
  },
  'lv:resolveSave': (message, sender) => resolveSave(sender.tab.id, message.action),
};

const contentOnly = new Set([
  'lv:getLogins', 'lv:getCredentials', 'lv:filled', 'lv:getTotp', 'lv:continue',
  'lv:usernameStep', 'lv:loginSubmitted', 'lv:pendingSave', 'lv:resolveSave',
]);

ext.runtime.onMessage.addListener((message, sender, sendResponse) => {
  const handler = handlers[message?.type];
  if (!handler || sender.id !== ext.runtime.id) return false;

  // Eklenti sayfaları (açılır pencere, sekmede açılan ayarlar) kendi adresimizden gelir; içerik betikleri
  // ise web sayfasının adresinden. sender.tab bu ayrım için yeterli değil (ayarlar sayfası da bir sekmededir).
  const fromExtensionPage = typeof sender.url === 'string' && sender.url.startsWith(ext.runtime.getURL(''));
  const fromContent = !fromExtensionPage && !!sender.tab && isWebUrl(sender.url);
  if (contentOnly.has(message.type) ? !fromContent : !fromExtensionPage) return false;

  Promise.resolve(handler(message, sender))
    .then(sendResponse)
    .catch((error) => sendResponse({ ok: false, error: { code: 'internal', message: String(error?.message ?? error) } }));
  return true;   // yanıt asenkron
});

ext.commands.onCommand.addListener((command) => {
  if (command === 'fill-login') fillActiveTabFromShortcut();
});
