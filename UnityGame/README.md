# 🧟 صايح — لعبة رعب كوميدية أونلاين

لعبة أونلاين مع الربع: واحد عشوائي يصير **الوحش** 🧟 والباقين **عيال** يحاولون يخلصون
مهام مضحكة ويهربون. المشكلة؟ **كل مهمة تسوي ضجة... والوحش يسمعها!** 😂

---

## 🎮 فكرة اللعب

| الدور | وش يسوي |
|---|---|
| 🧟 **الوحش** (لاعب عشوائي واحد) | يطلع بشكل عشوائي من 3 وحوش، محبوس أول 10 ثواني، بعدها يصيد. يشوف **سهم أحمر** على شاشته يدله على أي ضجة! |
| 🏃 **العيال** (الباقين) | يخلصون 6 مهام مضحكة → يفتح باب الهروب → ينجون. اللي ينمسك يصير شبح 👻 يتفرج |

**الركض نفسه يسوي ضجة** — فامشِ بهدوء... أو اركض وتحمل العواقب 😈

### المهام الست المضحكة
1. 😱 **صايح بأعلى صوتك** — اضغط Space بسرعة 10 مرات... وكل صيحة توصل للوحش!
2. 🎤 **حفلة كاريوكي** — اضغط الحرف الصح (J/K/L)، وكل نشاز = ضجة قوية
3. 🕺 **ارقص على المنصة** — اضغط الأسهم وارقص على موسيقى صاخبة
4. 🍜 **اطبخ النودلز** — قلّب بـ E كل شوي، نسيت؟ إنذار الدخان يفضحك 🚨
5. 🐔 **امسك الدجاجة** — دجاجة هاربة تصارخ بأعلى صوتها وهي تهرب منك
6. 🚽 **صلّح دورة المياه المسكونة** — اضغط E بإيقاع هادئ، استعجلت = انفجار سيفون

---

## 🛠️ خطوات التركيب (من الصفر للعب)

### 1) جهز المشروع
1. افتح **Unity Hub** → مشروع جديد **3D (URP أو HDRP)** — Unity 2022.3 أو أحدث
2. من **Window → Package Manager** ثبّت:
   - `Netcode for GameObjects` (com.unity.netcode.gameobjects)
   - `Multiplayer Services` (com.unity.services.multiplayer)
   - `Multiplayer Widgets` — هذي أسيت **Multiplayer Session** اللي شريته
3. من **Edit → Project Settings → Services**: اربط المشروع بحسابك
   (نفس حساب `alwaleed305xx` اللي فيه الأسيتات) وفعّل خدمة **Multiplayer / Relay**
4. انسخ مجلد `Assets/Scripts` من هنا داخل مجلد `Assets` بمشروعك

### 2) نزّل أسيتاتك من My Assets
من **Window → Package Manager → My Assets** نزّل واستورد:
- 🧟 **FREE Zombie Male AAB** ← الوحش رقم 1
- 👹 **Morbid Creatures: Mutant** ← الوحش رقم 2
- 🕷️ **Mimic prototype** ← الوحش رقم 3
- 🏠 **VINTAGE LIVING ROOM 3D GAME PACK** ← الخريطة (بيت مسكون — مثالي!)
- 🏘️ **lowpoly medieval buildings** أو **The Wasteland LITE** ← خرائط إضافية بعدين

### 3) اضبط NetworkManager
1. GameObject فاضي بالمشهد → سمّه `NetworkManager` → أضف مكوّن **NetworkManager**
2. اختر **Unity Transport** كـ Transport
3. خانة **Player Prefab** خلّها **فاضية** (GameManager هو اللي ينزّل الشخصيات حسب الدور)

### 4) سوّ بريفاب الولد (Survivor)
1. كبسولة بسيطة (أو أي شخصية عندك) → سمّها `Survivor`
2. أضف عليها: `CharacterController` + `NetworkObject` + `NetworkTransform` + سكربت `PlayerController`
3. في **NetworkTransform**: خلّ الصلاحية للمالك (Owner/Client Authoritative)
4. سوّ ابن فاضي اسمه `CameraHolder` على مستوى الراس (Y ≈ 1.6) واربطه بخانة `cameraHolder`
5. اسحبها لمجلد واحفظها كبريفاب

### 5) سوّ بريفاب الوحش (Monster) ⭐
1. نفس خطوات Survivor بس بسكربتات `MonsterController` + `MonsterSkinSelector`
2. اسحب **داخل البريفاب** 3 أبناء:
   - موديل الزومبي من أسيت Zombie Male
   - موديل المسخ من أسيت Morbid Creatures
   - موديل الميميك من أسيت Mimic prototype
3. اربط الثلاثة في مصفوفة `skins` بسكربت `MonsterSkinSelector`
4. كل جولة يطلع الوحش بشكل عشوائي منهم — تلقائياً! 🎲

### 6) سجّل البريفابات بالشبكة
في `NetworkManager` → **Network Prefabs List**: أضف بريفاب `Survivor` و `Monster`
وكل مهمة بتسويها كـ NetworkObject.

### 7) ركّب الخريطة والمهام
1. اسحب غرف **Vintage Living Room** وركّب بيت مسكون (غرف + ممرات + إضاءة خافتة 🕯️)
2. GameObject فاضي اسمه `GameManager` → أضف عليه `GameManager` + `NoiseSystem` + `NetworkObject`
   - اربط بريفابات Survivor/Monster ونقاط الظهور (Empty objects وزّعها بالبيت)
3. وزّع المهام الست: على كل جسم مناسب (مايك، قدر، مرحاض...) أضف `NetworkObject` + Collider + سكربت المهمة:
   `TaskScream` / `TaskKaraoke` / `TaskDance` / `TaskNoodles` / `TaskChicken` / `TaskToilet`
   - الدجاجة 🐔 تحتاج `NetworkTransform` بعد عشان حركتها تتزامن
4. GameObject اسمه `TaskManager` → سكربت `TaskManager` → اربط المهام الست وباب الهروب
5. باب عند المخرج → Collider + سكربت `EscapeDoor`

### 8) الواجهة (Canvas)
1. Canvas → أضف سكربت `GameUI` واربط العناصر:
   - لوحة كشف الدور + نص
   - نص الأحداث أعلى الشاشة
   - لوحة المهمة (اسم + وصف + تلميح + Slider تقدم)
   - نص عداد المهام
   - **صورة سهم** بنص الشاشة + نص (هذا اللي يدل الوحش على الضجة!)
   - لوحة نهاية الجولة + نص
2. أضف ويدجت **Create Session** و **Join Session by Code** من أسيت Multiplayer Widgets
   (سحب وإفلات — تتصل بـ Netcode تلقائياً وتعطيك كود غرفة تشاركه مع الربع)
3. زر "🎮 ابدأ الجولة" → اربطه بدالة `GameManager.StartRound` (يشتغل عند المضيف فقط)

### 9) اختبار سريع بدون نت
ثبّت **Multiplayer Play Mode** (com.unity.multiplayer.playmode) من Package Manager —
يخليك تشغل نسختين من اللعبة بنفس الجهاز وتجرب الوحش والولد مع بعض.

---

## 🎛️ أزرار التحكم

| الزر | الفعل |
|---|---|
| WASD | حركة |
| Shift | ركض (⚠️ يسوي ضجة!) |
| Space | قفز / صياح بالمهمة / طيران الشبح |
| E | تفاعل مع مهمة أو باب |
| Q | إلغاء المهمة والهرب 🐔 |
| كلك يسار | هجوم الوحش |

## ⚖️ موازنة اللعب (عدّلها من الـ Inspector)
- سرعة الوحش `8.2` مقابل ركض الولد `7.5` — الوحش أسرع بشوي بس
- حبس الوحش أول `10` ثواني
- قوة ضجة كل مهمة قابلة للتعديل (`noiseLoudness` من 0 إلى 1)

استانسوا! 🎉 وإذا تبي تزيد مهام، ورّث من `TaskBase` وشوف الأمثلة في `FunnyTasks.cs`
