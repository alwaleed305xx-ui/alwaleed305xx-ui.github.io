/* Sora Fashion — admin panel (orders, products, suppliers, settings). */
(function () {
  'use strict';
  const $ = (s, r) => (r || document).querySelector(s);
  const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
  const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const r2 = n => Math.round((Number(n) + Number.EPSILON) * 100) / 100;
  const sar = n => `${r2(n).toLocaleString('en')} ر.س`;
  const LS = {
    get(k, d) { try { const v = localStorage.getItem(k); return v == null ? d : JSON.parse(v); } catch (e) { return d; } },
    set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) { /* ignore */ } },
    del(k) { try { localStorage.removeItem(k); } catch (e) { /* ignore */ } },
  };
  const isLocal = ['localhost', '127.0.0.1', '[::1]'].includes(location.hostname);
  const K = { draft: 'sora-draft', token: 'sora-admin-token', sup: 'sora-suppliers', fee: 'sora-admin-fee', apiDev: 'sora-api-dev' };

  const STATUS = {
    awaiting_payment: 'بانتظار الدفع', new: 'جديد (عند الاستلام)', paid: 'مدفوع', processing: 'عند المورد',
    shipped: 'تم الشحن', delivered: 'تم التوصيل', cancelled: 'ملغي', refunded: 'مسترجع',
  };
  /* automatic CJ ordering (backend) — what happened to the supplier side of each order */
  const SUP_STATUS = {
    queued: ['بالدور — ينرسل لـ CJ خلال دقائق', 'info'], ordering: ['جاري الإرسال لـ CJ', 'info'], ordered: ['انطلب من CJ وانْدفع ✓', 'ok'],
    shipped: ['شحنته CJ ✓', 'ok'], delivered: ['وصل للعميلة ✓', 'ok'], awaiting_balance: ['ينتظر شحن رصيد CJ', 'warn'],
    failed: ['تعذّر الإرسال — يعيد المحاولة تلقائياً', 'warn'], hold: ['موقوف — يحتاج قرارك', 'err'], manual: ['قطع تحتاج طلب يدوي', 'err'],
  };
  const AUTO_OK = ['queued', 'ordering', 'ordered', 'shipped', 'delivered'];
  const manualOpen = o => ((o.supplierInfo || {}).manual || []).length > 0 && !(o.supplierInfo || {}).manualHandled;
  /* bought by hand: the owner typed a supplier order number and the automation isn't running it */
  const handledByHand = o => !!o.supplierRef && ['manual', ''].includes(o.supplierStatus || '') && o.status !== 'paid' && o.status !== 'new';
  const needsOwner = o => ['paid', 'new', 'processing'].includes(o.status) &&
    ((!AUTO_OK.includes(o.supplierStatus || '') && !handledByHand(o)) || manualOpen(o));
  const CARRIERS = ['CJ Packet', 'YunExpress', 'Cainiao', 'SMSA', 'Aramex', 'سبل SPL', 'J&T', 'DHL', 'Naqel', 'أخرى'];
  const TYPES = window.Garment.TYPES;

  let data = null;           // working copy (draft or published)
  let published = null;      // published products.json for diffing
  let suppliers = LS.get(K.sup, {});
  let supDirty = new Set(LS.get('sora-sup-dirty', []));      // edited here, not yet saved to the server
  let supRemoved = new Set(LS.get('sora-sup-removed', []));  // deleted here, not yet removed on the server
  let supSynced = false;                                      // server map loaded this session
  const saveSupLocal = () => { LS.set(K.sup, suppliers); LS.set('sora-sup-dirty', [...supDirty]); LS.set('sora-sup-removed', [...supRemoved]); };
  const markSup = id => { supDirty.add(String(id)); supRemoved.delete(String(id)); saveSupLocal(); };
  let orders = [];
  let orderFilter = 'todo';

  const toast = msg => {
    let w = $('.toasts');
    if (!w) { w = document.createElement('div'); w.className = 'toasts'; document.body.appendChild(w); }
    const t = document.createElement('div'); t.className = 'toast'; t.textContent = msg; w.appendChild(t);
    setTimeout(() => { t.classList.add('out'); setTimeout(() => t.remove(), 320); }, 2400);
  };

  /* ───── data ───── */
  async function load() {
    const res = await fetch('products.json', { cache: 'no-cache' });
    published = await res.json();
    const draft = LS.get(K.draft, null);
    data = draft && Array.isArray(draft.products) ? draft : JSON.parse(JSON.stringify(published));
  }
  const isDraft = () => !!LS.get(K.draft, null);
  function saveDraft(msg) {
    LS.set(K.draft, data);
    renderDraftBanner();
    if (msg) toast(msg);
  }
  function renderDraftBanner() {
    $('#draftBanner').innerHTML = isDraft()
      ? `<div class="banner warn">عندك <b>تعديلات غير منشورة</b> (تشوفينها أنتِ فقط في المتجر). للنشر: الإعدادات والنشر ← «تنزيل products.json». <button class="copy-btn" id="discardDraft">إلغاء التعديلات</button></div>`
      : '';
    const d = $('#discardDraft');
    if (d) d.onclick = () => { if (confirm('ترجعين للنسخة المنشورة وتنحذف تعديلاتك؟')) { LS.del(K.draft); location.reload(); } };
  }

  /* ───── api ───── */
  const endpoint = () => (isLocal && LS.get(K.apiDev, '')) || (data.config.ordersEndpoint || '').trim();
  async function api(action, payload) {
    const ep = endpoint();
    if (!ep) throw new Error('backend_not_configured');
    const body = JSON.stringify({ action, token: LS.get(K.token, ''), ...(payload || {}) });
    if (ep === 'local') {
      if (!window.SoraEmu) await new Promise((ok, bad) => { const s = document.createElement('script'); s.src = 'dev/emulator.js'; s.onload = ok; s.onerror = bad; document.head.appendChild(s); });
      return window.SoraEmu.call(body);
    }
    const res = await fetch(ep, { method: 'POST', headers: { 'Content-Type': 'text/plain;charset=utf-8' }, body });
    return res.json();
  }
  const fee = () => LS.get(K.fee, { pct: 2.5, fixed: 1 });
  const payFee = o => (o.payment === 'online' ? r2(o.total * fee().pct / 100 + fee().fixed) : 0);
  const itemCost = it => { const s = suppliers[String(it.id)]; return s && s.cost ? r2(s.cost * it.qty) : null; };

  /* ═════════ ORDERS ═════════ */
  function renderOrdersShell() {
    const ep = endpoint(), tok = LS.get(K.token, '');
    $('#tab-orders').innerHTML = `
      ${!ep ? `<div class="banner info">ما ربطتي السيرفر للحين. اتبعي <b>backend/SETUP.md</b> (أو اطلبي من كلود) ثم الصقي رابط السيرفر في «الإعدادات والنشر».</div>` : ''}
      <div class="card"><div class="row">
        <div class="field" style="flex:1;min-width:220px"><label>رمز الدخول (ADMIN_TOKEN)</label><input class="input" id="tokenIn" type="password" value="${esc(tok)}" placeholder="الرمز السري اللي حطيتيه في السيرفر" autocomplete="off"></div>
        <button class="btn btn-dark btn-sm" id="connectBtn" style="align-self:flex-end">دخول وتحديث</button>
      </div><p class="help">الرمز يتحفظ على هذا الجهاز فقط.</p></div>
      <div id="ordersArea" style="margin-top:1rem"></div>`;
    $('#connectBtn').onclick = async () => { LS.set(K.token, $('#tokenIn').value.trim()); await refreshOrders(); };
  }

  async function refreshOrders() {
    const area = $('#ordersArea');
    area.innerHTML = '<p class="mini">جاري تحميل الطلبات…</p>';
    let res;
    try { res = await api('listOrders', { limit: 500 }); } catch (e) { res = { ok: false, error: e.message }; }
    if (!res.ok) {
      const m = { unauthorized: 'الرمز غلط.', admin_not_configured: 'ADMIN_TOKEN مو مضبوط في السيرفر.', backend_not_configured: 'السيرفر مو مربوط.', locked_try_later: 'محاولات كثيرة — جربي بعد 15 دقيقة.' };
      area.innerHTML = `<div class="banner warn">${m[res.error] || 'تعذر الاتصال بالسيرفر.'}</div>`;
      return;
    }
    orders = res.orders;
    await loadSuppliers();
    renderOrders();
  }

  function renderOrders() {
    const area = $('#ordersArea');
    const paidLike = o => ['paid', 'new', 'processing', 'shipped', 'delivered'].includes(o.status);
    const valid = orders.filter(paidLike);
    const revenue = valid.reduce((s, o) => s + o.total, 0);
    let profit = 0, unknown = 0;
    valid.forEach(o => {
      const costs = o.items.map(itemCost);
      if (costs.some(c => c == null)) unknown++;
      profit += o.total - costs.reduce((s, c) => s + (c || 0), 0) - payFee(o);
    });
    const todo = orders.filter(needsOwner);
    const filters = { todo: 'تحتاج تدخّلك', processing: 'عند المورد', shipped: 'بالطريق', all: 'الكل' };
    const list = orderFilter === 'all' ? orders : orderFilter === 'todo' ? todo : orders.filter(o => o.status === orderFilter);
    area.innerHTML = `
      <div class="stats">
        <div class="stat"><b>${todo.length}</b><span>طلبات تنتظرك</span></div>
        <div class="stat"><b>${valid.length}</b><span>طلبات مؤكدة</span></div>
        <div class="stat"><b>${sar(revenue)}</b><span>المبيعات</span></div>
        <div class="stat good"><b>${sar(profit)}</b><span>ربحك التقريبي${unknown ? ` (${unknown} طلب بدون تكلفة مورد)` : ''}</span></div>
      </div>
      <div class="row" style="margin-bottom:1rem">${Object.entries(filters).map(([k, v]) => `<button class="chip ${orderFilter === k ? 'active' : ''}" data-of="${k}">${v}</button>`).join('')}
        <span style="flex:1"></span><button class="btn btn-ghost btn-sm" id="reloadOrders">تحديث ↻</button></div>
      ${list.length ? list.map(orderCard).join('') : '<div class="card"><p class="mini">ما فيه طلبات هنا.</p></div>'}`;
    $$('[data-of]', area).forEach(b => { b.onclick = () => { orderFilter = b.dataset.of; renderOrders(); }; });
    $('#reloadOrders').onclick = refreshOrders;
    $$('[data-save]', area).forEach(b => { b.onclick = () => saveOrder(b.dataset.save); });
    $$('[data-ful]', area).forEach(b => { b.onclick = () => fulfil(b.dataset.ful, b); });
    $$('[data-force]', area).forEach(b => { b.onclick = () => { if (confirm('تكلفة CJ أعلى من اللي دفعته العميلة — تشتريه من رصيد CJ رغم الخسارة؟')) fulfil(b.dataset.force, b, true); }; });
    $$('[data-mdone]', area).forEach(b => { b.onclick = () => markManualDone(b.dataset.mdone); });
    $$('[data-copy]', area).forEach(b => { b.onclick = () => copy(b.dataset.copy); });
  }

  function addressText(o) {
    return [o.name, o.phone, `${o.city} - ${o.district}`, o.address, o.nationalAddress ? `Short address: ${o.nationalAddress}` : '', 'Saudi Arabia'].filter(Boolean).join('\n');
  }
  function orderCard(o) {
    const costs = o.items.map(itemCost);
    const costSum = costs.reduce((s, c) => s + (c || 0), 0);
    const fees = payFee(o);
    const profit = r2(o.total - costSum - fees);
    const missing = costs.some(c => c == null);
    const wa = '966' + o.phone.slice(1);
    const ss = SUP_STATUS[o.supplierStatus];
    const info = o.supplierInfo || {};
    const cjParts = Object.values(info.cj || {});
    const canSend = ['paid', 'processing', 'new'].includes(o.status) && !['ordered', 'shipped', 'delivered', 'ordering'].includes(o.supplierStatus || '') && !handledByHand(o);
    const marginHold = o.supplierStatus === 'hold' && /^margin_guard/.test(info.lastError || '');
    const tone = ss ? (manualOpen(o) && ss[1] === 'ok' ? 'warn' : ss[1]) : (handledByHand(o) ? 'ok' : 'warn');
    /* test mode only checks the order with CJ — nothing was bought */
    const testRun = cjParts.length > 0 && cjParts.every(c => c.test);
    const supStrip = (ss || canSend || handledByHand(o)) ? `<div class="sup-strip ${testRun ? 'info' : tone}">
        <b>${testRun ? 'تجربة ✓ — انفحص مع CJ وما انطلب فعلياً' : ss ? ss[0] : handledByHand(o) ? 'انطلب يدوي ✓' : 'ما انرسل لـ CJ'}</b>
        ${cjParts.map(c => `<span class="mini" dir="ltr">CJ ${esc(c.orderId || c.orderNumber)} · ${esc(c.logistic || '')}${c.days ? ' · ' + esc(c.days) + ' days' : ''}${c.amountUsd ? ' · $' + esc(c.amountUsd) : ''}${c.status ? ' · ' + esc(c.status) : ''}</span>`).join('')}
        ${info.lastError ? `<span class="mini">السبب: <span dir="ltr">${esc(info.lastError)}</span></span>` : ''}
        ${(info.manual || []).length ? `<span class="mini">${info.manualHandled ? 'انطلب يدوي ✓: ' : 'يحتاج طلب يدوي: '}${info.manual.map(m => esc(m.name + ' (' + m.color + ' / ' + m.size + ' × ' + m.qty + ')')).join('، ')}</span>` : ''}
        ${manualOpen(o) ? `<button class="btn btn-ghost btn-sm" data-mdone="${esc(o.orderId)}">تم الطلب اليدوي</button>` : ''}
        ${canSend ? `<button class="btn btn-dark btn-sm" data-ful="${esc(o.orderId)}">إرسال لـ CJ الآن</button>` : ''}
        ${testRun && ['paid', 'processing', 'new'].includes(o.status) ? `<button class="btn btn-ghost btn-sm" data-ful="${esc(o.orderId)}" title="بعد «تشغيل CJ الحقيقي» من الجدول">اطلبه فعلياً من CJ</button>` : ''}
        ${marginHold ? `<button class="btn btn-ghost btn-sm" data-force="${esc(o.orderId)}">اشترِ رغم الخسارة</button>` : ''}
      </div>` : '';
    return `<article class="order" id="o-${esc(o.orderId)}">
      <div class="order-head"><span class="oid">${esc(o.orderId)}</span><span class="pill st-${esc(o.status)}">${STATUS[o.status] || esc(o.status)}</span>
        <span class="mini">${esc(o.createdAt.replace('T', ' '))}</span><span class="mini">${o.payment === 'cod' ? 'عند الاستلام' : 'أونلاين'}${o.paymentId ? ' · ' + esc(o.paymentId) : ''}</span>
        <span style="flex:1"></span><b>${sar(o.total)}</b></div>
      ${supStrip}
      <div class="order-body">
        <div class="kv">
          <div><b>${esc(o.name)}</b>${o.nameEn ? ` <span class="mini" dir="ltr">(${esc(o.nameEn)})</span>` : ''}</div>
          <div><a href="tel:${esc(o.phone)}" dir="ltr">${esc(o.phone)}</a> · <a href="https://wa.me/${esc(wa)}" target="_blank" rel="noopener" class="copy-btn">واتساب</a></div>
          <div>${esc(o.city)} — ${esc(o.district)}<br>${esc(o.address)}${o.nationalAddress ? `<br>العنوان الوطني: <span dir="ltr">${esc(o.nationalAddress)}</span>` : ''}</div>
          ${o.notes ? `<div class="mini">ملاحظة: ${esc(o.notes)}</div>` : ''}
          <div><button class="copy-btn" data-copy="${esc(addressText(o))}">نسخ العنوان للمورد</button></div>
        </div>
        <div>
          <table class="items"><thead><tr><th>القطعة</th><th>لون / مقاس</th><th>كمية</th><th>البيع</th><th>المورد</th></tr></thead><tbody>
          ${o.items.map((it, i) => { const s = suppliers[String(it.id)] || {}; return `<tr>
            <td>${esc(it.name)}</td><td>${esc(it.color)} / ${esc(it.size)}</td><td>${it.qty}</td><td>${sar(it.line)}</td>
            <td>${s.url ? `<a class="sup-link" href="${esc(s.url)}" target="_blank" rel="noopener">اطلبي من ${esc(s.supplier || 'المورد')} ↗</a>` : '<span class="sup-missing">ما فيه رابط مورد</span>'}${costs[i] != null ? `<div class="mini">تكلفة ${sar(costs[i])}</div>` : ''}</td></tr>`; }).join('')}
          </tbody></table>
          <div class="profit"><span>شحن محصّل: ${sar(o.shipping)}</span>${o.discount ? `<span>خصم: ${sar(o.discount)} (${esc(o.coupon)})</span>` : ''}<span>رسوم دفع تقريبية: ${sar(fees)}</span><span>تكلفة المورد: ${missing ? '<span class="sup-missing">ناقصة</span>' : sar(costSum)}</span>
            <span>الربح: <b class="${profit >= 0 ? 'pos' : 'neg'}">${missing ? '—' : sar(profit)}</b></span></div>
        </div>
      </div>
      <div class="order-actions">
        <div class="field"><label>الحالة</label><select class="select" id="st-${esc(o.orderId)}">${Object.entries(STATUS).map(([k, v]) => `<option value="${k}" ${k === o.status ? 'selected' : ''}>${v}</option>`).join('')}</select></div>
        <div class="field"><label>رقم الطلب عند المورد</label><input class="input" id="sr-${esc(o.orderId)}" value="${esc(o.supplierRef)}" dir="ltr"></div>
        <div class="field"><label>شركة الشحن</label><input class="input" id="ca-${esc(o.orderId)}" value="${esc(o.carrier)}" list="carriers"></div>
        <div class="field"><label>رقم التتبّع</label><input class="input" id="tn-${esc(o.orderId)}" value="${esc(o.trackingNo)}" dir="ltr"></div>
        <button class="btn btn-dark btn-sm" data-save="${esc(o.orderId)}">حفظ</button>
      </div>
    </article>`;
  }

  async function saveOrder(id) {
    const v = p => $(`#${p}-${CSS.escape(id)}`).value.trim();
    const cur = orders.find(o => o.orderId === id) || {};
    const payload = { orderId: id, prevStatus: cur.status, status: v('st'), supplierRef: v('sr'), carrier: v('ca'), trackingNo: v('tn') };
    if (payload.status === 'shipped' && !payload.trackingNo && !confirm('ما كتبتي رقم تتبّع — تحفظين "تم الشحن" بدونه؟')) return;
    let res;
    try { res = await api('updateOrder', payload); } catch (e) { res = { ok: false }; }
    if (!res.ok && res.error === 'stale_order') { toast('الطلب تغيّر من مكان ثاني (مثلاً وصل الدفع) — حدّثنا البيانات، راجعيه واحفظي مرة ثانية'); await refreshOrders(); return; }
    if (!res.ok) { toast('ما انحفظ — ' + (res.error || 'خطأ اتصال')); return; }
    const i = orders.findIndex(o => o.orderId === id);
    if (i > -1) orders[i] = res.order;
    toast('انحفظ ✓');
    renderOrders();
  }
  async function markManualDone(id) {
    const cur = orders.find(o => o.orderId === id) || {};
    let res;
    try { res = await api('updateOrder', { orderId: id, prevStatus: cur.status, manualHandled: true }); } catch (e) { res = { ok: false }; }
    toast(res.ok ? 'انحفظ ✓' : 'ما انحفظ — ' + (res.error || 'خطأ اتصال'));
    await refreshOrders();
  }
  async function fulfil(id, btn, force) {
    const cur = orders.find(o => o.orderId === id) || {};
    if (!force && cur.supplierRef && !confirm(`فيه رقم طلب مورد مسجّل (${cur.supplierRef}). ترسل لـ CJ مرة ثانية؟`)) return;
    btn.disabled = true; btn.textContent = 'جاري الإرسال…';
    let res;
    try { res = await api('fulfillOrder', { orderId: id, force: !!force }); } catch (e) { res = { ok: false, error: e.message }; }
    const msg = { cj_not_configured: 'مفتاح CJ مو مضاف في السيرفر (CJ_API_KEY)', order_not_paid: 'الطلب مو مدفوع', cj_auth_failed: 'مفتاح CJ غلط أو موقوف' };
    const testRef = /^TEST-/.test(res.supplierRef || '');
    toast(res.ok ? (testRef ? (res.already ? 'وضع التجربة شغّال — شغّل «3) تشغيل CJ الحقيقي» من الجدول أول' : 'تجربة ✓ — انفحص مع CJ وما انطلب فعلياً')
      : res.already ? 'الطلب مرسل لـ CJ من قبل ✓' : 'انطلب من CJ وانْدفع ✓') : 'ما انرسل — ' + (msg[res.error] || (SUP_STATUS[res.supplierStatus] || [res.error || ''])[0]));
    await refreshOrders();
  }
  function copy(text) {
    (navigator.clipboard ? navigator.clipboard.writeText(text) : Promise.reject()).then(() => toast('انسخ ✓'), () => prompt('انسخي:', text));
  }

  /* ═════════ PRODUCTS ═════════ */
  let pq = '', pcat = 'all';
  function renderProducts() {
    $('#tab-products').innerHTML = `
      <div class="row" style="margin-bottom:1rem">
        <button class="btn btn-plum btn-sm" id="addP">＋ منتج جديد</button>
        <button class="btn btn-dark btn-sm" id="addCj">＋ استيراد من CJ</button>
        <input class="input" id="pq" placeholder="ابحثي…" value="${esc(pq)}" style="max-width:240px;min-height:40px">
        <select class="select" id="pcat" style="max-width:200px;min-height:40px"><option value="all">كل الأقسام</option>${data.cats.map(c => `<option value="${esc(c.id)}" ${pcat === c.id ? 'selected' : ''}>${esc(c.name)}</option>`).join('')}</select>
        <span class="mini" id="pcount"></span>
      </div>
      <div class="pgrid" id="pgrid"></div>`;
    renderProductGrid();
    $('#addP').onclick = () => openEditor(null);
    $('#addCj').onclick = openCjImport;
    $('#pq').oninput = e => { pq = e.target.value; renderProductGrid(); };
    $('#pcat').onchange = e => { pcat = e.target.value; renderProductGrid(); };
    bindProductActions();
  }
  function renderProductGrid() {
    const q = pq.trim();
    const list = data.products.filter(p => (pcat === 'all' || p.cat === pcat) && (!q || p.name.includes(q)));
    $('#pcount').textContent = `${list.length} من ${data.products.length}`;
    $('#pgrid').innerHTML = `${list.map(p => {
        const s = suppliers[String(p.id)] || {};
        const c0 = (p.colors || [])[0] || { hex: '#333' };
        const img = (p.images || [])[0];
        return `<div class="pc ${p.show === false ? 'off' : ''}">
          <div class="pc-media">${img ? `<img src="${esc(img)}" alt="" referrerpolicy="no-referrer">` : window.Garment.svg(p.type, c0.hex, { accent: p.accent })}</div>
          <div class="pc-body"><b>${esc(p.name)}</b><span class="mini">${esc((data.cats.find(c => c.id === p.cat) || {}).name || p.cat)} · ${sar(p.price)}</span>
            <span class="mini">${/^cj/i.test(s.supplier || '') && s.ref && !/:/.test(s.ref) ? '<span class="ok-dot">●</span> CJ تلقائي'
              : /^(sourcing|found):/.test(s.ref || '') ? '<span class="warn-dot">●</span> CJ يدور عليه / جاهز للربط'
                : s.url ? '<span class="warn-dot">●</span> مربوط بمورد (طلب يدوي)' : '<span class="warn-dot">●</span> بدون مورد'}${s.cost ? ` · تكلفة ${sar(s.cost)}` : ''}${(p.images || []).length ? ' · صور حقيقية' : ''}</span>
            <div class="pc-actions"><button data-edit="${p.id}">تعديل</button><button data-dup="${p.id}">نسخ</button><button data-tog="${p.id}">${p.show === false ? 'إظهار' : 'إخفاء'}</button><button data-del="${p.id}">حذف</button></div>
          </div></div>`;
      }).join('')}`;
  }
  function bindProductActions() {
    $('#tab-products').onclick = e => {
      const b = e.target.closest('[data-edit],[data-dup],[data-tog],[data-del]');
      if (!b) return;
      const id = Number(b.dataset.edit || b.dataset.dup || b.dataset.tog || b.dataset.del);
      const p = data.products.find(x => x.id === id);
      if (b.dataset.edit) openEditor(p);
      else if (b.dataset.dup) { const copyP = JSON.parse(JSON.stringify(p)); copyP.id = nextId(); copyP.name += ' (نسخة)'; data.products.unshift(copyP); saveDraft('انسخ المنتج'); renderProducts(); }
      else if (b.dataset.tog) { p.show = p.show === false; saveDraft(); renderProducts(); }
      else if (b.dataset.del && confirm(`حذف «${p.name}»؟`)) {
        data.config.lastId = Math.max(Number(data.config.lastId) || 0, ...data.products.map(x => Number(x.id) || 0));
        data.products = data.products.filter(x => x.id !== id);
        if (suppliers[String(id)]) { delete suppliers[String(id)]; supDirty.delete(String(id)); supRemoved.add(String(id)); saveSupLocal(); syncSuppliers(true); }
        saveDraft('انحذف'); renderProducts();
      }
    };
  }
  const nextId = () => {
    let id = Math.max(Number(data.config.lastId) || 0, ...data.products.map(p => Number(p.id) || 0)) + 1;
    /* never reuse an id that still has a supplier row (it would inherit someone else's CJ links) */
    while (suppliers[String(id)]) id++;
    data.config.lastId = id;
    return id;
  };

  /* ═════════ CJ IMPORT ═════════ */
  const CJ_COLORS = {
    black: ['أسود', '#1c1b1d'], white: ['أبيض', '#f5f3ee'], 'off white': ['أوف وايت', '#efe8dc'], ivory: ['أوف وايت', '#efe8dc'], cream: ['كريمي', '#efe6d2'],
    beige: ['بيج', '#d9c7a8'], apricot: ['مشمشي', '#f0b48a'], khaki: ['خاكي', '#b09a6d'], camel: ['جملي', '#b98a57'], champagne: ['شامبين', '#e7d3b0'],
    coffee: ['بني قهوة', '#5a3e2b'], brown: ['بني', '#6b4a33'], 'dark brown': ['بني غامق', '#4a3123'], 'light brown': ['بني فاتح', '#9b7556'],
    grey: ['رمادي', '#8d8d8f'], gray: ['رمادي', '#8d8d8f'], 'dark gray': ['رمادي غامق', '#4f4f53'], 'dark grey': ['رمادي غامق', '#4f4f53'], 'light gray': ['رمادي فاتح', '#c9c9cb'], 'light grey': ['رمادي فاتح', '#c9c9cb'],
    navy: ['كحلي', '#1f2a44'], 'navy blue': ['كحلي', '#1f2a44'], 'dark blue': ['كحلي', '#1f2a44'], blue: ['أزرق', '#3b5ba5'], 'royal blue': ['أزرق ملكي', '#2847a0'],
    'sky blue': ['أزرق سماوي', '#9cc3e6'], 'light blue': ['أزرق فاتح', '#a9c8e8'], green: ['أخضر', '#3f7d4e'], 'dark green': ['أخضر غامق', '#23402e'],
    'army green': ['زيتي', '#5d5f3a'], olive: ['زيتي', '#5d5f3a'], mint: ['نعناعي', '#a8d8c4'], 'mint green': ['نعناعي', '#a8d8c4'], 'light green': ['أخضر فاتح', '#a7c99a'],
    pink: ['وردي', '#e8a3b5'], 'light pink': ['وردي فاتح', '#f3c9d3'], 'rose red': ['وردي غامق', '#c2416b'], rose: ['وردي غامق', '#c2416b'], red: ['أحمر', '#b3263a'],
    'wine red': ['عنابي', '#6d1f2f'], wine: ['عنابي', '#6d1f2f'], burgundy: ['عنابي', '#6d1f2f'], purple: ['بنفسجي', '#6b3d8f'], 'light purple': ['لافندر', '#b9a7d6'],
    lavender: ['لافندر', '#b9a7d6'], violet: ['بنفسجي', '#7b4fa0'], yellow: ['أصفر', '#e7c64a'], orange: ['برتقالي', '#e27a3b'], gold: ['ذهبي', '#c9a64a'], silver: ['فضي', '#b8b8bd'],
  };
  const cjColor = en => {
    const k = String(en || '').toLowerCase().replace(/[^a-z ]/g, ' ').replace(/\s+/g, ' ').trim();
    if (CJ_COLORS[k]) return CJ_COLORS[k];
    const hit = Object.keys(CJ_COLORS).sort((a, b) => b.length - a.length).find(key => k.includes(key));
    return hit ? CJ_COLORS[hit] : [String(en || 'حسب الصورة').trim(), '#8a8a8a'];
  };
  const cjSize = s => {
    const t = String(s || '').trim().toUpperCase().replace(/\.$/, '');
    if (/^(ONE ?SIZE|FREE ?SIZE|F|OS)$/.test(t)) return 'مقاس حر';
    return { XXL: '2XL', XXXL: '3XL', XXXXL: '4XL', XXXXXL: '5XL' }[t] || t;
  };
  const SIZE_ORDER = ['XXS', 'XS', 'S', 'M', 'L', 'XL', '2XL', '3XL', '4XL', '5XL', '6XL'];
  const roundPrice = sar => { let p = Math.ceil(sar); p += (9 - p % 10) % 10; return p; };

  function openCjImport() {
    const card = $('#editorCard');
    card.innerHTML = `
      <div class="modal-head"><h3>استيراد منتج من CJ</h3><button class="icon-btn" data-x>✕</button></div>
      <div class="row"><input class="input" id="cjq" placeholder="ابحث بالإنجليزي، أو الصق رابط منتج من CJ" dir="ltr" style="flex:1;min-width:220px">
        <select class="select" id="cjcat" style="max-width:200px">${data.cats.map(c => `<option value="${esc(c.id)}">${esc(c.name)}</option>`).join('')}</select>
        <button class="btn btn-dark btn-sm" id="cjgo">بحث</button>
        <button class="btn btn-ghost btn-sm" id="cjmine" title="القطع اللي ضفتها في CJ بزر Add to My Products">منتجاتي في CJ</button></div>
      <p class="help">اختر من موقع CJ (مثلاً قسم Brands) واضغط على القطعة <b>Add to My Products</b>، وبعدها «منتجاتي في CJ» هنا — أو الصق رابط القطعة مباشرة. نجيب صورها وألوانها ومقاساتها وسعر الشحن للسعودية، ونربط كل لون ومقاس بـ CJ تلقائياً عشان الطلبات تمشي بدون تدخّل. (كل بحث يصرف 50 نقطة من رصيد CJ اليومي.)</p>
      <div class="cj-results" id="cjres"></div>`;
    $$('[data-x]', card).forEach(b => { b.onclick = closeEditor; });
    const imported = new Set(Object.values(suppliers).map(s => s && s.ref).filter(Boolean));
    const show = (r, empty) => {
      if (!r.ok) { $('#cjres').innerHTML = `<p class="sup-missing">${r.error === 'cj_not_configured' ? 'أضف CJ_API_KEY في إعدادات السيرفر أول.' : 'تعذر — ' + esc(r.error)}</p>`; return; }
      const thumb = u => (/^https:\/\/((oss-)?cf\.cjdropshipping\.com|cj-product-center\.oss-accelerate\.aliyuncs\.com)\/[^?#]+$/.test(u) ? u + '?x-oss-process=image/resize,w_200' : u);
      $('#cjres').innerHTML = r.list.length ? r.list.map(p => `<div class="cj-item">${p.image ? `<img src="${esc(thumb(p.image))}" alt="" loading="lazy" referrerpolicy="no-referrer">` : ''}
        <div><span dir="ltr">${esc(p.name)}</span><b>$${esc(p.priceUsd)}</b>${p.stock != null ? `<span class="mini">مخزون ${p.stock} · مضاف في ${p.listed} متجر</span>` : ''}
        ${imported.has(p.pid) ? '<span class="mini">موجودة في المتجر ✓</span>' : `<button class="btn btn-plum btn-sm" data-cjpid="${esc(p.pid)}">إضافة للمتجر</button>`}</div></div>`).join('') : `<p class="mini">${empty}</p>`;
    };
    const run = async () => {
      const q = $('#cjq').value.trim();
      if (!q) return;
      /* a pasted CJ product link (…-p-<id>.html) or a bare product id goes straight to the import */
      const m = q.match(/-p-([0-9A-Za-z-]{16,})\.html/) || q.match(/^([0-9]{17,20}|[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12})$/i);
      if (m) {
        $('#cjres').innerHTML = `<div class="cj-item"><div><span dir="ltr">${esc(m[1])}</span><button class="btn btn-plum btn-sm" data-cjpid="${esc(m[1])}">إضافة للمتجر</button></div></div>`;
        return;
      }
      $('#cjres').innerHTML = '<p class="mini">جاري البحث…</p>';
      let r;
      try { r = await api('cjSearch', { keyword: q }); } catch (e) { r = { ok: false, error: e.message }; }
      show(r, 'ما فيه نتائج — جرب كلمة ثانية.');
    };
    $('#cjmine').onclick = async () => {
      $('#cjres').innerHTML = '<p class="mini">جاري جلب منتجاتك من CJ…</p>';
      let r;
      try { r = await api('cjMyProducts'); } catch (e) { r = { ok: false, error: e.message }; }
      show(r, 'ما فيه منتجات في My Products — افتح القطعة في موقع CJ واضغط Add to My Products.');
    };
    $('#cjgo').onclick = run;
    $('#cjq').onkeydown = ev => { if (ev.key === 'Enter') run(); };
    $('#cjres').onclick = ev => { const b = ev.target.closest('[data-cjpid]'); if (b) importCj(b.dataset.cjpid, $('#cjcat').value, b); };
    $('#editor').classList.add('open'); $('#overlay').classList.add('show');
    setTimeout(() => $('#cjq').focus(), 50);
  }

  /* CJ variantKey ("Black-XL") → Arabic colour + store size, the cheapest ≤25-day line, and landed cost in SAR */
  function parseCj(p) {
    const names = String(p.optionNames || '').split('-').map(s => s.trim().toLowerCase()).filter(Boolean);
    const ci = names.findIndex(n => /colou?r/.test(n)), si = names.findIndex(n => /size/.test(n));
    const rows = [], odd = [];
    let maxUsd = 0;
    (p.variants || []).forEach(v => {
      const parts = String(v.key).split('-').map(x => x.trim());
      let cRaw = '', sRaw = '';
      /* everything that isn't the size (colour, plus any style/material option) is part of the colour label,
         so two different CJ variants can never share one colour|size key */
      if (names.length && parts.length === names.length) { sRaw = si >= 0 ? parts[si] : ''; cRaw = parts.filter((_, i) => i !== si).join(' '); }
      else if (names.length <= 1) { if (si === 0) sRaw = v.key; else cRaw = v.key; }
      else { odd.push(v.key); return; }
      const [color, hex] = cRaw ? cjColor(cRaw) : ['حسب الصورة', '#8a8a8a'];
      rows.push({ color, hex, size: sRaw ? cjSize(sRaw) : 'مقاس حر', vid: v.vid, image: v.image, raw: cRaw });
      maxUsd = Math.max(maxUsd, Number(v.priceUsd) || 0);
    });
    /* different CJ colours that translate to the same Arabic name ("Navy" / "Dark Blue" → كحلي) keep their CJ name */
    const rawsOf = {}, seen = new Set();
    rows.forEach(r => { (rawsOf[r.color] = rawsOf[r.color] || new Set()).add(r.raw); });
    rows.forEach(r => { if (rawsOf[r.color].size > 1) r.color = `${r.color} (${r.raw})`; });
    const unique = rows.filter(r => { const k = r.color + '|' + r.size; if (seen.has(k)) { odd.push(r.raw + '-' + r.size + ' (مكرر)'); return false; } seen.add(k); return true; });
    rows.length = 0; rows.push(...unique);
    const maxDays = x => Number((String(x.days).match(/(\d+)\s*$/) || [])[1] || 99);
    /* a line's clearance fee is charged by CJ on top of postage */
    const lineUsd = x => Math.max(Number(x.usd) || 0, Number(x.totalUsd) || 0);
    const lines = (p.shipping || []).slice().sort((a, b) => lineUsd(a) - lineUsd(b));
    const ship = lines.find(x => maxDays(x) <= 25) || lines[0] || null;
    return { rows, odd, ship, costSar: r2((maxUsd + (ship ? lineUsd(ship) : 0)) * 3.75) };
  }

  async function importCj(pid, cat, btn) {
    btn.disabled = true; btn.textContent = 'جاري الجلب…';
    let r;
    try { r = await api('cjProduct', { pid }); } catch (e) { r = { ok: false, error: e.message }; }
    if (!r.ok) { btn.disabled = false; btn.textContent = 'إضافة للمتجر'; toast('تعذر جلب المنتج — ' + (r.error || '')); return; }
    const p = r.product;
    const { rows, odd, ship, costSar } = parseCj(p);
    const colors = new Map(), sizes = [], variants = {}, colorImages = {};
    rows.forEach(v => {
      if (!colors.has(v.color)) colors.set(v.color, v.hex);
      if (!sizes.includes(v.size)) sizes.push(v.size);
      variants[v.color + '|' + v.size] = v.vid;
      if (v.image && !colorImages[v.color]) colorImages[v.color] = [v.image];
    });
    if (!Object.keys(variants).length) { btn.disabled = false; btn.textContent = 'إضافة للمتجر'; toast('ما قدرنا نقرأ ألوان/مقاسات هالقطعة — اختر غيرها'); return; }
    sizes.sort((a, b) => (SIZE_ORDER.indexOf(a) + 1 || 99) - (SIZE_ORDER.indexOf(b) + 1 || 99));
    const mult = costSar < 50 ? 2.5 : costSar < 100 ? 2.2 : 2.0;
    const catObj = data.cats.find(c => c.id === cat) || data.cats[0];
    const onlyFree = sizes.length === 1 && sizes[0] === 'مقاس حر';
    const draft = {
      id: nextId(), cat: catObj.id, type: catObj.type || 'maxi', name: '', price: roundPrice(Math.max(costSar * mult, costSar + 40)), old: 0, badge: 'new',
      sizeChart: onlyFree ? 'onesize' : (catObj.id === 'sport' ? 'sport' : 'women'), sizes, colors: [...colors].map(([name, hex]) => ({ name, hex })),
      desc: '', details: [], fabric: '', care: '', images: p.images || [], colorImages, show: true,
    };
    /* colour + size pairs CJ doesn't sell: the product page greys them out and checkout refuses them */
    const na = [];
    draft.colors.forEach(c => sizes.forEach(z => { if (!variants[c.name + '|' + z]) na.push(c.name + '|' + z); }));
    if (na.length) draft.na = na;
    const preset = {
      supplier: 'CJ', url: `https://cjdropshipping.com/search/${encodeURIComponent(p.sku || p.pid)}.html`, cost: costSar,
      notes: `${p.name} | SKU ${p.sku || '-'}${ship ? ` | شحن ${ship.name} ${ship.days} يوم $${ship.usd}` : ''}${odd.length ? ' | متغيرات ما انقرت: ' + odd.join(', ') : ''}`,
      ref: p.pid, variants, logistic: ship ? ship.name : '', from: p.from || 'CN',
    };
    toast(`انجلب ✓ ${colors.size} لون · ${sizes.length} مقاس — اكتب الاسم والوصف بالعربي واحفظ`);
    openEditor(draft, preset);
  }

  /* link an existing store product to the CJ product CJ's agents found: colours are matched by Arabic name */
  async function relinkCj(productId, cjPid, btn) {
    const prod = data.products.find(x => String(x.id) === String(productId));
    if (!prod) return;
    btn.disabled = true; btn.textContent = 'جاري الربط…';
    let r;
    try { r = await api('cjProduct', { pid: cjPid }); } catch (e) { r = { ok: false, error: e.message }; }
    if (!r.ok) { btn.disabled = false; btn.textContent = 'ربط بـ CJ'; toast('تعذر جلب المنتج من CJ — ' + (r.error || '')); return; }
    const { rows, ship, costSar } = parseCj(r.product);
    /* each store colour gets exactly one CJ colour: exact Arabic name first, then a fuzzy match only when a
       single CJ colour fits — an ambiguous colour stays unlinked rather than risk shipping the wrong one */
    const base = v => (v.raw ? cjColor(v.raw)[0] : '');
    const raws = [...new Set(rows.map(v => v.raw))];
    const bind = {}, ambiguous = [];
    prod.colors.forEach(c => {
      const exact = raws.filter(raw => base({ raw }) === c.name);
      if (exact.length === 1) bind[c.name] = exact[0];
      else if (exact.length > 1) ambiguous.push(c.name);
    });
    prod.colors.forEach(c => {
      if (bind[c.name] || ambiguous.includes(c.name)) return;
      const taken = new Set(Object.values(bind));
      const fuzzy = raws.filter(raw => !taken.has(raw) && base({ raw }) && (c.name.includes(base({ raw })) || base({ raw }).includes(c.name)));
      if (fuzzy.length === 1) bind[c.name] = fuzzy[0];
      else if (fuzzy.length > 1) ambiguous.push(c.name);
    });
    if (prod.colors.length === 1 && raws.length === 1 && !bind[prod.colors[0].name]) bind[prod.colors[0].name] = raws[0];
    const variants = {};
    Object.entries(bind).forEach(([cName, raw]) => rows.filter(v => v.raw === raw && prod.sizes.includes(v.size)).forEach(v => { variants[cName + '|' + v.size] = v.vid; }));
    const linked = Object.keys(variants).length;
    if (!linked) { btn.disabled = false; btn.textContent = 'ربط بـ CJ'; toast('ألوان CJ ما طابقت ألوان المنتج' + (ambiguous.length ? ' (التباس في: ' + ambiguous.join('، ') + ')' : '') + ' — استورده كمنتج جديد من «استيراد من CJ»'); return; }
    if (ambiguous.length && !confirm(`ألوان فيها التباس وما انربطت: ${ambiguous.join('، ')} — بتنقفل بالموقع. نكمل؟`)) { btn.disabled = false; btn.textContent = 'ربط بـ CJ'; return; }
    /* a colour CJ doesn't make at all leaves the product; partial gaps are greyed out per size */
    const dropped = prod.colors.filter(c => !prod.sizes.some(z => variants[c.name + '|' + z])).map(c => c.name);
    prod.colors = prod.colors.filter(c => !dropped.includes(c.name));
    dropped.forEach(n => { if (prod.colorImages) delete prod.colorImages[n]; });
    const na = [];
    prod.colors.forEach(c => prod.sizes.forEach(z => { if (!variants[c.name + '|' + z]) na.push(c.name + '|' + z); }));
    if (na.length) prod.na = na; else delete prod.na;
    const prev = suppliers[String(prod.id)] || {};
    suppliers[String(prod.id)] = Object.assign({}, prev, { supplier: 'CJ', ref: cjPid, variants, logistic: ship ? ship.name : '', from: r.product.from || 'CN', cost: costSar || prev.cost,
      notes: `${prev.notes ? prev.notes + ' | ' : ''}CJ ${cjPid}${prev.url ? ' (كان: ' + prev.url + ')' : ''}` });
    markSup(prod.id);
    saveDraft();
    await syncSuppliers(true);
    toast(`انربط ✓ ${linked} لون/مقاس${dropped.length ? ` — شلنا ${dropped.length} لون ما يوفّره CJ` : ''}${na.length ? ` — ${na.length} مقاس انقفل` : ''}${dropped.length || na.length ? ' (انشر products.json)' : ''}`);
    renderSuppliers();
  }

  function openEditor(p, preset) {
    const isNew = !p || !data.products.some(x => x.id === p.id);
    const e = p ? JSON.parse(JSON.stringify(p)) : { id: nextId(), cat: data.cats[0].id, type: data.cats[0].type, name: '', price: 0, old: 0, badge: 'new', sizeChart: 'women', sizes: ['S', 'M', 'L', 'XL'], colors: [{ name: 'أسود', hex: '#1c1b1d' }], desc: '', details: [], fabric: '', care: '', images: [], show: true };
    const s = Object.assign({ supplier: '', url: '', cost: 0, notes: '' }, suppliers[String(e.id)] || {}, preset || {});
    const card = $('#editorCard');
    card.innerHTML = `
      <div class="modal-head"><h3>${isNew ? 'منتج جديد' : 'تعديل: ' + esc(e.name)}</h3><button class="icon-btn" data-x>✕</button></div>
      <form id="pForm"><div class="editor-grid">
        <div class="field c4"><label>اسم المنتج</label><input class="input" name="name" value="${esc(e.name)}" required maxlength="80"></div>
        <div class="field"><label>القسم</label><select class="select" name="cat">${data.cats.map(c => `<option value="${esc(c.id)}" ${c.id === e.cat ? 'selected' : ''}>${esc(c.name)}</option>`).join('')}</select></div>
        <div class="field"><label>شكل الرسمة (لو ما فيه صور)</label><select class="select" name="type">${Object.entries(TYPES).map(([k, v]) => `<option value="${k}" ${k === e.type ? 'selected' : ''}>${v}</option>`).join('')}</select></div>
        <div class="field"><label>السعر (ر.س)</label><input class="input" name="price" type="number" min="1" step="1" value="${e.price}" required></div>
        <div class="field"><label>السعر قبل الخصم <span class="opt">(اختياري)</span></label><input class="input" name="old" type="number" min="0" step="1" value="${e.old || ''}"></div>
        <div class="field"><label>الشارة</label><select class="select" name="badge">${[['', 'بدون'], ['new', 'جديد'], ['sale', 'عرض'], ['trend', 'ترند'], ['pick', 'مختارات سورا']].map(([k, v]) => `<option value="${k}" ${k === (e.badge || '') ? 'selected' : ''}>${v}</option>`).join('')}</select></div>
        <div class="field"><label>جدول المقاسات</label><select class="select" name="sizeChart">${[['women', 'ملابس نسائية'], ['eu', 'مقاسات بالأرقام (36-48)'], ['sport', 'رياضي'], ['abaya', 'عبايات وإسدال (طول)'], ['onesize', 'مقاس حر']].map(([k, v]) => `<option value="${k}" ${k === e.sizeChart ? 'selected' : ''}>${v}</option>`).join('')}</select></div>
        <div class="field c2"><label>المقاسات (افصلي بفاصلة)</label><input class="input" name="sizes" value="${esc((e.sizes || []).join(', '))}" required></div>
        <div class="field"><label>مميّز بالصفحة الرئيسية</label><select class="select" name="featured"><option value="1" ${e.featured ? 'selected' : ''}>نعم</option><option value="" ${!e.featured ? 'selected' : ''}>لا</option></select></div>
        <div class="field"><label>مدة التوصيل لهالقطعة <span class="opt">(اختياري)</span></label><input class="input" name="shipDays" value="${esc(e.shipDays || '')}" dir="ltr" placeholder="${esc(data.config.shippingDays)}" pattern="\\d{1,2}\\s*-\\s*\\d{1,2}"></div>
        <div class="field"><label>معروض</label><select class="select" name="show"><option value="1" ${e.show !== false ? 'selected' : ''}>نعم</option><option value="" ${e.show === false ? 'selected' : ''}>لا (مخفي)</option></select></div>
        <div class="field c4"><label>الألوان</label><div class="color-rows" id="colorRows">${(e.colors || []).map(colorRow).join('')}</div><button type="button" class="copy-btn" id="addColor" style="margin-top:.4rem">＋ لون</button></div>
        <div class="field c4"><label>الوصف</label><textarea class="textarea" name="desc" maxlength="500">${esc(e.desc)}</textarea></div>
        <div class="field c2"><label>التفاصيل (كل نقطة بسطر)</label><textarea class="textarea" name="details">${esc((e.details || []).join('\n'))}</textarea></div>
        <div class="field c2"><label>روابط الصور (كل رابط بسطر) — من صفحة المورد</label><textarea class="textarea" name="images" dir="ltr" placeholder="https://…jpg">${esc((e.images || []).join('\n'))}</textarea><span class="help">لو تركتيها فاضية تظهر الرسمة الأنيقة بلون المنتج.</span></div>
        <div class="field c2"><label>الخامة</label><input class="input" name="fabric" value="${esc(e.fabric)}"></div>
        <div class="field c2"><label>العناية</label><input class="input" name="care" value="${esc(e.care)}"></div>
        <div class="c4 sup-box"><h4>🔒 بيانات المورد (سرّية — ما تنشر بالموقع)</h4><div class="editor-grid">
          <div class="field"><label>اسم المورد</label><input class="input" name="s_supplier" value="${esc(s.supplier)}" placeholder="CJ Dropshipping"></div>
          <div class="field c2"><label>رابط المنتج عند المورد</label><input class="input" name="s_url" value="${esc(s.url)}" dir="ltr" placeholder="https://cjdropshipping.com/product/..."></div>
          <div class="field"><label>التكلفة مع الشحن (ر.س)</label><input class="input" name="s_cost" type="number" min="0" step="0.01" value="${s.cost || ''}"></div>
          <div class="field c4"><label>ملاحظات (اسم المتغيّر عند المورد، الخامة…)</label><input class="input" name="s_notes" value="${esc(s.notes)}"></div>
          ${s.ref ? `<div class="c4 mini"><span class="ok-dot">●</span> مربوط بـ CJ (PID <span dir="ltr">${esc(s.ref)}</span>) — ${Object.keys(s.variants || {}).length} لون/مقاس مربوط، والطلبات تنرسل تلقائياً${s.logistic ? ` · خط الشحن <span dir="ltr">${esc(s.logistic)}</span>` : ''}. لو غيّرت أسماء الألوان هنا ينحدّث الربط تلقائياً.</div>` : ''}
          <div class="c4 mini" id="marginOut"></div>
        </div></div>
      </div>
      <div class="row" style="justify-content:flex-end;margin-top:1rem"><button type="button" class="btn btn-ghost btn-sm" data-x>إلغاء</button><button class="btn btn-plum btn-sm">حفظ</button></div></form>`;
    const f = $('#pForm');
    const margin = () => {
      const price = Number(f.price.value) || 0, cost = Number(f.s_cost.value) || 0;
      if (!price || !cost) { $('#marginOut').textContent = 'اكتبي التكلفة عشان نحسب ربحك.'; return; }
      const fees = r2(price * fee().pct / 100 + fee().fixed), prof = r2(price - cost - fees);
      $('#marginOut').innerHTML = `ربحك التقريبي بالقطعة (قبل الإعلانات): <b style="color:${prof > 0 ? 'var(--ok)' : 'var(--err)'}">${sar(prof)}</b> — هامش ${Math.round(prof / price * 100)}٪ · السعر ÷ التكلفة = ${(price / cost).toFixed(1)}× ${price / cost < 2.5 ? '<span class="sup-missing">(أقل من 2.5× — صعب يغطي الإعلانات)</span>' : ''}`;
    };
    f.price.oninput = margin; f.s_cost.oninput = margin; margin();
    $('#addColor').onclick = () => { $('#colorRows').insertAdjacentHTML('beforeend', colorRow({ name: '', hex: '#888888' })); };
    $('#colorRows').onclick = ev => { const b = ev.target.closest('[data-rmc]'); if (b && $$('.color-row').length > 1) b.closest('.color-row').remove(); };
    f.onsubmit = ev => {
      ev.preventDefault();
      const colors = $$('.color-row').map(r => ({ name: $('input[type=text]', r).value.trim(), hex: $('input[type=color]', r).value })).filter(c => c.name);
      const sizes = f.sizes.value.split(/[,،]/).map(x => x.trim()).filter(Boolean);
      if (!colors.length || !sizes.length) { toast('لازم لون ومقاس واحد على الأقل'); return; }
      Object.assign(e, {
        name: f.name.value.trim(), cat: f.cat.value, type: f.type.value, price: Number(f.price.value), old: Number(f.old.value) || 0,
        badge: f.badge.value, sizeChart: f.sizeChart.value, sizes, colors, featured: !!f.featured.value, show: !!f.show.value,
        desc: f.desc.value.trim(), details: f.details.value.split('\n').map(x => x.trim()).filter(Boolean),
        images: f.images.value.split('\n').map(x => x.trim()).filter(x => /^https?:\/\//i.test(x)), fabric: f.fabric.value.trim(), care: f.care.value.trim(),
      });
      if (!e.featured) delete e.featured;
      const sd = f.shipDays.value.trim().match(/^(\d{1,2})\s*-\s*(\d{1,2})$/);
      if (sd && Number(sd[1]) <= Number(sd[2])) e.shipDays = `${Number(sd[1])}-${Number(sd[2])}`; else delete e.shipDays;
      const i = data.products.findIndex(x => x.id === e.id);
      if (i > -1) data.products[i] = e; else data.products.unshift(e);
      const prevSup = Object.assign({}, s, suppliers[String(e.id)] || {});
      /* every pre-existing colour row carries its original name (data-orig), so renames, deletions and re-ordering
         keep each CJ variant on the colour it really is; a link whose colour or size was removed is dropped */
      const rename = {};
      $$('.color-row').forEach(r => { const orig = r.dataset.orig, now = $('input[type=text]', r).value.trim(); if (orig && now) rename[orig] = now; });
      const remapKey = k => { const [cName, size] = k.split('|'); return rename[cName] && sizes.includes(size) ? rename[cName] + '|' + size : null; };
      if (Array.isArray(e.na)) {
        e.na = [...new Set(e.na.map(remapKey).filter(Boolean))];
        if (!e.na.length) delete e.na;
      }
      if (e.colorImages && typeof e.colorImages === 'object') {
        const imgs = {};
        Object.entries(e.colorImages).forEach(([cName, list]) => { if (rename[cName]) imgs[rename[cName]] = list; });
        e.colorImages = imgs;
        if (!Object.keys(imgs).length) delete e.colorImages;
      }
      if (prevSup.variants && typeof prevSup.variants === 'object') {
        const moved = {};
        Object.entries(prevSup.variants).forEach(([k, vid]) => { const nk = remapKey(k); if (nk) moved[nk] = vid; });
        prevSup.variants = moved;
      }
      suppliers[String(e.id)] = Object.assign(prevSup, { supplier: f.s_supplier.value.trim(), url: f.s_url.value.trim(), cost: Number(f.s_cost.value) || 0, notes: f.s_notes.value.trim() });
      markSup(e.id);
      saveDraft('انحفظ في المسودة ✓');
      syncSuppliers(true);
      closeEditor();
      renderProducts();
    };
    $$('[data-x]', card).forEach(b => { b.onclick = closeEditor; });
    $('#editor').classList.add('open'); $('#overlay').classList.add('show');
  }
  const colorRow = c => `<div class="color-row"${c.name ? ` data-orig="${esc(c.name)}"` : ''}><input class="input" type="text" value="${esc(c.name)}" placeholder="اسم اللون"><input type="color" value="${esc(c.hex)}"><button type="button" class="icon-btn" data-rmc aria-label="حذف">✕</button></div>`;
  const closeEditor = () => { $('#editor').classList.remove('open'); $('#overlay').classList.remove('show'); };

  async function loadSuppliers() {
    try {
      const s = await api('getSuppliers');
      if (!s.ok) return false;
      const merged = Object.assign({}, s.map);
      /* a local edit wins for the fields it has, but never blanks the CJ link fields the server set (e.g. a sourcing ref) */
      supDirty.forEach(id => {
        if (!suppliers[id]) return;
        const srv = s.map[id] || {}, loc = suppliers[id];
        merged[id] = Object.assign({}, srv, loc);
        ['ref', 'variants', 'logistic', 'from'].forEach(k => {
          const empty = loc[k] == null || loc[k] === '' || (k === 'variants' && !Object.keys(loc[k] || {}).length);
          if (empty && srv[k] != null) merged[id][k] = srv[k];
        });
      });
      supRemoved.forEach(id => { delete merged[id]; });
      suppliers = merged; supSynced = true; saveSupLocal();
      return true;
    } catch (e) { return false; }
  }
  /* upsert only what changed here; never push a stale full copy over the server's rows */
  async function syncSuppliers(silent) {
    if (!endpoint() || !LS.get(K.token, '')) return false;
    if (!supSynced && !(await loadSuppliers())) { if (!silent) toast('تعذر تحميل بيانات الموردين من السيرفر'); return false; }
    if (!supDirty.size && !supRemoved.size) { if (!silent) toast('كل شي متزامن ✓'); return true; }
    try {
      const map = {};
      supDirty.forEach(id => { if (suppliers[id]) map[id] = suppliers[id]; });
      const res = await api('setSuppliers', { map, remove: [...supRemoved] });
      if (res.ok) { supDirty.clear(); supRemoved.clear(); suppliers = res.map || suppliers; saveSupLocal(); }
      if (!silent) toast(res.ok ? `انحفظت بيانات الموردين في السيرفر ✓ (${res.count})` : 'ما انحفظت — ' + res.error);
      return res.ok;
    } catch (e) { if (!silent) toast('تعذر الاتصال'); return false; }
  }

  /* ═════════ SUPPLIERS ═════════ */
  function cjCell(p, s) {
    const ref = String(s.ref || '');
    if (/^cj/i.test(s.supplier || '') && ref && !/:/.test(ref)) return `<span class="ok-dot">●</span> تلقائي (${Object.keys(s.variants || {}).length} مربوط)`;
    if (ref.startsWith('sourcing:')) return '<span class="mini">CJ يدور عليه…</span>';
    if (ref.startsWith('found:')) return `<button class="copy-btn" data-relink="${esc(p.id)}" data-cjpid="${esc(ref.slice(6))}">لقوه — ربط بـ CJ</button>`;
    if (ref.startsWith('sourcing-failed:')) return `<span class="sup-missing">ما لقوه: ${esc(ref.slice(16))}</span>`;
    return '<span class="mini">—</span>';
  }
  function renderSuppliers() {
    const rows = data.products.map(p => {
      const s = suppliers[String(p.id)] || {};
      const fees = r2(p.price * fee().pct / 100 + fee().fixed);
      const prof = s.cost ? r2(p.price - s.cost - fees) : null;
      return `<tr><td>${esc(p.name)}</td><td>${sar(p.price)}</td>
        <td><input data-s="${p.id}" data-k="supplier" value="${esc(s.supplier || '')}" placeholder="المورد"></td>
        <td><input data-s="${p.id}" data-k="url" value="${esc(s.url || '')}" dir="ltr" placeholder="https://"></td>
        <td style="width:110px"><input data-s="${p.id}" data-k="cost" type="number" step="0.01" value="${s.cost || ''}"></td>
        <td>${prof == null ? '—' : `<b style="color:${prof > 0 ? 'var(--ok)' : 'var(--err)'}">${sar(prof)}</b>`}</td>
        <td>${cjCell(p, s)}</td>
        <td>${s.url ? `<a class="sup-link" href="${esc(s.url)}" target="_blank" rel="noopener">فتح ↗</a>` : ''}</td></tr>`;
    }).join('');
    const unlinked = data.products.filter(p => { const s = suppliers[String(p.id)] || {}; return s.url && !/^cj/i.test(s.supplier || '') && !s.ref; });
    const linked = data.products.filter(p => (suppliers[String(p.id)] || {}).url).length;
    $('#tab-suppliers').innerHTML = `
      <div class="banner info">هنا تربطين كل منتج بصفحته عند المورد وتكلفته (مع الشحن للسعودية). هالبيانات <b>سرّية</b>: تنحفظ على جهازك وفي جدول قوقل الخاص فيك، وما تنشر بالموقع أبداً. مربوط: <b>${linked}/${data.products.length}</b></div>
      <div class="card" style="margin-bottom:1rem"><div class="row">
        <div class="field"><label>رسوم بوابة الدفع (٪)</label><input class="input" id="feePct" type="number" step="0.1" value="${fee().pct}" style="width:120px"></div>
        <div class="field"><label>رسوم ثابتة لكل عملية (ر.س)</label><input class="input" id="feeFixed" type="number" step="0.1" value="${fee().fixed}" style="width:120px"></div>
        <button class="btn btn-dark btn-sm" id="saveSup" style="align-self:flex-end">حفظ ومزامنة مع السيرفر</button>
        <button class="btn btn-ghost btn-sm" id="importSup" style="align-self:flex-end">استيراد ملف الموردين</button>
        <input type="file" id="importSupFile" accept=".json" hidden>
      </div><p class="help">الربح هنا = سعر البيع − تكلفة المورد − رسوم الدفع، وقبل تكلفة الإعلانات. الشحن اللي تدفعه الزبونة يغطي فرق الشحن.</p></div>
      <div class="card" style="margin-bottom:1rem"><h3>التشغيل التلقائي مع CJ</h3>
        <p class="help">المنتجات المربوطة بـ CJ تنطلب وتندفع من رصيد CJ وتنشحن للعميلة بدون تدخّل. المنتجات المربوطة بعلي إكسبرس توصلك برسالة تطلبها يدوي — اطلب من CJ يوفّرها (يردّون خلال يوم إلى يومين) وبعدها اضغط «ربط بـ CJ».</p>
        <div class="row"><button class="btn btn-dark btn-sm" id="cjSource" ${unlinked.length ? '' : 'disabled'}>اطلب من CJ يوفّر ${unlinked.length} منتج غير مربوط</button>
          <button class="btn btn-ghost btn-sm" id="cjSourceStatus">تحديث حالة طلبات التوفير</button><span class="mini" id="cjSourceOut"></span></div></div>
      <div style="overflow-x:auto"><table class="tbl"><thead><tr><th>المنتج</th><th>سعر البيع</th><th>المورد</th><th>رابط المنتج عند المورد</th><th>التكلفة</th><th>ربح القطعة</th><th>CJ</th><th></th></tr></thead><tbody>${rows}</tbody></table></div>`;
    $('#cjSource').onclick = async () => {
      if (!confirm(`نرسل ${unlinked.length} منتج لـ CJ يدورون عليها؟ (مجاني)`)) return;
      $('#cjSourceOut').textContent = 'جاري الإرسال…';
      let r;
      try { r = await api('cjSourcingCreate', { productIds: unlinked.map(p => String(p.id)) }); } catch (e) { r = { ok: false, error: e.message }; }
      if (!r.ok) { $('#cjSourceOut').textContent = '✗ ' + (r.error === 'cj_not_configured' ? 'أضف CJ_API_KEY في السيرفر أول' : r.error); return; }
      const sent = r.results.filter(x => x.sourcingId).length;
      await loadSuppliers(); renderSuppliers();
      toast(`انرسل ${sent} طلب توفير لـ CJ ✓`);
    };
    $('#cjSourceStatus').onclick = async () => {
      $('#cjSourceOut').textContent = 'جاري التحديث…';
      let r;
      try { r = await api('cjSourcingStatus'); } catch (e) { r = { ok: false, error: e.message }; }
      if (!r.ok) { $('#cjSourceOut').textContent = '✗ ' + r.error; return; }
      await loadSuppliers(); renderSuppliers();
      toast(`${r.results.filter(x => x.status === '3').length} لقوها · ${r.results.filter(x => x.status === '5').length} ما لقوها · الباقي قيد البحث`);
    };
    $('#tab-suppliers').onclick = ev => { const b = ev.target.closest('[data-relink]'); if (b) relinkCj(b.dataset.relink, b.dataset.cjpid, b); };
    $('#tab-suppliers').oninput = ev => {
      const i = ev.target.closest('[data-s]');
      if (!i) return;
      const id = i.dataset.s;
      suppliers[id] = Object.assign({ supplier: '', url: '', cost: 0, notes: '' }, suppliers[id] || {});
      suppliers[id][i.dataset.k] = i.dataset.k === 'cost' ? Number(i.value) || 0 : i.value.trim();
      markSup(id);
    };
    $('#importSup').onclick = () => $('#importSupFile').click();
    $('#importSupFile').onchange = ev => {
      const file = ev.target.files[0]; if (!file) return;
      file.text().then(async t => {
        const map = JSON.parse(t);
        const ids = Object.keys(map).filter(id => map[id] && typeof map[id] === 'object');
        if (!ids.length) throw new Error('empty');
        ids.forEach(id => {
          const s = map[id];
          suppliers[String(id)] = { supplier: String(s.supplier || ''), url: String(s.url || ''), cost: Number(s.cost) || 0, notes: String(s.notes || '') };
          supDirty.add(String(id)); supRemoved.delete(String(id));
        });
        saveSupLocal();
        toast(`انستورد ${ids.length} مورد ✓`);
        await syncSuppliers(true);
        renderSuppliers();
      }).catch(() => toast('الملف غير صالح'));
      ev.target.value = '';
    };
    $('#saveSup').onclick = async () => {
      LS.set(K.fee, { pct: Number($('#feePct').value) || 0, fixed: Number($('#feeFixed').value) || 0 });
      const ok = await syncSuppliers(false);
      if (!ok && !endpoint()) toast('انحفظ على جهازك ✓ (السيرفر مو مربوط)');
      renderSuppliers();
    };
  }

  /* ═════════ SETTINGS & PUBLISH ═════════ */
  function renderSettings() {
    const c = data.config, ct = c.contact || {}, lg = c.legal || {};
    const inp = (k, v, attrs) => `<input class="input" data-cfg="${k}" value="${esc(v == null ? '' : v)}" ${attrs || ''}>`;
    $('#tab-settings').innerHTML = `
      <div class="card"><h3>الربط والدفع</h3><div class="editor-grid">
        <div class="field c4"><label>رابط السيرفر (Apps Script Web App URL)</label>${inp('ordersEndpoint', c.ordersEndpoint, 'dir="ltr" placeholder="https://script.google.com/macros/s/…/exec"')}</div>
        <div class="field c2"><label>مفتاح ميسّر العام (Publishable key)</label>${inp('moyasarPublishableKey', c.moyasarPublishableKey, 'dir="ltr" placeholder="pk_live_…"')}<span class="help">المفتاح العام فقط (pk_). السري (sk_) يروح في السيرفر وما ينحط هنا أبداً.</span></div>
        <div class="field"><label>الدفع عند الاستلام</label><select class="select" data-cfg="codEnabled"><option value="" ${!c.codEnabled ? 'selected' : ''}>مقفل</option><option value="1" ${c.codEnabled ? 'selected' : ''}>مفعّل</option></select></div>
        <div class="field"><label>رسوم الدفع عند الاستلام</label>${inp('codFee', c.codFee, 'type="number"')}</div>
        <div class="field c4"><label>مفتاح Cloudflare Turnstile العام (اختياري — حماية من الطلبات الوهمية)</label>${inp('turnstileSiteKey', c.turnstileSiteKey, 'dir="ltr" placeholder="0x4AAAA…"')}<span class="help">إذا حطيتيه، حطي المفتاح السري في السيرفر باسم TURNSTILE_SECRET.</span></div>
        <div class="c4"><button class="btn btn-ghost btn-sm" id="testConn">اختبار الاتصال بالسيرفر</button> <span class="mini" id="connOut"></span></div>
      </div></div>
      <div class="card"><h3>الشحن والاستبدال</h3><div class="editor-grid">
        <div class="field"><label>رسوم الشحن</label>${inp('shippingFee', c.shippingFee, 'type="number"')}</div>
        <div class="field"><label>شحن مجاني فوق</label>${inp('freeShippingOver', c.freeShippingOver, 'type="number"')}</div>
        <div class="field"><label>مدة التوصيل (أيام)</label>${inp('shippingDays', c.shippingDays)}</div>
        <div class="field"><label>مهلة الاستبدال (أيام)</label>${inp('returnsDays', c.returnsDays, 'type="number"')}</div>
      </div></div>
      <div class="card"><h3>التواصل والبيانات النظامية</h3><div class="editor-grid">
        <div class="field c2"><label>الإيميل</label><input class="input" data-ct="email" value="${esc(ct.email)}" dir="ltr"></div>
        <div class="field c2"><label>الجوال</label><input class="input" data-ct="phone" value="${esc(ct.phone)}" dir="ltr"></div>
        <div class="field"><label>انستقرام (اسم المستخدم)</label><input class="input" data-ct="instagram" value="${esc(ct.instagram)}" dir="ltr"></div>
        <div class="field"><label>تيك توك</label><input class="input" data-ct="tiktok" value="${esc(ct.tiktok)}" dir="ltr"></div>
        <div class="field"><label>سناب</label><input class="input" data-ct="snapchat" value="${esc(ct.snapchat)}" dir="ltr"></div>
        <div class="field"><label>رقم وثيقة العمل الحر</label><input class="input" data-lg="freelanceDocNo" value="${esc(lg.freelanceDocNo)}" dir="ltr"></div>
        <div class="field c2"><label>اسم صاحب/ة المتجر (كما في الوثيقة)</label><input class="input" data-lg="ownerName" value="${esc(lg.ownerName)}"></div>
        <div class="field c2"><label>المدينة</label><input class="input" data-lg="city" value="${esc(lg.city)}"></div>
        <div class="field c2"><label>العنوان (مدينة وحي — يكفي العنوان الوطني المختصر)</label><input class="input" data-lg="address" value="${esc(lg.address)}"></div>
        <div class="field c2"><label>رابط شهادة توثيق المتجر (منصة الأعمال business.sa)</label><input class="input" data-lg="authUrl" value="${esc(lg.authUrl)}" dir="ltr" placeholder="https://"></div>
      </div><p class="help">نظام التجارة الإلكترونية يلزم المتجر يعرض الاسم والعنوان وبيانات التواصل ورقم التسجيل — تظهر تلقائياً بأسفل كل صفحة وفي السياسات. التوثيق في منصة الأعمال مجاني بوثيقة العمل الحر.</p></div>
      <div class="card"><h3>شريط الإعلانات (كل سطر رسالة)</h3><textarea class="textarea" id="annIn">${esc((c.announcements || []).join('\n'))}</textarea></div>
      <div class="card"><h3>أكواد الخصم</h3><div class="coupon-rows" id="couponRows">${(c.coupons || []).map(couponRow).join('')}</div><button class="copy-btn" id="addCoupon" style="margin-top:.5rem">＋ كود</button></div>
      <div class="card"><h3>الحفظ والنشر</h3>
        <div class="row"><button class="btn btn-dark btn-sm" id="saveCfg">حفظ في المسودة</button><button class="btn btn-plum btn-sm" id="exportJson">تنزيل products.json للنشر</button><button class="btn btn-ghost btn-sm" id="importJson">استيراد ملف</button><input type="file" id="importFile" accept=".json" hidden></div>
        <p class="help">للنشر: نزّلي الملف وأعطيه لكلود (أو ارفعيه بمكان products.json في GitHub). الزوار يشوفون التحديث خلال دقيقة إلى عشر دقايق.</p></div>`;
    $('#addCoupon').onclick = () => $('#couponRows').insertAdjacentHTML('beforeend', couponRow({ code: '', type: 'percent', value: 10, minSubtotal: 0, active: true }));
    $('#couponRows').onclick = ev => { const b = ev.target.closest('[data-rmcp]'); if (b) b.closest('.coupon-r').remove(); };
    $('#saveCfg').onclick = () => { collectSettings(); saveDraft('انحفظت الإعدادات في المسودة ✓'); };
    $('#exportJson').onclick = () => {
      collectSettings(); saveDraft();
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
      const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = 'products.json'; a.click(); URL.revokeObjectURL(a.href);
      toast('نزل products.json ✓');
    };
    $('#importJson').onclick = () => $('#importFile').click();
    $('#importFile').onchange = ev => {
      const file = ev.target.files[0]; if (!file) return;
      file.text().then(t => { const d = JSON.parse(t); if (!Array.isArray(d.products) || !d.config) throw new Error(); data = d; saveDraft('انستورد ✓'); renderSettings(); })
        .catch(() => toast('الملف غير صالح'));
    };
    $('#testConn').onclick = async () => {
      collectSettings(); saveDraft();
      const out = $('#connOut'); out.textContent = 'جاري الاختبار…';
      try {
        const ping = await api('ping');
        const adm = LS.get(K.token, '') ? await api('adminCheck') : { ok: false, error: 'اكتبي الرمز في تبويب الطلبات أولاً' };
        let cj = '';
        if (adm.ok && adm.cj) {
          const s = await api('cjStatus');
          cj = s.connected ? ` · CJ: متصل ✓ · الرصيد ${s.balanceUsd == null ? '؟' : '$' + s.balanceUsd}${s.sandbox ? ' · (وضع التجربة)' : ''}` : ` · CJ: ✗ ${esc(s.error || 'غير متصل')}`;
        } else if (adm.ok) cj = ' · CJ: غير مربوط (أضف CJ_API_KEY في السيرفر)';
        out.innerHTML = ping.ok ? `✓ السيرفر شغّال · الدفع الإلكتروني: ${ping.online ? 'مفعّل' : 'غير مفعّل (أضيفي MOYASAR_SECRET_KEY)'} · الحماية من السبام: ${ping.captcha ? 'مفعّلة' : 'غير مفعّلة'} · ${adm.ok ? `الرمز صحيح · إيميل الإشعارات: ${adm.email ? 'مفعّل' : 'غير مضبوط'}` : 'الرمز: ' + adm.error}${cj}` : '✗ ' + ping.error;
      } catch (e) { out.textContent = '✗ تعذر الاتصال — تأكدي من الرابط'; }
    };
  }
  const couponRow = c => `<div class="coupon-r"><input class="input" data-cp="code" value="${esc(c.code)}" placeholder="الكود" dir="ltr" style="text-transform:uppercase">
    <select class="select" data-cp="type"><option value="percent" ${c.type === 'percent' ? 'selected' : ''}>نسبة ٪</option><option value="fixed" ${c.type === 'fixed' ? 'selected' : ''}>مبلغ ر.س</option></select>
    <input class="input" data-cp="value" type="number" value="${esc(c.value)}" placeholder="القيمة"><input class="input" data-cp="minSubtotal" type="number" value="${esc(c.minSubtotal || 0)}" placeholder="أقل مبلغ">
    <label class="mini"><input type="checkbox" data-cp="active" ${c.active !== false ? 'checked' : ''}> فعّال</label><label class="mini"><input type="checkbox" data-cp="showOnHome" ${c.showOnHome ? 'checked' : ''}> بالرئيسية</label><label class="mini"><input type="checkbox" data-cp="firstOrderOnly" ${c.firstOrderOnly ? 'checked' : ''}> لأول طلب فقط</label>
    <button type="button" class="icon-btn" data-rmcp aria-label="حذف">✕</button></div>`;
  function collectSettings() {
    const c = data.config;
    $$('[data-cfg]').forEach(el => {
      const k = el.dataset.cfg;
      if (k === 'codEnabled') c[k] = !!el.value;
      else if (['codFee', 'shippingFee', 'freeShippingOver', 'returnsDays'].includes(k)) c[k] = Math.max(0, Number(el.value) || 0);
      else c[k] = el.value.trim();
    });
    c.contact = c.contact || {}; c.legal = c.legal || {};
    $$('[data-ct]').forEach(el => { c.contact[el.dataset.ct] = el.value.trim().replace(/^@/, ''); });
    $$('[data-lg]').forEach(el => { c.legal[el.dataset.lg] = el.value.trim(); });
    c.announcements = $('#annIn').value.split('\n').map(x => x.trim()).filter(Boolean);
    c.coupons = $$('.coupon-r').map(r => {
      const g = k => $(`[data-cp="${k}"]`, r);
      const cp = { code: g('code').value.trim().toUpperCase(), type: g('type').value, value: Number(g('value').value) || 0, minSubtotal: Number(g('minSubtotal').value) || 0, active: g('active').checked };
      if (g('showOnHome').checked) cp.showOnHome = true;
      if (g('firstOrderOnly').checked) cp.firstOrderOnly = true;
      const prev = (data.config.coupons || []).find(x => x.code === cp.code);
      if (prev && prev.headline && prev.type === cp.type && Number(prev.value) === cp.value) cp.headline = prev.headline;
      return cp;
    }).filter(cp => cp.code && cp.value > 0);
    if (c.moyasarPublishableKey && !/^pk_(live|test)_/.test(c.moyasarPublishableKey) && c.moyasarPublishableKey !== 'pk_mock') toast('تنبيه: مفتاح ميسر العام يبدأ بـ pk_live_ أو pk_test_');
    if (/^sk_/.test(c.moyasarPublishableKey)) { c.moyasarPublishableKey = ''; alert('هذا مفتاح سري (sk_) — لا تحطينه هنا أبداً! يروح في إعدادات السيرفر فقط.'); }
  }

  /* ═════════ tabs & boot ═════════ */
  function show(tab) {
    $$('.tab').forEach(t => t.classList.toggle('on', t.dataset.tab === tab));
    ['orders', 'products', 'suppliers', 'settings'].forEach(t => { $('#tab-' + t).hidden = t !== tab; });
    if (tab === 'products') renderProducts();
    if (tab === 'suppliers') renderSuppliers();
    if (tab === 'settings') renderSettings();
    history.replaceState(null, '', '#' + tab);
  }
  $('#tabs').onclick = e => { const t = e.target.closest('[data-tab]'); if (t) show(t.dataset.tab); };
  $('#overlay').onclick = closeEditor;
  document.addEventListener('keydown', e => { if (e.key === 'Escape') closeEditor(); });

  load().then(() => {
    document.body.insertAdjacentHTML('beforeend', `<datalist id="carriers">${CARRIERS.map(c => `<option value="${c}">`).join('')}</datalist>`);
    renderDraftBanner();
    renderOrdersShell();
    if (endpoint() && LS.get(K.token, '')) refreshOrders();
    const h = location.hash.slice(1);
    if (['products', 'suppliers', 'settings'].includes(h)) show(h);
  }).catch(err => { document.body.insertAdjacentHTML('afterbegin', '<p style="padding:2rem">تعذر تحميل البيانات</p>'); console.error(err); });
})();
