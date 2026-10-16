/* Sora Fashion — product detail page */
Sora.ready(() => {
  'use strict';
  const S = Sora, { $, $$, esc, I, money, moneyText } = S;
  const cfg = S.config;
  const id = new URLSearchParams(location.search).get('id');
  const p = S.product(id);

  if (!p) {
    $('#pdp').innerHTML = '<div class="center-card" style="grid-column:1/-1"><h1>القطعة هذي مو متوفرة</h1><p>يمكن انتهت أو انشالت من المتجر.</p><a class="btn btn-dark" href="index.html#catalog" style="margin-top:1.4rem">تصفّحي المجموعة</a></div>';
    return;
  }

  const SIZE_CHARTS = {
    women: {
      head: ['المقاس', 'الصدر (سم)', 'الخصر (سم)', 'الأرداف (سم)'],
      rows: [['S', '84-88', '66-70', '90-94'], ['M', '88-92', '70-74', '94-98'], ['L', '92-98', '74-80', '98-104'], ['XL', '98-104', '80-86', '104-110'], ['2XL', '104-110', '86-92', '110-116'], ['3XL', '110-116', '92-98', '116-122']],
      note: 'إذا كنتِ بين مقاسين اختاري الأكبر. القياسات تقريبية وقد تختلف 1-2 سم حسب القطعة.',
    },
    sport: {
      head: ['المقاس', 'الصدر (سم)', 'الخصر (سم)', 'الأرداف (سم)'],
      rows: [['XS', '76-80', '58-62', '84-88'], ['S', '80-84', '62-66', '88-92'], ['M', '84-90', '66-72', '92-98'], ['L', '90-96', '72-78', '98-104'], ['XL', '96-102', '78-84', '104-110'], ['2XL', '102-108', '84-90', '110-116']],
      note: 'الملابس الرياضية ضاغطة ومطاطية — لو تحبين اللبس الواسع اختاري مقاس أكبر.',
    },
    abaya: {
      head: ['المقاس (الطول)', 'طولك تقريباً', 'طول القطعة'],
      rows: [['52', '150 - 155 سم', '132 سم'], ['54', '155 - 160 سم', '137 سم'], ['56', '160 - 165 سم', '142 سم'], ['58', '165 - 170 سم', '147 سم'], ['60', '170 - 176 سم', '152 سم']],
      note: 'مقاس العباية والإسدال هو الطول بالإنش من الكتف للأرض. القصّة واسعة وتناسب أغلب الأجسام.',
    },
    onesize: { head: [], rows: [], note: 'مقاس حر بقصّة واسعة يناسب من S إلى 2XL تقريباً.' },
  };

  const sel = { color: (p.colors[0] || {}).name || '', size: p.sizes && p.sizes.length === 1 ? p.sizes[0] : '', qty: 1, img: 0 };
  const off = S.offPct(p);
  const chart = SIZE_CHARTS[p.sizeChart] || SIZE_CHARTS.women;

  document.title = `${p.name} | Sora Fashion`;
  const md = document.querySelector('meta[name="description"]');
  if (md) md.setAttribute('content', `${p.name} — ${p.desc}`.slice(0, 160));
  const ld = document.createElement('script');
  ld.type = 'application/ld+json';
  ld.textContent = JSON.stringify({
    '@context': 'https://schema.org', '@type': 'Product', name: p.name, description: p.desc,
    brand: { '@type': 'Brand', name: 'Sora Fashion' }, image: S.imagesFor(p, sel.color),
    offers: { '@type': 'Offer', priceCurrency: 'SAR', price: p.price, availability: 'https://schema.org/InStock', url: location.href },
  });
  document.head.appendChild(ld);

  const cat = S.cat(p.cat);
  $('#crumbs').innerHTML = `<a href="index.html">الرئيسية</a><span>/</span>${cat ? `<a href="index.html?cat=${esc(cat.id)}#catalog">${esc(cat.name)}</a><span>/</span>` : ''}<span>${esc(p.name)}</span>`;

  $('#pdp').innerHTML = `
    <div class="gallery" id="gallery"></div>
    <div class="pdp-info">
      <h1>${esc(p.name)}</h1>
      <div class="pdp-price"><span class="price">${money(p.price)}</span>${off ? `<span class="price-old">${S.fmt(p.old)} ر.س</span><span class="price-off">وفّري <bdi dir="ltr">${off}%</bdi></span>` : ''}</div>
      <p class="pdp-note">السعر نهائي · الشحن يُحسب عند الدفع</p>

      <div class="opt-group">
        <div class="opt-label"><b>اللون: <span id="colorName">${esc(sel.color)}</span></b></div>
        <div class="swatches" id="swatches" role="radiogroup" aria-label="اللون">
          ${p.colors.map(c => `<button class="swatch ${c.name === sel.color ? 'on' : ''}" style="background:${esc(c.hex)}" data-color="${esc(c.name)}" role="radio" aria-checked="${c.name === sel.color}" aria-label="${esc(c.name)}" title="${esc(c.name)}"></button>`).join('')}
        </div>
      </div>

      <div class="opt-group">
        <div class="opt-label"><b>المقاس: <span id="sizeName">${sel.size ? esc(sel.size) : 'اختاري'}</span></b>${chart.rows.length ? '<button type="button" id="sizeGuide">دليل المقاسات</button>' : ''}</div>
        <div class="sizes" id="sizes" role="radiogroup" aria-label="المقاس">
          ${p.sizes.map(z => `<button class="size-btn ${z === sel.size ? 'on' : ''}" data-size="${esc(z)}" role="radio" aria-checked="${z === sel.size}">${esc(z)}</button>`).join('')}
        </div>
        <p class="size-hint" id="sizeHint" aria-live="polite"></p>
      </div>

      <div class="buy-row">
        <div class="qty"><button type="button" id="qtyPlus" aria-label="زيادة">+</button><b id="qtyVal">1</b><button type="button" id="qtyMinus" aria-label="إنقاص">−</button></div>
        <button class="btn btn-dark" id="addBtn">أضيفي للسلة</button>
      </div>
      <button class="btn btn-plum btn-block" id="buyNow">اشتري الآن</button>

      <div class="pdp-perks">
        <div>${I.truck}<span>يوصلك خلال <b>${esc(cfg.shippingDays)} يوم</b> · شحن مجاني فوق ${S.fmt(cfg.freeShippingOver)} ر.س</span></div>
        <div>${I.swap}<span>استبدال أو استرجاع خلال ${esc(cfg.returnsDays)} أيام من الاستلام</span></div>
        <div>${I.shield}<span>دفع آمن: مدى · Apple Pay · بطاقات${cfg.codEnabled ? ' · عند الاستلام' : ''}</span></div>
      </div>

      <div class="acc">
        <details open><summary>الوصف والتفاصيل</summary><div class="acc-body"><p>${esc(p.desc)}</p>${(p.details || []).length ? `<ul style="margin-top:.6rem">${p.details.map(d => `<li>${esc(d)}</li>`).join('')}</ul>` : ''}</div></details>
        <details><summary>الخامة والعناية</summary><div class="acc-body"><p><b>الخامة:</b> ${esc(p.fabric || '—')}</p><p><b>العناية:</b> ${esc(p.care || '—')}</p></div></details>
        <details><summary>الشحن والاستبدال</summary><div class="acc-body"><p>نجهّز طلبك خلال 1-3 أيام ويوصلك خلال ${esc(cfg.shippingDays)} يوم مع رقم تتبّع. رسوم الشحن ${S.fmt(cfg.shippingFee)} ر.س ومجاني فوق ${S.fmt(cfg.freeShippingOver)} ر.س. <a href="policies.html#returns" style="text-decoration:underline">سياسة الاستبدال</a></p></div></details>
      </div>
    </div>`;

  function renderGallery() {
    const imgs = S.imagesFor(p, sel.color);
    const g = $('#gallery');
    if (!imgs.length) {
      g.className = 'gallery single';
      g.innerHTML = `<div class="stage">${S.media(p, sel.color, { eager: true })}</div>`;
      return;
    }
    sel.img = Math.min(sel.img, imgs.length - 1);
    g.className = imgs.length > 1 ? 'gallery' : 'gallery single';
    g.innerHTML = (imgs.length > 1 ? `<div class="thumbs">${imgs.map((u, i) => `<button class="${i === sel.img ? 'on' : ''}" data-img="${i}" aria-label="صورة ${i + 1}"><img src="${esc(u)}" alt="" loading="lazy" referrerpolicy="no-referrer"></button>`).join('')}</div>` : '') +
      `<div class="stage"><img src="${esc(imgs[sel.img])}" alt="${esc(p.name)} — ${esc(sel.color)}" referrerpolicy="no-referrer"></div>`;
  }
  renderGallery();

  $('#pdp').addEventListener('click', e => {
    const t = e.target.closest('[data-color],[data-size],[data-img]');
    if (!t) return;
    if (t.dataset.color) {
      sel.color = t.dataset.color; sel.img = 0;
      $$('#swatches .swatch').forEach(b => { const on = b.dataset.color === sel.color; b.classList.toggle('on', on); b.setAttribute('aria-checked', on); });
      $('#colorName').textContent = sel.color;
      renderGallery();
    } else if (t.dataset.size) {
      sel.size = t.dataset.size;
      $$('#sizes .size-btn').forEach(b => { const on = b.dataset.size === sel.size; b.classList.toggle('on', on); b.setAttribute('aria-checked', on); });
      $('#sizeName').textContent = sel.size;
      $('#sizeHint').textContent = '';
    } else if (t.dataset.img) {
      sel.img = Number(t.dataset.img); renderGallery();
    }
  });
  const setQty = q => { sel.qty = Math.max(1, Math.min(10, q)); $('#qtyVal').textContent = sel.qty; };
  $('#qtyPlus').addEventListener('click', () => setQty(sel.qty + 1));
  $('#qtyMinus').addEventListener('click', () => setQty(sel.qty - 1));

  function add() {
    if (!sel.size) {
      $('#sizeHint').textContent = 'اختاري المقاس أول';
      $('#sizes').scrollIntoView({ behavior: 'smooth', block: 'center' });
      return false;
    }
    S.addToCart(p.id, sel.color, sel.size, sel.qty);
    return true;
  }
  $('#addBtn').addEventListener('click', () => { if (add()) { S.toast('انضافت للسلة ✓'); S.openCart(); } });
  $('#stickyAdd').addEventListener('click', () => { if (add()) { S.toast('انضافت للسلة ✓'); S.openCart(); } });
  $('#buyNow').addEventListener('click', () => { if (add()) location.href = 'checkout.html'; });

  /* size guide */
  const sg = $('#sizeGuide');
  if (sg) sg.addEventListener('click', () => {
    $('#sizeBody').innerHTML = (chart.rows.length ? `<table class="size-table"><thead><tr>${chart.head.map(h => `<th>${h}</th>`).join('')}</tr></thead><tbody>${chart.rows.map(r => `<tr>${r.map(c => `<td>${c}</td>`).join('')}</tr>`).join('')}</tbody></table>` : '') + `<p class="note">${chart.note}</p>`;
    $('#sizeModal').classList.add('open');
    $('#overlay').classList.add('show');
  });
  document.addEventListener('click', e => { if (e.target.closest('[data-close-modal]')) S.closeAll(); });

  /* sticky buy bar on mobile */
  $('#stickyPrice').innerHTML = money(p.price);
  if ('IntersectionObserver' in window) {
    new IntersectionObserver(([en]) => $('#stickyBuy').classList.toggle('show', !en.isIntersecting && en.boundingClientRect.top < 0), { threshold: 0 }).observe($('#addBtn'));
  }

  /* related */
  const rel = S.products.filter(x => x.cat === p.cat && x.id !== p.id).slice(0, 4);
  const more = rel.length < 4 ? S.products.filter(x => x.cat !== p.cat && x.featured).slice(0, 4 - rel.length) : [];
  const list = [...rel, ...more];
  if (list.length) { $('#relatedWrap').hidden = false; $('#related').innerHTML = list.map(S.card).join(''); S.reveal($('#related')); }
});
