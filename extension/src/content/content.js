// İçerik betiği: formları izler, simgeleri ekler ve arka plan betiğiyle konuşarak doldurur.
(() => {
  'use strict';

  if (globalThis.__localVaultContent) return;
  globalThis.__localVaultContent = true;

  const ext = globalThis.browser ?? globalThis.chrome;

  const ERROR_MESSAGES = {
    locked: 'Kasa kilitli. LocalVault penceresinde kilidi açıp tekrar deneyin.',
    not_paired: 'Eklenti henüz LocalVault\'a bağlı değil. Araç çubuğundaki LocalVault simgesinden bağlanın.',
    unauthorized: 'Eklentinin bağlantısı kaldırılmış. Araç çubuğundaki LocalVault simgesinden yeniden bağlanın.',
    app_not_running: 'LocalVault çalışmıyor. Uygulamayı başlatıp tekrar deneyin.',
    host_missing: 'LocalVault masaüstü uygulaması bu tarayıcıya kayıtlı değil.',
    not_found: 'Bu sayfa için böyle bir kayıt yok.',
    timeout: 'LocalVault yanıt vermedi.',
  };

  let settings = { showIcons: true, autofillOnLoad: false, continueFlow: true, offerSave: true };
  let groups = [];
  let otpGroups = [];
  let autofillDone = false;
  // Akış devamı (parola/kod adımı) her sayfa yüklemesinde en fazla bir kez denenir.
  const continued = { password: false, otp: false };

  // Chrome MV3 ve Firefox'ta sendMessage promise döndürür (Firefox geri çağırma kabul etmez).
  async function request(message) {
    try {
      const response = await ext.runtime.sendMessage(message);
      return response ?? { ok: false, error: { code: 'internal', message: 'Eklentiyle iletişim kurulamadı.' } };
    } catch {
      // Eklenti güncellendi/kaldırıldı; bu sayfadaki eski betik artık çalışamaz.
      return { ok: false, error: { code: 'internal', message: 'Eklenti güncellendi; sayfayı yenileyin.' } };
    }
  }

  function errorText(error) {
    return ERROR_MESSAGES[error?.code] ?? error?.message ?? 'LocalVault kullanılamıyor.';
  }

  const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

  // ---------------------------------------------------------------------------
  // Kullanıcı adı / parola
  // ---------------------------------------------------------------------------

  function groupOf(input) {
    return groups.find((g) => g.username === input || g.password === input) ?? LVForms.bestGroup(groups);
  }

  /** Kimlik bilgisini alıp doldurur ve sonucu arka plana bildirir (çok adımlı akış takibi için). */
  async function fillEntry(entryId, group) {
    const response = await request({ type: 'lv:getCredentials', entryId });
    if (!response.ok) {
      LVUi.toast(errorText(response.error));
      return false;
    }
    const result = LVForms.fillGroup(group, response.data);
    await request({ type: 'lv:filled', entryId, ...result });
    if (!result.username && !result.password) {
      LVUi.toast('Doldurulacak uygun alan bulunamadı.');
      return false;
    }
    // Parola ile kod aynı sayfadaysa kodu da tamamla.
    if (result.password && otpGroups.length) continueFlow('otp');
    return true;
  }

  async function onLoginIconClick(input) {
    const response = await request({ type: 'lv:getLogins', interactive: true });
    if (!response.ok) {
      LVUi.toast(errorText(response.error));
      return;
    }
    const logins = response.data.logins;
    const group = groupOf(input);
    if (logins.length === 0) LVUi.toast('Bu site için kayıtlı hesap yok.');
    else if (logins.length === 1) await fillEntry(logins[0].id, group);
    else LVUi.showMenu(input, logins, (login) => fillEntry(login.id, group));
  }

  // ---------------------------------------------------------------------------
  // Doğrulama kodu (OTP)
  // ---------------------------------------------------------------------------

  function otpGroupOf(input) {
    return otpGroups.find((g) => g.inputs.includes(input)) ?? otpGroups[0] ?? null;
  }

  function fillCode(group, code) {
    if (!group) {
      LVUi.toast('Bu sayfada doğrulama kodu alanı bulunamadı.');
      return false;
    }
    if (LVForms.fillOtp(group, code)) return true;
    LVUi.toast(group.kind === 'segmented' && group.inputs.length !== code.length
      ? `Sayfa ${group.inputs.length} haneli kod bekliyor, kayıttaki kod ${code.length} haneli.`
      : 'Doğrulama kodu alana yazılamadı.');
    return false;
  }

  /** Kodu alır; süresi dolmak üzereyse (≤2 sn) bir sonrakini bekler, böylece site eski kodu reddetmez. */
  async function fetchCode(entryId) {
    let response = await request({ type: 'lv:getTotp', entryId });
    if (response.ok && response.data.remaining <= 2) {
      await sleep(response.data.remaining * 1000 + 300);
      response = await request({ type: 'lv:getTotp', entryId });
    }
    return response;
  }

  async function fillOtpEntry(entryId, group) {
    const response = await fetchCode(entryId);
    if (!response.ok) {
      LVUi.toast(errorText(response.error));
      return false;
    }
    return fillCode(group, response.data.code);
  }

  async function onOtpIconClick(input) {
    const response = await request({ type: 'lv:getLogins', interactive: true });
    if (!response.ok) {
      LVUi.toast(errorText(response.error));
      return;
    }
    const group = otpGroupOf(input);
    const logins = response.data.logins.filter((l) => l.hasTotp);
    // Bu sekmede az önce giriş yapılan hesap öncelikli.
    const preferred = logins.find((l) => l.id === response.data.flowEntryId);
    if (preferred) await fillOtpEntry(preferred.id, group);
    else if (logins.length === 0) LVUi.toast('Bu site için kayıtlı doğrulama kodu (TOTP) yok.');
    else if (logins.length === 1) await fillOtpEntry(logins[0].id, group);
    else LVUi.showMenu(input, logins, (login) => fillOtpEntry(login.id, group), 'LOCALVAULT · DOĞRULAMA KODU');
  }

  // ---------------------------------------------------------------------------
  // Çok adımlı akış: bu sekmede kullanıcı az önce LocalVault ile giriş başlattıysa sonraki
  // adımı (parola sayfası, doğrulama kodu sayfası) aynı hesapla tamamla. Arka plan yalnızca
  // kullanıcı eylemiyle başlamış, birkaç dakikalık bir akış için yanıt verir.
  // ---------------------------------------------------------------------------

  async function continueFlow(need) {
    if (!settings.continueFlow || continued[need]) return;
    continued[need] = true;

    const response = await request({ type: 'lv:continue', need });
    if (!response.ok) return;   // akış yok/süresi dolmuş: sessizce geç

    if (need === 'password') {
      const group = groups.find((g) => g.password && !g.newPassword);
      if (!group) return;
      const result = LVForms.fillGroup(
        { ...group, username: group.username?.value ? null : group.username }, response.data);
      await request({ type: 'lv:filled', entryId: response.entryId, ...result });
    } else if (fillCode(otpGroups[0], response.data.code)) {
      LVUi.toast('LocalVault doğrulama kodunu doldurdu.', 3000);
    }
  }

  // ---------------------------------------------------------------------------
  // Yeni hesabı kaydetme önerisi
  // Kullanıcı bir giriş formunu gönderdiğinde (form gönderme, giriş düğmesi, Enter) değerler okunur
  // ve arka plana iletilir. Arka plan kasaya sorar; hesap yoksa veya parola değişmişse sekmenin ana
  // çerçevesinde öneri gösterilir.
  // Not: sayfa betiği form.requestSubmit() çağırırsa tarayıcı da "güvenilir" bir submit olayı üretir;
  // yani bir sayfa kendi alanlarındaki değerlerle öneri kartını açtırabilir. Bu yalnızca o sitenin
  // origin'i için bir öneridir; kayıt her durumda kullanıcının karttaki gerçek tıklamasıyla yapılır.
  // ---------------------------------------------------------------------------

  const SUBMIT_HINT = /giriş|oturum|login|log in|sign in|signin|devam|ileri|continue|next|submit|gönder|kaydol|kayıt|sign up|register|üye ol|onayla|tamam|ok\b/i;
  let lastSubmission = 0;

  /** Gönderilen formun kullanıcı adı / parolasını okur (kayıt ve parola değiştirme formları dahil). */
  function readSubmission(scope) {
    const all = LVForms.findLoginGroups();
    const relevant = scope ? all.filter((g) => [g.username, g.password].some((el) => el && scope.contains(el))) : all;
    const candidates = relevant.length ? relevant : all;

    // Parola değiştirme formunda yeni parola esas alınır.
    const withNew = candidates.find((g) => g.newPassword && g.password?.value);
    const withPassword = withNew ?? candidates.find((g) => g.password?.value);
    if (withPassword) {
      const username = candidates.map((g) => g.username?.value?.trim()).find(Boolean) ?? '';
      return { username, password: withPassword.password.value };
    }
    const usernameOnly = candidates.find((g) => g.username?.value && !g.password);
    return usernameOnly ? { username: usernameOnly.username.value.trim(), password: '' } : null;
  }

  async function onSubmission(scope) {
    if (!settings.offerSave || Date.now() - lastSubmission < 1500) return;
    const submission = readSubmission(scope);
    if (!submission) return;
    lastSubmission = Date.now();

    if (!submission.password) {
      // Çok adımlı girişin ilk adımı: kullanıcı adını sonraki adım için hatırlat.
      await request({ type: 'lv:usernameStep', username: submission.username });
      return;
    }
    await request({ type: 'lv:loginSubmitted', ...submission });
  }

  function formScope(element) {
    return element?.closest?.('form') ?? element?.closest?.('[role="dialog"], [role="form"], section, main') ?? null;
  }

  document.addEventListener('submit', (e) => {
    if (e.isTrusted) onSubmission(e.target);
  }, true);

  document.addEventListener('click', (e) => {
    if (!e.isTrusted) return;
    const button = e.target?.closest?.('button, input[type="submit"], input[type="button"], [role="button"]');
    if (!button || button.closest('localvault-ui')) return;
    const type = (button.getAttribute('type') || '').toLowerCase();
    const label = `${button.textContent || ''} ${button.value || ''} ${button.getAttribute('aria-label') || ''}`;
    if (type === 'submit' || SUBMIT_HINT.test(label)) onSubmission(formScope(button));
  }, true);

  document.addEventListener('keydown', (e) => {
    if (!e.isTrusted || e.key !== 'Enter' || !(e.target instanceof HTMLInputElement)) return;
    if (groups.some((g) => g.username === e.target || g.password === e.target)) onSubmission(formScope(e.target));
  }, true);

  function showSaveOffer(offer) {
    if (window !== window.top || !offer) return;
    LVUi.showSavePrompt(offer, async (action) => {
      const result = await request({ type: 'lv:resolveSave', action });
      if (action === 'save') {
        LVUi.toast(result.ok
          ? (offer.status === 'changed' ? 'Parola LocalVault\'ta güncellendi.' : 'Hesap LocalVault\'a kaydedildi.')
          : errorText(result.error));
      } else if (action === 'never') {
        LVUi.toast('Bu site için bir daha sorulmayacak.');
      }
    });
  }

  // ---------------------------------------------------------------------------
  // Tarama
  // ---------------------------------------------------------------------------

  function scan() {
    groups = LVForms.findLoginGroups();
    otpGroups = LVForms.findOtpGroups();

    if (settings.showIcons) {
      for (const group of groups) {
        for (const input of [group.username, group.password]) {
          if (input && !(input === group.password && group.newPassword)) LVUi.addIcon(input, onLoginIconClick);
        }
      }
      for (const group of otpGroups) {
        const title = 'LocalVault ile doğrulama kodunu doldur';
        if (group.kind === 'segmented') {
          LVUi.addIcon(group.inputs[group.inputs.length - 1], onOtpIconClick, { placement: 'after', title });
        } else {
          LVUi.addIcon(group.inputs[0], onOtpIconClick, { title });
        }
      }
      LVUi.reposition();
    }

    if (groups.some((g) => g.password && !g.newPassword && !g.password.value)) continueFlow('password');
    if (otpGroups.length) continueFlow('otp');
    if (settings.autofillOnLoad && !autofillDone) tryAutofill();
  }

  /**
   * İsteğe bağlı: sayfa açılınca tek eşleşen hesabı doldurur. Yalnızca ana çerçevede, HTTPS'te ve
   * görünür bir parola alanı varken çalışır; kasa kilitliyse pencere açılmaz (interactive: false).
   */
  async function tryAutofill() {
    if (window !== window.top || location.protocol !== 'https:') return;
    const group = groups.find((g) => g.password && !g.newPassword);
    if (!group) return;
    autofillDone = true;

    const response = await request({ type: 'lv:getLogins', interactive: false });
    if (response.ok && response.data.logins.length === 1) await fillEntry(response.data.logins[0].id, group);
  }

  // Dinamik sayfalar (SPA'lar, açılır giriş pencereleri) için DOM değişikliklerini izle.
  let scanTimer = null;
  const observer = new MutationObserver(() => {
    clearTimeout(scanTimer);
    scanTimer = setTimeout(scan, 300);
  });

  // ---------------------------------------------------------------------------
  // Arka plandan gelen mesajlar (açılır pencere, klavye kısayolu)
  // ---------------------------------------------------------------------------

  ext.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    switch (message?.type) {
      case 'lv:pageInfo': {
        groups = LVForms.findLoginGroups();
        otpGroups = LVForms.findOtpGroups();
        sendResponse({
          hasPassword: groups.some((g) => g.password && !g.newPassword),
          hasUsername: groups.some((g) => g.username),
          hasOtp: otpGroups.length > 0,
        });
        return false;
      }
      case 'lv:fill': {
        // Arka plan bu kimlik bilgisini belirli bir origin için aldı; sayfa bu arada başka yere gittiyse doldurma.
        if (message.origin !== location.origin) {
          sendResponse({ filled: false });
          return false;
        }
        groups = LVForms.findLoginGroups();
        const group = LVForms.bestGroup(groups);
        const result = group ? LVForms.fillGroup(group, message) : { username: false, password: false };
        sendResponse({ filled: result.username || result.password, ...result });
        return false;
      }
      case 'lv:fillOtp': {
        if (message.origin !== location.origin) {
          sendResponse({ filled: false });
          return false;
        }
        otpGroups = LVForms.findOtpGroups();
        sendResponse({ filled: fillCode(otpGroups[0], message.code) });
        return false;
      }
      case 'lv:showMenu': {
        groups = LVForms.findLoginGroups();
        otpGroups = LVForms.findOtpGroups();
        if (message.otp && otpGroups.length) {
          const group = otpGroups[0];
          LVUi.showMenu(group.inputs[0], message.logins, (login) => fillOtpEntry(login.id, group), 'LOCALVAULT · DOĞRULAMA KODU');
          return false;
        }
        const group = LVForms.bestGroup(groups);
        const anchor = group?.username ?? group?.password;
        if (anchor) LVUi.showMenu(anchor, message.logins, (login) => fillEntry(login.id, group));
        else LVUi.toast('Bu sayfada giriş alanı bulunamadı.');
        return false;
      }
      case 'lv:toast':
        LVUi.toast(message.message);
        return false;
      case 'lv:offerSave':
        showSaveOffer(message.offer);
        return false;
      default:
        return false;
    }
  });

  async function init() {
    try {
      const stored = await ext.storage.local.get('settings');
      settings = { ...settings, ...(stored?.settings ?? {}) };
    } catch {
      // Varsayılan ayarlarla devam et.
    }
    scan();
    // Giriş sonrası açılan sayfada, önceki sayfada yakalanan gönderim için öneri bekliyor olabilir.
    if (window === window.top && settings.offerSave) {
      const pending = await request({ type: 'lv:pendingSave' });
      if (pending.ok && pending.data) showSaveOffer(pending.data);
    }
    observer.observe(document.documentElement, {
      childList: true, subtree: true, attributes: true, attributeFilter: ['type', 'style', 'class', 'hidden'],
    });
  }

  init();
})();
