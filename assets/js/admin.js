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
    awaiting_payment: 'بانتظار الدفع', new: 'جديد (عند الاستلام)', paid: 'مدفوع — اطلبيه من المورد', processing: 'تم الطلب من المورد',
    shipped: 'تم الشحن', delivered: 'تم التوصيل', cancelled: 'ملغي', refunded: 'مسترجع',
  };
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
    const todo = orders.filter(o => o.status === 'paid' || o.status === 'new');
    const filters = { todo: 'تحتاج طلب من المورد', processing: 'عند المورد', shipped: 'بالطريق', all: 'الكل' };
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
    return `<article class="order" id="o-${esc(o.orderId)}">
      <div class="order-head"><span class="oid">${esc(o.orderId)}</span><span class="pill st-${esc(o.status)}">${STATUS[o.status] || esc(o.status)}</span>
        <span class="mini">${esc(o.createdAt.replace('T', ' '))}</span><span class="mini">${o.payment === 'cod' ? 'عند الاستلام' : 'أونلاين'}${o.paymentId ? ' · ' + esc(o.paymentId) : ''}</span>
        <span style="flex:1"></span><b>${sar(o.total)}</b></div>
      <div class="order-body">
        <div class="kv">
          <div><b>${esc(o.name)}</b></div>
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
  function copy(text) {
    (navigator.clipboard ? navigator.clipboard.writeText(text) : Promise.reject()).then(() => toast('انسخ ✓'), () => prompt('انسخي:', text));
  }

  /* ═════════ PRODUCTS ═════════ */
  let pq = '', pcat = 'all';
  function renderProducts() {
    $('#tab-products').innerHTML = `
      <div class="row" style="margin-bottom:1rem">
        <button class="btn btn-plum btn-sm" id="addP">＋ منتج جديد</button>
        <input class="input" id="pq" placeholder="ابحثي…" value="${esc(pq)}" style="max-width:240px;min-height:40px">
        <select class="select" id="pcat" style="max-width:200px;min-height:40px"><option value="all">كل الأقسام</option>${data.cats.map(c => `<option value="${esc(c.id)}" ${pcat === c.id ? 'selected' : ''}>${esc(c.name)}</option>`).join('')}</select>
        <span class="mini" id="pcount"></span>
      </div>
      <div class="pgrid" id="pgrid"></div>`;
    renderProductGrid();
    $('#addP').onclick = () => openEditor(null);
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
            <span class="mini">${s.url ? '<span class="ok-dot">●</span> مربوط بمورد' : '<span class="warn-dot">●</span> بدون مورد'}${s.cost ? ` · تكلفة ${sar(s.cost)}` : ''}${(p.images || []).length ? ' · صور حقيقية' : ''}</span>
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
    const id = Math.max(Number(data.config.lastId) || 0, ...data.products.map(p => Number(p.id) || 0)) + 1;
    data.config.lastId = id;
    return id;
  };

  function openEditor(p) {
    const isNew = !p;
    const e = p ? JSON.parse(JSON.stringify(p)) : { id: nextId(), cat: data.cats[0].id, type: data.cats[0].type, name: '', price: 0, old: 0, badge: 'new', sizeChart: 'women', sizes: ['S', 'M', 'L', 'XL'], colors: [{ name: 'أسود', hex: '#1c1b1d' }], desc: '', details: [], fabric: '', care: '', images: [], show: true };
    const s = Object.assign({ supplier: '', url: '', cost: 0, notes: '' }, suppliers[String(e.id)] || {});
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
        <div class="field"><label>جدول المقاسات</label><select class="select" name="sizeChart">${[['women', 'ملابس نسائية'], ['sport', 'رياضي'], ['abaya', 'عبايات وإسدال (طول)'], ['onesize', 'مقاس حر']].map(([k, v]) => `<option value="${k}" ${k === e.sizeChart ? 'selected' : ''}>${v}</option>`).join('')}</select></div>
        <div class="field c2"><label>المقاسات (افصلي بفاصلة)</label><input class="input" name="sizes" value="${esc((e.sizes || []).join(', '))}" required></div>
        <div class="field"><label>مميّز بالصفحة الرئيسية</label><select class="select" name="featured"><option value="1" ${e.featured ? 'selected' : ''}>نعم</option><option value="" ${!e.featured ? 'selected' : ''}>لا</option></select></div>
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
      const i = data.products.findIndex(x => x.id === e.id);
      if (i > -1) data.products[i] = e; else data.products.unshift(e);
      suppliers[String(e.id)] = { supplier: f.s_supplier.value.trim(), url: f.s_url.value.trim(), cost: Number(f.s_cost.value) || 0, notes: f.s_notes.value.trim() };
      markSup(e.id);
      saveDraft('انحفظ في المسودة ✓');
      syncSuppliers(true);
      closeEditor();
      renderProducts();
    };
    $$('[data-x]', card).forEach(b => { b.onclick = closeEditor; });
    $('#editor').classList.add('open'); $('#overlay').classList.add('show');
  }
  const colorRow = c => `<div class="color-row"><input class="input" type="text" value="${esc(c.name)}" placeholder="اسم اللون"><input type="color" value="${esc(c.hex)}"><button type="button" class="icon-btn" data-rmc aria-label="حذف">✕</button></div>`;
  const closeEditor = () => { $('#editor').classList.remove('open'); $('#overlay').classList.remove('show'); };

  async function loadSuppliers() {
    try {
      const s = await api('getSuppliers');
      if (!s.ok) return false;
      const merged = Object.assign({}, s.map);
      supDirty.forEach(id => { if (suppliers[id]) merged[id] = suppliers[id]; });
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
        <td>${s.url ? `<a class="sup-link" href="${esc(s.url)}" target="_blank" rel="noopener">فتح ↗</a>` : ''}</td></tr>`;
    }).join('');
    const linked = data.products.filter(p => (suppliers[String(p.id)] || {}).url).length;
    $('#tab-suppliers').innerHTML = `
      <div class="banner info">هنا تربطين كل منتج بصفحته عند المورد وتكلفته (مع الشحن للسعودية). هالبيانات <b>سرّية</b>: تنحفظ على جهازك وفي جدول قوقل الخاص فيك، وما تنشر بالموقع أبداً. مربوط: <b>${linked}/${data.products.length}</b></div>
      <div class="card" style="margin-bottom:1rem"><div class="row">
        <div class="field"><label>رسوم بوابة الدفع (٪)</label><input class="input" id="feePct" type="number" step="0.1" value="${fee().pct}" style="width:120px"></div>
        <div class="field"><label>رسوم ثابتة لكل عملية (ر.س)</label><input class="input" id="feeFixed" type="number" step="0.1" value="${fee().fixed}" style="width:120px"></div>
        <button class="btn btn-dark btn-sm" id="saveSup" style="align-self:flex-end">حفظ ومزامنة مع السيرفر</button>
      </div><p class="help">الربح هنا = سعر البيع − تكلفة المورد − رسوم الدفع، وقبل تكلفة الإعلانات. الشحن اللي تدفعه الزبونة يغطي فرق الشحن.</p></div>
      <div style="overflow-x:auto"><table class="tbl"><thead><tr><th>المنتج</th><th>سعر البيع</th><th>المورد</th><th>رابط المنتج عند المورد</th><th>التكلفة</th><th>ربح القطعة</th><th></th></tr></thead><tbody>${rows}</tbody></table></div>`;
    $('#tab-suppliers').oninput = ev => {
      const i = ev.target.closest('[data-s]');
      if (!i) return;
      const id = i.dataset.s;
      suppliers[id] = Object.assign({ supplier: '', url: '', cost: 0, notes: '' }, suppliers[id] || {});
      suppliers[id][i.dataset.k] = i.dataset.k === 'cost' ? Number(i.value) || 0 : i.value.trim();
      markSup(id);
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
        out.innerHTML = ping.ok ? `✓ السيرفر شغّال · الدفع الإلكتروني: ${ping.online ? 'مفعّل' : 'غير مفعّل (أضيفي MOYASAR_SECRET_KEY)'} · الحماية من السبام: ${ping.captcha ? 'مفعّلة' : 'غير مفعّلة'} · ${adm.ok ? `الرمز صحيح · إيميل الإشعارات: ${adm.email ? 'مفعّل' : 'غير مضبوط'}` : 'الرمز: ' + adm.error}` : '✗ ' + ping.error;
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
