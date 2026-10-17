/* Sora Fashion — local backend emulator (development only).
   Runs the real backend/Code.gs in the browser against mocked Apps Script services,
   persisting "sheets" in localStorage so checkout, thanks, track and admin pages all
   share one fake database. Activated only when the API endpoint is "local" on localhost. */
(function () {
  'use strict';
  const script = document.currentScript;
  const BASE = new URL('../', script ? script.src : location.href).href;
  const K = { sheets: 'sora-emu-sheets', props: 'sora-emu-props', pays: 'sora-emu-payments', mail: 'sora-emu-mail', cj: 'sora-emu-cj' };
  const load = (k, d) => { try { return JSON.parse(localStorage.getItem(k)) || d; } catch (e) { return d; } };
  const save = (k, v) => localStorage.setItem(k, JSON.stringify(v));

  const DEFAULT_PROPS = {
    ADMIN_TOKEN: 'dev-admin-token-0123456789',
    SITE_URL: BASE.replace(/\/$/, ''),
    OWNER_EMAIL: 'owner@example.test',
    MOYASAR_SECRET_KEY: 'sk_mock_emulator',
    MOYASAR_WEBHOOK_SECRET: 'whsec_mock',
  };

  function syncGet(url) {
    const x = new XMLHttpRequest();
    x.open('GET', url, false);
    x.send();
    return { code: x.status, text: x.responseText };
  }

  /* ── spreadsheet mock ── */
  function makeSpreadsheet() {
    const db = load(K.sheets, {});
    const meta = load(K.sheets + '-meta', {});
    const persist = () => { save(K.sheets, db); save(K.sheets + '-meta', meta); };
    function sheet(name) {
      const rows = () => db[name];
      /* like Sheets: a new sheet has 26 columns and ranges past the edge throw */
      const maxCols = () => (meta[name + '#cols'] || 26);
      const self = {
        getName: () => name,
        getMaxRows: () => meta[name] || 1000,
        getMaxColumns: maxCols,
        insertRowsAfter: (after, n) => { meta[name] = (meta[name] || 1000) + n; persist(); },
        insertColumnsAfter: (after, n) => { meta[name + '#cols'] = maxCols() + n; persist(); },
        getLastRow: () => { const r = rows(); for (let i = r.length - 1; i >= 0; i--) if ((r[i] || []).some(v => v !== '' && v != null)) return i + 1; return 0; },
        getLastColumn: () => rows().reduce((m, r) => { let c = 0; (r || []).forEach((v, i) => { if (v !== '' && v != null) c = i + 1; }); return Math.max(m, c); }, 0),
        setFrozenRows: () => self,
        getRange: (r, c, nr, nc) => {
          nr = nr || 1; nc = nc || 1;
          if (c + nc - 1 > maxCols()) throw new Error('Exception: The coordinates of the range are outside the dimensions of the sheet.');
          const range = {
            getValues: () => Array.from({ length: nr }, (_, i) => Array.from({ length: nc }, (_, j) => { const row = rows()[r - 1 + i] || []; const v = row[c - 1 + j]; return v == null ? '' : v; })),
            setValues: vals => {
              vals.forEach((rv, i) => rv.forEach((v, j) => {
                const ri = r - 1 + i, ci = c - 1 + j;
                while (rows().length <= ri) rows().push([]);
                rows()[ri][ci] = v == null ? '' : String(v);   // plain-text ('@') cells keep values verbatim
              }));
              persist();
              return range;
            },
            setNumberFormat: () => range,
            clearContent: () => { for (let i = 0; i < nr; i++) for (let j = 0; j < nc; j++) { const row = rows()[r - 1 + i]; if (row) row[c - 1 + j] = ''; } persist(); return range; },
          };
          return range;
        },
      };
      return self;
    }
    return {
      getSheetByName: n => (db[n] ? sheet(n) : null),
      insertSheet: n => { db[n] = []; persist(); return sheet(n); },
    };
  }

  /* ── CJ Dropshipping API mock (only answers when CJ_API_KEY is set in the emulator props) ── */
  const CJ_KEY = 'CJ0000000@api@emulator-key';
  const cjState = () => load(K.cj, { balance: 200, orders: {}, seq: 1 });
  const cjMockVariant = vid => (/^MOCK-/.test(vid) ? { vid, variantSellPrice: 9.5, variantKey: vid.replace('MOCK-', ''), inventories: [{ countryCode: 'CN', totalInventory: vid.indexOf('SOLDOUT') !== -1 ? 0 : 400 }] } : null);
  function cjMock(url, opts) {
    const props = Object.assign({}, DEFAULT_PROPS, load(K.props, {}));
    const u = new URL(url);
    const path = u.pathname.replace('/api2.0/v1', '');
    const body = opts.payload ? JSON.parse(opts.payload) : {};
    const ok = data => resp(200, JSON.stringify({ code: 200, result: true, message: 'Success', data, requestId: 'emu-' + Date.now() }));
    const err = (code, message, http) => resp(http || 200, JSON.stringify({ code, result: false, message, data: null, requestId: 'emu-' + Date.now() }));
    if (path === '/authentication/getAccessToken') {
      return body.apiKey === props.CJ_API_KEY ? ok({ openId: 42, accessToken: 'emu-token-1', accessTokenExpiryDate: new Date(Date.now() + 180 * 86400000).toISOString(), refreshToken: 'r', refreshTokenExpiryDate: '' }) : err(1600005, 'APIkey is wrong');
    }
    if ((opts.headers || {})['CJ-Access-Token'] !== 'emu-token-1') return err(1600001, 'Invalid API key or access token', 401);
    const st = cjState();
    const put = () => save(K.cj, st);
    const find = id => st.orders[id] || Object.values(st.orders).find(o => o.orderNum === id);
    switch (path) {
      case '/setting/get': return ok({ openId: 42, root: 'GENERAL', isSandbox: false, setting: { qpsLimit: 1 } });
      case '/shopping/pay/getBalance': return ok({ amount: st.balance, noWithdrawalAmount: null, freezeAmount: null });
      case '/product/variant/queryByVid': { const v = cjMockVariant(u.searchParams.get('vid')); return v ? ok(v) : err(1602003, 'Variant removed'); }
      case '/logistic/freightCalculate':
        if ((body.products || []).some(p => /NOSHIP/.test(p.vid))) return ok([]);
        /* a brand-store item stocked only in the US: no route from China */
        if (body.startCountryCode === 'CN' && (body.products || []).some(p => /USONLY/.test(p.vid))) return ok([]);
        return ok([{ logisticName: 'CJPacket Sensitive', logisticPrice: 7.1, logisticAging: '10-18' }, { logisticName: 'CJPacket Ordinary', logisticPrice: 6.2, logisticAging: '12-20' }]);
      case '/shopping/order/createOrderV2': {
        for (const f of ['orderNumber', 'shippingCountryCode', 'shippingProvince', 'shippingCity', 'shippingCustomerName', 'shippingAddress', 'logisticName', 'fromCountryCode']) if (!body[f]) return err(1600100, 'Param error: ' + f);
        if (Object.values(st.orders).some(o => o.orderNum === body.orderNumber)) return err(1603003, 'Order exist, please do not duplicate create');
        let goods = 0;
        for (const p of body.products || []) { const v = cjMockVariant(p.vid); if (!v) return err(1602003, 'Variant removed'); goods += v.variantSellPrice * p.quantity; }
        const amount = Math.round((goods + 7.1) * 100) / 100;
        if (body.payType === 2 && st.balance < amount) return err(1604000, 'Balance is insufficient');
        const id = 'SD' + Date.now() + String(st.seq++).padStart(3, '0');
        const paid = body.payType === 2;
        if (paid) st.balance = Math.round((st.balance - amount) * 100) / 100;
        st.orders[id] = { orderId: id, orderNum: body.orderNumber, orderStatus: paid ? 'UNSHIPPED' : 'CREATED', orderAmount: amount, logisticName: body.logisticName, trackNumber: null, trackingProvider: null, trackingUrl: null, address: body, paymentDate: paid ? new Date().toISOString() : null };
        put();
        return ok({ orderId: id, orderNumber: body.orderNumber, orderStatus: st.orders[id].orderStatus, orderAmount: amount, interceptOrderReasons: [] });
      }
      case '/shopping/order/getOrderDetail': { const o = find(u.searchParams.get('orderId')); return o ? ok(o) : err(1600300, 'order not found'); }
      case '/shopping/pay/payBalance': {
        const o = find(body.orderId); if (!o) return err(1600300, 'order not found');
        if (st.balance < o.orderAmount) return err(1604000, 'Balance is insufficient');
        st.balance -= o.orderAmount; o.orderStatus = 'UNSHIPPED'; put(); return ok(null);
      }
      case '/logistic/trackInfo': { const n = u.searchParams.get('trackNumber'); return ok([{ trackingNumber: n, trackingStatus: 'In transit', lastMileCarrier: 'SMSA', lastTrackNumber: 'SMSA' + n.slice(-8) }]); }
      case '/product/listV2':
        return ok({ totalRecords: 2, content: [{ productList: [{ id: 'MOCK-PID-1', nameEn: 'Women Modest Long Sleeve Maxi Dress', bigImage: '', sellPrice: '9.50', listedNum: 120, warehouseInventoryNum: 800 }, { id: 'MOCK-PID-2', nameEn: 'Women Abaya Open Front Kimono', bigImage: '', sellPrice: '12.40', listedNum: 60, warehouseInventoryNum: 300 }, { id: 'MOCK-PID-COLL', nameEn: 'Blue Tones Dress', bigImage: '', sellPrice: '9.50', listedNum: 5, warehouseInventoryNum: 90 }] }] });
      case '/product/myProduct/query': {
        /* two pages of the owner's "My Products"; a page past the end repeats page 1 (CJ ignores unknown paging params) */
        const page = Number(u.searchParams.get('pageNum')) || 1;
        const all = [{ productId: 'MOCK-PID-1', nameEn: 'Women Modest Long Sleeve Maxi Dress', sku: 'CJMOCK1', bigImage: '', sellPrice: '9.50', createAt: '2026-10-17' },
          { productId: 'MOCK-PID-2', nameEn: 'Women Abaya Open Front Kimono', sku: 'CJMOCK2', bigImage: '', sellPrice: '12.40', createAt: '2026-10-17' },
          { productId: 'MOCK-PID-COLL', nameEn: 'Blue Tones Dress', sku: 'CJMOCK3', bigImage: '', sellPrice: '9.50', createAt: '2026-10-17' }];
        const content = page === 2 ? all.slice(2) : all.slice(0, 2);
        return ok({ pageSize: 2, pageNumber: page, totalRecords: 3, totalPages: 2, content });
      }
      case '/product/query':
        return ok({ pid: u.searchParams.get('pid'), productNameEn: 'Women Modest Long Sleeve Maxi Dress', productSku: 'CJMOCK1', bigImage: '', productImageSet: [], productKeyEn: 'Color-Size', productWeight: 380,
          variants: u.searchParams.get('pid') === 'MOCK-PID-US'
            ? ['Black-M', 'Black-L'].map(k => ({ vid: 'MOCK-USONLY-' + k, variantKey: k, variantNameEn: 'Abaya ' + k, variantImage: '', variantSellPrice: 14, inventories: [{ countryCode: 'US', totalInventory: 60 }, { countryCode: 'CN', totalInventory: 0 }] }))
            : (u.searchParams.get('pid') === 'MOCK-PID-COLL' ? ['Navy-M', 'Dark Blue-M', 'Black-M'] : ['Black-M', 'Black-L', 'Beige-M']).map(k => ({ vid: 'MOCK-' + k, variantKey: k, variantNameEn: 'Maxi Dress ' + k, variantImage: '', variantSellPrice: 9.5 })) });
      case '/product/sourcing/create': {
        if (!body.productName || !body.productImage) return err(1600100, 'Param error');
        const sid = String(1000 + st.seq++); st.sourcing = st.sourcing || {}; st.sourcing[sid] = { url: body.productUrl, status: '3' }; put();
        return ok({ cjSourcingId: sid, result: 'submitted' });
      }
      case '/product/sourcing/queryList': {
        const ids = u.searchParams.getAll('sourceIds');
        const rows = ids.filter(id => (st.sourcing || {})[id]).map(id => ({ sourceId: id, sourceStatus: '3', sourceStatusStr: 'Sourcing succeeded', cjProductId: 'MOCK-PID-1' }));
        return rows.length ? ok(rows) : err(1600300, 'not found');
      }
      default: return err(16900202, 'emulator: no mock for ' + path);
    }
  }

  /* ── service mocks ── */
  const memCache = {};
  const services = {
    PropertiesService: { getScriptProperties: () => ({
      getProperty: k => { const p = Object.assign({}, DEFAULT_PROPS, load(K.props, {})); return p[k] == null ? null : p[k]; },
      setProperty: (k, v) => { const p = load(K.props, {}); p[k] = String(v); save(K.props, p); },
    }) },
    CacheService: { getScriptCache: () => ({
      get: k => { const e = memCache[k]; return e && e.exp > Date.now() ? e.v : null; },
      put: (k, v, s) => { memCache[k] = { v: String(v), exp: Date.now() + (s || 600) * 1000 }; },
    }) },
    LockService: { getScriptLock: () => ({ waitLock: () => {}, releaseLock: () => {} }) },
    SpreadsheetApp: { getActiveSpreadsheet: () => makeSpreadsheet() },
    UrlFetchApp: {
      fetch: (url, opts) => {
        opts = opts || {};
        if (url.indexOf('https://api.moyasar.com/v1/payments/') === 0) {
          const props = Object.assign({}, DEFAULT_PROPS, load(K.props, {}));
          const auth = (opts.headers || {}).Authorization;
          if (auth !== 'Basic ' + btoa(props.MOYASAR_SECRET_KEY + ':')) return resp(401, '{"type":"authentication_error"}');
          const id = decodeURIComponent(url.split('/').pop());
          const pay = load(K.pays, {})[id];
          return pay ? resp(200, JSON.stringify(pay)) : resp(404, '{"type":"record_not_found"}');
        }
        if (url.indexOf('https://developers.cjdropshipping.com/api2.0/v1/') === 0) return cjMock(url, opts);
        const r = syncGet(url);
        if (r.code === 200 && /products\.json/.test(url)) {
          const dev = load('sora-dev-config', null);
          if (dev) { const data = JSON.parse(r.text); Object.assign(data.config, dev); return resp(200, JSON.stringify(data)); }
        }
        return resp(r.code, r.text);
      },
    },
    Utilities: {
      sleep: () => {},
      base64Encode: s => btoa(s),
      newBlob: s => ({ getBytes: () => Array.from(new TextEncoder().encode(String(s))) }),
      getUuid: () => crypto.randomUUID(),
      formatDate: (d, tz, fmt) => {
        const parts = {};
        new Intl.DateTimeFormat('en-GB', { timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
          .formatToParts(d).forEach(p => { parts[p.type] = p.value; });
        const hh = parts.hour === '24' ? '00' : parts.hour;
        return fmt.replace("'T'", 'T').replace('yyyy', parts.year).replace('yy', parts.year.slice(2)).replace('MM', parts.month)
          .replace('dd', parts.day).replace('HH', hh).replace('mm', parts.minute).replace('ss', parts.second);
      },
    },
    MailApp: { sendEmail: m => { const box = load(K.mail, []); box.push({ to: m.to, subject: m.subject, at: new Date().toISOString() }); save(K.mail, box.slice(-50)); console.info('[emu mail]', m.subject); } },
    ContentService: { MimeType: { JSON: 'json' }, createTextOutput: s => ({ setMimeType: () => ({ getContent: () => s }) }) },
  };
  function resp(code, text) { return { getResponseCode: () => code, getContentText: () => text }; }

  const src = syncGet(BASE + 'backend/Code.gs');
  if (src.code !== 200) throw new Error('emulator: cannot load backend/Code.gs');
  const names = Object.keys(services);
  // eslint-disable-next-line no-new-func
  const backend = new Function(...names, src.text + '\n;return { handle_, computeTotals_, normPhone_, doPost, processSupplierQueue, fulfillOrder_, syncCjOrder_, cjDailyCheck };')(...names.map(n => services[n]));

  window.SoraEmu = {
    backend,
    props: () => Object.assign({}, DEFAULT_PROPS, load(K.props, {})),
    setProps: p => save(K.props, Object.assign(load(K.props, {}), p)),
    call: async body => {
      await new Promise(r => setTimeout(r, 120));
      return JSON.parse(JSON.stringify(backend.handle_(JSON.parse(body))));
    },
    /* simulates the Moyasar hosted form creating a payment */
    mockPay: ({ amount, currency, metadata, status, sourceType }) => {
      const id = crypto.randomUUID();
      const pays = load(K.pays, {});
      pays[id] = { id, status: status || 'paid', amount, currency: currency || 'SAR', metadata: metadata || {}, source: { type: sourceType || 'mada', message: status === 'failed' ? 'INSUFFICIENT FUNDS' : 'APPROVED' } };
      save(K.pays, pays);
      return id;
    },
    mail: () => load(K.mail, []),
    /* CJ simulation: connect, fund the wallet, run the 5-minute trigger, ship an order */
    cjConnect: () => save(K.props, Object.assign(load(K.props, {}), { CJ_API_KEY: CJ_KEY })),
    cjState: () => cjState(),
    cjSetBalance: usd => { const s = cjState(); s.balance = usd; save(K.cj, s); },
    cjShip: orderNumber => {
      const s = cjState();
      const o = Object.values(s.orders).find(x => x.orderNum === orderNumber || x.orderId === orderNumber);
      if (!o) return false;
      o.orderStatus = 'SHIPPED'; o.trackNumber = 'CJPKL' + String(Date.now()).slice(-8); o.trackingProvider = 'CJPacket'; o.trackingUrl = 'https://www.17track.net/en/track?nums=' + o.trackNumber;
      save(K.cj, s);
      return o.trackNumber;
    },
    runQueue: () => { backend.processSupplierQueue(); return true; },
    reset: () => { [K.sheets, K.sheets + '-meta', K.pays, K.mail, K.props, K.cj].forEach(k => localStorage.removeItem(k)); },
  };
})();
