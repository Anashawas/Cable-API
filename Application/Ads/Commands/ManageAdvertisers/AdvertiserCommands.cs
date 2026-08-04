using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ads.Commands.ManageAdvertisers;

public record AdvertiserDto(int Id, string Name, string? Contact, string? Notes, int CampaignCount, DateTime CreatedAt);

/// <summary>Admin: create an advertiser (an ad customer).</summary>
public record CreateAdvertiserCommand(string Name, string? Contact, string? Notes) : IRequest<int>;

public class CreateAdvertiserCommandValidator : AbstractValidator<CreateAdvertiserCommand>
{
    public CreateAdvertiserCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Contact).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateAdvertiserCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateAdvertiserCommand, int>
{
    public async Task<int> Handle(CreateAdvertiserCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var advertiser = new Domain.Enitites.Advertiser
        {
            Name = request.Name.Trim(),
            Contact = request.Contact,
            Notes = request.Notes
        };
        applicationDbContext.Advertisers.Add(advertiser);
        await applicationDbContext.SaveChanges(cancellationToken);
        return advertiser.Id;
    }
}

/// <summary>Admin: update an advertiser.</summary>
public record UpdateAdvertiserCommand(int Id, string Name, string? Contact, string? Notes) : IRequest;

public class UpdateAdvertiserCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateAdvertiserCommand>
{
    public async Task Handle(UpdateAdvertiserCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var advertiser = await applicationDbContext.Advertisers
                             .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsDeleted, cancellationToken)
                         ?? throw new NotFoundException($"can not find advertiser with id {request.Id}");

        advertiser.Name = request.Name.Trim();
        advertiser.Contact = request.Contact;
        advertiser.Notes = request.Notes;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

/// <summary>Admin: all advertisers with campaign counts.</summary>
public record GetAllAdvertisersRequest(int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<AdvertiserDto>>;

public class GetAllAdvertisersRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllAdvertisersRequest, PagedResult<AdvertiserDto>>
{
    public async Task<PagedResult<AdvertiserDto>> Handle(GetAllAdvertisersRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        return await applicationDbContext.Advertisers.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.Id)
            .Select(a => new AdvertiserDto(
                a.Id, a.Name, a.Contact, a.Notes,
                a.Campaigns.Count(c => !c.IsDeleted),
                a.CreatedAt))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
