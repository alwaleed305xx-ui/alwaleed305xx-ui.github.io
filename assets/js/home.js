/* Sora Fashion — home page */
Sora.ready(() => {
  'use strict';
  const S = Sora, { $, esc, I } = S;
  const cfg = S.config;
  const params = new URLSearchParams(location.search);
  let activeCat = params.get('cat') || 'all';
  let view = params.get('view') || '';
  let sortBy = 'featured';
  if (activeCat !== 'all' && !S.cat(activeCat)) activeCat = 'all';

  /* hero */
  $('#heroPerks').innerHTML = [
    [I.plane, `شحن دولي من مستودعاتنا بالخارج · ${esc(S.shipRange())} يوم`],
    [I.shield, 'دفع آمن: مدى · أبل باي'],
    [I.swap, `استبدال خلال ${esc(cfg.returnsDays)} أيام`],
  ].map(([i, t]) => `<span>${i}${t}</span>`).join('');
  /* the first product photo of a category (featured first), or null */
  const photoOf = catId => {
    const list = S.products.filter(p => p.cat === catId && (p.images || []).length);
    const p = list.find(x => x.featured) || list[0];
    return p ? { p, src: p.images[0] } : null;
  };
  /* the hero arches lead with the showcased collections once they have photos */
  const featuredCats = S.cats.filter(c => c.showcase && photoOf(c.id)).slice(0, 3).map(c => [c.type, c.hex, c.name, c.id]);
  $('#heroArt').innerHTML = (featuredCats.length === 3 ? featuredCats : [
    ['abaya', '#1c1b1d', 'عبايات', 'abayas'],
    ['maxi', '#c99a94', 'فساتين', 'dresses'],
    ['set', '#7f8f74', 'رياضي', 'sport'],
  ]).map(([t, c, label, cat]) => {
    const ph = photoOf(cat);
    return `<a class="arch${ph ? ' has-photo' : ''}" href="index.html?cat=${cat}#catalog">${ph ? S.imgTag(ph.p, ph.src, '', { eager: true, w: 600 }) : Garment.svg(t, c)}<span class="arch-tag">${label}</span></a>`;
  }).join('');

  /* trust */
  $('#trust').innerHTML = [
    [I.plane, 'يُشحن من الخارج', `مع رقم تتبّع · ${esc(S.shipRange())} يوم`],
    [I.shield, 'دفع آمن 100%', 'مدى · Apple Pay · بطاقات'],
    [I.swap, 'استبدال سهل', `خلال ${esc(cfg.returnsDays)} أيام من الاستلام`],
    [I.ruler, 'مقاسات واضحة', 'جدول مقاسات لكل قطعة'],
  ].map(([i, b, s]) => `<div class="trust-item">${i}<div><b>${b}</b><small>${s}</small></div></div>`).join('');

  /* categories */
  const counts = {};
  S.products.forEach(p => { counts[p.cat] = (counts[p.cat] || 0) + 1; });
  $('#catRail').innerHTML = S.cats.filter(c => counts[c.id]).map(c => `
    <a class="cat-tile reveal" href="index.html?cat=${esc(c.id)}#catalog" data-cat="${esc(c.id)}">
      <span class="cat-art${photoOf(c.id) ? ' has-photo' : ''}">${photoOf(c.id) ? S.imgTag(photoOf(c.id).p, photoOf(c.id).src, '', { w: 400 }) : Garment.svg(c.type, c.hex, { accent: c.accent })}</span>
      <b>${esc(c.name)}</b><small>${counts[c.id]} قطعة</small>
    </a>`).join('');

  /* a showcased category's pieces in display order: featured first */
  const rankedOf = catId => {
    const list = S.products.filter(p => p.cat === catId);
    return list.filter(p => p.featured).concat(list.filter(p => !p.featured));
  };
  const RAIL = 12;

  /* new arrivals: one new piece per category in turn, skipping what the showcase rails already show */
  const freshBy = S.cats.map(c => (c.showcase ? rankedOf(c.id).slice(RAIL) : S.products.filter(p => p.cat === c.id))
    .filter(p => p.badge === 'new')).filter(l => l.length);
  const newest = [];
  for (let i = 0; newest.length < 8 && freshBy.some(l => l[i]); i++) freshBy.forEach(l => { if (l[i] && newest.length < 8) newest.push(l[i]); });
  if (newest.length < 4) newest.splice(0, newest.length, ...[...S.products].sort((a, b) => b.id - a.id).slice(0, 8));
  $('#newGrid').innerHTML = newest.map(S.card).join('');

  /* collection showcases: a banner with three photos from the category, then a scrolling rail of its pieces */
  $('#showcases').innerHTML = S.cats.filter(c => c.showcase && counts[c.id]).map(c => {
    const sc = c.showcase;
    const ranked = rankedOf(c.id);
    const pics = ranked.filter(p => (p.images || []).length).slice(0, 3);
    const style = `--sc-bg:${esc(sc.bg || c.hex || '#1f1a1d')};--sc-fg:${esc(sc.fg || '#fbf8f4')};--sc-accent:${esc(sc.accent || '#a9874f')}`;
    return `<section class="section showcase" id="sc-${esc(c.id)}" style="${style}">
      <div class="container">
        <div class="sc-banner reveal">
          <div class="sc-copy">
            ${sc.eyebrow ? `<span class="eyebrow">${esc(sc.eyebrow)}</span>` : ''}
            <h2>${esc(sc.title || c.name)}</h2>
            <p>${esc(sc.sub || c.desc || '')}</p>
            <a class="sc-cta" href="index.html?cat=${esc(c.id)}#catalog">تسوّقي ${esc(c.name)} <small>${counts[c.id]} قطعة</small></a>
          </div>
          <div class="sc-mosaic">${pics.map((p, i) => `<a class="sc-pic sc-pic-${i + 1}" href="${S.productUrl(p)}" aria-label="${esc(p.name)}">${S.imgTag(p, p.images[0], '', { w: 600 })}</a>`).join('')}</div>
        </div>
        <div class="sc-rail">${ranked.slice(0, RAIL).map(S.card).join('')}</div>
      </div>
    </section>`;
  }).join('');
  S.reveal($('#showcases'));

  /* promo */
  const promo = (cfg.coupons || []).find(c => c.active !== false && c.showOnHome);
  if (promo) {
    $('#promoCode').firstChild.textContent = promo.code + ' ';
    $('.promo-copy h3').textContent = promo.type === 'percent' ? `خصم ${promo.value}٪ على طلبك` : `خصم ${promo.value} ر.س على طلبك`;
    if (promo.headline) $('.promo-copy h3').textContent = promo.headline;
  } else {
    $('.promo').closest('.section').hidden = true;
  }
  const promoPics = ['evening', 'kaftan'].map(photoOf).filter(Boolean);
  $('#promoArt').innerHTML = promoPics.length === 2
    ? promoPics.map(ph => `<span class="promo-photo">${S.imgTag(ph.p, ph.src, '', { w: 600 })}</span>`).join('')
    : Garment.svg('gown', '#5e2b48') + Garment.svg('kaftan', '#efe8dc', { accent: '#a9874f' });
  $('#copyCode').addEventListener('click', () => {
    const code = $('#promoCode').firstChild.textContent.trim();
    (navigator.clipboard ? navigator.clipboard.writeText(code) : Promise.reject()).then(
      () => S.toast('انسخ الكود ✓'), () => S.toast('الكود: ' + code));
  });

  /* catalog */
  function chips() {
    const list = [{ id: 'all', name: 'الكل' }, ...S.cats.filter(c => counts[c.id])];
    const favN = S.favs().filter(id => S.product(id)).length;
    $('#chips').innerHTML = list.map(c => `<button class="chip ${view !== 'fav' && c.id === activeCat ? 'active' : ''}" data-chip="${esc(c.id)}">${esc(c.name)}</button>`).join('') +
      (favN ? `<button class="chip ${view === 'fav' ? 'active' : ''}" data-chip="__fav">♡ المفضلة (${favN})</button>` : '');
  }
  const PAGE = 24;
  let shown = PAGE;
  function grid() {
    let list = S.products;
    if (view === 'fav') { const f = S.favs().map(String); list = list.filter(p => f.includes(String(p.id))); }
    else if (activeCat !== 'all') list = list.filter(p => p.cat === activeCat);
    const sorters = {
      featured: (a, b) => (b.featured ? 1 : 0) - (a.featured ? 1 : 0),
      new: (a, b) => b.id - a.id,
      priceAsc: (a, b) => a.price - b.price,
      priceDesc: (a, b) => b.price - a.price,
    };
    list = [...list].sort(sorters[sortBy]);
    $('#catalogGrid').innerHTML = list.slice(0, shown).map(S.card).join('');
    const more = $('#catalogMore');
    more.hidden = list.length <= shown;
    more.textContent = `عرض المزيد (${list.length - shown})`;
    const empty = $('#catalogEmpty');
    empty.hidden = list.length > 0;
    empty.textContent = view === 'fav' ? 'ما عندك قطع بالمفضلة للحين — اضغطي ♡ على أي قطعة تعجبك' : 'ما فيه قطع بهالقسم حالياً';
    const c = S.cat(activeCat);
    $('#catalogTitle').textContent = view === 'fav' ? 'مفضلتك' : (c ? c.name : 'كل المنتجات');
    $('#catalogSub').textContent = view === 'fav' ? 'القطع اللي حفظتيها' : (c ? c.desc : 'اختاري القسم ورتّبي على كيفك');
    S.reveal($('#catalogGrid'));
  }
  $('#chips').addEventListener('click', e => {
    const b = e.target.closest('[data-chip]');
    if (!b) return;
    if (b.dataset.chip === '__fav') { view = 'fav'; activeCat = 'all'; }
    else { view = ''; activeCat = b.dataset.chip; }
    const q = view === 'fav' ? '?view=fav' : (activeCat === 'all' ? '' : `?cat=${encodeURIComponent(activeCat)}`);
    history.replaceState(null, '', `${location.pathname}${q}#catalog`);
    shown = PAGE;
    chips(); grid();
  });
  $('#catRail').addEventListener('click', e => {
    const t = e.target.closest('[data-cat]');
    if (!t) return;
    e.preventDefault();
    view = ''; activeCat = t.dataset.cat;
    history.replaceState(null, '', `${location.pathname}?cat=${encodeURIComponent(activeCat)}#catalog`);
    shown = PAGE;
    chips(); grid();
    $('#catalog').scrollIntoView({ behavior: 'smooth' });
  });
  $('#sort').addEventListener('change', e => { sortBy = e.target.value; shown = PAGE; grid(); });
  $('#catalogMore').addEventListener('click', () => { shown += PAGE; grid(); });
  document.addEventListener('click', e => { if (e.target.closest('[data-fav]') && view === 'fav') setTimeout(() => { chips(); grid(); }, 0); });
  chips(); grid();

  /* sport spotlight */
  const sport = S.products.filter(p => p.cat === 'sport').slice(0, 4);
  $('#sportGrid').innerHTML = sport.map(S.card).join('');
  if (!sport.length) $('#sport').hidden = true;

  /* why */
  $('#why').innerHTML = [
    [I.box, 'قطع منتقاة بعناية', 'نختار كل قطعة على أساس الخامة والقصّة وتقييمات المشتريات، ونستبعد اللي ما يستاهل.'],
    [I.ruler, 'مقاس يناسبك من أول مرة', 'جدول مقاسات بالسنتيمتر لكل قطعة، ومقاسات العبايات بالطول — وإذا ما ضبط نستبدله لك.'],
    [I.lock, 'دفع آمن وموثوق', 'الدفع عبر بوابة سعودية مرخّصة بمدى وأبل باي والبطاقات — ما نحفظ بيانات بطاقتك أبداً.'],
  ].map(([i, h, p]) => `<div class="why-card reveal">${i}<h3>${h}</h3><p>${p}</p></div>`).join('');

  /* faq */
  const faq = [
    ['من وين تُشحن الطلبات؟', 'منتجاتنا تُشحن مباشرة من مستودعات الموردين خارج السعودية (أغلبها من الصين)، مو من مخزن داخل المملكة — وعشان كذا مدة التوصيل أطول من المتاجر المحلية. مكان الشحن ومدة التوصيل مكتوبة في صفحة كل قطعة وقبل الدفع.'],
    ['كم يأخذ التوصيل؟', `نجهّز طلبك خلال 1-3 أيام ونشحنه من مستودع المورد بالخارج، ويوصلك خلال ${esc(S.shipRange())} يوم لكل مدن المملكة حسب القطعة (المدة مكتوبة في صفحة كل قطعة). يوصلك رقم تتبّع، وتقدرين تتابعين طلبك من صفحة «تتبّع طلبك».`],
    ['وش طرق الدفع؟', `مدى، Apple Pay، فيزا وماستركارد عبر بوابة دفع سعودية آمنة${cfg.codEnabled ? '، والدفع عند الاستلام برسوم ' + esc(cfg.codFee) + ' ر.س' : ''}.`],
    ['كيف أعرف مقاسي؟', 'كل قطعة فيها «دليل المقاسات» بالسنتيمتر. للعبايات والإسدال المقاس هو الطول (52 إلى 60). وإذا احترتي بين مقاسين اختاري الأكبر.'],
    ['أقدر أستبدل أو أرجّع؟', `إيه، خلال ${esc(cfg.returnsDays)} أيام من الاستلام إذا القطعة بحالتها الأصلية مع البطاقة. التفاصيل في سياسة الاستبدال والاسترجاع.`],
    ['كم الشحن؟', `${esc(cfg.shippingFee)} ر.س${S.freeOver() ? `، ومجاني للطلبات فوق ${esc(S.freeOver())} ر.س` : ''}.`],
  ];
  $('#faqList').innerHTML = faq.map(([q, a]) => `<details class="reveal"><summary>${q}</summary><p>${a}</p></details>`).join('') +
    '<details class="reveal"><summary>كيف أتابع طلبي؟</summary><p>من صفحة <a href="track.html" style="text-decoration:underline">تتبّع طلبك</a> برقم الطلب ورقم جوالك.</p></details>';

  S.reveal();
  if (location.hash === '#catalog' && (params.get('cat') || params.get('view'))) setTimeout(() => $('#catalog').scrollIntoView(), 50);
});
