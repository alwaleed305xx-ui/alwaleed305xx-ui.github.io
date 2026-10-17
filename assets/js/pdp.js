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
    eu: {
      head: ['المقاس', 'يعادل', 'الصدر (سم)', 'الخصر (سم)', 'الأرداف (سم)'],
      rows: [['34', 'XS', '80-84', '62-66', '88-92'], ['36', 'S', '84-88', '66-70', '92-96'], ['38', 'M', '88-92', '70-74', '96-100'], ['40', 'L', '92-96', '74-78', '100-104'], ['42', 'XL', '96-102', '78-84', '104-110'], ['44', '2XL', '102-108', '84-90', '110-116'], ['46', '3XL', '108-114', '90-96', '116-122'], ['48', '4XL', '114-120', '96-102', '122-128'], ['50', '5XL', '120-126', '102-108', '128-134'], ['52', '6XL', '126-132', '108-114', '134-140'], ['54-56', '7XL', '132-144', '114-126', '140-152'], ['58-64', '8XL+', '144-168', '126-150', '152-176']],
      note: 'مقاسات تركية/أوروبية بالأرقام. إذا كنتِ بين مقاسين اختاري الأكبر — القياسات تقريبية وقد تختلف 1-2 سم حسب القطعة.',
    },
    abaya: {
      head: ['المقاس (الطول)', 'طولك تقريباً', 'طول القطعة'],
      rows: [['50', '145 - 150 سم', '127 سم'], ['52', '150 - 155 سم', '132 سم'], ['54', '155 - 160 سم', '137 سم'], ['56', '160 - 165 سم', '142 سم'], ['58', '165 - 170 سم', '147 سم'], ['60', '170 - 176 سم', '152 سم'], ['62', '176 - 181 سم', '157 سم']],
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

      <div class="origin-box">${I.plane}<div><b>منتج يُشحن ${esc(S.originText(p))}</b><span>ما نخزّن داخل السعودية — القطعة تنشحن لك مباشرة من مستودع المورد${S.originOf(p) ? ' في ' + esc(S.originOf(p)) : ''} وتوصلك خلال ${esc(S.shipDaysOf(p))} يوم مع رقم تتبّع.</span></div></div>

      <div class="pdp-perks">
        <div>${I.truck}<span>يوصلك خلال <b>${esc(S.shipDaysOf(p))} يوم</b> (شحن دولي)${S.freeOver() ? ` · شحن مجاني فوق ${S.fmt(S.freeOver())} ر.س` : ''}</span></div>
        <div>${I.swap}<span>استبدال أو استرجاع خلال ${esc(cfg.returnsDays)} أيام من الاستلام</span></div>
        <div>${I.shield}<span>دفع آمن: مدى · Apple Pay · بطاقات${cfg.codEnabled ? ' · عند الاستلام' : ''}</span></div>
      </div>

      <div class="acc">
        <details open><summary>الوصف والتفاصيل</summary><div class="acc-body"><p>${esc(p.desc)}</p>${(p.details || []).length ? `<ul style="margin-top:.6rem">${p.details.map(d => `<li>${esc(d)}</li>`).join('')}</ul>` : ''}</div></details>
        <details><summary>الخامة والعناية</summary><div class="acc-body"><p><b>الخامة:</b> ${esc(p.fabric || '—')}</p><p><b>العناية:</b> ${esc(p.care || '—')}</p></div></details>
        <details><summary>الشحن والاستبدال</summary><div class="acc-body"><p>القطعة تُشحن ${esc(S.originText(p))} (مو من مخزن داخل السعودية). نجهّز طلبك خلال 1-3 أيام ويوصلك خلال ${esc(S.shipDaysOf(p))} يوم مع رقم تتبّع. رسوم الشحن ${S.fmt(cfg.shippingFee)} ر.س${S.freeOver() ? ` ومجاني فوق ${S.fmt(S.freeOver())} ر.س` : ''}. <a href="policies.html#returns" style="text-decoration:underline">سياسة الاستبدال</a></p></div></details>
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
    sel.img = Math.max(0, Math.min(sel.img, imgs.length - 1));
    const many = imgs.length > 1;
    g.className = many ? 'gallery' : 'gallery single';
    g.innerHTML = (many ? `<div class="thumbs">${imgs.map((u, i) => `<button class="${i === sel.img ? 'on' : ''}" data-img="${i}" aria-label="صورة ${i + 1}">${S.imgTag(p, u, sel.color, { w: 200 })}</button>`).join('')}</div>` : '') +
      `<div class="stage photo" id="stage">${S.imgTag(p, imgs[sel.img], sel.color, { eager: true, w: 1000 })}
        ${many ? `<button class="stage-nav prev" data-step="-1" aria-label="الصورة السابقة">›</button><button class="stage-nav next" data-step="1" aria-label="الصورة التالية">‹</button>
        <div class="stage-dots">${imgs.map((_, i) => `<i class="${i === sel.img ? 'on' : ''}"></i>`).join('')}</div>` : ''}</div>`;
    const stage = $('#stage');
    let x0 = null;
    stage.addEventListener('touchstart', e => { x0 = e.touches[0].clientX; }, { passive: true });
    stage.addEventListener('touchend', e => {
      if (x0 == null || !many) return;
      const dx = e.changedTouches[0].clientX - x0; x0 = null;
      if (Math.abs(dx) > 40) step(dx > 0 ? 1 : -1);   // RTL: swipe right = next
    });
  }
  /* sizes the supplier doesn't make in the chosen colour are greyed out */
  function syncSizes() {
    $$('#sizes .size-btn').forEach(b => {
      const na = S.isNa(p, sel.color, b.dataset.size);
      b.disabled = na;
      b.classList.toggle('na', na);
      b.title = na ? 'غير متوفر بهاللون' : '';
      if (na && sel.size === b.dataset.size) {
        sel.size = '';
        b.classList.remove('on'); b.setAttribute('aria-checked', 'false');
        $('#sizeName').textContent = 'اختاري';
        $('#sizeHint').textContent = 'المقاس اللي اخترتيه مو متوفر بهاللون — اختاري مقاس ثاني';
      }
    });
  }
  function step(d) {
    const n = S.imagesFor(p, sel.color).length;
    if (n < 2) return;
    sel.img = (sel.img + d + n) % n;
    renderGallery();
  }
  renderGallery();
  syncSizes();

  $('#pdp').addEventListener('click', e => {
    const st = e.target.closest('[data-step]');
    if (st) { step(Number(st.dataset.step)); return; }
    const t = e.target.closest('[data-color],[data-size],[data-img]');
    if (!t) return;
    if (t.dataset.color) {
      sel.color = t.dataset.color; sel.img = 0;
      $$('#swatches .swatch').forEach(b => { const on = b.dataset.color === sel.color; b.classList.toggle('on', on); b.setAttribute('aria-checked', on); });
      $('#colorName').textContent = sel.color;
      renderGallery();
      syncSizes();
    } else if (t.dataset.size) {
      if (S.isNa(p, sel.color, t.dataset.size)) return;
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
