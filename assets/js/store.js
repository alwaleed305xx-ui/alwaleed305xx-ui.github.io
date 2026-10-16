/* Sora Fashion — shared storefront runtime (data, cart, header/footer, drawer, api). */
(function () {
  'use strict';

  const LS = {
    get(k, d) { try { const v = localStorage.getItem(k); return v == null ? d : JSON.parse(v); } catch (e) { return d; } },
    set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* storage blocked */ } },
    del(k) { try { localStorage.removeItem(k); } catch (e) { /* ignore */ } },
  };
  const KEYS = { cart: 'sora-cart', favs: 'sora-favs', coupon: 'sora-coupon', draft: 'sora-draft', api: 'sora-api-dev' };
  const isLocalHost = ['localhost', '127.0.0.1', '[::1]'].includes(location.hostname);

  const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const round2 = n => Math.round((Number(n) + Number.EPSILON) * 100) / 100;
  const fmt = n => {
    const v = round2(n);
    return (Number.isInteger(v) ? v.toLocaleString('en') : v.toLocaleString('en', { minimumFractionDigits: 2, maximumFractionDigits: 2 }));
  };
  const money = n => `${fmt(n)} <small>ر.س</small>`;
  const moneyText = n => `${fmt(n)} ر.س`;
  const $ = (sel, root) => (root || document).querySelector(sel);
  const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));

  /* ───── icons ───── */
  const I = {
    search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.6-3.6"/></svg>',
    heart: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"><path d="M12 20.5s-7.5-4.6-9.2-9.4C1.6 7.6 3.9 4.5 7.2 4.5c2 0 3.6 1.1 4.8 2.8 1.2-1.7 2.8-2.8 4.8-2.8 3.3 0 5.6 3.1 4.4 6.6C19.5 15.9 12 20.5 12 20.5Z"/></svg>',
    bag: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"><path d="M5 8h14l-1.2 12.1a1 1 0 0 1-1 .9H7.2a1 1 0 0 1-1-.9L5 8Z"/><path d="M9 8V6.5a3 3 0 0 1 6 0V8" stroke-linecap="round"/></svg>',
    menu: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"><path d="M4 7h16M4 12h16M4 17h10"/></svg>',
    close: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18"/></svg>',
    truck: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"><path d="M2.5 6.5h11v9h-11zM13.5 9.5h4l3 3v3h-7"/><circle cx="6.5" cy="17" r="1.8"/><circle cx="17" cy="17" r="1.8"/></svg>',
    shield: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"><path d="M12 3 4.5 6v5.5c0 4.6 3.2 8.3 7.5 9.5 4.3-1.2 7.5-4.9 7.5-9.5V6L12 3Z"/><path d="m8.8 12 2.2 2.2 4.3-4.4" stroke-linecap="round"/></svg>',
    swap: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M4 8h13l-3-3M20 16H7l3 3"/></svg>',
    chat: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"><path d="M4 5.5h16v10H9l-5 4v-14Z"/><path d="M8 10h8M8 13h5" stroke-linecap="round"/></svg>',
    ruler: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"><path d="M3 15.5 15.5 3 21 8.5 8.5 21 3 15.5Z"/><path d="m7 12 2 2M10 9l2 2M13 6l2 2" stroke-linecap="round"/></svg>',
    lock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/></svg>',
    check: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>',
    x: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M7 7l10 10M17 7 7 17"/></svg>',
    box: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"><path d="M3.5 7.5 12 3l8.5 4.5v9L12 21l-8.5-4.5v-9Z"/><path d="M3.5 7.5 12 12l8.5-4.5M12 12v9"/></svg>',
    insta: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><rect x="3.5" y="3.5" width="17" height="17" rx="5"/><circle cx="12" cy="12" r="4"/><circle cx="17.2" cy="6.8" r="1" fill="currentColor"/></svg>',
    tiktok: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M15.6 3c.4 2.2 1.8 3.7 4 4v3a7.3 7.3 0 0 1-4-1.3v6.1A5.7 5.7 0 1 1 10 9.2v3.1a2.6 2.6 0 1 0 2.5 2.6V3h3.1Z"/></svg>',
    snap: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"><path d="M12 3.5c3 0 5 2.2 5 5.2v2.2l1.8.5c.5.2.5.8 0 1-.8.4-1.7.6-2 1 .5 1.6 1.9 2.8 3.4 3.3-.4.8-1.6 1-2.6 1.2-.2.6-.3 1.2-.8 1.2-.8 0-1.7-.5-3 .1-.8.4-1.2 1.3-2.8 1.3s-2-.9-2.8-1.3c-1.3-.6-2.2-.1-3-.1-.5 0-.6-.6-.8-1.2-1-.2-2.2-.4-2.6-1.2 1.5-.5 2.9-1.7 3.4-3.3-.3-.4-1.2-.6-2-1-.5-.2-.5-.8 0-1L7 10.9V8.7c0-3 2-5.2 5-5.2Z"/></svg>',
  };

  /* ───── data ───── */
  const state = { data: null, usingDraft: false, ready: false, readyFns: [] };

  async function loadData() {
    const draft = LS.get(KEYS.draft, null);
    if (draft && Array.isArray(draft.products) && Array.isArray(draft.cats) && draft.config) {
      state.data = draft;
      state.usingDraft = true;
      return;
    }
    const res = await fetch('products.json', { cache: 'no-cache' });
    if (!res.ok) throw new Error('products.json ' + res.status);
    state.data = await res.json();
    if (isLocalHost) {
      const dev = LS.get('sora-dev-config', null);
      if (dev && typeof dev === 'object') Object.assign(state.data.config, dev);
    }
  }

  const S = {
    I, esc, money, moneyText, fmt, round2, $, $$, LS, KEYS, isLocalHost,
    get config() { return state.data.config; },
    get cats() { return state.data.cats; },
    get all() { return state.data.products; },
    get products() { return state.data.products.filter(p => p.show !== false); },
    get usingDraft() { return state.usingDraft; },
    ready(fn) { if (state.ready) fn(); else state.readyFns.push(fn); },
  };

  S.product = id => S.products.find(p => String(p.id) === String(id));
  S.cat = id => S.cats.find(c => c.id === id);
  S.catName = id => (S.cat(id) || {}).name || '';
  S.productUrl = p => `product.html?id=${encodeURIComponent(p.id)}`;
  S.offPct = p => (p.old && p.old > p.price ? Math.round((1 - p.price / p.old) * 100) : 0);
  S.colorOf = (p, name) => (p.colors || []).find(c => c.name === name) || (p.colors || [])[0] || { name: '', hex: '#1c1b1d' };

  S.imagesFor = (p, colorName) => {
    const per = p.colorImages && colorName && p.colorImages[colorName];
    if (Array.isArray(per) && per.length) return per;
    return Array.isArray(p.images) ? p.images.filter(Boolean) : [];
  };
  S.media = (p, colorName, opts) => {
    opts = opts || {};
    const imgs = S.imagesFor(p, colorName);
    if (imgs.length) {
      return `<img src="${esc(imgs[0])}" alt="${esc(p.name)}" loading="${opts.eager ? 'eager' : 'lazy'}" decoding="async" referrerpolicy="no-referrer">`;
    }
    const c = S.colorOf(p, colorName);
    return window.Garment.svg(p.type, c.hex, { label: p.name, accent: p.accent });
  };

  /* ───── favourites ───── */
  S.favs = () => LS.get(KEYS.favs, []);
  S.isFav = id => S.favs().map(String).includes(String(id));
  S.toggleFav = id => {
    const f = S.favs().map(String);
    const on = f.includes(String(id));
    LS.set(KEYS.favs, on ? f.filter(x => x !== String(id)) : [...f, String(id)]);
    S.updateBadges();
    $$(`[data-fav="${CSS.escape(String(id))}"]`).forEach(b => b.classList.toggle('on', !on));
    S.toast(on ? 'انشالت من المفضلة' : 'انضافت للمفضلة ♡');
    return !on;
  };

  /* ───── cart ───── */
  const lineKey = (id, color, size) => `${id}|${color}|${size}`;
  S.cartRaw = () => LS.get(KEYS.cart, []);
  S.cartLines = () => S.cartRaw().map(l => {
    const p = S.product(l.id);
    if (!p) return null;
    const qty = Math.max(1, Math.min(10, parseInt(l.qty, 10) || 1));
    return { ...l, qty, p, unit: p.price, line: round2(p.price * qty), color: S.colorOf(p, l.color) };
  }).filter(Boolean);
  S.cartCount = () => S.cartLines().reduce((s, l) => s + l.qty, 0);
  S.addToCart = (id, color, size, qty) => {
    const cart = S.cartRaw();
    const key = lineKey(id, color, size);
    const ex = cart.find(l => lineKey(l.id, l.color, l.size) === key);
    if (ex) ex.qty = Math.min(10, (parseInt(ex.qty, 10) || 1) + (qty || 1));
    else cart.push({ id: String(id), color, size, qty: Math.min(10, qty || 1) });
    LS.set(KEYS.cart, cart);
    S.renderCart();
    S.updateBadges();
  };
  S.setQty = (key, qty) => {
    let cart = S.cartRaw();
    if (qty <= 0) cart = cart.filter(l => lineKey(l.id, l.color, l.size) !== key);
    else cart.forEach(l => { if (lineKey(l.id, l.color, l.size) === key) l.qty = Math.min(10, qty); });
    LS.set(KEYS.cart, cart);
    S.renderCart();
    S.updateBadges();
    document.dispatchEvent(new CustomEvent('sora:cart'));
  };
  S.clearCart = () => { LS.set(KEYS.cart, []); LS.del(KEYS.coupon); S.updateBadges(); };
  S.lineKey = lineKey;

  /* totals — MUST mirror backend/Code.gs computeTotals() */
  S.findCoupon = code => {
    if (!code) return null;
    const c = (S.config.coupons || []).find(x => String(x.code).toUpperCase() === String(code).trim().toUpperCase() && x.active !== false);
    return c || null;
  };
  S.totals = (lines, opts) => {
    opts = opts || {};
    const cfg = S.config;
    const subtotal = round2(lines.reduce((s, l) => s + l.unit * l.qty, 0));
    let discount = 0, coupon = null, couponError = '';
    if (opts.coupon) {
      const c = S.findCoupon(opts.coupon);
      if (!c) couponError = 'الكود غير صحيح';
      else if (subtotal < (Number(c.minSubtotal) || 0)) couponError = `الكود يشتغل للطلبات من ${fmt(c.minSubtotal)} ر.س`;
      else {
        coupon = c;
        discount = c.type === 'percent' ? round2(subtotal * Number(c.value) / 100) : Math.min(round2(Number(c.value)), subtotal);
      }
    }
    const after = round2(subtotal - discount);
    const shipping = lines.length === 0 ? 0 : (after >= Number(cfg.freeShippingOver || Infinity) ? 0 : round2(Number(cfg.shippingFee) || 0));
    const codFee = opts.payment === 'cod' ? round2(Number(cfg.codFee) || 0) : 0;
    return { subtotal, discount, coupon, couponError, shipping, codFee, total: round2(after + shipping + codFee) };
  };

  /* ───── api ───── */
  S.endpoint = () => {
    if (isLocalHost) { const dev = LS.get(KEYS.api, ''); if (dev) return dev; }
    return (S.config.ordersEndpoint || '').trim();
  };
  S.api = async (action, payload) => {
    const ep = S.endpoint();
    if (!ep) throw new Error('backend_not_configured');
    const body = JSON.stringify({ action, ...(payload || {}) });
    if (ep === 'local') {
      if (!window.SoraEmu) await new Promise((ok, bad) => {
        const s = document.createElement('script');
        s.src = 'dev/emulator.js'; s.onload = ok; s.onerror = () => bad(new Error('emulator_missing'));
        document.head.appendChild(s);
      });
      return window.SoraEmu.call(body);
    }
    const res = await fetch(ep, { method: 'POST', headers: { 'Content-Type': 'text/plain;charset=utf-8' }, body, redirect: 'follow' });
    if (!res.ok) throw new Error('http_' + res.status);
    return res.json();
  };

  /* ───── toast ───── */
  S.toast = msg => {
    let wrap = $('.toasts');
    if (!wrap) { wrap = document.createElement('div'); wrap.className = 'toasts'; wrap.setAttribute('aria-live', 'polite'); document.body.appendChild(wrap); }
    const t = document.createElement('div');
    t.className = 'toast';
    t.textContent = msg;
    wrap.appendChild(t);
    setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 320); }, 2300);
  };

  /* ───── product card (shared) ───── */
  const BADGE = { new: ['new', 'جديد'], sale: ['sale', 'عرض'], trend: ['trend', 'ترند'], pick: ['new', 'مختارات سورا'] };
  S.card = p => {
    const c0 = (p.colors || [])[0] || {};
    const off = S.offPct(p);
    const b = p.badge && BADGE[p.badge] ? `<span class="p-badge ${BADGE[p.badge][0]}">${BADGE[p.badge][1]}</span>` : '';
    const sw = (p.colors || []).slice(0, 5).map(c => `<i style="background:${esc(c.hex)}" title="${esc(c.name)}"></i>`).join('');
    return `<article class="p-card reveal">
      <a class="p-media" href="${S.productUrl(p)}" aria-label="${esc(p.name)}">${b}${S.media(p, c0.name)}</a>
      <button class="p-fav ${S.isFav(p.id) ? 'on' : ''}" data-fav="${esc(p.id)}" aria-label="أضيفي للمفضلة">${I.heart}</button>
      <div class="p-info">
        <a class="p-name" href="${S.productUrl(p)}">${esc(p.name)}</a>
        <div class="p-price"><span class="price">${money(p.price)}</span>${off ? `<span class="price-old">${fmt(p.old)}</span><span class="price-off" dir="ltr">-${off}%</span>` : ''}</div>
        ${sw ? `<div class="swatches-mini">${sw}</div>` : ''}
      </div>
    </article>`;
  };

  /* ───── layout: header / footer / drawer ───── */
  const NAV = [
    ['index.html#new', 'جديدنا'], ['index.html?cat=dresses#catalog', 'فساتين'], ['index.html?cat=abayas#catalog', 'عبايات'],
    ['index.html?cat=sport#catalog', 'رياضي'], ['index.html?cat=lounge#catalog', 'لاونج'], ['index.html?cat=hijab#catalog', 'طرح'],
  ];

  function renderHeader() {
    const el = $('#site-header');
    if (!el) return;
    const ann = (S.config.announcements || []).map((a, i) => `<span class="${i === 0 ? 'on' : ''}">${esc(a)}</span>`).join('');
    el.innerHTML = `
      ${ann ? `<div class="announce" role="status">${ann}</div>` : ''}
      ${state.usingDraft ? '<div class="draft-bar">تشاهدين <b>نسخة مسودة</b> من لوحة التحكم — الزوار يشوفون النسخة المنشورة فقط <button type="button" id="dropDraft">إلغاء المسودة</button></div>' : ''}
      <header class="header" id="header">
        <div class="container header-inner">
          <div class="nav-wrap">
            <button class="icon-btn burger" id="burger" aria-label="القائمة">${I.menu}</button>
            <nav class="nav" id="nav" aria-label="الأقسام">
              <button class="icon-btn nav-close burger" id="navClose" aria-label="إغلاق">${I.close}</button>
              ${NAV.map(([h, t]) => `<a href="${h}">${t}</a>`).join('')}
            </nav>
          </div>
          <a class="logo" href="index.html" aria-label="Sora Fashion"><span class="logo-word">SORA</span><span class="logo-sub">FASHION</span></a>
          <div class="header-actions">
            <button class="icon-btn" id="searchBtn" aria-label="بحث">${I.search}</button>
            <a class="icon-btn" href="index.html?view=fav#catalog" aria-label="المفضلة">${I.heart}<span class="badge-count" id="favCount"></span></a>
            <button class="icon-btn" id="cartBtn" aria-label="السلة">${I.bag}<span class="badge-count" id="cartCount"></span></button>
          </div>
        </div>
      </header>
      <div class="search-panel" id="searchPanel" role="dialog" aria-label="بحث">
        <div class="container">
          <div class="search-row">
            <input class="input" id="searchInput" type="search" placeholder="ابحثي: فستان، عباية، ليقنز…" autocomplete="off">
            <button class="icon-btn" id="searchClose" aria-label="إغلاق">${I.close}</button>
          </div>
          <div class="search-results" id="searchResults"></div>
        </div>
      </div>`;
    const spans = $$('.announce span');
    if (spans.length > 1) {
      let i = 0;
      setInterval(() => { spans[i].classList.remove('on'); i = (i + 1) % spans.length; spans[i].classList.add('on'); }, 4200);
    }
  }

  function renderFooter() {
    const el = $('#site-footer');
    if (!el) return;
    const cfg = S.config, ct = cfg.contact || {}, lg = cfg.legal || {};
    const pays = ['مدى', 'Apple Pay', 'Visa', 'Mastercard', 'STC Pay'];
    if (cfg.codEnabled) pays.push('الدفع عند الاستلام');
    const social = [
      ct.instagram && `<a href="https://instagram.com/${esc(ct.instagram)}" target="_blank" rel="noopener" aria-label="انستقرام">${I.insta}</a>`,
      ct.tiktok && `<a href="https://www.tiktok.com/@${esc(ct.tiktok)}" target="_blank" rel="noopener" aria-label="تيك توك">${I.tiktok}</a>`,
      ct.snapchat && `<a href="https://www.snapchat.com/add/${esc(ct.snapchat)}" target="_blank" rel="noopener" aria-label="سناب شات">${I.snap}</a>`,
    ].filter(Boolean).join('');
    el.innerHTML = `
      <footer class="footer">
        <div class="container footer-grid">
          <div>
            <a class="logo" href="index.html"><span class="logo-word">SORA</span><span class="logo-sub">FASHION</span></a>
            <p>سورا فاشن — أزياء نسائية منتقاة: فساتين، عبايات، وملابس رياضية بخامات مريحة وقصّات تناسبك.</p>
            ${social ? `<div class="socials">${social}</div>` : ''}
          </div>
          <div>
            <h4>تسوّقي</h4>
            ${S.cats.slice(0, 6).map(c => `<a href="index.html?cat=${esc(c.id)}#catalog">${esc(c.name)}</a>`).join('')}
          </div>
          <div>
            <h4>المساعدة</h4>
            <a href="track.html">تتبّع طلبك</a>
            <a href="policies.html#shipping">الشحن والتوصيل</a>
            <a href="policies.html#returns">الاستبدال والاسترجاع</a>
            <a href="policies.html#privacy">سياسة الخصوصية</a>
            <a href="policies.html#terms">الشروط والأحكام</a>
            <a href="policies.html#complaints">الشكاوى</a>
          </div>
          <div>
            <h4>تواصلي معنا</h4>
            ${ct.email ? `<a href="mailto:${esc(ct.email)}">${esc(ct.email)}</a>` : ''}
            ${ct.phone ? `<a href="tel:${esc(ct.phone)}" dir="ltr" style="text-align:right">${esc(ct.phone)}</a>` : ''}
            <h4 style="margin-top:1.2rem">طرق الدفع</h4>
            <div class="pay-row">${pays.map(x => `<span>${x}</span>`).join('')}</div>
          </div>
        </div>
        <div class="container footer-bottom">
          <span>© ${new Date().getFullYear()} Sora Fashion — جميع الحقوق محفوظة</span>
          <span>${lg.freelanceDocNo ? `وثيقة العمل الحر رقم ${esc(lg.freelanceDocNo)}` : ''}${lg.ownerName ? ` · ${esc(lg.ownerName)}` : ''}${lg.address || lg.city ? ` · ${esc(lg.address || lg.city)}، المملكة العربية السعودية` : ''}${lg.authUrl ? ` · <a href="${esc(lg.authUrl)}" target="_blank" rel="noopener" style="text-decoration:underline">متجر موثّق في منصة الأعمال</a>` : ''}</span>
        </div>
      </footer>`;
  }

  function renderShell() {
    const d = document.createElement('div');
    d.innerHTML = `
      <div class="overlay" id="overlay"></div>
      <aside class="drawer" id="cartDrawer" aria-label="سلة التسوق" aria-hidden="true">
        <div class="drawer-head"><h3>سلّتك <span id="drawerCount" style="color:var(--muted);font-weight:400"></span></h3><button class="icon-btn" id="cartClose" aria-label="إغلاق">${I.close}</button></div>
        <div class="ship-meter" id="shipMeter"></div>
        <div class="drawer-body" id="cartBody"></div>
        <div class="drawer-foot" id="cartFoot"></div>
      </aside>`;
    while (d.firstChild) document.body.appendChild(d.firstChild);
  }

  S.renderCart = () => {
    const body = $('#cartBody');
    if (!body) return;
    const lines = S.cartLines();
    const t = S.totals(lines, {});
    const cfg = S.config;
    $('#drawerCount').textContent = lines.length ? `(${S.cartCount()})` : '';
    const free = Number(cfg.freeShippingOver) || 0;
    const left = round2(free - (t.subtotal - t.discount));
    $('#shipMeter').innerHTML = !lines.length ? `شحن مجاني للطلبات فوق ${fmt(free)} ر.س` :
      (left > 0 ? `باقي <b>${fmt(left)} ر.س</b> ويصير شحنك مجاني` : '<b>مبروك!</b> شحنك مجاني 🎉') +
      `<div class="bar"><i style="width:${Math.min(100, (t.subtotal / (free || 1)) * 100)}%"></i></div>`;
    if (!lines.length) {
      body.innerHTML = `<div class="cart-empty">${I.bag}<p>سلّتك فاضية</p><a class="btn btn-line" href="index.html#catalog" style="margin-top:1rem">ابدئي التسوّق</a></div>`;
      $('#cartFoot').innerHTML = '';
      return;
    }
    body.innerHTML = lines.map(l => `
      <div class="line-item">
        <a class="li-thumb" href="${S.productUrl(l.p)}">${S.media(l.p, l.color.name)}</a>
        <div>
          <a class="li-name" href="${S.productUrl(l.p)}">${esc(l.p.name)}</a>
          <div class="li-meta">${esc(l.color.name)}${l.size ? ` · مقاس ${esc(l.size)}` : ''}</div>
          <div class="li-actions">
            <div class="qty"><button data-q="${esc(S.lineKey(l.id, l.color.name, l.size))}" data-d="1" aria-label="زيادة">+</button><b>${l.qty}</b><button data-q="${esc(S.lineKey(l.id, l.color.name, l.size))}" data-d="-1" aria-label="إنقاص">−</button></div>
            <button class="li-remove" data-rm="${esc(S.lineKey(l.id, l.color.name, l.size))}">حذف</button>
          </div>
        </div>
        <div class="li-price">${moneyText(l.line)}</div>
      </div>`).join('');
    $('#cartFoot').innerHTML = `
      <div class="sum-row"><span>المجموع</span><b>${moneyText(t.subtotal)}</b></div>
      <div class="sum-row"><span>الشحن</span><span>${t.shipping ? moneyText(t.shipping) : 'مجاني'}</span></div>
      <div class="sum-row total"><span>الإجمالي</span><span>${moneyText(t.total)}</span></div>
      <a class="btn btn-plum btn-block" href="checkout.html">إتمام الطلب</a>
      <p class="drawer-note">${I.lock.replace('<svg', '<svg style="width:13px;height:13px;display:inline;vertical-align:-2px"')} دفع آمن · كود الخصم في صفحة الدفع</p>`;
  };

  S.openCart = () => { $('#cartDrawer').classList.add('open'); $('#cartDrawer').setAttribute('aria-hidden', 'false'); $('#overlay').classList.add('show'); document.body.style.overflow = 'hidden'; };
  S.closeAll = () => {
    $$('.drawer.open, .nav.open, .search-panel.open, .modal.open').forEach(x => x.classList.remove('open'));
    const d = $('#cartDrawer'); if (d) d.setAttribute('aria-hidden', 'true');
    const o = $('#overlay'); if (o) o.classList.remove('show');
    document.body.style.overflow = '';
  };

  S.updateBadges = () => {
    const c = $('#cartCount'), f = $('#favCount');
    if (c) { const n = S.cartCount(); c.textContent = n || ''; c.dataset.n = n; }
    if (f) { const n = S.favs().filter(id => S.product(id)).length; f.textContent = n || ''; f.dataset.n = n; }
  };

  function renderSearch(q) {
    const box = $('#searchResults');
    q = q.trim();
    if (!q) { box.innerHTML = ''; return; }
    const hits = S.products.filter(p => (p.name + ' ' + S.catName(p.cat) + ' ' + (p.tags || []).join(' ')).includes(q)).slice(0, 12);
    box.innerHTML = hits.length ? hits.map(p => `<a class="search-hit" href="${S.productUrl(p)}"><span class="thumb">${S.media(p, (p.colors[0] || {}).name)}</span><span>${esc(p.name)}<br><b>${moneyText(p.price)}</b></span></a>`).join('')
      : '<p class="empty-state">ما لقينا شي بهالاسم — جربي كلمة ثانية</p>';
  }

  function bindShell() {
    document.addEventListener('click', e => {
      const t = e.target.closest('[data-fav],[data-q],[data-rm],#cartBtn,#cartClose,#overlay,#burger,#navClose,#searchBtn,#searchClose,#dropDraft');
      if (!t) return;
      if (t.dataset.fav) { e.preventDefault(); S.toggleFav(t.dataset.fav); }
      else if (t.dataset.q) S.setQty(t.dataset.q, (S.cartRaw().find(l => S.lineKey(l.id, l.color, l.size) === t.dataset.q) || { qty: 0 }).qty + Number(t.dataset.d));
      else if (t.dataset.rm) S.setQty(t.dataset.rm, 0);
      else if (t.id === 'cartBtn') S.openCart();
      else if (t.id === 'burger') { $('#nav').classList.add('open'); $('#overlay').classList.add('show'); }
      else if (t.id === 'searchBtn') { $('#searchPanel').classList.add('open'); $('#overlay').classList.add('show'); setTimeout(() => $('#searchInput').focus(), 150); }
      else if (t.id === 'dropDraft') { LS.del(KEYS.draft); location.reload(); }
      else S.closeAll();
    });
    document.addEventListener('keydown', e => { if (e.key === 'Escape') S.closeAll(); });
    const si = $('#searchInput');
    if (si) si.addEventListener('input', e => renderSearch(e.target.value));
    const h = $('#header');
    if (h) window.addEventListener('scroll', () => h.classList.toggle('scrolled', scrollY > 8), { passive: true });
    window.addEventListener('storage', e => { if (e.key === KEYS.cart || e.key === KEYS.favs) { S.renderCart(); S.updateBadges(); } });
  }

  const io = 'IntersectionObserver' in window ? new IntersectionObserver(es => es.forEach(en => { if (en.isIntersecting) { en.target.classList.add('in'); io.unobserve(en.target); } }), { threshold: 0.08 }) : null;
  S.reveal = root => $$('.reveal:not(.in)', root).forEach(el => (io ? io.observe(el) : el.classList.add('in')));

  S.boot = async () => {
    try { await loadData(); } catch (err) {
      document.body.insertAdjacentHTML('afterbegin', '<p style="padding:2rem;text-align:center">تعذر تحميل المتجر — حدّثي الصفحة</p>');
      console.error(err);
      return;
    }
    renderHeader();
    renderFooter();
    renderShell();
    bindShell();
    S.renderCart();
    S.updateBadges();
    state.ready = true;
    state.readyFns.forEach(fn => { try { fn(); } catch (err) { console.error(err); } });
    S.reveal();
  };

  window.Sora = S;
  document.addEventListener('DOMContentLoaded', S.boot);
})();
