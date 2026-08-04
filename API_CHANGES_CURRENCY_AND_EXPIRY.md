# API Changes — Currency & Expiry Updates

## 1. Currency Changed from KWD to JOD

Default currency changed from Kuwaiti Dinar (KWD) to Jordanian Dinar (JOD).

**Affected tables:** `ProviderOffer`, `OfferTransaction`

**DB update:** Existing records updated from KWD to JOD. Default value for new records is now JOD.

---

## 2. Expiry Fields Changed from Minutes to Seconds

### Offers
- **Field renamed:** `OfferCodeExpiryMinutes` → `OfferCodeExpirySeconds`
- **Default value:** `60` (seconds)
- **Affected endpoints:**
  - `POST /api/offers/ProposeOffer` — request field renamed
  - `PUT /api/offers/UpdateOffer/{id}` — request field renamed
  - All offer query responses now return `OfferCodeExpirySeconds`

### Partners
- **Field renamed:** `CodeExpiryMinutes` → `CodeExpirySeconds`
- **Default value:** `60` (seconds)
- **Affected endpoints:**
  - `POST /api/partners/admin/CreatePartnerAgreement` — request field renamed
  - `PUT /api/partners/admin/UpdatePartnerAgreement/{id}` — request field renamed
  - All partner query responses now return `CodeExpirySeconds`

### DB columns renamed:
```sql
ProviderOffer.OfferCodeExpiryMinutes → OfferCodeExpirySeconds
PartnerAgreement.CodeExpiryMinutes → CodeExpirySeconds
```

---

## 3. Offer Transaction ID Added to Response

`POST /api/offers/provider/CreateTransaction` now returns `Id` in the response.

**Response:**
```json
{
  "id": 1,
  "offerCode": "CBL-7X9K2M",
  "expiresAt": "2026-03-11T12:00:00Z",
  "pointsCost": 100,
  "monetaryValue": 5.000,
  "currencyCode": "JOD"
}
```

This allows the provider app to poll `GetOfferById` or check transaction status by ID.

---

## Flutter Impact

- Replace all `OfferCodeExpiryMinutes` fields with `OfferCodeExpirySeconds` in request/response models
- Replace all `CodeExpiryMinutes` fields with `CodeExpirySeconds` in partner request/response models
- Update currency display from KWD to JOD
- Use `id` from offer transaction creation response for polling
- Timer/countdown logic should now use seconds instead of minutes
