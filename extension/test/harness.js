// Tarayıcıda içerik betiklerini eklenti olmadan sınamak için sahte "chrome" API'si.
// Sorgu parametreleri:
//   ?logins=0|1|2   eşleşen hesap sayısı      ?autofill   sayfa açılınca doldurma açık
//   ?error=locked   tüm istekler bu hatayla   ?flow       bu sekmede devam eden bir giriş akışı var
//   ?digits=8       TOTP hane sayısı          ?save=new|changed  gönderimde kaydetme önerisi
//   ?pending        sayfa açılışında bekleyen öneri
(() => {
  const params = new URLSearchParams(location.search);
  const count = Number(params.get('logins') ?? 2);
  const error = params.get('error');
  const digits = Number(params.get('digits') ?? 6);
  const code = '12345678'.slice(0, digits);

  const LOGINS = [
    { id: 'a1', title: 'GitHub', username: 'ahmet@example.com', hasPassword: true, hasTotp: true },
    { id: 'b2', title: 'GitHub (iş)', username: 'ahmet.k@firma.com', hasPassword: true, hasTotp: false },
  ];
  const CREDENTIALS = {
    a1: { username: 'ahmet@example.com', password: 'Parola-1!' },
    b2: { username: 'ahmet.k@firma.com', password: 'Is-Parola-2!' },
  };

  // Arka planın akış durumunu taklit eder.
  const flow = params.has('flow') ? { entryId: 'a1', passwordFilled: false, otpDone: false } : null;

  window.__lvLog = [];
  globalThis.chrome = {
    runtime: {
      async sendMessage(message) {
        window.__lvLog.push(message);
        if (error) return { ok: false, error: { code: error, message: error } };
        switch (message.type) {
          case 'lv:getLogins':
            return { ok: true, data: { logins: LOGINS.slice(0, count), flowEntryId: flow?.entryId ?? null } };
          case 'lv:getCredentials':
            return { ok: true, data: CREDENTIALS[message.entryId] };
          case 'lv:getTotp':
            return LOGINS.find((l) => l.id === message.entryId)?.hasTotp
              ? { ok: true, data: { code, remaining: 20, period: 30 } }
              : { ok: false, error: { code: 'not_found', message: 'TOTP yok' } };
          case 'lv:filled':
            if (flow && message.password) flow.passwordFilled = true;
            return { ok: true };
          case 'lv:continue':
            if (!flow) return { ok: false, error: { code: 'no_flow' } };
            if (message.need === 'password' && !flow.passwordFilled) return { ok: true, data: CREDENTIALS.a1, entryId: 'a1' };
            if (message.need === 'otp' && !flow.otpDone) {
              flow.otpDone = true;
              return { ok: true, data: { code, remaining: 20, period: 30 } };
            }
            return { ok: false, error: { code: 'no_flow' } };
          case 'lv:usernameStep':
            return { ok: true };
          case 'lv:loginSubmitted': {
            // ?save=new|changed → arka plan kasaya sordu ve öneri gönderdi
            if (!params.has('save')) return { ok: true, data: { offer: false } };
            const offer = { status: params.get('save') || 'new', host: location.host, username: message.username, title: 'GitHub' };
            window.__lvListener({ type: 'lv:offerSave', offer }, {}, () => {});
            return { ok: true, data: { offer: true } };
          }
          case 'lv:pendingSave':
            return { ok: true, data: params.has('pending') ? { status: 'new', host: location.host, username: 'onceki@example.com' } : null };
          case 'lv:resolveSave':
            return { ok: true };
          default:
            return { ok: false, error: { code: 'bad_request', message: 'bilinmeyen' } };
        }
      },
      onMessage: { addListener(fn) { window.__lvListener = fn; } },
    },
    storage: {
      local: { async get() { return { settings: { showIcons: true, continueFlow: true, offerSave: true, autofillOnLoad: params.has('autofill') } }; } },
    },
  };

  /** Arka planın gönderdiği bir mesajı taklit eder (ör. kısayol veya açılır pencereden doldurma). */
  window.__lvSend = (message) => new Promise((resolve) => window.__lvListener(message, {}, resolve));
  window.__lvCalls = (type) => window.__lvLog.filter((m) => m.type === type);
})();
