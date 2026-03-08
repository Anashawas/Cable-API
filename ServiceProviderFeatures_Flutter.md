# Service Provider Features - Flutter Implementation Guide (Station Partner App)

## Overview
This document covers all backend API endpoints for the **Service Provider** module in the Cable EV Charging Station Management System. These endpoints are used by the **Station Partner App** (Flutter) to manage service providers, categories, ratings, favorites, icon uploads, and attachments.

**Base URL**: `{serverUrl}/api`

**Authentication**: JWT Bearer token in `Authorization` header. Endpoints marked with `[Auth]` require authentication.

---

## 1. Service Categories

**Base Path**: `/api/service-categories`

### 1.1 Get All Service Categories
- **Method**: `GET`
- **Path**: `/api/service-categories/GetAllServiceCategories`
- **Auth**: No
- **Response**: `List<ServiceCategoryDto>`

```json
// ServiceCategoryDto
{
  "id": 1,
  "name": "EV Charging",
  "nameAr": "شحن المركبات الكهربائية",
  "description": "Electric vehicle charging services",
  "iconUrl": "https://example.com/icon.png",
  "sortOrder": 1,
  "isActive": true
}
```

### 1.2 Create Service Category
- **Method**: `POST`
- **Path**: `/api/service-categories/CreateServiceCategory`
- **Auth**: `[Auth]` (Admin)
- **Request Body**:

```json
{
  "name": "EV Charging",
  "nameAr": "شحن المركبات الكهربائية",
  "description": "Electric vehicle charging services",
  "iconUrl": "https://example.com/icon.png",
  "sortOrder": 1,
  "isActive": true
}
```

- **Response**: `int` (created category ID)

### 1.3 Update Service Category
- **Method**: `PUT`
- **Path**: `/api/service-categories/UpdateServiceCategory/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int) - Category ID
- **Request Body**: Same as Create
- **Response**: `200 OK`

---

## 2. Service Providers

**Base Path**: `/api/service-providers`

### 2.1 Get All Service Providers
- **Method**: `GET`
- **Path**: `/api/service-providers/GetAllServiceProviders`
- **Auth**: No
- **Query Params**: `categoryId` (int, optional) - Filter by category
- **Response**: `List<ServiceProviderDto>`

```json
// ServiceProviderDto
{
  "id": 1,
  "name": "EV Fast Charge Station",
  "ownerId": 5,
  "ownerName": "Ahmed",
  "serviceCategoryId": 1,
  "serviceCategoryName": "EV Charging",
  "serviceCategoryNameAr": "شحن المركبات الكهربائية",
  "statusId": 1,
  "statusName": "Active",
  "description": "Fast charging station with multiple ports",
  "phone": "+965XXXXXXXX",
  "address": "Block 5, Street 10",
  "countryName": "Kuwait",
  "cityName": "Kuwait City",
  "latitude": 29.3759,
  "longitude": 47.9774,
  "price": 5.0,
  "priceDescription": "5 KWD per session",
  "fromTime": "08:00",
  "toTime": "22:00",
  "methodPayment": "Cash, Card",
  "visitorsCount": 150,
  "isVerified": true,
  "hasOffer": true,
  "offerDescription": "20% off first charge",
  "service": "EV Charging",
  "icon": "https://server/CableServiceProvider/icon.jpg",
  "whatsAppNumber": "+965XXXXXXXX",
  "websiteUrl": "https://example.com",
  "avgRating": 4.5,
  "rateCount": 23,
  "images": [
    "https://server/CableServiceProvider/img1.jpg",
    "https://server/CableServiceProvider/img2.jpg"
  ],
  "createdAt": "2025-01-15T10:30:00Z"
}
```

### 2.2 Get Service Provider By ID
- **Method**: `GET`
- **Path**: `/api/service-providers/GetServiceProviderById/{id}`
- **Auth**: No
- **Path Params**: `id` (int, required) - Service provider ID
- **Response**: `ServiceProviderDto`
- **Note**: Increments the visitor count each time this endpoint is called

### 2.3 Get Service Providers By Category
- **Method**: `GET`
- **Path**: `/api/service-providers/GetByCategory/{categoryId}`
- **Auth**: No
- **Path Params**: `categoryId` (int, required) - Category ID
- **Response**: `List<ServiceProviderDto>`

### 2.4 Get Nearby Service Providers
- **Method**: `GET`
- **Path**: `/api/service-providers/GetNearby`
- **Auth**: No
- **Query Params**:
  - `latitude` (double, required)
  - `longitude` (double, required)
  - `radiusKm` (double, optional, default: 10)
- **Response**: `List<ServiceProviderDto>`

### 2.5 Create Service Provider
- **Method**: `POST`
- **Path**: `/api/service-providers/CreateServiceProvider`
- **Auth**: `[Auth]`
- **Request Body**:

```json
{
  "name": "My Charging Station",
  "serviceCategoryId": 1,
  "statusId": 1,
  "description": "Fast charging station",
  "phone": "+965XXXXXXXX",
  "ownerPhone": "+965XXXXXXXX",
  "address": "Block 5, Street 10",
  "countryName": "Kuwait",
  "cityName": "Kuwait City",
  "latitude": 29.3759,
  "longitude": 47.9774,
  "price": 5.0,
  "priceDescription": "5 KWD per session",
  "fromTime": "08:00",
  "toTime": "22:00",
  "methodPayment": "Cash, Card",
  "hasOffer": false,
  "offerDescription": null,
  "service": "EV Charging",
  "icon": null,
  "note": "Near the mall entrance",
  "whatsAppNumber": "+965XXXXXXXX",
  "websiteUrl": "https://example.com"
}
```

- **Response**: `int` (created service provider ID)
- **Note**: The authenticated user becomes the owner of this service provider.

### 2.6 Update Service Provider
- **Method**: `PUT`
- **Path**: `/api/service-providers/UpdateServiceProvider/{id}`
- **Auth**: `[Auth]`
- **Path Params**: `id` (int, required) - Service provider ID
- **Request Body**:

```json
{
  "name": "Updated Station Name",
  "serviceCategoryId": 1,
  "statusId": 1,
  "description": "Updated description",
  "phone": "+965XXXXXXXX",
  "ownerPhone": "+965XXXXXXXX",
  "address": "Block 5, Street 10",
  "countryName": "Kuwait",
  "cityName": "Kuwait City",
  "latitude": 29.3759,
  "longitude": 47.9774,
  "price": 5.0,
  "priceDescription": "5 KWD per session",
  "fromTime": "08:00",
  "toTime": "22:00",
  "methodPayment": "Cash, Card",
  "isVerified": false,
  "hasOffer": true,
  "offerDescription": "Special offer",
  "service": "EV Charging",
  "icon": null,
  "note": "Updated note",
  "whatsAppNumber": "+965XXXXXXXX",
  "websiteUrl": "https://example.com"
}
```

- **Response**: `200 OK`

### 2.7 Delete Service Provider
- **Method**: `DELETE`
- **Path**: `/api/service-providers/DeleteServiceProvider/{id}`
- **Auth**: `[Auth]`
- **Path Params**: `id` (int, required)
- **Response**: `200 OK`
- **Note**: This is a soft delete (sets IsDeleted flag).

### 2.8 Verify Service Provider (Admin)
- **Method**: `PUT`
- **Path**: `/api/service-providers/VerifyServiceProvider/{id}`
- **Auth**: `[Auth]` (Admin)
- **Path Params**: `id` (int, required)
- **Response**: `200 OK`
- **Note**: Marks a service provider as verified by an admin.

---

## 3. Service Provider Ratings

### 3.1 Get Ratings
- **Method**: `GET`
- **Path**: `/api/service-providers/GetRatings/{serviceProviderId}`
- **Auth**: No
- **Path Params**: `serviceProviderId` (int, required)
- **Response**: `List<ServiceProviderRatingDto>`

```json
// ServiceProviderRatingDto
{
  "id": 1,
  "userId": 5,
  "userName": "Ahmed",
  "rating": 5,
  "avgRating": 4.5,
  "comment": "Great service!",
  "createdAt": "2025-01-15T10:30:00Z"
}
```

### 3.2 Rate Service Provider
- **Method**: `POST`
- **Path**: `/api/service-providers/RateServiceProvider/{serviceProviderId}`
- **Auth**: `[Auth]`
- **Path Params**: `serviceProviderId` (int, required)
- **Request Body**:

```json
{
  "rating": 5,
  "comment": "Excellent charging station!"
}
```

- **Response**: `int` (rating ID)
- **Note**: This also awards loyalty points to the user (action code: `RATE_SERVICE`).

---

## 4. Service Provider Favorites

### 4.1 Get My Favorites
- **Method**: `GET`
- **Path**: `/api/service-providers/GetMyFavorites`
- **Auth**: `[Auth]`
- **Response**: `List<ServiceProviderDto>`
- **Note**: Returns the current authenticated user's favorite service providers.

### 4.2 Add to Favorites
- **Method**: `POST`
- **Path**: `/api/service-providers/AddToFavorites/{serviceProviderId}`
- **Auth**: `[Auth]`
- **Path Params**: `serviceProviderId` (int, required)
- **Response**: `int` (favorite record ID)
- **Note**: Awards loyalty points (action code: `ADD_FAVORITE_SERVICE`).

### 4.3 Remove from Favorites
- **Method**: `DELETE`
- **Path**: `/api/service-providers/RemoveFromFavorites/{serviceProviderId}`
- **Auth**: `[Auth]`
- **Path Params**: `serviceProviderId` (int, required)
- **Response**: `200 OK`

---

## 5. Service Provider Icon Upload

### 5.1 Upload Icon
- **Method**: `POST`
- **Path**: `/api/service-providers/UploadServiceProviderIcon/{id}`
- **Auth**: `[Auth]`
- **Path Params**: `id` (int, required) - Service provider ID
- **Content-Type**: `multipart/form-data`
- **Form Data**: `file` (IFormFile) - The icon image file
- **Response**: `200 OK`
- **Note**: Replaces the existing icon if one exists. The old icon file is deleted from storage.

**Flutter Implementation**:
```dart
// Use multipart request
final request = http.MultipartRequest(
  'POST',
  Uri.parse('$baseUrl/api/service-providers/UploadServiceProviderIcon/$id'),
);
request.headers['Authorization'] = 'Bearer $token';
request.files.add(await http.MultipartFile.fromPath('file', filePath));
final response = await request.send();
```

---

## 6. Service Provider Attachments

Attachments are files (images, documents) associated with a service provider. Files are stored in the `CableServiceProvider` folder on the server.

### 6.1 Get Attachments
- **Method**: `GET`
- **Path**: `/api/service-providers/GetAttachments/{id}`
- **Auth**: No
- **Path Params**: `id` (int, required) - Service provider ID
- **Response**: `List<UploadFile>`

```json
// UploadFile
{
  "fileName": "abc123.jpg",
  "contentType": "image/jpeg",
  "filePath": "https://server/CableServiceProvider/abc123.jpg",
  "fileExtension": ".jpg",
  "fileSize": 245000
}
```

### 6.2 Add Attachments
- **Method**: `POST`
- **Path**: `/api/service-providers/AddAttachments/{id}`
- **Auth**: `[Auth]`
- **Path Params**: `id` (int, required) - Service provider ID
- **Content-Type**: `multipart/form-data`
- **Form Data**: Multiple files (IFormFileCollection)
- **Response**: `int[]` (array of created attachment IDs)

**Flutter Implementation**:
```dart
// Upload multiple files
final request = http.MultipartRequest(
  'POST',
  Uri.parse('$baseUrl/api/service-providers/AddAttachments/$id'),
);
request.headers['Authorization'] = 'Bearer $token';
for (final file in files) {
  request.files.add(await http.MultipartFile.fromPath('files', file.path));
}
final response = await request.send();
```

### 6.3 Delete All Attachments
- **Method**: `DELETE`
- **Path**: `/api/service-providers/DeleteAttachments/{id}`
- **Auth**: `[Auth]`
- **Path Params**: `id` (int, required) - Service provider ID
- **Response**: `200 OK`
- **Note**: Deletes ALL attachments for the service provider (both files from disk and database records).

---

## Error Responses

All endpoints may return:

| Status Code | Description |
|---|---|
| `200` | Success |
| `400` | Validation error (bad request body) |
| `401` | Unauthorized (missing or invalid token) |
| `403` | Forbidden (insufficient permissions) |
| `404` | Resource not found |
| `500` | Internal server error |

**Validation Error Format**:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Name": ["The Name field is required."]
  }
}
```

---

## Enums Reference

### StatusId (Service Provider Status)
Check database `Statuses` table for values. Common values:
- `1` = Active
- `2` = Inactive

### ProviderType (used in Offers & Loyalty)
- `"ChargingPoint"` - For charging point providers
- `"ServiceProvider"` - For service providers

---

## Business Logic Notes

1. **Creating a Service Provider**: The authenticated user becomes the owner. The `Icon` field in the create request is a string URL (legacy). Use the dedicated `UploadServiceProviderIcon` endpoint for proper icon upload.

2. **Visitor Count**: The `GetServiceProviderById` endpoint increments the visitor count automatically.

3. **Soft Delete**: Delete operations set `IsDeleted = true` rather than removing records.

4. **Verification**: Only admins can verify service providers via the `VerifyServiceProvider` endpoint.

5. **Ratings**: Rating a service provider awards loyalty points. The `avgRating` field is recalculated.

6. **Favorites**: Adding to favorites awards loyalty points. Each user can favorite a provider only once.

7. **File Upload**: Icon and attachment uploads use `multipart/form-data`. The server generates unique file names and returns the full URL path in responses.

8. **Images in ServiceProviderDto**: The `images` array contains full URLs to all attachment files for that provider.

---

## Flutter App Screens Suggestion

Based on the endpoints, the Station Partner App should implement:

1. **Service Provider List** - Browse/search providers with category filter
2. **Service Provider Detail** - View full details, ratings, attachments, map location
3. **Create/Edit Service Provider** - Form with all fields + icon upload + attachment management
4. **My Favorites** - List of favorite providers
5. **Nearby Providers** - Map view with location-based search
6. **Rating Screen** - View and submit ratings
7. **Category Browser** - Browse providers by category
8. **Admin: Category Management** - CRUD for service categories
9. **Admin: Provider Verification** - Verify/unverify providers
