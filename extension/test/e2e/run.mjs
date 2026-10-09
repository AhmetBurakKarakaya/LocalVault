// Gerçek tarayıcıyla uçtan uca test (bağımlılık yok; Node 22+ yerleşik WebSocket kullanır).
//
// Akış: test köprü sunucusu → Chromium tabanlı tarayıcı (geçici profil, eklenti yüklü, headless)
//       → eklentinin ayarlar sayfasında "Bağlan" → gerçek native host → named pipe → eşleştirme
//       → test sayfasında LocalVault simgesine gerçek fare tıklaması → alanlar doldu mu?
//
// Ön koşullar: `node build.mjs`, `node test/serve.mjs 8765` çalışıyor ve native host kayıtlı
// (LocalVault.NativeHost.exe --register veya masaüstü uygulaması bir kez çalıştırılmış).
//
//   node test/e2e/run.mjs <tarayıcı.exe> <TestServer.dll>
import { spawn } from 'node:child_process';
import { createHmac } from 'node:crypto';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const [browserExe, testServerDll] = process.argv.slice(2);
if (!browserExe || !testServerDll) {
  console.error('Kullanım: node test/e2e/run.mjs <tarayıcı.exe> <Vault.Bridge.TestServer.dll>');
  process.exit(2);
}

const EXTENSION_ID = 'cfhminffkiclobdbhggbnpapfknickdi';
const DEBUG_PORT = 9333;
const FIXTURE = 'http://localhost:8765/test/fixtures/real-login.html';
const extensionDir = resolve(fileURLToPath(new URL('../../dist/chrome', import.meta.url)));
const pipeName = `LocalVault.E2E.${process.pid}`;
const profileDir = mkdtempSync(join(tmpdir(), 'localvault-e2e-'));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Test sunucusundaki kaydın TOTP'si (JBSWY3DPEHPK3PXP), eklentiden bağımsız hesap: RFC 6238. */
function totpCodes() {
  const secret = Buffer.from('48656c6c6f21deadbeef', 'hex');   // Base32 "JBSWY3DPEHPK3PXP"
  const counter = Math.floor(Date.now() / 1000 / 30);
  return [counter - 1, counter, counter + 1].map((c) => {
    const msg = Buffer.alloc(8);
    msg.writeBigUInt64BE(BigInt(c));
    const hash = createHmac('sha1', secret).update(msg).digest();
    const offset = hash[hash.length - 1] & 0x0f;
    return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, '0');
  });
}
const results = [];
const screenshotDir = process.env.E2E_SCREENSHOTS;

/** E2E_SCREENSHOTS ayarlıysa sekmenin görüntüsünü kaydeder (görsel kontrol için). */
async function capture(cdp, name) {
  if (!screenshotDir) return;
  mkdirSync(screenshotDir, { recursive: true });
  const { data } = await cdp.send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(join(screenshotDir, `${name}.png`), Buffer.from(data, 'base64'));
}

function check(name, ok, detail = '') {
  results.push({ name, ok, detail });
  console.log(`${ok ? '✓' : '✗'} ${name}${detail ? ` — ${detail}` : ''}`);
}

// ---- Minimal CDP istemcisi ----
class Cdp {
  static async connect(url) {
    const cdp = new Cdp();
    cdp.ws = new WebSocket(url);
    cdp.nextId = 0;
    cdp.pending = new Map();
    cdp.ws.onmessage = (event) => {
      const message = JSON.parse(event.data);
      const entry = cdp.pending.get(message.id);
      if (!entry) return;
      cdp.pending.delete(message.id);
      message.error ? entry.reject(new Error(message.error.message)) : entry.resolve(message.result);
    };
    await new Promise((res, rej) => { cdp.ws.onopen = res; cdp.ws.onerror = rej; });
    return cdp;
  }

  send(method, params = {}) {
    const id = ++this.nextId;
    this.ws.send(JSON.stringify({ id, method, params }));
    return new Promise((resolve, reject) => this.pending.set(id, { resolve, reject }));
  }

  async evaluate(expression) {
    const { result, exceptionDetails } = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (exceptionDetails) throw new Error(exceptionDetails.exception?.description ?? exceptionDetails.text);
    return result.value;
  }

  /** Odaktaki alana gerçek klavye girdisi gibi metin yazar. */
  async type(text) {
    await this.send('Input.insertText', { text });
  }

  /**
   * Kapalı shadow DOM içindeki (sayfa betiklerinin erişemediği) bir düğmenin merkezini bulur.
   * DevTools protokolü shadow kökleri de gösterebildiği için LocalVault'un sayfa içi kartı test edilebilir.
   */
  async shadowButtonCenter(label) {
    const { root } = await this.send('DOM.getDocument', { depth: -1, pierce: true });
    const stack = [root];
    while (stack.length) {
      const node = stack.pop();
      if (node.nodeName === 'BUTTON' && (node.children ?? []).some((c) => c.nodeValue?.trim() === label)) {
        const { model } = await this.send('DOM.getBoxModel', { nodeId: node.nodeId });
        const q = model.content;
        return { x: (q[0] + q[4]) / 2, y: (q[1] + q[5]) / 2 };
      }
      stack.push(...(node.children ?? []), ...(node.shadowRoots ?? []));
    }
    return null;
  }

  /** Gerçek (isTrusted) fare tıklaması. */
  async click(x, y) {
    for (const type of ['mouseMoved', 'mousePressed', 'mouseReleased']) {
      await this.send('Input.dispatchMouseEvent', { type, x, y, button: 'left', clickCount: 1 });
    }
  }

  close() { this.ws.close(); }
}

async function openTab(url) {
  const target = await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/new?${encodeURIComponent(url)}`, { method: 'PUT' })).json();
  const cdp = await Cdp.connect(target.webSocketDebuggerUrl);
  await cdp.send('Page.enable');
  await cdp.send('Page.bringToFront');
  return cdp;
}

async function waitFor(fn, timeoutMs, label) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await fn();
      if (last) return last;
    } catch (error) {
      last = error.message;
    }
    await sleep(250);
  }
  throw new Error(`Zaman aşımı: ${label} (son değer: ${JSON.stringify(last)})`);
}

// ---- Süreçler ----
const server = spawn('dotnet', [testServerDll, pipeName], { stdio: ['pipe', 'pipe', 'inherit'] });
const serverLog = [];
server.stdout.on('data', (d) => serverLog.push(...d.toString().split(/\r?\n/).filter(Boolean)));

let browser;
try {
  await waitFor(() => serverLog.some((l) => l.startsWith('HAZIR')), 15000, 'test sunucusu');

  browser = spawn(browserExe, [
    `--user-data-dir=${profileDir}`,
    `--load-extension=${extensionDir}`,
    `--disable-extensions-except=${extensionDir}`,
    '--disable-features=DisableLoadExtensionCommandLineSwitch',
    `--remote-debugging-port=${DEBUG_PORT}`,
    '--headless=new',
    '--no-first-run',
    '--no-default-browser-check',
    '--window-size=1280,800',
    'about:blank',
  ], { env: { ...process.env, LOCALVAULT_PIPE_NAME: pipeName }, stdio: 'ignore' });

  await waitFor(async () => (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/version`)).ok, 20000, 'tarayıcı');

  const worker = await waitFor(async () => {
    const targets = await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json();
    return targets.find((t) => t.url.startsWith(`chrome-extension://${EXTENSION_ID}/`) && t.type === 'service_worker');
  }, 15000, 'eklenti service worker');
  check('Eklenti beklenen sabit kimlikle yüklendi', !!worker, worker.url);

  // 1) Eşleştirme: ayarlar sayfasında "LocalVault'a bağlan"
  const options = await openTab(`chrome-extension://${EXTENSION_ID}/options/options.html`);
  const connectButton = await waitFor(() => options.evaluate(`(() => {
    const b = [...document.querySelectorAll('button')].find(b => b.textContent.includes('bağlan'));
    if (!b) return null;
    const r = b.getBoundingClientRect();
    return { x: r.x + r.width / 2, y: r.y + r.height / 2, status: document.getElementById('badge').textContent };
  })()`), 15000, 'Bağlan düğmesi');
  check('Ayarlar sayfası host\'a ulaştı ve "bağlı değil" gösterdi', connectButton.status === 'Bağlı değil', connectButton.status);
  await capture(options, 'options-not-paired');

  // Kod, onay gelene kadar kısa süre görünür; ekrana geldiği anı yakala.
  await options.evaluate(`new MutationObserver(() => {
    const code = document.querySelector('.code')?.textContent;
    if (code) window.__seenCode = code;
  }).observe(document.body, { childList: true, subtree: true })`);
  await options.click(connectButton.x, connectButton.y);
  const code = await waitFor(() => options.evaluate(`window.__seenCode`), 5000, 'doğrulama kodu');
  const serverCode = await waitFor(() => serverLog.find((l) => l.includes('eşleştirme isteği'))?.match(/kod (\S+)/)?.[1], 5000, 'sunucuda eşleştirme isteği');
  check('Eklentide ve uygulamada aynı doğrulama kodu', code === serverCode, `${code} / ${serverCode}`);

  const paired = await waitFor(() => options.evaluate(`document.getElementById('badge').textContent === 'Hazır' && document.body.innerText`), 10000, 'eşleştirme sonucu');
  check('Eşleştirme tamamlandı, durum "Hazır"', paired.includes('Chrome') || paired.includes('Edge'), paired.split('\n').find((l) => l.includes('bağlı')));
  await capture(options, 'options-paired');
  const popup = await openTab(`chrome-extension://${EXTENSION_ID}/popup/popup.html`);
  await sleep(1500);
  await capture(popup, 'popup-ready');
  popup.close();
  options.close();

  // 2) Doldurma: test sayfasında simgeye gerçek tıklama
  const page = await openTab(FIXTURE);
  const icon = await waitFor(() => page.evaluate(`(() => {
    if (!document.querySelector('localvault-ui')) return null;
    const r = document.getElementById('u').getBoundingClientRect();
    return { x: r.right - 17, y: r.top + r.height / 2 };
  })()`), 10000, 'içerik betiği simgesi');
  check('İçerik betiği giriş alanına simge ekledi', !!icon);

  await page.click(icon.x, icon.y);
  const filled = await waitFor(() => page.evaluate(`(() => {
    const u = document.getElementById('u').value, p = document.getElementById('p').value;
    return u && p ? { u, p } : null;
  })()`), 10000, 'alanların dolması');
  check('Kullanıcı adı dolduruldu', filled.u === 'e2e-kullanici', filled.u);
  check('Parola dolduruldu', filled.p === 'E2E-Parola-42!', '(gizli)');
  await capture(page, 'page-filled');

  // 3) Aynı sekmede 2FA sayfasına geçiş: kod kendiliğinden dolmalı (akış devamı)
  await page.send('Page.navigate', { url: 'http://localhost:8765/test/fixtures/real-otp.html' });
  const otp = await waitFor(() => page.evaluate(`document.getElementById('otp')?.value`), 10000, 'OTP alanının dolması');
  check('Giriş sonrası 2FA sayfasında kod kendiliğinden doldu', totpCodes().includes(otp), `${otp} (beklenen: ${totpCodes()[1]})`);
  await capture(page, 'page-otp-auto');

  // 4) 6 kutulu sayfa: akış zaten tamamlandı, bu yüzden simgeye tıklayarak doldurulur
  await page.send('Page.navigate', { url: 'http://localhost:8765/test/fixtures/real-otp-boxes.html' });
  const boxIcon = await waitFor(() => page.evaluate(`(() => {
    if (!document.querySelector('localvault-ui')) return null;
    const boxes = document.querySelectorAll('#boxes input');
    const r = boxes[boxes.length - 1].getBoundingClientRect();
    return { x: r.right + 8 + 9, y: r.top + r.height / 2, auto: [...boxes].map(b => b.value).join('') };
  })()`), 10000, '6 kutu simgesi');
  check('Akış bir kez tamamlandıktan sonra ikinci 2FA sayfası kendiliğinden doldurulmadı', boxIcon.auto === '', boxIcon.auto || '(boş)');
  await page.click(boxIcon.x, boxIcon.y);
  const boxed = await waitFor(() => page.evaluate(`(() => {
    const v = [...document.querySelectorAll('#boxes input')].map(b => b.value).join('');
    return v.length === 6 ? v : null;
  })()`), 10000, 'kutuların dolması');
  check('6 kutulu forma simgeyle kod dolduruldu', totpCodes().includes(boxed), boxed);
  const boxStats = await page.evaluate(`(() => {
    const log = window.__focusLog;
    return { backward: log.filter((x, k) => k > 0 && x < log[k - 1]).length, focus: log.length, submits: window.__submits };
  })()`);
  check('Kutular arasında ileri geri gezinme yok ve site kodu tek kez doğruladı',
    boxStats.backward === 0 && boxStats.submits === 1, `geri dönüş ${boxStats.backward}, doğrulama ${boxStats.submits}`);
  await capture(page, 'page-otp-boxes');

  // 4b) Kurumsal portal benzeri AngularJS kutuları (kapsayıcıda dinleyen, gecikmeli yapıştıran, odakta
  // bir sonraki turda select() eden yönerge): simgeye tıklandıktan sonra odak kısa sürede durmalı.
  // ?nopaste sürümünde eklenti kutuları tek tek doldurur; sonsuz odak döngüsü bu yolda oluşuyordu.
  for (const variant of ['', '?nopaste']) {
    const label = variant ? 'yapıştırmasız portal' : 'Portal';
    await page.send('Page.navigate', { url: `http://localhost:8765/test/fixtures/portal-auth-inputs.html${variant}` });
    const portalIcon = await waitFor(() => page.evaluate(`(() => {
      const boxes = document.querySelectorAll('input.auth-input');
      if (boxes.length !== 6 || !document.querySelector('localvault-ui')) return null;
      const r = boxes[5].getBoundingClientRect();
      return { x: r.right + 8 + 9, y: r.top + r.height / 2 };
    })()`), 10000, `${label} kutularının simgesi`);
    await page.click(portalIcon.x, portalIcon.y);
    const clickedAt = await page.evaluate(`Math.round(performance.now())`);
    await new Promise((r) => setTimeout(r, 3000));
    const portal = await page.evaluate(`({
      value: [...document.querySelectorAll('input.auth-input')].map((b) => b.value).join(''),
      focus: window.__focusLog,
      digests: window.__digests,
    })`);
    const lateFocus = portal.focus.filter(([ms]) => ms > clickedAt + 1500).length;
    check(`${label} kutularına simgeyle kod dolduruldu`, totpCodes().includes(portal.value), portal.value);
    check(`${label} kutularında odak durdu (sürekli gezinme yok)`, lateFocus === 0 && portal.focus.length <= 15,
      `toplam odak ${portal.focus.length}, son 1,5 sn'de ${lateFocus}, digest ${portal.digests}`);
  }
  await capture(page, 'page-portal-auth-inputs');

  const log = serverLog.filter((l) => /→/.test(l));
  check('İstekler imzalı yoldan geçti (get-logins, get-credentials)',
    log.some((l) => l.startsWith('get-logins') && l.endsWith('ok')) && log.some((l) => l.startsWith('get-credentials') && l.endsWith('ok')));
  check('Doğrulama kodu imzalı get-totp ile alındı', log.some((l) => l.startsWith('get-totp') && l.endsWith('ok')));
  page.close();

  // 3) Eşleşmeyen site: parola istenmemeli
  const other = await openTab('http://127.0.0.1:8765/test/fixtures/real-login.html');
  const otherIcon = await waitFor(() => other.evaluate(`(() => {
    if (!document.querySelector('localvault-ui')) return null;
    const r = document.getElementById('u').getBoundingClientRect();
    return { x: r.right - 17, y: r.top + r.height / 2 };
  })()`), 10000, 'ikinci sayfada simge');
  const before = serverLog.filter((l) => l.startsWith('get-credentials')).length;
  await other.click(otherIcon.x, otherIcon.y);
  await sleep(1500);
  await capture(other, 'page-other-origin');
  const otherValues = await other.evaluate(`[document.getElementById('u').value, document.getElementById('p').value]`);
  const after = serverLog.filter((l) => l.startsWith('get-credentials')).length;
  check('Farklı origin (127.0.0.1) için hiçbir şey doldurulmadı ve parola istenmedi',
    otherValues[0] === '' && otherValues[1] === '' && before === after);

  // 5) Yeni hesabı kaydetme önerisi: kasada olmayan sitede elle giriş → öneri → Kaydet
  const fieldCenter = (id) => other.evaluate(`(() => {
    const r = document.getElementById('${id}').getBoundingClientRect();
    return { x: r.left + 40, y: r.top + r.height / 2 };
  })()`);
  const emailField = await fieldCenter('u');
  await other.click(emailField.x, emailField.y);
  await other.type('yeni.hesap@ornek.com');
  const passwordField = await fieldCenter('p');
  await other.click(passwordField.x, passwordField.y);
  await other.type('Elle-Girilen-7!');
  const submit = await other.evaluate(`(() => { const r = document.querySelector('button').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()`);
  await other.click(submit.x, submit.y);

  const saveButton = await waitFor(() => other.shadowButtonCenter('Kaydet'), 10000, 'kaydetme önerisi');
  check('Elle girilen yeni hesap için kaydetme önerisi gösterildi', !!saveButton);
  await capture(other, 'save-offer');
  await other.click(saveButton.x, saveButton.y);
  await waitFor(() => serverLog.some((l) => l.startsWith('save-login') && l.endsWith('ok')), 10000, 'save-login');
  check('Kaydet tıklamasıyla hesap kasaya eklendi (save-login)', serverLog.some((l) => l.includes('kaydedildi') && l.includes('127.0.0.1')));

  // Kaydedilen hesap artık bu sitede doldurulabilmeli.
  await other.send('Page.reload');
  const savedIcon = await waitFor(() => other.evaluate(`(() => {
    if (!document.querySelector('localvault-ui')) return null;
    const r = document.getElementById('u').getBoundingClientRect();
    return { x: r.right - 17, y: r.top + r.height / 2 };
  })()`), 10000, 'yenilenen sayfada simge');
  await other.click(savedIcon.x, savedIcon.y);
  const refilled = await waitFor(() => other.evaluate(`(() => {
    const u = document.getElementById('u').value, p = document.getElementById('p').value;
    return u && p ? { u, p } : null;
  })()`), 10000, 'kaydedilen hesabın doldurulması');
  check('Kaydedilen hesap bir sonraki ziyarette dolduruldu', refilled.u === 'yeni.hesap@ornek.com' && refilled.p === 'Elle-Girilen-7!', refilled.u);

  // Aynı bilgilerle tekrar giriş: hesap artık kayıtlı, öneri gösterilmemeli.
  await other.click(submit.x, submit.y);
  await sleep(1500);
  check('Kayıtlı hesapla girişte öneri tekrar gösterilmedi', (await other.shadowButtonCenter('Kaydet')) === null);
  other.close();
} catch (error) {
  check('Beklenmeyen hata', false, error.message);
} finally {
  try {
    const version = await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/version`)).json();
    const cdp = await Cdp.connect(version.webSocketDebuggerUrl);
    await cdp.send('Browser.close').catch(() => {});
  } catch {
    browser?.kill();
  }
  server.stdin.end();
  await sleep(1000);
  server.kill();
  try {
    rmSync(profileDir, { recursive: true, force: true });
  } catch {
    // Tarayıcı dosyaları hâlâ kilitliyse geçici klasör sonra temizlenir.
  }
  console.log('\nTest sunucusu günlüğü:\n  ' + serverLog.join('\n  '));
  const failed = results.filter((r) => !r.ok).length;
  console.log(`\n${results.length - failed}/${results.length} kontrol geçti.`);
  process.exit(failed ? 1 : 0);
}
