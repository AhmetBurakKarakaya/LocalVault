// Sayfa içi arayüz: alan simgeleri, hesap seçme menüsü ve kısa bildirimler.
// Kapalı (closed) shadow DOM kullanılır; sayfa betikleri içeriğe erişemez ve sitenin CSS'i karışmaz.
// Yalnızca gerçek kullanıcı tıklamalarına (event.isTrusted) tepki verilir.
// eslint-disable-next-line no-var
var LVUi = (() => {
  'use strict';

  const ICON_SIZE = 18;
  const LOCK_SVG = '<svg viewBox="0 0 24 24" width="12" height="12" aria-hidden="true"><path fill="#fff" d="M12,17A2,2 0 0,0 14,15C14,13.89 13.1,13 12,13A2,2 0 0,0 10,15A2,2 0 0,0 12,17M18,8A2,2 0 0,1 20,10V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V10C4,8.89 4.9,8 6,8H7V6A5,5 0 0,1 12,1A5,5 0 0,1 17,6V8H18M12,3A3,3 0 0,0 9,6V8H15V6A3,3 0 0,0 12,3Z"/></svg>';

  // Fluent 2 (Windows 11) görünümü; renkler masaüstü uygulamasıyla aynı.
  const STYLE = `
    :host { all: initial;
      --lv-font: "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, sans-serif;
      --lv-surface: #f9f9f9; --lv-stroke: rgba(0,0,0,.08); --lv-fg: rgba(0,0,0,.89); --lv-muted: rgba(0,0,0,.6);
      --lv-hover: rgba(0,0,0,.045); --lv-active: rgba(37,99,235,.1); --lv-accent: #2563eb; --lv-accent-hover: #1d4ed8;
      --lv-control: #fff; --lv-control-stroke: rgba(0,0,0,.09); --lv-control-bottom: rgba(0,0,0,.16);
      --lv-shadow: 0 8px 16px rgba(0,0,0,.14), 0 0 2px rgba(0,0,0,.08); }
    @media (prefers-color-scheme: dark) {
      :host { --lv-surface: #2c2c2c; --lv-stroke: rgba(0,0,0,.3); --lv-fg: #fff; --lv-muted: rgba(255,255,255,.72);
        --lv-hover: rgba(255,255,255,.06); --lv-active: rgba(59,130,246,.18); --lv-accent: #3b82f6; --lv-accent-hover: #60a5fa;
        --lv-control: #373737; --lv-control-stroke: rgba(255,255,255,.07); --lv-control-bottom: rgba(255,255,255,.05);
        --lv-shadow: 0 8px 16px rgba(0,0,0,.36), 0 0 2px rgba(0,0,0,.3); }
    }
    .icon { position: fixed; width: ${ICON_SIZE}px; height: ${ICON_SIZE}px; border-radius: 4px; border: 0; padding: 0;
            background: var(--lv-accent); display: flex; align-items: center; justify-content: center;
            cursor: pointer; box-shadow: 0 1px 2px rgba(0,0,0,.2); opacity: .88; z-index: 2147483647;
            transition: opacity .1s, background-color .1s; }
    .icon:hover { opacity: 1; background: var(--lv-accent-hover); }
    .menu { position: fixed; min-width: 260px; max-width: 360px; max-height: 300px; overflow-y: auto; z-index: 2147483647;
            box-sizing: border-box; background: var(--lv-surface); color: var(--lv-fg); border: 1px solid var(--lv-stroke);
            border-radius: 8px; padding: 4px; box-shadow: var(--lv-shadow); font: 14px/1.4 var(--lv-font);
            animation: lv-in .12s ease-out; }
    .menu-title { font-size: 12px; font-weight: 600; color: var(--lv-muted); padding: 6px 10px 6px; }
    .item { position: relative; display: flex; align-items: center; gap: 12px; width: 100%; border: 0; background: transparent;
            text-align: left; padding: 6px 10px; margin: 1px 0; border-radius: 4px; cursor: pointer; font: inherit; color: inherit; }
    .item:hover { background: var(--lv-hover); }
    .item.active { background: var(--lv-hover); }
    .item.active::before { content: ""; position: absolute; left: 0; top: 50%; width: 3px; height: 16px; margin-top: -8px;
                           border-radius: 2px; background: var(--lv-accent); }
    .avatar { flex: none; width: 28px; height: 28px; border-radius: 6px; color: #fff; font-weight: 600; font-size: 13px;
              display: flex; align-items: center; justify-content: center; }
    .text { min-width: 0; }
    .title { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .sub { color: var(--lv-muted); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .toast { position: fixed; right: 20px; bottom: 20px; max-width: 380px; z-index: 2147483647; box-sizing: border-box;
             background: var(--lv-surface); color: var(--lv-fg); border: 1px solid var(--lv-stroke);
             padding: 12px 16px; border-radius: 8px; box-shadow: var(--lv-shadow);
             font: 14px/1.4 var(--lv-font); display: flex; gap: 12px; align-items: center; animation: lv-in .15s ease-out; }
    .toast .badge, .save .head .badge { flex: none; width: 20px; height: 20px; border-radius: 5px; background: var(--lv-accent);
                    display: flex; align-items: center; justify-content: center; }
    .save { position: fixed; top: 16px; right: 16px; width: 340px; z-index: 2147483647; box-sizing: border-box;
            background: var(--lv-surface); color: var(--lv-fg); border: 1px solid var(--lv-stroke); border-radius: 8px;
            padding: 16px; box-shadow: var(--lv-shadow); font: 14px/1.43 var(--lv-font); animation: lv-in .15s ease-out; }
    .save .head { display: flex; align-items: center; gap: 10px; font-weight: 600; }
    .save .close { margin: -6px -8px -6px auto; width: 32px; height: 32px; border: 0; border-radius: 4px; background: transparent;
                   color: var(--lv-muted); font-size: 18px; cursor: pointer; line-height: 1; }
    .save .close:hover { background: var(--lv-hover); color: var(--lv-fg); }
    .save p { margin: 10px 0 2px; }
    .save .who { color: var(--lv-muted); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .save .actions { display: flex; gap: 8px; justify-content: flex-end; align-items: center; margin-top: 14px; }
    .save .actions button { font: inherit; border-radius: 4px; min-height: 32px; padding: 4px 14px; cursor: pointer;
                            border: 1px solid transparent; }
    .save .primary { background: var(--lv-accent); color: #fff; border-color: rgba(255,255,255,.08); border-bottom-color: rgba(0,0,0,.3); }
    .save .primary:hover { background: var(--lv-accent-hover); }
    .save .never { background: var(--lv-control); color: var(--lv-fg); margin-right: auto;
                   border-color: var(--lv-control-stroke); border-bottom-color: var(--lv-control-bottom); }
    .save .never:hover { background: var(--lv-hover); }
    @keyframes lv-in { from { opacity: 0; transform: translateY(-4px); } to { opacity: 1; transform: none; } }
    @media (prefers-reduced-motion: reduce) { .menu, .toast, .save { animation: none; } }`;

  const AVATAR_COLORS = ['#2563EB', '#7C3AED', '#DB2777', '#EA580C', '#059669', '#0891B2', '#CA8A04', '#4F46E5'];

  let host = null;
  let root = null;
  const icons = new Map();   // input → { button, placement }
  let menu = null;
  let toastEl = null;
  let toastTimer = null;
  let rafPending = false;

  function ensureRoot() {
    if (host?.isConnected) return;
    host = document.createElement('localvault-ui');
    host.style.cssText = 'all: initial !important; position: fixed !important; top: 0 !important; left: 0 !important; width: 0 !important; height: 0 !important; z-index: 2147483647 !important;';
    root = host.attachShadow({ mode: 'closed' });
    const style = document.createElement('style');
    style.textContent = STYLE;
    root.appendChild(style);
    (document.body ?? document.documentElement).appendChild(host);
  }

  function scheduleReposition() {
    if (rafPending) return;
    rafPending = true;
    requestAnimationFrame(() => {
      rafPending = false;
      reposition();
    });
  }

  function reposition() {
    for (const [input, { button, placement }] of icons) {
      if (!input.isConnected) {
        button.remove();
        icons.delete(input);
        continue;
      }
      const rect = input.getBoundingClientRect();
      const minWidth = placement === 'after' ? 10 : 40;
      const visible = rect.width >= minWidth && rect.height >= 14 && rect.bottom > 0 && rect.top < innerHeight;
      button.style.display = visible ? 'flex' : 'none';
      if (!visible) continue;
      // 'inside': alanın sağ iç kenarı; 'after': alanın hemen sağı (küçük OTP kutuları için)
      const left = placement === 'after' ? rect.right + 8 : rect.right - ICON_SIZE - 8;
      button.style.left = `${Math.round(left)}px`;
      button.style.top = `${Math.round(rect.top + (rect.height - ICON_SIZE) / 2)}px`;
    }
    if (menu?.anchor) placeMenu(menu.el, menu.anchor);
  }

  addEventListener('scroll', scheduleReposition, { capture: true, passive: true });
  addEventListener('resize', scheduleReposition, { passive: true });

  function addIcon(input, onClick, { placement = 'inside', title = 'LocalVault ile doldur' } = {}) {
    if (icons.has(input)) return;
    ensureRoot();
    const button = document.createElement('button');
    button.className = 'icon';
    button.type = 'button';
    button.title = title;
    button.setAttribute('aria-label', title);
    button.innerHTML = LOCK_SVG;
    // mousedown'da odak kaybını engelle: menü açılırken alan odakta kalsın.
    button.addEventListener('mousedown', (e) => e.preventDefault());
    button.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (e.isTrusted) onClick(input);
    });
    root.appendChild(button);
    icons.set(input, { button, placement });
    scheduleReposition();
  }

  function placeMenu(el, anchor) {
    const rect = anchor.getBoundingClientRect();
    const height = el.offsetHeight || 200;
    const below = rect.bottom + 4 + height <= innerHeight || rect.top < height;
    el.style.left = `${Math.max(8, Math.min(rect.left, innerWidth - el.offsetWidth - 8))}px`;
    el.style.top = `${below ? rect.bottom + 4 : rect.top - height - 4}px`;
    el.style.minWidth = `${Math.max(260, Math.min(rect.width, 360))}px`;
  }

  function closeMenu() {
    if (!menu) return;
    menu.el.remove();
    removeEventListener('mousedown', menu.outside, true);
    removeEventListener('keydown', menu.keys, true);
    menu = null;
  }

  /** Hesap seçme menüsü. logins: [{id,title,username}] */
  function showMenu(anchor, logins, onPick, heading = 'LOCALVAULT · HESAP SEÇİN') {
    closeMenu();
    ensureRoot();
    const el = document.createElement('div');
    el.className = 'menu';
    el.setAttribute('role', 'listbox');
    const title = document.createElement('div');
    title.className = 'menu-title';
    title.textContent = heading;
    el.appendChild(title);

    const items = logins.map((login, index) => {
      const item = document.createElement('button');
      item.type = 'button';
      item.className = 'item';
      item.setAttribute('role', 'option');
      const avatar = document.createElement('span');
      avatar.className = 'avatar';
      avatar.style.background = AVATAR_COLORS[colorIndex(login.title)];
      avatar.textContent = (login.title.trim()[0] ?? '?').toLocaleUpperCase('tr-TR');
      const text = document.createElement('span');
      text.className = 'text';
      const name = document.createElement('div');
      name.className = 'title';
      name.textContent = login.title;
      const sub = document.createElement('div');
      sub.className = 'sub';
      sub.textContent = login.username || '(kullanıcı adı yok)';
      text.append(name, sub);
      item.append(avatar, text);
      item.addEventListener('mousedown', (e) => e.preventDefault());
      item.addEventListener('click', (e) => {
        if (!e.isTrusted) return;
        closeMenu();
        onPick(login);
      });
      item.addEventListener('mouseenter', () => setActive(index));
      el.appendChild(item);
      return item;
    });

    let active = 0;
    function setActive(index) {
      items[active]?.classList.remove('active');
      active = (index + items.length) % items.length;
      items[active]?.classList.add('active');
      items[active]?.scrollIntoView({ block: 'nearest' });
    }
    setActive(0);

    const outside = (e) => {
      if (!e.composedPath().includes(host)) closeMenu();
    };
    const keys = (e) => {
      if (!e.isTrusted) return;
      if (e.key === 'ArrowDown') setActive(active + 1);
      else if (e.key === 'ArrowUp') setActive(active - 1);
      else if (e.key === 'Enter') {
        const login = logins[active];
        closeMenu();
        onPick(login);
      } else if (e.key === 'Escape') closeMenu();
      else return;
      e.preventDefault();
      e.stopPropagation();
    };
    addEventListener('mousedown', outside, true);
    addEventListener('keydown', keys, true);

    root.appendChild(el);
    placeMenu(el, anchor);
    menu = { el, anchor, outside, keys };
  }

  function toast(message, durationMs = 4000) {
    ensureRoot();
    toastEl?.remove();
    clearTimeout(toastTimer);
    toastEl = document.createElement('div');
    toastEl.className = 'toast';
    toastEl.setAttribute('role', 'status');
    const badge = document.createElement('span');
    badge.className = 'badge';
    badge.innerHTML = LOCK_SVG;
    const text = document.createElement('span');
    text.textContent = message;
    toastEl.append(badge, text);
    root.appendChild(toastEl);
    toastTimer = setTimeout(() => toastEl?.remove(), durationMs);
  }

  let saveEl = null;

  function closeSavePrompt() {
    saveEl?.remove();
    saveEl = null;
  }

  /**
   * "Bu hesabı kaydet?" kartı. offer: { status: 'new'|'changed', host, username, title }
   * onAction('save' | 'never' | 'dismiss') yalnızca gerçek tıklamayla çağrılır.
   */
  function showSavePrompt(offer, onAction) {
    ensureRoot();
    closeSavePrompt();
    const card = document.createElement('div');
    card.className = 'save';
    card.setAttribute('role', 'dialog');
    card.setAttribute('aria-label', 'LocalVault');

    const head = document.createElement('div');
    head.className = 'head';
    const badge = document.createElement('span');
    badge.className = 'badge';
    badge.innerHTML = LOCK_SVG;
    const name = document.createElement('span');
    name.textContent = 'LocalVault';
    const close = document.createElement('button');
    close.className = 'close';
    close.type = 'button';
    close.title = 'Şimdi değil';
    close.textContent = '×';
    head.append(badge, name, close);

    const question = document.createElement('p');
    question.textContent = offer.status === 'changed'
      ? `"${offer.title}" kaydının parolası güncellensin mi?`
      : `${offer.host} için bu hesap kaydedilsin mi?`;
    const who = document.createElement('div');
    who.className = 'who';
    who.textContent = offer.username ? `Kullanıcı: ${offer.username}` : 'Kullanıcı adı yok';

    const actions = document.createElement('div');
    actions.className = 'actions';
    const never = document.createElement('button');
    never.className = 'never';
    never.type = 'button';
    never.textContent = 'Bu sitede asla';
    const primary = document.createElement('button');
    primary.className = 'primary';
    primary.type = 'button';
    primary.textContent = offer.status === 'changed' ? 'Güncelle' : 'Kaydet';
    actions.append(never, primary);

    const bind = (button, action) => button.addEventListener('click', (e) => {
      if (!e.isTrusted) return;
      closeSavePrompt();
      onAction(action);
    });
    bind(primary, 'save');
    bind(never, 'never');
    bind(close, 'dismiss');

    card.append(head, question, who, actions);
    root.appendChild(card);
    saveEl = card;
  }

  function colorIndex(title) {
    // FNV-1a — masaüstü uygulamasıyla aynı renkler
    let hash = 2166136261;
    for (let i = 0; i < title.length; i++) {
      hash ^= title.charCodeAt(i);
      hash = Math.imul(hash, 16777619) >>> 0;
    }
    return hash % 8;
  }

  return {
    addIcon, showMenu, closeMenu, toast, showSavePrompt, closeSavePrompt,
    reposition: scheduleReposition, hasIcon: (input) => icons.has(input),
  };
})();
