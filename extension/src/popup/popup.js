'use strict';

const { ext, send, colorFor, describeState, el } = LVCommon;
const content = document.getElementById('content');
const badge = document.getElementById('badge');

document.getElementById('open-options').addEventListener('click', (e) => {
  e.preventDefault();
  ext.runtime.openOptionsPage();
  window.close();
});

function setBadge(info) {
  badge.textContent = info.badge;
  badge.className = `badge ${info.tone ?? ''}`;
}

function renderState(status) {
  const info = describeState(status);
  setBadge(info);
  const box = el('div', { className: 'state' },
    el('h2', { textContent: info.title }),
    el('p', { className: 'muted', textContent: info.text }));

  const action = (label, handler, primary = true) => {
    const button = el('button', { className: primary ? 'primary' : '', textContent: label });
    button.addEventListener('click', async () => {
      button.disabled = true;
      await handler();
      button.disabled = false;
    });
    box.append(button);
  };

  switch (status.state) {
    case 'app_not_running':
      action('LocalVault\'u başlat', async () => {
        const result = await send('lv:launchApp');
        if (!result.ok) box.append(el('p', { className: 'error', textContent: result.error?.message }));
        else setTimeout(refresh, 1500);
      });
      break;
    case 'not_paired':
      if (status.revoked) box.append(el('p', { className: 'error', textContent: 'Önceki bağlantı uygulamadan kaldırılmış.' }));
      action('Bağlan', async () => {
        await ext.runtime.openOptionsPage();
        window.close();
      });
      break;
    case 'locked':
      action('Kilidi aç', async () => {
        await send('lv:showApp');
        window.close();
      });
      break;
    default:
      action('Tekrar dene', refresh, false);
  }
  content.replaceChildren(box);
}

async function renderLogins() {
  const result = await send('lv:popupLogins');
  if (!result.ok) {
    renderState({ state: result.error?.code ?? 'error', message: result.error?.message });
    return;
  }

  const logins = result.data.logins;
  const children = [];
  if (result.url) {
    children.push(el('p', { className: 'site muted', textContent: new URL(result.url).host }));
  }
  if (result.otpStep && logins.length) {
    children.push(el('p', { className: 'notice', textContent: 'Bu sayfa doğrulama kodu istiyor.' }));
  }

  if (!result.url) {
    children.push(el('p', { className: 'muted center', textContent: 'Bu sayfada doldurulacak bir şey yok.' }));
  } else if (logins.length === 0) {
    children.push(el('p', { className: 'muted center', textContent: 'Bu site için kayıtlı hesap yok.' }));
  } else {
    const list = el('ul', { className: 'logins' });
    const template = document.getElementById('login-row');
    for (const login of logins) {
      const row = template.content.firstElementChild.cloneNode(true);
      const avatar = row.querySelector('.avatar');
      avatar.textContent = (login.title.trim()[0] ?? '?').toLocaleUpperCase('tr-TR');
      avatar.style.background = colorFor(login.title);
      row.querySelector('.title').textContent = login.title;
      row.querySelector('.sub').textContent = login.username || '(kullanıcı adı yok)';
      if (login.hasTotp) setupOtp(row.querySelector('.otp'), result.url, login.id);
      const button = row.querySelector('.fill');
      if (result.otpStep) {
        button.textContent = 'Kodu doldur';
        button.disabled = !login.hasTotp;
        if (!login.hasTotp) button.title = 'Bu kayıtta doğrulama kodu tanımlı değil';
      }
      button.addEventListener('click', async () => {
        button.disabled = true;
        const fill = await send('lv:popupFill', { tabId: result.tabId, url: result.url, entryId: login.id });
        if (fill.ok) {
          window.close();
          return;
        }
        button.disabled = false;
        row.after(el('li', { className: 'error', textContent: fill.error?.message ?? 'Doldurulamadı.' }));
      });
      list.append(row);
    }
    children.push(list);
  }
  content.replaceChildren(...children);
}

/** Hesabın TOTP kodunu gösterir, her saniye geri sayar, süre bitince yenisini alır; tıklayınca kopyalar. */
function setupOtp(button, url, entryId) {
  let code = '';
  let remaining = 0;
  let timer = null;

  const render = () => {
    button.querySelector('.code').textContent = code.length >= 6
      ? `${code.slice(0, code.length / 2)} ${code.slice(code.length / 2)}` : code;
    button.querySelector('.left').textContent = `${remaining} sn`;
    button.classList.toggle('expiring', remaining <= 5);
  };

  const load = async () => {
    const result = await send('lv:popupTotp', { url, entryId });
    if (!result.ok) {
      button.hidden = true;
      return;
    }
    ({ code, remaining } = result.data);
    button.hidden = false;
    render();
    clearInterval(timer);
    timer = setInterval(() => {
      remaining -= 1;
      if (remaining <= 0) load();
      else render();
    }, 1000);
  };

  button.addEventListener('click', async () => {
    await navigator.clipboard.writeText(code);
    button.classList.add('copied');
    button.querySelector('.left').textContent = 'kopyalandı';
    setTimeout(() => button.classList.remove('copied'), 1200);
  });
  load();
}

async function refresh() {
  content.replaceChildren(el('p', { className: 'muted center', textContent: 'Bağlanıyor…' }));
  const status = await send('lv:status');
  if (status.state !== 'ready') {
    renderState(status);
    return;
  }
  setBadge(describeState(status));
  badge.title = `${status.pairedAs} olarak bağlı`;
  await renderLogins();
}

refresh();
