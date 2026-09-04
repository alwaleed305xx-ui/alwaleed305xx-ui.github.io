/* ===== مُنتقى — منطق المتجر (نموذج التسويق بالعمولة) ===== */
'use strict';

/* ═══════════════════════════════════════════════════════════
   إعدادات العمولة — عبّيها مرة وحدة وكل روابط الموقع تشتغل تلقائياً
   ─────────────────────────────────────────────────────────────
   1) أمازون السعودية: سجّل في برنامج Amazon Associates من
      affiliate-program.amazon.sa وحط وسمك هنا (مثال: muntaqa-21)
   2) علي إكسبرس: سجّل في portals.aliexpress.com وحط مفتاح
      الرابط المختصر (aff_short_key) هنا
   3) نون: التسجيل عبر شبكات مثل ArabClicks — حط رابط التتبع
      الأساسي هنا ويُضاف رابط المنتج له تلقائياً
   ═══════════════════════════════════════════════════════════ */
const AFF = {
  amazonTag: '',        // ← وسم أمازون حقك، مثال: 'muntaqa-21'
  aliShortKey: '',      // ← مفتاح علي إكسبرس، مثال: 'AbCdEfG'
  noonTracking: '',     // ← رابط تتبع نون من ArabClicks (يترك فاضي إذا ما سجلت)
};

/* الأقسام — مبنية على أكثر النيشات طلباً في السعودية */
const CATS = [
  { id: 'trend',     name: 'ترند الآن',         icon: '🔥', grad: 'linear-gradient(120deg,#e0492e,#e8842c)', desc: 'المنتجات اللي كاسحة تيك توك وسناب حالياً' },
  { id: 'coffee',    name: 'ركن القهوة',        icon: '☕', grad: 'linear-gradient(135deg,#8d6142,#5c3d27)', desc: 'قهوة مختصة وسعودية' },
  { id: 'fragrance', name: 'العطور والبخور',    icon: '🧿', grad: 'linear-gradient(135deg,#7b5ea7,#4b3178)', desc: 'مباخر ومعطرات' },
  { id: 'camping',   name: 'الكشتات والرحلات',  icon: '🏕️', grad: 'linear-gradient(135deg,#c98a3d,#8a5a22)', desc: 'عدة البر كاملة' },
  { id: 'tech',      name: 'الإلكترونيات',      icon: '🎧', grad: 'linear-gradient(135deg,#3f6fd8,#243f8f)', desc: 'سماعات وساعات وشواحن' },
  { id: 'home',      name: 'المنزل الذكي',      icon: '🏠', grad: 'linear-gradient(135deg,#3d9e8c,#20685b)', desc: 'أجهزة وإضاءة' },
  { id: 'car',       name: 'السيارة',           icon: '🚗', grad: 'linear-gradient(135deg,#697586,#3c4654)', desc: 'إكسسوارات وعناية' },
  { id: 'care',      name: 'العناية الشخصية',   icon: '💆', grad: 'linear-gradient(135deg,#d87ba0,#a04468)', desc: 'أجهزة عناية وجمال' },
  { id: 'fitness',   name: 'الرياضة واللياقة',  icon: '💪', grad: 'linear-gradient(135deg,#e0913f,#a85f18)', desc: 'أدوات التمرين' },
];

/* المنتجات — price تقريبي بالريال للمقارنة، q كلمة البحث في المتاجر */
const PRODUCTS = [
  // ترند الآن 🔥
  { id: 31, cat: 'trend', name: 'مروحة رقبة محمولة بدون شفرات', emoji: '🌬️', price: 59, old: 95, rating: 4.6, sold: 5240, badge: 'trend',
    q: 'portable neck fan bladeless',
    desc: 'ترند الصيف الأول في الخليج — مروحة تلبس حول الرقبة، بدون شفرات وآمنة على الشعر، تبرد وجهك ورقبتك وأنت تمشي.',
    feats: ['بدون شفرات — آمنة تماماً', '3 سرعات وتشغيل هادئ', 'بطارية تدوم حتى 8 ساعات', 'خفيفة ومريحة للبس الطويل'] },
  { id: 32, cat: 'trend', name: 'جهاز عرض المجرة والنجوم (رائد الفضاء)', emoji: '🌌', price: 89, old: 145, rating: 4.7, sold: 4680, badge: 'trend',
    q: 'astronaut galaxy projector',
    desc: 'المنتج اللي كسر تيك توك — رائد فضاء يحول سقف غرفتك لمجرة نجوم متحركة بالريموت. هدية وديكور بنفس الوقت.',
    feats: ['سديم ونجوم متحركة بألوان متعددة', 'ريموت + مؤقت نوم', 'رأس يتحرك 360 درجة', 'الهدية الأكثر طلباً للغرف'] },
  { id: 33, cat: 'trend', name: 'خلاط عصير محمول USB', emoji: '🥭', price: 69, old: 109, rating: 4.5, sold: 6120, badge: 'trend',
    q: 'portable blender usb rechargeable',
    desc: 'الترند اللي ما مات — اعصر سموذي طازج في المكتب والنادي والسيارة. شحنة وحدة تكفي 15 كوب.',
    feats: ['6 شفرات ستانلس قوية', 'يكسر الثلج والفواكه المجمدة', 'سهل التنظيف — اغسله بضغطة', 'سعة 450 مل تكفي كوب كبير'] },
  { id: 34, cat: 'trend', name: 'مصباح الغروب Sunset Lamp', emoji: '🌇', price: 49, old: 79, rating: 4.4, sold: 3890, badge: 'trend',
    q: 'sunset lamp projector',
    desc: 'إضاءة الغروب اللي غزت السناب — تعطي غرفتك أجواء غروب ذهبية مثالية للتصوير والجلسات.',
    feats: ['16 لون بدرجات الغروب', 'دوران 180 درجة', 'مثالي لتصوير المحتوى', 'شحن USB بسيط'] },
  { id: 35, cat: 'trend', name: 'كوب حافظ للبرودة 1.2 لتر مع مصاصة', emoji: '🧊', price: 79, old: 125, rating: 4.8, sold: 7350, badge: 'trend',
    q: 'insulated tumbler with straw 40oz',
    desc: 'الكوب اللي صار ستايل حياة — يحافظ على الثلج 24 ساعة كاملة، بمقبض ومصاصة، رفيق الدوام والجيم والمشاوير.',
    feats: ['الثلج يظل ثلج 24 ساعة', 'مقبض مريح ومصاصة مدمجة', 'يدخل حامل أكواب السيارة', 'ستانلس ستيل صحي'] },
  { id: 36, cat: 'trend', name: 'مسبحة ذكية رقمية (خاتم)', emoji: '📿', price: 39, old: 65, rating: 4.7, sold: 5860, badge: 'trend',
    q: 'مسبحة الكترونية خاتم رقمي',
    desc: 'ترند سعودي بامتياز — خاتم مسبحة رقمي بشاشة LED وعداد ذكي، وصار الهدية الأولى للأهل والكبار.',
    feats: ['عداد رقمي بشاشة واضحة', 'اهتزاز خفيف كل 100 تسبيحة', 'بطارية تدوم أسابيع', 'مقاسات قابلة للتعديل'] },
  { id: 37, cat: 'trend', name: 'طابعة صور جيب محمولة', emoji: '📸', price: 149, old: 219, rating: 4.5, sold: 2980, badge: 'trend',
    q: 'mini portable photo printer',
    desc: 'اطبع ذكرياتك بثواني — طابعة جيب تطبع صور جوالك فوراً بدون حبر (تقنية حرارية)، ترند الهدايا الحالي.',
    feats: ['طباعة بدون حبر — ورق حراري', 'تتصل بالجوال بلوتوث', 'ملصقات قابلة للصق', 'حجم الجيب — خذها بكل مكان'] },
  { id: 38, cat: 'trend', name: 'مايك كاريوكي بلوتوث مع سبيكر', emoji: '🎤', price: 79, old: 129, rating: 4.4, sold: 3410, badge: 'trend',
    q: 'karaoke microphone bluetooth speaker',
    desc: 'نجم الجلسات العائلية — مايك وسبيكر ببعض، غنّ مع أي أغنية من جوالك بمؤثرات صوتية.',
    feats: ['سبيكر مدمج بصوت قوي', 'مؤثرات وتغيير الصوت', 'يتصل بأي جوال بلوتوث', 'بطارية تكفي 6 ساعات غناء'] },
  // ركن القهوة
  { id: 1,  cat: 'coffee', name: 'ماكينة قهوة إسبريسو محمولة', emoji: '☕', price: 149, old: 229, rating: 4.8, sold: 2140, badge: 'hot',
    q: 'portable espresso maker',
    desc: 'رفيقة الدوام والسفر — إسبريسو بضغط 18 بار من كبسولة أو قهوة مطحونة، بدون كهرباء.',
    feats: ['ضغط 18 بار لكريما مثالية', 'تشتغل يدوياً بدون كهرباء', 'حجم صغير يناسب الشنطة والسيارة', 'متوافقة مع الكبسولات والقهوة المطحونة'] },
  { id: 2,  cat: 'coffee', name: 'مطحنة قهوة كهربائية محمولة', emoji: '🫘', price: 119, old: 179, rating: 4.7, sold: 1680, badge: 'sale',
    q: 'electric portable coffee grinder',
    desc: 'مطحنة سيراميك بشحن USB-C ودرجات طحن متعددة من التركي إلى الفرنش برس.',
    feats: ['أحجار طحن سيراميك دقيقة', '36 درجة طحن قابلة للضبط', 'بطارية تدوم لأسبوع استخدام', 'هادئة وسهلة التنظيف'] },
  { id: 3,  cat: 'coffee', name: 'طقم القهوة السعودية (دلة وفناجيل)', emoji: '🏺', price: 189, old: 259, rating: 4.9, sold: 950, badge: 'hot',
    q: 'طقم دلة قهوة عربية',
    desc: 'دلة نحاسية بنقوش تراثية مع 6 فناجيل مذهّبة — يليق بمجلسك وضيوفك.',
    feats: ['دلة سعة 1 لتر بتشطيب فاخر', '6 فناجيل بحواف ذهبية', 'علبة هدية أنيقة', 'مقاومة للحرارة العالية'] },
  { id: 4,  cat: 'coffee', name: 'ميزان قهوة رقمي مع مؤقت', emoji: '⚖️', price: 99, old: 149, rating: 4.6, sold: 720, badge: null,
    q: 'coffee scale with timer',
    desc: 'دقة 0.1 جرام مع مؤقت مدمج — الأداة الأساسية لكل مهتم بالقهوة المختصة.',
    feats: ['دقة حتى 0.1 جرام', 'مؤقت مدمج للتقطير', 'شاشة LED واضحة', 'سطح مقاوم للحرارة والماء'] },
  // العطور والبخور
  { id: 5,  cat: 'fragrance', name: 'مبخرة كهربائية محمولة USB', emoji: '🧿', price: 69, old: 109, rating: 4.8, sold: 3250, badge: 'hot',
    q: 'مبخرة الكترونية محمولة',
    desc: 'الأكثر طلباً — مبخرة جيب تشتغل بالشحن، للعود والبخور، للمجلس والسيارة والشماغ.',
    feats: ['شحن USB يدوم حتى ساعتين', 'حجم الجيب — خذها وين ما رحت', 'أمان كامل بدون فحم', 'ثلاث درجات حرارة'] },
  { id: 6,  cat: 'fragrance', name: 'معطر جو ذكي بالريموت', emoji: '💨', price: 89, old: 139, rating: 4.5, sold: 1120, badge: null,
    q: 'automatic aroma diffuser',
    desc: 'موزع عطر أوتوماتيكي بفتحات رش قابلة للجدولة — استقبال معطر دائم لبيتك.',
    feats: ['جدولة أوقات الرش', 'ريموت تحكم عن بعد', 'يغطي حتى 60 متر مربع', 'يعمل بأي زيت عطري'] },
  { id: 7,  cat: 'fragrance', name: 'مبخرة سيارة على فتحة التهوية', emoji: '🚘', price: 49, old: 79, rating: 4.4, sold: 1870, badge: 'sale',
    q: 'معطر سيارة فتحة التكييف',
    desc: 'عبق دائم في سيارتك — تركب على فتحة المكيف وتوزع الرائحة مع الهواء.',
    feats: ['تثبيت بثواني على المكيف', 'تصميم معدني فاخر', 'تجي مع 3 روائح مجانية', 'لا تحتاج شحن أو بطارية'] },
  { id: 8,  cat: 'fragrance', name: 'ستاند مباخر فاخر مع درج', emoji: '🕌', price: 129, old: 189, rating: 4.7, sold: 640, badge: 'new',
    q: 'ستاند مبخرة خشبي',
    desc: 'ستاند خشبي بتصميم تراثي لمبخرتك مع درج لتخزين العود — قطعة ديكور بحد ذاتها.',
    feats: ['خشب طبيعي بتشطيب يدوي', 'درج مدمج لتخزين العود', 'قاعدة عازلة للحرارة', 'يناسب جميع المباخر'] },
  // الكشتات والرحلات
  { id: 9,  cat: 'camping', name: 'كرسي رحلات قابل للطي مع حامل كوب', emoji: '🪑', price: 119, old: 179, rating: 4.8, sold: 2560, badge: 'hot',
    q: 'folding camping chair',
    desc: 'كرسي الكشتة الأسطوري — هيكل فولاذي يتحمل 150 كيلو ويطوى بحجم شنطة صغيرة.',
    feats: ['يتحمل حتى 150 كجم', 'حامل كوب ومسند ذراعين', 'يطوى خلال 5 ثواني', 'شنطة حمل مجانية'] },
  { id: 10, cat: 'camping', name: 'إضاءة تخييم قابلة للشحن 3 أوضاع', emoji: '🏮', price: 69, old: 105, rating: 4.6, sold: 1930, badge: null,
    q: 'rechargeable camping light',
    desc: 'كشاف تخييم قوي بثلاث درجات إضاءة وبطارية تدوم طوال الليلة — مع خطاف للتعليق.',
    feats: ['بطارية 10000mAh تشحن جوالك', 'ثلاث درجات + وضع الطوارئ', 'مقاوم للغبار والرذاذ', 'خطاف ومغناطيس للتثبيت'] },
  { id: 11, cat: 'camping', name: 'ترمس حراري ستانلس 1 لتر', emoji: '🫖', price: 85, old: 125, rating: 4.7, sold: 1440, badge: 'sale',
    q: 'ترمس حراري ستانلس ستيل',
    desc: 'يحفظ حرارة قهوتك 12 ساعة وبرودة مويتك 24 ساعة — ستانلس ستيل طبقتين.',
    feats: ['عزل مزدوج بتفريغ هوائي', 'حرارة 12 ساعة / برودة 24', 'غطاء يتحول لكوب', 'ضد التسريب 100%'] },
  { id: 12, cat: 'camping', name: 'شبكة إضاءة نجوم للمخيم 10م', emoji: '✨', price: 59, old: 95, rating: 4.5, sold: 880, badge: 'new',
    q: 'camping string lights usb',
    desc: 'حبل إضاءة دافئة 10 أمتار يشتغل بالشحن — يحول جلستك في البر للوحة.',
    feats: ['10 أمتار إضاءة دافئة', '8 أوضاع وميض', 'شحن USB مع بطارية مدمجة', 'مقاومة للعوامل الجوية'] },
  // الإلكترونيات
  { id: 13, cat: 'tech', name: 'سماعات بلوتوث لاسلكية برو', emoji: '🎧', price: 89, old: 149, rating: 4.7, sold: 4120, badge: 'hot',
    q: 'wireless earbuds anc',
    desc: 'عزل ضوضاء وصوت نقي وبطارية 30 ساعة مع علبة الشحن — الأكثر طلباً في قسم التقنية.',
    feats: ['عزل ضوضاء نشط ANC', '30 ساعة مع علبة الشحن', 'مقاومة للماء IPX5', 'لمس ذكي والتوصيل بجهازين'] },
  { id: 14, cat: 'tech', name: 'ساعة ذكية رياضية AMOLED', emoji: '⌚', price: 129, old: 219, rating: 4.6, sold: 2870, badge: 'sale',
    q: 'smart watch amoled',
    desc: 'شاشة AMOLED ساطعة، تتبع نبض ونوم و100+ وضع رياضي، مكالمات بلوتوث.',
    feats: ['شاشة AMOLED عالية السطوع', 'مكالمات بلوتوث من الساعة', 'تتبع النبض والأكسجين والنوم', 'بطارية تدوم 7 أيام'] },
  { id: 15, cat: 'tech', name: 'شاحن متنقل 20000mAh شحن سريع', emoji: '🔋', price: 79, old: 119, rating: 4.8, sold: 3340, badge: null,
    q: 'power bank 20000mah fast charging',
    desc: 'باور بانك بسعة تشحن جوالك 4 مرات، بشحن سريع 22.5W وشاشة رقمية للنسبة.',
    feats: ['سعة 20000mAh حقيقية', 'شحن سريع PD 22.5W', 'شاشة رقمية لنسبة البطارية', 'يشحن 3 أجهزة معاً'] },
  { id: 16, cat: 'tech', name: 'ستاند لابتوب ألمنيوم قابل للتعديل', emoji: '💻', price: 75, old: 115, rating: 4.5, sold: 990, badge: null,
    q: 'aluminum laptop stand adjustable',
    desc: 'ارفع شاشتك لمستوى النظر — ألمنيوم خفيف بزوايا قابلة للتعديل وتهوية ممتازة.',
    feats: ['ألمنيوم طيران خفيف', '6 مستويات ارتفاع', 'يطوى بحجم كتاب', 'يناسب حتى 17 بوصة'] },
  // المنزل الذكي
  { id: 17, cat: 'home', name: 'مرطب هواء بإضاءة RGB هادئ', emoji: '💧', price: 79, old: 129, rating: 4.6, sold: 2210, badge: 'sale',
    q: 'humidifier rgb light',
    desc: 'رذاذ ناعم مع إضاءة ليلية متغيرة الألوان — أجواء هادئة لغرفتك ومكتبك.',
    feats: ['سعة 500 مل تدوم 10 ساعات', 'إضاءة RGB قابلة للتثبيت', 'صامت تماماً أثناء النوم', 'إيقاف تلقائي عند نفاد الماء'] },
  { id: 18, cat: 'home', name: 'مصباح القمر ثلاثي الأبعاد 16 لون', emoji: '🌙', price: 69, old: 99, rating: 4.7, sold: 1560, badge: null,
    q: 'moon lamp 3d 16 colors',
    desc: 'قمر مطبوع 3D بملمس حقيقي و16 لون بالريموت — هدية ما تفشل وديكور خيالي.',
    feats: ['طباعة 3D بتفاصيل سطح القمر', '16 لون مع ريموت', 'قاعدة خشبية أنيقة', 'شحن USB يدوم 8 ساعات'] },
  { id: 19, cat: 'home', name: 'مكنسة لاسلكية محمولة قوية', emoji: '🌀', price: 139, old: 199, rating: 4.5, sold: 1180, badge: null,
    q: 'handheld cordless vacuum',
    desc: 'شفط قوي 120W بحجم صغير — للكنب والسيارة والزوايا الصعبة، مع 3 رؤوس.',
    feats: ['قوة شفط 120W', 'فلتر HEPA قابل للغسل', '3 رؤوس لكل الاستخدامات', 'شحن سريع USB-C'] },
  { id: 20, cat: 'home', name: 'شريط إضاءة LED ذكي 5 متر', emoji: '🌈', price: 59, old: 99, rating: 4.4, sold: 2650, badge: 'sale',
    q: 'smart led strip 5m',
    desc: 'غيّر أجواء غرفتك — 16 مليون لون بالتطبيق مع مزامنة الموسيقى.',
    feats: ['تحكم كامل من التطبيق', 'مزامنة مع الموسيقى', 'يقص ويلصق بأي مكان', 'مؤقتات وجدولة تلقائية'] },
  // السيارة
  { id: 21, cat: 'car', name: 'مكنسة سيارة لاسلكية قوية', emoji: '🧹', price: 99, old: 159, rating: 4.6, sold: 1720, badge: 'hot',
    q: 'car vacuum cleaner cordless',
    desc: 'نظف سيارتك بدقايق — شفط 9000Pa لاسلكي مع إضاءة LED للزوايا المظلمة.',
    feats: ['قوة شفط 9000Pa', 'لاسلكية بشحن USB-C', 'إضاءة LED مدمجة', 'فلتر ستانلس قابل للغسل'] },
  { id: 22, cat: 'car', name: 'حامل جوال مغناطيسي للسيارة', emoji: '🧲', price: 39, old: 69, rating: 4.7, sold: 3980, badge: 'sale',
    q: 'magnetic car phone holder',
    desc: 'مغناطيس N52 قوي يثبت جوالك بيد وحدة — دوران 360 وتصميم معدني صغير.',
    feats: ['مغناطيس N52 فائق القوة', 'دوران 360 درجة', 'يركب على التهوية أو الطبلون', 'يناسب جميع الجوالات'] },
  { id: 23, cat: 'car', name: 'شاحن سيارة سريع 65W منفذين', emoji: '⚡', price: 45, old: 75, rating: 4.5, sold: 2140, badge: null,
    q: 'car charger 65w usb c',
    desc: 'اشحن جوالك ولابتوبك من ولاعة السيارة — PD 65W مع منفذين USB-C وUSB-A.',
    feats: ['قوة إجمالية 65W', 'منفذ USB-C PD ومنفذ USB-A', 'حماية من الحرارة الزائدة', 'معدن صغير بإضاءة خفيفة'] },
  { id: 24, cat: 'car', name: 'منظم سيارة جلد بين المقاعد', emoji: '🧰', price: 55, old: 89, rating: 4.4, sold: 1310, badge: null,
    q: 'car seat gap organizer leather',
    desc: 'وداعاً للفوضى — منظم جلد فاخر يسد فراغ الكونسول ويخزن أغراضك الصغيرة.',
    feats: ['جلد صناعي فاخر سهل التنظيف', 'جيوب للجوال والبطاقات', 'حامل أكواب إضافي', 'يركب بدون أدوات'] },
  // العناية الشخصية
  { id: 25, cat: 'care', name: 'جهاز تنظيف البشرة بالسيليكون', emoji: '🧖', price: 59, old: 95, rating: 4.5, sold: 1490, badge: null,
    q: 'silicone facial cleansing brush',
    desc: 'تنظيف عميق بالاهتزاز الصوتي — سيليكون طبي ناعم يناسب كل أنواع البشرة.',
    feats: ['8000 اهتزاز صوتي بالدقيقة', 'سيليكون طبي مضاد للبكتيريا', 'مقاوم للماء بالكامل', 'شحنة تدوم 30 استخدام'] },
  { id: 26, cat: 'care', name: 'مشط لحية كهربائي حراري', emoji: '🧔', price: 79, old: 119, rating: 4.6, sold: 1080, badge: 'new',
    q: 'heated beard straightener comb',
    desc: 'لحية مرتبة بدقايق — تسخين سريع بحماية سيراميك يصفف بدون تقصف.',
    feats: ['تسخين خلال 30 ثانية', 'طلاء سيراميك يحمي الشعر', '3 درجات حرارة', 'يشتغل بالشحن — للسفر'] },
  { id: 27, cat: 'care', name: 'مجفف شعر أيوني سريع', emoji: '💇', price: 129, old: 189, rating: 4.7, sold: 860, badge: null,
    q: 'ionic hair dryer',
    desc: 'تجفيف سريع بتقنية الأيونات ضد الهيشان — خفيف وقوي بتصميم عصري.',
    feats: ['تقنية أيونية ضد الهيشان', 'محرك سريع خفيف الوزن', '3 درجات حرارة ودرجتين هواء', 'فوهة تركيز مغناطيسية'] },
  // الرياضة
  { id: 28, cat: 'fitness', name: 'طقم حبال مقاومة 5 مستويات', emoji: '🏋️', price: 49, old: 85, rating: 4.5, sold: 1670, badge: 'sale',
    q: 'resistance bands set',
    desc: 'جيم كامل في شنطة صغيرة — 5 حبال بمستويات مختلفة مع مقابض وأحزمة.',
    feats: ['5 مستويات مقاومة حتى 68 كجم', 'مقابض ومرابط للباب', 'لاتكس طبيعي متين', 'شنطة حمل مجانية'] },
  { id: 29, cat: 'fitness', name: 'زجاجة ماء ذكية بشاشة حرارة', emoji: '🥤', price: 45, old: 69, rating: 4.4, sold: 1240, badge: null,
    q: 'smart water bottle temperature display',
    desc: 'شاشة LED تعرض حرارة مشروبك بلمسة — ستانلس معزول يحافظ على البرودة 24 ساعة.',
    feats: ['شاشة حرارة LED باللمس', 'عزل 24 ساعة برودة', 'ستانلس 316 صحي', 'ضد التسريب تماماً'] },
  { id: 30, cat: 'fitness', name: 'بساط يوغا سميك ضد الانزلاق', emoji: '🧘', price: 69, old: 105, rating: 4.6, sold: 930, badge: null,
    q: 'yoga mat 10mm non slip',
    desc: 'سماكة 10مم مريحة للمفاصل بوجهين ضد الانزلاق — مع حزام حمل.',
    feats: ['سماكة 10مم مريحة', 'وجهان ضد الانزلاق', 'خامة TPE صديقة للبيئة', 'حزام حمل مجاني'] },
];

/* ===== بناء روابط المتاجر (مع وسم العمولة إذا موجود) ===== */
const SHOPS = {
  amazon: {
    name: 'أمازون السعودية', icon: '🟠', note: 'توصيل 1-4 أيام · دفع عند الاستلام',
    url: p => {
      let u = `https://www.amazon.sa/s?k=${encodeURIComponent(p.q)}`;
      if (AFF.amazonTag) u += `&tag=${encodeURIComponent(AFF.amazonTag)}`;
      return u;
    },
  },
  ali: {
    name: 'علي إكسبرس', icon: '🔴', note: 'أرخص سعر · توصيل 10-20 يوم',
    url: p => {
      const target = `https://ar.aliexpress.com/wholesale?SearchText=${encodeURIComponent(p.q)}`;
      if (AFF.aliShortKey)
        return `https://s.click.aliexpress.com/deep_link.htm?aff_short_key=${encodeURIComponent(AFF.aliShortKey)}&dl_target_url=${encodeURIComponent(target)}`;
      return target;
    },
  },
  noon: {
    name: 'نون', icon: '🟡', note: 'توصيل سريع داخل المملكة',
    url: p => {
      const target = `https://www.noon.com/saudi-ar/search/?q=${encodeURIComponent(p.q)}`;
      if (AFF.noonTracking) return AFF.noonTracking + encodeURIComponent(target);
      return target;
    },
  },
};

function openShop(shopId, productId) {
  const p = PRODUCTS.find(x => x.id == productId);
  const shop = SHOPS[shopId];
  if (!p || !shop) return;
  window.open(shop.url(p), '_blank', 'noopener');
}

/* ===== أدوات مساعدة ===== */
const gradOf = p => CATS.find(c => c.id === p.cat)?.grad || 'var(--primary-soft)';
const catName = id => CATS.find(c => c.id === id)?.name || '';
const rial = n => `${n} <small>ر.س</small>`;
const offPct = p => p.old ? Math.round((1 - p.price / p.old) * 100) : 0;
const stars = r => '★'.repeat(Math.round(r)) + '☆'.repeat(5 - Math.round(r));
const BADGES = { hot: ['hot', 'الأكثر طلباً 🔥'], new: ['new', 'جديد'], sale: ['sale', 'سعر ممتاز'], trend: ['trend', 'ترند 🔥'] };

/* ===== الحالة ===== */
let favs = [];
try { favs = JSON.parse(localStorage.getItem('muntaqa-favs') || '[]'); } catch (e) { favs = []; }
let activeCat = 'all';
let query = '';
let sortBy = 'popular';

const $ = id => document.getElementById(id);
const isFav = id => favs.includes(Number(id));

/* ===== بطاقة منتج ===== */
function productCard(p) {
  const badge = p.badge ? `<span class="p-badge ${BADGES[p.badge][0]}">${BADGES[p.badge][1]}</span>` : '';
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
      <div class="p-rating"><span class="stars">${stars(p.rating)}</span> ${p.rating} · ${p.sold.toLocaleString('en')}+ طلب</div>
      <div class="p-price-row"><span class="p-price">${rial(p.price)}</span>${old}</div>
      <button class="p-add" data-view="${p.id}">
        اطلب الآن
        <svg viewBox="0 0 24 24" width="16" height="16" style="transform:scaleX(-1)"><path d="M7 17 17 7M9 7h8v8" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
      </button>
    </div>
  </article>`;
}

/* ===== عرض الأقسام ===== */
function renderCats() {
  $('catsGrid').innerHTML = CATS.map(c => `
    <div class="cat-card ${c.id === 'trend' ? 'cat-trend' : ''} reveal in" data-cat="${c.id}">
      <div class="cat-icon" style="background:${c.grad};filter:saturate(.9)"><span style="filter:drop-shadow(0 4px 6px rgba(0,0,0,.3))">${c.icon}</span></div>
      <b>${c.name}</b><span>${c.desc}</span>
      ${c.id === 'trend' ? '<em class="cat-trend-cta">استكشف الترند ←</em>' : ''}
    </div>`).join('');
}

/* ===== عرض المنتجات ===== */
function visibleProducts() {
  let list = PRODUCTS.filter(p =>
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

function renderBest() {
  const best = PRODUCTS.filter(p => p.cat !== 'trend').sort((a, b) => b.sold - a.sold).slice(0, 4);
  $('bestGrid').innerHTML = best.map(productCard).join('');
}

function renderChips() {
  const all = [{ id: 'all', name: 'الكل' }, ...CATS];
  $('filterChips').innerHTML = all.map(c =>
    `<button class="chip ${c.id === activeCat ? 'active' : ''}" data-chip="${c.id}">${c.name}</button>`).join('');
}

/* ===== المفضلة ===== */
function saveFavs() { localStorage.setItem('muntaqa-favs', JSON.stringify(favs)); }

function toggleFav(id) {
  id = Number(id);
  const on = isFav(id);
  favs = on ? favs.filter(f => f !== id) : [...favs, id];
  saveFavs();
  renderFavs();
  document.querySelectorAll(`[data-fav="${id}"]`).forEach(b => b.classList.toggle('on', !on));
  const p = PRODUCTS.find(x => x.id === id);
  toast(on ? `💔 شلنا «${p.name}» من مفضلتك` : `❤️ أضفنا «${p.name}» لمفضلتك`);
  $('favBtn').classList.remove('bump');
  void $('favBtn').offsetWidth;
  $('favBtn').classList.add('bump');
}

function renderFavs() {
  const list = favs.map(id => PRODUCTS.find(p => p.id === id)).filter(Boolean);
  $('favCount').textContent = list.length;
  $('drawerCount').textContent = list.length ? `(${list.length})` : '';
  if (!list.length) {
    $('drawerItems').innerHTML = `<div class="cart-empty"><span>💛</span>مفضلتك فاضية…<br>اضغط ♥ على أي منتج يعجبك ويصير هنا</div>`;
    return;
  }
  $('drawerItems').innerHTML = list.map(p => `
    <div class="ci">
      <div class="ci-thumb" style="--grad:${gradOf(p)}">${p.emoji}</div>
      <div class="ci-info">
        <b>${p.name}</b>
        <span class="ci-price">≈ ${p.price} ر.س</span>
      </div>
      <button class="ci-order" data-view="${p.id}">اطلب ↗</button>
      <button class="ci-del" data-unfav="${p.id}" aria-label="حذف">🗑</button>
    </div>`).join('');
}

/* ===== نافذة المنتج (اختيار المتجر) ===== */
function openModal(id) {
  const p = PRODUCTS.find(x => x.id == id);
  if (!p) return;
  const old = p.old ? `<span class="m-old">${p.old} ر.س</span><span class="m-off">وفر حتى ${p.old - p.price} ر.س (-${offPct(p)}%)</span>` : '';
  const shopBtns = Object.entries(SHOPS).map(([sid, s]) => `
    <button class="shop-btn" data-shop="${sid}:${p.id}">
      <span class="shop-ic">${s.icon}</span>
      <span class="shop-txt"><b>اطلبه من ${s.name}</b><small>${s.note}</small></span>
      <span class="shop-go">↖</span>
    </button>`).join('');
  $('modalBody').innerHTML = `
    <div class="m-media" style="--grad:${gradOf(p)}"><span class="m-emoji">${p.emoji}</span></div>
    <div class="m-info">
      <span class="m-cat">${catName(p.cat)}</span>
      <h3>${p.name}</h3>
      <div class="m-rating"><span class="stars">${stars(p.rating)}</span> ${p.rating} من 5 · ${p.sold.toLocaleString('en')}+ طلب</div>
      <p class="m-desc">${p.desc}</p>
      <div class="m-feats">${p.feats.map(f => `<span>${f}</span>`).join('')}</div>
      <div class="m-price-row"><span class="m-price">${rial(p.price)}</span>${old}</div>
      <div class="m-shops">${shopBtns}</div>
      <div class="m-trust"><span>🛡️ الشراء يتم داخل المتجر الرسمي بكل ضماناته</span><span>💵 السعر النهائي يظهر هناك</span></div>
    </div>`;
  $('modalBackdrop').classList.add('show');
  document.body.style.overflow = 'hidden';
}
function closeModal() {
  $('modalBackdrop').classList.remove('show');
  document.body.style.overflow = '';
}

/* ===== درج المفضلة ===== */
function openDrawer() { $('favDrawer').classList.add('open'); $('drawerBackdrop').classList.add('show'); document.body.style.overflow = 'hidden'; }
function closeDrawer() { $('favDrawer').classList.remove('open'); $('drawerBackdrop').classList.remove('show'); document.body.style.overflow = ''; }

/* ===== تنبيهات ===== */
function toast(msg) {
  const el = document.createElement('div');
  el.className = 'toast';
  el.textContent = msg;
  $('toastWrap').appendChild(el);
  setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 300); }, 2200);
}

/* ===== العد التنازلي (يتجدد كل جمعة) ===== */
function tickCountdown() {
  const now = new Date();
  const end = new Date(now);
  end.setDate(now.getDate() + ((5 - now.getDay() + 7) % 7 || 7));
  end.setHours(23, 59, 59, 0);
  let diff = Math.max(0, end - now) / 1000;
  const d = Math.floor(diff / 86400); diff %= 86400;
  const h = Math.floor(diff / 3600); diff %= 3600;
  const m = Math.floor(diff / 60);
  const s = Math.floor(diff % 60);
  $('cdD').textContent = d; $('cdH').textContent = h; $('cdM').textContent = m; $('cdS').textContent = s;
}

/* ===== ظهور تدريجي ===== */
const io = new IntersectionObserver(es => es.forEach(e => e.isIntersecting && e.target.classList.add('in')), { threshold: .12 });
function observeReveals() { document.querySelectorAll('.reveal:not(.in)').forEach(el => io.observe(el)); }

/* ===== الأحداث ===== */
document.addEventListener('click', e => {
  const t = e.target.closest('[data-fav],[data-unfav],[data-view],[data-chip],[data-cat],[data-shop]');
  if (!t) return;
  if (t.dataset.fav) { e.stopPropagation(); toggleFav(t.dataset.fav); }
  else if (t.dataset.unfav) { toggleFav(t.dataset.unfav); }
  else if (t.dataset.shop) {
    const [sid, pid] = t.dataset.shop.split(':');
    openShop(sid, pid);
  }
  else if (t.dataset.view) { closeDrawer(); openModal(t.dataset.view); }
  else if (t.dataset.chip) { activeCat = t.dataset.chip; renderChips(); renderProducts(); }
  else if (t.dataset.cat && t.classList.contains('cat-card')) {
    activeCat = t.dataset.cat; renderChips(); renderProducts();
    document.getElementById('products').scrollIntoView({ behavior: 'smooth' });
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
  if (query) document.getElementById('products').scrollIntoView({ behavior: 'smooth', block: 'start' });
});
$('sortSelect').addEventListener('change', e => { sortBy = e.target.value; renderProducts(); });

/* ترند الأسبوع → فلتر القهوة */
$('offerBtn').addEventListener('click', () => { activeCat = 'coffee'; renderChips(); renderProducts(); });

/* الهيدر عند التمرير + تفعيل رابط القائمة */
const header = $('header');
window.addEventListener('scroll', () => {
  header.classList.toggle('scrolled', window.scrollY > 10);
  const links = document.querySelectorAll('.nav a');
  let current = 'home';
  document.querySelectorAll('section[id]').forEach(sec => {
    if (window.scrollY >= sec.offsetTop - 140) current = sec.id;
  });
  links.forEach(a => a.classList.toggle('active', a.getAttribute('href') === '#' + current));
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

/* ===== تشغيل ===== */
renderCats();
renderChips();
renderBest();
renderProducts();
renderFavs();
tickCountdown();
setInterval(tickCountdown, 1000);
observeReveals();
