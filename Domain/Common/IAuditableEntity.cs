namespace Domain.Common;

public interface IAuditableEntity
{
    public int? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
}