namespace Application.Banners.Queries.GetAllBanners;

public record GetAllBannersDto(
    int Id,
    string Name,
    string Phone,
    List<BannerDurationSummery> BannerDurations,
    List<BannerAttachmentSummery> BannerAttachments);

public record BannerDurationSummery(int Id, DateOnly StartDate, DateOnly EndDate);

public record BannerAttachmentSummery(
    int Id,
    string ContentType,
    string FileName,
    long FileSize,
    string FileExtension,
    string FilePath);

public class GetAllBannersDtoMapping : Profile
{
    public GetAllBannersDtoMapping()
    {
        CreateMap<Banner, GetAllBannersDto>()
            .ForCtorParam(nameof(GetAllBannersDto.BannerDurations),
                opt => opt.MapFrom((banner, context) =>
                    context.Mapper.Map<List<BannerDurationSummery>>(banner.BannerDurations)))
            .ForCtorParam(nameof(GetAllBannersDto.BannerAttachments),
                opt => opt.MapFrom((banner, context) =>
                    context.Mapper.Map<List<BannerAttachmentSummery>>(banner.BannerAttachments)));
    }
}

public class BannerDurationSummeryMapping : Profile
{
    public BannerDurationSummeryMapping()
    {
        CreateMap<BannerDuration, BannerDurationSummery>();
        CreateMap<BannerAttachment, BannerAttachmentSummery>();
    }
}

