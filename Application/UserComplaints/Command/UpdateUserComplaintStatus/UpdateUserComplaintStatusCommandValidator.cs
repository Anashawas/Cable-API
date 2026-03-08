using Cable.Core.Emuns;
using FluentValidation;

namespace Application.UserComplaints.Command.UpdateUserComplaintStatus;

public class UpdateUserComplaintStatusCommandValidator : AbstractValidator<UpdateUserComplaintStatusCommand>
{
    public UpdateUserComplaintStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}
