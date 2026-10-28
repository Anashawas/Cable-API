using Application.Common.Security;
using Application.Common.Utilities;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.CableGifts.Commands.ManageGifts;

/// <summary>The admin form of a gift (both languages). Dates without an offset are Jordan time.</summary>
public record CableGiftInput(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? HowToReceiveAr,
    string? HowToReceiveEn,
    GiftType GiftType,
    int PointsCost,
    /// <summary>Null = unlimited.</summary>
    int? Stock,
    /// <summary>Null = unlimited.</summary>
    int? PerUserLimit,
    DateTime? StartsAt,
    DateTime? EndsAt,
    /// <summary>City names (Arabic or English, resolved to the canonical list); empty = every city.</summary>
    List<string>? Cities,
    int SortOrder = 0,
    bool IsActive = true);

public class CableGiftInputValidator : AbstractValidator<CableGiftInput>
{
    public CableGiftInputValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().MaximumLength(150);
        RuleFor(x => x.NameEn).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DescriptionAr).MaximumLength(2000);
        RuleFor(x => x.DescriptionEn).MaximumLength(2000);
        RuleFor(x => x.HowToReceiveAr).MaximumLength(1000);
        RuleFor(x => x.HowToReceiveEn).MaximumLength(1000);
        RuleFor(x => x.GiftType).IsInEnum();
        RuleFor(x => x.PointsCost).GreaterThan(0);
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0).When(x => x.Stock.HasValue);
        RuleFor(x => x.PerUserLimit).GreaterThan(0).When(x => x.PerUserLimit.HasValue);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt!.Value)
            .When(x => x.StartsAt.HasValue && x.EndsAt.HasValue)
            .WithMessage("EndsAt must be after StartsAt");
    }
}

/// <summary>Shared by create and update.</summary>
public static class CableGiftInputMapper
{
    public static void Apply(CableGift gift, CableGiftInput input)
    {
        gift.NameAr = input.NameAr.Trim();
        gift.NameEn = input.NameEn.Trim();
        gift.DescriptionAr = Clean(input.DescriptionAr);
        gift.DescriptionEn = Clean(input.DescriptionEn);
        gift.HowToReceiveAr = Clean(input.HowToReceiveAr);
        gift.HowToReceiveEn = Clean(input.HowToReceiveEn);
        gift.GiftType = input.GiftType;
        gift.PointsCost = input.PointsCost;
        gift.Stock = input.Stock;
        gift.PerUserLimit = input.PerUserLimit;
        gift.StartsAt = JordanTime.ToUtc(input.StartsAt);
        gift.EndsAt = JordanTime.ToUtc(input.EndsAt);
        gift.Cities = CsvList.Join(ResolveCities(input.Cities));
        gift.SortOrder = input.SortOrder;
        gift.IsActive = input.IsActive;
    }

    /// <summary>Arabic or English city names → the canonical list; an unknown name is a 400, never silently dropped.</summary>
    public static List<string> ResolveCities(List<string>? cities)
    {
        var result = new List<string>();
        foreach (var raw in cities ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var city = JordanCityResolver.Resolve(raw)
                       ?? throw new DataValidationException("Cities",
                           $"Unknown city '{raw}'. Use one of: {string.Join(", ", JordanCityResolver.CanonicalCities)}");
            if (!result.Contains(city)) result.Add(city);
        }
        return result;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>POST /api/admin/cable-gifts. Returns the new id.</summary>
public record CreateCableGiftCommand(CableGiftInput Input) : IRequest<int>;

public class CreateCableGiftCommandValidator : AbstractValidator<CreateCableGiftCommand>
{
    public CreateCableGiftCommandValidator() => RuleFor(x => x.Input).NotNull().SetValidator(new CableGiftInputValidator());
}

public class CreateCableGiftCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<CreateCableGiftCommand, int>
{
    public async Task<int> Handle(CreateCableGiftCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var gift = new CableGift();
        CableGiftInputMapper.Apply(gift, request.Input);
        db.CableGifts.Add(gift);
        await db.SaveChanges(cancellationToken);
        return gift.Id;
    }
}

/// <summary>PUT /api/admin/cable-gifts/{id}. The stock can't go below the pieces already ordered.</summary>
public record UpdateCableGiftCommand(int Id, CableGiftInput Input) : IRequest;

public class UpdateCableGiftCommandValidator : AbstractValidator<UpdateCableGiftCommand>
{
    public UpdateCableGiftCommandValidator() => RuleFor(x => x.Input).NotNull().SetValidator(new CableGiftInputValidator());
}

public class UpdateCableGiftCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<UpdateCableGiftCommand>
{
    public async Task Handle(UpdateCableGiftCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var gift = await db.CableGifts.FirstOrDefaultAsync(g => g.Id == request.Id && !g.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"Gift {request.Id} was not found");

        if (request.Input.Stock.HasValue && request.Input.Stock.Value < gift.OrdersCount)
            throw new DataValidationException("Stock",
                $"Stock can't be below the {gift.OrdersCount} pieces already ordered");

        CableGiftInputMapper.Apply(gift, request.Input);
        await db.SaveChanges(cancellationToken);
    }
}

/// <summary>PATCH /api/admin/cable-gifts/{id}/active.</summary>
public record SetCableGiftActiveCommand(int Id, bool IsActive) : IRequest;

public class SetCableGiftActiveCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetCableGiftActiveCommand>
{
    public async Task Handle(SetCableGiftActiveCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var gift = await db.CableGifts.FirstOrDefaultAsync(g => g.Id == request.Id && !g.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"Gift {request.Id} was not found");
        gift.IsActive = request.IsActive;
        await db.SaveChanges(cancellationToken);
    }
}

public record CableGiftImageResult(string ImageUrl);

/// <summary>POST /api/admin/cable-gifts/{id}/image — multipart, one jpg/png/webp up to 5 MB; sets imageUrl.</summary>
public record UploadCableGiftImageCommand(int Id, IFormFile File) : IRequest<CableGiftImageResult>;

public class UploadCableGiftImageCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IUploadFileService uploadFileService)
    : IRequestHandler<UploadCableGiftImageCommand, CableGiftImageResult>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxSizeBytes = 5 * 1024 * 1024;

    public async Task<CableGiftImageResult> Handle(UploadCableGiftImageCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        if (request.File is null)
            throw new DataValidationException("File", "Attach the image as form field 'files'");
        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new DataValidationException("File", "Only jpg, png or webp images are allowed");
        if (request.File.Length is 0 or > MaxSizeBytes)
            throw new DataValidationException("File", "Image must be between 1 byte and 5 MB");

        var gift = await db.CableGifts.FirstOrDefaultAsync(g => g.Id == request.Id && !g.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"Gift {request.Id} was not found");

        var fileName = await uploadFileService.SaveFileAsync(request.File, UploadFileFolders.CableGifts, cancellationToken);
        gift.ImageUrl = uploadFileService.GetFilePath(UploadFileFolders.CableGifts, fileName);
        await db.SaveChanges(cancellationToken);

        return new CableGiftImageResult(gift.ImageUrl);
    }
}
