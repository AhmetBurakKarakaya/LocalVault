// Açılır pencere ve ayarlar sayfasının ortak yardımcıları.
// eslint-disable-next-line no-var
var LVCommon = (() => {
  'use strict';

  const ext = globalThis.browser ?? globalThis.chrome;
  const AVATAR_COLORS = ['#2563EB', '#7C3AED', '#DB2777', '#EA580C', '#059669', '#0891B2', '#CA8A04', '#4F46E5'];

  async function send(type, extra = {}) {
    try {
      return (await ext.runtime.sendMessage({ type, ...extra })) ?? { ok: false, error: { message: 'Yanıt yok.' } };
    } catch (error) {
      return { ok: false, error: { code: 'internal', message: String(error?.message ?? error) } };
    }
  }

  function colorFor(title) {
    let hash = 2166136261;
    for (let i = 0; i < title.length; i++) {
      hash ^= title.charCodeAt(i);
      hash = Math.imul(hash, 16777619) >>> 0;
    }
    return AVATAR_COLORS[hash % 8];
  }

  /** Durum → kullanıcıya gösterilecek başlık/açıklama. */
  const STATES = {
    host_missing: {
      badge: 'Kurulu değil', tone: 'warn', title: 'Masaüstü uygulaması bulunamadı',
      text: 'LocalVault masaüstü uygulamasını en az bir kez çalıştırın; tarayıcı kaydını kendisi yapar. Ardından tarayıcıyı yeniden başlatın.',
    },
    app_not_running: {
      badge: 'Kapalı', tone: 'warn', title: 'LocalVault çalışmıyor',
      text: 'Hesaplarınıza erişmek için masaüstü uygulamasını başlatın.',
    },
    not_paired: {
      badge: 'Bağlı değil', tone: 'warn', title: 'Eklenti bağlı değil',
      text: 'Eklentinin kasanıza erişebilmesi için LocalVault uygulamasıyla bir kez eşleştirin.',
    },
    locked: {
      badge: 'Kilitli', tone: 'warn', title: 'Kasa kilitli',
      text: 'LocalVault penceresinde ana parolanızla kilidi açın.',
    },
    ready: { badge: 'Hazır', tone: 'ready' },
  };

  function describeState(status) {
    return STATES[status.state] ?? {
      badge: 'Hata', tone: 'warn', title: 'LocalVault\'a ulaşılamadı', text: status.message ?? 'Bilinmeyen hata.',
    };
  }

  function el(tag, props = {}, ...children) {
    const node = document.createElement(tag);
    Object.assign(node, props);
    node.append(...children);
    return node;
  }

  return { ext, send, colorFor, describeState, el };
})();
