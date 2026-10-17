/**
 * Sora Fashion — order backend (Google Apps Script web app, V8 runtime).
 * Setup guide: backend/SETUP.md
 *
 * Script properties (Project Settings → Script properties):
 *   ADMIN_TOKEN             required. Long random secret typed into admin.html (run generateAdminToken).
 *   SITE_URL                required. Store origin, e.g. https://alwaleed305xx-ui.github.io
 *   OWNER_EMAIL             optional. Receives an email for every paid / COD order and every payment needing review.
 *   MOYASAR_SECRET_KEY      optional. sk_live_… (or sk_test_…) — enables online payment.
 *   MOYASAR_WEBHOOK_SECRET  optional. The "secret token" set on the Moyasar webhook.
 *   TURNSTILE_SECRET        optional. Cloudflare Turnstile secret — when set, every order needs a valid anti-bot token.
 *   CJ_API_KEY              optional. CJ Dropshipping API key — when set, paid orders are placed with CJ automatically,
 *                           paid from the CJ wallet balance, and tracking numbers are pulled back (run installTriggers once).
 *   CJ_SANDBOX              optional. "1" = CJ test orders (no real money, no shipping) while you try the flow.
 *   CJ_LOGISTICS            optional. Preferred CJ shipping lines in order, comma separated (default: cheapest line).
 *   CJ_MIN_BALANCE_USD      optional. Daily email when the CJ wallet drops below this (default 25).
 *
 * Prices are never trusted from the browser: every order is re-priced from the
 * published products.json, and every payment is fetched from Moyasar and checked
 * for status, amount, currency and order id before the order is marked paid.
 * A payment Moyasar has captured but that cannot be applied is always logged and
 * emailed to the owner instead of being dropped.
 */

var SHEET_ORDERS = 'Orders';
var SHEET_SUPPLIERS = 'Suppliers';
var ORDER_HEADERS = ['orderId', 'createdAt', 'status', 'payment', 'paymentId', 'name', 'phone', 'city', 'district',
  'address', 'nationalAddress', 'notes', 'items', 'subtotal', 'discount', 'coupon', 'shipping', 'codFee', 'total',
  'supplierRef', 'trackingNo', 'carrier', 'updatedAt', 'log', 'region', 'supplierStatus', 'supplierInfo', 'trackingUrl', 'nameEn'];
var SUPPLIER_HEADERS = ['productId', 'supplier', 'url', 'cost', 'notes', 'ref', 'variants', 'logistic', 'from'];
var STATUSES = ['awaiting_payment', 'new', 'paid', 'processing', 'shipped', 'delivered', 'cancelled', 'refunded'];
var PAID_LIKE = ['paid', 'processing', 'shipped', 'delivered', 'refunded'];
var STALE_DATA_ERRORS = ['coupon_invalid', 'coupon_min_not_met', 'product_unavailable', 'color_invalid', 'size_invalid', 'cod_disabled', 'online_payment_disabled'];
var ORDER_RE = /^SR\d{6}-[A-Z0-9]{5}$/;
var UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
var TZ = 'Asia/Riyadh';
var STORE_TTL = 120;
/* checkout city (Arabic) → [English city, English region] for supplier shipping labels */
var SA_CITIES = {
  'الرياض': ['Riyadh', 'Riyadh'], 'جدة': ['Jeddah', 'Makkah'], 'مكة المكرمة': ['Mecca', 'Makkah'], 'المدينة المنورة': ['Medina', 'Madinah'],
  'الدمام': ['Dammam', 'Eastern Province'], 'الخبر': ['Al Khobar', 'Eastern Province'], 'الظهران': ['Dhahran', 'Eastern Province'],
  'الأحساء': ['Al Hofuf', 'Eastern Province'], 'القطيف': ['Qatif', 'Eastern Province'], 'الجبيل': ['Jubail', 'Eastern Province'],
  'الطائف': ['Taif', 'Makkah'], 'تبوك': ['Tabuk', 'Tabuk'], 'بريدة': ['Buraidah', 'Al Qassim'], 'عنيزة': ['Unaizah', 'Al Qassim'],
  'الرس': ['Ar Rass', 'Al Qassim'], 'حائل': ['Hail', 'Hail'], 'أبها': ['Abha', 'Asir'], 'خميس مشيط': ['Khamis Mushait', 'Asir'],
  'بيشة': ['Bisha', 'Asir'], 'جازان': ['Jazan', 'Jazan'], 'نجران': ['Najran', 'Najran'], 'ينبع': ['Yanbu', 'Madinah'],
  'الباحة': ['Al Bahah', 'Al Bahah'], 'سكاكا': ['Sakaka', 'Al Jawf'], 'عرعر': ['Arar', 'Northern Borders'], 'القريات': ['Al Qurayyat', 'Al Jawf'],
  'حفر الباطن': ['Hafar Al Batin', 'Eastern Province'], 'الخرج': ['Al Kharj', 'Riyadh'], 'المجمعة': ['Al Majmaah', 'Riyadh'],
  'القنفذة': ['Al Qunfudhah', 'Makkah'],
};
var CJ_BASE = 'https://developers.cjdropshipping.com/api2.0/v1';
var FAIL_CAP = 8;
var USD_SAR = 3.75;
/* supplierStatus: '' (not sent) → queued → ordering → ordered → shipped → delivered;
   side states: awaiting_balance (CJ wallet too low, re-tried every 30 min), failed (retried with back-off, emailed after 3),
   hold (out of stock / price above what the customer paid — waits for the owner), manual (not linked to CJ) */
var SA_REGIONS = ['Riyadh', 'Makkah', 'Madinah', 'Eastern Province', 'Al Qassim', 'Asir', 'Tabuk', 'Hail', 'Northern Borders', 'Jazan', 'Najran', 'Al Bahah', 'Al Jawf'];

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
      case 'ping': return { ok: true, time: new Date().toISOString(), online: !!prop_('MOYASAR_SECRET_KEY'), captcha: !!prop_('TURNSTILE_SECRET') };
      case 'createOrder': return createOrder_(body);
      case 'verifyPayment': return verifyPayment_(body);
      case 'track': return trackOrder_(body);
      case 'moyasarWebhook': return moyasarWebhook_(body);
      case 'listOrders': requireAdmin_(body); return listOrders_(body);
      case 'updateOrder': requireAdmin_(body); return updateOrder_(body);
      case 'getSuppliers': requireAdmin_(body); return getSuppliers_();
      case 'setSuppliers': requireAdmin_(body); return setSuppliers_(body);
      case 'adminCheck': requireAdmin_(body); return { ok: true, online: !!prop_('MOYASAR_SECRET_KEY'), email: !!prop_('OWNER_EMAIL'), captcha: !!prop_('TURNSTILE_SECRET'), cj: !!prop_('CJ_API_KEY') };
      case 'fulfillOrder': requireAdmin_(body); return fulfillOrder_(str_(body.orderId, 20).toUpperCase(), 'admin', { force: body.force === true });
      case 'cjStatus': requireAdmin_(body); return cjStatus_();
      case 'cjSearch': requireAdmin_(body); return cjSearch_(body);
      case 'cjProduct': requireAdmin_(body); return cjProduct_(body);
      case 'cjSourcingCreate': requireAdmin_(body); return cjSourcingCreate_(body);
      case 'cjSourcingStatus': requireAdmin_(body); return cjSourcingStatus_();
      case 'cjMyProducts': requireAdmin_(body); return cjMyProducts_();
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
function code_(err) { return String((err && err.message) || err).replace(/^E:/, ''); }
function round2_(n) { return Math.round((Number(n) + Number.EPSILON) * 100) / 100; }
function str_(v, max) {
  var s = String(v == null ? '' : v).replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/\s+/g, ' ').trim();
  return max ? s.slice(0, max) : s;
}
function digits_(s) {
  return String(s == null ? '' : s).replace(/[\u0660-\u0669]/g, function (d) { return String(d.charCodeAt(0) - 0x0660); })
    .replace(/[\u06f0-\u06f9]/g, function (d) { return String(d.charCodeAt(0) - 0x06f0); });
}
function normPhone_(p) {
  var d = digits_(p).replace(/[^\d]/g, '');
  if (d.indexOf('00966') === 0) d = d.slice(5);
  else if (d.indexOf('966') === 0) d = d.slice(3);
  if (d.length === 9 && d.charAt(0) === '5') d = '0' + d;
  return /^05\d{8}$/.test(d) ? d : '';
}
/* Sheets are formatted as plain text ('@') so phones keep their leading zero and
   timestamps are not turned into dates. Text that would start a formula gets a
   leading space, which can never be parsed as a formula and is harmless if shown. */
function cell_(v) {
  if (v == null) return '';
  var s = String(v);
  return /^[=+\-@]/.test(s) ? ' ' + s : s;
}

function appendRow_(sh, values) {
  var row = sh.getLastRow() + 1;
  if (row > sh.getMaxRows()) sh.insertRowsAfter(sh.getMaxRows(), 50);
  sh.getRange(row, 1, 1, values.length).setNumberFormat('@').setValues([values]);
  return row;
}
function now_() { return Utilities.formatDate(new Date(), TZ, "yyyy-MM-dd'T'HH:mm:ss"); }

/* The correct token always passes; the failure counter only slows down guessing. */
function requireAdmin_(b) {
  var expected = prop_('ADMIN_TOKEN');
  if (!expected || expected.length < 16) fail_('admin_not_configured');
  var got = String(b.token || '');
  var same = got.length === expected.length;
  for (var i = 0; i < expected.length; i++) if (got.charCodeAt(i) !== expected.charCodeAt(i)) same = false;
  if (same) return;
  fail_(rateOk_('adm_fail', 30, 900) ? 'unauthorized' : 'locked_try_later');
}

/* Fixed-window counter on CacheService: the window never extends, so a busy
   counter always expires on schedule. peek=true checks without counting. */
function rateOk_(key, max, seconds, peek) {
  var cache = CacheService.getScriptCache();
  var k = ('rl_' + key + '_' + Math.floor(Date.now() / 1000 / seconds)).slice(0, 240);
  var n = Number(cache.get(k) || 0);
  if (n >= max) return false;
  if (!peek) { try { cache.put(k, String(n + 1), seconds + 5); } catch (e) { /* cache full: allow */ } }
  return true;
}

function sheet_(name, headers) {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sh = ss.getSheetByName(name);
  /* a new sheet has 26 columns; writing past getMaxColumns() throws */
  var grow = function (need) { if (sh.getMaxColumns() < need) sh.insertColumnsAfter(sh.getMaxColumns(), need - sh.getMaxColumns()); };
  if (!sh) {
    sh = ss.insertSheet(name);
    grow(headers.length);
    sh.getRange(1, 1, sh.getMaxRows(), headers.length).setNumberFormat('@');
    sh.getRange(1, 1, 1, headers.length).setValues([headers]);
    sh.setFrozenRows(1);
    return sh;
  }
  var lastCol = Math.max(1, sh.getLastColumn());
  var have = sh.getRange(1, 1, 1, lastCol).getValues()[0];
  var missing = headers.filter(function (h) { return have.indexOf(h) === -1; });
  if (missing.length) {
    var start = have.filter(String).length + 1;
    grow(start + missing.length - 1);
    sh.getRange(1, start, sh.getMaxRows(), missing.length).setNumberFormat('@');
    sh.getRange(1, start, 1, missing.length).setValues([missing]);
  }
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
  var log = (prev ? prev + '\n' : '') + now_() + ' ' + text;
  if (log.length > 40000) log = '…' + log.slice(-40000);   // a Sheets cell holds at most 50,000 characters
  writeFields_(ctx, { log: log, updatedAt: now_() });
}

function publicOrder_(o) {
  var items = [];
  try { items = JSON.parse(o.items || '[]'); } catch (e) { items = []; }
  return {
    orderId: String(o.orderId), createdAt: String(o.createdAt), status: String(o.status), payment: String(o.payment),
    paymentId: String(o.paymentId || ''), name: String(o.name), nameEn: String(o.nameEn || ''), phone: String(o.phone), city: String(o.city), region: String(o.region || ''),
    district: String(o.district), address: String(o.address), nationalAddress: String(o.nationalAddress || ''),
    notes: String(o.notes || ''), items: items, subtotal: Number(o.subtotal), discount: Number(o.discount),
    coupon: String(o.coupon || ''), shipping: Number(o.shipping), codFee: Number(o.codFee), total: Number(o.total),
    supplierRef: String(o.supplierRef || ''), trackingNo: String(o.trackingNo || ''), carrier: String(o.carrier || ''),
    trackingUrl: String(o.trackingUrl || ''), supplierStatus: String(o.supplierStatus || ''), supplierInfo: parseJson_(o.supplierInfo) || {},
    updatedAt: String(o.updatedAt || ''), log: String(o.log || ''),
  };
}

/* ───────────── store data (published products.json) ───────────── */

function fetchStore_(fresh) {
  var cache = CacheService.getScriptCache();
  if (!fresh) {
    var hit = cache.get('store_json');
    if (hit) { var cached = JSON.parse(hit); cached._fromCache = true; return cached; }
  }
  var site = prop_('SITE_URL').replace(/\/+$/, '');
  if (!site) fail_('site_url_not_configured');
  var res = UrlFetchApp.fetch(site + '/products.json?t=' + new Date().getTime(), { muteHttpExceptions: true, followRedirects: true });
  if (res.getResponseCode() !== 200) fail_('store_unavailable');
  var text = res.getContentText();
  var data = JSON.parse(text);
  if (!data || !data.config || !Array.isArray(data.products)) fail_('store_invalid');
  try { if (Utilities.newBlob(text).getBytes().length < 95000) cache.put('store_json', text, STORE_TTL); } catch (e) { /* too large: skip cache */ }
  return data;
}

/* MUST mirror Sora.totals() in assets/js/store.js */
function computeTotals_(cfg, lines, couponCode, payment) {
  var subtotal = round2_(lines.reduce(function (s, l) { return s + l.unit * l.qty; }, 0));
  var discount = 0, coupon = null;
  if (couponCode) {
    var code = String(couponCode).trim().toUpperCase();
    var c = (cfg.coupons || []).filter(function (x) { return String(x.code).toUpperCase() === code && x.active !== false; })[0];
    if (!c) fail_('coupon_invalid');
    if (subtotal < (Number(c.minSubtotal) || 0)) fail_('coupon_min_not_met');
    discount = c.type === 'percent' ? round2_(subtotal * Number(c.value) / 100) : Math.min(round2_(Number(c.value)), subtotal);
    coupon = c;
  }
  var after = round2_(subtotal - discount);
  var free = Number(cfg.freeShippingOver);
  var shipping = lines.length === 0 ? 0 : (isFinite(free) && free > 0 && after >= free ? 0 : round2_(Number(cfg.shippingFee) || 0));
  var codFee = payment === 'cod' ? round2_(Number(cfg.codFee) || 0) : 0;
  return { subtotal: subtotal, discount: discount, coupon: coupon ? coupon.code : '', couponObj: coupon, shipping: shipping, codFee: codFee, total: round2_(after + shipping + codFee) };
}

function priceWith_(store, raw, couponCode, payment) {
  var cfg = store.config || {};
  if (payment === 'cod' && !cfg.codEnabled) fail_('cod_disabled');
  if (payment === 'online' && (!prop_('MOYASAR_SECRET_KEY') || !cfg.moyasarPublishableKey)) fail_('online_payment_disabled');
  var merged = {};
  raw.forEach(function (it) {
    var id = String(it && it.id), color = str_(it && it.color, 60), size = str_(it && it.size, 30);
    var qty = Math.floor(Number(it && it.qty));
    if (!(qty >= 1 && qty <= 10)) fail_('qty_invalid');
    var p = store.products.filter(function (x) { return String(x.id) === id && x.show !== false; })[0];
    if (!p) fail_('product_unavailable');
    /* compare normalised values on both sides, then keep the product's own spelling */
    var c = (p.colors || []).filter(function (x) { return str_(x.name, 60) === color; })[0];
    if (!c) fail_('color_invalid');
    var s = (p.sizes || []).filter(function (x) { return str_(x, 30) === size; })[0];
    if (s == null) fail_('size_invalid');
    if (Array.isArray(p.na) && p.na.indexOf(c.name + '|' + s) !== -1) fail_('size_invalid');
    var key = id + '|' + c.name + '|' + s;
    if (merged[key]) merged[key].qty = Math.min(10, merged[key].qty + qty);
    else merged[key] = { id: id, name: p.name, color: c.name, size: String(s), qty: qty, unit: round2_(p.price) };
  });
  var lines = Object.keys(merged).map(function (k) { var l = merged[k]; l.line = round2_(l.unit * l.qty); return l; });
  var t = computeTotals_(cfg, lines, couponCode, payment);
  if (payment === 'online' && Math.round(t.total * 100) < 100) fail_('amount_too_small');
  return { lines: lines, totals: t };
}

/* Prices against the cached store; if the cache disagrees with what the shopper
   just saw (new coupon, price, colour, payment setting), retry once fresh. */
function priceOrder_(raw, couponCode, payment) {
  var store = fetchStore_(false);
  try {
    return priceWith_(store, raw, couponCode, payment);
  } catch (err) {
    if (!store._fromCache || STALE_DATA_ERRORS.indexOf(code_(err)) === -1) throw err;
    return priceWith_(fetchStore_(true), raw, couponCode, payment);
  }
}

function verifyHuman_(token) {
  var secret = prop_('TURNSTILE_SECRET');
  if (!secret) return;
  if (!token) fail_('captcha_required');
  var res = UrlFetchApp.fetch('https://challenges.cloudflare.com/turnstile/v0/siteverify', {
    method: 'post', muteHttpExceptions: true, payload: { secret: secret, response: String(token).slice(0, 2048) },
  });
  var j = {};
  try { j = JSON.parse(res.getContentText()); } catch (e) { j = {}; }
  if (!j.success) fail_('captcha_failed');
}

/* ───────────── public actions ───────────── */

function createOrder_(b) {
  if (b.website) fail_('rejected');                       // honeypot field
  var c = b.customer || {};
  var name = str_(c.name, 60), phone = normPhone_(c.phone), city = str_(c.city, 40), district = str_(c.district, 60);
  var address = str_(c.address, 200), national = str_(c.nationalAddress, 12).replace(/\s/g, '').toUpperCase(), notes = str_(c.notes, 300);
  var nameEn = str_(c.nameEn, 50);
  if (name.length < 3) fail_('name_invalid');
  if (!/^[A-Za-z][A-Za-z .'-]{2,49}$/.test(nameEn)) fail_('name_en_invalid');
  if (!phone) fail_('phone_invalid');
  if (city.length < 2) fail_('city_invalid');
  var region = SA_CITIES[city] ? SA_CITIES[city][1] : str_(c.region, 30);
  if (SA_REGIONS.indexOf(region) === -1) fail_('region_invalid');
  if (district.length < 2) fail_('district_invalid');
  if (address.length < 5) fail_('address_invalid');
  /* Saudi carriers must refuse parcels without a National Address (TGA, from 1 Jan 2026) */
  if (!national) fail_('national_address_required');
  if (!/^[A-Z]{4}\d{4}$/.test(national)) fail_('national_address_invalid');
  var raw = Array.isArray(b.items) ? b.items : [];
  if (!raw.length) fail_('items_invalid');
  if (raw.length > 20) fail_('too_many_items');
  var payment = b.payment === 'cod' ? 'cod' : 'online';

  /* only successful orders are counted (below), so junk requests cannot fill the limits */
  if (!rateOk_('ord_ph_' + phone, 8, 3600, true)) fail_('too_many_orders');
  if (!rateOk_('ord_all', 240, 600, true)) fail_('busy_try_again');
  verifyHuman_(b.captcha);

  var priced = priceOrder_(raw, b.coupon, payment);
  var t = priced.totals;

  var lock = LockService.getScriptLock();
  lock.waitLock(15000);
  var orderId;
  try {
    var sh = sheet_(SHEET_ORDERS, ORDER_HEADERS);
    var all = readAll_(sh);
    var H = all.headers;
    if (t.couponObj && t.couponObj.firstOrderOnly) {
      var phCol = H.indexOf('phone'), stCol = H.indexOf('status');
      var used = all.rows.some(function (r) { return String(r[phCol]) === phone && (PAID_LIKE.indexOf(String(r[stCol])) !== -1 || String(r[stCol]) === 'new'); });
      if (used) fail_('coupon_first_order_only');
    }
    var idCol = H.indexOf('orderId');
    var taken = {};
    all.rows.forEach(function (r) { taken[String(r[idCol])] = true; });
    do { orderId = newOrderId_(); } while (taken[orderId]);
    var status = payment === 'cod' ? 'new' : 'awaiting_payment';
    var rec = {
      orderId: orderId, createdAt: now_(), status: status, payment: payment, paymentId: '', name: name, nameEn: nameEn, phone: phone,
      city: city, region: region, district: district, address: address, nationalAddress: national, notes: notes, items: JSON.stringify(priced.lines),
      subtotal: t.subtotal, discount: t.discount, coupon: t.coupon, shipping: t.shipping, codFee: t.codFee, total: t.total,
      supplierRef: '', trackingNo: '', carrier: '', updatedAt: now_(), log: now_() + ' created (' + payment + ')',
    };
    appendRow_(sh, H.map(function (h) { return h in rec ? cell_(rec[h]) : ''; }));
  } finally {
    lock.releaseLock();
  }
  rateOk_('ord_ph_' + phone, 8, 3600);
  rateOk_('ord_all', 240, 600);
  if (payment === 'cod') notifyOwner_('طلب جديد (دفع عند الاستلام) ' + orderId, orderId);
  var totals = { subtotal: t.subtotal, discount: t.discount, coupon: t.coupon, shipping: t.shipping, codFee: t.codFee, total: t.total };
  return { ok: true, orderId: orderId, payment: payment, total: t.total, amount: Math.round(t.total * 100), currency: 'SAR', totals: totals };
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
  if (!rateOk_('mysr_fetch', 400, 900)) fail_('busy_try_again');
  var res = UrlFetchApp.fetch('https://api.moyasar.com/v1/payments/' + encodeURIComponent(paymentId), {
    method: 'get', muteHttpExceptions: true, headers: { Authorization: 'Basic ' + Utilities.base64Encode(key + ':') },
  });
  if (res.getResponseCode() !== 200) return null;
  return JSON.parse(res.getContentText());
}

/* money was captured but could not be applied to the order: keep a trail and tell the owner */
function flagPayment_(ctx, p, reason) {
  appendLog_(ctx, 'REVIEW payment ' + p.id + ' amount=' + p.amount + ' ' + p.currency + ' status=' + p.status + ' — ' + reason);
  notifyOwner_('دفعة تحتاج مراجعة أو استرجاع — ' + ctx.order.orderId, String(ctx.order.orderId),
    'رقم الدفعة: ' + p.id + ' · المبلغ: ' + (Number(p.amount) / 100) + ' ' + p.currency + ' · السبب: ' + reason + '. راجعي لوحة ميسّر واسترجعي المبلغ أو اعتمدي الطلب.');
}

function verifyPayment_(b) {
  var orderId = str_(b.orderId, 20).toUpperCase(), paymentId = str_(b.paymentId, 64);
  if (!UUID_RE.test(paymentId)) fail_('payment_invalid');
  var p = null;
  if (!orderId) {
    /* customer came back without the order number (new tab, cleared storage): the
       payment's own metadata says which order it belongs to */
    if (!rateOk_('vp_noid', 60, 900)) fail_('busy_try_again');
    p = fetchMoyasarPayment_(paymentId);
    if (!p || !p.metadata || !p.metadata.order_id) fail_('order_not_found');
    orderId = String(p.metadata.order_id).toUpperCase();
  }
  if (!ORDER_RE.test(orderId)) fail_('order_invalid');
  if (!rateOk_('vp_' + orderId, 30, 900)) fail_('busy_try_again');

  var ctx = findOrder_(orderId);
  if (!ctx) fail_('order_not_found');
  var o = ctx.order;
  if (PAID_LIKE.indexOf(String(o.status)) !== -1 && String(o.paymentId) === paymentId) {
    return { ok: true, status: 'paid', orderId: orderId, total: Number(o.total) };
  }
  if (String(o.payment) !== 'online') fail_('order_not_online');

  if (!p) p = fetchMoyasarPayment_(paymentId);
  if (!p) return { ok: false, status: 'not_found', orderId: orderId };
  if (p.status !== 'paid' && p.status !== 'captured') {
    return { ok: false, status: String(p.status), message: String((p.source && p.source.message) || ''), orderId: orderId };
  }
  if (!p.metadata || String(p.metadata.order_id).toUpperCase() !== orderId) fail_('payment_order_mismatch');
  if (Number(p.amount) !== Math.round(Number(o.total) * 100) || String(p.currency).toUpperCase() !== 'SAR') {
    flagPayment_(ctx, p, 'amount_mismatch (expected ' + o.total + ' SAR)');
    fail_('payment_needs_review');
  }

  var lock = LockService.getScriptLock();
  lock.waitLock(15000);
  var reinstated = false;
  try {
    var fresh = findOrder_(orderId);
    var pidCol = fresh.headers.indexOf('paymentId');
    var reused = fresh.rows.some(function (r, i) { return i + 2 !== fresh.order._row && String(r[pidCol]) === paymentId; });
    if (reused) fail_('payment_reused');
    var st = String(fresh.order.status);
    if (PAID_LIKE.indexOf(st) !== -1) {
      if (String(fresh.order.paymentId) === paymentId) return { ok: true, status: 'paid', orderId: orderId, total: Number(o.total) };
      flagPayment_(fresh, p, 'duplicate_payment — order already paid with ' + fresh.order.paymentId);
      fail_('payment_needs_review');
    }
    reinstated = st === 'cancelled';
    var paidFields = { status: 'paid', paymentId: paymentId };
    if (prop_('CJ_API_KEY') && !String(fresh.order.supplierStatus || '')) paidFields.supplierStatus = 'queued';
    writeFields_(fresh, paidFields);
    appendLog_(fresh, (reinstated ? 'REINSTATED (paid after cancel) ' : 'paid via Moyasar ') + paymentId + ' (' + ((p.source && p.source.type) || 'card') + ')');
  } finally {
    lock.releaseLock();
  }
  notifyOwner_((reinstated ? 'دفعة وصلت لطلب كان ملغي — رجع مدفوع ' : 'طلب جديد مدفوع ') + orderId, orderId);
  return { ok: true, status: 'paid', orderId: orderId, total: Number(o.total) };
}

function moyasarWebhook_(b) {
  var secret = prop_('MOYASAR_WEBHOOK_SECRET');
  if (!secret || String(b.secret_token || '') !== secret) fail_('unauthorized');
  if (b.type !== 'payment_paid' || !b.data) return { ok: true, ignored: true };
  var orderId = b.data.metadata && b.data.metadata.order_id;
  if (!orderId) return { ok: true, ignored: true };
  var r = verifyPayment_({ orderId: String(orderId), paymentId: String(b.data.id) });
  /* server-to-server, so the extra seconds don't keep a shopper waiting; the queue trigger is the safety net */
  if (r && r.ok && prop_('CJ_API_KEY')) {
    try { fulfillOrder_(String(r.orderId), 'webhook'); } catch (err) { console.error('fulfil after webhook', err); }
  }
  return r;
}

function trackOrder_(b) {
  var orderId = str_(b.orderId, 20).toUpperCase(), phone = normPhone_(b.phone);
  if (!ORDER_RE.test(orderId) || !phone) fail_('not_found');
  if (!rateOk_('tr_' + phone, 30, 900)) fail_('busy_try_again');
  var ctx = findOrder_(orderId);
  if (!ctx || String(ctx.order.phone) !== phone) fail_('not_found');
  var o = publicOrder_(ctx.order);
  return {
    ok: true, orderId: o.orderId, status: o.status, payment: o.payment, createdAt: o.createdAt, total: o.total,
    carrier: o.carrier, trackingNo: o.trackingNo, trackingUrl: /^https:\/\//.test(o.trackingUrl) ? o.trackingUrl : '', city: o.city,
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

/* prevStatus = the status the admin card showed; a mismatch means someone (or the
   payment webhook) changed the order since, so the stale edit is refused. */
function updateOrder_(b) {
  var orderId = str_(b.orderId, 20);
  var lock = LockService.getScriptLock();
  lock.waitLock(15000);
  try {
    var ctx = findOrder_(orderId);
    if (!ctx) fail_('order_not_found');
    if (b.prevStatus != null && String(ctx.order.status) !== String(b.prevStatus)) fail_('stale_order');
    var fields = {};
    if (b.status != null && String(b.status) !== String(ctx.order.status)) {
      if (STATUSES.indexOf(String(b.status)) === -1) fail_('status_invalid');
      fields.status = String(b.status);
    }
    [['supplierRef', 120], ['trackingNo', 80], ['carrier', 60]].forEach(function (f) {
      if (b[f[0]] != null && str_(b[f[0]], f[1]) !== String(ctx.order[f[0]] || '')) fields[f[0]] = str_(b[f[0]], f[1]);
    });
    var info = parseJson_(ctx.order.supplierInfo) || {};
    var cjIds = Object.keys(info.cj || {}).map(function (k) { return String(info.cj[k].orderId || ''); }).filter(String);
    var ss = String(ctx.order.supplierStatus || '');
    /* a supplier order number typed by hand means the owner bought it themselves: take it out of the CJ queue */
    if (fields.supplierRef && cjIds.indexOf(fields.supplierRef) === -1 && ['ordered', 'shipped', 'delivered'].indexOf(ss) === -1) fields.supplierStatus = 'manual';
    if (b.manualHandled && (info.manual || []).length && !info.manualHandled) {
      info.manualHandled = now_();
      fields.supplierInfo = JSON.stringify(info);
    }
    var note = str_(b.note, 200);
    if (Object.keys(fields).length || note) {
      writeFields_(ctx, fields);
      appendLog_(ctx, 'admin: ' + Object.keys(fields).map(function (k) { return k === 'supplierInfo' ? 'manual lines bought by hand' : k + '=' + fields[k]; }).join(', ') + (note ? ' — ' + note : ''));
    }
    return { ok: true, order: publicOrder_(ctx.order) };
  } finally {
    lock.releaseLock();
  }
}

function getSuppliers_() {
  var sh = sheet_(SHEET_SUPPLIERS, SUPPLIER_HEADERS);
  var all = readAll_(sh);
  var map = {};
  all.rows.forEach(function (r) {
    var o = toObj_(all.headers, r, 0);
    if (o.productId !== '' && o.productId != null) {
      map[String(o.productId)] = { supplier: String(o.supplier || ''), url: String(o.url || ''), cost: Number(o.cost) || 0, notes: String(o.notes || ''),
        ref: String(o.ref || ''), variants: parseJson_(o.variants) || {}, logistic: String(o.logistic || ''), from: String(o.from || '') };
    }
  });
  return { ok: true, map: map };
}

function parseJson_(s) {
  if (s && typeof s === 'object') return s;
  try { return JSON.parse(String(s || '')); } catch (e) { return null; }
}

/* variants: {"<colour>|<size>": "<CJ vid>"} — keys use the store's own colour and size spelling */
function cleanVariants_(v) {
  var src = parseJson_(v) || {}, out = {}, n = 0;
  Object.keys(src).forEach(function (k) {
    if (n >= 300) return;
    var key = str_(k, 100), vid = str_(src[k], 60);
    if (key.indexOf('|') > 0 && vid) { out[key] = vid; n++; }
  });
  return out;
}

/* upsert: entries in `map` are added/replaced, ids in `remove` are deleted, everything else is kept */
function setSuppliers_(b) {
  var updates = b.map || {};
  var remove = Array.isArray(b.remove) ? b.remove.map(String) : [];
  if (Object.keys(updates).length > 2000) fail_('too_many');
  var lock = LockService.getScriptLock();
  lock.waitLock(15000);
  try {
    var current = getSuppliers_().map;
    Object.keys(updates).forEach(function (id) {
      var s = updates[id] || {};
      var url = str_(s.url, 500);
      if (url && !/^https?:\/\//i.test(url)) fail_('url_invalid');
      var prev = current[str_(id, 20)] || {};
      /* the admin panel may not know the CJ fields yet: keep stored ones unless new ones are sent */
      current[str_(id, 20)] = {
        supplier: str_(s.supplier, 60), url: url, cost: round2_(Number(s.cost) || 0), notes: str_(s.notes, 300),
        ref: s.ref != null ? str_(s.ref, 60) : (prev.ref || ''),
        variants: s.variants != null ? cleanVariants_(s.variants) : (prev.variants || {}),
        logistic: s.logistic != null ? str_(s.logistic, 60) : (prev.logistic || ''),
        from: s.from != null ? str_(s.from, 4).toUpperCase() : (prev.from || ''),
      };
    });
    remove.forEach(function (id) { delete current[id]; });
    var rows = Object.keys(current).map(function (id) {
      var s = current[id];
      return [id, s.supplier, s.url, s.cost, s.notes, s.ref || '', JSON.stringify(s.variants || {}), s.logistic || '', s.from || ''].map(cell_);
    });
    var sh = sheet_(SHEET_SUPPLIERS, SUPPLIER_HEADERS);
    var last = sh.getLastRow();
    if (last > 1) sh.getRange(2, 1, last - 1, SUPPLIER_HEADERS.length).clearContent();
    if (rows.length) {
      if (rows.length + 1 > sh.getMaxRows()) sh.insertRowsAfter(sh.getMaxRows(), rows.length + 1 - sh.getMaxRows());
      sh.getRange(2, 1, rows.length, SUPPLIER_HEADERS.length).setNumberFormat('@').setValues(rows);
    }
    return { ok: true, count: rows.length, map: current };
  } finally {
    lock.releaseLock();
  }
}

/* ───────────── CJ Dropshipping: automatic supplier orders ───────────── */

/* CJ returns the same token for 24 h and tokens last ~180 days; never call CJ's logout —
   it would also kill the token the automation is using. */
function cjToken_(force) {
  var key = prop_('CJ_API_KEY');
  if (!key) fail_('cj_not_configured');
  var props = PropertiesService.getScriptProperties();
  var t = parseJson_(props.getProperty('CJ_TOKEN'));
  var fp = key.slice(-6);
  if (!force && t && t.accessToken && t.fp === fp && Date.parse(t.expires) > Date.now() + 7 * 86400000) return t.accessToken;
  var res = UrlFetchApp.fetch(CJ_BASE + '/authentication/getAccessToken', {
    method: 'post', contentType: 'application/json', muteHttpExceptions: true, payload: JSON.stringify({ apiKey: key }),
  });
  var j = parseJson_(res.getContentText()) || {};
  if (res.getResponseCode() !== 200 || Number(j.code) !== 200 || !j.data || !j.data.accessToken) {
    console.error('CJ auth failed', j.code, j.message, j.requestId);
    fail_('cj_auth_failed');
  }
  props.setProperty('CJ_TOKEN', JSON.stringify({ accessToken: j.data.accessToken, expires: j.data.accessTokenExpiryDate, openId: String(j.data.openId || ''), fp: fp }));
  return j.data.accessToken;
}

/* free CJ accounts are limited to 1 request per second */
function cjThrottle_() {
  var cache = CacheService.getScriptCache();
  var wait = Number(cache.get('cj_last') || 0) + 1100 - Date.now();
  if (wait > 0) Utilities.sleep(Math.min(wait, 2000));
  try { cache.put('cj_last', String(Date.now()), 30); } catch (e) { /* cache full */ }
}

/* {ok, http, code, data, message, requestId}; CJ's own rule: success = HTTP 200 and code 200 */
function cjCall_(method, path, body, query) {
  var parts = [];
  Object.keys(query || {}).forEach(function (k) {
    [].concat(query[k]).forEach(function (v) { if (v !== '' && v != null) parts.push(encodeURIComponent(k) + '=' + encodeURIComponent(v)); });
  });
  var url = CJ_BASE + path + (parts.length ? '?' + parts.join('&') : '');
  var forceToken = false, last = null;
  for (var attempt = 0; attempt < 3; attempt++) {
    cjThrottle_();
    /* a timeout, DNS error or failed login must come back as a result, never as a throw:
       callers hold order state in memory (e.g. a CJ order that was just paid) and must get to save it */
    var res;
    try {
      var opts = { method: method, muteHttpExceptions: true, headers: { 'CJ-Access-Token': cjToken_(forceToken) } };
      if (body != null) { opts.contentType = 'application/json'; opts.payload = JSON.stringify(body); }
      res = UrlFetchApp.fetch(url, opts);
    } catch (err) {
      var ec = code_(err);
      last = { ok: false, http: 0, code: ec === 'cj_auth_failed' ? 'auth' : ec === 'cj_not_configured' ? 'not_configured' : 'network', data: null, message: ec, requestId: '' };
      if (last.code !== 'network') break;
      Utilities.sleep(1500 * (attempt + 1));
      continue;
    }
    var http = res.getResponseCode();
    var j = parseJson_(res.getContentText()) || {};
    var code = j.code == null ? http : Number(j.code);
    last = { ok: http === 200 && code === 200, http: http, code: code, data: j.data, message: String(j.message || ''), requestId: String(j.requestId || '') };
    if (last.ok) return last;
    if ((http === 401 || code === 1600001 || code === 1600002) && !forceToken) { forceToken = true; continue; }
    if (code === 1600200 || code === 1600000 || code === 1600301 || http === 429 || http >= 500) { Utilities.sleep(1500 * (attempt + 1)); continue; }
    break;
  }
  console.warn('CJ ' + path + ' failed', last.code, last.message, last.requestId);
  return last;
}

function cjAddress_(o) {
  var c = SA_CITIES[String(o.city)];
  var d = normPhone_(o.phone);
  return {
    shippingCountryCode: 'SA', shippingCountry: 'Saudi Arabia',
    shippingProvince: String(o.region || (c ? c[1] : '')) || 'Riyadh',
    shippingCity: c ? c[0] : str_(o.city, 50), shippingCounty: str_(o.district, 50),
    shippingAddress: str_(o.address + '، ' + o.district + '، ' + o.city, 480),
    shippingAddress2: o.nationalAddress ? 'National Address: ' + String(o.nationalAddress) : '',
    shippingCustomerName: str_(o.nameEn || o.name, 50), shippingPhone: d ? '+966' + d.slice(1) : '',
  };
}

/* the product's own line, then CJ_LOGISTICS, then the cheapest line promising ≤ 25 days */
function cjPickLogistic_(options, preferred) {
  var list = (Array.isArray(options) ? options : []).filter(function (x) { return x && x.logisticName; });
  if (!list.length) return null;
  var wanted = (preferred || []).concat(prop_('CJ_LOGISTICS').split(',')).map(function (s) { return String(s).trim(); }).filter(String);
  for (var i = 0; i < wanted.length; i++) {
    var hit = list.filter(function (x) { return x.logisticName === wanted[i]; })[0];
    if (hit) return hit;
  }
  var maxDays = function (x) { var m = String(x.logisticAging || '').match(/(\d+)\s*$/); return m ? Number(m[1]) : 99; };
  var fast = list.filter(function (x) { return maxDays(x) <= 25; });
  var price = function (x) { return Math.max(Number(x.logisticPrice) || 0, Number(x.totalPostageFee) || 0); };
  return (fast.length ? fast : list).slice().sort(function (a, b) { return price(a) - price(b); })[0];
}

/* Places (and pays from the CJ wallet) the CJ part of a paid order. Safe to call again:
   our order id is CJ's orderNumber, and CJ refuses a duplicate (1603003). */
function fulfillOrder_(orderId, source, opts) {
  if (!ORDER_RE.test(orderId)) fail_('order_invalid');
  if (!prop_('CJ_API_KEY')) fail_('cj_not_configured');
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  var ctx;
  try {
    ctx = findOrder_(orderId);
    if (!ctx) fail_('order_not_found');
    var st = String(ctx.order.status), ss = String(ctx.order.supplierStatus || '');
    var info = parseJson_(ctx.order.supplierInfo) || {};
    /* an order only checked in test mode can be bought for real by the owner once test mode is off */
    var cjKeys = Object.keys(info.cj || {});
    var testOnly = cjKeys.length > 0 && cjKeys.every(function (k) { return info.cj[k].test; });
    var buyForReal = testOnly && source === 'admin' && prop_('CJ_SANDBOX') !== '1';
    if (['ordered', 'shipped', 'delivered'].indexOf(ss) !== -1 && !buyForReal) return { ok: true, already: true, supplierStatus: ss, supplierRef: String(ctx.order.supplierRef || '') };
    if (buyForReal) { info.tests = info.cj; info.cj = {}; }
    /* COD orders go to CJ only when the owner sends them (cash isn't collected yet); after that the queue may retry them */
    var cod = st === 'new' && String(ctx.order.payment) === 'cod' && (source === 'admin' || ss !== '');
    if (!(st === 'paid' || st === 'processing' || cod)) fail_('order_not_paid');
    if (ss === 'manual' && source !== 'admin') return { ok: false, supplierStatus: ss };
    if (ss === 'ordering' && source !== 'reconcile' && Date.now() - (info.startedAt || 0) < 15 * 60000) return { ok: false, busy: true, supplierStatus: ss };
    info.startedAt = Date.now();
    info.attempts = (info.attempts || 0) + 1;
    writeFields_(ctx, { supplierStatus: 'ordering', supplierInfo: JSON.stringify(info) });
  } finally {
    lock.releaseLock();
  }
  var out;
  try { out = placeCjOrders_(ctx.order, opts || {}); } catch (err) { out = { status: 'failed', error: code_(err) }; }
  return finishFulfil_(orderId, out);
}

/* the CJ order record is written the moment CJ confirms it, so nothing later in the run can lose it */
function saveCjProgress_(orderId, cj) {
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  try {
    var ctx = findOrder_(orderId);
    if (!ctx) return;
    var info = parseJson_(ctx.order.supplierInfo) || {};
    info.cj = cj;
    var refs = Object.keys(cj).map(function (k) { return cj[k].orderId; }).filter(String).join(', ');
    writeFields_(ctx, refs ? { supplierInfo: JSON.stringify(info), supplierRef: refs } : { supplierInfo: JSON.stringify(info) });
  } finally {
    lock.releaseLock();
  }
}

function placeCjOrders_(o, opts) {
  var items = parseJson_(o.items) || [];
  var sup = getSuppliers_().map;
  var info = parseJson_(o.supplierInfo) || {};
  info.cj = info.cj || {};
  info.seq = info.seq || {};
  var groups = {}, order = [], manual = [];
  items.forEach(function (l, i) {
    var s = sup[String(l.id)];
    var isCj = !!s && /^cj/i.test(String(s.supplier || ''));
    var vid = isCj ? (s.variants || {})[l.color + '|' + l.size] : '';
    if (!vid) { manual.push({ name: l.name, color: l.color, size: l.size, qty: l.qty, reason: isCj ? 'variant_unmapped' : 'not_cj', url: s ? s.url : '' }); return; }
    var from = String(s.from || 'CN').toUpperCase();
    if (!groups[from]) { groups[from] = { products: [], lines: [], logistic: [], estUsd: 0, revenueSar: 0 }; order.push(from); }
    groups[from].products.push({ vid: vid, quantity: Number(l.qty), storeLineItemId: o.orderId + '-' + (i + 1) });
    groups[from].lines.push({ vid: vid, line: l, url: s.url });
    if (s.logistic) groups[from].logistic.push(s.logistic);
    groups[from].estUsd += (Number(s.cost) || 0) / USD_SAR * Number(l.qty);
    groups[from].revenueSar += Number(l.line) || Number(l.unit) * Number(l.qty) || 0;
  });
  if (!order.length) return { status: 'manual', info: info, manual: manual };
  var numberOf = function (from, gi) {
    return String(o.orderId) + (order.length > 1 ? '-' + (gi + 1) : '') + (info.seq[from] ? '-R' + info.seq[from] : '');
  };

  /* an earlier run may have created the CJ order and died before saving it: adopt it before anything else,
     so the wallet, stock and price checks never run against our own already-paid order */
  order.forEach(function (from, gi) {
    if (info.cj[from] && info.cj[from].orderId) return;
    var ex = cjCall_('get', '/shopping/order/getOrderDetail', null, { orderId: numberOf(from, gi) });
    if (ex.ok && ex.data && ex.data.orderId) {
      info.cj[from] = { orderNumber: numberOf(from, gi), orderId: String(ex.data.orderId), logistic: String(ex.data.logisticName || ''),
        amountUsd: Number(ex.data.orderAmount) || 0, status: String(ex.data.orderStatus || ''), vids: groups[from].products.map(function (p) { return p.vid; }), adopted: true };
    }
  });
  /* lines linked to CJ after their group's CJ order was placed are not in that order */
  order.forEach(function (from) {
    var rec = info.cj[from];
    if (!rec || !rec.orderId || !Array.isArray(rec.vids)) return;
    groups[from].lines.forEach(function (x) {
      if (rec.vids.indexOf(x.vid) === -1) manual.push({ name: x.line.name, color: x.line.color, size: x.line.size, qty: x.line.qty, reason: 'not_in_cj_order', url: x.url });
    });
  });

  var sandbox = prop_('CJ_SANDBOX') === '1';
  var est = 0;
  order.forEach(function (k) { if (!(info.cj[k] && info.cj[k].orderId)) est += groups[k].estUsd; });
  /* sandbox orders never touch the wallet, so an empty wallet must not block a test */
  var bal = sandbox || est === 0 ? { ok: false } : cjCall_('get', '/shopping/pay/getBalance');
  if (bal.ok && bal.data && Number(bal.data.amount) < est * 1.1) {
    return { status: 'awaiting_balance', info: info, manual: manual, note: 'CJ balance ' + bal.data.amount + ' USD, needs about ' + round2_(est * 1.1) };
  }
  var failure = '', low = false, hold = false;
  order.forEach(function (from, gi) {
    if (info.cj[from] && info.cj[from].paid) return;
    var g = groups[from];
    var number = numberOf(from, gi);
    var rec = info.cj[from] || { orderNumber: number };
    if (!rec.orderId) {
      /* live stock + price per line: CJ does not push price changes, and money leaves the wallet on create */
      var goodsUsd = 0, problem = '';
      g.products.forEach(function (p) {
        if (problem) return;
        var v = cjCall_('get', '/product/variant/queryByVid', null, { vid: p.vid, features: 'enable_inventory' });
        if (!v.ok || !v.data) { problem = v.code === 1602002 || v.code === 1602003 ? 'variant_removed ' + p.vid : 'variant_check_failed ' + v.code; return; }
        var stock = 0;
        (v.data.inventories || []).forEach(function (inv) { if (String(inv.countryCode || from).toUpperCase() === from) stock += Number(inv.totalInventory) || 0; });
        if ((v.data.inventories || []).length && stock < p.quantity) { problem = 'out_of_stock ' + p.vid; return; }
        goodsUsd += (Number(v.data.variantSellPrice) || 0) * p.quantity;
      });
      /* a failed lookup is a network/CJ hiccup — retry it; a removed or sold-out variant needs the owner */
      if (problem) { failure = problem; if (problem.indexOf('variant_check_failed') !== 0) hold = true; return; }
      var fr = cjCall_('post', '/logistic/freightCalculate', { startCountryCode: from, endCountryCode: 'SA',
        products: g.products.map(function (p) { return { vid: p.vid, quantity: p.quantity }; }) });
      var opt = fr.ok ? cjPickLogistic_(fr.data, g.logistic) : null;
      if (!opt) { failure = 'no_shipping_line' + (fr.ok ? '' : ' (cj ' + fr.code + ')'); return; }
      /* never buy at a loss: CJ cost (goods + shipping) must stay under what the customer paid for these lines
         (their share of the order after coupon, plus the shipping fee she paid) */
      var sub = Number(o.subtotal) || 0;
      var paidSar = sub > 0 ? g.revenueSar * ((Number(o.total) || 0) - (Number(o.codFee) || 0)) / sub : g.revenueSar;
      var shipUsd = Math.max(Number(opt.logisticPrice) || 0, Number(opt.totalPostageFee) || 0);   // includes a clearance fee CJ adds
      var costSar = (goodsUsd + shipUsd) * USD_SAR;
      if (paidSar > 0 && costSar > paidSar && !opts.force) { failure = 'margin_guard cost ' + round2_(costSar) + ' SAR > paid ' + round2_(paidSar) + ' SAR'; hold = true; return; }
      if (sandbox) {
        /* test mode is a dry run: CJ honours isSandbox only on special sandbox accounts (a normal account may treat
           it as a real order), so nothing is created at CJ — stock, shipping and the price check above are real */
        info.cj[from] = { orderNumber: number, orderId: 'TEST-' + number, logistic: opt.logisticName, days: String(opt.logisticAging || ''),
          shipUsd: shipUsd, amountUsd: round2_(goodsUsd + shipUsd), status: 'TEST', test: true, paid: true,
          vids: g.products.map(function (p) { return p.vid; }) };
        saveCjProgress_(o.orderId, info.cj);
        return;
      }
      var body = Object.assign({ orderNumber: number, logisticName: opt.logisticName, fromCountryCode: from, payType: 2,
        remark: 'Sora Fashion ' + o.orderId, products: g.products }, cjAddress_(o));
      var cr = cjCall_('post', '/shopping/order/createOrderV2', body);
      if (!cr.ok && cr.code === 1603003) {
        var ex = cjCall_('get', '/shopping/order/getOrderDetail', null, { orderId: number });
        if (ex.ok && ex.data) cr = { ok: true, data: ex.data };
      }
      if (!cr.ok) {
        if (cr.code === 1604000 || cr.code === 1604001) low = true;
        else failure = 'cj ' + cr.code + ' ' + cr.message.slice(0, 120);
        return;
      }
      rec = { orderNumber: number, orderId: String(cr.data.orderId || ''), logistic: opt.logisticName, days: String(opt.logisticAging || ''),
        shipUsd: Number(opt.logisticPrice) || 0, amountUsd: Number(cr.data.orderAmount) || 0, status: String(cr.data.orderStatus || ''),
        vids: g.products.map(function (p) { return p.vid; }) };
      if (opts.force) rec.forced = now_();
      var intercepts = (cr.data.interceptOrderReasons || []).filter(function (x) { return x && (x.code || x.message); });
      if (intercepts.length) rec.intercept = intercepts.map(function (x) { return x.code + ' ' + (x.message || ''); }).join('; ').slice(0, 200);
      info.cj[from] = rec;
      saveCjProgress_(o.orderId, info.cj);
    }
    /* payType 2 should already have taken the money from the wallet — check, and pay now if not */
    var d = cjCall_('get', '/shopping/order/getOrderDetail', null, { orderId: rec.orderId || number });
    var status = d.ok && d.data ? String(d.data.orderStatus || '') : rec.status;
    if (d.ok && d.data && d.data.orderId) rec.orderId = String(d.data.orderId);
    if (['CREATED', 'IN_CART', 'UNPAID'].indexOf(status) !== -1) {
      var pay = cjPayExisting_(rec.orderId, status);
      if (!pay.ok) {
        if (pay.code === 1604000 || pay.code === 1604001) low = true;
        else { failure = 'pay ' + pay.code + ' ' + String(pay.message || '').slice(0, 80) + (rec.intercept ? ' — CJ hold: ' + rec.intercept : ''); if (rec.intercept) hold = true; }
        rec.status = status;
        return;
      }
      status = 'UNSHIPPED';
    }
    rec.status = status;
    rec.paid = ['UNSHIPPED', 'SHIPPED', 'DELIVERED'].indexOf(status) !== -1;
    if (!rec.paid && !failure) failure = 'cj order still ' + (status || 'unknown');
  });
  var done = order.every(function (k) { return info.cj[k] && info.cj[k].paid; });
  return { status: done ? 'ordered' : (hold ? 'hold' : (low ? 'awaiting_balance' : 'failed')), info: info, manual: manual, error: done ? '' : failure };
}

/* Pays a CJ order that payType 2 left unpaid, using CJ's documented step flow. */
function cjPayExisting_(cjOrderId, status) {
  if (status === 'UNPAID') {
    var direct = cjCall_('post', '/shopping/pay/payBalance', { orderId: cjOrderId });
    if (direct.ok || direct.code === 1604000 || direct.code === 1604001) return direct;
  }
  if (status === 'CREATED') {
    var add = cjCall_('post', '/shopping/order/addCart', { cjOrderIdList: [cjOrderId] });
    if (!add.ok) return add;
  }
  var conf = cjCall_('post', '/shopping/order/addCartConfirm', { cjOrderIdList: [cjOrderId] });
  if (!conf.ok) return conf;
  var shipment = String((conf.data && conf.data.shipmentsId) || cjOrderId);
  var parent = cjCall_('post', '/shopping/order/saveGenerateParentOrder', { shipmentOrderId: shipment });
  if (!parent.ok) return parent;
  var body = { shipmentOrderId: shipment };
  if (parent.data && parent.data.payId) body.payId = String(parent.data.payId);
  return cjCall_('post', '/shopping/pay/payBalanceV2', body);
}

function finishFulfil_(orderId, out) {
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  var ctx, info, mail = null;
  try {
    ctx = findOrder_(orderId);
    var prev = parseJson_(ctx.order.supplierInfo) || {};
    info = out.info || prev;
    info.attempts = prev.attempts || info.attempts || 1;
    info.mailed = prev.mailed || {};
    if (prev.manualHandled) info.manualHandled = prev.manualHandled;
    delete info.startedAt;
    info.manual = out.manual || [];
    if (out.error || out.note) info.lastError = out.error || out.note; else delete info.lastError;
    /* consecutive real failures (a wallet wait is not a failure): back off 10 min, 20, 40 … up to 6 h */
    info.fails = out.status === 'failed' ? (prev.fails || 0) + 1 : 0;
    if (out.status === 'failed') info.nextTry = Date.now() + Math.min(6 * 3600000, 10 * 60000 * Math.pow(2, info.fails - 1));
    else if (out.status === 'awaiting_balance') info.nextTry = Date.now() + 30 * 60000;
    else delete info.nextTry;
    var refs = Object.keys(info.cj || {}).map(function (k) { return info.cj[k].orderId; }).filter(String).join(', ');
    var fields = { supplierStatus: out.status };
    if (refs) fields.supplierRef = refs;
    var st = String(ctx.order.status);
    if (out.status === 'ordered' && (st === 'paid' || st === 'new')) fields.status = 'processing';
    if (out.status === 'awaiting_balance' && !info.mailed.balance) { info.mailed.balance = now_(); mail = 'balance'; }
    if (out.status === 'failed' && info.fails === 3) mail = 'failed';
    if (out.status === 'failed' && info.fails === FAIL_CAP) mail = 'stopped';
    if (out.status === 'hold' && info.mailed.hold !== info.lastError) { info.mailed.hold = info.lastError; mail = 'hold'; }
    if (info.manual.length && !info.mailed.manual && !mail) { info.mailed.manual = now_(); mail = 'manual'; }
    var cancelled = refs && (st === 'cancelled' || st === 'refunded') && !info.mailed.cancelled;
    if (cancelled) info.mailed.cancelled = now_();
    fields.supplierInfo = JSON.stringify(info);
    writeFields_(ctx, fields);
    var line = 'supplier: ' + out.status + (refs ? ' (CJ ' + refs + ')' : '') + (info.lastError ? ' — ' + info.lastError : '') +
      (info.manual.length ? ' — ' + info.manual.length + ' line(s) not linked to CJ' : '');
    /* a wallet wait repeats every 30 min: log it once, not 48 times a day */
    if (!(out.status === 'awaiting_balance' && String(prev.lastLog || '') === line)) appendLog_(ctx, line);
    info.lastLog = line;
    writeFields_(ctx, { supplierInfo: JSON.stringify(info) });
  } finally {
    lock.releaseLock();
  }
  var manualText = info.manual.length ? ' | قطع تحتاج طلب يدوي: ' + info.manual.map(function (m) { return m.name + ' (' + m.color + ' / ' + m.size + ' × ' + m.qty + ')' + (m.url ? ' ← ' + m.url : ''); }).join('، ') : '';
  if (mail === 'balance') notifyOwner_('رصيد CJ ما يكفي — الطلب ' + orderId + ' ينتظر', orderId,
    'اشحن محفظة CJ (My CJ ← Wallet). الطلب ينرسل ويندفع تلقائياً أول ما يتوفر الرصيد — ما يحتاج تسوي شي ثاني. ' + (info.lastError || '') + manualText);
  if (mail === 'failed') notifyOwner_('تعذّر إرسال الطلب ' + orderId + ' إلى CJ', orderId,
    'فشل ' + info.fails + ' مرات ورا بعض. السبب: ' + (info.lastError || 'غير معروف') + '. النظام يعيد المحاولة تلقائياً (لين ' + FAIL_CAP + ' مرات)، ولو استمر افتح لوحة التحكم ← الطلبات ← «إرسال لـ CJ الآن».' + manualText);
  if (mail === 'stopped') notifyOwner_('توقفت المحاولات التلقائية للطلب ' + orderId, orderId,
    'فشل الإرسال لـ CJ ' + FAIL_CAP + ' مرات ورا بعض، والنظام وقّف المحاولة. السبب الأخير: ' + (info.lastError || 'غير معروف') + '. اضغط «إرسال لـ CJ الآن» من لوحة التحكم بعد ما تتأكد، أو استرجع المبلغ للعميلة.' + manualText);
  if (mail === 'hold') {
    var why = String(info.lastError || '');
    notifyOwner_('الطلب ' + orderId + ' موقوف ويحتاج قرارك', orderId, 'ما انطلب من CJ لأن: ' + why + '. ' +
      (/^margin_guard/.test(why) ? 'تكلفة CJ صارت أعلى من المبلغ اللي دفعته العميلة. إما تسترجع لها من لوحة ميسّر، أو تضغط «اشترِ رغم الخسارة» من لوحة التحكم.'
        : /^out_of_stock|^variant_removed/.test(why) ? 'القطعة خلصت أو انشالت عند CJ. استرجع للعميلة من لوحة ميسّر أو تواصل معها لبديل.'
          : 'راجع الطلب في لوحة التحكم.') + manualText);
  }
  if (mail === 'manual') notifyOwner_('قطع في الطلب ' + orderId + ' تحتاج طلب يدوي', orderId,
    info.manual.map(function (m) { return m.name + ' (' + m.color + ' / ' + m.size + ' × ' + m.qty + ')' + (m.url ? ' ← ' + m.url : ''); }).join(' | ') +
    ' — هذي القطع مو مربوطة بـ CJ، فاطلبها من رابط المورد بعنوان العميلة، وبعدها اضغط «تم الطلب اليدوي» في لوحة التحكم.');
  if (cancelled) notifyOwner_('انطلب من CJ لطلب ملغي ' + orderId, orderId,
    'الطلب انلغى أو انسترجع في المتجر، لكن فيه طلب CJ (' + refs + '). ألغه من My CJ ← Orders (أو افتح نزاع) عشان يرجع المبلغ لرصيدك.');
  return { ok: out.status === 'ordered', supplierStatus: out.status, supplierRef: String((ctx && ctx.order.supplierRef) || ''), error: info.lastError || '' };
}

/* Time trigger (every 5 min, see installTriggers): send queued orders to CJ, retry, and pull tracking. */
function processSupplierQueue() {
  if (!prop_('CJ_API_KEY')) return;
  var started = Date.now();
  var all = readAll_(sheet_(SHEET_ORDERS, ORDER_HEADERS));
  var todo = [], open = [];
  var cutoff = Utilities.formatDate(new Date(Date.now() - 60 * 86400000), TZ, "yyyy-MM-dd'T'HH:mm:ss");
  all.rows.forEach(function (r) {
    var o = toObj_(all.headers, r, 0);
    var st = String(o.status), ss = String(o.supplierStatus || ''), info = parseJson_(o.supplierInfo) || {};
    var due = !info.nextTry || Date.now() >= info.nextTry;
    /* a COD order joins the queue only after the owner sent it once (its supplierStatus is then set) */
    var live = st === 'paid' || st === 'processing' || (st === 'new' && String(o.payment) === 'cod' && ss !== '');
    if (live && due) {
      if (ss === 'queued' || ss === 'awaiting_balance' || (ss === 'failed' && (info.fails || 0) < FAIL_CAP)) todo.push([String(o.orderId), 'queue']);
      if (ss === 'ordering' && Date.now() - (info.startedAt || 0) > 15 * 60000) todo.push([String(o.orderId), 'reconcile']);
    }
    /* tracking changes slowly (and trigger time is capped at 90 min/day on a free Google account):
       before shipping every 3 h, in transit every 12 h, and not at all after 60 days */
    var every = ss === 'shipped' ? 12 * 3600000 : 3 * 3600000;
    if ((ss === 'ordered' || ss === 'shipped') && ['delivered', 'cancelled', 'refunded'].indexOf(st) === -1 &&
        String(o.createdAt) >= cutoff && Date.now() - (info.lastSync || 0) > every) open.push(o);
  });
  todo.slice(0, 8).forEach(function (t) {
    if (Date.now() - started > 200000) return;
    try { fulfillOrder_(t[0], t[1]); } catch (err) { console.error('queue ' + t[0], err); }
  });
  open.slice(0, 40).forEach(function (o) {
    if (Date.now() - started > 280000) return;
    try { syncCjOrder_(String(o.orderId), o); } catch (err) { console.error('sync ' + o.orderId, err); }
  });
}

/* CJ status → store status + tracking number (the Saudi last-mile number when CJ has it).
   `snap` is the row the queue already read, so the sheet is not read twice per order. */
function syncCjOrder_(orderId, snap) {
  var order = snap;
  if (!order) { var c0 = findOrder_(orderId); if (!c0) return; order = c0.order; }
  var ctx = { order: order };
  var info = parseJson_(ctx.order.supplierInfo) || {};
  /* test-mode orders were never created at CJ: nothing to sync */
  var parts = Object.keys(info.cj || {}).filter(function (k) { return !info.cj[k].test; });
  if (!parts.length) return;
  var shipped = 0, delivered = 0, cancelled = 0, numbers = [], carriers = [], url = '';
  parts.forEach(function (k) {
    var rec = info.cj[k];
    var d = cjCall_('get', '/shopping/order/getOrderDetail', null, { orderId: rec.orderId || rec.orderNumber });
    if (!d.ok || !d.data) return;
    rec.status = String(d.data.orderStatus || rec.status || '');
    if (d.data.trackNumber) {
      rec.trackNumber = String(d.data.trackNumber);
      rec.carrier = String(d.data.trackingProvider || d.data.logisticName || rec.logistic || '');
      rec.trackingUrl = String(d.data.trackingUrl || '');
      if (!rec.lastMile) {
        var t = cjCall_('get', '/logistic/trackInfo', null, { trackNumber: rec.trackNumber });
        var row = t.ok && Array.isArray(t.data) ? t.data[0] : null;
        if (row && row.lastTrackNumber && String(row.lastTrackNumber) !== rec.trackNumber) rec.lastMile = { no: String(row.lastTrackNumber), carrier: String(row.lastMileCarrier || '') };
      }
    }
    if (rec.status === 'SHIPPED' || rec.status === 'DELIVERED') shipped++;
    if (rec.status === 'DELIVERED') delivered++;
    if (rec.status === 'CANCELLED') cancelled++;
    var no = rec.lastMile ? rec.lastMile.no : rec.trackNumber;
    if (no) {
      numbers.push(no);
      carriers.push(rec.lastMile && rec.lastMile.carrier ? rec.lastMile.carrier : rec.carrier);
      url = url || (rec.lastMile ? 'https://t.17track.net/ar#nums=' + encodeURIComponent(no) : rec.trackingUrl);
    }
  });
  info.lastSync = Date.now();
  var fields = {}, st = String(ctx.order.status);
  var supplierStatus = delivered === parts.length ? 'delivered' : (shipped === parts.length ? 'shipped' : String(ctx.order.supplierStatus));
  if (numbers.length && numbers.join(', ') !== String(ctx.order.trackingNo || '')) {
    fields.trackingNo = numbers.join(', ');
    fields.carrier = carriers.filter(String).join(', ');
    fields.trackingUrl = /^https:\/\//.test(url) ? url : '';
  }
  var cancelMail = false;
  if (cancelled) {
    /* a cancelled CJ order no longer covers its lines: move it aside and bump the order-number suffix,
       so "send to CJ" places a fresh order instead of reporting the dead one as paid */
    info.cancelled = info.cancelled || {};
    info.seq = info.seq || {};
    parts.forEach(function (k) {
      if (info.cj[k] && info.cj[k].status === 'CANCELLED') {
        info.cancelled[k + '#' + ((info.seq[k] || 0) + 1)] = info.cj[k];
        delete info.cj[k];
        info.seq[k] = (info.seq[k] || 0) + 1;
      }
    });
    supplierStatus = 'hold';
    info.lastError = 'CJ cancelled ' + cancelled + ' part(s) of this order';
    info.mailed = info.mailed || {};
    if (info.mailed.hold !== info.lastError) { info.mailed.hold = info.lastError; cancelMail = true; }
    fields.supplierRef = Object.keys(info.cj).map(function (k) { return info.cj[k].orderId; }).filter(String).join(', ');
  }
  if (supplierStatus !== String(ctx.order.supplierStatus)) fields.supplierStatus = supplierStatus;
  if (supplierStatus === 'shipped' && (st === 'paid' || st === 'processing' || st === 'new')) fields.status = 'shipped';
  if (supplierStatus === 'delivered' && st !== 'delivered' && st !== 'refunded') fields.status = 'delivered';
  fields.supplierInfo = JSON.stringify(info);
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  try {
    var fresh = findOrder_(orderId);
    if (!fresh) return;
    /* the order may have changed since the snapshot (owner edit, a payment): only touch an order that is still ours to sync */
    if (String(fresh.order.supplierStatus || '') !== String(ctx.order.supplierStatus || '')) return;
    writeFields_(fresh, fields);
    if (fields.trackingNo || fields.status || cancelMail) appendLog_(fresh, 'CJ sync: ' + (fields.status || fresh.order.status) + (fields.trackingNo ? ' tracking ' + fields.trackingNo : '') + (cancelMail ? ' — ' + info.lastError : ''));
  } finally {
    lock.releaseLock();
  }
  if (cancelMail) notifyOwner_('CJ ألغت جزء من الطلب ' + orderId, orderId,
    'CJ ألغت الطلب عندها (غالباً نفد المخزون) والمبلغ يرجع لرصيدك في CJ. إما تضغط «إرسال لـ CJ الآن» لطلب جديد (لو رجع متوفر)، أو تسترجع للعميلة من لوحة ميسّر.');
}

/* Daily trigger: CJ revokes API access after 30 days without a transaction, and an empty wallet stops orders. */
function cjDailyCheck() {
  if (!prop_('CJ_API_KEY')) return null;
  var problems = [];
  var s = cjCall_('get', '/setting/get');
  if (!s.ok) problems.push('الاتصال مع CJ فشل (' + s.code + ' ' + s.message + '). تأكد من مفتاح API في إعدادات السكربت.');
  else if (s.data && s.data.root === 'NO_PERMISSION') problems.push('صلاحية API في CJ موقوفة (يصير هذا بعد 30 يوم بدون أي عملية). فعّلها من cjdropshipping.com/my.html#/authorize/APIStores');
  var b = cjCall_('get', '/shopping/pay/getBalance');
  var min = Number(prop_('CJ_MIN_BALANCE_USD') || 25);
  if (b.ok && b.data && Number(b.data.amount) < min) problems.push('رصيد محفظة CJ ' + b.data.amount + '$ — أقل من ' + min + '$. اشحنه من My CJ ← Wallet عشان تمشي الطلبات تلقائي.');
  /* test mode only checks orders: a real customer's paid order would wait forever */
  if (prop_('CJ_SANDBOX') === '1') {
    var all = readAll_(sheet_(SHEET_ORDERS, ORDER_HEADERS));
    var tested = all.rows.map(function (r) { return toObj_(all.headers, r, 0); })
      .filter(function (o) { return /^TEST-/.test(String(o.supplierRef || '')) && ['processing', 'paid'].indexOf(String(o.status)) !== -1; });
    if (tested.length) problems.push('وضع التجربة شغّال: ' + tested.length + ' طلب انفحص مع CJ بس وما انطلب فعلياً (' + tested.slice(0, 5).map(function (o) { return o.orderId; }).join('، ') +
      '). إذا خلصت التجربة: ⚙️ Sora ← «3) تشغيل CJ الحقيقي»، وبعدها من لوحة التحكم اضغط «اطلبه فعلياً من CJ» على الطلبات الحقيقية.');
  }
  if (problems.length) mailOwnerText_('تنبيه CJ', problems);
  return { ok: !problems.length, problems: problems };
}

function cjStatus_() {
  if (!prop_('CJ_API_KEY')) return { ok: true, configured: false };
  var s = cjCall_('get', '/setting/get');
  var b = cjCall_('get', '/shopping/pay/getBalance');
  return { ok: true, configured: true, sandbox: prop_('CJ_SANDBOX') === '1', connected: s.ok, root: s.ok && s.data ? String(s.data.root || '') : '',
    error: s.ok ? '' : String(s.code + ' ' + s.message), balanceUsd: b.ok && b.data ? Number(b.data.amount) : null, logistics: prop_('CJ_LOGISTICS') };
}

/* admin product search (50 CJ points per call) */
function cjSearch_(b) {
  var r = cjCall_('get', '/product/listV2', null, { keyWord: str_(b.keyword, 80), page: Math.max(1, Math.min(50, Number(b.page) || 1)), size: Math.max(10, Math.min(100, Number(b.size) || 20)) });
  if (!r.ok) fail_('cj_' + r.code);
  var content = (r.data && r.data.content) || [];
  var list = [];
  content.forEach(function (c) { (c.productList || []).forEach(function (p) { list.push({ pid: String(p.id), name: String(p.nameEn || ''), image: String(p.bigImage || ''), priceUsd: String(p.sellPrice || ''), listed: Number(p.listedNum) || 0, stock: Number(p.warehouseInventoryNum) || 0 }); }); });
  return { ok: true, total: r.data ? Number(r.data.totalRecords) || list.length : list.length, list: list };
}

/* the owner's own picks: everything added to "My Products" on cjdropshipping.com (no points cost listed).
   CJ doesn't document paging params for this call, so both spellings are sent and repeats end the loop. */
function cjMyProducts_() {
  var out = [], seen = {};
  for (var page = 1; page <= 20; page++) {
    var r = cjCall_('get', '/product/myProduct/query', null, { pageNum: page, pageSize: 100, page: page, size: 100 });
    if (!r.ok) { if (page === 1) fail_('cj_' + r.code); break; }
    var d = r.data || {}, list = d.content || d.list || [];
    var added = 0;
    list.forEach(function (p) {
      var pid = String(p.productId || p.pid || '');
      if (!pid || seen[pid]) return;
      seen[pid] = 1;
      added++;
      out.push({ pid: pid, name: String(p.nameEn || p.productNameEn || ''), sku: String(p.sku || ''), image: String(p.bigImage || p.productImage || ''),
        priceUsd: String(p.sellPrice || ''), addedAt: String(p.createAt || '') });
    });
    if (!added || page >= (Number(d.totalPages) || 1)) break;
  }
  return { ok: true, total: out.length, list: out };
}

/* admin product detail + Saudi shipping quote (≈20 CJ points) — the admin panel turns it into a product */
function cjProduct_(b) {
  var pid = str_(b.pid, 60);
  if (!pid) fail_('pid_required');
  var r = cjCall_('get', '/product/query', null, { pid: pid });
  if (!r.ok || !r.data) fail_('cj_' + (r.code || 'not_found'));
  var p = r.data;
  var imgs = [];
  try { imgs = Array.isArray(p.productImageSet) ? p.productImageSet : JSON.parse(p.productImageSet || '[]'); } catch (e) { imgs = []; }
  if (p.bigImage && imgs.indexOf(p.bigImage) === -1) imgs.unshift(p.bigImage);
  var variants = (p.variants || []).map(function (v) {
    return { vid: String(v.vid), key: String(v.variantKey || ''), name: String(v.variantNameEn || ''), image: String(v.variantImage || ''), priceUsd: Number(v.variantSellPrice) || 0 };
  });
  var shipping = [];
  if (variants.length) {
    var fr = cjCall_('post', '/logistic/freightCalculate', { startCountryCode: 'CN', endCountryCode: 'SA', products: [{ vid: variants[0].vid, quantity: 1 }] });
    /* tax/clearance are what CJ itself charges on top of postage for the route (0 when the carrier collects VAT on delivery) */
    if (fr.ok && Array.isArray(fr.data)) shipping = fr.data.map(function (x) { return { name: String(x.logisticName), usd: Number(x.logisticPrice) || 0, days: String(x.logisticAging || ''),
      taxUsd: Number(x.taxesFee) || 0, clearUsd: Number(x.clearanceOperationFee) || 0, totalUsd: Number(x.totalPostageFee) || 0 }; });
  }
  return { ok: true, product: { pid: String(p.pid || pid), name: String(p.productNameEn || ''), sku: String(p.productSku || ''), images: imgs.slice(0, 10),
    optionNames: String(p.productKeyEn || ''), weight: Number(p.productWeight) || 0, variants: variants, shipping: shipping } };
}

/* Ask CJ's agents to find products that are linked to another supplier (e.g. an AliExpress URL).
   The supplier row's ref becomes "sourcing:<id>" until CJ answers (usually 1-2 days). */
function cjSourcingCreate_(b) {
  var ids = (Array.isArray(b.productIds) ? b.productIds : []).map(String).slice(0, 40);
  var store = fetchStore_(true), sup = getSuppliers_().map, updates = {}, out = [];
  ids.forEach(function (id) {
    var p = store.products.filter(function (x) { return String(x.id) === id; })[0];
    var s = sup[id];
    if (!p || !s || !s.url || /^cj/i.test(s.supplier || '') || /^sourcing:/.test(s.ref || '')) { out.push({ id: id, skipped: true }); return; }
    var title = String(s.notes || '').split('|')[0].trim() || p.name;
    var r = cjCall_('post', '/product/sourcing/create', {
      productName: title.slice(0, 200), productImage: String((p.images || [])[0] || '').slice(0, 200), productUrl: s.url.slice(0, 200),
      price: s.cost ? round2_(s.cost / USD_SAR) : undefined, remark: ('Sora Fashion product ' + id + ' — same item, colours: ' + (p.colors || []).map(function (c) { return c.name; }).join('/')).slice(0, 200),
      acceptSimilar: 0,
    });
    if (r.ok && r.data && r.data.cjSourcingId) {
      updates[id] = Object.assign({}, s, { ref: 'sourcing:' + r.data.cjSourcingId });
      out.push({ id: id, sourcingId: String(r.data.cjSourcingId) });
    } else out.push({ id: id, error: 'cj_' + r.code + ' ' + r.message });
  });
  if (Object.keys(updates).length) setSuppliers_({ map: updates });
  return { ok: true, results: out };
}

/* "sourcing:<id>" → "found:<CJ product id>" (admin then links colours/sizes) or "sourcing-failed:<reason>" */
function cjSourcingStatus_() {
  var sup = getSuppliers_().map, byId = {};
  Object.keys(sup).forEach(function (pid) { var m = /^sourcing:(.+)$/.exec(sup[pid].ref || ''); if (m) byId[m[1]] = pid; });
  var ids = Object.keys(byId), updates = {}, out = [];
  for (var i = 0; i < ids.length; i += 100) {
    var r = cjCall_('get', '/product/sourcing/queryList', null, { sourceIds: ids.slice(i, i + 100) });
    (r.ok && Array.isArray(r.data) ? r.data : []).forEach(function (x) {
      var pid = byId[String(x.sourceId)];
      if (!pid) return;
      var st = String(x.sourceStatus || '');
      if (st === '3' && (x.cjProductId || x.productId)) updates[pid] = Object.assign({}, sup[pid], { ref: 'found:' + (x.cjProductId || x.productId) });
      else if (st === '5') updates[pid] = Object.assign({}, sup[pid], { ref: 'sourcing-failed:' + str_(x.failReasonStr || x.failReason || 'not found', 80) });
      out.push({ productId: pid, status: st, text: String(x.sourceStatusStr || ''), cjProductId: String(x.cjProductId || '') });
    });
  }
  if (Object.keys(updates).length) setSuppliers_({ map: updates });
  return { ok: true, pending: ids.length, results: out, map: getSuppliers_().map };
}

/* Run once from the editor after adding CJ_API_KEY (Run → installTriggers). */
function installTriggers() {
  var mine = ['processSupplierQueue', 'cjDailyCheck', 'cleanupStaleOrders'];
  ScriptApp.getProjectTriggers().forEach(function (t) { if (mine.indexOf(t.getHandlerFunction()) !== -1) ScriptApp.deleteTrigger(t); });
  ScriptApp.newTrigger('processSupplierQueue').timeBased().everyMinutes(5).create();
  ScriptApp.newTrigger('cjDailyCheck').timeBased().everyDays(1).atHour(9).create();
  ScriptApp.newTrigger('cleanupStaleOrders').timeBased().everyDays(1).atHour(4).create();
  console.log('Triggers installed: supplier queue every 5 min, CJ check daily 9:00, stale-order cleanup daily 4:00.');
}

/* ───────────── email ───────────── */

function mailOwnerText_(subject, lines) {
  var to = prop_('OWNER_EMAIL');
  if (!to) return;
  try {
    MailApp.sendEmail({ to: to, subject: 'Sora Fashion — ' + subject,
      htmlBody: '<div dir="rtl" style="font-family:Tahoma,Arial"><h2>' + esc_(subject) + '</h2><ul>' + lines.map(function (l) { return '<li>' + esc_(l) + '</li>'; }).join('') + '</ul></div>' });
  } catch (err) {
    console.error('mail failed', err);
  }
}

function notifyOwner_(subject, orderId, extra) {
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
      '<h2>' + esc_(subject) + '</h2>' + (extra ? '<p style="background:#fff4d6;padding:10px">' + esc_(extra) + '</p>' : '') +
      '<p><b>العميلة:</b> ' + esc_(o.name) + ' — ' + esc_(o.phone) + '<br><b>العنوان:</b> ' + esc_(o.city) + '، ' + esc_(o.district) + '، ' + esc_(o.address) +
      (o.nationalAddress ? ' (العنوان الوطني ' + esc_(o.nationalAddress) + ')' : '') + '</p>' +
      '<table border="1" cellpadding="6" style="border-collapse:collapse"><tr><th>القطعة</th><th>اللون</th><th>المقاس</th><th>الكمية</th><th>المبلغ</th></tr>' + rows + '</table>' +
      '<p><b>الإجمالي:</b> ' + o.total + ' ر.س (' + (o.payment === 'cod' ? 'دفع عند الاستلام' : 'أونلاين') + ')</p>' +
      (prop_('CJ_API_KEY') ? '<p>القطع المربوطة بـ CJ تنطلب وتندفع من رصيد CJ تلقائياً، ورقم التتبّع يوصل للعميلة بنفسه. حالة الطلب في لوحة التحكم ← الطلبات.</p>'
        : '<p>افتح لوحة التحكم لطلب القطع من المورد.</p>') + '</div>';
    MailApp.sendEmail({ to: to, subject: 'Sora Fashion — ' + subject, htmlBody: html });
  } catch (err) {
    console.error('notify failed', err);
  }
}

function esc_(s) {
  return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; });
}

/* ───────────── maintenance (run from the editor or a time trigger) ───────────── */

/* Optional daily trigger: unpaid online orders older than 3 days become 'cancelled'.
   A payment that still arrives later reinstates the order (see verifyPayment_). */
function cleanupStaleOrders() {
  var lock = LockService.getScriptLock();
  lock.waitLock(30000);
  try {
    var sh = sheet_(SHEET_ORDERS, ORDER_HEADERS);
    var all = readAll_(sh);
    var H = all.headers, st = H.indexOf('status'), cr = H.indexOf('createdAt'), lg = H.indexOf('log'), up = H.indexOf('updatedAt');
    var cutoff = Utilities.formatDate(new Date(Date.now() - 3 * 86400000), TZ, "yyyy-MM-dd'T'HH:mm:ss");
    var n = 0;
    all.rows.forEach(function (r, i) {
      if (String(r[st]) === 'awaiting_payment' && String(r[cr]) < cutoff) {
        sh.getRange(i + 2, st + 1, 1, 1).setNumberFormat('@').setValues([['cancelled']]);
        sh.getRange(i + 2, lg + 1, 1, 1).setNumberFormat('@').setValues([[String(r[lg]) + '\n' + now_() + ' auto-cancelled (unpaid 3 days)']]);
        sh.getRange(i + 2, up + 1, 1, 1).setNumberFormat('@').setValues([[now_()]]);
        n++;
      }
    });
    console.log('cancelled ' + n + ' stale unpaid orders');
    return n;
  } finally {
    lock.releaseLock();
  }
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
    ADMIN_TOKEN: prop_('ADMIN_TOKEN').length >= 16 ? 'ok' : 'MISSING — run generateAdminToken',
    SITE_URL: prop_('SITE_URL') || 'MISSING',
    OWNER_EMAIL: prop_('OWNER_EMAIL') || '(optional) not set',
    MOYASAR_SECRET_KEY: prop_('MOYASAR_SECRET_KEY') ? 'set' : '(optional) not set — online payment disabled',
    MOYASAR_WEBHOOK_SECRET: prop_('MOYASAR_WEBHOOK_SECRET') ? 'set' : '(optional) not set',
    TURNSTILE_SECRET: prop_('TURNSTILE_SECRET') ? 'set — anti-bot check on' : '(optional) not set',
    CJ_API_KEY: prop_('CJ_API_KEY') ? 'set' : '(optional) not set — orders are not sent to CJ automatically',
  };
  if (prop_('CJ_API_KEY')) {
    try { var cj = cjStatus_(); report.CJ = cj.connected ? 'connected, balance ' + cj.balanceUsd + ' USD' + (cj.sandbox ? ' (SANDBOX)' : '') : 'NOT connected: ' + cj.error; } catch (e) { report.CJ = 'FAILED: ' + e.message; }
    report.triggers = ScriptApp.getProjectTriggers().map(function (t) { return t.getHandlerFunction(); }).join(', ') || 'none — run installTriggers';
  }
  try { var s = fetchStore_(true); report.products = s.products.length + ' products loaded from SITE_URL'; } catch (e) { report.products = 'FAILED: ' + e.message; }
  console.log(JSON.stringify(report, null, 2));
  return report;
}

/* ───────────── one-click setup from the Google Sheet: menu «⚙️ Sora» ───────────── */

var DEFAULT_SITE = 'https://alwaleed305xx-ui.github.io';

function onOpen() {
  SpreadsheetApp.getUi().createMenu('⚙️ Sora')
    .addItem('1) تجهيز المتجر (أول مرة)', 'soraSetup')
    .addItem('2) ربط CJ (مفتاح API)', 'soraConnectCj')
    .addItem('3) تشغيل CJ الحقيقي (إنهاء وضع التجربة)', 'soraCjLive')
    .addItem('4) ربط ميسّر (الدفع)', 'soraMoyasar')
    .addSeparator()
    .addItem('عرض رمز الدخول للوحة التحكم', 'soraShowToken')
    .addItem('فحص الإعداد', 'soraCheck')
    .addToUi();
}

function ui_() { try { return SpreadsheetApp.getUi(); } catch (e) { return null; } }
function say_(title, text) {
  console.log(title + '\n' + text);
  var u = ui_();
  if (u) u.alert(title, text, u.ButtonSet.OK);
}
function ask_(title, text) {
  var u = ui_();
  if (!u) { console.log('Run this from the sheet menu «⚙️ Sora»'); return null; }
  var r = u.prompt(title, text, u.ButtonSet.OK_CANCEL);
  return r.getSelectedButton() === u.Button.OK ? String(r.getResponseText() || '').trim() : null;
}

/* sets SITE_URL, OWNER_EMAIL, ADMIN_TOKEN and the sheets in one go, and shows the admin code */
function soraSetup() {
  var props = PropertiesService.getScriptProperties();
  if (!prop_('SITE_URL')) props.setProperty('SITE_URL', DEFAULT_SITE);
  if (!prop_('OWNER_EMAIL')) {
    try { var me = Session.getEffectiveUser().getEmail(); if (me) props.setProperty('OWNER_EMAIL', me); } catch (e) { /* no email scope */ }
  }
  var token = generateAdminToken();
  sheet_(SHEET_ORDERS, ORDER_HEADERS);
  sheet_(SHEET_SUPPLIERS, SUPPLIER_HEADERS);
  var n;
  try { n = fetchStore_(true).products.length + ' منتج ✓'; } catch (e) { n = 'تعذر قراءة المتجر (' + e.message + ')'; }
  say_('✓ المتجر جاهز', 'رمز الدخول للوحة التحكم — انسخه واحفظه:\n\n' + token +
    '\n\nإيميل الإشعارات: ' + (prop_('OWNER_EMAIL') || 'غير محدد') + '\nبيانات المتجر: ' + n +
    '\n\nتقدر تشوف الرمز مرة ثانية من القائمة: ⚙️ Sora ← عرض رمز الدخول.');
}

function soraShowToken() { say_('رمز الدخول للوحة التحكم', prop_('ADMIN_TOKEN') || 'ما فيه رمز للحين — شغّل «1) تجهيز المتجر» أول.'); }

/* saves the CJ key, starts in test mode the first time, installs the timers and checks the connection */
function soraConnectCj() {
  var key = ask_('ربط CJ', 'الصق مفتاح API من CJ\n(My CJ ← Authorization ← API ← عمود API Key):');
  if (key == null) return;
  if (key.length < 10) { say_('المفتاح ناقص', 'انسخ المفتاح كامل وحاول مرة ثانية.'); return; }
  var props = PropertiesService.getScriptProperties();
  props.setProperty('CJ_API_KEY', key);
  props.deleteProperty('CJ_TOKEN');
  if (props.getProperty('CJ_SANDBOX') === null) props.setProperty('CJ_SANDBOX', '1');
  installTriggers();
  var s = cjStatus_();
  if (!s.connected) { say_('✗ ما اتصل بـ CJ', 'السبب: ' + s.error + '\nتأكد من المفتاح (وإن تطبيق API مثبّت في CJ) وجرّب مرة ثانية.'); return; }
  say_('✓ CJ متصل', 'رصيد المحفظة: ' + s.balanceUsd + '$\n' +
    (s.sandbox ? 'الوضع الحالي: تجربة — كل طلب مدفوع ينفحص مع CJ (المخزون وخط الشحن والتكلفة) بدون ما ينطلب فعلياً: ما ينخصم شي وما ينشحن شي.\nبعد ما تجرب طلب كامل من المتجر: ⚙️ Sora ← «3) تشغيل CJ الحقيقي».' : 'الوضع: حقيقي — الطلبات المدفوعة تنطلب وتنشحن تلقائياً.'));
}

function soraCjLive() {
  if (!prop_('CJ_API_KEY')) { say_('CJ مو مربوط', 'شغّل «2) ربط CJ» أول.'); return; }
  var u = ui_();
  if (u && u.alert('تشغيل CJ الحقيقي', 'من الآن كل طلب مدفوع ينطلب من CJ ويندفع من رصيد محفظتك فعلياً. نكمل؟', u.ButtonSet.YES_NO) !== u.Button.YES) return;
  PropertiesService.getScriptProperties().setProperty('CJ_SANDBOX', '0');
  installTriggers();
  var s = cjStatus_();
  say_('✓ صار حقيقي', 'الطلبات المدفوعة تنرسل لـ CJ تلقائياً خلال 5 دقائق.\nرصيد المحفظة الحالي: ' + s.balanceUsd + '$');
}

/* stores Moyasar's secret key and prepares the webhook secret + URL to paste into Moyasar */
function soraMoyasar() {
  var sk = ask_('ربط ميسّر', 'الصق المفتاح السري من لوحة ميسّر (يبدأ بـ sk_live_ أو sk_test_):');
  if (sk == null) return;
  if (!/^sk_(live|test)_/.test(sk)) { say_('مفتاح غير صحيح', 'لازم يبدأ بـ sk_live_ أو sk_test_ (المفتاح العام pk_ يروح في لوحة التحكم، مو هنا).'); return; }
  var props = PropertiesService.getScriptProperties();
  props.setProperty('MOYASAR_SECRET_KEY', sk);
  if (!prop_('MOYASAR_WEBHOOK_SECRET')) props.setProperty('MOYASAR_WEBHOOK_SECRET', (Utilities.getUuid() + Utilities.getUuid()).replace(/-/g, '').slice(0, 32));
  var url = '';
  try { url = ScriptApp.getService().getUrl() || ''; } catch (e) { url = ''; }
  say_('✓ انحفظ مفتاح ميسّر', 'في لوحة ميسّر ← Webhooks ← أضف:\n\nالرابط:\n' + (url ? url + '?action=moyasarWebhook' : '(سوّ Deploy أول ثم افتح هذي الخطوة مرة ثانية)') +
    '\n\nالحدث: payment_paid\nSecret token:\n' + prop_('MOYASAR_WEBHOOK_SECRET') + '\n\nوالمفتاح العام pk_ حطّه في لوحة التحكم ← الإعدادات والنشر.');
}

function soraCheck() {
  var r = setupCheck();
  say_('فحص الإعداد', Object.keys(r).map(function (k) { return k + ': ' + r[k]; }).join('\n'));
}
