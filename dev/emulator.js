/* Sora Fashion — local backend emulator (development only).
   Runs the real backend/Code.gs in the browser against mocked Apps Script services,
   persisting "sheets" in localStorage so checkout, thanks, track and admin pages all
   share one fake database. Activated only when the API endpoint is "local" on localhost. */
(function () {
  'use strict';
  const script = document.currentScript;
  const BASE = new URL('../', script ? script.src : location.href).href;
  const K = { sheets: 'sora-emu-sheets', props: 'sora-emu-props', pays: 'sora-emu-payments', mail: 'sora-emu-mail' };
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
      const self = {
        getName: () => name,
        getMaxRows: () => meta[name] || 1000,
        insertRowsAfter: (after, n) => { meta[name] = (meta[name] || 1000) + n; persist(); },
        getLastRow: () => { const r = rows(); for (let i = r.length - 1; i >= 0; i--) if ((r[i] || []).some(v => v !== '' && v != null)) return i + 1; return 0; },
        getLastColumn: () => rows().reduce((m, r) => { let c = 0; (r || []).forEach((v, i) => { if (v !== '' && v != null) c = i + 1; }); return Math.max(m, c); }, 0),
        setFrozenRows: () => self,
        getRange: (r, c, nr, nc) => {
          nr = nr || 1; nc = nc || 1;
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

  /* ── service mocks ── */
  const memCache = {};
  const services = {
    PropertiesService: { getScriptProperties: () => ({ getProperty: k => { const p = Object.assign({}, DEFAULT_PROPS, load(K.props, {})); return p[k] == null ? null : p[k]; } }) },
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
        const r = syncGet(url);
        if (r.code === 200 && /products\.json/.test(url)) {
          const dev = load('sora-dev-config', null);
          if (dev) { const data = JSON.parse(r.text); Object.assign(data.config, dev); return resp(200, JSON.stringify(data)); }
        }
        return resp(r.code, r.text);
      },
    },
    Utilities: {
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
  const backend = new Function(...names, src.text + '\n;return { handle_, computeTotals_, normPhone_, doPost };')(...names.map(n => services[n]));

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
    reset: () => { [K.sheets, K.sheets + '-meta', K.pays, K.mail, K.props].forEach(k => localStorage.removeItem(k)); },
  };
})();
