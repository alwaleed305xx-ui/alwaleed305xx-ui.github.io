/* Sora Fashion — checkout */
Sora.ready(() => {
  'use strict';
  const S = Sora, { $, $$, esc, I, moneyText, LS, KEYS } = S;
  const cfg = S.config;
  const CITIES = ['الرياض', 'جدة', 'مكة المكرمة', 'المدينة المنورة', 'الدمام', 'الخبر', 'الظهران', 'الأحساء', 'القطيف', 'الجبيل',
    'الطائف', 'تبوك', 'بريدة', 'عنيزة', 'الرس', 'حائل', 'أبها', 'خميس مشيط', 'بيشة', 'جازان', 'نجران', 'ينبع', 'الباحة',
    'سكاكا', 'عرعر', 'القريات', 'حفر الباطن', 'الخرج', 'المجمعة', 'القنفذة', 'أخرى'];
  const ERR = {
    name_invalid: 'اكتبي الاسم الكامل (3 أحرف على الأقل).',
    phone_invalid: 'رقم الجوال لازم يكون سعودي مثل 05XXXXXXXX.',
    city_invalid: 'اختاري المدينة.', district_invalid: 'اكتبي اسم الحي.', address_invalid: 'اكتبي العنوان بالتفصيل.',
    national_address_invalid: 'العنوان الوطني المختصر 4 حروف و4 أرقام، مثل RAAA1234.',
    too_many_orders: 'وصلنا طلبات كثيرة من نفس الرقم — جربي بعد ساعة أو تواصلي معنا.',
    product_unavailable: 'قطعة في سلتك ما عادت متوفرة — حدّثي السلة.', color_invalid: 'لون قطعة في سلتك ما عاد متوفر — حدّثي السلة.',
    size_invalid: 'مقاس قطعة في سلتك ما عاد متوفر — حدّثي السلة.', qty_invalid: 'الكمية لازم تكون من 1 إلى 10.',
    items_invalid: 'سلتك فاضية.', coupon_invalid: 'كود الخصم غير صحيح.', coupon_min_not_met: 'كود الخصم ما يشمل هالمبلغ.',
    online_payment_disabled: 'الدفع الإلكتروني غير متاح حالياً.', cod_disabled: 'الدفع عند الاستلام غير متاح حالياً.',
    busy_try_again: 'الضغط عالي حالياً — حاولي بعد دقيقة.', backend_not_configured: 'المتجر يستقبل الطلبات قريباً.',
    amount_too_small: 'مبلغ الطلب صغير جداً.', too_many_items: 'الحد 20 قطعة مختلفة في الطلب الواحد.',
    coupon_first_order_only: 'هذا الكود لأول طلب فقط — وعندك طلب سابق بهالرقم.',
    captcha_required: 'أكملي خطوة التحقق (أنا لست روبوت) ثم أعيدي المحاولة.', captcha_failed: 'ما نجح التحقق — حدّثي الصفحة وحاولي مرة ثانية.',
  };
  const errMsg = code => ERR[code] || 'صار خطأ بالاتصال — حاولي مرة ثانية.';
  const showEmpty = () => {
    $('#checkout').innerHTML = `<div class="center-card" style="grid-column:1/-1"><div class="ico">${I.bag}</div><h1>سلّتك فاضية</h1><p>أضيفي القطع اللي تعجبك وارجعي هنا.</p><a class="btn btn-dark" href="index.html#catalog" style="margin-top:1.4rem">تسوّقي الحين</a></div>`;
  };

  /* drop lines whose colour/size/product no longer exists, and say so */
  const stale = S.cartLines(true).filter(l => l.unavailable);
  if (stale.length) {
    stale.forEach(l => S.setQty(l.key, 0));
    S.toast('شلنا من سلتك قطع ما عادت متوفرة بنفس اللون أو المقاس');
  }
  let lines = S.cartLines();
  if (!lines.length) { showEmpty(); return; }

  const mockMode = S.isLocalHost && cfg.moyasarPublishableKey === 'pk_mock';
  const canOnline = !!cfg.moyasarPublishableKey;
  const canCod = !!cfg.codEnabled;
  const backend = !!S.endpoint();
  let payment = canOnline ? 'online' : (canCod ? 'cod' : '');
  let coupon = LS.get(KEYS.coupon, '') || '';
  let orderLocked = false;

  /* payment options */
  $('#payOptions').innerHTML = (!backend || (!canOnline && !canCod))
    ? '<div class="closed-note">المتجر يجهّز لاستقبال الطلبات — جربي بعد قليل أو تواصلي معنا.</div>'
    : [
      canOnline && `<label class="pay-opt"><input type="radio" name="pay" value="online" ${payment === 'online' ? 'checked' : ''}><div><b>بطاقة مدى / ائتمانية أو Apple Pay</b><small>دفع فوري وآمن عبر بوابة ميسّر المرخّصة</small></div><span class="pay-logos"><span>مدى</span><span>Apple Pay</span><span>VISA</span><span>MC</span><span>STC Pay</span></span></label>`,
      canCod && `<label class="pay-opt"><input type="radio" name="pay" value="cod" ${payment === 'cod' ? 'checked' : ''}><div><b>الدفع عند الاستلام</b><small>رسوم إضافية ${esc(S.fmt(cfg.codFee))} ر.س</small></div></label>`,
    ].filter(Boolean).join('');
  if (!payment || !backend) $('#placeBtn').disabled = true;
  $('#secureLine').innerHTML = `${I.lock} بياناتك محمية — ما نحفظ بيانات البطاقة`;
  $('#agreeDays').textContent = cfg.shippingDays;
  $('#payOptions').addEventListener('change', e => { if (e.target.name === 'pay') { payment = e.target.value; renderSummary(); } });

  /* cities */
  $('#fCity').innerHTML = '<option value="">اختاري المدينة</option>' + CITIES.map(c => `<option>${c}</option>`).join('');
  $('#fCity').addEventListener('change', e => { $('#cityOtherWrap').hidden = e.target.value !== 'أخرى'; });

  /* restore details typed earlier in this session */
  const saved = (() => { try { return JSON.parse(sessionStorage.getItem('sora-co') || '{}'); } catch (e) { return {}; } })();
  ['name', 'phone', 'district', 'address', 'nationalAddress', 'notes'].forEach(k => { const el = $(`#coForm [name="${k}"]`); if (el && saved[k]) el.value = saved[k]; });
  if (saved.city) { $('#fCity').value = CITIES.includes(saved.city) ? saved.city : 'أخرى'; if ($('#fCity').value === 'أخرى') { $('#cityOtherWrap').hidden = false; $('#fCityOther').value = saved.city === 'أخرى' ? '' : saved.city; } }

  /* summary */
  function renderSummary() {
    const t = S.totals(lines, { coupon, payment });
    if (coupon && t.couponError) { /* keep the code visible with its error */ }
    $('#summary').innerHTML = `
      <h2 style="font-size:1.1rem;margin-bottom:1rem">ملخص الطلب</h2>
      <div style="display:flex;flex-direction:column;gap:.9rem">${lines.map(l => `
        <div class="line-item">
          <span class="li-thumb">${S.media(l.p, l.color.name)}<span class="qbadge">${l.qty}</span></span>
          <div><div class="li-name">${esc(l.p.name)}</div><div class="li-meta">${esc(l.color.name)} · ${esc(l.size)}</div></div>
          <div class="li-price">${moneyText(l.line)}</div>
        </div>`).join('')}</div>
      <div class="coupon-row"><input class="input" id="couponIn" placeholder="كود الخصم" value="${esc(coupon)}" ${orderLocked ? 'disabled' : ''} dir="ltr" maxlength="24"><button class="btn btn-ghost" type="button" id="couponBtn" ${orderLocked ? 'disabled' : ''}>${coupon && !t.couponError ? 'إزالة' : 'تطبيق'}</button></div>
      <div class="coupon-msg ${t.coupon ? 'ok' : (t.couponError ? 'bad' : '')}">${t.coupon ? `✓ تم تطبيق ${esc(t.coupon.code)}` : esc(t.couponError)}</div>
      <div class="sum-row"><span>المجموع</span><span>${moneyText(t.subtotal)}</span></div>
      ${t.discount ? `<div class="sum-row" style="color:var(--ok)"><span>الخصم</span><span>− ${moneyText(t.discount)}</span></div>` : ''}
      <div class="sum-row"><span>الشحن</span><span>${t.shipping ? moneyText(t.shipping) : 'مجاني'}</span></div>
      ${t.codFee ? `<div class="sum-row"><span>رسوم الدفع عند الاستلام</span><span>${moneyText(t.codFee)}</span></div>` : ''}
      <div class="sum-row total"><span>الإجمالي</span><span>${moneyText(t.total)}</span></div>
      <p class="drawer-note">يوصلك خلال ${esc(cfg.shippingDays)} يوم</p>`;
    const btn = $('#couponBtn');
    if (btn) btn.addEventListener('click', () => {
      if (coupon && !t.couponError) { coupon = ''; LS.del(KEYS.coupon); }
      else { coupon = $('#couponIn').value.trim().toUpperCase(); if (S.findCoupon(coupon)) LS.set(KEYS.coupon, coupon); }
      renderSummary();
    });
    return t;
  }
  renderSummary();
  /* the cart can change in the drawer on this page or in another tab */
  document.addEventListener('sora:cart', () => {
    if (orderLocked) return;
    lines = S.cartLines();
    if (!lines.length) showEmpty(); else renderSummary();
  });

  /* optional anti-bot check (Cloudflare Turnstile) when a site key is configured */
  let captchaToken = '', captchaWidget = null;
  if (cfg.turnstileSiteKey && backend) {
    $('#placeBtn').insertAdjacentHTML('beforebegin', '<div id="tsBox" style="margin-top:1rem"></div>');
    window.soraTs = () => {
      captchaWidget = window.turnstile.render('#tsBox', {
        sitekey: cfg.turnstileSiteKey, language: 'ar',
        callback: tok => { captchaToken = tok; }, 'expired-callback': () => { captchaToken = ''; },
      });
    };
    const ts = document.createElement('script');
    ts.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?onload=soraTs&render=explicit';
    ts.async = true;
    document.head.appendChild(ts);
  }

  /* validation (mirrors backend rules) */
  const toAscii = s => String(s || '').replace(/[٠-٩]/g, d => '٠١٢٣٤٥٦٧٨٩'.indexOf(d)).replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d));
  const normPhone = p => {
    let d = toAscii(p).replace(/\D/g, '');
    if (d.startsWith('00966')) d = d.slice(5); else if (d.startsWith('966')) d = d.slice(3);
    if (d.length === 9 && d[0] === '5') d = '0' + d;
    return /^05\d{8}$/.test(d) ? d : '';
  };
  function collect() {
    const v = n => ($(`#coForm [name="${n}"]`).value || '').trim();
    const city = $('#fCity').value === 'أخرى' ? $('#fCityOther').value.trim() : $('#fCity').value;
    return { name: v('name'), phone: normPhone(v('phone')), rawPhone: v('phone'), city, district: v('district'), address: v('address'), nationalAddress: toAscii(v('nationalAddress')).toUpperCase(), notes: v('notes') };
  }
  function validate(c) {
    const errs = {};
    if (c.name.length < 3) errs.name = ERR.name_invalid;
    if (!c.phone) errs.phone = ERR.phone_invalid;
    if (c.city.length < 2) errs.city = ERR.city_invalid;
    if (c.district.length < 2) errs.district = ERR.district_invalid;
    if (c.address.length < 5) errs.address = ERR.address_invalid;
    if (c.nationalAddress && !/^[A-Z]{4}\d{4}$/.test(c.nationalAddress)) errs.nationalAddress = ERR.national_address_invalid;
    if (!$('#fAgree').checked) errs.agree = 'لازم توافقين على السياسات قبل الطلب.';
    $$('[data-err]').forEach(el => { el.textContent = errs[el.dataset.err] || ''; });
    $$('#coForm .input, #coForm .select').forEach(el => el.classList.toggle('invalid', !!errs[el.name]));
    const first = Object.keys(errs)[0];
    if (first) { const el = $(`#coForm [name="${first}"]`) || $('#fAgree'); el.focus(); }
    return !first;
  }
  const showError = msg => { const b = $('#formError'); b.textContent = msg; b.hidden = !msg; };

  $('#coForm').addEventListener('submit', async e => {
    e.preventDefault();
    if (orderLocked) return;
    showError('');
    lines = S.cartLines();
    if (!lines.length) { showEmpty(); return; }
    const c = collect();
    if (!validate(c)) return;
    if (cfg.turnstileSiteKey && !captchaToken) { showError(ERR.captcha_required); return; }
    try { sessionStorage.setItem('sora-co', JSON.stringify({ ...c, phone: c.rawPhone })); } catch (err) { /* ignore */ }
    const t = renderSummary();
    if (coupon && t.couponError) { showError(t.couponError); return; }
    const btn = $('#placeBtn');
    btn.disabled = true;
    btn.textContent = 'جاري تأكيد الطلب…';
    let res;
    try {
      res = await S.api('createOrder', {
        customer: { name: c.name, phone: c.phone, city: c.city, district: c.district, address: c.address, nationalAddress: c.nationalAddress, notes: c.notes },
        items: lines.map(l => ({ id: l.id, color: l.color.name, size: l.size, qty: l.qty })),
        coupon: t.coupon ? t.coupon.code : '', payment, website: $('#fWebsite').value, captcha: captchaToken,
      });
    } catch (err) {
      res = { ok: false, error: err.message };
    }
    if (!res || !res.ok) {
      btn.disabled = false;
      btn.textContent = 'إتمام الطلب';
      showError(errMsg(res && res.error));
      if (captchaWidget != null && window.turnstile) { window.turnstile.reset(captchaWidget); captchaToken = ''; }
      return;
    }
    if (Math.abs(res.total - t.total) > 0.01) {
      /* server price differs from cached page data — tell the customer before charging */
      S.toast('تم تحديث السعر حسب آخر تحديث للمتجر');
    }
    const pending = { orderId: res.orderId, total: res.total, payment: res.payment, at: Date.now() };
    try { sessionStorage.setItem('sora-pending', JSON.stringify(pending)); localStorage.setItem('sora-pending', JSON.stringify(pending)); } catch (err) { /* ignore */ }

    if (res.payment === 'cod') {
      S.clearCart();
      location.href = `thanks.html?order=${encodeURIComponent(res.orderId)}&m=cod`;
      return;
    }
    orderLocked = true;
    $$('#coForm input, #coForm select').forEach(el => { el.disabled = true; });
    btn.hidden = true;
    renderSummary();
    $('#payStep').hidden = false;
    $('#payStep').insertAdjacentHTML('afterbegin', `<p class="note" style="margin-top:1rem">رقم طلبك <b dir="ltr">${esc(res.orderId)}</b> — أكملي الدفع (${moneyText(res.total)}) لتأكيده.</p>`);
    startPayment(res);
  });

  function startPayment(res) {
    const callback = new URL('thanks.html', location.href).href;
    if (mockMode) {
      $('#mockPay').hidden = false;
      $('#mockPay').innerHTML = `<div class="mock-pay"><b>وضع التجربة (محلي)</b><p style="font-size:.86rem;color:var(--muted);margin:.3rem 0 .8rem">محاكاة صفحة ميسّر — ما فيه أي خصم حقيقي.</p>
        <div style="display:flex;gap:.6rem;flex-wrap:wrap"><button type="button" class="btn btn-plum" data-mock="paid">ادفعي بنجاح (مدى)</button><button type="button" class="btn btn-ghost" data-mock="failed">محاولة فاشلة</button></div></div>`;
      $('#mockPay').addEventListener('click', ev => {
        const b = ev.target.closest('[data-mock]');
        if (!b) return;
        const id = window.SoraEmu.mockPay({ amount: res.amount, currency: 'SAR', metadata: { order_id: res.orderId }, status: b.dataset.mock });
        location.href = `${callback}?id=${encodeURIComponent(id)}&status=${b.dataset.mock}&message=${b.dataset.mock === 'paid' ? 'APPROVED' : 'DECLINED'}`;
      });
      return;
    }
    const css = document.createElement('link');
    css.rel = 'stylesheet'; css.href = 'https://cdn.moyasar.com/mpf/1.15.0/moyasar.css';
    document.head.appendChild(css);
    const js = document.createElement('script');
    js.src = 'https://cdn.moyasar.com/mpf/1.15.0/moyasar.js';
    js.onload = () => {
      window.Moyasar.init({
        element: '.mysr-form',
        amount: res.amount,
        currency: 'SAR',
        description: `Sora Fashion ${res.orderId}`,
        publishable_api_key: cfg.moyasarPublishableKey,
        callback_url: callback,
        language: 'ar',
        methods: ['creditcard', 'applepay', 'stcpay'],
        supported_networks: ['mada', 'visa', 'mastercard'],
        apple_pay: { country: 'SA', label: 'Sora Fashion', validate_merchant_url: 'https://api.moyasar.com/v1/applepay/initiate' },
        metadata: { order_id: res.orderId },
        on_completed: async payment => {
          try { sessionStorage.setItem('sora-last-payment', payment.id); localStorage.setItem('sora-last-payment', payment.id); } catch (err) { /* ignore */ }
        },
      });
    };
    js.onerror = () => showError('تعذر تحميل بوابة الدفع — تأكدي من الاتصال وحدّثي الصفحة.');
    document.body.appendChild(js);
  }
});
