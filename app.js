/* ═══════ مُنتقى — منطق المتجر (أمازون أفلييت) ═══════ */
'use strict';

/* البيانات: من products.js — وإذا فيه مسودة من لوحة التحكم بجهازك، تظهر لك أنت فقط */
let DATA = window.STORE;
let usingDraft = false;
try {
  const draft = JSON.parse(localStorage.getItem('muntaqa-draft') || 'null');
  if (draft && Array.isArray(draft.products) && Array.isArray(draft.cats)) {
    DATA = draft;
    usingDraft = true;
  }
} catch (e) { /* مسودة تالفة — نتجاهلها */ }

const CONFIG = DATA.config || {};
const CATS = DATA.cats;
const PRODUCTS = DATA.products.filter(p => p.show !== false);

/* ═══════ روابط الشراء ═══════ */
function amazonUrl(p) {
  let u = p.asin
    ? `https://www.amazon.sa/dp/${encodeURIComponent(p.asin)}/`
    : `https://www.amazon.sa/s?k=${encodeURIComponent(p.q || p.name)}`;
  if (CONFIG.amazonTag) u += (u.includes('?') ? '&' : '?') + `tag=${encodeURIComponent(CONFIG.amazonTag)}`;
  return u;
}
function aliUrl(p) {
  const target = `https://ar.aliexpress.com/wholesale?SearchText=${encodeURIComponent(p.q || p.name)}`;
  if (CONFIG.aliShortKey)
    return `https://s.click.aliexpress.com/deep_link.htm?aff_short_key=${encodeURIComponent(CONFIG.aliShortKey)}&dl_target_url=${encodeURIComponent(target)}`;
  return target;
}
function noonUrl(p) {
  const target = `https://www.noon.com/saudi-ar/search/?q=${encodeURIComponent(p.q || p.name)}`;
  return CONFIG.noonTracking ? CONFIG.noonTracking + encodeURIComponent(target) : target;
}
function buyAmazon(id) {
  const p = PRODUCTS.find(x => x.id == id);
  if (p) window.open(amazonUrl(p), '_blank', 'noopener');
}

/* ═══════ أدوات ═══════ */
const $ = id => document.getElementById(id);
const gradOf = p => CATS.find(c => c.id === p.cat)?.grad || 'linear-gradient(135deg,#333,#555)';
const catName = id => CATS.find(c => c.id === id)?.name || '';
const rial = n => `${n} <small>ر.س</small>`;
const offPct = p => p.old ? Math.round((1 - p.price / p.old) * 100) : 0;
const stars = r => '★'.repeat(Math.round(r)) + '☆'.repeat(5 - Math.round(r));
const BADGES = { hot: 'الأكثر طلباً', new: 'جديد', sale: 'سعر ممتاز', trend: 'ترند 🔥' };

/* ═══════ الحالة ═══════ */
let favs = [];
try { favs = JSON.parse(localStorage.getItem('muntaqa-favs') || '[]'); } catch (e) { favs = []; }
let activeCat = 'all';
let query = '';
let sortBy = 'popular';
const isFav = id => favs.includes(Number(id));

/* ═══════ بطاقة منتج ═══════ */
function productCard(p) {
  const badge = p.badge && BADGES[p.badge] ? `<span class="p-badge ${p.badge}">${BADGES[p.badge]}</span>` : '';
  const old = p.old ? `<span class="p-old">${p.old} ر.س</span><span class="p-off">-${offPct(p)}%</span>` : '';
  return `
  <article class="p-card reveal in" data-id="${p.id}">
    <div class="p-media" style="--grad:${gradOf(p)}" data-view="${p.id}">
      ${badge}
      <button class="p-fav ${isFav(p.id) ? 'on' : ''}" data-fav="${p.id}" aria-label="مفضلة">♥</button>
      <span class="p-emoji">${p.emoji}</span>
    </div>
    <div class="p-body">
      <span class="p-cat">${catName(p.cat)}</span>
      <h3 class="p-name" data-view="${p.id}">${p.name}</h3>
      <div class="p-rating"><span class="stars">${stars(p.rating)}</span> ${p.rating} · ${(+p.sold).toLocaleString('en')}+ طلب</div>
      <div class="p-price-row"><span class="p-price">${rial(p.price)}</span>${old}</div>
      <button class="p-buy" data-buy="${p.id}">🛒 اشترِ من أمازون</button>
      <button class="p-more" data-view="${p.id}">التفاصيل والمتاجر الأخرى</button>
    </div>
  </article>`;
}

/* ═══════ العرض ═══════ */
function renderCats() {
  const counts = {};
  PRODUCTS.forEach(p => counts[p.cat] = (counts[p.cat] || 0) + 1);
  $('catsGrid').innerHTML = CATS.map(c => `
    <div class="cat-card reveal in" data-cat="${c.id}">
      <div class="cat-icon" style="--grad:${c.grad};background:${c.grad}"><span>${c.icon}</span></div>
      <div><b>${c.name}</b><span>${c.desc}</span></div>
      <span class="cat-count">${counts[c.id] || 0} منتج</span>
    </div>`).join('');
}

function visibleProducts() {
  const list = PRODUCTS.filter(p =>
    (activeCat === 'all' || p.cat === activeCat) &&
    (!query || p.name.includes(query) || catName(p.cat).includes(query))
  );
  const sorters = {
    popular: (a, b) => b.sold - a.sold,
    priceAsc: (a, b) => a.price - b.price,
    priceDesc: (a, b) => b.price - a.price,
    rating: (a, b) => b.rating - a.rating,
  };
  return list.sort(sorters[sortBy]);
}

function renderProducts() {
  const list = visibleProducts();
  $('productsGrid').innerHTML = list.map(productCard).join('');
  $('emptyMsg').hidden = list.length > 0;
}

function renderTrend() {
  const trend = PRODUCTS.filter(p => p.cat === 'trend').slice(0, 8);
  $('trendGrid').innerHTML = trend.map(productCard).join('');
}

function renderChips() {
  const all = [{ id: 'all', name: 'الكل' }, ...CATS];
  $('filterChips').innerHTML = all.map(c =>
    `<button class="chip ${c.id === activeCat ? 'active' : ''}" data-chip="${c.id}">${c.name}</button>`).join('');
}

/* ═══════ المفضلة ═══════ */
function saveFavs() { localStorage.setItem('muntaqa-favs', JSON.stringify(favs)); }

function toggleFav(id) {
  id = Number(id);
  const on = isFav(id);
  favs = on ? favs.filter(f => f !== id) : [...favs, id];
  saveFavs(); renderFavs();
  document.querySelectorAll(`[data-fav="${id}"]`).forEach(b => b.classList.toggle('on', !on));
  const p = PRODUCTS.find(x => x.id === id);
  if (p) toast(on ? `💔 شلنا «${p.name}» من مفضلتك` : `❤️ أضفنا «${p.name}» لمفضلتك`);
  $('favBtn').classList.remove('bump');
  void $('favBtn').offsetWidth;
  $('favBtn').classList.add('bump');
}

function renderFavs() {
  const list = favs.map(id => PRODUCTS.find(p => p.id === id)).filter(Boolean);
  $('favCount').textContent = list.length;
  $('drawerCount').textContent = list.length ? `(${list.length})` : '';
  $('drawerItems').innerHTML = list.length ? list.map(p => `
    <div class="ci">
      <div class="ci-thumb" style="--grad:${gradOf(p)};background:${gradOf(p)}">${p.emoji}</div>
      <div class="ci-info"><b>${p.name}</b><span class="ci-price">≈ ${p.price} ر.س</span></div>
      <button class="ci-buy" data-buy="${p.id}">🛒 اشترِ</button>
      <button class="ci-del" data-unfav="${p.id}" aria-label="حذف">🗑</button>
    </div>`).join('')
    : `<div class="fav-empty"><span>💛</span>مفضلتك فاضية…<br>اضغط ♥ على أي منتج يعجبك</div>`;
}

/* ═══════ نافذة المنتج ═══════ */
function openModal(id) {
  const p = PRODUCTS.find(x => x.id == id);
  if (!p) return;
  const old = p.old ? `<span class="m-old">${p.old} ر.س</span><span class="m-off">وفر حتى ${p.old - p.price} ر.س</span>` : '';
  $('modalBody').innerHTML = `
    <div class="m-media" style="--grad:${gradOf(p)};background:${gradOf(p)}"><span class="m-emoji">${p.emoji}</span></div>
    <div class="m-info">
      <span class="m-cat">${catName(p.cat)}</span>
      <h3>${p.name}</h3>
      <div class="m-rating"><span class="stars">${stars(p.rating)}</span> ${p.rating} من 5 · ${(+p.sold).toLocaleString('en')}+ طلب</div>
      <p class="m-desc">${p.desc || ''}</p>
      <div class="m-feats">${(p.feats || []).map(f => `<span>${f}</span>`).join('')}</div>
      <div class="m-price-row"><span class="m-price">${rial(p.price)}</span>${old}</div>
      <button class="m-buy" data-buy="${p.id}">🛒 اشترِ الآن من أمازون السعودية</button>
      <div class="m-alt">
        <button data-alt="ali:${p.id}">علي إكسبرس (أرخص، أبطأ)</button>
        <button data-alt="noon:${p.id}">نون</button>
      </div>
      <div class="m-trust"><span>🛡️ الشراء داخل المتجر الرسمي</span><span>🚚 توصيل أمازون 1-4 أيام</span><span>🔁 استرجاع رسمي</span></div>
    </div>`;
  $('modalBackdrop').classList.add('show');
  document.body.style.overflow = 'hidden';
}
function closeModal() { $('modalBackdrop').classList.remove('show'); document.body.style.overflow = ''; }
function openDrawer() { $('favDrawer').classList.add('open'); $('drawerBackdrop').classList.add('show'); document.body.style.overflow = 'hidden'; }
function closeDrawer() { $('favDrawer').classList.remove('open'); $('drawerBackdrop').classList.remove('show'); document.body.style.overflow = ''; }

/* ═══════ تنبيهات ═══════ */
function toast(msg) {
  const el = document.createElement('div');
  el.className = 'toast';
  el.textContent = msg;
  $('toastWrap').appendChild(el);
  setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 300); }, 2200);
}

/* ═══════ العد التنازلي (يتجدد كل جمعة) ═══════ */
function tickCountdown() {
  const now = new Date();
  const end = new Date(now);
  end.setDate(now.getDate() + ((5 - now.getDay() + 7) % 7 || 7));
  end.setHours(23, 59, 59, 0);
  let diff = Math.max(0, end - now) / 1000;
  const d = Math.floor(diff / 86400); diff %= 86400;
  const h = Math.floor(diff / 3600); diff %= 3600;
  $('cdD').textContent = d;
  $('cdH').textContent = h;
  $('cdM').textContent = Math.floor(diff / 60);
  $('cdS').textContent = Math.floor(diff % 60);
}

/* ═══════ ظهور تدريجي ═══════ */
const io = new IntersectionObserver(es => es.forEach(e => e.isIntersecting && e.target.classList.add('in')), { threshold: .12 });
function observeReveals() { document.querySelectorAll('.reveal:not(.in)').forEach(el => io.observe(el)); }

/* ═══════ الأحداث ═══════ */
document.addEventListener('click', e => {
  const t = e.target.closest('[data-buy],[data-alt],[data-fav],[data-unfav],[data-view],[data-chip],[data-cat]');
  if (!t) return;
  if (t.dataset.buy) { buyAmazon(t.dataset.buy); }
  else if (t.dataset.alt) {
    const [shop, pid] = t.dataset.alt.split(':');
    const p = PRODUCTS.find(x => x.id == pid);
    if (p) window.open(shop === 'ali' ? aliUrl(p) : noonUrl(p), '_blank', 'noopener');
  }
  else if (t.dataset.fav) { toggleFav(t.dataset.fav); }
  else if (t.dataset.unfav) { toggleFav(t.dataset.unfav); }
  else if (t.dataset.view) { closeDrawer(); openModal(t.dataset.view); }
  else if (t.dataset.chip) { activeCat = t.dataset.chip; renderChips(); renderProducts(); }
  else if (t.dataset.cat && t.classList.contains('cat-card')) {
    activeCat = t.dataset.cat; renderChips(); renderProducts();
    $('products').scrollIntoView({ behavior: 'smooth' });
  }
});

$('favBtn').addEventListener('click', openDrawer);
$('closeDrawer').addEventListener('click', closeDrawer);
$('drawerBackdrop').addEventListener('click', closeDrawer);
$('closeModal').addEventListener('click', closeModal);
$('modalBackdrop').addEventListener('click', e => { if (e.target === $('modalBackdrop')) closeModal(); });
document.addEventListener('keydown', e => { if (e.key === 'Escape') { closeModal(); closeDrawer(); } });

$('searchInput').addEventListener('input', e => {
  query = e.target.value.trim();
  if (query && activeCat !== 'all') { activeCat = 'all'; renderChips(); }
  renderProducts();
  if (query) $('products').scrollIntoView({ behavior: 'smooth', block: 'start' });
});
$('sortSelect').addEventListener('change', e => { sortBy = e.target.value; renderProducts(); });

/* الهيدر + تفعيل روابط القائمة */
const header = $('header');
window.addEventListener('scroll', () => {
  header.classList.toggle('scrolled', window.scrollY > 10);
  let current = 'home';
  document.querySelectorAll('section[id]').forEach(sec => {
    if (window.scrollY >= sec.offsetTop - 150) current = sec.id;
  });
  document.querySelectorAll('.nav a').forEach(a => a.classList.toggle('active', a.getAttribute('href') === '#' + current));
}, { passive: true });

/* قائمة الجوال */
$('burger').addEventListener('click', () => {
  $('burger').classList.toggle('open');
  $('nav').classList.toggle('open');
});
document.querySelectorAll('.nav a').forEach(a => a.addEventListener('click', () => {
  $('burger').classList.remove('open');
  $('nav').classList.remove('open');
}));

/* شريط المسودة */
if (usingDraft) {
  $('draftBar').hidden = false;
  $('draftDiscard').addEventListener('click', () => {
    localStorage.removeItem('muntaqa-draft');
    location.reload();
  });
}

/* ═══════ تشغيل ═══════ */
$('statCount').textContent = PRODUCTS.length;
renderCats();
renderChips();
renderTrend();
renderProducts();
renderFavs();
tickCountdown();
setInterval(tickCountdown, 1000);
observeReveals();
