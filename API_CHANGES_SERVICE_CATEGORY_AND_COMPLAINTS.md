# API Changes - ServiceCategory Delete/Upload & Complaint Status

**Date:** 2026-03-08

---

## 1. ServiceCategory — New Endpoints

### 1.1 DELETE `/api/service-categories/DeleteServiceCategory/{id}`

**Purpose:** Soft-delete a service category.

**Authorization:** Required

**Route Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | int | Yes | The ID of the service category to delete |

**Response:** `200 OK` (no body)

**Error responses:**
- `401` — Unauthorized
- `403` — Forbidden
- `404` — Service category not found
- `500` — Internal server error

---

### 1.2 POST `/api/service-categories/UploadServiceCategoryIcon/{id}`

**Purpose:** Upload or replace the icon image for a service category.

**Authorization:** Required

**Route Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | int | Yes | The ID of the service category |

**Request:** `multipart/form-data`

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| file | IFormFile | Yes | The icon image file |

**Validation:**
- File must not be empty
- File size must be within allowed limits
- File extension must be a valid image type

**Response:** `200 OK` (no body)

**Behavior:**
- If the category already has an icon, the old file is deleted before saving the new one
- Icon is stored in the `CableServiceProvider` upload folder

**Error responses:**
- `401` — Unauthorized
- `403` — Forbidden
- `404` — Service category not found
- `400` — File size not valid / File extension not valid
- `500` — Internal server error

---

## 2. UserComplaints — Update Status

### 2.1 PATCH `/api/usercomplaints/UpdateUserComplaintStatus/{id}`

**Purpose:** Update the status of a user complaint (admin action).

**Authorization:** Required

**Route Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| id | int | Yes | The ID of the user complaint |

**Request body:**

```json
{
  "status": 2
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| status | int (enum) | Yes | The new complaint status |

**ComplaintStatus Enum:**

| Value | Name | Description |
|-------|------|-------------|
| 0 | Pending | Complaint submitted, awaiting review |
| 1 | Rejected | Complaint reviewed and rejected |
| 2 | Solved | Complaint resolved |

**Response:** `200 OK` (no body)

**Error responses:**
- `401` — Unauthorized
- `403` — Forbidden
- `404` — Complaint not found
- `400` — Invalid status value
- `500` — Internal server error

---

### 2.2 Modified Response — GetUserComplaintsDto

All complaint query endpoints now include the `status` field:

- `GET /api/usercomplaints/GetAllUserComplaints`
- `GET /api/usercomplaints/GetComplaintsByChargingPointId/{chargingPointId}`
- `GET /api/usercomplaints/GetMyComplaints`

**Updated response:**

```json
{
  "id": 1,
  "note": "Charger was not working properly",
  "status": 0,
  "userAccount": {
    "id": 5,
    "name": "Ahmed"
  },
  "chargingPoint": {
    "id": 12,
    "name": "Mall Charging Station"
  }
}
```

| Field | Type | Description | **New?** |
|-------|------|-------------|----------|
| id | int | Complaint ID | Existing |
| note | string | Complaint text | Existing |
| **status** | **int** | **Complaint status (0=Pending, 1=Rejected, 2=Solved)** | **NEW** |
| userAccount | object | User who submitted the complaint | Existing |
| chargingPoint | object | Charging point the complaint is about | Existing |

---

## Summary

| Change | Type | Endpoint |
|--------|------|----------|
| Delete service category | New endpoint | `DELETE /api/service-categories/DeleteServiceCategory/{id}` |
| Upload service category icon | New endpoint | `POST /api/service-categories/UploadServiceCategoryIcon/{id}` |
| Update complaint status | New endpoint | `PATCH /api/usercomplaints/UpdateUserComplaintStatus/{id}` |
| Status field in complaints | Modified response | All complaint GET endpoints |
