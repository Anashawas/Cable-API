using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Ads.Commands.ManageCampaigns;

public record CampaignDto(
    int Id, int AdvertiserId, string AdvertiserName, string Type, string? CityArea,
    DateTime StartDate, DateTime? EndDate, decimal Price, int Status, DateTime CreatedAt);

/// <summary>Admin: create a sold campaign (type: banner | premium | welcome).</summary>
public record CreateCampaignCommand(
    int AdvertiserId, string Type, string? CityArea,
    DateTime StartDate, DateTime? EndDate, decimal Price, int Status = 1) : IRequest<int>;

public class CreateCampaignCommandValidator : AbstractValidator<CreateCampaignCommand>
{
    public CreateCampaignCommandValidator()
    {
        RuleFor(x => x.Type).Must(t => t is "banner" or "premium" or "welcome")
            .WithMessage("type must be banner, premium or welcome");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Status).InclusiveBetween(0, 3);
    }
}

public class CreateCampaignCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateCampaignCommand, int>
{
    public async Task<int> Handle(CreateCampaignCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        if (!await applicationDbContext.Advertisers.AnyAsync(a => a.Id == request.AdvertiserId && !a.IsDeleted, cancellationToken))
            throw new DataValidationException("AdvertiserId", $"Advertiser {request.AdvertiserId} does not exist");

        var campaign = new Domain.Enitites.Campaign
        {
            AdvertiserId = request.AdvertiserId,
            Type = request.Type,
            CityArea = request.CityArea,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Price = request.Price,
            Status = request.Status
        };
        applicationDbContext.Campaigns.Add(campaign);
        await applicationDbContext.SaveChanges(cancellationToken);
        return campaign.Id;
    }
}

/// <summary>Admin: update a campaign (window, price, status: 0 Draft / 1 Active / 2 Paused / 3 Ended).</summary>
public record UpdateCampaignCommand(
    int Id, string? CityArea, DateTime StartDate, DateTime? EndDate, decimal Price, int Status) : IRequest;

public class UpdateCampaignCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateCampaignCommand>
{
    public async Task Handle(UpdateCampaignCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var campaign = await applicationDbContext.Campaigns
                           .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException($"can not find campaign with id {request.Id}");

        campaign.CityArea = request.CityArea;
        campaign.StartDate = request.StartDate;
        campaign.EndDate = request.EndDate;
        campaign.Price = request.Price;
        campaign.Status = request.Status;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

/// <summary>Admin: all campaigns with their advertiser.</summary>
public record GetAllCampaignsRequest(int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<CampaignDto>>;

public class GetAllCampaignsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllCampaignsRequest, PagedResult<CampaignDto>>
{
    public async Task<PagedResult<CampaignDto>> Handle(GetAllCampaignsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        return await applicationDbContext.Campaigns.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.Id)
            .Select(c => new CampaignDto(
                c.Id, c.AdvertiserId, c.Advertiser.Name, c.Type, c.CityArea,
                c.StartDate, c.EndDate, c.Price, c.Status, c.CreatedAt))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
