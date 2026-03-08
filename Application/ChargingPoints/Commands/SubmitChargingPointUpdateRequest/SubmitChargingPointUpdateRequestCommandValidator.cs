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
}
