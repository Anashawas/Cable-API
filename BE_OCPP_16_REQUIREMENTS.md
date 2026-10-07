# طلبات الباك إند — OCPP 1.6J (قراءة فقط)

> **لمطوّر الباك إند.** كل يلي بهالملف مبني على محاكاة فعلية اشتغلت — مش نظري.
> المختبر: `caasd/ocpp-lab/` · السجل الخام: `ocpp_messages.jsonl` (574 رسالة)
> التاريخ: 2026-09-22

---

## 1. الهدف

تطبيق **كيبل بارتنر** بدّه يعرض لصاحب المحطة:
- حالة كل شاحن وموصل **حيّة**
- تفاصيل الجلسة الشغالة (kW · kWh · الوقت · SoC)
- سجل الجلسات السابقة
- الأعطال
- إحصائيات (kWh اليوم/الأسبوع/الشهر · نسبة التوفر)

## 2. النطاق

| ✅ مطلوب | ❌ خارج النطاق |
|---|---|
| استقبال وتخزين رسائل الشاحن | `RemoteStartTransaction` / `RemoteStopTransaction` |
| endpoints قراءة لتطبيق البارتنر | نظام دفع أو فوترة |
| | `Reset` / `ChangeConfiguration` / `UnlockConnector` |
| | إدارة بطاقات RFID |
| | أي تغيير على تطبيق كيبل B2C |

---

## 3. المعمارية

```
┌──────────┐   WebSocket دائم (wss)   ┌─────────────┐   REST   ┌──────────────┐
│  الشاحن  │ ───────────────────────► │  كيبل CSMS  │ ───────► │ تطبيق البارتنر│
│          │ ◄─────────────────────── │             │          │              │
└──────────┘        ردود فقط          └─────────────┘          └──────────────┘
```

🔴 **الشاحن هو يلي بيتصل فينا** — إحنا السيرفر. ما في webhook، ما في polling.
🔴 **اتصال واحد دائم مفتوح لكل شاحن، 24/7.**

---

## 4. البنية التحتية المطلوبة

| المطلوب | التفاصيل |
|---|---|
| **نقطة النهاية** | `wss://ocpp.cable-app.com/ocpp16/{chargePointId}` |
| **البورت** | **443** — بعض شبكات المحطات بتسكّر غيره |
| **Subprotocol** | `Sec-WebSocket-Protocol: ocpp1.6` (إلزامي — ارفض الاتصال بدونه) |
| **TLS** | شهادة من CA معروفة. بعض الشواحن عندها مكتبات TLS قديمة → **ادعم TLS 1.2** |
| **التوثيق** | HTTP Basic بالـ handshake. حسب المعيار: **`username` = `chargePointId`** |
| **الاستضافة** | ❌ **مش serverless · مش استضافة مشتركة** — بدها VPS/حاوية باتصالات طويلة |
| ⚠️ **Cloudflare** | الـ proxy ممكن يقطع الاتصالات الخاملة — إعداد خاص أو bypass للـ subdomain |
| **المراقبة** | تنبيه لما شاحن يتجاوز فترة الـ Heartbeat ×2.5 بدون رسائل |

**مكتبات جاهزة:** `OCPP.Core` (.NET) · `ocpp` (Python) · `ocpp-rpc` (Node) · `SteVe` (Java)

---

## 5. الجداول

```sql
chargers
  id                  PK
  charge_point_id     UNIQUE, VARCHAR(20)   -- الاسم يلي بينكتب جوّا الشاحن
  station_id          FK  → المحطة الموجودة
  vendor              -- بتتعبّى تلقائياً من BootNotification
  model
  firmware_version
  serial_number
  iccid, imsi, meter_serial_number
  auth_username, auth_password_hash
  last_boot_at
  last_heartbeat_at
  heartbeat_interval  DEFAULT 300
  created_at

connectors
  id                  PK
  charger_id          FK
  connector_id        INT        -- 0 = الشاحن نفسه، 1..n = المقابس
  status              -- Available|Preparing|Charging|SuspendedEV|SuspendedEVSE|Finishing|Reserved|Unavailable|Faulted
  error_code
  vendor_error_code
  info
  status_updated_at              -- ⚠️ وقت الشاحن (من الرسالة) مش وقت الوصول
  status_received_at             -- وقت الوصول (للتشخيص)
  plug_type, power_kw
  UNIQUE (charger_id, connector_id)

ocpp_transactions
  id                  PK
  transaction_id      INT        -- يلي إحنا ولّدناه بالرد على StartTransaction
  charger_id          FK
  connector_id        INT
  id_tag
  meter_start_wh      BIGINT
  meter_stop_wh       BIGINT NULL
  started_at          TIMESTAMP  -- وقت الشاحن
  stopped_at          TIMESTAMP NULL
  stop_reason
  energy_kwh          DECIMAL    -- (meter_stop - meter_start) / 1000
  is_open             BOOL
  UNIQUE (charger_id, transaction_id)

ocpp_meter_values
  id                  PK
  charger_id          FK
  transaction_id      INT NULL
  measured_at         TIMESTAMP  -- ⚠️ وقت الشاحن
  received_at         TIMESTAMP
  energy_wh           BIGINT     -- تراكمي
  power_w             INT
  current_a           DECIMAL
  voltage_v           DECIMAL
  soc_percent         INT NULL   -- ⚠️ ممكن يكون NULL — مش كل شاحن بيبعته
  temperature_c       DECIMAL NULL
  UNIQUE (charger_id, transaction_id, measured_at)   -- 🔴 يمنع التكرار بعد الانقطاع
  INDEX (transaction_id, measured_at)

ocpp_raw_messages     -- 🔴 مش رفاهية — أهم أداة تشخيص
  id                  PK
  charger_id          FK NULL
  charge_point_id
  direction           -- 'in' | 'out'
  action
  message_id
  payload             JSON
  created_at
  INDEX (charge_point_id, created_at)
  -- احتفظ 30 يوم فقط
```

### ⚠️ حجم البيانات
شاحن واحد × موصل واحد × كل 60 ثانية = **1,440 صف/يوم**.
10 شواحن × 2 موصل = **~29,000 صف/يوم**.
**خطة أرشفة مطلوبة من أول يوم** (تجميع لساعة بعد 30 يوم).

---

## 6. الرسائل — الـ8 المطلوبة

> كل العيّنات تحت **حقيقية** من سجل المختبر.

### 6.1 `BootNotification`
```json
// من الشاحن
{
  "chargePointVendor": "CHARGER",
  "chargePointModel": "DC-120",
  "chargePointSerialNumber": "CH120-JO-000187",
  "chargeBoxSerialNumber": "CB-000187",
  "firmwareVersion": "1.4.2-rc3",
  "iccid": "8996201912345678901",
  "imsi": "416031234567890",
  "meterType": "AC/DC Meter",
  "meterSerialNumber": "MTR-77120934"
}

// ردّنا
{ "status": "Accepted", "currentTime": "2026-09-22T10:43:57.704Z", "interval": 300 }
```
- `currentTime` **بيظبّط ساعة الشاحن** — لازم UTC صحيح
- `interval` = فترة الـ Heartbeat بالثواني
- خزّن `vendor` / `model` / `firmware` / `serial` على `chargers`
- 🔴 **ما بتسكّر أي جلسة مفتوحة** — انظر القاعدة R2

### 6.2 `Heartbeat`
```json
// من الشاحن: {}
// ردّنا:
{ "currentTime": "2026-09-22T10:44:07.225Z" }
```
حدّث `last_heartbeat_at`.

### 6.3 `StatusNotification`
```json
// من الشاحن
{ "connectorId": 1, "status": "Available", "errorCode": "NoError",
  "timestamp": "2026-09-22T11:14:15.196Z" }

// عند عطل
{ "connectorId": 1, "status": "Faulted", "errorCode": "GroundFailure",
  "info": "Residual current detected", "vendorErrorCode": "E-0x21",
  "timestamp": "..." }

// ردّنا: {}
```
- `connectorId: 0` = **حالة الشاحن نفسه** مش مقبس
- الحالات: `Available` `Preparing` `Charging` `SuspendedEV` `SuspendedEVSE` `Finishing` `Reserved` `Unavailable` `Faulted`
- 🔔 **`Faulted` → إشعار فوري لصاحب المحطة**

### 6.4 `Authorize`
```json
// من الشاحن
{ "idTag": "CBL0000123" }

// ردّنا
{ "idTagInfo": { "status": "Accepted" } }
```
🔴 **حرج:** بوضع القراءة فقط نرد `Accepted` دايماً. **رد غلط أو تأخير = السيارة ما بتشحن.**

### 6.5 `StartTransaction`
```json
// من الشاحن
{ "connectorId": 1, "idTag": "CBL0000123",
  "meterStart": 1523400, "timestamp": "2026-09-22T10:43:55.196Z" }

// ردّنا
{ "transactionId": 1002, "idTagInfo": { "status": "Accepted" } }
```
- `transactionId` **إحنا بنولّده** — لازم يكون فريد لكل شاحن
- `meterStart` = قراءة العدّاد **التراكمي** (مش صفر)

### 6.6 `MeterValues`
```json
{
  "connectorId": 1,
  "transactionId": 1002,
  "meterValue": [{
    "timestamp": "2026-09-22T11:12:55.196Z",
    "sampledValue": [
      { "value": "1561321", "context": "Sample.Periodic", "format": "Raw",
        "measurand": "Energy.Active.Import.Register", "location": "Outlet", "unit": "Wh" },
      { "value": "31000", "measurand": "Power.Active.Import", "location": "Outlet", "unit": "W" },
      { "value": "78.5",  "measurand": "Current.Import", "location": "Outlet", "unit": "A" },
      { "value": "392.5", "measurand": "Voltage", "location": "Outlet", "unit": "V" },
      { "value": "81",    "measurand": "SoC", "location": "EV", "unit": "Percent" },
      { "value": "33.6",  "measurand": "Temperature", "location": "Body", "unit": "Celsius" }
    ]
  }]
}
// ردّنا: {}
```
- ⚠️ `measurand` **ممكن يكون غايب** → افتراضه `Energy.Active.Import.Register`
- ⚠️ **`SoC` اختياري** — الشاحن بيبعته فقط إذا السيارة بتدعمه
- ⚠️ `meterValue` **مصفوفة** — ممكن تجي أكثر من عيّنة برسالة وحدة
- الطاقة **تراكمية** — لا تعرضها للمستخدم كما هي

### 6.7 `StopTransaction`
```json
{
  "transactionId": 1002,
  "meterStop": 1561321,
  "timestamp": "2026-09-22T11:13:55.196Z",
  "reason": "Local",
  "transactionData": [{
    "timestamp": "2026-09-22T11:13:55.196Z",
    "sampledValue": [{ "value": "1561321", "context": "Transaction.End",
      "measurand": "Energy.Active.Import.Register", "unit": "Wh" }]
  }]
}
// ردّنا: { "idTagInfo": { "status": "Accepted" } }
```
**طاقة الجلسة = `(meterStop − meterStart) / 1000` kWh**
أسباب الإيقاف: `Local` `Remote` `EVDisconnected` `EmergencyStop` `PowerLoss` `Reboot` `DeAuthorized`

### 6.8 `DataTransfer`
```json
// من الشاحن: { "vendorId": "...", "messageId": "...", "data": "..." }
// ردّنا (آمن): { "status": "Rejected" }
```
رسائل خاصة بكل شركة. **ارفضها بأمان وسجّلها خام** — إذا الشاحن اعتمد عليها منراجع.

### 6.9 اختيارية
`FirmwareStatusNotification` · `DiagnosticsStatusNotification` → رد `{}` وسجّل.

---

## 7. 🔴 القواعد السبع الحرجة

> **كل وحدة منهم اكتشفناها بالمحاكاة الفعلية. لا تتجاهل ولا وحدة.**

| # | القاعدة | ليش |
|:---:|---|---|
| **R1** | **لا تستعمل وقت الوصول أبداً — استعمل `timestamp` يلي جوّا الرسالة** | بعد انقطاع النت، 4 رسائل وصلت **بفارق 0.12 ثانية** بدل 60 ثانية. الرسم البياني بينهار لو اعتمدت على وقت الوصول |
| **R2** | **`BootNotification` ما بتسكّر ولا بتصفّر الجلسات المفتوحة** | بالمحاكاة إجت `BootNotification` **بنص جلسة شغالة** بعد رجوع النت. لو عملت reset، بتفقد الجلسة والطاقة |
| **R3** | **امنع التكرار** — مفتاح فريد `(charger_id, transaction_id, measured_at)` | الرسائل المخزّنة ممكن تنبعت مرتين. بدون هاد بتتضاعف الطاقة |
| **R4** | **اربط الرد بالطلب عبر `messageId` — مش بالترتيب** | الشاحن بيبعت طلب جديد قبل ما يستلم رد القديم. شفناه بالمحاكاة |
| **R5** | **«غير متصل» ≠ «ما في جلسة»** — خزّن آخر حالة معروفة + وقتها | السيرفر كان أعمى 3 دقائق **والسيارة عم تشحن**. التطبيق بدّه 3 حالات |
| **R6** | **اقبل `StopTransaction` المتأخرة** — حتى لو وصلت بعد ساعات | الشاحن بيخزّنها وبيبعتها لما يرجع النت |
| **R7** | **خزّن الوقتين** (وقت الشاحن + وقت الوصول). لو الفرق كبير **نبّه — بس لا ترفض** | ساعات الشواحن بتنحرف. الرفض = فقدان بيانات فوترة |

### حالات إضافية لازم تتعامل معها
- `StopTransaction` لـ `transactionId` ما عندك بدايته → **خزّنها كجلسة يتيمة**، لا ترميها
- جلسة مفتوحة بدون `StopTransaction` لأكثر من 24 ساعة → علّمها `stale` للتسوية اليدوية
- العدّاد رجع لصفر (بعد استبدال/تصفير) → اكتشف القيمة السالبة ولا تحسبها
- `connectorId` غير معروف → أنشئه تلقائياً، لا ترفض الرسالة

---

## 8. إعدادات نطلبها من الشركة المصنّعة

```
# حماية: لو سيرفرنا وقف، السيارات تضل تشحن
AllowOfflineTxForUnknownId      = true
LocalAuthorizeOffline           = true
AuthorizationCacheEnabled       = true
StopTransactionOnInvalidId      = false

# استقرار الاتصال
WebSocketPingInterval           = 60     # 🔴 مهم — بيمنع قطع الجدار الناري
HeartbeatInterval               = 300
ConnectionTimeOut               = 60

# البيانات المطلوبة
MeterValueSampleInterval        = 60
MeterValuesSampledData          = Energy.Active.Import.Register,
                                  Power.Active.Import,
                                  Current.Import,
                                  Voltage,
                                  SoC

# إعادة الإرسال بعد الانقطاع
TransactionMessageAttempts      = 5
TransactionMessageRetryInterval = 60
```

---

## 9. ربط الشاحن بالنظام

```
① النظام يولّد معرّف فريد:      CBL-IRB-001
② يولّد بيانات دخول:            username = CBL-IRB-001  (لازم يساوي المعرّف)
                                password = <قوي>
③ يتسجّلوا بجدول chargers + يُربطوا بـ station_id
④ الفني يكتبهم جوّا الشاحن:
      Central System URL : wss://ocpp.cable-app.com/ocpp16/
      Charge Point ID    : CBL-IRB-001
      Username           : CBL-IRB-001
      Password           : ••••••••
⑤ Reset للشاحن
⑥ الشاحن بيتصل → BootNotification → السيرفر بيتعرّف عليه ويعبّي vendor/model/firmware
```

### قواعد المعرّف
| القاعدة | السبب |
|---|---|
| أحرف وأرقام و `-` فقط | كثير شواحن بترفض المسافات والرموز |
| **≤ 20 حرف** | حد أقصى ببعض الموديلات |
| 🔴 **لا تستعمل الرقم التسلسلي للشاحن** | لو بدّلوا الجهاز بتتكسر الربطة |
| فريد على مستوى النظام | مش لكل محطة |

---

## 10. Endpoints لتطبيق البارتنر

> ⚠️ **مقترحة — تُعتمد بالاتفاق.** كلها **GET فقط**.

```http
GET /api/partner/chargers
→ [{ id, chargePointId, stationId, stationName, vendor, model,
     isOnline, lastHeartbeatAt,
     connectors: [{ connectorId, status, errorCode, statusUpdatedAt, powerKw }] }]

GET /api/partner/chargers/{id}
→ { ...التفاصيل الكاملة + firmwareVersion + serialNumber + lastBootAt }

GET /api/partner/chargers/{id}/live
→ { isOnline, lastSeenAt,
    connectors: [{ connectorId, status,
      session: { transactionId, startedAt, durationSec,
                 energyKwh, powerKw, socPercent (nullable),
                 voltageV, currentA } | null }] }
   # polling كل 10 ثواني من التطبيق

GET /api/partner/chargers/{id}/sessions?from=&to=&page=
→ [{ transactionId, connectorId, startedAt, stoppedAt,
     durationSec, energyKwh, avgPowerKw, stopReason }]

GET /api/partner/sessions/{transactionId}/meter-values
→ [{ measuredAt, powerW, energyWh, socPercent, voltageV, currentA }]
   # لرسم منحنى الجلسة

GET /api/partner/chargers/{id}/stats?period=day|week|month
→ { totalKwh, sessionsCount, avgSessionKwh, uptimePercent,
    faultsCount, peakHours: [...] }
```

### ملاحظات تصميم للتطبيق
1. 🔴 **`socPercent` ممكن يكون `null`** — الشاشة لازم تشتغل بدونه
2. 🔴 **لا تعرض `energy_wh` التراكمي** — اعرض طاقة الجلسة فقط
3. **القدرة قيمة لحظية** بتتغيّر باستمرار (88 → 75 → 53 → 31 kW) — لا تعرضها كرقم ثابت
4. **`isOnline`** = آخر heartbeat أحدث من `heartbeat_interval × 2.5`

---

## 11. الاختبار — المحاكي جاهز

```bash
cd caasd/ocpp-lab
npm install
npm start                                   # سيرفر CSMS مصغّر

# بتيرمنال ثاني:
npm run charger                             # جلسة كاملة
node fake-charger.js --scenario offline     # 🔴 اختبر R1 R2 R3 R5 R6
node fake-charger.js --scenario fault       # اختبر التنبيهات
node fake-charger.js --scenario suspend     # حالة SuspendedEV
node fake-charger.js --id CBL-IRB-002       # شاحنين بنفس الوقت
```

**وجّه المحاكي على سيرفرك:**
```bash
node fake-charger.js --url ws://localhost:PORT/CBL-TEST-001
```

📊 `ocpp_messages.jsonl` فيه **574 رسالة خام حقيقية** كمرجع.

### ✅ معايير القبول للمرحلة B
- [ ] جلسة كاملة تتسجّل صح والطاقة تطابق `meterStop − meterStart`
- [ ] `--scenario offline` → **ولا رسالة مكررة، ولا جلسة ضايعة**، والطاقة نفسها تماماً
- [ ] `--scenario fault` → العطل ينحفظ ويطلع تنبيه
- [ ] شاحنين متزامنين بدون تداخل
- [ ] إعادة تشغيل السيرفر بنص جلسة → الجلسة تكمل صح
- [ ] كل الرسائل الخام مسجّلة

---

## 12. خارج النطاق (حالياً)

❌ تشغيل/إيقاف عن بعد · نظام دفع · فوترة وتعرفة · بطاقات RFID · `Reset`/`ChangeConfiguration`/`UnlockConnector` · Smart Charging · تحديث الفيرموير · تطبيق كيبل B2C

> لو انطلب أي إشي منهم لاحقاً، **يُعاد تقدير النطاق والعقد.**

---

## 13. ملفات مرتبطة

| الملف | المحتوى |
|---|---|
| `OCPP_PROJECT_REPORT.md` | التقرير الكامل: القرارات، المخاطر، الأسئلة المفتوحة |
| `caasd/ocpp-lab/` | المختبر — سيرفر + محاكي |
| `caasd/ocpp-lab/ocpp_messages.jsonl` | 574 رسالة خام |
