'use strict';

const { ext, send, describeState, el } = LVCommon;
const connection = document.getElementById('connection');
const badge = document.getElementById('badge');
const DEFAULT_SETTINGS = { showIcons: true, continueFlow: true, autofillOnLoad: false, offerSave: true };

function setBadge(info) {
  badge.textContent = info.badge;
  badge.className = `badge ${info.tone ?? ''}`;
}

function button(label, handler, className = '') {
  const node = el('button', { className, textContent: label });
  node.addEventListener('click', async () => {
    node.disabled = true;
    try {
      await handler();
    } finally {
      node.disabled = false;
    }
  });
  return node;
}

async function pair() {
  const start = await send('lv:pairStart');
  if (!start.code) {
    connection.replaceChildren(el('p', { className: 'error', textContent: start.error?.message ?? 'Eşleştirme başlatılamadı.' }));
    return;
  }

  connection.replaceChildren(
    el('p', { textContent: 'LocalVault penceresinde bir bağlantı isteği açıldı. Oradaki kodun bununla aynı olduğunu kontrol edip "İzin ver"e tıklayın:' }),
    el('span', { className: 'code', textContent: start.code }),
    el('p', { className: 'muted', textContent: 'Onay bekleniyor… (60 sn)' }));

  const result = await send('lv:pairWait');
  if (result.ok) {
    connection.replaceChildren(el('p', { className: 'success', textContent: `Bağlandı: ${result.name}` }));
    setTimeout(refresh, 1200);
    return;
  }

  const messages = {
    denied: 'İstek reddedildi veya süresi doldu.',
    locked: 'Kasa kilitli. LocalVault\'ta kilidi açıp tekrar deneyin.',
    app_not_running: 'LocalVault çalışmıyor.',
  };
  connection.replaceChildren(
    el('p', { className: 'error', textContent: messages[result.error?.code] ?? result.error?.message ?? 'Eşleştirilemedi.' }),
    button('Tekrar dene', pair, 'primary'));
}

async function refresh() {
  const status = await send('lv:status');
  const info = describeState(status);
  setBadge(info);

  if (status.state === 'ready') {
    connection.replaceChildren(
      el('p', { textContent: `Bu tarayıcı LocalVault'a "${status.pairedAs}" adıyla bağlı.` }),
      button('Bağlantıyı kaldır', async () => {
        await send('lv:unpair');
        await refresh();
      }, 'danger'));
    return;
  }

  const children = [el('p', { className: 'muted', textContent: info.text })];
  if (status.state === 'not_paired') {
    if (status.revoked) children.unshift(el('p', { className: 'notice warn', textContent: 'Önceki bağlantı uygulamadan kaldırılmış; yeniden eşleştirin.' }));
    children.push(button('LocalVault\'a bağlan', pair, 'primary'));
  } else if (status.state === 'app_not_running') {
    children.push(el('div', { className: 'row' },
      button('LocalVault\'u başlat', async () => {
        await send('lv:launchApp');
        setTimeout(refresh, 2000);
      }, 'primary'),
      button('Yenile', refresh)));
  } else if (status.state === 'locked') {
    children.push(el('div', { className: 'row' },
      button('Pencereyi aç', () => send('lv:showApp'), 'primary'),
      button('Yenile', refresh)));
  } else {
    children.push(button('Yenile', refresh));
  }
  connection.replaceChildren(...children);
}

async function loadSettings() {
  const { settings } = await ext.storage.local.get('settings');
  const current = { ...DEFAULT_SETTINGS, ...(settings ?? {}) };
  for (const key of Object.keys(DEFAULT_SETTINGS)) {
    const input = document.getElementById(key);
    const stateText = input.parentElement.querySelector('.state-text');
    const showState = () => { if (stateText) stateText.textContent = input.checked ? 'Açık' : 'Kapalı'; };
    input.checked = current[key];
    showState();
    input.addEventListener('change', async () => {
      showState();
      const { settings: latest } = await ext.storage.local.get('settings');
      await ext.storage.local.set({ settings: { ...DEFAULT_SETTINGS, ...(latest ?? {}), [key]: input.checked } });
    });
  }
}

async function renderNeverList() {
  const { settings } = await ext.storage.local.get('settings');
  const sites = settings?.neverSave ?? [];
  document.getElementById('never-box').hidden = sites.length === 0;
  document.getElementById('never-list').replaceChildren(...sites.map((origin) => el('li', {},
    el('span', { textContent: origin }),
    button('Kaldır', async () => {
      const { settings: latest } = await ext.storage.local.get('settings');
      await ext.storage.local.set({ settings: { ...latest, neverSave: (latest?.neverSave ?? []).filter((o) => o !== origin) } });
      await renderNeverList();
    }, 'small danger'))));
}

loadSettings();
renderNeverList();
refresh();
