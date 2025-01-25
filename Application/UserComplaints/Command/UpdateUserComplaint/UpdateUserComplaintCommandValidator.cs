using FluentValidation;

namespace Application.UserComplaints.Command.UpdateUserComplaint;

public class UpdateUserComplaintCommandValidator:AbstractValidator<UpdateUserComplaintCommand>
{
    private readonly IApplicationDbContext _applicationDbContext;
    public UpdateUserComplaintCommandValidator(IApplicationDbContext applicationDbContext)
    {
        _applicationDbContext = applicationDbContext;
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
    }
}