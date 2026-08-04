using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// A single social-media URL belonging to either a <see cref="ServiceProvider"/>
/// or a <see cref="ChargingPoint"/>. Polymorphic via <see cref="ProviderType"/> +
/// <see cref="ProviderId"/> — mirrors the existing <c>ProviderOffer</c> pattern.
/// Multiple links may share the same platform per provider.
/// </summary>
public class SocialLink : BaseAuditableEntity
{
    /// <summary>"ServiceProvider" or "ChargingPoint".</summary>
    public string ProviderType          { get; set; } = null!;

    /// <summary>The Id of the ServiceProvider or ChargingPoint this link belongs to.</summary>
    public int    ProviderId            { get; set; }

    public int    SocialMediaPlatformId { get; set; }
    public string Url                   { get; set; } = null!;
    public int    DisplayOrder          { get; set; }

    public virtual SocialMediaPlatform SocialMediaPlatform { get; set; } = null!;
}
