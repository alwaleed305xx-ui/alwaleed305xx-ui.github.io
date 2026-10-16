/**
 * Sora Fashion — order backend (Google Apps Script web app, V8 runtime).
 * Setup guide: backend/SETUP.md
 *
 * Script properties (Project Settings → Script properties):
 *   ADMIN_TOKEN             required. Long random secret typed into admin.html.
 *   SITE_URL                required. Store origin, e.g. https://alwaleed305xx-ui.github.io
 *   OWNER_EMAIL             optional. Receives an email for every paid / COD order.
 *   MOYASAR_SECRET_KEY      optional. sk_live_… (or sk_test_…) — enables online payment.
 *   MOYASAR_WEBHOOK_SECRET  optional. The "secret token" set on the Moyasar webhook.
 *
 * Prices are never trusted from the browser: every order is re-priced from the
 * published products.json, and every payment is fetched from Moyasar and checked
 * for status, amount, currency and order id before the order is marked paid.
 */

var SHEET_ORDERS = 'Orders';
var SHEET_SUPPLIERS = 'Suppliers';
var ORDER_HEADERS = ['orderId', 'createdAt', 'status', 'payment', 'paymentId', 'name', 'phone', 'city', 'district',
  'address', 'nationalAddress', 'notes', 'items', 'subtotal', 'discount', 'coupon', 'shipping', 'codFee', 'total',
  'supplierRef', 'trackingNo', 'carrier', 'updatedAt', 'log'];
var SUPPLIER_HEADERS = ['productId', 'supplier', 'url', 'cost', 'notes'];
var STATUSES = ['awaiting_payment', 'new', 'paid', 'processing', 'shipped', 'delivered', 'cancelled', 'refunded'];
var PAID_LIKE = ['paid', 'processing', 'shipped', 'delivered', 'refunded'];
var TZ = 'Asia/Riyadh';

/* ───────────── entry points ───────────── */

function doPost(e) {
  var body = {};
  try {
    body = JSON.parse((e && e.postData && e.postData.contents) || '{}');
  } catch (err) {
    return out_({ ok: false, error: 'bad_json' });
  }
  if (e && e.parameter && e.parameter.action && !body.action) body.action = e.parameter.action;
  return out_(handle_(body));
}

function doGet() {
  return out_({ ok: true, service: 'sora-backend', hint: 'POST JSON with an "action" field' });
}

function out_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}

function handle_(body) {
  try {
    switch (body.action) {
      case 'ping': return { ok: true, time: new Date().toISOString(), online: !!prop_('MOYASAR_SECRET_KEY') };
      case 'createOrder': return createOrder_(body);
      case 'verifyPayment': return verifyPayment_(body);
      case 'track': return trackOrder_(body);
      case 'moyasarWebhook': return moyasarWebhook_(body);
      case 'listOrders': requireAdmin_(body); return listOrders_(body);
      case 'updateOrder': requireAdmin_(body); return updateOrder_(body);
      case 'getSuppliers': requireAdmin_(body); return getSuppliers_();
      case 'setSuppliers': requireAdmin_(body); return setSuppliers_(body);
      case 'adminCheck': requireAdmin_(body); return { ok: true, online: !!prop_('MOYASAR_SECRET_KEY'), email: !!prop_('OWNER_EMAIL') };
      default: return { ok: false, error: 'unknown_action' };
    }
  } catch (err) {
    var msg = String((err && err.message) || err);
    if (msg.indexOf('E:') === 0) return { ok: false, error: msg.slice(2) };
    console.error(err);
    return { ok: false, error: 'server_error' };
  }
}

/* ───────────── helpers ───────────── */

function prop_(k) {
  var v = PropertiesService.getScriptProperties().getProperty(k);
  return v == null ? '' : String(v).trim();
}
function fail_(code) { throw new Error('E:' + code); }
function round2_(n) { return Math.round((Number(n) + Number.EPSILON) * 100) / 100; }
function str_(v, max) {
  var s = String(v == null ? '' : v).replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/\s+/g, ' ').trim();
  return max ? s.slice(0, max) : s;
}
function digits_(s) {
  return String(s == null ? '' : s).replace(/[٠-٩]/g, function (d) { return String(d.charCodeAt(0) - 0x0660); })
    .replace(/[۰-۹]/g, function (d) { return String(d.charCodeAt(0) - 0x06f0); });
}
function normPhone_(p) {
  var d = digits_(p).replace(/[^\d]/g, '');
  if (d.indexOf('00966') === 0) d = d.slice(5);
  else if (d.indexOf('966') === 0) d = d.slice(3);
  if (d.length === 9 && d.charAt(0) === '5') d = '0' + d;
  return /^05\d{8}$/.test(d) ? d : '';
}
/* Sheets are formatted as plain text ('@') so phones keep their leading zero and
   timestamps are not turned into dates; text that would start a formula gets an
   apostrophe so a customer cannot inject one. */
function cell_(v) {
  if (v == null) return '';
  var s = String(v);
  return /^[=+\-@]/.test(s) ? "'" + s : s;
}

function appendRow_(sh, values) {
  var row = sh.getLastRow() + 1;
  if (row > sh.getMaxRows()) sh.insertRowsAfter(sh.getMaxRows(), 50);
  sh.getRange(row, 1, 1, values.length).setNumberFormat('@').setValues([values]);
  return row;
}
function now_() { return Utilities.formatDate(new Date(), TZ, "yyyy-MM-dd'T'HH:mm:ss"); }

function requireAdmin_(b) {
  var expected = prop_('ADMIN_TOKEN');
  if (!expected || expected.length < 16) fail_('admin_not_configured');
  if (!rateOk_('adm_fail', 20, 900, true)) fail_('locked_try_later');
  var got = String(b.token || '');
  var same = got.length === expected.length;
  for (var i = 0; i < expected.length; i++) if (got.charCodeAt(i) !== expected.charCodeAt(i)) same = false;
  if (!same) { rateOk_('adm_fail', 20, 900); fail_('unauthorized'); }
}

/* rate limiter on CacheService. peek=true only checks without counting. */
function rateOk_(key, max, seconds, peek) {
  var cache = CacheService.getScriptCache();
  var n = Number(cache.get('rl_' + key) || 0);
  if (n >= max) return false;
  if (!peek) cache.put('rl_' + key, String(n + 1), seconds);
  return true;
}

function sheet_(name, headers) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sh = ss.getSheetByName(name);
  if (!sh) {
    sh = ss.insertSheet(name);
    sh.getRange(1, 1, sh.getMaxRows(), headers.length).setNumberFormat('@');
    sh.getRange(1, 1, 1, headers.length).setValues([headers]);
    sh.setFrozenRows(1);
    return sh;
  }
  var lastCol = Math.max(1, sh.getLastColumn());
  var have = sh.getRange(1, 1, 1, lastCol).getValues()[0];
  var missing = headers.filter(function (h) { return have.indexOf(h) === -1; });
  if (missing.length) sh.getRange(1, have.filter(String).length + 1, 1, missing.length).setValues([missing]);
  return sh;
}

function readAll_(sh) {
  var last = sh.getLastRow();
  if (last < 2) return { headers: sh.getRange(1, 1, 1, Math.max(1, sh.getLastColumn())).getValues()[0], rows: [] };
  var vals = sh.getRange(1, 1, last, sh.getLastColumn()).getValues();
  return { headers: vals[0], rows: vals.slice(1) };
}

function toObj_(headers, row, rowIndex) {
  var o = { _row: rowIndex };
  headers.forEach(function (h, i) { if (h) o[h] = row[i]; });
  return o;
}

function findOrder_(orderId) {
  var sh = sheet_(SHEET_ORDERS, ORDER_HEADERS);
  var all = readAll_(sh);
  var idx = all.headers.indexOf('orderId');
  for (var i = 0; i < all.rows.length; i++) {
    if (String(all.rows[i][idx]) === orderId) return { sh: sh, headers: all.headers, order: toObj_(all.headers, all.rows[i], i + 2), rows: all.rows };
  }
  return null;
}

function writeFields_(ctx, fields) {
  Object.keys(fields).forEach(function (k) {
    var col = ctx.headers.indexOf(k);
    if (col === -1) return;
    ctx.sh.getRange(ctx.order._row, col + 1, 1, 1).setNumberFormat('@').setValues([[cell_(fields[k])]]);
    ctx.order[k] = fields[k];
  });
}

function appendLog_(ctx, text) {
  var prev = String(ctx.order.log || '');
  writeFields_(ctx, { log: (prev ? prev + '\n' : '') + now_() + ' ' + text, updatedAt: now_() });
}

function publicOrder_(o) {
  var items = [];
  try { items = JSON.parse(o.items || '[]'); } catch (e) { items = []; }
  return {
    orderId: String(o.orderId), createdAt: String(o.createdAt), status: String(o.status), payment: String(o.payment),
    paymentId: String(o.paymentId || ''), name: String(o.name), phone: String(o.phone), city: String(o.city),
    district: String(o.district), address: String(o.address), nationalAddress: String(o.nationalAddress || ''),
    notes: String(o.notes || ''), items: items, subtotal: Number(o.subtotal), discount: Number(o.discount),
    coupon: String(o.coupon || ''), shipping: Number(o.shipping), codFee: Number(o.codFee), total: Number(o.total),
    supplierRef: String(o.supplierRef || ''), trackingNo: String(o.trackingNo || ''), carrier: String(o.carrier || ''),
    updatedAt: String(o.updatedAt || ''), log: String(o.log || ''),
  };
}

/* ───────────── store data (published products.json) ───────────── */

function fetchStore_() {
  var cache = CacheService.getScriptCache();
  var hit = cache.get('store_json');
  if (hit) return JSON.parse(hit);
  var site = prop_('SITE_URL').replace(/\/+$/, '');
  if (!site) fail_('site_url_not_configured');
  var res = UrlFetchApp.fetch(site + '/products.json?t=' + new Date().getTime(), { muteHttpExceptions: true, followRedirects: true });
  if (res.getResponseCode() !== 200) fail_('store_unavailable');
  var text = res.getContentText();
  var data = JSON.parse(text);
  if (!data || !data.config || !Array.isArray(data.products)) fail_('store_invalid');
  if (text.length < 95000) cache.put('store_json', text, 300);
  return data;
}

/* MUST mirror Sora.totals() in assets/js/store.js */
function computeTotals_(cfg, lines, couponCode, payment) {
  var subtotal = round2_(lines.reduce(function (s, l) { return s + l.unit * l.qty; }, 0));
  var discount = 0, coupon = '';
  if (couponCode) {
    var code = String(couponCode).trim().toUpperCase();
    var c = (cfg.coupons || []).filter(function (x) { return String(x.code).toUpperCase() === code && x.active !== false; })[0];
    if (!c) fail_('coupon_invalid');
    if (subtotal < (Number(c.minSubtotal) || 0)) fail_('coupon_min_not_met');
    discount = c.type === 'percent' ? round2_(subtotal * Number(c.value) / 100) : Math.min(round2_(Number(c.value)), subtotal);
    coupon = c.code;
  }
  var after = round2_(subtotal - discount);
  var free = Number(cfg.freeShippingOver);
  var shipping = lines.length === 0 ? 0 : (isFinite(free) && free > 0 && after >= free ? 0 : round2_(Number(cfg.shippingFee) || 0));
  var codFee = payment === 'cod' ? round2_(Number(cfg.codFee) || 0) : 0;
  return { subtotal: subtotal, discount: discount, coupon: coupon, shipping: shipping, codFee: codFee, total: round2_(after + shipping + codFee) };
}

/* ───────────── public actions ───────────── */

function createOrder_(b) {
  if (b.website) fail_('rejected');                       // honeypot field
  if (!rateOk_('ord_all', 60, 60)) fail_('busy_try_again');
  var c = b.customer || {};
  var name = str_(c.name, 60), phone = normPhone_(c.phone), city = str_(c.city, 40), district = str_(c.district, 60);
  var address = str_(c.address, 200), national = str_(c.nationalAddress, 8).toUpperCase(), notes = str_(c.notes, 300);
  if (name.length < 3) fail_('name_invalid');
  if (!phone) fail_('phone_invalid');
  if (city.length < 2) fail_('city_invalid');
  if (district.length < 2) fail_('district_invalid');
  if (address.length < 5) fail_('address_invalid');
  if (national && !/^[A-Z]{4}\d{4}$/.test(national)) fail_('national_address_invalid');
  if (!rateOk_('ord_' + phone, 6, 3600)) fail_('too_many_orders');

  var payment = b.payment === 'cod' ? 'cod' : 'online';
  var store = fetchStore_();
  var cfg = store.config || {};
  if (payment === 'cod' && !cfg.codEnabled) fail_('cod_disabled');
  if (payment === 'online' && (!prop_('MOYASAR_SECRET_KEY') || !cfg.moyasarPublishableKey)) fail_('online_payment_disabled');

  var raw = Array.isArray(b.items) ? b.items : [];
  if (!raw.length || raw.length > 20) fail_('items_invalid');
  var merged = {};
  raw.forEach(function (it) {
    var id = String(it && it.id), color = str_(it && it.color, 40), size = str_(it && it.size, 20);
    var qty = Math.floor(Number(it && it.qty));
    if (!(qty >= 1 && qty <= 10)) fail_('qty_invalid');
    var p = store.products.filter(function (x) { return String(x.id) === id && x.show !== false; })[0];
    if (!p) fail_('product_unavailable');
    if (!(p.colors || []).some(function (x) { return x.name === color; })) fail_('color_invalid');
    if ((p.sizes || []).indexOf(size) === -1) fail_('size_invalid');
    var key = id + '|' + color + '|' + size;
    if (merged[key]) merged[key].qty = Math.min(10, merged[key].qty + qty);
    else merged[key] = { id: id, name: p.name, color: color, size: size, qty: qty, unit: round2_(p.price) };
  });
  var lines = Object.keys(merged).map(function (k) { var l = merged[k]; l.line = round2_(l.unit * l.qty); return l; });
  var t = computeTotals_(cfg, lines, b.coupon, payment);
  if (payment === 'online' && Math.round(t.total * 100) < 100) fail_('amount_too_small');

  var lock = LockService.getScriptLock();
  lock.waitLock(15000);
  var orderId;
  try {
    var sh = sheet_(SHEET_ORDERS, ORDER_HEADERS);
    orderId = newOrderId_();
    var status = payment === 'cod' ? 'new' : 'awaiting_payment';
    var rec = {
      orderId: orderId, createdAt: now_(), status: status, payment: payment, paymentId: '', name: name, phone: phone,
      city: city, district: district, address: address, nationalAddress: national, notes: notes, items: JSON.stringify(lines),
      subtotal: t.subtotal, discount: t.discount, coupon: t.coupon, shipping: t.shipping, codFee: t.codFee, total: t.total,
      supplierRef: '', trackingNo: '', carrier: '', updatedAt: now_(), log: now_() + ' created (' + payment + ')',
    };
    var headers = sh.getRange(1, 1, 1, sh.getLastColumn()).getValues()[0];
    appendRow_(sh, headers.map(function (h) { return h in rec ? cell_(rec[h]) : ''; }));
  } finally {
    lock.releaseLock();
  }
  if (payment === 'cod') notifyOwner_('طلب جديد (دفع عند الاستلام) ' + orderId, orderId);
  return { ok: true, orderId: orderId, payment: payment, total: t.total, amount: Math.round(t.total * 100), currency: 'SAR', totals: t };
}

function newOrderId_() {
  var alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  var s = '';
  for (var i = 0; i < 5; i++) s += alphabet.charAt(Math.floor(Math.random() * alphabet.length));
  return 'SR' + Utilities.formatDate(new Date(), TZ, 'yyMMdd') + '-' + s;
}

function fetchMoyasarPayment_(paymentId) {
  var key = prop_('MOYASAR_SECRET_KEY');
  if (!key) fail_('online_payment_disabled');
  var res = UrlFetchApp.fetch('https://api.moyasar.com/v1/payments/' + encodeURIComponent(paymentId), {
    method: 'get', muteHttpExceptions: true, headers: { Authorization: 'Basic ' + Utilities.base64Encode(key + ':') },
  });
  if (res.getResponseCode() !== 200) return null;
  return JSON.parse(res.getContentText());
}

function verifyPayment_(b) {
  var orderId = str_(b.orderId, 20).toUpperCase(), paymentId = str_(b.paymentId, 64);
  if (!/^[A-Za-z0-9_-]{8,64}$/.test(paymentId)) fail_('payment_invalid');
  var p = null;
  if (!orderId) {
    /* customer came back without the order number (new tab, cleared storage): the
       payment's own metadata says which order it belongs to */
    if (!rateOk_('vp_pid_' + paymentId, 30, 900)) fail_('busy_try_again');
    p = fetchMoyasarPayment_(paymentId);
    if (!p || !p.metadata || !p.metadata.order_id) fail_('order_not_found');
    orderId = String(p.metadata.order_id).toUpperCase();
  }
  if (!/^SR\d{6}-[A-Z0-9]{5}$/.test(orderId)) fail_('order_invalid');
  if (!rateOk_('vp_' + orderId, 30, 900)) fail_('busy_try_again');

  var ctx = findOrder_(orderId);
  if (!ctx) fail_('order_not_found');
  var o = ctx.order;
  if (PAID_LIKE.indexOf(String(o.status)) !== -1) {
    if (String(o.paymentId) === paymentId) return { ok: true, status: 'paid', orderId: orderId, total: Number(o.total) };
    fail_('order_already_paid');
  }
  if (String(o.payment) !== 'online') fail_('order_not_online');
  if (String(o.status) === 'cancelled') fail_('order_cancelled');

  if (!p) p = fetchMoyasarPayment_(paymentId);
  if (!p) return { ok: false, status: 'not_found', orderId: orderId };
  if (p.status !== 'paid' && p.status !== 'captured') {
    return { ok: false, status: String(p.status), message: String((p.source && p.source.message) || ''), orderId: orderId };
  }
  var expected = Math.round(Number(o.total) * 100);
  if (Number(p.amount) !== expected || String(p.currency).toUpperCase() !== 'SAR') {
    appendLog_(ctx, 'payment ' + paymentId + ' mismatch amount=' + p.amount + ' ' + p.currency);
    fail_('amount_mismatch');
  }
  if (!p.metadata || String(p.metadata.order_id) !== orderId) fail_('payment_order_mismatch');

  var lock = LockService.getScriptLock();
  lock.waitLock(15000);
  try {
    var fresh = findOrder_(orderId);
    if (PAID_LIKE.indexOf(String(fresh.order.status)) !== -1) {
      if (String(fresh.order.paymentId) === paymentId) return { ok: true, status: 'paid', orderId: orderId, total: Number(o.total) };
      fail_('order_already_paid');
    }
    var pidCol = fresh.headers.indexOf('paymentId');
    var reused = fresh.rows.some(function (r, i) { return i + 2 !== fresh.order._row && String(r[pidCol]) === paymentId; });
    if (reused) fail_('payment_reused');
    writeFields_(fresh, { status: 'paid', paymentId: paymentId });
    appendLog_(fresh, 'paid via Moyasar ' + paymentId + ' (' + ((p.source && p.source.type) || 'card') + ')');
  } finally {
    lock.releaseLock();
  }
  notifyOwner_('طلب جديد مدفوع ' + orderId, orderId);
  return { ok: true, status: 'paid', orderId: orderId, total: Number(o.total) };
}

function moyasarWebhook_(b) {
  var secret = prop_('MOYASAR_WEBHOOK_SECRET');
  if (!secret || String(b.secret_token || '') !== secret) fail_('unauthorized');
  if (b.type !== 'payment_paid' || !b.data) return { ok: true, ignored: true };
  var orderId = b.data.metadata && b.data.metadata.order_id;
  if (!orderId) return { ok: true, ignored: true };
  return verifyPayment_({ orderId: String(orderId), paymentId: String(b.data.id) });
}

function trackOrder_(b) {
  var orderId = str_(b.orderId, 20).toUpperCase(), phone = normPhone_(b.phone);
  if (!/^SR\d{6}-[A-Z0-9]{5}$/.test(orderId) || !phone) fail_('not_found');
  if (!rateOk_('tr_' + phone, 30, 900)) fail_('busy_try_again');
  var ctx = findOrder_(orderId);
  if (!ctx || String(ctx.order.phone) !== phone) fail_('not_found');
  var o = publicOrder_(ctx.order);
  return {
    ok: true, orderId: o.orderId, status: o.status, payment: o.payment, createdAt: o.createdAt, total: o.total,
    carrier: o.carrier, trackingNo: o.trackingNo, city: o.city,
    items: o.items.map(function (i) { return { name: i.name, color: i.color, size: i.size, qty: i.qty }; }),
  };
}

/* ───────────── admin actions ───────────── */

function listOrders_(b) {
  var sh = sheet_(SHEET_ORDERS, ORDER_HEADERS);
  var all = readAll_(sh);
  var list = all.rows.map(function (r, i) { return publicOrder_(toObj_(all.headers, r, i + 2)); }).reverse();
  if (b.status) list = list.filter(function (o) { return o.status === b.status; });
  var limit = Math.min(1000, Math.max(1, Number(b.limit) || 300));
  return { ok: true, orders: list.slice(0, limit), total: list.length };
}

function updateOrder_(b) {
  var orderId = str_(b.orderId, 20);
  var ctx = findOrder_(orderId);
  if (!ctx) fail_('order_not_found');
  var fields = {};
  if (b.status != null) {
    if (STATUSES.indexOf(String(b.status)) === -1) fail_('status_invalid');
    fields.status = String(b.status);
  }
  if (b.supplierRef != null) fields.supplierRef = str_(b.supplierRef, 120);
  if (b.trackingNo != null) fields.trackingNo = str_(b.trackingNo, 80);
  if (b.carrier != null) fields.carrier = str_(b.carrier, 60);
  writeFields_(ctx, fields);
  var note = str_(b.note, 200);
  appendLog_(ctx, 'admin: ' + Object.keys(fields).map(function (k) { return k + '=' + fields[k]; }).join(', ') + (note ? ' — ' + note : ''));
  return { ok: true, order: publicOrder_(ctx.order) };
}

function getSuppliers_() {
  var sh = sheet_(SHEET_SUPPLIERS, SUPPLIER_HEADERS);
  var all = readAll_(sh);
  var map = {};
  all.rows.forEach(function (r) {
    var o = toObj_(all.headers, r, 0);
    if (o.productId !== '' && o.productId != null) map[String(o.productId)] = { supplier: String(o.supplier || ''), url: String(o.url || ''), cost: Number(o.cost) || 0, notes: String(o.notes || '') };
  });
  return { ok: true, map: map };
}

function setSuppliers_(b) {
  var map = b.map || {};
  var ids = Object.keys(map);
  if (ids.length > 2000) fail_('too_many');
  var rows = ids.map(function (id) {
    var s = map[id] || {};
    var url = str_(s.url, 500);
    if (url && !/^https?:\/\//i.test(url)) fail_('url_invalid');
    return [str_(id, 20), str_(s.supplier, 60), url, round2_(Number(s.cost) || 0), str_(s.notes, 300)].map(cell_);
  });
  var lock = LockService.getScriptLock();
  lock.waitLock(15000);
  try {
    var sh = sheet_(SHEET_SUPPLIERS, SUPPLIER_HEADERS);
    var last = sh.getLastRow();
    if (last > 1) sh.getRange(2, 1, last - 1, SUPPLIER_HEADERS.length).clearContent();
    if (rows.length) {
      if (rows.length + 1 > sh.getMaxRows()) sh.insertRowsAfter(sh.getMaxRows(), rows.length + 1 - sh.getMaxRows());
      sh.getRange(2, 1, rows.length, SUPPLIER_HEADERS.length).setNumberFormat('@').setValues(rows);
    }
  } finally {
    lock.releaseLock();
  }
  return { ok: true, count: rows.length };
}

/* ───────────── email ───────────── */

function notifyOwner_(subject, orderId) {
  var to = prop_('OWNER_EMAIL');
  if (!to) return;
  try {
    var ctx = findOrder_(orderId);
    if (!ctx) return;
    var o = publicOrder_(ctx.order);
    var rows = o.items.map(function (i) {
      return '<tr><td>' + esc_(i.name) + '</td><td>' + esc_(i.color) + '</td><td>' + esc_(i.size) + '</td><td>' + i.qty + '</td><td>' + i.line + '</td></tr>';
    }).join('');
    var html = '<div dir="rtl" style="font-family:Tahoma,Arial">' +
      '<h2>' + esc_(subject) + '</h2>' +
      '<p><b>العميلة:</b> ' + esc_(o.name) + ' — ' + esc_(o.phone) + '<br><b>العنوان:</b> ' + esc_(o.city) + '، ' + esc_(o.district) + '، ' + esc_(o.address) +
      (o.nationalAddress ? ' (العنوان الوطني ' + esc_(o.nationalAddress) + ')' : '') + '</p>' +
      '<table border="1" cellpadding="6" style="border-collapse:collapse"><tr><th>القطعة</th><th>اللون</th><th>المقاس</th><th>الكمية</th><th>المبلغ</th></tr>' + rows + '</table>' +
      '<p><b>الإجمالي:</b> ' + o.total + ' ر.س (' + (o.payment === 'cod' ? 'دفع عند الاستلام' : 'مدفوع أونلاين') + ')</p>' +
      '<p>افتحي لوحة التحكم لطلب القطع من المورد.</p></div>';
    MailApp.sendEmail({ to: to, subject: 'Sora Fashion — ' + subject, htmlBody: html });
  } catch (err) {
    console.error('notify failed', err);
  }
}

function esc_(s) {
  return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; });
}

/* Run once from the editor (Run → generateAdminToken): creates a strong ADMIN_TOKEN if none
   exists and prints it in the execution log so you can paste it into admin.html. */
function generateAdminToken() {
  var props = PropertiesService.getScriptProperties();
  var existing = props.getProperty('ADMIN_TOKEN');
  if (existing && existing.length >= 16) { console.log('ADMIN_TOKEN already set: ' + existing); return existing; }
  var token = (Utilities.getUuid() + Utilities.getUuid()).replace(/-/g, '').slice(0, 40);
  props.setProperty('ADMIN_TOKEN', token);
  console.log('ADMIN_TOKEN created: ' + token);
  return token;
}

/* Run once from the editor (Run → setupCheck) to create the sheets and see what is missing. */
function setupCheck() {
  sheet_(SHEET_ORDERS, ORDER_HEADERS);
  sheet_(SHEET_SUPPLIERS, SUPPLIER_HEADERS);
  var report = {
    ADMIN_TOKEN: prop_('ADMIN_TOKEN').length >= 16 ? 'ok' : 'MISSING (16+ characters)',
    SITE_URL: prop_('SITE_URL') || 'MISSING',
    OWNER_EMAIL: prop_('OWNER_EMAIL') || '(optional) not set',
    MOYASAR_SECRET_KEY: prop_('MOYASAR_SECRET_KEY') ? 'set' : '(optional) not set — online payment disabled',
    MOYASAR_WEBHOOK_SECRET: prop_('MOYASAR_WEBHOOK_SECRET') ? 'set' : '(optional) not set',
  };
  try { var s = fetchStore_(); report.products = s.products.length + ' products loaded from SITE_URL'; } catch (e) { report.products = 'FAILED: ' + e.message; }
  console.log(JSON.stringify(report, null, 2));
  return report;
}
