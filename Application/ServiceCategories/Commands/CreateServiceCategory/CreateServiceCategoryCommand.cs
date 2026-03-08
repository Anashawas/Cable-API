namespace Application.ServiceCategories.Commands.CreateServiceCategory;

public record CreateServiceCategoryCommand(
    string Name,
    string? NameAr,
    string? Description,
    string? IconUrl,
    int SortOrder,
    bool IsActive
) : IRequest<int>;

public class CreateServiceCategoryCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<CreateServiceCategoryCommand, int>
{
    public async Task<int> Handle(CreateServiceCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = new ServiceCategory
        {
            Name = request.Name,
            NameAr = request.NameAr,
            Description = request.Description,
            IconUrl = request.IconUrl,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive
        };

        applicationDbContext.ServiceCategories.Add(category);
        await applicationDbContext.SaveChanges(cancellationToken);

        return category.Id;
    }
}
