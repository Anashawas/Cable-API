# Charging Point Update Approval System

## 📋 Table of Contents

- [Overview](#overview)
- [Feature Requirements](#feature-requirements)
- [Architecture Design](#architecture-design)
- [Database Schema](#database-schema)
- [Domain Entities](#domain-entities)
- [Application Layer](#application-layer)
- [API Endpoints](#api-endpoints)
- [Approval Flow](#approval-flow)
- [Rejection Flow](#rejection-flow)
- [Implementation Checklist](#implementation-checklist)
- [Testing Scenarios](#testing-scenarios)
- [API Usage Examples](#api-usage-examples)

---

## 🎯 Overview

**Goal**: Allow charging point owners to submit update requests that require admin approval before being applied to the actual charging point.

**Key Requirements**:
- Owners submit changes without immediate effect on production data
- Admins review and approve/reject changes
- Track changes for: basic info, plug types, icon, and attachments
- Maintain complete audit trail of all requests
- Support file uploads (icon and attachments) during request submission
- Clean up unused files on rejection

**Approach**: Staging Table Pattern
- Separate "pending changes" table stores requested modifications
- Upon approval, changes are applied to actual entities
- Upon rejection, uploaded files are cleaned up, records kept for audit

---

## ✨ Feature Requirements

### For Station Owners
1. ✅ Submit update request for their charging point
2. ✅ Upload new icon during request
3. ✅ Upload new attachments during request
4. ✅ Mark existing attachments for deletion
5. ✅ Update basic charging point information
6. ✅ Update plug types
7. ✅ View status of their submitted requests
8. ✅ See rejection reasons if request rejected
9. ✅ Cancel pending requests
10. ✅ Only one pending request per charging point at a time

### For Admins
1. ✅ View all pending update requests
2. ✅ View details with side-by-side comparison (old vs new)
3. ✅ Approve requests (apply changes to charging point)
4. ✅ Reject requests with reason
5. ✅ View history of all requests (approved/rejected)
6. ✅ Filter requests by status

---

## 📐 Architecture Design

### Staging Table Pattern

**Advantages**:
- ✅ No impact on production data until approved
- ✅ Full audit trail of all change requests
- ✅ Can store multiple historical requests
- ✅ Easy to show "diff" of changes
- ✅ Rollback-friendly
- ✅ Files uploaded during request, cleaned up if rejected

### Data Flow

```
Owner Submits Request
        ↓
Files Uploaded to Disk
        ↓
Request Saved to ChargingPointUpdateRequest
        ↓
Attachment Changes Saved to ChargingPointUpdateRequestAttachment
        ↓
Admin Reviews
        ↓
    ┌───────┴───────┐
    ↓               ↓
APPROVE         REJECT
    ↓               ↓
Apply Changes   Delete Files
to ChargingPoint    ↓
    ↓           Keep Records
Mark Approved   Mark Rejected
    ↓               ↓
Keep Records    Store Reason
```

---

## 🗄️ Database Schema

### Table: `ChargingPointUpdateRequest`

```sql
CREATE TABLE ChargingPointUpdateRequest (
    -- Primary Key
    Id INT PRIMARY KEY IDENTITY(1,1),

    -- References
    ChargingPointId INT NOT NULL,
    RequestedByUserId INT NOT NULL,

    -- Status
    RequestStatus INT NOT NULL, -- 0: Pending, 1: Approved, 2: Rejected

    -- Charging Point Fields (nullable - only populated if changed)
    Name NVARCHAR(255) NULL,
    Note NVARCHAR(MAX) NULL,
    CountryName NVARCHAR(100) NULL,
    CityName NVARCHAR(100) NULL,
    Phone NVARCHAR(50) NULL,
    MethodPayment NVARCHAR(100) NULL,
    Price FLOAT NULL,
    FromTime NVARCHAR(10) NULL,
    ToTime NVARCHAR(10) NULL,
    ChargerSpeed INT NULL,
    ChargersCount INT NULL,
    Latitude FLOAT NULL,
    Longitude FLOAT NULL,
    ChargerPointTypeId INT NULL,
    StationTypeId INT NULL,
    OwnerPhone NVARCHAR(50) NULL,
    HasOffer BIT NULL,
    Service NVARCHAR(255) NULL,
    OfferDescription NVARCHAR(1000) NULL,
    Address NVARCHAR(500) NULL,

    -- Icon change tracking
    NewIcon NVARCHAR(255) NULL,
    OldIcon NVARCHAR(255) NULL,

    -- Plug types change tracking (JSON)
    NewPlugTypeIds NVARCHAR(MAX) NULL, -- JSON array: "[1,2,3]"
    OldPlugTypeIds NVARCHAR(MAX) NULL, -- JSON array: "[2,3]"

    -- Review information
    ReviewedByUserId INT NULL,
    ReviewedAt DATETIME NULL,
    RejectionReason NVARCHAR(500) NULL,

    -- Audit fields
    CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy INT NULL,
    ModifiedAt DATETIME NULL,
    ModifiedBy INT NULL,
    IsDeleted BIT NOT NULL DEFAULT 0,

    -- Foreign Keys
    CONSTRAINT FK_ChargingPointUpdateRequest_ChargingPoint
        FOREIGN KEY (ChargingPointId) REFERENCES ChargingPoint(Id),
    CONSTRAINT FK_ChargingPointUpdateRequest_RequestedBy
        FOREIGN KEY (RequestedByUserId) REFERENCES UserAccount(Id),
    CONSTRAINT FK_ChargingPointUpdateRequest_ReviewedBy
        FOREIGN KEY (ReviewedByUserId) REFERENCES UserAccount(Id)
);

-- Indexes
CREATE INDEX IX_ChargingPointUpdateRequest_ChargingPointId
    ON ChargingPointUpdateRequest(ChargingPointId) WHERE IsDeleted = 0;

CREATE INDEX IX_ChargingPointUpdateRequest_RequestStatus
    ON ChargingPointUpdateRequest(RequestStatus) WHERE IsDeleted = 0;

CREATE INDEX IX_ChargingPointUpdateRequest_RequestedBy
    ON ChargingPointUpdateRequest(RequestedByUserId) WHERE IsDeleted = 0;
```

### Table: `ChargingPointUpdateRequestAttachment`

```sql
CREATE TABLE ChargingPointUpdateRequestAttachment (
    -- Primary Key
    Id INT PRIMARY KEY IDENTITY(1,1),

    -- References
    UpdateRequestId INT NOT NULL,

    -- Action Type
    AttachmentAction INT NOT NULL, -- 0: Add, 1: Delete

    -- For new attachments (Action = Add)
    FileName NVARCHAR(255) NULL,
    FileExtension NVARCHAR(50) NULL,
    FileSize BIGINT NULL,
    ContentType NVARCHAR(50) NULL,

    -- For deletion requests (Action = Delete)
    ExistingAttachmentId INT NULL,

    -- Audit fields
    CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy INT NULL,
    IsDeleted BIT NOT NULL DEFAULT 0,

    -- Foreign Keys
    CONSTRAINT FK_ChargingPointUpdateRequestAttachment_UpdateRequest
        FOREIGN KEY (UpdateRequestId) REFERENCES ChargingPointUpdateRequest(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ChargingPointUpdateRequestAttachment_ExistingAttachment
        FOREIGN KEY (ExistingAttachmentId) REFERENCES ChargingPointAttachment(Id)
);

-- Indexes
CREATE INDEX IX_ChargingPointUpdateRequestAttachment_UpdateRequestId
    ON ChargingPointUpdateRequestAttachment(UpdateRequestId) WHERE IsDeleted = 0;
```

---

## 🏗️ Domain Entities

### Enum: `RequestStatus`

**Location**: `/Cable.Core/Enums/RequestStatus.cs`

```csharp
namespace Cable.Core.Enums;

public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
```

### Enum: `AttachmentAction`

**Location**: `/Cable.Core/Enums/AttachmentAction.cs`

```csharp
namespace Cable.Core.Enums;

public enum AttachmentAction
{
    Add = 0,
    Delete = 1
}
```

### Entity: `ChargingPointUpdateRequest`

**Location**: `/Domain/Entities/ChargingPointUpdateRequest.cs`

```csharp
using Cable.Core.Enums;
using Domain.Common;

namespace Domain.Entities;

public class ChargingPointUpdateRequest : BaseAuditableEntity
{
    public int ChargingPointId { get; set; }
    public int RequestedByUserId { get; set; }
    public RequestStatus RequestStatus { get; set; }

    // Charging Point Fields (nullable - only populated if changed)
    public string? Name { get; set; }
    public string? Note { get; set; }
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
    public string? Phone { get; set; }
    public string? MethodPayment { get; set; }
    public double? Price { get; set; }
    public string? FromTime { get; set; }
    public string? ToTime { get; set; }
    public int? ChargerSpeed { get; set; }
    public int? ChargersCount { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? ChargerPointTypeId { get; set; }
    public int? StationTypeId { get; set; }
    public string? OwnerPhone { get; set; }
    public bool? HasOffer { get; set; }
    public string? Service { get; set; }
    public string? OfferDescription { get; set; }
    public string? Address { get; set; }

    // Icon change tracking
    public string? NewIcon { get; set; }
    public string? OldIcon { get; set; }

    // Plug types (stored as JSON)
    public string? NewPlugTypeIds { get; set; } // JSON: "[1,2,3]"
    public string? OldPlugTypeIds { get; set; } // JSON: "[2,3]"

    // Review information
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }

    // Navigation properties
    public virtual ChargingPoint ChargingPoint { get; set; } = null!;
    public virtual UserAccount RequestedBy { get; set; } = null!;
    public virtual UserAccount? ReviewedBy { get; set; }
    public virtual ICollection<ChargingPointUpdateRequestAttachment> AttachmentChanges { get; set; }
        = new List<ChargingPointUpdateRequestAttachment>();
}
```

### Entity: `ChargingPointUpdateRequestAttachment`

**Location**: `/Domain/Entities/ChargingPointUpdateRequestAttachment.cs`

```csharp
using Cable.Core.Enums;
using Domain.Common;

namespace Domain.Entities;

public class ChargingPointUpdateRequestAttachment : BaseAuditableEntity
{
    public int UpdateRequestId { get; set; }
    public AttachmentAction AttachmentAction { get; set; }

    // For new attachments (Action = Add)
    public string? FileName { get; set; }
    public string? FileExtension { get; set; }
    public long? FileSize { get; set; }
    public string? ContentType { get; set; }

    // For deletion requests (Action = Delete)
    public int? ExistingAttachmentId { get; set; }

    // Navigation properties
    public virtual ChargingPointUpdateRequest UpdateRequest { get; set; } = null!;
    public virtual ChargingPointAttachment? ExistingAttachment { get; set; }
}
```

### EF Core Configuration: `ChargingPointUpdateRequestConfiguration`

**Location**: `/Infrastructure/Persistence/Configurations/ChargingPointUpdateRequestConfiguration.cs`

```csharp
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ChargingPointUpdateRequestConfiguration : IEntityTypeConfiguration<ChargingPointUpdateRequest>
{
    public void Configure(EntityTypeBuilder<ChargingPointUpdateRequest> builder)
    {
        builder.ToTable("ChargingPointUpdateRequest");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RequestStatus).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.CountryName).HasMaxLength(100);
        builder.Property(x => x.CityName).HasMaxLength(100);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.MethodPayment).HasMaxLength(100);
        builder.Property(x => x.FromTime).HasMaxLength(10);
        builder.Property(x => x.ToTime).HasMaxLength(10);
        builder.Property(x => x.OwnerPhone).HasMaxLength(50);
        builder.Property(x => x.Service).HasMaxLength(255);
        builder.Property(x => x.OfferDescription).HasMaxLength(1000);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.NewIcon).HasMaxLength(255);
        builder.Property(x => x.OldIcon).HasMaxLength(255);
        builder.Property(x => x.RejectionReason).HasMaxLength(500);

        builder.HasOne(x => x.ChargingPoint)
            .WithMany()
            .HasForeignKey(x => x.ChargingPointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RequestedBy)
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewedBy)
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ChargingPointId)
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(x => x.RequestStatus)
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(x => x.RequestedByUserId)
            .HasFilter("[IsDeleted] = 0");
    }
}
```

### EF Core Configuration: `ChargingPointUpdateRequestAttachmentConfiguration`

**Location**: `/Infrastructure/Persistence/Configurations/ChargingPointUpdateRequestAttachmentConfiguration.cs`

```csharp
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ChargingPointUpdateRequestAttachmentConfiguration
    : IEntityTypeConfiguration<ChargingPointUpdateRequestAttachment>
{
    public void Configure(EntityTypeBuilder<ChargingPointUpdateRequestAttachment> builder)
    {
        builder.ToTable("ChargingPointUpdateRequestAttachment");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AttachmentAction).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.FileExtension).HasMaxLength(50);
        builder.Property(x => x.ContentType).HasMaxLength(50);

        builder.HasOne(x => x.UpdateRequest)
            .WithMany(x => x.AttachmentChanges)
            .HasForeignKey(x => x.UpdateRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ExistingAttachment)
            .WithMany()
            .HasForeignKey(x => x.ExistingAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UpdateRequestId)
            .HasFilter("[IsDeleted] = 0");
    }
}
```

### Update `IApplicationDbContext`

**Location**: `/Application/Common/Interfaces/IApplicationDbContext.cs`

Add these DbSet properties:

```csharp
DbSet<ChargingPointUpdateRequest> ChargingPointUpdateRequests { get; set; }
DbSet<ChargingPointUpdateRequestAttachment> ChargingPointUpdateRequestAttachments { get; set; }
```

### Update `ApplicationDbContext`

**Location**: `/Infrastructure/Persistence/ApplicationDbContext.cs`

Add these DbSet properties and configuration:

```csharp
public DbSet<ChargingPointUpdateRequest> ChargingPointUpdateRequests { get; set; }
public DbSet<ChargingPointUpdateRequestAttachment> ChargingPointUpdateRequestAttachments { get; set; }

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // ... existing configurations
    modelBuilder.ApplyConfiguration(new ChargingPointUpdateRequestConfiguration());
    modelBuilder.ApplyConfiguration(new ChargingPointUpdateRequestAttachmentConfiguration());
}
```

---

## 🔧 Application Layer

### Phase 1: Owner Submits Update Request

#### Command: `SubmitChargingPointUpdateRequestCommand`

**Location**: `/Application/ChargingPoints/Commands/SubmitChargingPointUpdateRequest/SubmitChargingPointUpdateRequestCommand.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Application.ChargingPoints.Commands.SubmitChargingPointUpdateRequest;

public record SubmitChargingPointUpdateRequestCommand(
    int ChargingPointId,
    string? Name,
    string? Note,
    string? CountryName,
    string? CityName,
    string? Phone,
    string? MethodPayment,
    double? Price,
    string? FromTime,
    string? ToTime,
    int? ChargerSpeed,
    int? ChargersCount,
    double? Latitude,
    double? Longitude,
    int? ChargerPointTypeId,
    int? StationTypeId,
    string? OwnerPhone,
    bool? HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,
    List<int>? PlugTypeIds,
    IFormFile? NewIconFile,
    List<int>? AttachmentsToDelete,
    IFormFileCollection? NewAttachments
) : IRequest<int>;

public class SubmitChargingPointUpdateRequestCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<SubmitChargingPointUpdateRequestCommand, int>
{
    public async Task<int> Handle(SubmitChargingPointUpdateRequestCommand request, CancellationToken ct)
    {
        // 1. Verify charging point exists and user is owner
        var chargingPoint = await context.ChargingPoints
            .Include(x => x.ChargingPlugs)
            .FirstOrDefaultAsync(x => x.Id == request.ChargingPointId && !x.IsDeleted, ct)
            ?? throw new NotFoundException($"Charging point not found: {request.ChargingPointId}");

        if (chargingPoint.OwnerId != currentUserService.UserId)
            throw new ForbiddenAccessException("You are not the owner of this charging point");

        // 2. Check for existing pending request
        var existingPendingRequest = await context.ChargingPointUpdateRequests
            .AnyAsync(x => x.ChargingPointId == request.ChargingPointId &&
                          x.RequestStatus == RequestStatus.Pending &&
                          !x.IsDeleted, ct);

        if (existingPendingRequest)
            throw new DataValidationException("ChargingPointId",
                "There is already a pending update request for this charging point");

        // 3. Create update request entity
        var updateRequest = new ChargingPointUpdateRequest
        {
            ChargingPointId = request.ChargingPointId,
            RequestedByUserId = currentUserService.UserId!.Value,
            RequestStatus = RequestStatus.Pending,

            // Store only changed fields
            Name = request.Name != chargingPoint.Name ? request.Name : null,
            Note = request.Note != chargingPoint.Note ? request.Note : null,
            CountryName = request.CountryName != chargingPoint.CountryName ? request.CountryName : null,
            CityName = request.CityName != chargingPoint.CityName ? request.CityName : null,
            Phone = request.Phone != null ? PhoneNumberUtility.NormalizePhoneNumber(request.Phone) : null,
            MethodPayment = request.MethodPayment != chargingPoint.MethodPayment ? request.MethodPayment : null,
            Price = request.Price != chargingPoint.Price ? request.Price : null,
            FromTime = request.FromTime != chargingPoint.FromTime ? request.FromTime : null,
            ToTime = request.ToTime != chargingPoint.ToTime ? request.ToTime : null,
            ChargerSpeed = request.ChargerSpeed != chargingPoint.ChargerSpeed ? request.ChargerSpeed : null,
            ChargersCount = request.ChargersCount != chargingPoint.ChargersCount ? request.ChargersCount : null,
            Latitude = request.Latitude != chargingPoint.Latitude ? request.Latitude : null,
            Longitude = request.Longitude != chargingPoint.Longitude ? request.Longitude : null,
            ChargerPointTypeId = request.ChargerPointTypeId != chargingPoint.ChargerPointTypeId ? request.ChargerPointTypeId : null,
            StationTypeId = request.StationTypeId != chargingPoint.StationTypeId ? request.StationTypeId : null,
            OwnerPhone = request.OwnerPhone != null ? PhoneNumberUtility.NormalizePhoneNumber(request.OwnerPhone) : null,
            HasOffer = request.HasOffer != chargingPoint.HasOffer ? request.HasOffer : null,
            Service = request.Service != chargingPoint.Service ? request.Service : null,
            OfferDescription = request.OfferDescription != chargingPoint.OfferDescription ? request.OfferDescription : null,
            Address = request.Address != chargingPoint.Address ? request.Address : null,
        };

        // 4. Handle icon upload if provided
        if (request.NewIconFile?.Length > 0)
        {
            var iconFileName = await uploadFileService.SaveFileAsync(
                request.NewIconFile,
                UploadFileFolders.CableChargingPoint,
                ct);

            updateRequest.NewIcon = iconFileName;
            updateRequest.OldIcon = chargingPoint.Icon;
        }

        // 5. Handle plug type changes
        if (request.PlugTypeIds != null)
        {
            var currentPlugTypeIds = chargingPoint.ChargingPlugs
                .Where(x => !x.IsDeleted)
                .Select(x => x.PlugTypeId)
                .OrderBy(x => x)
                .ToList();

            var newPlugTypeIds = request.PlugTypeIds.OrderBy(x => x).ToList();

            if (!currentPlugTypeIds.SequenceEqual(newPlugTypeIds))
            {
                updateRequest.OldPlugTypeIds = JsonSerializer.Serialize(currentPlugTypeIds);
                updateRequest.NewPlugTypeIds = JsonSerializer.Serialize(newPlugTypeIds);
            }
        }

        // 6. Save update request
        context.ChargingPointUpdateRequests.Add(updateRequest);
        await context.SaveChanges(ct);

        // 7. Handle attachment deletions
        if (request.AttachmentsToDelete?.Any() == true)
        {
            foreach (var attachmentId in request.AttachmentsToDelete)
            {
                context.ChargingPointUpdateRequestAttachments.Add(new ChargingPointUpdateRequestAttachment
                {
                    UpdateRequestId = updateRequest.Id,
                    AttachmentAction = AttachmentAction.Delete,
                    ExistingAttachmentId = attachmentId
                });
            }
        }

        // 8. Handle new attachment uploads
        if (request.NewAttachments?.Any() == true)
        {
            foreach (var file in request.NewAttachments)
            {
                var fileName = await uploadFileService.SaveFileAsync(
                    file,
                    UploadFileFolders.CableAttachments,
                    ct);

                context.ChargingPointUpdateRequestAttachments.Add(new ChargingPointUpdateRequestAttachment
                {
                    UpdateRequestId = updateRequest.Id,
                    AttachmentAction = AttachmentAction.Add,
                    FileName = fileName,
                    FileExtension = file.GetFileExtension(),
                    FileSize = file.Length,
                    ContentType = file.ContentType
                });
            }
        }

        await context.SaveChanges(ct);

        // 9. TODO: Send notification to admins about new update request

        return updateRequest.Id;
    }
}
```

#### Validator: `SubmitChargingPointUpdateRequestCommandValidator`

**Location**: `/Application/ChargingPoints/Commands/SubmitChargingPointUpdateRequest/SubmitChargingPointUpdateRequestCommandValidator.cs`

```csharp
using Application.Common.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.SubmitChargingPointUpdateRequest;

public class SubmitChargingPointUpdateRequestCommandValidator
    : AbstractValidator<SubmitChargingPointUpdateRequestCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IUploadFileService _uploadFileService;

    public SubmitChargingPointUpdateRequestCommandValidator(
        IApplicationDbContext context,
        IUploadFileService uploadFileService)
    {
        _context = context;
        _uploadFileService = uploadFileService;

        RuleFor(x => x.ChargingPointId)
            .GreaterThan(0)
            .MustAsync(ChargingPointExists)
            .WithMessage("Charging point does not exist");

        RuleFor(x => x.Name)
            .MaximumLength(255)
            .When(x => x.Name != null);

        RuleFor(x => x.OfferDescription)
            .MaximumLength(1000)
            .When(x => x.OfferDescription != null);

        // Icon validation
        RuleFor(x => x.NewIconFile)
            .Must(file => file == null || IsValidFileSize(file))
            .WithMessage("Icon file size exceeds limit")
            .Must(file => file == null || IsValidFileExtension(file))
            .WithMessage("Icon file type not allowed");

        // Attachments validation
        RuleFor(x => x.NewAttachments)
            .Must(files => files == null || _uploadFileService.IsValidSize(files))
            .WithMessage("One or more attachment files exceed size limit")
            .Must(files => files == null || _uploadFileService.IsValidExtension(files))
            .WithMessage("One or more attachment files have invalid type");

        // Plug types validation
        RuleFor(x => x.PlugTypeIds)
            .MustAsync(async (ids, ct) => await ValidatePlugTypes(ids, ct))
            .When(x => x.PlugTypeIds != null)
            .WithMessage("One or more plug type IDs are invalid");

        // Charger point type validation
        RuleFor(x => x.ChargerPointTypeId)
            .MustAsync(async (id, ct) => await ChargerPointTypeExists(id, ct))
            .When(x => x.ChargerPointTypeId.HasValue)
            .WithMessage("Charger point type does not exist");

        // Station type validation
        RuleFor(x => x.StationTypeId)
            .MustAsync(async (id, ct) => await StationTypeExists(id, ct))
            .When(x => x.StationTypeId.HasValue)
            .WithMessage("Station type does not exist");
    }

    private async Task<bool> ChargingPointExists(int id, CancellationToken ct)
        => await _context.ChargingPoints.AnyAsync(x => x.Id == id && !x.IsDeleted, ct);

    private async Task<bool> ValidatePlugTypes(List<int>? ids, CancellationToken ct)
    {
        if (ids == null || !ids.Any()) return true;

        var validIds = await _context.PlugTypes
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);

        return validIds.Count == ids.Count;
    }

    private async Task<bool> ChargerPointTypeExists(int? id, CancellationToken ct)
    {
        if (!id.HasValue) return true;
        return await _context.ChargingPointTypes.AnyAsync(x => x.Id == id.Value, ct);
    }

    private async Task<bool> StationTypeExists(int? id, CancellationToken ct)
    {
        if (!id.HasValue) return true;
        return await _context.StationTypes.AnyAsync(x => x.Id == id.Value, ct);
    }

    private bool IsValidFileSize(IFormFile file)
    {
        var collection = new FormFileCollection { file };
        return _uploadFileService.IsValidSize(collection);
    }

    private bool IsValidFileExtension(IFormFile file)
    {
        var collection = new FormFileCollection { file };
        return _uploadFileService.IsValidExtension(collection);
    }
}
```

### Phase 2: Queries for Viewing Requests

#### Query: `GetPendingUpdateRequestsQuery`

**Location**: `/Application/ChargingPoints/Queries/GetPendingUpdateRequests/GetPendingUpdateRequestsQuery.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetPendingUpdateRequests;

public record GetPendingUpdateRequestsQuery(RequestStatus? Status = null)
    : IRequest<List<ChargingPointUpdateRequestDto>>;

public class GetPendingUpdateRequestsQueryHandler(
    IApplicationDbContext context,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetPendingUpdateRequestsQuery, List<ChargingPointUpdateRequestDto>>
{
    public async Task<List<ChargingPointUpdateRequestDto>> Handle(
        GetPendingUpdateRequestsQuery request,
        CancellationToken ct)
    {
        var query = context.ChargingPointUpdateRequests
            .Include(x => x.ChargingPoint)
            .Include(x => x.RequestedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.AttachmentChanges)
            .Where(x => !x.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(x => x.RequestStatus == request.Status.Value);

        var requests = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return requests.Select(x => MapToDto(x)).ToList();
    }

    private ChargingPointUpdateRequestDto MapToDto(ChargingPointUpdateRequest request)
    {
        return new ChargingPointUpdateRequestDto(
            request.Id,
            request.ChargingPointId,
            request.ChargingPoint.Name,
            request.RequestedBy.Name,
            request.RequestStatus,
            request.CreatedAt!.Value,

            // Changed fields (only non-null means changed)
            request.Name,
            request.Note,
            request.CountryName,
            request.CityName,
            request.Phone,
            request.MethodPayment,
            request.Price,
            request.FromTime,
            request.ToTime,
            request.ChargerSpeed,
            request.ChargersCount,
            request.Latitude,
            request.Longitude,
            request.ChargerPointTypeId,
            request.StationTypeId,
            request.OwnerPhone,
            request.HasOffer,
            request.Service,
            request.OfferDescription,
            request.Address,

            // Icon URLs
            request.NewIcon != null
                ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, request.NewIcon)
                : null,
            request.OldIcon != null
                ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, request.OldIcon)
                : null,

            // Plug types
            request.NewPlugTypeIds,
            request.OldPlugTypeIds,

            // Attachment changes count
            request.AttachmentChanges.Count(x => x.AttachmentAction == AttachmentAction.Add),
            request.AttachmentChanges.Count(x => x.AttachmentAction == AttachmentAction.Delete),

            // Review info
            request.ReviewedBy?.Name,
            request.ReviewedAt,
            request.RejectionReason
        );
    }
}
```

#### DTO: `ChargingPointUpdateRequestDto`

**Location**: `/Application/ChargingPoints/Queries/GetPendingUpdateRequests/ChargingPointUpdateRequestDto.cs`

```csharp
using Cable.Core.Enums;

namespace Application.ChargingPoints.Queries.GetPendingUpdateRequests;

public record ChargingPointUpdateRequestDto(
    int Id,
    int ChargingPointId,
    string ChargingPointName,
    string RequestedByName,
    RequestStatus Status,
    DateTime CreatedAt,

    // Changed fields
    string? Name,
    string? Note,
    string? CountryName,
    string? CityName,
    string? Phone,
    string? MethodPayment,
    double? Price,
    string? FromTime,
    string? ToTime,
    int? ChargerSpeed,
    int? ChargersCount,
    double? Latitude,
    double? Longitude,
    int? ChargerPointTypeId,
    int? StationTypeId,
    string? OwnerPhone,
    bool? HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,

    // Icon
    string? NewIconUrl,
    string? OldIconUrl,

    // Plug types (JSON strings)
    string? NewPlugTypeIds,
    string? OldPlugTypeIds,

    // Attachment changes
    int NewAttachmentsCount,
    int DeletedAttachmentsCount,

    // Review info
    string? ReviewedByName,
    DateTime? ReviewedAt,
    string? RejectionReason
);
```

#### Query: `GetMyUpdateRequestsQuery`

**Location**: `/Application/ChargingPoints/Queries/GetMyUpdateRequests/GetMyUpdateRequestsQuery.cs`

```csharp
using Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetMyUpdateRequests;

public record GetMyUpdateRequestsQuery() : IRequest<List<ChargingPointUpdateRequestDto>>;

public class GetMyUpdateRequestsQueryHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetMyUpdateRequestsQuery, List<ChargingPointUpdateRequestDto>>
{
    public async Task<List<ChargingPointUpdateRequestDto>> Handle(
        GetMyUpdateRequestsQuery request,
        CancellationToken ct)
    {
        var userId = currentUserService.UserId!.Value;

        var requests = await context.ChargingPointUpdateRequests
            .Include(x => x.ChargingPoint)
            .Include(x => x.RequestedBy)
            .Include(x => x.ReviewedBy)
            .Include(x => x.AttachmentChanges)
            .Where(x => !x.IsDeleted && x.RequestedByUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        // Use same mapping logic as GetPendingUpdateRequestsQuery
        return requests.Select(x => MapToDto(x)).ToList();
    }

    // Same MapToDto method as above
}
```

### Phase 3: Admin Approves Request

#### Command: `ApproveUpdateRequestCommand`

**Location**: `/Application/ChargingPoints/Commands/ApproveUpdateRequest/ApproveUpdateRequestCommand.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Application.ChargingPoints.Commands.ApproveUpdateRequest;

public record ApproveUpdateRequestCommand(int RequestId) : IRequest;

public class ApproveUpdateRequestCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<ApproveUpdateRequestCommand>
{
    public async Task Handle(ApproveUpdateRequestCommand request, CancellationToken ct)
    {
        // 1. Get update request with all related data
        var updateRequest = await context.ChargingPointUpdateRequests
            .Include(x => x.ChargingPoint)
                .ThenInclude(x => x.ChargingPlugs)
            .Include(x => x.AttachmentChanges)
            .FirstOrDefaultAsync(x => x.Id == request.RequestId && !x.IsDeleted, ct)
            ?? throw new NotFoundException($"Update request not found: {request.RequestId}");

        if (updateRequest.RequestStatus != RequestStatus.Pending)
            throw new DataValidationException("RequestId", "Only pending requests can be approved");

        var chargingPoint = updateRequest.ChargingPoint;

        // 2. Apply basic field changes (only non-null fields)
        if (updateRequest.Name != null) chargingPoint.Name = updateRequest.Name;
        if (updateRequest.Note != null) chargingPoint.Note = updateRequest.Note;
        if (updateRequest.CountryName != null) chargingPoint.CountryName = updateRequest.CountryName;
        if (updateRequest.CityName != null) chargingPoint.CityName = updateRequest.CityName;
        if (updateRequest.Phone != null) chargingPoint.Phone = updateRequest.Phone;
        if (updateRequest.MethodPayment != null) chargingPoint.MethodPayment = updateRequest.MethodPayment;
        if (updateRequest.Price.HasValue) chargingPoint.Price = updateRequest.Price;
        if (updateRequest.FromTime != null) chargingPoint.FromTime = updateRequest.FromTime;
        if (updateRequest.ToTime != null) chargingPoint.ToTime = updateRequest.ToTime;
        if (updateRequest.ChargerSpeed.HasValue) chargingPoint.ChargerSpeed = updateRequest.ChargerSpeed;
        if (updateRequest.ChargersCount.HasValue) chargingPoint.ChargersCount = updateRequest.ChargersCount;
        if (updateRequest.Latitude.HasValue) chargingPoint.Latitude = updateRequest.Latitude.Value;
        if (updateRequest.Longitude.HasValue) chargingPoint.Longitude = updateRequest.Longitude.Value;
        if (updateRequest.ChargerPointTypeId.HasValue) chargingPoint.ChargerPointTypeId = updateRequest.ChargerPointTypeId.Value;
        if (updateRequest.StationTypeId.HasValue) chargingPoint.StationTypeId = updateRequest.StationTypeId.Value;
        if (updateRequest.OwnerPhone != null) chargingPoint.OwnerPhone = updateRequest.OwnerPhone;
        if (updateRequest.HasOffer.HasValue) chargingPoint.HasOffer = updateRequest.HasOffer.Value;
        if (updateRequest.Service != null) chargingPoint.Service = updateRequest.Service;
        if (updateRequest.OfferDescription != null) chargingPoint.OfferDescription = updateRequest.OfferDescription;
        if (updateRequest.Address != null) chargingPoint.Address = updateRequest.Address;

        // 3. Apply icon change
        if (updateRequest.NewIcon != null)
        {
            // Delete old icon if exists
            if (!string.IsNullOrEmpty(chargingPoint.Icon))
            {
                uploadFileService.DeleteFiles(
                    UploadFileFolders.CableChargingPoint,
                    [chargingPoint.Icon],
                    ct);
            }

            chargingPoint.Icon = updateRequest.NewIcon;
        }

        // 4. Apply plug type changes
        if (updateRequest.NewPlugTypeIds != null)
        {
            var newPlugTypeIds = JsonSerializer.Deserialize<List<int>>(updateRequest.NewPlugTypeIds)!;

            // Remove existing plugs
            var existingPlugs = chargingPoint.ChargingPlugs.Where(x => !x.IsDeleted).ToList();
            context.ChargingPlugs.RemoveRange(existingPlugs);

            // Add new plugs
            var newPlugs = newPlugTypeIds.Select(plugTypeId => new ChargingPlug
            {
                PlugTypeId = plugTypeId,
                ChargingPointId = chargingPoint.Id,
                IsDeleted = false
            }).ToList();

            await context.ChargingPlugs.AddRangeAsync(newPlugs, ct);
        }

        // 5. Apply attachment changes
        foreach (var attachmentChange in updateRequest.AttachmentChanges)
        {
            if (attachmentChange.AttachmentAction == AttachmentAction.Add)
            {
                // Add new attachment
                context.ChargingPointAttachments.Add(new ChargingPointAttachment
                {
                    ChargingPointId = chargingPoint.Id,
                    FileName = attachmentChange.FileName!,
                    FileExtension = attachmentChange.FileExtension!,
                    FileSize = attachmentChange.FileSize!.Value,
                    ContentType = attachmentChange.ContentType!
                });
            }
            else if (attachmentChange.AttachmentAction == AttachmentAction.Delete)
            {
                // Delete existing attachment
                var attachmentToDelete = await context.ChargingPointAttachments
                    .FirstOrDefaultAsync(x => x.Id == attachmentChange.ExistingAttachmentId!.Value, ct);

                if (attachmentToDelete != null)
                {
                    // Delete physical file
                    uploadFileService.DeleteFiles(
                        UploadFileFolders.CableAttachments,
                        [attachmentToDelete.FileName],
                        ct);

                    // Remove from database
                    context.ChargingPointAttachments.Remove(attachmentToDelete);
                }
            }
        }

        // 6. Update request status
        updateRequest.RequestStatus = RequestStatus.Approved;
        updateRequest.ReviewedByUserId = currentUserService.UserId;
        updateRequest.ReviewedAt = DateTime.UtcNow;

        await context.SaveChanges(ct);

        // 7. TODO: Send notification to charging point owner about approval
    }
}
```

#### Validator: `ApproveUpdateRequestCommandValidator`

**Location**: `/Application/ChargingPoints/Commands/ApproveUpdateRequest/ApproveUpdateRequestCommandValidator.cs`

```csharp
using Application.Common.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.ApproveUpdateRequest;

public class ApproveUpdateRequestCommandValidator : AbstractValidator<ApproveUpdateRequestCommand>
{
    private readonly IApplicationDbContext _context;

    public ApproveUpdateRequestCommandValidator(IApplicationDbContext context)
    {
        _context = context;

        RuleFor(x => x.RequestId)
            .GreaterThan(0)
            .MustAsync(RequestExists)
            .WithMessage("Update request does not exist");
    }

    private async Task<bool> RequestExists(int id, CancellationToken ct)
        => await _context.ChargingPointUpdateRequests
            .AnyAsync(x => x.Id == id && !x.IsDeleted, ct);
}
```

### Phase 4: Admin Rejects Request

#### Command: `RejectUpdateRequestCommand`

**Location**: `/Application/ChargingPoints/Commands/RejectUpdateRequest/RejectUpdateRequestCommand.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.RejectUpdateRequest;

public record RejectUpdateRequestCommand(int RequestId, string Reason) : IRequest;

public class RejectUpdateRequestCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<RejectUpdateRequestCommand>
{
    public async Task Handle(RejectUpdateRequestCommand request, CancellationToken ct)
    {
        // 1. Get update request
        var updateRequest = await context.ChargingPointUpdateRequests
            .Include(x => x.AttachmentChanges)
            .FirstOrDefaultAsync(x => x.Id == request.RequestId && !x.IsDeleted, ct)
            ?? throw new NotFoundException($"Update request not found: {request.RequestId}");

        if (updateRequest.RequestStatus != RequestStatus.Pending)
            throw new DataValidationException("RequestId", "Only pending requests can be rejected");

        // 2. Delete uploaded files (icon and new attachments) since they won't be used
        if (updateRequest.NewIcon != null)
        {
            uploadFileService.DeleteFiles(
                UploadFileFolders.CableChargingPoint,
                [updateRequest.NewIcon],
                ct);
        }

        foreach (var attachmentChange in updateRequest.AttachmentChanges)
        {
            if (attachmentChange.AttachmentAction == AttachmentAction.Add &&
                attachmentChange.FileName != null)
            {
                uploadFileService.DeleteFiles(
                    UploadFileFolders.CableAttachments,
                    [attachmentChange.FileName],
                    ct);
            }
        }

        // 3. Update request status
        updateRequest.RequestStatus = RequestStatus.Rejected;
        updateRequest.ReviewedByUserId = currentUserService.UserId;
        updateRequest.ReviewedAt = DateTime.UtcNow;
        updateRequest.RejectionReason = request.Reason;

        await context.SaveChanges(ct);

        // 4. TODO: Send notification to charging point owner about rejection
    }
}
```

#### Validator: `RejectUpdateRequestCommandValidator`

**Location**: `/Application/ChargingPoints/Commands/RejectUpdateRequest/RejectUpdateRequestCommandValidator.cs`

```csharp
using Application.Common.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.RejectUpdateRequest;

public class RejectUpdateRequestCommandValidator : AbstractValidator<RejectUpdateRequestCommand>
{
    private readonly IApplicationDbContext _context;

    public RejectUpdateRequestCommandValidator(IApplicationDbContext context)
    {
        _context = context;

        RuleFor(x => x.RequestId)
            .GreaterThan(0)
            .MustAsync(RequestExists)
            .WithMessage("Update request does not exist");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Rejection reason is required")
            .MaximumLength(500)
            .WithMessage("Rejection reason must not exceed 500 characters");
    }

    private async Task<bool> RequestExists(int id, CancellationToken ct)
        => await _context.ChargingPointUpdateRequests
            .AnyAsync(x => x.Id == id && !x.IsDeleted, ct);
}
```

### Phase 5: Owner Cancels Pending Request

#### Command: `CancelUpdateRequestCommand`

**Location**: `/Application/ChargingPoints/Commands/CancelUpdateRequest/CancelUpdateRequestCommand.cs`

```csharp
using Application.Common.Interfaces;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.CancelUpdateRequest;

public record CancelUpdateRequestCommand(int RequestId) : IRequest;

public class CancelUpdateRequestCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    IUploadFileService uploadFileService)
    : IRequestHandler<CancelUpdateRequestCommand>
{
    public async Task Handle(CancelUpdateRequestCommand request, CancellationToken ct)
    {
        // 1. Get update request
        var updateRequest = await context.ChargingPointUpdateRequests
            .Include(x => x.AttachmentChanges)
            .FirstOrDefaultAsync(x => x.Id == request.RequestId && !x.IsDeleted, ct)
            ?? throw new NotFoundException($"Update request not found: {request.RequestId}");

        // 2. Verify ownership
        if (updateRequest.RequestedByUserId != currentUserService.UserId)
            throw new ForbiddenAccessException("You can only cancel your own requests");

        // 3. Verify status is pending
        if (updateRequest.RequestStatus != RequestStatus.Pending)
            throw new DataValidationException("RequestId", "Only pending requests can be cancelled");

        // 4. Delete uploaded files
        if (updateRequest.NewIcon != null)
        {
            uploadFileService.DeleteFiles(
                UploadFileFolders.CableChargingPoint,
                [updateRequest.NewIcon],
                ct);
        }

        foreach (var attachmentChange in updateRequest.AttachmentChanges)
        {
            if (attachmentChange.AttachmentAction == AttachmentAction.Add &&
                attachmentChange.FileName != null)
            {
                uploadFileService.DeleteFiles(
                    UploadFileFolders.CableAttachments,
                    [attachmentChange.FileName],
                    ct);
            }
        }

        // 5. Soft delete the request
        updateRequest.IsDeleted = true;
        await context.SaveChanges(ct);
    }
}
```

---

## 🌐 API Endpoints

### WebApi Request DTOs

#### Request: `SubmitChargingPointUpdateRequest`

**Location**: `/WebApi/Requests/ChargingPoints/SubmitChargingPointUpdateRequest.cs`

```csharp
namespace Cable.Requests.ChargingPoints;

/// <summary>
/// Request to submit charging point update for admin approval
/// </summary>
public record SubmitChargingPointUpdateRequest(
    string? Name,
    string? Note,
    string? CountryName,
    string? CityName,
    string? Phone,
    string? MethodPayment,
    double? Price,
    string? FromTime,
    string? ToTime,
    int? ChargerSpeed,
    int? ChargersCount,
    double? Latitude,
    double? Longitude,
    int? ChargerPointTypeId,
    int? StationTypeId,
    string? OwnerPhone,
    bool? HasOffer,
    string? Service,
    string? OfferDescription,
    string? Address,
    List<int>? PlugTypeIds,
    List<int>? AttachmentsToDelete
);
```

#### Request: `RejectUpdateRequestRequest`

**Location**: `/WebApi/Requests/ChargingPoints/RejectUpdateRequestRequest.cs`

```csharp
namespace Cable.Requests.ChargingPoints;

/// <summary>
/// Request to reject an update request with reason
/// </summary>
/// <param name="Reason">Reason for rejection</param>
public record RejectUpdateRequestRequest(string Reason);
```

### Route Mapping

#### For Station Owners (in `MapStationRoutes`)

**Location**: `/WebApi/Routes/ChargingPointsRoutes.cs`

```csharp
// 1. Submit Update Request
app.MapPost("/submit-update-request/{chargingPointId:int}",
        async (IMediator mediator,
               [FromRoute] int chargingPointId,
               [FromForm] SubmitChargingPointUpdateRequest request,
               [FromForm] IFormFile? newIcon,
               [FromForm] IFormFileCollection? newAttachments,
               CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new SubmitChargingPointUpdateRequestCommand(
                chargingPointId,
                request.Name,
                request.Note,
                request.CountryName,
                request.CityName,
                request.Phone,
                request.MethodPayment,
                request.Price,
                request.FromTime,
                request.ToTime,
                request.ChargerSpeed,
                request.ChargersCount,
                request.Latitude,
                request.Longitude,
                request.ChargerPointTypeId,
                request.StationTypeId,
                request.OwnerPhone,
                request.HasOffer,
                request.Service,
                request.OfferDescription,
                request.Address,
                request.PlugTypeIds,
                newIcon,
                request.AttachmentsToDelete,
                newAttachments
            ), cancellationToken)))
    .Produces<int>()
    .RequireAuthorization()
    .ProducesUnAuthorized()
    .ProducesForbidden()
    .ProducesValidationProblem()
    .ProducesInternalServerError()
    .DisableAntiforgery()
    .WithName("Submit Charging Point Update Request")
    .WithSummary("Station owners submit update request for their charging point")
    .WithDescription("Creates a pending update request that requires admin approval. Supports changing basic info, plug types, icon, and attachments. Only one pending request allowed per charging point.")
    .WithOpenApi(op =>
    {
        op.Parameters[0].Required = true;
        op.Parameters[0].Description = "The ID of the charging point to update";
        op.RequestBody.Required = true;
        return op;
    });

// 2. Get My Update Requests
app.MapGet("/my-update-requests",
        async (IMediator mediator, CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new GetMyUpdateRequestsQuery(), cancellationToken)))
    .Produces<List<ChargingPointUpdateRequestDto>>()
    .RequireAuthorization()
    .ProducesUnAuthorized()
    .ProducesInternalServerError()
    .WithName("Get My Update Requests")
    .WithSummary("Get all update requests submitted by current user")
    .WithDescription("Returns list of all update requests (pending, approved, rejected) created by the authenticated user.")
    .WithOpenApi();

// 3. Cancel Pending Request
app.MapDelete("/cancel-update-request/{requestId:int}",
        async (IMediator mediator, [FromRoute] int requestId, CancellationToken cancellationToken) =>
            await mediator.Send(new CancelUpdateRequestCommand(requestId), cancellationToken))
    .Produces(200)
    .RequireAuthorization()
    .ProducesUnAuthorized()
    .ProducesForbidden()
    .ProducesNotFound()
    .ProducesValidationProblem()
    .ProducesInternalServerError()
    .WithName("Cancel Update Request")
    .WithSummary("Cancel a pending update request")
    .WithDescription("Allows owners to cancel their pending update requests. Uploaded files will be deleted.")
    .WithOpenApi(op =>
    {
        op.Parameters[0].Required = true;
        op.Parameters[0].Description = "The ID of the update request to cancel";
        return op;
    });
```

#### For Admins (Create new route group or add to existing admin routes)

**Location**: Create `/WebApi/Routes/ChargingPointUpdateRequestRoutes.cs` or add to existing admin routes

```csharp
using Application.ChargingPoints.Commands.ApproveUpdateRequest;
using Application.ChargingPoints.Commands.RejectUpdateRequest;
using Application.ChargingPoints.Queries.GetPendingUpdateRequests;
using Cable.Core.Enums;
using Cable.Requests.ChargingPoints;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ChargingPointUpdateRequestRoutes
{
    public static IEndpointRouteBuilder MapChargingPointUpdateRequestRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/admin/charging-point-update-requests")
            .WithTags("Admin - Charging Point Update Requests")
            .RequireAuthorization() // Add admin role requirement here
            .MapAdminRoutes();

        return app;
    }

    private static RouteGroupBuilder MapAdminRoutes(this RouteGroupBuilder app)
    {
        // 1. Get All Update Requests (with optional filter)
        app.MapGet("",
                async (IMediator mediator, [FromQuery] RequestStatus? status, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetPendingUpdateRequestsQuery(status), cancellationToken)))
            .Produces<List<ChargingPointUpdateRequestDto>>()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get All Update Requests")
            .WithSummary("Get all charging point update requests with optional status filter")
            .WithDescription("Returns all update requests. Filter by status: 0=Pending, 1=Approved, 2=Rejected")
            .WithOpenApi();

        // 2. Approve Update Request
        app.MapPost("/{requestId:int}/approve",
                async (IMediator mediator, [FromRoute] int requestId, CancellationToken cancellationToken) =>
                    await mediator.Send(new ApproveUpdateRequestCommand(requestId), cancellationToken))
            .Produces(200)
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Approve Update Request")
            .WithSummary("Approve a pending update request")
            .WithDescription("Applies all changes from the update request to the actual charging point. Deletes old files and marks request as approved.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the update request to approve";
                return op;
            });

        // 3. Reject Update Request
        app.MapPost("/{requestId:int}/reject",
                async (IMediator mediator, [FromRoute] int requestId, RejectUpdateRequestRequest request, CancellationToken cancellationToken) =>
                    await mediator.Send(new RejectUpdateRequestCommand(requestId, request.Reason), cancellationToken))
            .Produces(200)
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Reject Update Request")
            .WithSummary("Reject a pending update request with reason")
            .WithDescription("Rejects the update request. Deletes uploaded files and stores rejection reason for owner to see.")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the update request to reject";
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }
}
```

**Register in Program.cs**:

```csharp
// Add after other route mappings
app.MapChargingPointUpdateRequestRoutes();
```

---

## ✅ Approval Flow

### Visual Flow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    APPROVAL FLOW                             │
└─────────────────────────────────────────────────────────────┘

Owner submits request
    ↓
Files uploaded to disk:
  - NewIcon → D:\Attachments\CableChargingPoint\{uuid}.jpg
  - NewAttachments → D:\Attachments\CableAttachments\{uuid}.jpg
    ↓
Request saved:
  - ChargingPointUpdateRequest (Status: Pending)
  - ChargingPointUpdateRequestAttachment records
    ↓
Admin reviews and clicks "Approve"
    ↓
ApproveUpdateRequestCommand executes:
    ↓
    ├─ Update ChargingPoint fields ✓
    │  (Name, Price, ChargerSpeed, etc.)
    ↓
    ├─ Replace Icon ✓
    │  ├─ Delete old icon file
    │  └─ Set ChargingPoint.Icon = NewIcon
    ↓
    ├─ Replace Plug Types ✓
    │  ├─ Delete old ChargingPlug records
    │  └─ Insert new ChargingPlug records
    ↓
    ├─ Apply Attachment Changes ✓
    │  ├─ For Action=Add: Create ChargingPointAttachment
    │  └─ For Action=Delete: Delete attachment + file
    ↓
    ├─ Update Request Status ✓
    │  ├─ RequestStatus = Approved
    │  ├─ ReviewedByUserId = CurrentAdmin
    │  └─ ReviewedAt = Now
    ↓
    └─ Send notification to owner ✓

Result:
  ✅ ChargingPoint updated with new values
  ✅ Old icon deleted, new icon in use
  ✅ Plug types updated
  ✅ Attachments added/deleted
  ✅ Request marked as Approved
  ✅ Audit trail preserved
```

### Database State Changes on Approval

| Table | Before | After |
|-------|--------|-------|
| **ChargingPointUpdateRequest** | Status: Pending | Status: Approved, ReviewedBy filled |
| **ChargingPointUpdateRequestAttachment** | Records exist | Records kept (audit trail) |
| **ChargingPoint** | Old values | ✅ **New values applied** |
| **ChargingPlug** | Old plug types | ✅ **New plug types** |
| **ChargingPointAttachment** | Existing attachments | ✅ **New added, marked deleted removed** |
| **Disk - Old Icon** | Exists | ❌ **Deleted** |
| **Disk - New Icon** | Exists (staged) | ✅ **Kept and used** |
| **Disk - New Attachments** | Exist (staged) | ✅ **Kept and used** |
| **Disk - Deleted Attachments** | Exist | ❌ **Deleted** |

---

## ❌ Rejection Flow

### Visual Flow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    REJECTION FLOW                            │
└─────────────────────────────────────────────────────────────┘

Owner submits request
    ↓
Files uploaded to disk:
  - NewIcon → D:\Attachments\CableChargingPoint\{uuid}.jpg
  - NewAttachments → D:\Attachments\CableAttachments\{uuid}.jpg
    ↓
Request saved:
  - ChargingPointUpdateRequest (Status: Pending)
  - ChargingPointUpdateRequestAttachment records
    ↓
Admin reviews and clicks "Reject" with reason
    ↓
RejectUpdateRequestCommand executes:
    ↓
    ├─ Delete New Icon File ✓
    │  (D:\Attachments\CableChargingPoint\{uuid}.jpg)
    ↓
    ├─ Delete New Attachment Files ✓
    │  (D:\Attachments\CableAttachments\{uuid}.jpg)
    ↓
    ├─ Update Request Status ✓
    │  ├─ RequestStatus = Rejected
    │  ├─ ReviewedByUserId = CurrentAdmin
    │  ├─ ReviewedAt = Now
    │  └─ RejectionReason = "Images not clear"
    ↓
    ├─ Keep Database Records ✓
    │  (For audit trail)
    ↓
    └─ Send notification to owner with reason ✓

Result:
  ✅ ChargingPoint NOT TOUCHED (remains unchanged)
  ✅ New files deleted (cleanup)
  ✅ Old files kept (still in use)
  ✅ Request marked as Rejected with reason
  ✅ Database records kept for audit
  ✅ Owner can see rejection reason
```

### Database State Changes on Rejection

| Table | Before | After |
|-------|--------|-------|
| **ChargingPointUpdateRequest** | Status: Pending | Status: Rejected, Reason filled |
| **ChargingPointUpdateRequestAttachment** | Records exist | ✅ **Records kept (audit trail)** |
| **ChargingPoint** | Current values | ✅ **NOT TOUCHED** |
| **ChargingPlug** | Current plug types | ✅ **NOT TOUCHED** |
| **ChargingPointAttachment** | Current attachments | ✅ **NOT TOUCHED** |
| **Disk - Old Icon** | Exists | ✅ **Kept (still in use)** |
| **Disk - New Icon** | Exists (staged) | ❌ **Deleted (cleanup)** |
| **Disk - New Attachments** | Exist (staged) | ❌ **Deleted (cleanup)** |
| **Disk - Existing Attachments** | Exist | ✅ **Kept (still in use)** |

---

## 📝 Implementation Checklist

### Phase 1: Database Setup
- [ ] Create `RequestStatus` enum in `/Cable.Core/Enums/RequestStatus.cs`
- [ ] Create `AttachmentAction` enum in `/Cable.Core/Enums/AttachmentAction.cs`
- [ ] Create `ChargingPointUpdateRequest` entity
- [ ] Create `ChargingPointUpdateRequestAttachment` entity
- [ ] Create EF Core configuration for `ChargingPointUpdateRequest`
- [ ] Create EF Core configuration for `ChargingPointUpdateRequestAttachment`
- [ ] Update `IApplicationDbContext` interface with new DbSets
- [ ] Update `ApplicationDbContext` with new DbSets and configurations
- [ ] Create and run EF Core migration
- [ ] Verify tables and indexes created correctly

### Phase 2: Submit Update Request (Owner)
- [ ] Create `SubmitChargingPointUpdateRequestCommand` + Handler
- [ ] Create `SubmitChargingPointUpdateRequestCommandValidator`
- [ ] Create `SubmitChargingPointUpdateRequest` DTO (WebApi)
- [ ] Add endpoint in `MapStationRoutes`
- [ ] Test: Submit request with basic field changes
- [ ] Test: Submit request with icon upload
- [ ] Test: Submit request with new attachments
- [ ] Test: Submit request with attachment deletions
- [ ] Test: Submit request with plug type changes
- [ ] Test: Validation - duplicate pending requests blocked
- [ ] Test: Authorization - only owner can submit

### Phase 3: View Update Requests
- [ ] Create `GetPendingUpdateRequestsQuery` + Handler
- [ ] Create `ChargingPointUpdateRequestDto`
- [ ] Create `GetMyUpdateRequestsQuery` + Handler
- [ ] Add admin endpoint to get all requests
- [ ] Add owner endpoint to get their requests
- [ ] Test: Admin can see all pending requests
- [ ] Test: Owner can see their requests
- [ ] Test: Filter by status works
- [ ] Test: URLs for icons and attachments are correct

### Phase 4: Approve Update Request (Admin)
- [ ] Create `ApproveUpdateRequestCommand` + Handler
- [ ] Create `ApproveUpdateRequestCommandValidator`
- [ ] Add admin endpoint to approve request
- [ ] Test: Approve with basic field changes - fields updated
- [ ] Test: Approve with icon change - old deleted, new used
- [ ] Test: Approve with plug types - old removed, new added
- [ ] Test: Approve with new attachments - attachments added
- [ ] Test: Approve with deleted attachments - files deleted
- [ ] Test: Request status changed to Approved
- [ ] Test: ReviewedBy and ReviewedAt populated
- [ ] Test: Can't approve already approved/rejected request

### Phase 5: Reject Update Request (Admin)
- [ ] Create `RejectUpdateRequestCommand` + Handler
- [ ] Create `RejectUpdateRequestCommandValidator`
- [ ] Create `RejectUpdateRequestRequest` DTO (WebApi)
- [ ] Add admin endpoint to reject request
- [ ] Test: Reject request - files deleted
- [ ] Test: Reject request - ChargingPoint NOT modified
- [ ] Test: Request status changed to Rejected
- [ ] Test: Rejection reason stored
- [ ] Test: ReviewedBy and ReviewedAt populated
- [ ] Test: Can't reject already approved/rejected request
- [ ] Test: Owner can see rejection reason

### Phase 6: Cancel Request (Owner)
- [ ] Create `CancelUpdateRequestCommand` + Handler
- [ ] Add owner endpoint to cancel request
- [ ] Test: Owner can cancel their pending request
- [ ] Test: Files deleted on cancel
- [ ] Test: Request soft deleted
- [ ] Test: Can't cancel approved/rejected request
- [ ] Test: Can't cancel another owner's request

### Phase 7: Notifications (Optional)
- [ ] Send notification to admins on new request
- [ ] Send notification to owner on approval
- [ ] Send notification to owner on rejection
- [ ] Test: Notifications sent correctly

### Phase 8: Integration Testing
- [ ] Test complete flow: submit → approve → verify changes
- [ ] Test complete flow: submit → reject → verify cleanup
- [ ] Test concurrent requests to same charging point
- [ ] Test file upload limits and validations
- [ ] Test authorization for all endpoints
- [ ] Test with production-like data volume

---

## 🧪 Testing Scenarios

### Scenario 1: Submit and Approve Basic Changes

```
1. Owner logs in
2. Submits update request:
   - Change Name: "Downtown Station" → "Downtown EV Hub"
   - Change Price: 10.5 → 12.0
3. Verify: Request created with Status: Pending
4. Admin logs in
5. Views pending requests
6. Approves request
7. Verify:
   ✅ ChargingPoint.Name = "Downtown EV Hub"
   ✅ ChargingPoint.Price = 12.0
   ✅ Request.Status = Approved
```

### Scenario 2: Submit with Icon and Reject

```
1. Owner uploads new icon during update request
2. Verify: File exists at D:\Attachments\CableChargingPoint\{uuid}.jpg
3. Admin rejects with reason: "Icon quality too low"
4. Verify:
   ✅ File deleted from disk
   ✅ Request.Status = Rejected
   ✅ Request.RejectionReason = "Icon quality too low"
   ✅ ChargingPoint.Icon unchanged (old icon still used)
```

### Scenario 3: Complex Update with Attachments

```
1. Owner submits request:
   - Add 3 new attachment photos
   - Delete 2 existing attachments
   - Change plug types: [1, 2] → [2, 3, 4]
2. Admin approves
3. Verify:
   ✅ 3 new ChargingPointAttachment records created
   ✅ 2 old attachments deleted (records + files)
   ✅ ChargingPlug records updated correctly
   ✅ Old plug type 1 removed, new types 3 and 4 added
```

### Scenario 4: Duplicate Pending Request Blocked

```
1. Owner submits update request for Charging Point #123
2. Request in Pending status
3. Owner tries to submit another update for same charging point
4. Verify:
   ❌ Validation error: "There is already a pending update request"
   ❌ Second request NOT created
```

### Scenario 5: Authorization Checks

```
1. Owner A submits request for their charging point
2. Owner B tries to approve (not admin)
3. Verify: ❌ 403 Forbidden
4. Owner B tries to submit update for Owner A's charging point
5. Verify: ❌ 403 Forbidden
```

---

## 📚 API Usage Examples

### Example 1: Submit Update Request

**Request:**
```http
POST /api/charging-points/submit-update-request/123
Authorization: Bearer {owner-token}
Content-Type: multipart/form-data

{
    "name": "Downtown EV Hub",
    "price": 12.5,
    "chargerSpeed": 150,
    "plugTypeIds": [2, 3, 4],
    "attachmentsToDelete": [101, 102],
    "newIcon": [binary file data],
    "newAttachments": [binary file 1, binary file 2]
}
```

**Response:**
```json
{
    "data": 456,
    "message": "Update request submitted successfully"
}
```

### Example 2: Get My Update Requests

**Request:**
```http
GET /api/charging-points/my-update-requests
Authorization: Bearer {owner-token}
```

**Response:**
```json
[
    {
        "id": 456,
        "chargingPointId": 123,
        "chargingPointName": "Downtown Station",
        "requestedByName": "John Doe",
        "status": 0,
        "createdAt": "2026-01-15T10:00:00Z",
        "name": "Downtown EV Hub",
        "price": 12.5,
        "chargerSpeed": 150,
        "newIconUrl": "https://localhost/CableChargingPoint/01934d2a-8b6f-7890-abcd-1234567890ab.jpg",
        "oldIconUrl": "https://localhost/CableChargingPoint/old-icon-uuid.jpg",
        "newPlugTypeIds": "[2,3,4]",
        "oldPlugTypeIds": "[1,2]",
        "newAttachmentsCount": 2,
        "deletedAttachmentsCount": 2,
        "reviewedByName": null,
        "reviewedAt": null,
        "rejectionReason": null
    }
]
```

### Example 3: Admin Gets Pending Requests

**Request:**
```http
GET /api/admin/charging-point-update-requests?status=0
Authorization: Bearer {admin-token}
```

**Response:**
```json
[
    {
        "id": 456,
        "chargingPointId": 123,
        "chargingPointName": "Downtown Station",
        "requestedByName": "John Doe",
        "status": 0,
        "createdAt": "2026-01-15T10:00:00Z",
        ...
    }
]
```

### Example 4: Admin Approves Request

**Request:**
```http
POST /api/admin/charging-point-update-requests/456/approve
Authorization: Bearer {admin-token}
```

**Response:**
```json
{
    "message": "Update request approved successfully"
}
```

### Example 5: Admin Rejects Request

**Request:**
```http
POST /api/admin/charging-point-update-requests/456/reject
Authorization: Bearer {admin-token}
Content-Type: application/json

{
    "reason": "Uploaded images are not clear enough. Please provide higher quality photos."
}
```

**Response:**
```json
{
    "message": "Update request rejected"
}
```

### Example 6: Owner Cancels Request

**Request:**
```http
DELETE /api/charging-points/cancel-update-request/456
Authorization: Bearer {owner-token}
```

**Response:**
```json
{
    "message": "Update request cancelled successfully"
}
```

---

## 🎯 Key Benefits

1. ✅ **No Production Impact Until Approved** - Changes staged separately
2. ✅ **Complete Audit Trail** - All requests logged with before/after values
3. ✅ **File Management** - Automatic cleanup on rejection/cancellation
4. ✅ **Ownership Validation** - Only owners can submit for their charging points
5. ✅ **Prevents Duplicate Requests** - One pending request per charging point
6. ✅ **Flexible Changes** - Support for all fields, files, and relationships
7. ✅ **Admin Control** - Full review and approval workflow
8. ✅ **Transparency** - Owners see rejection reasons
9. ✅ **Rollback Capability** - Old values preserved in request record

---

## 🔐 Security Considerations

1. **Authorization**:
   - Owners can only submit for their charging points
   - Only admins can approve/reject
   - Owners can only cancel their own requests

2. **File Upload Security**:
   - File size limits enforced (5 MB)
   - File type restrictions (.jpg, .jpeg, .png only)
   - UUID-based filenames prevent guessing
   - Files deleted on rejection to prevent disk bloat

3. **Validation**:
   - Comprehensive FluentValidation on all commands
   - Database constraints enforced
   - Duplicate pending request prevention

4. **Audit Trail**:
   - All actions logged with timestamps
   - Creator and reviewer tracked
   - Rejection reasons stored

---

## 📌 Future Enhancements (Optional)

1. **Batch Approval** - Approve multiple requests at once
2. **Approval Delegation** - Allow multiple admin roles
3. **Request Editing** - Allow owners to edit pending requests
4. **Auto-Approval Rules** - Auto-approve trusted owners or minor changes
5. **Change Comparison UI** - Visual diff of old vs new values
6. **Request Expiry** - Auto-reject requests older than X days
7. **Approval Comments** - Allow admins to add notes during approval
8. **Notification Preferences** - Let owners configure how they're notified
9. **Request Templates** - Pre-fill common update patterns
10. **Analytics Dashboard** - Track approval rates, common rejection reasons

---

## 🚀 Ready to Implement

This comprehensive plan provides everything needed to implement the Charging Point Update Approval System. Follow the implementation checklist phase by phase, and refer to the code examples for guidance.

**Next Steps:**
1. Create database migration
2. Implement Phase 1 (Submit Update Request)
3. Test thoroughly
4. Proceed to subsequent phases

Good luck with the implementation! 🎉
