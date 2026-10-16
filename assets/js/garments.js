/* Sora Fashion — fashion-flat garment illustrations (SVG), recolourable per variant.
   Garment.svg(type, hex, opts) → <svg> markup string. viewBox is 300×400. */
(function () {
  'use strict';

  function rgb(hex) {
    let h = String(hex || '#888').replace('#', '');
    if (h.length === 3) h = h.split('').map(c => c + c).join('');
    const n = parseInt(h, 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  }
  function toHex(c) { return '#' + c.map(v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join(''); }
  function mix(hex, to, t) { const a = rgb(hex), b = rgb(to); return toHex(a.map((v, i) => v + (b[i] - v) * t)); }
  function luminance(hex) {
    const c = rgb(hex).map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
  }

  function palette(hex, accent) {
    const L = luminance(hex);
    const darkish = L < 0.12;
    return {
      base: hex,
      dark: mix(hex, '#000000', darkish ? 0.35 : 0.24),
      deep: mix(hex, '#000000', darkish ? 0.55 : 0.4),
      light: mix(hex, '#ffffff', darkish ? 0.16 : 0.24),
      line: L > 0.45 ? 'rgba(40,20,30,.16)' : 'rgba(255,255,255,.17)',
      sheen: L > 0.6 ? 'rgba(255,255,255,.55)' : 'rgba(255,255,255,.22)',
      accent: accent || (L < 0.3 ? '#cdb389' : mix(hex, '#000000', 0.45)),
      trimLight: L < 0.5 ? '#efe6d6' : '#ffffff',
    };
  }

  let uid = 0;

  /* ---------- garment parts (each returns markup, coordinates in 300×400) ---------- */
  const fold = (d, p, w) => `<path d="${d}" fill="none" stroke="${p.line}" stroke-width="${w || 1.6}" stroke-linecap="round"/>`;
  const fill = (d, f, extra) => `<path d="${d}" fill="${f}" ${extra || ''}/>`;

  const parts = {
    maxi(p, g) {
      return [
        fill('M121,62 C103,66 96,78 94,94 L79,212 Q89,219 99,215 L121,110 Z', `url(#${g})`),
        fill('M179,62 C197,66 204,78 206,94 L221,212 Q211,219 201,215 L179,110 Z', `url(#${g})`),
        fill('M79,205 L100,208.5 L99,215 Q89,219 79,212 Z', p.dark),
        fill('M221,205 L200,208.5 L201,215 Q211,219 221,212 Z', p.dark),
        fill('M121,62 L136,57 Q150,72 164,57 L179,62 L177,104 L174,150 C188,215 210,300 236,380 Q222,387 208,383 Q193,389 179,384 Q164,390 150,385 Q136,390 121,384 Q107,389 92,383 Q78,387 64,380 C90,300 112,215 126,150 L123,104 Z', `url(#${g})`),
        fill('M125,146 L175,146 L176.5,158 L123.5,158 Z', p.dark),
        `<ellipse cx="150" cy="152" rx="6" ry="5" fill="${p.deep}"/>`,
        fold('M136,57 Q150,72 164,57', p, 2),
        fold('M139,88 L141,144', p), fold('M161,88 L159,144', p),
        fold('M138,160 C134,240 124,310 112,382', p), fold('M162,160 C166,240 176,310 188,382', p),
        fold('M150,160 L150,383', p, 1.2), fold('M124,170 C112,250 96,320 84,380', p, 1.2), fold('M176,170 C188,250 204,320 216,380', p, 1.2),
      ].join('');
    },
    wrap(p, g) {
      return [
        fill('M122,62 C104,66 92,80 88,100 Q104,112 122,106 Z', `url(#${g})`),
        fill('M178,62 C196,66 208,80 212,100 Q196,112 178,106 Z', `url(#${g})`),
        fold('M90,98 Q104,108 121,104', p, 1.4), fold('M210,98 Q196,108 179,104', p, 1.4),
        fill('M122,62 L138,57 L150,112 L162,57 L178,62 L176,106 L173,152 C186,200 200,260 214,312 Q198,320 182,316 Q166,322 150,318 Q134,322 118,316 Q102,320 86,312 C100,260 114,200 127,152 L124,106 Z', `url(#${g})`),
        fold('M162,57 C158,100 150,135 127,152', p, 2), fold('M150,156 C166,210 182,262 200,314', p, 2),
        fill('M127,147 L173,147 L173,157 L127,157 Z', p.dark),
        fill('M172,152 C186,138 198,145 191,154 C186,161 177,157 172,152 Z', p.dark),
        fill('M172,152 C182,160 190,170 184,174 C178,176 174,164 172,152 Z', p.deep),
        `<path d="M175,156 C180,176 178,196 184,218" fill="none" stroke="${p.dark}" stroke-width="5" stroke-linecap="round"/>`,
        `<path d="M178,156 C188,172 192,190 197,206" fill="none" stroke="${p.dark}" stroke-width="5" stroke-linecap="round"/>`,
        fold('M140,162 C136,220 128,270 120,314', p), fold('M112,180 C104,230 96,270 92,308', p, 1.2),
      ].join('');
    },
    gown(p, g) {
      return [
        `<path d="M131,101 L134,53" stroke="${p.dark}" stroke-width="3" stroke-linecap="round"/>`,
        `<path d="M169,101 L166,53" stroke="${p.dark}" stroke-width="3" stroke-linecap="round"/>`,
        fill('M126,100 Q150,124 174,100 L178,140 C182,210 190,300 214,384 Q182,393 150,389 Q118,393 86,384 C110,300 118,210 122,140 Z', `url(#${g})`),
        fold('M132,104 Q150,128 168,104', p, 1.8), fold('M138,111 Q150,125 162,111', p, 1.4),
        fold('M122,140 Q150,150 178,140', p, 1.4),
        `<path d="M140,152 C142,230 136,310 124,384" fill="none" stroke="${p.sheen}" stroke-width="7" stroke-linecap="round" opacity=".6"/>`,
        `<path d="M159,152 C162,240 170,320 182,387" fill="none" stroke="${p.sheen}" stroke-width="3" stroke-linecap="round" opacity=".5"/>`,
        fold('M170,262 C174,300 180,345 188,387', p, 1.8),
        fold('M128,160 C122,240 110,320 98,384', p, 1.2),
      ].join('');
    },
    abaya(p, g) {
      const trim = p.accent;
      return [
        fill('M114,64 C92,72 80,96 74,124 L52,252 Q76,268 100,258 L110,150 Z', `url(#${g})`),
        fill('M186,64 C208,72 220,96 226,124 L248,252 Q224,268 200,258 L190,150 Z', `url(#${g})`),
        `<path d="M53,247 Q76,262 100,253" fill="none" stroke="${trim}" stroke-width="3"/>`,
        `<path d="M247,247 Q224,262 200,253" fill="none" stroke="${trim}" stroke-width="3"/>`,
        `<path d="M55,238 Q76,252 101,243" fill="none" stroke="${trim}" stroke-width="2" stroke-dasharray="2 5" stroke-linecap="round"/>`,
        `<path d="M245,238 Q224,252 199,243" fill="none" stroke="${trim}" stroke-width="2" stroke-dasharray="2 5" stroke-linecap="round"/>`,
        fill('M114,64 L136,57 Q150,66 164,57 L186,64 L193,130 C206,230 218,320 228,386 L72,386 C82,320 94,230 107,130 Z', `url(#${g})`),
        fill('M141,62 Q150,68 159,62 L166,386 L134,386 Z', p.deep),
        `<path d="M141,62 L134,386 M159,62 L166,386" fill="none" stroke="${trim}" stroke-width="3"/>`,
        `<path d="M146,70 L140,384 M154,70 L160,384" fill="none" stroke="${trim}" stroke-width="1.6" stroke-dasharray="1.5 6" stroke-linecap="round"/>`,
        `<path d="M136,57 Q150,66 164,57" fill="none" stroke="${trim}" stroke-width="2.4"/>`,
        fold('M118,160 C114,250 104,330 96,386', p), fold('M182,160 C186,250 196,330 204,386', p),
        fold('M84,150 Q72,200 62,246', p, 1.3), fold('M216,150 Q228,200 238,246', p, 1.3),
      ].join('');
    },
    kaftan(p, g) {
      const a = p.accent;
      return [
        fill('M128,58 Q150,72 172,58 L196,66 C226,82 250,102 262,124 Q258,146 244,154 L198,142 C204,240 210,320 214,386 L86,386 C90,320 96,240 102,142 L56,154 Q42,146 38,124 C50,102 74,82 104,66 Z', `url(#${g})`),
        `<path d="M134,60 L150,118 L166,60" fill="none" stroke="${a}" stroke-width="6" stroke-linejoin="round" opacity=".92"/>`,
        `<path d="M140,62 L150,100 L160,62" fill="none" stroke="${p.trimLight}" stroke-width="1.4" stroke-dasharray="1.5 4" stroke-linecap="round"/>`,
        `<path d="M150,124 L150,260" fill="none" stroke="${a}" stroke-width="3.2" stroke-dasharray="1 7" stroke-linecap="round"/>`,
        `<path d="M38,124 Q42,146 56,154 M262,124 Q258,146 244,154" fill="none" stroke="${a}" stroke-width="4"/>`,
        `<path d="M88,370 L212,370" stroke="${a}" stroke-width="3" stroke-dasharray="6 4"/>`,
        `<path d="M87,378 L213,378" stroke="${a}" stroke-width="1.6"/>`,
        fold('M118,150 C114,250 104,330 98,386', p), fold('M182,150 C186,250 196,330 202,386', p),
        fold('M100,100 Q80,118 60,140', p, 1.3), fold('M200,100 Q220,118 240,140', p, 1.3),
      ].join('');
    },
    leggings(p, g) {
      return [
        fill('M97,98 Q150,92 203,98 L200,176 L188,384 L158,384 L151,196 L149,196 L142,384 L112,384 L100,176 Z', `url(#${g})`),
        fill('M98,58 Q150,52 202,58 L203,98 Q150,92 97,98 Z', p.dark),
        fold('M98,78 Q150,72 202,78', p, 1.2),
        `<path d="M98,85 Q150,79 202,85" fill="none" stroke="${p.line}" stroke-width="1" stroke-dasharray="3 3"/>`,
        fold('M150,98 L150,196', p, 1.8),
        fold('M118,106 C132,150 138,200 140,240', p, 1.4), fold('M182,106 C168,150 162,200 160,240', p, 1.4),
        `<path d="M110,112 C108,200 116,300 121,378" fill="none" stroke="${p.sheen}" stroke-width="6" stroke-linecap="round" opacity=".55"/>`,
        `<path d="M190,112 C192,200 184,300 179,378" fill="none" stroke="${p.sheen}" stroke-width="4" stroke-linecap="round" opacity=".35"/>`,
      ].join('');
    },
    bra(p, g) {
      return [
        fill('M116,92 L128,92 L138,150 L124,152 Z', p.dark),
        fill('M184,92 L172,92 L162,150 L176,152 Z', p.dark),
        fill('M104,170 C108,140 122,132 136,142 Q150,170 164,142 C178,132 192,140 196,170 L202,236 Q150,250 98,236 Z', `url(#${g})`),
        fill('M98,230 Q150,244 202,230 L204,262 Q150,276 96,262 Z', p.dark),
        fold('M98,246 Q150,260 202,246', p, 1),
        fold('M150,166 L150,240', p, 1.6),
        fold('M108,182 Q124,160 141,176', p, 1.4), fold('M192,182 Q176,160 159,176', p, 1.4),
        `<path d="M114,198 C120,184 132,180 140,186" fill="none" stroke="${p.sheen}" stroke-width="5" stroke-linecap="round" opacity=".5"/>`,
      ].join('');
    },
    hoodie(p, g) {
      return [
        fill('M124,62 C122,30 140,18 150,18 C160,18 178,30 176,62 Z', p.dark),
        fill('M108,66 C90,74 82,90 80,108 L70,226 Q80,234 92,230 L104,130 Z', `url(#${g})`),
        fill('M192,66 C210,74 218,90 220,108 L230,226 Q220,234 208,230 L196,130 Z', `url(#${g})`),
        fill('M70,219 L92,223 L92,230 Q80,234 70,226 Z', p.dark), fill('M230,219 L208,223 L208,230 Q220,234 230,226 Z', p.dark),
        fill('M108,66 Q128,58 150,72 Q172,58 192,66 L198,232 Q150,240 102,232 Z', `url(#${g})`),
        fill('M132,62 C134,40 142,32 150,32 C158,32 166,40 168,62 Q150,76 132,62 Z', p.deep),
        fill('M102,226 Q150,234 198,226 L198,242 Q150,250 102,242 Z', p.dark),
        fill('M124,168 L176,168 L184,212 L116,212 Z', p.dark, 'opacity=".55"'),
        fold('M124,168 L116,212 M176,168 L184,212', p, 1.4),
        `<path d="M144,74 L142,114 M156,74 L158,114" stroke="${p.trimLight}" stroke-width="2.4" stroke-linecap="round"/>`,
        `<circle cx="142" cy="116" r="2.6" fill="${p.trimLight}"/><circle cx="158" cy="116" r="2.6" fill="${p.trimLight}"/>`,
        fold('M86,110 L78,210', p, 1.2), fold('M214,110 L222,210', p, 1.2),
      ].join('');
    },
    joggers(p, g) {
      return [
        fill('M105,84 L195,84 L190,170 L184,370 L188,384 L156,384 L158,370 L151,186 L149,186 L142,370 L144,384 L112,384 L116,370 L110,170 Z', `url(#${g})`),
        fill('M106,60 Q150,56 194,60 L195,84 Q150,80 105,84 Z', p.dark),
        fill('M112,370 L144,370 L144,384 L112,384 Z', p.dark), fill('M156,370 L188,370 L188,384 L156,384 Z', p.dark),
        `<path d="M146,70 L144,104 M154,70 L156,104" stroke="${p.trimLight}" stroke-width="2.2" stroke-linecap="round"/>`,
        fold('M150,84 L150,186', p, 1.6), fold('M122,100 C124,140 126,170 128,200', p, 1.3), fold('M178,100 C176,140 174,170 172,200', p, 1.3),
        `<path d="M114,110 C114,200 118,300 122,366" fill="none" stroke="${p.sheen}" stroke-width="5" stroke-linecap="round" opacity=".35"/>`,
      ].join('');
    },
    shirt(p, g) {
      return [
        fill('M110,64 C94,70 86,84 84,100 L76,186 Q86,192 98,188 L106,120 Z', `url(#${g})`),
        fill('M190,64 C206,70 214,84 216,100 L224,186 Q214,192 202,188 L194,120 Z', `url(#${g})`),
        `<path d="M76,180 L98,182 M224,180 L202,182" stroke="${p.trimLight}" stroke-width="2.2"/>`,
        fill('M110,64 L137,58 L150,90 L163,58 L190,64 L194,226 Q150,232 106,226 Z', `url(#${g})`),
        fill('M137,58 L126,84 L146,108 L150,90 Z', p.light), fill('M163,58 L174,84 L154,108 L150,90 Z', p.light),
        `<path d="M137,58 L126,84 L146,108 M163,58 L174,84 L154,108" fill="none" stroke="${p.trimLight}" stroke-width="2.2" stroke-linejoin="round"/>`,
        fold('M150,96 L150,228', p, 1.4),
        [124, 152, 180, 208].map(y => `<circle cx="150" cy="${y}" r="3.2" fill="${p.trimLight}"/>`).join(''),
        `<path d="M118,118 L136,118 L136,138 L118,138 Z" fill="none" stroke="${p.trimLight}" stroke-width="1.8"/>`,
        `<path d="M118,150 C120,180 116,206 114,224" fill="none" stroke="${p.sheen}" stroke-width="6" stroke-linecap="round" opacity=".5"/>`,
      ].join('');
    },
    pants(p, g) {
      return [
        fill('M103,82 L197,82 L204,384 L160,384 L151,176 L149,176 L140,384 L96,384 Z', `url(#${g})`),
        fill('M104,60 Q150,56 196,60 L197,82 Q150,78 103,82 Z', p.dark),
        `<path d="M96,374 L140,374 M160,374 L204,374" stroke="${p.trimLight}" stroke-width="2.4"/>`,
        fold('M150,82 L150,176', p, 1.4),
        `<path d="M112,96 C110,200 108,300 106,372" fill="none" stroke="${p.sheen}" stroke-width="7" stroke-linecap="round" opacity=".5"/>`,
        `<path d="M186,96 C190,200 192,300 194,372" fill="none" stroke="${p.sheen}" stroke-width="3" stroke-linecap="round" opacity=".4"/>`,
      ].join('');
    },
    isdal(p, g) {
      const a = p.accent;
      return [
        fill('M70,206 Q150,226 230,206 C236,280 244,330 252,386 L48,386 C56,330 64,280 70,206 Z', `url(#${g})`),
        fold('M110,232 C104,300 96,350 90,386', p), fold('M190,232 C196,300 204,350 210,386', p), fold('M150,236 L150,386', p, 1.2),
        fill('M150,26 C118,26 102,48 100,84 C98,120 84,160 58,214 Q150,236 242,214 C216,160 202,120 200,84 C198,48 182,26 150,26 Z', `url(#${g})`),
        fill('M150,48 C132,48 124,62 124,82 C124,104 136,118 150,118 C164,118 176,104 176,82 C176,62 168,48 150,48 Z', '#ebe2d6'),
        `<path d="M150,50 C134,50 126,63 126,82" fill="none" stroke="${p.dark}" stroke-width="5" stroke-linecap="round" opacity=".35"/>`,
        `<path d="M58,214 Q150,236 242,214" fill="none" stroke="${a}" stroke-width="3"/>`,
        `<path d="M62,222 Q150,244 238,222" fill="none" stroke="${a}" stroke-width="5" stroke-dasharray="0.1 9" stroke-linecap="round"/>`,
        `<path d="M124,82 C124,104 136,118 150,118 C164,118 176,104 176,82" fill="none" stroke="${a}" stroke-width="2"/>`,
        fold('M126,130 C118,160 106,190 94,214', p), fold('M174,130 C182,160 194,190 206,214', p),
      ].join('');
    },
    hijab(p, g) {
      return [
        fill('M96,58 L204,58 C210,150 216,232 220,292 Q186,322 150,314 Q114,322 80,292 C84,232 90,150 96,58 Z', `url(#${g})`, 'fill-opacity=".95"'),
        fold('M110,80 C108,160 102,232 96,296', p, 1.2), fold('M128,80 C128,170 124,240 122,312', p, 1.2),
        fold('M190,80 C192,160 198,232 204,296', p, 1.2),
        `<path d="M80,292 Q114,322 150,314 Q186,322 220,292" fill="none" stroke="${p.dark}" stroke-width="1.6"/>`,
        fill('M150,62 L182,60 C190,140 202,250 206,352 Q192,362 176,354 C174,262 166,160 150,62 Z', p.dark, 'opacity=".9"'),
        fill('M154,62 L180,61 C187,140 198,250 202,348 Q190,356 178,350 C176,262 168,160 154,62 Z', `url(#${g})`),
        fold('M168,90 C174,170 180,260 188,350', p, 1.2),
        `<path d="M178,352 L177,362 M184,354 L184,365 M190,354 L191,364 M196,352 L198,361" stroke="${p.dark}" stroke-width="1.6" stroke-linecap="round"/>`,
        fill('M94,48 Q150,38 206,48 L206,66 Q150,57 94,66 Z', p.dark),
        `<path d="M104,90 C102,170 98,232 92,286" fill="none" stroke="${p.sheen}" stroke-width="8" stroke-linecap="round" opacity=".35"/>`,
      ].join('');
    },
    tunic(p, g) {
      return [
        fill('M114,62 C98,68 90,82 88,98 L74,228 Q84,234 96,230 L108,124 Z', `url(#${g})`),
        fill('M186,62 C202,68 210,82 212,98 L226,228 Q216,234 204,230 L192,124 Z', `url(#${g})`),
        fill('M114,62 L136,57 L150,62 L164,57 L186,62 L194,140 C196,190 198,230 200,262 Q150,274 100,262 C102,230 104,190 106,140 Z', `url(#${g})`),
        fill('M106,140 C104,190 102,230 100,262 L114,265 C116,230 118,190 120,140 Z', p.dark),
        fill('M194,140 C196,190 198,230 200,262 L186,265 C184,230 182,190 180,140 Z', p.dark),
        fill('M136,57 L138,45 Q150,40 162,45 L164,57 L150,62 Z', p.dark),
        `<path d="M150,44 L150,118" stroke="${p.trimLight}" stroke-width="2.2"/>`,
        `<rect x="147" y="116" width="6" height="12" rx="2" fill="${p.trimLight}"/>`,
        fold('M90,104 L78,222', p, 1.2), fold('M210,104 L222,222', p, 1.2),
        `<path d="M128,90 C126,150 124,210 122,258" fill="none" stroke="${p.sheen}" stroke-width="6" stroke-linecap="round" opacity=".4"/>`,
      ].join('');
    },
  };

  /* composite outfits drawn from parts */
  const composites = {
    set: [['leggings', 'translate(39,98) scale(0.74)'], ['bra', 'translate(52,-54) scale(0.65)']],
    lounge: [['joggers', 'translate(40,90) scale(0.72)'], ['hoodie', 'translate(39,0) scale(0.74)']],
    pajama: [['pants', 'translate(36,94) scale(0.76)'], ['shirt', 'translate(36,6) scale(0.76)']],
    modestset: [['leggings', 'translate(40,96) scale(0.73)'], ['tunic', 'translate(24.5,4) scale(0.83)']],
  };
  const HANGER = new Set(['maxi', 'wrap', 'gown', 'abaya', 'kaftan', 'hijab']);

  const TYPES = {
    maxi: 'فستان ماكسي', wrap: 'فستان لف', gown: 'فستان سهرة', abaya: 'عباية', kaftan: 'جلابية / قفطان',
    leggings: 'ليقنز', bra: 'توب رياضي', set: 'طقم رياضي', modestset: 'طقم رياضي محتشم', lounge: 'طقم هودي',
    pajama: 'بيجامة ساتان', isdal: 'إسدال صلاة', hijab: 'طرحة / حجاب', tunic: 'توب رياضي طويل',
  };

  function hangerMarkup() {
    return '<g fill="none" stroke="#b49a78" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">' +
      '<path d="M151,13 Q152,6 160,6 Q170,6 170,14 Q170,23 160,24 Q150,26 150,38 L150,48"/>' +
      '<path d="M96,62 L150,48 L204,62"/></g>';
  }

  function svg(type, hex, opts) {
    opts = opts || {};
    const id = 'g' + (++uid);
    const p = palette(hex || '#1c1b1d', opts.accent);
    const grad = `<linearGradient id="${id}" x1="0" x2="1" y1="0" y2="0">` +
      `<stop offset="0" stop-color="${p.dark}"/><stop offset=".3" stop-color="${p.base}"/>` +
      `<stop offset=".52" stop-color="${p.light}"/><stop offset=".74" stop-color="${p.base}"/>` +
      `<stop offset="1" stop-color="${p.dark}"/></linearGradient>`;
    let body;
    if (composites[type]) {
      body = composites[type].map(([part, tf]) => `<g transform="${tf}">${parts[part](p, id)}</g>`).join('');
    } else {
      body = (parts[type] || parts.maxi)(p, id);
    }
    const shadow = '<ellipse cx="150" cy="392" rx="92" ry="6" fill="rgba(60,30,40,.10)"/>';
    const hanger = HANGER.has(type) && opts.hanger !== false ? hangerMarkup() : '';
    const label = opts.label ? ` role="img" aria-label="${String(opts.label).replace(/"/g, '&quot;')}"` : ' aria-hidden="true"';
    return `<svg class="garment" viewBox="0 0 300 400" xmlns="http://www.w3.org/2000/svg"${label}><defs>${grad}</defs>${shadow}${hanger}${body}</svg>`;
  }

  window.Garment = { svg, TYPES, palette, mix, luminance };
})();
