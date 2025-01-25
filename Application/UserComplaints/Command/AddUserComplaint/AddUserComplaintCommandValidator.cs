using Application.Common.Localization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Command.AddUserComplaint;

public class AddUserComplaintCommandValidator : AbstractValidator<AddUserComplaintCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;

    public AddUserComplaintCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ChargingPointId).NotEmpty().MustAsync(ChargingPointExists).WithMessage(Resources.ChargingPointMustExist);
    }
    
    private async Task<bool> ChargingPointExists(int chargingPointId, CancellationToken cancellationToken)
    {
        return await _applicationDbContext.ChargingPoints.AnyAsync(x => x.Id == chargingPointId, cancellationToken);
    }
    
}