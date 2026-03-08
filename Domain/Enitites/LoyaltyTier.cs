namespace Domain.Enitites;

/// <summary>
/// Static reference table for loyalty tiers. No soft delete, no audit columns.
/// </summary>
public class LoyaltyTier
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int MinPoints { get; set; }
    public double Multiplier { get; set; }
    public int BonusPoints { get; set; }
    public string? IconUrl { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<UserSeasonProgress> UserSeasonProgresses { get; set; } = new List<UserSeasonProgress>();
}
