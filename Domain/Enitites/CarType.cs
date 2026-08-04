namespace Domain.Enitites;

public partial class CarType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>Logo icon file name (served via IUploadFileService).</summary>
    public string? Icon { get; set; }

    public ICollection<CarModel> CarModels { get; set; } = new List<CarModel>();
}