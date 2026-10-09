// Giriş formu algılama ve doldurma. İçerik betiklerinin yalıtılmış dünyasında çalışır;
// sayfa betikleri bu değişkenlere erişemez.
// eslint-disable-next-line no-var
var LVForms = (() => {
  'use strict';

  const USERNAME_HINT = /user|login|e-?mail|account|uname|identifier|kullan[ıi]c[ıi]|e-?posta|e_posta|musteri|müşteri|tckn|tc_?kimlik|t\.?c\.?_?no|cust|phone|telefon|gsm|mobile/i;
  const NOT_USERNAME_HINT = /search|arama|query|captcha|otp|one-?time|2fa|token|coupon|kupon|promo|zip|postal|posta_?kodu|newsletter|bülten/i;
  const OTP_HINT = /otp|one-?time|2fa|mfa|totp|verification|verify|doğrulama|dogrulama|authenticat|sms[ _-]?code|auth[ _-]?code|security[ _-]?code|güvenlik[ _-]?kodu|onay[ _-]?kodu|\bcode\b|\bkodu?\b/i;
  // "Kod" geçen ama doğrulama kodu olmayan alanlar
  const OTP_EXCLUDE = /coupon|kupon|promo|zip|postal|posta[ _-]?kodu|captcha|referral|referans|davet|invite|gift|hediye|country|ülke|area[ _-]?code|alan[ _-]?kodu|discount|indirim|voucher|iban|swift|tax|vergi|product|ürün|stok|sku/i;
  const TEXT_TYPES = new Set(['text', 'email', 'tel', '']);
  const OTP_TYPES = new Set(['text', 'tel', 'number', '']);

  /** Bir input'un sınıflandırılmasında kullanılan öznitelikleri toplar (DOM'suz test edilebilsin diye ayrık). */
  function describe(el) {
    return {
      type: (el.getAttribute('type') || 'text').toLowerCase(),
      name: el.getAttribute('name') || '',
      id: el.id || '',
      autocomplete: (el.getAttribute('autocomplete') || '').toLowerCase(),
      placeholder: el.getAttribute('placeholder') || '',
      ariaLabel: el.getAttribute('aria-label') || '',
      maxLength: el.maxLength > 0 ? el.maxLength : null,
      inputMode: (el.getAttribute('inputmode') || '').toLowerCase(),
      label: labelText(el),
    };
  }

  function labelText(el) {
    const labels = el.labels ? Array.from(el.labels).map((l) => l.textContent || '') : [];
    return labels.join(' ').trim().slice(0, 100);
  }

  /**
   * Bir alanı sınıflandırır: 'password' | 'new-password' | 'username' | 'otp' | null
   * @param {{type:string,name:string,id:string,autocomplete:string,placeholder:string,ariaLabel:string,maxLength:number|null,inputMode:string,label?:string}} a
   */
  function classify(a) {
    const tokens = a.autocomplete.split(/\s+/);
    if (a.type === 'password') {
      return tokens.includes('new-password') ? 'new-password' : 'password';
    }
    if (!TEXT_TYPES.has(a.type) && a.type !== 'number') return null;

    if (tokens.includes('one-time-code')) return 'otp';
    if (tokens.includes('username') || tokens.includes('email')) return 'username';

    const text = `${a.name} ${a.id} ${a.placeholder} ${a.ariaLabel} ${a.label ?? ''}`;
    if (OTP_HINT.test(text) && !OTP_EXCLUDE.test(text) && !USERNAME_HINT.test(`${a.name} ${a.id}`)) return 'otp';
    if (a.type === 'number') return null;
    if (a.type === 'email') return 'username';
    if (USERNAME_HINT.test(text) && !NOT_USERNAME_HINT.test(text)) return 'username';
    return null;
  }

  function isVisible(el) {
    if (!el.isConnected) return false;
    const rect = el.getBoundingClientRect();
    if (rect.width < 4 || rect.height < 4) return false;
    const style = getComputedStyle(el);
    return style.visibility !== 'hidden' && style.display !== 'none' && parseFloat(style.opacity || '1') > 0.05;
  }

  function isFillable(el) {
    return el instanceof HTMLInputElement && !el.disabled && !el.readOnly && isVisible(el);
  }

  function container(el) {
    return el.form ?? el.closest('[role="dialog"], [role="form"], section, main, body') ?? document.body;
  }

  /** Belge sırasına göre `before`dan önce gelen, kullanıcı adına benzeyen en yakın alanı bulur. */
  function findUsernameFor(password, inputs) {
    const scope = container(password);
    const candidates = inputs.filter((el) =>
      el !== password && container(el) === scope && el.type !== 'password' && isFillable(el)
      && TEXT_TYPES.has((el.getAttribute('type') || 'text').toLowerCase())
      && (el.compareDocumentPosition(password) & Node.DOCUMENT_POSITION_FOLLOWING));

    const scored = candidates.map((el, index) => {
      const kind = classify(describe(el));
      let score = index;   // yakın olan (sonraki) daha iyi
      if (kind === 'username') score += 1000;
      if (kind === 'otp') score -= 2000;
      if (NOT_USERNAME_HINT.test(`${el.name} ${el.id} ${el.placeholder}`)) score -= 2000;
      return { el, score };
    }).filter((c) => c.score > -1000);

    scored.sort((a, b) => b.score - a.score);
    return scored[0]?.el ?? null;
  }

  /**
   * Sayfadaki giriş alanı gruplarını döndürür.
   * @returns {{username: HTMLInputElement|null, password: HTMLInputElement|null, newPassword: boolean}[]}
   */
  function findLoginGroups(root = document) {
    const inputs = Array.from(root.querySelectorAll('input'));
    const passwords = inputs.filter((el) => el.type === 'password' && isFillable(el));
    const groups = [];
    const used = new Set();

    for (const password of passwords) {
      const kind = classify(describe(password));
      // Kayıt/parola değiştirme formunda ikinci "yeni parola (tekrar)" alanını ayrı grup sayma.
      if (groups.some((g) => g.password && container(g.password) === container(password) && kind === 'new-password' && g.newPassword)) continue;
      const username = findUsernameFor(password, inputs);
      if (username) used.add(username);
      groups.push({ username, password, newPassword: kind === 'new-password' });
    }

    // Çok adımlı girişlerin ilk adımı: yalnızca kullanıcı adı / e-posta alanı.
    if (groups.length === 0) {
      for (const el of inputs) {
        if (used.has(el) || !isFillable(el)) continue;
        if (classify(describe(el)) === 'username') {
          groups.push({ username: el, password: null, newPassword: false });
          break;
        }
      }
    }
    return groups;
  }

  /** React/Vue/Angular gibi çatıların değişikliği algılaması için yerel setter + input/change olayları. */
  function setValue(el, value) {
    el.focus({ preventScroll: true });
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  /** Grubu doldurur. Kayıt formlarında (new-password) parola alanına dokunmaz. */
  function fillGroup(group, credentials) {
    const result = { username: false, password: false };
    if (group.username && credentials.username && isFillable(group.username)) {
      setValue(group.username, credentials.username);
      result.username = true;
    }
    if (group.password && !group.newPassword && credentials.password && isFillable(group.password)) {
      setValue(group.password, credentials.password);
      result.password = true;
    }
    if (result.username || result.password) {
      (group.password && result.password ? group.password : group.username)?.blur();
    }
    return result;
  }

  // ---------------------------------------------------------------------------
  // Doğrulama kodu (OTP) alanları
  // ---------------------------------------------------------------------------

  /** Görünmez ama kullanılan alanlar: birçok OTP bileşeni kutuları çizip asıl input'u şeffaf tutar. */
  function isPresent(el) {
    if (!el.isConnected || el.disabled || el.readOnly) return false;
    const rect = el.getBoundingClientRect();
    if (rect.width < 1 || rect.height < 1) return false;
    const style = getComputedStyle(el);
    return style.visibility !== 'hidden' && style.display !== 'none';
  }

  function inputType(el) {
    return (el.getAttribute('type') || 'text').toLowerCase();
  }

  /** Tek karakterlik kutu (çok kutulu OTP formlarının parçası) mı? */
  function isSegmentBox(el) {
    if (!OTP_TYPES.has(inputType(el)) || !isFillable(el)) return false;
    const maxLength = el.maxLength > 0 ? el.maxLength : null;
    return maxLength !== null && maxLength <= 2 && el.getBoundingClientRect().width <= 90;
  }

  /** İki kutu aynı satırda ve birbirine yakın mı? */
  function adjacent(a, b) {
    const ra = a.getBoundingClientRect();
    const rb = b.getBoundingClientRect();
    return Math.abs(ra.top - rb.top) < ra.height / 2 && rb.left > ra.left && rb.left - ra.right < 64 && a.form === b.form;
  }

  /**
   * Sayfadaki OTP alanlarını döndürür.
   * @returns {{kind: 'single'|'segmented', inputs: HTMLInputElement[]}[]}
   */
  function findOtpGroups(root = document) {
    const inputs = Array.from(root.querySelectorAll('input'));
    const groups = [];

    // Çok kutulu: 4-8 ardışık tek karakterlik kutu
    let run = [];
    const flush = () => {
      if (run.length >= 4 && run.length <= 8) groups.push({ kind: 'segmented', inputs: run });
      run = [];
    };
    for (const el of inputs) {
      if (!isSegmentBox(el)) continue;
      if (run.length && !adjacent(run[run.length - 1], el)) flush();
      run.push(el);
    }
    flush();
    const inSegments = new Set(groups.flatMap((g) => g.inputs));

    // Tek alan (şeffaf input üzerine çizilmiş kutular dahil)
    for (const el of inputs) {
      if (inSegments.has(el) || !OTP_TYPES.has(inputType(el)) || !isPresent(el)) continue;
      if (classify(describe(el)) === 'otp') groups.push({ kind: 'single', inputs: [el] });
    }
    return groups;
  }

  /** Değeri yazar ve input/change olaylarını gönderir; odağa dokunmaz. */
  function writeValue(el, value) {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  /**
   * Kodu doldurur. Çok kutulu formlarda önce yapıştırma olayı denenir (çoğu bileşen kodu kutulara
   * kendisi dağıtır); işe yaramazsa kutular sırayla doldurulur.
   */
  function fillOtp(group, code) {
    if (group.kind === 'single') {
      setValue(group.inputs[0], code);
      // Bazı siteler kodu "123 456" biçiminde gösterir; yalnızca rakamları karşılaştır.
      return group.inputs[0].value.replace(/\D/g, '') === code;
    }

    const boxes = group.inputs;
    if (boxes.length !== code.length) return false;
    const filled = () => boxes.every((box, i) => box.value === code[i]);

    try {
      const data = new DataTransfer();
      data.setData('text/plain', code);
      boxes[0].focus({ preventScroll: true });
      boxes[0].dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
    } catch {
      // DataTransfer desteklenmiyorsa doğrudan kutu kutu doldur.
    }
    if (filled()) return true;

    // Kutuları yalnızca ileri yönde doldur. Çoğu bileşen bir rakam girilince odağı kendisi sonraki
    // kutuya taşır; odağı geri almak veya yapay tuş olayı göndermek bileşenle çakışıp imlecin kutular
    // arasında ileri geri gezinmesine (ve hanelerin kaymasına) yol açar.
    boxes.forEach((box, i) => {
      if (box.value === code[i]) return;   // bileşen zaten doğru yazdıysa dokunma
      if (document.activeElement !== box) box.focus({ preventScroll: true });
      writeValue(box, code[i]);
    });
    return filled();
  }

  /** Odaktaki alanın grubunu, yoksa ilk giriş grubunu seçer. */
  function bestGroup(groups, focused = document.activeElement) {
    return groups.find((g) => g.username === focused || g.password === focused)
      ?? groups.find((g) => g.password && !g.newPassword)
      ?? groups[0]
      ?? null;
  }

  return { classify, describe, isFillable, findLoginGroups, fillGroup, bestGroup, setValue, findOtpGroups, fillOtp };
})();
