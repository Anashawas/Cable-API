namespace Domain.Enitites;

public partial class SystemVersion
{
    public int Id { get; set; }

    public string Platform { get; set; } = null!;
    public string Version { get; set; } = null!;
    public bool ForceUpdate { get; set; }
}