namespace Domain.Enitites;

public partial class SharedLinkType
{
    public int Id { get; set; }
    public string TypeName { get; set; } = null!;
    public string? Description { get; set; }
    public string BaseUrl { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}